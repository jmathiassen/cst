using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Perl;

sealed class PerlParser
{
    private readonly string _text;
    private readonly IReadOnlyList<Token> _tokens;
    private readonly LineMap _map;
    private readonly List<ParseDiagnostic> _diag;
    private int _pos;

    public bool IsPartial { get; private set; }

    private const int KSub = (int)PerlTokenKind.KeywordSub;
    private const int KPackage = (int)PerlTokenKind.KeywordPackage;
    private const int KMy = (int)PerlTokenKind.KeywordMy;
    private const int KOur = (int)PerlTokenKind.KeywordOur;
    private const int KBegin = (int)PerlTokenKind.KeywordBegin;
    private const int KEnd = (int)PerlTokenKind.KeywordEnd;
    private const int KUse = (int)PerlTokenKind.KeywordUse;
    private const int KRequire = (int)PerlTokenKind.KeywordRequire;
    private const int KLBrace = (int)PerlTokenKind.LBrace;
    private const int KRBrace = (int)PerlTokenKind.RBrace;
    private const int KLParen = (int)PerlTokenKind.LParen;
    private const int KRParen = (int)PerlTokenKind.RParen;
    private const int KColon = (int)PerlTokenKind.Colon;
    private const int KIdentifier = (int)PerlTokenKind.Identifier;
    private const int KSemicolon = (int)PerlTokenKind.Semicolon;
    private const int KEof = (int)PerlTokenKind.Eof;
    private const int KSigilScalar = (int)PerlTokenKind.SigilScalar;
    private const int KSigilArray = (int)PerlTokenKind.SigilArray;
    private const int KSigilHash = (int)PerlTokenKind.SigilHash;

    public PerlParser(
        string text,
        IReadOnlyList<Token> tokens,
        LineMap map,
        List<ParseDiagnostic> diagnostics,
        CancellationToken ct)
    {
        _ = ct;
        _text = text;
        _tokens = tokens;
        _map = map;
        _diag = diagnostics;
    }

    public List<SyntaxNode> Parse(out List<OutlineNode> outlines)
    {
        List<SyntaxNode> children = [];
        outlines = [];

        while (PeekKind(0) != KEof)
        {
            int kind = PeekKind(0);
            if (kind == KPackage)
                ParsePackageStatement(children, outlines);
            else if (kind == KSub)
                ParseSubroutine(children, outlines, 1);
            else if (kind == KBegin || kind == KEnd)
            {
                Advance();
                if (PeekKind(0) == KLBrace)
                    SkipBalanced();
            }
            else if (kind == KUse || kind == KRequire)
                SkipStatement();
            else
                SkipStatement();
        }

        return children;
    }

    private void ParsePackageStatement(List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        int start = Pos();
        Advance();
        string name = ReadQualifiedName() ?? "";
        int line = _map.GetLineNumber(start);
        TextSpan span = _map.GetSpanForLines(line, line);
        children.Add(new SyntaxNode("package", span, name));
        outlines.Add(TextUtil.Outline("package", name, _map, line, line, 1));
        SkipToSemicolon();
    }

    private void ParseSubroutine(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int baseLevel)
    {
        int subStart = Pos();
        Advance();
        int line = _map.GetLineNumber(subStart);
        string name = ReadSubName() ?? "";

        SkipPrototypeAndAttrs();

        if (PeekKind(0) == KLBrace)
        {
            int braceStart = TokStart();
            Advance();

            List<SyntaxNode> innerChildren = [];
            List<OutlineNode> innerOutlines = [];
            int innerLevel = baseLevel + 1;

            while (PeekKind(0) != KRBrace && PeekKind(0) != KEof)
            {
                if (PeekKind(0) == KSub)
                    ParseSubroutine(innerChildren, innerOutlines, innerLevel);
                else
                    SkipStatement();
            }

            int bodyEnd;
            if (PeekKind(0) == KRBrace)
            {
                bodyEnd = TokEnd();
                Advance();
            }
            else
            {
                IsPartial = true;
                _diag.Add(new ParseDiagnostic("PERL001", "Unclosed subroutine body.",
                    new TextSpan(braceStart, 0), DiagnosticSeverity.Error));
                bodyEnd = _text.Length;
            }

            int endLine = _map.GetLineNumber(bodyEnd - 1);
            TextSpan span = new(subStart, bodyEnd - subStart);
            children.Add(new SyntaxNode("function", span, name,
                children: innerChildren.Count > 0 ? innerChildren : null));
            outlines.Add(TextUtil.Outline("function", name, _map, line, endLine, baseLevel,
                innerOutlines.Count > 0 ? innerOutlines : null));
        }
        else
        {
            TextSpan span = new(subStart, Math.Max(Pos() - subStart, 1));
            children.Add(new SyntaxNode("function", span, name));
            outlines.Add(TextUtil.Outline("function", name, _map, line, line, baseLevel));
        }
    }

