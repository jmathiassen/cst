using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Go;

/// <summary>
/// Recursive-descent parser for Go declarations. The parser commits to a declaration
/// once it sees a top-level keyword (<c>package</c>, <c>import</c>, <c>func</c>, <c>type</c>,
/// <c>var</c>, <c>const</c>) and resyncs to the next declaration on malformed input. Methods
/// with receiver types are nested under the corresponding type outline.
/// </summary>
internal sealed class GoParser
{
    private const int MaxDepth = 64;
    private const int MaxNodes = 500;

    private readonly string _text;
    private readonly LineMap _map;
    private readonly TokenReader _r;
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;
    private int _nodeCount;
    private int _depth;

    public GoParser(string text, IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _text = text;
        _map = new LineMap(text);
        _r = new TokenReader(tokens, ct);
        _diagnostics = diagnostics;
    }

    public bool IsPartial => _partial;

    public List<SyntaxNode> Parse(out List<OutlineNode> outlines)
    {
        outlines = [];
        List<SyntaxNode> children = [];
        ParseDeclarations(children, outlines);
        return children;
    }

    private void ParseDeclarations(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        _depth++;
        List<DeclEntry> topLevel = [];
        Dictionary<string, TypeDeclEntry> typeIndex = new(StringComparer.Ordinal);
        try
        {
            while (!_r.IsEof)
            {
                _r.ThrowIfCancellationRequested();
                if (!CheckCaps())
                    break;

                if (_r.Check((int)GoTokenKind.Semicolon))
                {
                    _r.Advance();
                    continue;
                }

                GoTokenKind kind = (GoTokenKind)_r.Current.Kind;
                switch (kind)
                {
                    case GoTokenKind.KwPackage:
                        ParsePackage(topLevel);
                        break;
                    case GoTokenKind.KwImport:
                        SkipImport();
                        break;
                    case GoTokenKind.KwVar:
                    case GoTokenKind.KwConst:
                        ParseVarConst(topLevel);
                        break;
                    case GoTokenKind.KwFunc:
                        ParseFunc(topLevel, typeIndex);
                        break;
                    case GoTokenKind.KwType:
                        ParseType(topLevel, typeIndex);
                        break;
                    default:
                        ResyncToDeclaration();
                        break;
                }
            }
        }
        finally
        {
            _depth--;
        }

        BuildTree(topLevel, children, outlines);
    }

    private void ParsePackage(List<DeclEntry> topLevel)
    {
        int start = _r.Current.Start;
        _r.Advance(); // skip 'package'

        if (!_r.Check((int)GoTokenKind.Identifier))
        {
            _diagnostics.Add(new ParseDiagnostic("GO001", "Expected package name.", _r.Current.Span, DiagnosticSeverity.Error));
            _partial = true;
            ResyncToDeclaration();
            return;
        }

        string name = _r.Current.Text ?? "item";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        int end = _r.Peek(-1).End;
        topLevel.Add(new SimpleDeclEntry("package_clause", "package", name, nameSpan, new TextSpan(start, Math.Max(0, end - start)), 1));
        _nodeCount++;
    }

