using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.M4;

/// <summary>
/// Structure-oriented m4 lexer: <c>dnl</c>/# comments, balanced <c>[…]</c> quotes,
/// identifiers, parens, commas, and quoted strings.
/// </summary>
internal sealed class M4Lexer
{
    private readonly LexCursor _c;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly List<Token> _tokens = [];
    private bool _partial;

    public M4Lexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
        : this(text, 0, text.Length, diagnostics, ct)
    {
    }

    /// <summary>
    /// Lex a window of <paramref name="text"/> without allocating a substring.
    /// Token spans are absolute offsets into <paramref name="text"/>.
    /// </summary>
    public M4Lexer(string text, int start, int length, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _c = new LexCursor(text, start, length, ct);
        _diagnostics = diagnostics;
    }

    public bool IsPartial => _partial;

    public IReadOnlyList<Token> Tokenize()
    {
        while (!_c.IsEof)
        {
            _c.ThrowIfCancellationRequested();
            _c.SkipWhitespace();
            if (_c.IsEof)
                break;

            char ch = _c.Peek();

            // dnl comment to EOL (keyword at token boundary)
            if (IsDnl())
            {
                _c.Advance(3);
                _c.SkipUntilNewline();
                continue;
            }

            // # line comment at quote-depth 0
            if (ch == '#')
            {
                _c.SkipHashComment();
                continue;
            }

            // Balanced [ … ] quote (nested)
            if (ch == '[')
            {
                int start = _c.Position;
                if (!ReadBalancedBrackets())
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic(
                        "M4001",
                        "Unclosed quote.",
                        new TextSpan(start, Math.Max(1, _c.Position - start)),
                        DiagnosticSeverity.Error));
                }
                Emit(M4TokenKind.Quoted, start);
                continue;
            }

            // " strings: skip so keywords inside do not start macros.
            // ' is only a string if closed on the same line; bare apostrophes
            // (don't, it's) are Other — Autoconf structure uses [ ] quotes.
            if (ch == '"')
            {
                int start = _c.Position;
                TextSpan span = _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed)
                    _partial = true;
                Emit(M4TokenKind.StringLiteral, new TextSpan(start, _c.Position - start));
                _ = span;
                continue;
            }

            if (ch == '\'')
            {
                int start = _c.Position;
                _c.Advance();
                int look = 0;
                bool closed = false;
                while (true)
                {
                    char n = _c.Peek(look);
                    if (n == '\0' || n == '\n' || n == '\r')
                        break;
                    if (n == '\'')
                    {
                        closed = true;
                        break;
                    }
                    look++;
                }
                if (closed)
                {
                    _c.Advance(look + 1);
                    Emit(M4TokenKind.StringLiteral, start);
                }
                else
                {
                    Emit(M4TokenKind.Other, start);
                }
                continue;
            }

            // Identifier: [A-Za-z_][A-Za-z0-9_]*
            if (char.IsLetter(ch) || ch == '_')
            {
                TextSpan span = _c.ReadIdentifier();
                string text = _c.Slice(span);
                Emit(M4TokenKind.Identifier, span, text);
                continue;
            }

            int pstart = _c.Position;
            char p = _c.Advance();
            M4TokenKind pk = p switch
            {
                '(' => M4TokenKind.LParen,
                ')' => M4TokenKind.RParen,
                ',' => M4TokenKind.Comma,
                _ => M4TokenKind.Other,
            };
            Emit(pk, pstart);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private bool IsDnl()
    {
        if (!_c.StartsWith("dnl"))
            return false;
        char after = _c.Peek(3);
        // token boundary: EOF, whitespace, or non-identifier continuation
        if (after == '\0')
            return true;
        if (char.IsLetterOrDigit(after) || after == '_')
            return false;
        return true;
    }

    /// <summary>
    /// Consume balanced <c>[…]</c> starting at current <c>[</c>. Returns false if unclosed.
    /// Inside Autoconf quotes only <c>[</c>/<c>]</c> nest; <c>"</c>/<c>'</c> are literal
    /// (e.g. English <c>don't</c> in a <c>dnl</c> line must not open a string).
    /// </summary>
    private bool ReadBalancedBrackets()
    {
        if (_c.Peek() != '[')
            return false;
        int depth = 0;
        while (!_c.IsEof)
        {
            _c.ThrowIfCancellationRequested();
            char c = _c.Peek();

            if (c == '[')
            {
                depth++;
                _c.Advance();
                continue;
            }

            if (c == ']')
            {
                depth--;
                _c.Advance();
                if (depth == 0)
                    return true;
                continue;
            }

            _c.Advance();
        }

        return false;
    }

    private void Emit(M4TokenKind kind, TextSpan span, string? text = null)
        => _tokens.Add(new Token((int)kind, span, text));

    private void Emit(M4TokenKind kind, int start, string? text = null)
        => Emit(kind, new TextSpan(start, _c.Position - start), text);
}
