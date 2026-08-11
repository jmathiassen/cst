using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Cpp;

/// <summary>
/// Recursive-descent parser for C++ declarations: namespaces (nested), class/struct/union/enum
/// with member methods, free functions, templates, operator overloads, constructors/destructors,
/// out-of-line qualified methods. Recovery is best-effort — never throws.
/// </summary>
internal sealed class CppParser
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

    public CppParser(string text, IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
        ParseDeclarations(children, outlines, level: 1, className: null);
        return children;
    }

    private void ParseDeclarations(List<SyntaxNode> children, List<OutlineNode> outlines, int level, string? className)
    {
        _depth++;
        try
        {
            while (!_r.IsEof && !_r.Check((int)CppTokenKind.RBrace))
            {
                _r.ThrowIfCancellationRequested();
                if (!CheckCaps())
                    break;

                int before = _r.Index;
                if (_r.Check((int)CppTokenKind.Semicolon))
                {
                    _r.Advance();
                    continue;
                }
                if (_r.Current.Kind is (int)CppTokenKind.KwPublic or (int)CppTokenKind.KwPrivate or (int)CppTokenKind.KwProtected
                    && _r.Peek(1).Kind == (int)CppTokenKind.Colon)
                {
                    _r.Advance();
                    _r.Advance();
                    continue;
                }
                if (_r.Check((int)CppTokenKind.KwTemplate))
                {
                    if (!SkipTemplatePrefix())
                        continue;
                }
                if (_r.Check((int)CppTokenKind.KwFriend)
                    && _r.Peek(1).Kind is (int)CppTokenKind.KwClass or (int)CppTokenKind.KwStruct
                        or (int)CppTokenKind.KwUnion or (int)CppTokenKind.KwEnum)
                {
                    SkipTrash();
                    continue;
                }
                if (TryParseNamespace(children, outlines, level))
                    continue;
                if (TryParseExternBlock(children, outlines, level))
                    continue;
                if (TryParseClassLike(children, outlines, level))
                    continue;
                if (TryParseFunction(children, outlines, level, className))
                    continue;
                SkipTrash();
                if (_r.Index == before)
                    _r.Advance();
            }
        }
        finally
        {
            _depth--;
        }
    }

    private bool TryParseNamespace(List<SyntaxNode> children, List<OutlineNode> outlines, int level)
    {
        int save = _r.Index;
        if (_r.Check((int)CppTokenKind.KwInline) || _r.Check((int)CppTokenKind.KwExport))
        {
            if (_r.Peek(1).Kind != (int)CppTokenKind.KwNamespace)
                return false;
            _r.Advance();
        }
        if (!_r.Check((int)CppTokenKind.KwNamespace))
            return false;
        int start = _r.Current.Start;
        _r.Advance();

        string? name = null;
        TextSpan nameSpan = default;
        if (_r.Check((int)CppTokenKind.Identifier))
        {
            name = _r.Current.Text ?? "item";
            nameSpan = _r.Current.Span;
            _r.Advance();
            while (_r.Check((int)CppTokenKind.ColonColon) && _r.Peek(1).Kind == (int)CppTokenKind.Identifier)
            {
                name += "::" + _r.Peek(1).Text;
                _r.Advance();
                _r.Advance();
            }
        }
        if (_r.Check((int)CppTokenKind.Colon))
        {
            SkipToSemicolon();
            return true;
        }
        if (!_r.Check((int)CppTokenKind.LBrace))
        {
            _r.Seek(save);
            return false;
        }
        _r.Advance();

        List<SyntaxNode> inner = [];
        List<OutlineNode> innerOutlines = [];
        ParseDeclarations(inner, innerOutlines, level + 1, className: null);
        int end;
        if (_r.Check((int)CppTokenKind.RBrace))
        {
            end = _r.Current.End;
            _r.Advance();
        }
        else
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("CPP001", "Unclosed namespace body.", new TextSpan(start, 0), DiagnosticSeverity.Error));
            end = _text.Length;
        }

        string label = name ?? "(anonymous)";
        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("namespace_definition", span, label, nameSpan, inner));
        outlines.Add(MakeOutline("namespace", label, span, level, innerOutlines));
        _nodeCount++;
        return true;
    }

    private bool TryParseExternBlock(List<SyntaxNode> children, List<OutlineNode> outlines, int level)
    {
        int save = _r.Index;
        if (!_r.Check((int)CppTokenKind.KwExtern))
            return false;
        if (_r.Peek(1).Kind != (int)CppTokenKind.StringLiteral)
            return false;
        _r.Advance();
        _r.Advance();
        if (!_r.Check((int)CppTokenKind.LBrace))
        {
            _r.Seek(save);
            return false;
        }
        _r.Advance();

        ParseDeclarations(children, outlines, level, className: null);
        if (_r.Check((int)CppTokenKind.RBrace))
            _r.Advance();
        else
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("CPP001", "Unclosed extern block.", new TextSpan(_r.Peek(-1).End, 0), DiagnosticSeverity.Error));
        }
        return true;
    }

    private bool TryParseClassLike(List<SyntaxNode> children, List<OutlineNode> outlines, int level)
    {
        int save = _r.Index;
        CppTokenKind startKind = (CppTokenKind)_r.Current.Kind;
        if (startKind is not (CppTokenKind.KwClass or CppTokenKind.KwStruct or CppTokenKind.KwUnion or CppTokenKind.KwEnum))
            return false;
        string outlineKind = startKind switch
        {
            CppTokenKind.KwClass => "class",
            CppTokenKind.KwStruct => "struct",
            CppTokenKind.KwUnion => "union",
            _ => "enum",
        };
        _r.Advance();
        if (outlineKind == "enum" && _r.Check((int)CppTokenKind.KwClass))
            _r.Advance();

        int start = _r.Peek(-1).Start;
        string? name = null;
        TextSpan nameSpan = default;
        if (_r.Check((int)CppTokenKind.Identifier))
        {
            name = _r.Current.Text ?? "item";
            nameSpan = _r.Current.Span;
            _r.Advance();
        }
        while (_r.Check((int)CppTokenKind.KwFinal))
            _r.Advance();
        if (_r.Check((int)CppTokenKind.Colon))
        {
            while (!_r.IsEof && !_r.Check((int)CppTokenKind.LBrace) && !_r.Check((int)CppTokenKind.Semicolon))            {
                if (_r.Check((int)CppTokenKind.LAngle))
                    _r.TrySkipBalanced((int)CppTokenKind.LAngle, (int)CppTokenKind.RAngle);
                else
                    _r.Advance();
            }
        }
        if (!_r.Check((int)CppTokenKind.LBrace))
        {
            _r.Seek(save);
            return false;
        }
        _r.Advance();

        List<SyntaxNode> memberNodes = [];
        List<OutlineNode> memberOutlines = [];
        ParseDeclarations(memberNodes, memberOutlines, level + 1, name);
        int end;
        if (_r.Check((int)CppTokenKind.RBrace))
        {
            end = _r.Current.End;
            _r.Advance();
            if (_r.Check((int)CppTokenKind.Semicolon))
                _r.Advance();
            else
                SkipToSemicolon();
        }
        else
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("CPP001", $"Unclosed {outlineKind} body.", new TextSpan(start, 0), DiagnosticSeverity.Error));
            end = _text.Length;
        }

        string label = name ?? "(anonymous)";
        string nodeKind = outlineKind switch
        {
            "class" => "class_specifier",
            "struct" => "struct_specifier",
            "union" => "union_specifier",
            _ => "enum_specifier",
        };
        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode(nodeKind, span, label, nameSpan, memberNodes));
        outlines.Add(MakeOutline(outlineKind, label, span, level, memberOutlines));
        _nodeCount++;
        return true;
    }

    private bool TryParseFunction(List<SyntaxNode> children, List<OutlineNode> outlines, int level, string? className)
    {
        int save = _r.Index;
        int start = _r.Current.Start;

        while (_r.Check((int)CppTokenKind.LBracket) && _r.Peek(1).Kind == (int)CppTokenKind.LBracket)
            _r.TrySkipBalanced((int)CppTokenKind.LBracket, (int)CppTokenKind.RBracket);

        string? name = null;
        TextSpan nameSpan = default;
        CppTokenKind lastSignificant = CppTokenKind.Other;
        bool qualified = false;
        string lastIdentifierText = "";
        int lastIdentifierStart = -1;
        bool isDestructor = false;
        bool isOperator = false;
        bool sawSpecifier = false;

        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            CppTokenKind kind = (CppTokenKind)_r.Current.Kind;

            if (kind == CppTokenKind.KwOperator)
            {
                isOperator = true;
                sawSpecifier = true;
                _r.Advance();
                int opStart = _r.Current.Start;
                while (!_r.IsEof && !_r.Check((int)CppTokenKind.LParen))
                {
                    if (_r.Check((int)CppTokenKind.Semicolon))
                    {
                        _r.Seek(save);
                        return false;
                    }
                    _r.Advance();
                }
                int opEnd = _r.Peek(-1).End;
                string symbol = opEnd > opStart ? _text.Substring(opStart, opEnd - opStart) : "";
                name = symbol.Length > 0 ? "operator" + symbol : "operator()";
                nameSpan = new TextSpan(start, Math.Max(0, opEnd - start));
                break;
            }
            if (kind == CppTokenKind.Tilde)
            {
                sawSpecifier = true;
                _r.Advance();
                if (!_r.Check((int)CppTokenKind.Identifier))
                {
                    _r.Seek(save);
                    return false;
                }
                isDestructor = true;
                name = "~" + (_r.Current.Text ?? "item");
                nameSpan = new TextSpan(start, Math.Max(0, _r.Current.End - start));
                _r.Advance();
                break;
            }
            if (kind == CppTokenKind.Identifier)
            {
                Token t = _r.Current;
                if ((CppTokenKind)_r.Peek(1).Kind == CppTokenKind.LParen)
                {
                    name = t.Text ?? "item";
                    nameSpan = t.Span;
                    qualified = lastSignificant == CppTokenKind.ColonColon;
                    if (sawSpecifier || isOperator || isDestructor || lastSignificant != CppTokenKind.Other || className == (t.Text ?? ""))
                    {
                        _r.Advance();
                        break;
                    }
                    _r.Seek(save);
                    return false;
                }
                lastIdentifierText = t.Text ?? "";
                lastIdentifierStart = t.Start;
                lastSignificant = kind;
                _r.Advance();
                continue;
            }
            if (IsTypeSpecifierKeyword(kind))
            {
                sawSpecifier = true;
                lastSignificant = kind;
                _r.Advance();
                continue;
            }
            if (kind is CppTokenKind.ColonColon or CppTokenKind.Star or CppTokenKind.Amp)
            {
                if (sawSpecifier)
                    lastSignificant = kind;
                _r.Advance();
                continue;
            }
            if (kind == CppTokenKind.LAngle)
            {
                _r.TrySkipBalanced((int)CppTokenKind.LAngle, (int)CppTokenKind.RAngle);
                lastSignificant = kind;
                continue;
            }
            if (kind == CppTokenKind.LParen)
            {
                _r.TrySkipBalanced((int)CppTokenKind.LParen, (int)CppTokenKind.RParen);
                lastSignificant = kind;
                continue;
            }
            break;
        }

        if (name is null || !_r.Check((int)CppTokenKind.LParen))
        {
            _r.Seek(save);
            return false;
        }
        if (!_r.TrySkipBalanced((int)CppTokenKind.LParen, (int)CppTokenKind.RParen))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("CPP001", "Unclosed parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
            SkipToSemicolon();
            return true;
        }
        if (!SkipTrailingQualifiers())
        {
            SkipToSemicolon();
            return true;
        }

        int end;
        if (_r.Check((int)CppTokenKind.LBrace))
        {
            Token open = _r.Current;
            if (!_r.TrySkipBalanced((int)CppTokenKind.LBrace, (int)CppTokenKind.RBrace))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("CPP001", "Unclosed function body.", open.Span, DiagnosticSeverity.Error));
                end = _text.Length;
            }
            else
                end = _r.Peek(-1).End;
        }
        else if (_r.Check((int)CppTokenKind.Semicolon))
        {
            _r.Advance();
            end = _r.Peek(-1).End;
        }
        else if (IsFunctionContinuation((CppTokenKind)_r.Current.Kind))
        {
            SkipToSemicolon();
            end = _r.Peek(-1).End;
        }
        else
        {
            _r.Seek(save);
            return false;
        }

        string outlineKind;
        string nodeKind;
        bool looksCtor = (className is not null && name == className)
            || (qualified && lastIdentifierText.Length > 0 && name == lastIdentifierText);
        if (isDestructor)
        {
            nodeKind = "destructor";
            outlineKind = "destructor";
        }
        else if (looksCtor)
        {
            nodeKind = "constructor";
            outlineKind = "constructor";
        }
        else if (className is not null || qualified)
        {
            nodeKind = "method_definition";
            outlineKind = "method";
        }
        else
        {
            nodeKind = "function_definition";
            outlineKind = "function";
        }

        Dictionary<string, string>? props = null;
        if ((nodeKind == "method_definition" || nodeKind == "constructor" || nodeKind == "destructor") && qualified)
        {
            string qualifier = lastIdentifierStart >= 0
                ? _text.Substring(lastIdentifierStart, Math.Max(0, nameSpan.Start - lastIdentifierStart))
                : "";
            props = new Dictionary<string, string> { ["qualified"] = qualifier + name };
        }

        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode(nodeKind, span, name, nameSpan, properties: props));
        outlines.Add(MakeOutline(outlineKind, name, span, level));
        _nodeCount++;
        return true;
    }

    private bool SkipTemplatePrefix()
    {
        _r.Advance();
        if (!_r.Check((int)CppTokenKind.LAngle))
            return true;
        if (!_r.TrySkipBalanced((int)CppTokenKind.LAngle, (int)CppTokenKind.RAngle))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("CPP002", "Unbalanced < in template parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
            SkipToSemicolon();
            return false;
        }
        return true;
    }

    private bool SkipTrailingQualifiers()
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            CppTokenKind kind = (CppTokenKind)_r.Current.Kind;
            if (IsTrailingQualifierKeyword(kind) || kind == CppTokenKind.Amp)
            {
                _r.Advance();
                continue;
            }
            if (kind == CppTokenKind.Equal)
            {
                _r.Advance();
                if (!_r.IsEof && !_r.Check((int)CppTokenKind.Semicolon))
                    _r.Advance();
                continue;
            }
            if (kind == CppTokenKind.Arrow)
            {
                SkipTrailingReturnType();
                return true;
            }
            return true;
        }
        return false;
    }

    private void SkipTrailingReturnType()
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            if (_r.Check((int)CppTokenKind.LBrace) || _r.Check((int)CppTokenKind.Semicolon))
                return;
            if (_r.Check((int)CppTokenKind.LParen))
                _r.TrySkipBalanced((int)CppTokenKind.LParen, (int)CppTokenKind.RParen);
            else if (_r.Check((int)CppTokenKind.LBracket))
                _r.TrySkipBalanced((int)CppTokenKind.LBracket, (int)CppTokenKind.RBracket);
            else
                _r.Advance();
        }
    }

    private void SkipTrash()
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            CppTokenKind k = (CppTokenKind)_r.Current.Kind;
            if (k is CppTokenKind.Semicolon)
            {
                _r.Advance();
                return;
            }
            if (IsDeclStarter(k))
                return;
            if (k is CppTokenKind.LBrace)
            {
                if (!_r.TrySkipBalanced((int)CppTokenKind.LBrace, (int)CppTokenKind.RBrace))
                    _partial = true;
            }
            else if (k is CppTokenKind.LParen)
            {
                _r.TrySkipBalanced((int)CppTokenKind.LParen, (int)CppTokenKind.RParen);
            }
            else if (k is CppTokenKind.LBracket)
            {
                _r.TrySkipBalanced((int)CppTokenKind.LBracket, (int)CppTokenKind.RBracket);
            }
            else if (k is CppTokenKind.LAngle)
            {
                _r.TrySkipBalanced((int)CppTokenKind.LAngle, (int)CppTokenKind.RAngle);
            }
            else
            {
                _r.Advance();
            }
        }
    }

    private static bool IsDeclStarter(CppTokenKind kind) => kind is
        CppTokenKind.KwClass or CppTokenKind.KwStruct or CppTokenKind.KwUnion or CppTokenKind.KwEnum or
        CppTokenKind.KwNamespace or CppTokenKind.KwTemplate;

    private void SkipToSemicolon()
    {
        while (!_r.IsEof && !_r.Check((int)CppTokenKind.Semicolon))
        {
            _r.ThrowIfCancellationRequested();
            _r.Advance();
        }
        if (_r.Check((int)CppTokenKind.Semicolon))
            _r.Advance();
    }

    private bool CheckCaps()
    {
        if (_nodeCount >= MaxNodes)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("CPP006", $"Outline node count exceeds CppOutlineMaxNodes ({MaxNodes}).", new TextSpan(_r.Current.Start, 0), DiagnosticSeverity.Warning));
            return false;
        }
        if (_depth >= MaxDepth)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("CPP005", $"Nesting depth exceeds CppMaxDepth ({MaxDepth}).", new TextSpan(_r.Current.Start, 0), DiagnosticSeverity.Warning));
            return false;
        }
        return true;
    }

    private static bool IsTypeSpecifierKeyword(CppTokenKind kind) => kind is
        CppTokenKind.KwClass or CppTokenKind.KwStruct or CppTokenKind.KwUnion or CppTokenKind.KwEnum or
        CppTokenKind.KwVoid or CppTokenKind.KwChar or CppTokenKind.KwInt or CppTokenKind.KwFloat or
        CppTokenKind.KwDouble or CppTokenKind.KwBool or CppTokenKind.KwUnsigned or CppTokenKind.KwSigned or
        CppTokenKind.KwLong or CppTokenKind.KwShort or CppTokenKind.KwAuto or CppTokenKind.KwConst or
        CppTokenKind.KwConstexpr or CppTokenKind.KwVolatile or CppTokenKind.KwStatic or CppTokenKind.KwMutable or
        CppTokenKind.KwExplicit or CppTokenKind.KwInline or CppTokenKind.KwExport or CppTokenKind.KwExtern or
        CppTokenKind.KwFriend or CppTokenKind.KwVirtual or CppTokenKind.KwNoexcept or CppTokenKind.KwTypedef;

    private static bool IsTrailingQualifierKeyword(CppTokenKind kind) => kind is
        CppTokenKind.KwConst or CppTokenKind.KwVolatile or CppTokenKind.KwNoexcept or
        CppTokenKind.KwOverride or CppTokenKind.KwFinal;

    private static bool IsFunctionContinuation(CppTokenKind kind) => kind is
        CppTokenKind.LParen;

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }
}
