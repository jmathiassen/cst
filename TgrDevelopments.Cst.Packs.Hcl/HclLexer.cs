using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Hcl;

internal sealed class HclLexer
{
    private static readonly Dictionary<string, HclTokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["resource"] = HclTokenKind.KwResource,
        ["data"] = HclTokenKind.KwData,
        ["module"] = HclTokenKind.KwModule,
        ["variable"] = HclTokenKind.KwVariable,
        ["output"] = HclTokenKind.KwOutput,
        ["provider"] = HclTokenKind.KwProvider,
        ["locals"] = HclTokenKind.KwLocals,
        ["terraform"] = HclTokenKind.KwTerraform,
        ["backend"] = HclTokenKind.KwBackend,
    };

    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private bool _partial;

    public HclLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _ = diagnostics;
        _c = new LexCursor(text, ct);
    }

    public bool IsPartial => _partial;

    public IReadOnlyList<Token> Tokenize()
    {
        while (!_c.IsEof)
        {
            _c.ThrowIfCancellationRequested();
            _c.SkipWhitespace();
            if (_c.IsEof) break;

            char ch = _c.Peek();
            if (ch == '#' || (ch == '/' && _c.Peek(1) == '/'))
            {
                if (ch == '/') _c.Advance(2);
                else _c.Advance();
                _c.SkipUntilNewline();
                continue;
            }
            if (ch == '/' && _c.Peek(1) == '*')
            {
                _c.SkipBlockComment(out bool unclosed);
                if (unclosed) _partial = true;
                continue;
            }
            if (ch == '"')
            {
                int start = _c.Position;
                _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed) _partial = true;
                string text = _c.Slice(new TextSpan(start, _c.Position - start));
                // strip quotes for label use
                string inner = text.Length >= 2 ? text.Substring(1, text.Length - 2) : text;
                Emit(HclTokenKind.StringLiteral, start, inner);
                continue;
            }
            if (LexCursor.IsDigit(ch))
            {
                Emit(HclTokenKind.Number, _c.ReadNumber());
                continue;
            }
            if (char.IsLetter(ch) || ch == '_')
            {
                TextSpan span = _c.ReadIdentifier();
                string text = _c.Slice(span);
                HclTokenKind kind = Keywords.TryGetValue(text, out HclTokenKind kw) ? kw : HclTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }
            int pstart = _c.Position;
            char p = _c.Advance();
            HclTokenKind pk = p switch
            {
                '{' => HclTokenKind.LBrace,
                '}' => HclTokenKind.RBrace,
                '(' => HclTokenKind.LParen,
                ')' => HclTokenKind.RParen,
                '[' => HclTokenKind.LBracket,
                ']' => HclTokenKind.RBracket,
                '=' => HclTokenKind.Eq,
                _ => HclTokenKind.Other,
            };
            Emit(pk, pstart);
        }
        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void Emit(HclTokenKind kind, TextSpan span, string? text = null)
        => _tokens.Add(new Token((int)kind, span, text));

    private void Emit(HclTokenKind kind, int start, string? text = null)
        => Emit(kind, new TextSpan(start, _c.Position - start), text);
}
