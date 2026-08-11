using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Css;

internal sealed class CssLexer
{
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private bool _partial;

    public CssLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
            if (ch == '/' && _c.Peek(1) == '*')
            {
                _c.SkipBlockComment(out bool unclosed);
                if (unclosed) _partial = true;
                continue;
            }
            if (ch is '"' or '\'')
            {
                int start = _c.Position;
                _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
                if (unclosed) _partial = true;
                Emit(CssTokenKind.StringLiteral, start);
                continue;
            }
            if (ch == '@')
            {
                int start = _c.Position;
                _c.Advance();
                if (char.IsLetter(_c.Peek()) || _c.Peek() == '-')
                    _c.ReadIdentifier();
                Emit(CssTokenKind.AtKeyword, start, _c.Slice(new TextSpan(start, _c.Position - start)));
                continue;
            }
            if (ch == '#')
            {
                int start = _c.Position;
                _c.Advance();
                while (!_c.IsEof && (char.IsLetterOrDigit(_c.Peek()) || _c.Peek() is '-' or '_'))
                    _c.Advance();
                Emit(CssTokenKind.Hash, start, _c.Slice(new TextSpan(start, _c.Position - start)));
                continue;
            }
            if (LexCursor.IsDigit(ch) || (ch == '.' && LexCursor.IsDigit(_c.Peek(1))))
            {
                Emit(CssTokenKind.Number, _c.ReadNumber());
                continue;
            }
            if (char.IsLetter(ch) || ch is '_' or '-')
            {
                int start = _c.Position;
                while (!_c.IsEof)
                {
                    char c = _c.Peek();
                    if (char.IsLetterOrDigit(c) || c is '_' or '-')
                        _c.Advance();
                    else
                        break;
                }
                TextSpan span = new(start, _c.Position - start);
                Emit(CssTokenKind.Ident, span, _c.Slice(span));
                continue;
            }
            int pstart = _c.Position;
            char p = _c.Advance();
            CssTokenKind pk = p switch
            {
                '{' => CssTokenKind.LBrace,
                '}' => CssTokenKind.RBrace,
                '(' => CssTokenKind.LParen,
                ')' => CssTokenKind.RParen,
                '[' => CssTokenKind.LBracket,
                ']' => CssTokenKind.RBracket,
                ':' => CssTokenKind.Colon,
                ';' => CssTokenKind.Semicolon,
                ',' => CssTokenKind.Comma,
                '.' => CssTokenKind.Dot,
                '*' => CssTokenKind.Star,
                '>' => CssTokenKind.Gt,
                '+' => CssTokenKind.Plus,
                '~' => CssTokenKind.Tilde,
                _ => CssTokenKind.Other,
            };
            Emit(pk, pstart);
        }
        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void Emit(CssTokenKind kind, TextSpan span, string? text = null)
        => _tokens.Add(new Token((int)kind, span, text));

    private void Emit(CssTokenKind kind, int start, string? text = null)
        => Emit(kind, new TextSpan(start, _c.Position - start), text);
}
