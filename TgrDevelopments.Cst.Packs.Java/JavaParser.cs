using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Java;

internal sealed class JavaParser
{
    private const int MaxDepth = 64;
    private const int MaxOutlineNodes = 500;

    private readonly string _text;
    private readonly TokenReader _r;
    private readonly LineMap _map;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly CancellationToken _ct;
    private int _depth;
    private int _nodeCount;
    private bool _partial;
    private bool _cancelled;

    public JavaParser(string text, IReadOnlyList<Token> tokens, LineMap map, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _text = text;
        _r = new TokenReader(tokens, ct);
        _map = map;
        _diagnostics = diagnostics;
        _ct = ct;
    }

    public bool IsPartial => _partial;
    public bool IsCancelled => _cancelled;

    public List<SyntaxNode> Parse(out List<OutlineNode> outlines)
    {
        List<SyntaxNode> children = [];
        outlines = [];
        if (_r.Count <= 1)
            return children;
        try
        {
            TryParsePackage(children, outlines);
            SkipImports();
            ParseTypeItems(children, outlines, 1, false);
        }
        catch (OperationCanceledException)
        {
            _cancelled = true;
        }
        return children;
    }

    private void TryParsePackage(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        if (!_r.Check((int)JavaTokenKind.KwPackage))
            return;
        int start = _r.Current.Start;
        _r.Advance();
        string? name = ScanDottedName();
        if (name is null)
            return;
        TextSpan nameSpan = _r.Peek(-1).Span;
        if (_r.Check((int)JavaTokenKind.Semicolon))
            _r.Advance();
        TextSpan span = new(start, _r.Peek(-1).End - start);
        children.Add(new SyntaxNode("package_decl", span, name, nameSpan));
        outlines.Add(MakeOutline("package", name, span, 1));
        _nodeCount++;
    }

    private void SkipImports()
    {
        while (_r.Check((int)JavaTokenKind.KwImport))
        {
            _r.Advance();
            SkipToSemicolon();
        }
    }

    private void ParseTypeItems(List<SyntaxNode> parentChildren, List<OutlineNode> parentOutlines, int baseLevel, bool insideType)
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();

            if (_r.Check((int)JavaTokenKind.RBrace))
                return;
            if (_r.Check((int)JavaTokenKind.Semicolon))
            {
                _r.Advance();
                continue;
            }

            SkipModifiersAndAnnotations();

            if (_r.Check((int)JavaTokenKind.KwClass) || _r.Check((int)JavaTokenKind.KwInterface)
                || _r.Check((int)JavaTokenKind.KwEnum) || _r.Check((int)JavaTokenKind.KwRecord))
            {
                TryParseType(parentChildren, parentOutlines, baseLevel, insideType);
                continue;
            }

            if (_r.Check((int)JavaTokenKind.LBrace))
            {
                _r.TrySkipBalanced((int)JavaTokenKind.LBrace, (int)JavaTokenKind.RBrace);
                continue;
            }