    private void SkipImport()
    {
        _r.Advance(); // skip 'import'
        if (_r.Check((int)GoTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)GoTokenKind.LParen, (int)GoTokenKind.RParen))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("GO001", "Unclosed import block.", _r.Current.Span, DiagnosticSeverity.Error));
                ResyncToDeclaration();
            }
            return;
        }

        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            GoTokenKind kind = (GoTokenKind)_r.Current.Kind;
            if (kind == GoTokenKind.Semicolon)
            {
                _r.Advance();
                return;
            }
            if (kind is GoTokenKind.KwPackage or GoTokenKind.KwImport or GoTokenKind.KwFunc
                or GoTokenKind.KwType or GoTokenKind.KwVar or GoTokenKind.KwConst)
                return;
            _r.Advance();
        }
    }

    private void ParseVarConst(List<DeclEntry> topLevel)
    {
        bool isConst = _r.Check((int)GoTokenKind.KwConst);
        int start = _r.Current.Start;
        _r.Advance(); // skip 'var' or 'const'

        string nodeKind = isConst ? "const_declaration" : "var_declaration";
        string outlineKind = isConst ? "const" : "var";

        if (_r.Check((int)GoTokenKind.LParen))
        {
            _r.Advance();
            while (!_r.IsEof && !_r.Check((int)GoTokenKind.RParen))
            {
                _r.ThrowIfCancellationRequested();
                if (_r.Check((int)GoTokenKind.Semicolon))
                {
                    _r.Advance();
                    continue;
                }
                GoTokenKind kind = (GoTokenKind)_r.Current.Kind;
                if (kind is GoTokenKind.KwPackage or GoTokenKind.KwImport or GoTokenKind.KwFunc
                    or GoTokenKind.KwType or GoTokenKind.KwVar or GoTokenKind.KwConst)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("GO001", "Unclosed var/const block.", _r.Current.Span, DiagnosticSeverity.Error));
                    break;
                }
                ParseVarConstEntry(topLevel, nodeKind, outlineKind);
            }
            if (_r.Check((int)GoTokenKind.RParen))
                _r.Advance();
            return;
        }

        ParseVarConstEntry(topLevel, nodeKind, outlineKind);
    }

    private void ParseVarConstEntry(List<DeclEntry> topLevel, string nodeKind, string outlineKind)
    {
        if (!_r.Check((int)GoTokenKind.Identifier))
        {
            _diagnostics.Add(new ParseDiagnostic("GO001", "Expected variable name.", _r.Current.Span, DiagnosticSeverity.Error));
            _partial = true;
            ResyncToDeclaration();
            return;
        }

        int start = _r.Current.Start;
        Token first = _r.Current;
        string label = first.Text ?? "item";
        TextSpan nameSpan = first.Span;
        _r.Advance();

        while (_r.Check((int)GoTokenKind.Comma) && _r.Peek(1).Kind == (int)GoTokenKind.Identifier)
        {
            _r.Advance();
            label += ", " + _r.Peek(0).Text;
            _r.Advance();
        }

        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            GoTokenKind kind = (GoTokenKind)_r.Current.Kind;
            if (kind == GoTokenKind.Semicolon)
            {
                _r.Advance();
                break;
            }
            if (kind == GoTokenKind.LParen)
                _r.TrySkipBalanced((int)GoTokenKind.LParen, (int)GoTokenKind.RParen);
            else if (kind == GoTokenKind.LBracket)
                _r.TrySkipBalanced((int)GoTokenKind.LBracket, (int)GoTokenKind.RBracket);
            else if (kind == GoTokenKind.LBrace)
                _r.TrySkipBalanced((int)GoTokenKind.LBrace, (int)GoTokenKind.RBrace);
            else
                _r.Advance();
        }

        int end = _r.Peek(-1).End;
        topLevel.Add(new SimpleDeclEntry(nodeKind, outlineKind, label, nameSpan, new TextSpan(start, Math.Max(0, end - start)), 1));
        _nodeCount++;
    }

    private void ParseFunc(List<DeclEntry> topLevel, Dictionary<string, TypeDeclEntry> typeIndex)
    {
        int start = _r.Current.Start;
        _r.Advance(); // skip 'func'

        string? receiverType = null;
        if (_r.Check((int)GoTokenKind.LParen))
            receiverType = SkipReceiver();

        if (!_r.Check((int)GoTokenKind.Identifier))
        {
            _diagnostics.Add(new ParseDiagnostic("GO001", "Expected function name.", _r.Current.Span, DiagnosticSeverity.Error));
            _partial = true;
            ResyncToDeclaration();
            return;
        }

        string name = _r.Current.Text ?? "item";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        if (_r.Check((int)GoTokenKind.LBracket))
        {
            if (!_r.TrySkipBalanced((int)GoTokenKind.LBracket, (int)GoTokenKind.RBracket))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("GO001", "Unclosed type parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
                ResyncToDeclaration();
                return;
            }
        }

        if (!_r.Check((int)GoTokenKind.LParen))
        {
            _diagnostics.Add(new ParseDiagnostic("GO001", "Expected parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
            _partial = true;
            ResyncToDeclaration();
            return;
        }

        if (!_r.TrySkipBalanced((int)GoTokenKind.LParen, (int)GoTokenKind.RParen))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("GO001", "Unclosed parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
            ResyncToDeclaration();
            return;
        }

        // Skip optional result type and body.
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            GoTokenKind kind = (GoTokenKind)_r.Current.Kind;
            if (kind == GoTokenKind.LBrace)
            {
                if (!_r.TrySkipBalanced((int)GoTokenKind.LBrace, (int)GoTokenKind.RBrace))
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("GO001", "Unclosed function body.", _r.Current.Span, DiagnosticSeverity.Error));
                }
                break;
            }
            if (kind == GoTokenKind.Semicolon)
            {
                _r.Advance();
                break;
            }
            if (kind == GoTokenKind.LParen)
                _r.TrySkipBalanced((int)GoTokenKind.LParen, (int)GoTokenKind.RParen);
            else if (kind == GoTokenKind.LBracket)
                _r.TrySkipBalanced((int)GoTokenKind.LBracket, (int)GoTokenKind.RBracket);
            else
                _r.Advance();
        }

        int end = _r.Peek(-1).End;
        TextSpan span = new(start, Math.Max(0, end - start));

        if (receiverType is not null)
        {
            if (!typeIndex.TryGetValue(receiverType, out TypeDeclEntry? typeEntry))
            {
                typeEntry = new TypeDeclEntry(receiverType, nameSpan, span, isStub: true);
                typeIndex[receiverType] = typeEntry;
                topLevel.Add(typeEntry);
            }
            typeEntry.AddMethod(new MethodDeclEntry(name, nameSpan, span, 2, receiverType));
        }
        else
        {
            topLevel.Add(new SimpleDeclEntry("function_declaration", "func", name, nameSpan, span, 1));
        }
        _nodeCount++;
    }

    /// <summary>
    /// Skips a receiver clause <c>(t *T[K])</c> and returns the receiver type name.
    /// The receiver type is the identifier after the optional pointer <c>*</c>; type
    /// parameters are ignored so <c>(s *Stack[T])</c> returns <c>Stack</c>. If the
    /// receiver is malformed, scanning stops at the next declaration boundary or semicolon.
    /// </summary>
    private string? SkipReceiver()
    {
        _r.Advance(); // skip '('
        int depth = 1;
        string? typeName = null;

        // Skip the required receiver variable name.
        if (_r.Check((int)GoTokenKind.Identifier))
        {
            _r.Advance();

            // Skip optional pointer marker.
            if (_r.Check((int)GoTokenKind.Star))
                _r.Advance();

            // The type name follows immediately.
            if (_r.Check((int)GoTokenKind.Identifier))
            {
                typeName = _r.Current.Text;
                _r.Advance();
            }
        }

        // Skip the rest of the receiver until the matching ')'.
        while (!_r.IsEof && depth > 0)
        {
            _r.ThrowIfCancellationRequested();
            GoTokenKind kind = (GoTokenKind)_r.Current.Kind;
            if (kind == GoTokenKind.LParen)
                depth++;
            else if (kind == GoTokenKind.RParen)
            {
                depth--;
                _r.Advance();
                if (depth == 0)
                    break;
                continue;
            }
            else if (kind == GoTokenKind.Semicolon || kind is GoTokenKind.KwPackage or GoTokenKind.KwImport
                or GoTokenKind.KwFunc or GoTokenKind.KwType or GoTokenKind.KwVar or GoTokenKind.KwConst)
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("GO001", "Unclosed receiver clause.", _r.Current.Span, DiagnosticSeverity.Error));
                break;
            }
            _r.Advance();
        }

        return typeName;
    }

    private void ParseType(List<DeclEntry> topLevel, Dictionary<string, TypeDeclEntry> typeIndex)
    {
        int start = _r.Current.Start;
        _r.Advance(); // skip 'type'

        if (_r.Check((int)GoTokenKind.LParen))
        {
            _r.Advance();
            while (!_r.IsEof && !_r.Check((int)GoTokenKind.RParen))
            {
                _r.ThrowIfCancellationRequested();
                if (_r.Check((int)GoTokenKind.Semicolon))
                {
                    _r.Advance();
                    continue;
                }
                GoTokenKind kind = (GoTokenKind)_r.Current.Kind;
                if (kind is GoTokenKind.KwPackage or GoTokenKind.KwImport or GoTokenKind.KwFunc
                    or GoTokenKind.KwType or GoTokenKind.KwVar or GoTokenKind.KwConst)
                {
                    // Malformed block: a top-level keyword cannot appear inside 'type ( ... )'.
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("GO001", "Unclosed type block.", _r.Current.Span, DiagnosticSeverity.Error));
                    break;
                }
                ParseTypeEntry(topLevel, typeIndex, start);
            }
            if (_r.Check((int)GoTokenKind.RParen))
                _r.Advance();
            return;
        }

        ParseTypeEntry(topLevel, typeIndex, start);
    }

    private void ParseTypeEntry(List<DeclEntry> topLevel, Dictionary<string, TypeDeclEntry> typeIndex, int declStart)
    {
        if (!_r.Check((int)GoTokenKind.Identifier))
        {
            _diagnostics.Add(new ParseDiagnostic("GO001", "Expected type name.", _r.Current.Span, DiagnosticSeverity.Error));
            _partial = true;
            ResyncToDeclaration();
            return;
        }

        int start = _r.Current.Start;
        string name = _r.Current.Text ?? "item";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        if (_r.Check((int)GoTokenKind.LBracket))
            _r.TrySkipBalanced((int)GoTokenKind.LBracket, (int)GoTokenKind.RBracket);

        SyntaxNode? bodyChild = null;
        if (_r.Check((int)GoTokenKind.KwStruct) || _r.Check((int)GoTokenKind.KwInterface))
        {
            bool isStruct = _r.Check((int)GoTokenKind.KwStruct);
            int bodyStart = _r.Current.Start;
            _r.Advance();
            if (_r.Check((int)GoTokenKind.LBrace))
            {
                if (!_r.TrySkipBalanced((int)GoTokenKind.LBrace, (int)GoTokenKind.RBrace))
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("GO001", $"Unclosed {(isStruct ? "struct" : "interface")} body.", _r.Current.Span, DiagnosticSeverity.Error));
                }
                int bodyEnd = _r.Peek(-1).End;
                bodyChild = new SyntaxNode(isStruct ? "struct_type" : "interface_type",
                    new TextSpan(bodyStart, Math.Max(0, bodyEnd - bodyStart)), name);
                _nodeCount++;
            }
        }
        else
        {
            while (!_r.IsEof)
            {
                _r.ThrowIfCancellationRequested();
                GoTokenKind kind = (GoTokenKind)_r.Current.Kind;
                if (kind == GoTokenKind.Semicolon || kind == GoTokenKind.RParen)
                    break;
                if (kind == GoTokenKind.LParen)
                    _r.TrySkipBalanced((int)GoTokenKind.LParen, (int)GoTokenKind.RParen);
                else if (kind == GoTokenKind.LBracket)
                    _r.TrySkipBalanced((int)GoTokenKind.LBracket, (int)GoTokenKind.RBracket);
                else if (kind == GoTokenKind.LBrace)
                    _r.TrySkipBalanced((int)GoTokenKind.LBrace, (int)GoTokenKind.RBrace);
                else
                    _r.Advance();
            }
            if (_r.Check((int)GoTokenKind.Semicolon))
                _r.Advance();
        }

        int end = _r.Peek(-1).End;
        TextSpan span = new(start, Math.Max(0, end - start));

        if (!typeIndex.TryGetValue(name, out TypeDeclEntry? typeEntry))
        {
            typeEntry = new TypeDeclEntry(name, nameSpan, span);
            typeIndex[name] = typeEntry;
            topLevel.Add(typeEntry);
        }
        else
        {
            typeEntry.NameSpan = nameSpan;
            typeEntry.Span = span;
            typeEntry.IsStub = false;
        }
        typeEntry.BodyChild = bodyChild;
    }

    /// <summary>
    /// Skips to the next top-level declaration boundary. Used after a malformed declaration
    /// so the parser does not get stuck and never throws.
    /// </summary>
    private void ResyncToDeclaration()
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            GoTokenKind kind = (GoTokenKind)_r.Current.Kind;
            if (kind == GoTokenKind.Semicolon)
            {
                _r.Advance();
                return;
            }
            if (kind is GoTokenKind.KwPackage or GoTokenKind.KwImport or GoTokenKind.KwFunc
                or GoTokenKind.KwType or GoTokenKind.KwVar or GoTokenKind.KwConst)
                return;
            if (kind == GoTokenKind.LBrace)
            {
                if (!_r.TrySkipBalanced((int)GoTokenKind.LBrace, (int)GoTokenKind.RBrace))
                    _partial = true;
            }
            else if (kind == GoTokenKind.LParen)
            {
                _r.TrySkipBalanced((int)GoTokenKind.LParen, (int)GoTokenKind.RParen);
            }
            else if (kind == GoTokenKind.LBracket)
            {
                _r.TrySkipBalanced((int)GoTokenKind.LBracket, (int)GoTokenKind.RBracket);
            }
            else
            {
                _r.Advance();
            }
        }
    }

    private void BuildTree(List<DeclEntry> topLevel, List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        foreach (DeclEntry entry in topLevel)
        {
            if (entry is TypeDeclEntry typeEntry)
            {
                List<SyntaxNode> typeChildren = [];
                List<OutlineNode> typeOutlineChildren = [];
                if (typeEntry.BodyChild is not null)
                    typeChildren.Add(typeEntry.BodyChild);
                foreach (SimpleDeclEntry method in typeEntry.Methods)
                {
                Dictionary<string, string>? methodProps = null;
                if (method is MethodDeclEntry methodEntry)
                    methodProps = new Dictionary<string, string> { ["receiver"] = methodEntry.ReceiverType };
                typeChildren.Add(new SyntaxNode(method.NodeKind, method.Span, method.Name, method.NameSpan, properties: methodProps));
                typeOutlineChildren.Add(MakeOutline(method.OutlineKind, method.Name, method.Span, method.Level));

                }
                children.Add(new SyntaxNode("type_declaration", typeEntry.Span, typeEntry.Name, typeEntry.NameSpan, typeChildren));
                outlines.Add(MakeOutline("type", typeEntry.Name, typeEntry.Span, 1, typeOutlineChildren));
            }
            else
            {
                SimpleDeclEntry simple = (SimpleDeclEntry)entry;
                children.Add(new SyntaxNode(simple.NodeKind, simple.Span, simple.Name, simple.NameSpan));
                outlines.Add(MakeOutline(simple.OutlineKind, simple.Name, simple.Span, simple.Level));
            }
        }
    }

    private bool CheckCaps()
    {
        if (_nodeCount >= MaxNodes)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("GO006", $"Outline node count exceeds GoOutlineMaxNodes ({MaxNodes}).", new TextSpan(_r.Current.Start, 0), DiagnosticSeverity.Warning));
            return false;
        }
        if (_depth >= MaxDepth)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("GO005", $"Nesting depth exceeds GoMaxDepth ({MaxDepth}).", new TextSpan(_r.Current.Start, 0), DiagnosticSeverity.Warning));
            return false;
        }
        return true;
    }

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level, List<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }

    private abstract class DeclEntry
    {
        public string NodeKind { get; }
        public string OutlineKind { get; }
        public string Name { get; }
        public TextSpan NameSpan { get; set; }
        public TextSpan Span { get; set; }
        public int Level { get; }

        protected DeclEntry(string nodeKind, string outlineKind, string name, TextSpan nameSpan, TextSpan span, int level)
        {
            NodeKind = nodeKind;
            OutlineKind = outlineKind;
            Name = name;
            NameSpan = nameSpan;
            Span = span;
            Level = level;
        }
    }

    private class SimpleDeclEntry : DeclEntry
    {
        public SimpleDeclEntry(string nodeKind, string outlineKind, string name, TextSpan nameSpan, TextSpan span, int level)
            : base(nodeKind, outlineKind, name, nameSpan, span, level)
        {
        }
    }

    private sealed class MethodDeclEntry : SimpleDeclEntry
    {
        public string ReceiverType { get; }

        public MethodDeclEntry(string name, TextSpan nameSpan, TextSpan span, int level, string receiverType)
            : base("method_declaration", "method", name, nameSpan, span, level)
        {
            ReceiverType = receiverType;
        }
    }

    private sealed class TypeDeclEntry : DeclEntry
    {
        public List<SimpleDeclEntry> Methods { get; } = [];
        public SyntaxNode? BodyChild { get; set; }
        public bool IsStub { get; set; }

        public TypeDeclEntry(string name, TextSpan nameSpan, TextSpan span, bool isStub = false)
            : base("type_declaration", "type", name, nameSpan, span, 1)
        {
            IsStub = isStub;
        }

        public void AddMethod(SimpleDeclEntry method)
        {
            Methods.Add(method);
        }
    }
}
