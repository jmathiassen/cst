using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Rust;

internal sealed class RustParser
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

    public RustParser(string text, IReadOnlyList<Token> tokens, LineMap map, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
            ParseItems(children, outlines, 1, false);
        }
        catch (OperationCanceledException)
        {
            _cancelled = true;
        }
        return children;
    }

    private void ParseItems(List<SyntaxNode> parentChildren, List<OutlineNode> parentOutlines, int baseLevel, bool insideImplOrTrait, bool stopAtBrace = false)
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();

            if (_r.Check((int)RustTokenKind.RBrace))
                return;

            SkipAttributes();

            if (_r.Check((int)RustTokenKind.KwPub))
            {
                _r.Advance();
                SkipParenQualifier();
                SkipAttributes();
            }
            if (_r.Check((int)RustTokenKind.KwAsync) || _r.Check((int)RustTokenKind.KwUnsafe) || _r.Check((int)RustTokenKind.KwExtern))
            {
                _r.Advance();
                if (_r.Check((int)RustTokenKind.StringLiteral))
                    _r.Advance();
            }

            if (_r.Check((int)RustTokenKind.KwUse))
            {
                SkipUse();
                continue;
            }

            if (_r.Check((int)RustTokenKind.KwMod))
            {
                TryParseMod(parentChildren, parentOutlines, baseLevel);
                continue;
            }

            if (_r.Check((int)RustTokenKind.KwFn))
            {
                string ok = insideImplOrTrait ? "method" : "fn";
                TryParseFn(parentChildren, parentOutlines, baseLevel, ok);
                continue;
            }

            if (_r.Check((int)RustTokenKind.KwStruct))
            {
                TryParseStruct(parentChildren, parentOutlines, baseLevel);
                continue;
            }

            if (_r.Check((int)RustTokenKind.KwEnum))
            {
                TryParseEnum(parentChildren, parentOutlines, baseLevel);
                continue;
            }

            if (_r.Check((int)RustTokenKind.KwUnion))
            {
                TryParseUnion(parentChildren, parentOutlines, baseLevel);
                continue;
            }

            if (_r.Check((int)RustTokenKind.KwImpl))
            {
                TryParseImpl(parentChildren, parentOutlines, baseLevel);
                continue;
            }

            if (_r.Check((int)RustTokenKind.KwTrait))
            {
                TryParseTrait(parentChildren, parentOutlines, baseLevel);
                continue;
            }

            if (_r.Check((int)RustTokenKind.KwConst) || _r.Check((int)RustTokenKind.KwStatic))
            {
                // const fn should dispatch to function parsing, not const/static
                if (_r.Check((int)RustTokenKind.KwConst) && !_r.IsEof && _r.Peek(1).Kind == (int)RustTokenKind.KwFn)
                {
                    _r.Advance(); // consume const, let loop dispatch KwFn to TryParseFn
                }
                else
                {
                    TryParseConstStatic(parentChildren, parentOutlines, baseLevel);
                    continue;
                }
            }

            if (_r.Check((int)RustTokenKind.KwMacroRules))
            {
                TryParseMacroRules(parentChildren, parentOutlines, baseLevel);
                continue;
            }

            if (_r.Check((int)RustTokenKind.KwType))
            {
                SkipTypeAlias();
                continue;
            }

            if (_r.Check((int)RustTokenKind.Semicolon))
            {
                _r.Advance();
                continue;
            }

            if (!SkipTrash())
                return;
        }
    }

    private void SkipAttributes()
    {
        while (!_r.IsEof)
        {
            if (_r.Check((int)RustTokenKind.Hash))
            {
                _r.Advance();
                if (_r.Check((int)RustTokenKind.Exclamation))
                    _r.Advance();
                if (_r.Check((int)RustTokenKind.LBracket))
                {
                    if (!_r.TrySkipBalanced((int)RustTokenKind.LBracket, (int)RustTokenKind.RBracket))
                    {
                        _partial = true;
                        _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed attribute bracket.", _r.Current.Span, DiagnosticSeverity.Error));
                        return;
                    }
                    continue;
                }
            }
            break;
        }
    }

    private void SkipParenQualifier()
    {
        if (_r.Check((int)RustTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)RustTokenKind.LParen, (int)RustTokenKind.RParen))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed visibility qualifier.", _r.Current.Span, DiagnosticSeverity.Error));
            }
        }
    }

    private void SkipUse()
    {
        _r.Advance();
        SkipToSemicolonOrBrace();
    }

    private void SkipTypeAlias()
    {
        _r.Advance();
        SkipToSemicolonOrBrace();
    }

    private bool TryParseMod(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        _r.Advance();
        if (!_r.Check((int)RustTokenKind.Identifier))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        string name = _r.Current.Text ?? "mod";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        if (_r.Check((int)RustTokenKind.Semicolon))
        {
            TextSpan span = new(start, _r.Current.End - start);
            children.Add(new SyntaxNode("mod_item", span, name, nameSpan));
            outlines.Add(MakeOutline("mod", name, span, baseLevel));
            _r.Advance();
            _nodeCount++;
            return true;
        }

        if (!_r.Check((int)RustTokenKind.LBrace))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        int bodyOpenPos = _r.Current.Start;
        _r.Advance();

        if (baseLevel + 1 > MaxDepth)
        {
            _diagnostics.Add(new ParseDiagnostic("RS005", "Max depth exceeded.", new TextSpan(bodyOpenPos, 0), DiagnosticSeverity.Warning));
            if (!_r.TrySkipBalanced((int)RustTokenKind.LBrace, (int)RustTokenKind.RBrace))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed mod body.", _r.Current.Span, DiagnosticSeverity.Error));
            }
            TextSpan span2 = new(start, _r.Peek(-1).End - start);
            children.Add(new SyntaxNode("mod_item", span2, name, nameSpan));
            outlines.Add(MakeOutline("mod", name, span2, baseLevel));
            _nodeCount++;
            return true;
        }
        _depth++;

        List<SyntaxNode> innerChildren = [];
        List<OutlineNode> innerOutlines = [];
        ParseItems(innerChildren, innerOutlines, baseLevel + 1, false, stopAtBrace: true);

        int bodyEndPos;
        if (_r.Check((int)RustTokenKind.RBrace))
        {
            bodyEndPos = _r.Current.End;
            _r.Advance();
        }
        else
        {
            bodyEndPos = _r.Peek(-1).End;
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed mod body.", new TextSpan(bodyOpenPos, 0), DiagnosticSeverity.Error));
        }

        TextSpan modSpan = new(start, bodyEndPos - start);
        children.Add(new SyntaxNode("mod_item", modSpan, name, nameSpan, innerChildren.Count > 0 ? innerChildren : null));
        outlines.Add(MakeOutline("mod", name, modSpan, baseLevel, innerOutlines.Count > 0 ? innerOutlines : null));
        _nodeCount++;
        _depth--;
        return true;
    }

    private bool TryParseFn(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, string outlineKind)
    {
        int start = _r.Current.Start;
        _r.Advance();

        if (!_r.Check((int)RustTokenKind.Identifier))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        string name = _r.Current.Text ?? "fn";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();
        SkipGenerics();

        if (_r.Check((int)RustTokenKind.Exclamation))
            _r.Advance();

        if (_r.Check((int)RustTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)RustTokenKind.LParen, (int)RustTokenKind.RParen))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
                SkipToSemicolonOrBrace();
                return true;
            }
        }

        if (_r.Check((int)RustTokenKind.ColonColon))
        {
            if (_r.Check((int)RustTokenKind.Identifier))
            {
                name = _r.Current.Text ?? name;
                nameSpan = _r.Current.Span;
                _r.Advance();
            }
        }

        SkipReturnType();

        SkipWhereClause();

        if (!_r.Check((int)RustTokenKind.LBrace))
        {
            SkipToSemicolonOrBrace();
            int endPos = _r.Peek(-1).End;
            CheckCaps(start, endPos);
            string nk = outlineKind == "method" ? "method_item" : "function_item";
            TextSpan sp = new(start, Math.Max(0, endPos - start));
            children.Add(new SyntaxNode(nk, sp, name, nameSpan));
            outlines.Add(MakeOutline(outlineKind, name, sp, baseLevel));
            _nodeCount++;
            return true;
        }
        if (!_r.TrySkipBalanced((int)RustTokenKind.LBrace, (int)RustTokenKind.RBrace))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed fn body.", _r.Current.Span, DiagnosticSeverity.Error));
        }
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);

        string nodeKind = outlineKind == "method" ? "method_item" : "function_item";
        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode(nodeKind, span, name, nameSpan));
        outlines.Add(MakeOutline(outlineKind, name, span, baseLevel));
        _nodeCount++;
        return true;
    }

    private void SkipReturnType()
    {
        if (_r.Check((int)RustTokenKind.Arrow))
        {
            _r.Advance();
            SkipType();
        }
    }

    private void SkipType()
    {
        while (!_r.IsEof)
        {
            RustTokenKind k = (RustTokenKind)_r.Current.Kind;
            if (k is RustTokenKind.Semicolon or RustTokenKind.LBrace or RustTokenKind.RBrace or RustTokenKind.LParen or RustTokenKind.RParen)
                return;
            if (k == RustTokenKind.LBracket)
            {
                if (!_r.TrySkipBalanced((int)RustTokenKind.LBracket, (int)RustTokenKind.RBracket))
                    return;
                continue;
            }
            if (k == RustTokenKind.LAngle)
            {
                if (!TrySkipGenerics())
                    return;
                continue;
            }
            _r.Advance();
        }
    }

    private void SkipWhereClause()
    {
        if (!_r.Check((int)RustTokenKind.KwWhere))
            return;
        _r.Advance();
        while (!_r.IsEof)
        {
            RustTokenKind k = (RustTokenKind)_r.Current.Kind;
            if (k is RustTokenKind.LBrace or RustTokenKind.Semicolon)
                return;
            if (_r.Check((int)RustTokenKind.KwWhere))
            {
                _r.Advance();
                continue;
            }
            if (_r.Check((int)RustTokenKind.LBrace))
                return;
            if (k == RustTokenKind.LBracket)
            {
                if (!_r.TrySkipBalanced((int)RustTokenKind.LBracket, (int)RustTokenKind.RBracket))
                    return;
                continue;
            }
            if (k == RustTokenKind.LAngle)
            {
                if (!TrySkipGenerics())
                    return;
                continue;
            }
            _r.Advance();
        }
    }

    private bool TryParseStruct(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        _r.Advance();
        string? name = ScanName();
        if (name is null)
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        TextSpan nameSpan = _r.Peek(-1).Span;
        SkipGenerics();

        if (_r.Check((int)RustTokenKind.Semicolon))
        {
            TextSpan span = new(start, _r.Current.End - start);
            children.Add(new SyntaxNode("struct_item", span, name, nameSpan));
            outlines.Add(MakeOutline("struct", name, span, baseLevel));
            _r.Advance();
            _nodeCount++;
            return true;
        }

        if (_r.Check((int)RustTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)RustTokenKind.LParen, (int)RustTokenKind.RParen))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed tuple struct body.", _r.Current.Span, DiagnosticSeverity.Error));
            }
            SkipWhereClause();
            TextSpan span = new(start, _r.Peek(-1).End - start);
            CheckCaps(start, span.End);
            children.Add(new SyntaxNode("struct_item", span, name, nameSpan));
            outlines.Add(MakeOutline("struct", name, span, baseLevel));
            _nodeCount++;
            return true;
        }

        if (!_r.Check((int)RustTokenKind.LBrace))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        if (!_r.TrySkipBalanced((int)RustTokenKind.LBrace, (int)RustTokenKind.RBrace))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed struct body.", _r.Current.Span, DiagnosticSeverity.Error));
        }
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan bodySpan = new(start, end - start);
        children.Add(new SyntaxNode("struct_item", bodySpan, name, nameSpan));
        outlines.Add(MakeOutline("struct", name, bodySpan, baseLevel));
        _nodeCount++;
        return true;
    }

    private bool TryParseEnum(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        _r.Advance();
        string? name = ScanName();
        if (name is null)
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        TextSpan nameSpan = _r.Peek(-1).Span;
        SkipGenerics();

        if (_r.Check((int)RustTokenKind.Semicolon))
        {
            TextSpan span = new(start, _r.Current.End - start);
            children.Add(new SyntaxNode("enum_item", span, name, nameSpan));
            outlines.Add(MakeOutline("enum", name, span, baseLevel));
            _r.Advance();
            _nodeCount++;
            return true;
        }

        SkipWhereClause();

        if (!_r.Check((int)RustTokenKind.LBrace))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        if (!_r.TrySkipBalanced((int)RustTokenKind.LBrace, (int)RustTokenKind.RBrace))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed enum body.", _r.Current.Span, DiagnosticSeverity.Error));
        }
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan bodySpan = new(start, end - start);
        children.Add(new SyntaxNode("enum_item", bodySpan, name, nameSpan));
        outlines.Add(MakeOutline("enum", name, bodySpan, baseLevel));
        _nodeCount++;
        return true;
    }

    private bool TryParseUnion(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        _r.Advance();
        string? name = ScanName();
        if (name is null)
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        TextSpan nameSpan = _r.Peek(-1).Span;

        if (!_r.Check((int)RustTokenKind.LBrace))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        if (!_r.TrySkipBalanced((int)RustTokenKind.LBrace, (int)RustTokenKind.RBrace))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed union body.", _r.Current.Span, DiagnosticSeverity.Error));
        }
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan bodySpan = new(start, end - start);
        children.Add(new SyntaxNode("union_item", bodySpan, name, nameSpan));
        outlines.Add(MakeOutline("union", name, bodySpan, baseLevel));
        _nodeCount++;
        return true;
    }

    private bool TryParseImpl(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        string? label = ScanImplLabel();
        if (label is null)
            return true;

        if (baseLevel + 1 > MaxDepth)
        {
            _diagnostics.Add(new ParseDiagnostic("RS005", "Max depth exceeded.", new TextSpan(start, _r.Current.End - start), DiagnosticSeverity.Warning));
            SkipToSemicolonOrBrace();
            return true;
        }
        _depth++;

        SkipWhereClause();

        if (!_r.Check((int)RustTokenKind.LBrace))
        {
            SkipToSemicolonOrBrace();
            _depth--;
            return true;
        }
        _r.Advance();

        List<SyntaxNode> innerChildren = [];
        List<OutlineNode> innerOutlines = [];
        if (_nodeCount < MaxOutlineNodes)
            ParseItems(innerChildren, innerOutlines, baseLevel + 1, true, stopAtBrace: true);

        int bodyEndPos;
        if (_r.Check((int)RustTokenKind.RBrace))
        {
            bodyEndPos = _r.Current.End;
            _r.Advance();
        }
        else
        {
            bodyEndPos = _r.Peek(-1).End;
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed impl body.", _r.Current.Span, DiagnosticSeverity.Error));
        }

        TextSpan span = new(start, bodyEndPos - start);
        children.Add(new SyntaxNode("impl_item", span, label, null, innerChildren.Count > 0 ? innerChildren : null));
        outlines.Add(MakeOutline("impl", label, span, baseLevel, innerOutlines.Count > 0 ? innerOutlines : null));
        _nodeCount++;
        _depth--;
        return true;
    }

    private bool TryParseTrait(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        _r.Advance();
        string? name = ScanName();
        if (name is null)
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        TextSpan nameSpan = _r.Peek(-1).Span;
        SkipGenerics();

        if (_r.Check((int)RustTokenKind.Semicolon))
        {
            TextSpan semiSpan = new(start, _r.Current.End - start);
            children.Add(new SyntaxNode("trait_item", semiSpan, name, nameSpan));
            outlines.Add(MakeOutline("trait", name, semiSpan, baseLevel));
            _r.Advance();
            _nodeCount++;
            return true;
        }

        SkipWhereClause();

        if (baseLevel + 1 > MaxDepth)
        {
            _diagnostics.Add(new ParseDiagnostic("RS005", "Max depth exceeded.", new TextSpan(start, _r.Current.End - start), DiagnosticSeverity.Warning));
            SkipToSemicolonOrBrace();
            return true;
        }
        _depth++;

        if (!_r.Check((int)RustTokenKind.LBrace))
        {
            SkipToSemicolonOrBrace();
            _depth--;
            return true;
        }
        _r.Advance();

        List<SyntaxNode> innerChildren = [];
        List<OutlineNode> innerOutlines = [];
        if (_nodeCount < MaxOutlineNodes)
            ParseItems(innerChildren, innerOutlines, baseLevel + 1, true, stopAtBrace: true);

        int bodyEndPos;
        if (_r.Check((int)RustTokenKind.RBrace))
        {
            bodyEndPos = _r.Current.End;
            _r.Advance();
        }
        else
        {
            bodyEndPos = _r.Peek(-1).End;
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed trait body.", _r.Current.Span, DiagnosticSeverity.Error));
        }

        TextSpan span = new(start, bodyEndPos - start);
        children.Add(new SyntaxNode("trait_item", span, name, nameSpan, innerChildren.Count > 0 ? innerChildren : null));
        outlines.Add(MakeOutline("trait", name, span, baseLevel, innerOutlines.Count > 0 ? innerOutlines : null));
        _nodeCount++;
        _depth--;
        return true;
    }

    private bool TryParseConstStatic(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        bool isStatic = _r.Check((int)RustTokenKind.KwStatic);
        _r.Advance();
        string? name = ScanName();
        if (name is null)
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        TextSpan nameSpan = _r.Peek(-1).Span;

        if (_r.Check((int)RustTokenKind.Semicolon))
        {
            TextSpan span = new(start, _r.Current.End - start);
            string kind = isStatic ? "static_item" : "const_item";
            string ok = isStatic ? "static" : "const";
            children.Add(new SyntaxNode(kind, span, name, nameSpan));
            outlines.Add(MakeOutline(ok, name, span, baseLevel));
            _r.Advance();
            _nodeCount++;
            return true;
        }

        if (_r.Check((int)RustTokenKind.Colon))
        {
            _r.Advance();
            SkipType();
            SkipWhereClause();
        }

        SkipToSemicolonOrBrace();
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        string k2 = isStatic ? "static_item" : "const_item";
        string ok2 = isStatic ? "static" : "const";
        TextSpan s2 = new(start, end - start);
        children.Add(new SyntaxNode(k2, s2, name, nameSpan));
        outlines.Add(MakeOutline(ok2, name, s2, baseLevel));
        _nodeCount++;
        return true;
    }

    private bool TryParseMacroRules(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        _r.Advance();
        if (!_r.Check((int)RustTokenKind.Exclamation))
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        _r.Advance();
        string? name = ScanName();
        if (name is null)
        {
            SkipToSemicolonOrBrace();
            return true;
        }
        TextSpan nameSpan = _r.Peek(-1).Span;

        SkipToSemicolonOrBrace();
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan span = new(start, end - start);
        children.Add(new SyntaxNode("macro_rules", span, name, nameSpan));
        outlines.Add(MakeOutline("macro", name, span, baseLevel));
        _nodeCount++;
        return true;
    }

    private string? ScanImplLabel()
    {
        _r.Advance();
        SkipGenerics();

        string label = "";
        bool foundFor = false;
        while (!_r.IsEof)
        {
            RustTokenKind k = (RustTokenKind)_r.Current.Kind;
            if (k is RustTokenKind.LBrace or RustTokenKind.Semicolon)
            {
                return label.Length > 0 ? (label.Length > 60 ? label[..60] : label) : null;
            }
            if (k == RustTokenKind.KwWhere)
            {
                SkipWhereClause();
                return label.Length > 0 ? (label.Length > 60 ? label[..60] : label) : null;
            }
            if (k == RustTokenKind.KwFor)
            {
                if (!foundFor)
                {
                    label += " for ";
                    foundFor = true;
                    _r.Advance();
                    continue;
                }
            }
            if (k == RustTokenKind.LAngle)
            {
                TrySkipGenerics();
                continue;
            }
            if (k is RustTokenKind.Identifier or RustTokenKind.KwSelf or RustTokenKind.KwDyn or RustTokenKind.ColonColon or RustTokenKind.Star or RustTokenKind.Amp or RustTokenKind.LParen or RustTokenKind.RParen or RustTokenKind.Question or RustTokenKind.Exclamation)
            {
                string? txt = _r.Current.Text;
                if (txt is not null)
                    label += txt;
                RustTokenKind sep = k == RustTokenKind.ColonColon ? RustTokenKind.ColonColon : k;
                if (k == RustTokenKind.LParen)
                {
                    if (!_r.TrySkipBalanced((int)RustTokenKind.LParen, (int)RustTokenKind.RParen))
                    {
                        _partial = true;
                        _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed parens in impl type.", _r.Current.Span, DiagnosticSeverity.Error));
                        return label.Length > 0 ? (label.Length > 60 ? label[..60] : label) : null;
                    }
                    continue;
                }
                _r.Advance();
                continue;
            }
            _r.Advance();
        }
        return label.Length > 0 ? (label.Length > 60 ? label[..60] : label) : null;
    }

    private string? ScanName()
    {
        if (!_r.Check((int)RustTokenKind.Identifier))
            return null;
        string name = _r.Current.Text ?? "item";
        _r.Advance();
        return name;
    }

    private bool SkipGenerics()
    {
        if (!_r.Check((int)RustTokenKind.LAngle))
            return false;
        return TrySkipGenerics();
    }

    private bool TrySkipGenerics()
    {
        if (!_r.TrySkipBalanced((int)RustTokenKind.LAngle, (int)RustTokenKind.RAngle))
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed generics.", _r.Current.Span, DiagnosticSeverity.Error));
            return false;
        }
        return true;
    }

    private void SkipToSemicolonOrBrace()
    {
        while (!_r.IsEof)
        {
            RustTokenKind k = (RustTokenKind)_r.Current.Kind;
            if (k is RustTokenKind.Semicolon)
            {
                _r.Advance();
                return;
            }
            if (k is RustTokenKind.LBrace or RustTokenKind.RBrace)
                return;
            if (k == RustTokenKind.LBracket && !_r.TrySkipBalanced((int)RustTokenKind.LBracket, (int)RustTokenKind.RBracket))
                return;
            if (k == RustTokenKind.LParen && !_r.TrySkipBalanced((int)RustTokenKind.LParen, (int)RustTokenKind.RParen))
                return;
            if (k == RustTokenKind.LAngle && !TrySkipGenerics())
                return;
            _r.Advance();
        }
    }

    private bool SkipTrash()
    {
        while (!_r.IsEof)
        {
            RustTokenKind k = (RustTokenKind)_r.Current.Kind;
            if (k is RustTokenKind.KwFn or RustTokenKind.KwStruct or RustTokenKind.KwEnum
                or RustTokenKind.KwImpl or RustTokenKind.KwTrait or RustTokenKind.KwMod
                or RustTokenKind.KwUse or RustTokenKind.KwConst or RustTokenKind.KwStatic
                or RustTokenKind.KwMacroRules or RustTokenKind.KwType
                or RustTokenKind.LBrace or RustTokenKind.RBrace or RustTokenKind.Semicolon
                or RustTokenKind.Hash)
                return true;
            _r.Advance();
        }
        return false;
    }

    private void CheckCaps(int start, int end)
    {
        if (_depth >= MaxDepth)
            _diagnostics.Add(new ParseDiagnostic("RS005", "Max depth exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
        if (_nodeCount >= MaxOutlineNodes)
            _diagnostics.Add(new ParseDiagnostic("RS006", "Max outline nodes exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
    }

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }
}