            TryParseMember(parentChildren, parentOutlines, baseLevel);
        }
    }

    private void SkipModifiersAndAnnotations()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)JavaTokenKind.KwPublic) || _r.Check((int)JavaTokenKind.KwPrivate)
                || _r.Check((int)JavaTokenKind.KwProtected) || _r.Check((int)JavaTokenKind.KwStatic)
                || _r.Check((int)JavaTokenKind.KwAbstract) || _r.Check((int)JavaTokenKind.KwFinal)
                || _r.Check((int)JavaTokenKind.KwNative) || _r.Check((int)JavaTokenKind.KwSynchronized)
                || _r.Check((int)JavaTokenKind.KwVolatile) || _r.Check((int)JavaTokenKind.KwTransient)
                || _r.Check((int)JavaTokenKind.KwStrictFp) || _r.Check((int)JavaTokenKind.KwSealed)
                || _r.Check((int)JavaTokenKind.KwDefault))
            {
                _r.Advance();
                continue;
            }
            if (_r.Check((int)JavaTokenKind.At))
            {
                SkipAnnotation();
                continue;
            }
            break;
        }
    }

    private void SkipAnnotation()
    {
        _r.Advance();
        if (_r.Check((int)JavaTokenKind.Identifier))
        {
            _r.Advance();
            if (_r.Check((int)JavaTokenKind.Period))
                ScanDottedName();
        }
        if (_r.Check((int)JavaTokenKind.LParen))
            _r.TrySkipBalanced((int)JavaTokenKind.LParen, (int)JavaTokenKind.RParen);
    }

    private bool TryParseType(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, bool insideType)
    {
        int start = _r.Current.Start;
        JavaTokenKind tkind = (JavaTokenKind)_r.Current.Kind;
        _r.Advance();

        if (_r.Check((int)JavaTokenKind.At))
            SkipAnnotation();

        if (!_r.Check((int)JavaTokenKind.Identifier))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        string name = _r.Current.Text ?? "type";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        if (_r.Check((int)JavaTokenKind.LAngle))
            TrySkipGenerics();

        if (_r.Check((int)JavaTokenKind.KwExtends))
        {
            _r.Advance();
            SkipType();
        }

        if (_r.Check((int)JavaTokenKind.KwImplements))
        {
            _r.Advance();
            SkipTypeList();
        }

        if (_r.Check((int)JavaTokenKind.KwPermits))
        {
            _r.Advance();
            SkipTypeList();
        }

        if (_r.Check((int)JavaTokenKind.KwSealed) || _r.Check((int)JavaTokenKind.KwDefault))
            _r.Advance();

        if (!_r.Check((int)JavaTokenKind.LBrace))
        {
            SkipToSemicolonOrBrace();
            int e1 = _r.Peek(-1).End;
            CheckCaps(start, e1);
            TextSpan s1 = new(start, e1 - start);
            string ok1 = KindString(tkind);
            children.Add(new SyntaxNode(ok1 + "_declaration", s1, name, nameSpan));
            outlines.Add(MakeOutline(ok1, name, s1, baseLevel));
            _nodeCount++;
            return true;
        }

        if (baseLevel + 1 > MaxDepth)
        {
            _diagnostics.Add(new ParseDiagnostic("JAVA005", "Max depth exceeded.", new TextSpan(start, _r.Current.End - start), DiagnosticSeverity.Warning));
            _r.TrySkipBalanced((int)JavaTokenKind.LBrace, (int)JavaTokenKind.RBrace);
            int e2 = _r.Peek(-1).End;
            TextSpan s2 = new(start, e2 - start);
            string ok2 = KindString(tkind);
            children.Add(new SyntaxNode(ok2 + "_declaration", s2, name, nameSpan));
            outlines.Add(MakeOutline(ok2, name, s2, baseLevel));
            _nodeCount++;
            return true;
        }

        _depth++;
        int bodyOpen = _r.Current.Start;
        _r.Advance();

        List<SyntaxNode> innerChildren = [];
        List<OutlineNode> innerOutlines = [];
        if (_nodeCount < MaxOutlineNodes)
            ParseTypeItems(innerChildren, innerOutlines, baseLevel + 1, true);

        int bodyEnd;
        if (_r.Check((int)JavaTokenKind.RBrace))
        {
            bodyEnd = _r.Current.End;
            _r.Advance();
        }
        else
        {
            bodyEnd = _r.Peek(-1).End;
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("JAVA001", "Unclosed type body.", new TextSpan(bodyOpen, 0), DiagnosticSeverity.Error));
        }

        string ok = KindString(tkind);
        TextSpan span = new(start, bodyEnd - start);
        children.Add(new SyntaxNode(ok + "_declaration", span, name, nameSpan, innerChildren.Count > 0 ? innerChildren : null));
        outlines.Add(MakeOutline(ok, name, span, baseLevel, innerOutlines.Count > 0 ? innerOutlines : null));
        _nodeCount++;
        _depth--;
        return true;
    }

    private void TryParseMember(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        string? lastName = null;
        TextSpan lastNameSpan = default;

        while (!_r.IsEof)
        {
            if (_r.Check((int)JavaTokenKind.RBrace))
                return;
            if (_r.Check((int)JavaTokenKind.LParen))
            {
                if (lastName is null)
                {
                    _r.TrySkipBalanced((int)JavaTokenKind.LParen, (int)JavaTokenKind.RParen);
                    SkipToSemicolonOrBrace();
                    return;
                }
                ParseMethodOrConstructor(children, outlines, lastName, lastNameSpan, start, baseLevel);
                return;
            }
            if (_r.Check((int)JavaTokenKind.Semicolon))
            {
                _r.Advance();
                return;
            }
            if (_r.Check((int)JavaTokenKind.LBrace))
            {
                _r.TrySkipBalanced((int)JavaTokenKind.LBrace, (int)JavaTokenKind.RBrace);
                return;
            }
            if (_r.Check((int)JavaTokenKind.LAngle))
            {
                TrySkipGenerics();
                continue;
            }
            if (_r.Check((int)JavaTokenKind.KwThrows))
            {
                _r.Advance();
                SkipTypeList();
                continue;
            }
            if (_r.Check((int)JavaTokenKind.Identifier) || _r.Check((int)JavaTokenKind.KwVoid))
            {
                lastName = _r.Current.Text ?? "member";
                lastNameSpan = _r.Current.Span;
                _r.Advance();
                continue;
            }
            if (_r.Check((int)JavaTokenKind.LBracket))
            {
                _r.TrySkipBalanced((int)JavaTokenKind.LBracket, (int)JavaTokenKind.RBracket);
                continue;
            }
            if (_r.Check((int)JavaTokenKind.DotDotDot))
            {
                _r.Advance();
                continue;
            }
            _r.Advance();
        }
    }

    private void ParseMethodOrConstructor(List<SyntaxNode> children, List<OutlineNode> outlines, string name, TextSpan nameSpan, int start, int baseLevel)
    {
        _r.TrySkipBalanced((int)JavaTokenKind.LParen, (int)JavaTokenKind.RParen);

        if (_r.Check((int)JavaTokenKind.KwThrows))
        {
            _r.Advance();
            SkipTypeList();
        }

        if (_r.Check((int)JavaTokenKind.LBrace))
        {
            if (!_r.TrySkipBalanced((int)JavaTokenKind.LBrace, (int)JavaTokenKind.RBrace))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("JAVA001", "Unclosed method body.", _r.Current.Span, DiagnosticSeverity.Error));
            }
        }
        else
        {
            SkipToSemicolonOrBrace();
        }

        int end = _r.Peek(-1).End;
        CheckCaps(start, end);

        TextSpan span = new(start, end - start);
        children.Add(new SyntaxNode("method_declaration", span, name, nameSpan));
        outlines.Add(MakeOutline("method", name, span, baseLevel));
        _nodeCount++;
    }

    private void SkipType()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)JavaTokenKind.Semicolon) || _r.Check((int)JavaTokenKind.LBrace)
                || _r.Check((int)JavaTokenKind.RBrace) || _r.Check((int)JavaTokenKind.KwImplements)
                || _r.Check((int)JavaTokenKind.KwExtends))
                return;
            if (_r.Check((int)JavaTokenKind.LAngle))
            {
                TrySkipGenerics();
                continue;
            }
            if (_r.Check((int)JavaTokenKind.LBracket))
            {
                _r.TrySkipBalanced((int)JavaTokenKind.LBracket, (int)JavaTokenKind.RBracket);
                continue;
            }
            if (_r.Check((int)JavaTokenKind.DotDotDot))
            {
                _r.Advance();
                continue;
            }
            _r.Advance();
        }
    }

    private void SkipTypeList()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)JavaTokenKind.Semicolon) || _r.Check((int)JavaTokenKind.LBrace)
                || _r.Check((int)JavaTokenKind.RBrace))
                return;
            if (_r.Check((int)JavaTokenKind.Comma))
            {
                _r.Advance();
                continue;
            }
            if (_r.Check((int)JavaTokenKind.LAngle))
            {
                TrySkipGenerics();
                continue;
            }
            _r.Advance();
        }
    }

    private bool TrySkipGenerics()
    {
        if (!_r.TrySkipBalanced((int)JavaTokenKind.LAngle, (int)JavaTokenKind.RAngle))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("JAVA001", "Unclosed generics.", _r.Current.Span, DiagnosticSeverity.Error));
            return false;
        }
        return true;
    }

    private string? ScanDottedName()
    {
        if (!_r.Check((int)JavaTokenKind.Identifier))
            return null;
        string result = _r.Current.Text ?? "";
        _r.Advance();
        while (_r.Check((int)JavaTokenKind.Period))
        {
            _r.Advance();
            if (!_r.Check((int)JavaTokenKind.Identifier))
                break;
            result += "." + (_r.Current.Text ?? "");
            _r.Advance();
        }
        return result;
    }

    private void SkipToSemicolon()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)JavaTokenKind.Semicolon))
            {
                _r.Advance();
                return;
            }
            if (_r.Check((int)JavaTokenKind.LBrace) || _r.Check((int)JavaTokenKind.RBrace))
                return;
            if (_r.Check((int)JavaTokenKind.LParen))
                _r.TrySkipBalanced((int)JavaTokenKind.LParen, (int)JavaTokenKind.RParen);
            else if (_r.Check((int)JavaTokenKind.LBracket))
                _r.TrySkipBalanced((int)JavaTokenKind.LBracket, (int)JavaTokenKind.RBracket);
            else if (_r.Check((int)JavaTokenKind.LAngle))
                TrySkipGenerics();
            else
                _r.Advance();
        }
    }

    private void SkipToSemicolonOrBrace()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)JavaTokenKind.Semicolon))
            {
                _r.Advance();
                return;
            }
            if (_r.Check((int)JavaTokenKind.LBrace) || _r.Check((int)JavaTokenKind.RBrace))
                return;
            if (_r.Check((int)JavaTokenKind.LParen))
                _r.TrySkipBalanced((int)JavaTokenKind.LParen, (int)JavaTokenKind.RParen);
            else if (_r.Check((int)JavaTokenKind.LBracket))
                _r.TrySkipBalanced((int)JavaTokenKind.LBracket, (int)JavaTokenKind.RBracket);
            else if (_r.Check((int)JavaTokenKind.LAngle))
                TrySkipGenerics();
            else
                _r.Advance();
        }
    }

    private bool SkipTrash()
    {
        while (!_r.IsEof)
        {
            JavaTokenKind k = (JavaTokenKind)_r.Current.Kind;
            if (k is JavaTokenKind.KwClass or JavaTokenKind.KwInterface or JavaTokenKind.KwEnum
                or JavaTokenKind.KwRecord or JavaTokenKind.KwPublic or JavaTokenKind.KwPrivate
                or JavaTokenKind.KwProtected or JavaTokenKind.KwStatic or JavaTokenKind.KwAbstract
                or JavaTokenKind.KwFinal or JavaTokenKind.At or JavaTokenKind.Identifier
                or JavaTokenKind.LBrace or JavaTokenKind.RBrace or JavaTokenKind.Semicolon
                or JavaTokenKind.KwVoid)
                return true;
            _r.Advance();
        }
        return false;
    }

    private void CheckCaps(int start, int end)
    {
        if (_depth >= MaxDepth)
            _diagnostics.Add(new ParseDiagnostic("JAVA005", "Max depth exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
        if (_nodeCount >= MaxOutlineNodes)
            _diagnostics.Add(new ParseDiagnostic("JAVA006", "Max outline nodes exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
    }

    private static string KindString(JavaTokenKind kind) => kind switch
    {
        JavaTokenKind.KwClass => "class",
        JavaTokenKind.KwInterface => "interface",
        JavaTokenKind.KwEnum => "enum",
        JavaTokenKind.KwRecord => "record",
        _ => "class",
    };

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }
}
