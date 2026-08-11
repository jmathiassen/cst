using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.FSharp;

/// <summary>
/// Structure parser for F# outline: namespace/module, type (+ members), let.
/// Uses token stream + line/column indent heuristics (not full F# layout).
/// File-scoped module/namespace owns following decls as children (span ends at next peer).
/// </summary>
internal sealed class FsParser
{
    private readonly string _text;
    private readonly LineMap _map;
    private readonly TokenReader _r;
    private bool _partial;

    public FsParser(string text, IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _ = diagnostics;
        _text = text;
        _map = new LineMap(text);
        _r = new TokenReader(tokens, ct);
    }

    public bool IsPartial => _partial;

    public List<SyntaxNode> Parse(out List<OutlineNode> outlines)
    {
        outlines = [];
        List<SyntaxNode> children = [];
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            if (_r.Check((int)FsTokenKind.Newline))
            {
                _r.Advance();
                continue;
            }
            if (TryParseDecl(children, outlines, level: 1, stopAtPeerModule: false))
                continue;
            _r.Advance();
        }
        return children;
    }

    private bool TryParseDecl(
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        bool stopAtPeerModule)
    {
        FsTokenKind k = (FsTokenKind)_r.Current.Kind;

        if (k is FsTokenKind.KwNamespace or FsTokenKind.KwModule)
        {
            if (stopAtPeerModule)
                return false;
            return ParseModuleOrNs(children, outlines, level);
        }

        if (k == FsTokenKind.KwType)
            return ParseType(children, outlines, level);

        if (k == FsTokenKind.KwLet)
            return ParseLet(children, outlines, level);

        if (k == FsTokenKind.KwMember)
            return ParseMember(children, outlines, level);

        return false;
    }

    private bool ParseModuleOrNs(List<SyntaxNode> children, List<OutlineNode> outlines, int level)
    {
        int start = _r.Current.Start;
        int startCol = ColumnOf(start);
        bool isNs = _r.Check((int)FsTokenKind.KwNamespace);
        _r.Advance();
        string name = ReadDottedName();

        bool fileScoped = true;
        int guard = 0;
        while (!_r.IsEof && !_r.Check((int)FsTokenKind.Newline) && guard++ < 16)
        {
            if (_r.Check((int)FsTokenKind.Eq) || _r.Check((int)FsTokenKind.LBrace))
            {
                fileScoped = false;
                break;
            }
            _r.Advance();
        }
        while (_r.Check((int)FsTokenKind.Newline))
            _r.Advance();

        List<SyntaxNode> nestedChildren = [];
        List<OutlineNode> nestedOutlines = [];
        int end;

        if (!fileScoped && _r.Check((int)FsTokenKind.LBrace))
        {
            end = SkipBrace();
        }
        else if (!fileScoped && _r.Check((int)FsTokenKind.Eq))
        {
            _r.Advance();
            while (_r.Check((int)FsTokenKind.Newline))
                _r.Advance();
            end = CollectNestedBody(startCol, nestedChildren, nestedOutlines, level + 1);
        }
        else
        {
            // File-scoped: own following decls until next module/namespace at same indent.
            end = CollectNestedBody(startCol, nestedChildren, nestedOutlines, level + 1, stopAtPeerModule: true);
        }

        if (end <= start)
            end = Math.Min(_text.Length, start + 1);

        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("module_decl", span, name, children: nestedChildren));
        outlines.Add(Make("module", name, span, level, nestedOutlines));
        _ = isNs;
        return true;
    }

    private int CollectNestedBody(
        int parentCol,
        List<SyntaxNode> children,
        List<OutlineNode> outlines,
        int level,
        bool stopAtPeerModule = false)
    {
        int end = _r.Current.IsEof ? _text.Length : _r.Current.Start;
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();
            if (_r.Check((int)FsTokenKind.Newline))
            {
                _r.Advance();
                continue;
            }

            int col = ColumnOf(_r.Current.Start);
            FsTokenKind k = (FsTokenKind)_r.Current.Kind;

            if (col < parentCol)
                break;

            if (col == parentCol && k is FsTokenKind.KwModule or FsTokenKind.KwNamespace)
                break;

            if (col == parentCol && stopAtPeerModule
                && k is FsTokenKind.KwType or FsTokenKind.KwLet)
            {
                // Still inside file-scoped module (same column is OK for F# nested content).
            }

            int save = _r.Index;
            if (TryParseDecl(children, outlines, level, stopAtPeerModule: false))
            {
                end = _r.Current.IsEof ? _text.Length : _r.Current.Start;
                if (children.Count > 0)
                {
                    SyntaxNode last = children[^1];
                    end = Math.Max(end, last.Span.End);
                }
                continue;
            }

            _r.Seek(save);
            // Non-decl line: if at or left of parent and is a keyword peer, stop; else consume line.
            if (col <= parentCol && k is FsTokenKind.KwModule or FsTokenKind.KwNamespace)
                break;

            while (!_r.IsEof && !_r.Check((int)FsTokenKind.Newline))
            {
                end = _r.Current.End;
                _r.Advance();
            }
        }
        return end;
    }

    private bool ParseType(List<SyntaxNode> children, List<OutlineNode> outlines, int level)
    {
        int start = _r.Current.Start;
        int startCol = ColumnOf(start);
        _r.Advance(); // type
        string name = "*";
        if (_r.Check((int)FsTokenKind.Identifier))
        {
            name = _r.Current.Text ?? "*";
            _r.Advance();
        }

        while (!_r.IsEof && !_r.Check((int)FsTokenKind.Newline) && !_r.Check((int)FsTokenKind.LBrace))
            _r.Advance();

        List<OutlineNode> members = [];
        int end;
        if (_r.Check((int)FsTokenKind.LBrace))
            end = SkipBrace();
        else
        {
            if (_r.Check((int)FsTokenKind.Newline))
                _r.Advance();
            end = CollectIndentedMembers(start, startCol, members);
        }

        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("type_decl", span, name));
        outlines.Add(Make("type", name, span, level, members));
        return true;
    }

    private bool ParseLet(List<SyntaxNode> children, List<OutlineNode> outlines, int level)
    {
        int start = _r.Current.Start;
        _r.Advance(); // let
        if (_r.Check((int)FsTokenKind.KwRec))
            _r.Advance();
        string name = "*";
        if (_r.Check((int)FsTokenKind.Identifier))
        {
            name = _r.Current.Text ?? "*";
            _r.Advance();
        }
        while (!_r.IsEof && !_r.Check((int)FsTokenKind.Newline))
            _r.Advance();
        int end = _r.Current.IsEof ? _text.Length : _r.Current.Start;
        if (_r.Check((int)FsTokenKind.Newline))
            end = _r.Current.Start;
        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("let_binding", span, name));
        outlines.Add(Make("function", name, span, level));
        return true;
    }

    private bool ParseMember(List<SyntaxNode> children, List<OutlineNode> outlines, int level)
    {
        int start = _r.Current.Start;
        _r.Advance(); // member
        if (_r.Check((int)FsTokenKind.Identifier) && (FsTokenKind)_r.Peek(1).Kind == FsTokenKind.Dot)
        {
            _r.Advance();
            _r.Advance();
        }
        string name = "*";
        if (_r.Check((int)FsTokenKind.Identifier))
        {
            name = _r.Current.Text ?? "*";
            _r.Advance();
        }
        while (!_r.IsEof && !_r.Check((int)FsTokenKind.Newline))
            _r.Advance();
        int end = _r.Current.IsEof ? _text.Length : _r.Current.Start;
        TextSpan span = new(start, Math.Max(0, end - start));
        children.Add(new SyntaxNode("member", span, name));
        outlines.Add(Make("member", name, span, level));
        return true;
    }

    private int CollectIndentedMembers(int typeStart, int typeCol, List<OutlineNode> members)
    {
        int end = typeStart + 1;
        while (!_r.IsEof)
        {
            if (_r.Check((int)FsTokenKind.Newline))
            {
                _r.Advance();
                continue;
            }
            int col = ColumnOf(_r.Current.Start);
            FsTokenKind k = (FsTokenKind)_r.Current.Kind;

            if (col <= typeCol && k is FsTokenKind.KwType or FsTokenKind.KwLet or FsTokenKind.KwModule
                or FsTokenKind.KwNamespace)
                break;

            if (k == FsTokenKind.KwMember && col > typeCol)
            {
                int mstart = _r.Current.Start;
                _r.Advance();
                if (_r.Check((int)FsTokenKind.Identifier) && (FsTokenKind)_r.Peek(1).Kind == FsTokenKind.Dot)
                {
                    _r.Advance();
                    _r.Advance();
                }
                string mname = "*";
                if (_r.Check((int)FsTokenKind.Identifier))
                {
                    mname = _r.Current.Text ?? "*";
                    _r.Advance();
                }
                while (!_r.IsEof && !_r.Check((int)FsTokenKind.Newline))
                    _r.Advance();
                int mend = _r.Current.IsEof ? _text.Length : _r.Current.Start;
                TextSpan mspan = new(mstart, Math.Max(0, mend - mstart));
                members.Add(Make("member", mname, mspan, 2));
                end = mend;
                continue;
            }

            while (!_r.IsEof && !_r.Check((int)FsTokenKind.Newline))
            {
                end = _r.Current.End;
                _r.Advance();
            }
        }
        if (end <= typeStart)
            end = Math.Min(_text.Length, typeStart + 1);
        return end;
    }

    private string ReadDottedName()
    {
        if (!_r.Check((int)FsTokenKind.Identifier))
            return "*";
        System.Text.StringBuilder sb = new(_r.Current.Text ?? "");
        _r.Advance();
        while (_r.Check((int)FsTokenKind.Dot) && (FsTokenKind)_r.Peek(1).Kind == FsTokenKind.Identifier)
        {
            _r.Advance();
            sb.Append('.');
            sb.Append(_r.Current.Text ?? "");
            _r.Advance();
        }
        return sb.ToString();
    }

    private int SkipBrace()
    {
        if (!_r.TrySkipBalanced((int)FsTokenKind.LBrace, (int)FsTokenKind.RBrace))
        {
            _partial = true;
            return _text.Length;
        }
        return _r.Peek(-1).End;
    }

    private int ColumnOf(int offset)
    {
        LinePosition p = _map.GetPosition(offset);
        return p.Column;
    }

    private OutlineNode Make(string kind, string label, TextSpan span, int level, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        return new OutlineNode(kind, label, span,
            new LinePositionSpan(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1)),
            level, children);
    }
}