    private string? ReadQualifiedName()
    {
        int nameStart = -1;
        int nameEnd = -1;
        while (PeekKind(0) == KIdentifier || PeekKind(0) == KColon)
        {
            if (PeekKind(0) == KColon && PeekKind(1) != KColon)
                break;
            if (nameStart < 0)
                nameStart = _tokens[_pos].Start;
            nameEnd = _tokens[_pos].End;
            Advance();
        }
        if (nameStart < 0 || nameEnd <= nameStart)
            return null;
        return _text.Substring(nameStart, nameEnd - nameStart).Trim(':');
    }

    private string? ReadSubName()
    {
        // skip optional sigil (sub $ref, etc.)
        int k = PeekKind(0);
        if (k == KSigilScalar || k == KSigilArray || k == KSigilHash)
            Advance();

        return ReadQualifiedName();
    }

    private void SkipPrototypeAndAttrs()
    {
        // prototype: sub name (proto) { ... }
        if (PeekKind(0) == KLParen)
        {
            Advance();
            int depth = 1;
            while (depth > 0 && PeekKind(0) != KEof)
            {
                if (PeekKind(0) == KLParen) depth++;
                else if (PeekKind(0) == KRParen) depth--;
                Advance();
            }
        }
        // attributes: sub name :attr :attr(expr)
        while (PeekKind(0) == KColon && PeekKind(1) == KIdentifier)
        {
            Advance(2);
            if (PeekKind(0) == KLParen)
            {
                Advance();
                int depth = 1;
                while (depth > 0 && PeekKind(0) != KEof)
                {
                    if (PeekKind(0) == KLParen) depth++;
                    else if (PeekKind(0) == KRParen) depth--;
                    Advance();
                }
            }
        }
    }

    private void SkipBalanced()
    {
        Advance();
        int depth = 1;
        while (depth > 0 && PeekKind(0) != KEof)
        {
            if (PeekKind(0) == KLBrace) depth++;
            else if (PeekKind(0) == KRBrace) depth--;
            Advance();
        }
    }

    private void SkipStatement()
    {
        int depth = 0;
        while (PeekKind(0) != KEof)
        {
            if (PeekKind(0) == KSemicolon && depth == 0)
            {
                Advance();
                return;
            }
            if (PeekKind(0) == KLBrace) { depth++; Advance(); }
            else if (PeekKind(0) == KRBrace)
            {
                if (depth == 0) return;
                depth--; Advance();
            }
            else Advance();
        }
    }

    private void SkipToSemicolon()
    {
        while (PeekKind(0) != KSemicolon && PeekKind(0) != KLBrace && PeekKind(0) != KEof)
            Advance();
        if (PeekKind(0) == KSemicolon)
            Advance();
    }

    private int PeekKind(int offset) =>
        _pos + offset < _tokens.Count ? _tokens[_pos + offset].Kind : KEof;

    private int Pos() => _tokens[_pos].Start;
    private int TokStart() => _tokens[_pos].Start;
    private int TokEnd() => _tokens[_pos].End;

    private void Advance()
    {
        if (_pos < _tokens.Count - 1)
            _pos++;
    }

    private void Advance(int n)
    {
        _pos = Math.Min(_pos + n, _tokens.Count - 1);
    }
}
