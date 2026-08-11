using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Shell;

/// <summary>Hand-written shell lexer (structure-oriented; # comments, quotes).</summary>
internal sealed class ShLexer
{
    private readonly LexCursor _c;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly List<Token> _tokens = [];
    private bool _partial;

    public ShLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _c = new LexCursor(text, ct);
        _diagnostics = diagnostics;
    }

    public bool IsPartial => _partial;

    public IReadOnlyList<Token> Tokenize()
    {
        if (_c.StartsWith("#!"))
        {
            _c.SkipUntilNewline();
            if (_c.Peek() is '\r' or '\n')
                _c.Advance();
        }

        while (!_c.IsEof)
        {
            _c.ThrowIfCancellationRequested();
            _c.SkipWhitespaceHorizontal();
            if (_c.IsEof)
                break;

            char ch = _c.Peek();
            if (ch is '\r' or '\n')
            {
                int start = _c.Position;
                if (ch == '\r')
                    _c.Advance();
                if (_c.Peek() == '\n')
                    _c.Advance();
                Emit(ShTokenKind.Newline, start);
                continue;
            }

            if (ch == '#')
            {
                _c.SkipHashComment();
                continue;
            }

            if (ch is '"' or '\'')
            {
                int start = _c.Position;
                // single-quoted: no escapes; double: basic escapes
                if (ch == '\'')
                {
                    _c.Advance();
                    while (!_c.IsEof && _c.Peek() != '\'')
                        _c.Advance();
                    if (_c.Peek() == '\'')
                        _c.Advance();
                    else
                        _partial = true;
                    Emit(ShTokenKind.StringLiteral, start);
                }
                else
                {
                    TextSpan span = _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                    if (unclosed)
                        _partial = true;
                    Emit(ShTokenKind.StringLiteral, new TextSpan(start, _c.Position - start));
                    _ = span;
                }
                continue;
            }

            if (LexCursor.IsDigit(ch))
            {
                Emit(ShTokenKind.NumericLiteral, _c.ReadNumber());
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
                string text = _c.Slice(span);
                ShTokenKind kind = text == "function" ? ShTokenKind.KwFunction : ShTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            int pstart = _c.Position;
            char p = _c.Advance();
            ShTokenKind pk = p switch
            {
                '(' => ShTokenKind.LParen,
                ')' => ShTokenKind.RParen,
                '{' => ShTokenKind.LBrace,
                '}' => ShTokenKind.RBrace,
                '[' => ShTokenKind.LBracket,
                ']' => ShTokenKind.RBracket,
                ';' => ShTokenKind.Semicolon,
                _ => ShTokenKind.Other,
            };
            Emit(pk, pstart);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void Emit(ShTokenKind kind, TextSpan span, string? text = null)
        => _tokens.Add(new Token((int)kind, span, text));

    private void Emit(ShTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));
}
