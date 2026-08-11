using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.PowerShell;

/// <summary>Hand-written PowerShell lexer (structure-oriented).</summary>
internal sealed class PsLexer
{
    private static readonly Dictionary<string, PsTokenKind> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["function"] = PsTokenKind.KwFunction,
        ["filter"] = PsTokenKind.KwFilter,
        ["class"] = PsTokenKind.KwClass,
        ["enum"] = PsTokenKind.KwEnum,
    };

    private readonly LexCursor _c;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly List<Token> _tokens = [];
    private bool _partial;

    public PsLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _c = new LexCursor(text, ct);
        _diagnostics = diagnostics;
    }

    public bool IsPartial => _partial;

    public IReadOnlyList<Token> Tokenize()
    {
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
                if (ch == '\r') _c.Advance();
                if (_c.Peek() == '\n') _c.Advance();
                Emit(PsTokenKind.Newline, start);
                continue;
            }

            // # comment or <# block #>
            if (ch == '#')
            {
                if (_c.Peek(1) == '>')
                {
                    // stray
                    _c.Advance();
                    continue;
                }
                _c.SkipHashComment();
                continue;
            }
            if (ch == '<' && _c.Peek(1) == '#')
            {
                int start = _c.Position;
                _c.Advance();
                _c.Advance();
                while (!_c.IsEof)
                {
                    if (_c.Peek() == '#' && _c.Peek(1) == '>')
                    {
                        _c.Advance();
                        _c.Advance();
                        break;
                    }
                    _c.Advance();
                }
                if (_c.IsEof)
                    _partial = true;
                continue;
            }

            if (ch is '"' or '\'')
            {
                int start = _c.Position;
                if (ch == '\'')
                {
                    _c.Advance();
                    while (!_c.IsEof && _c.Peek() != '\'')
                    {
                        // '' escape
                        if (_c.Peek() == '\'' && _c.Peek(1) == '\'')
                        {
                            _c.Advance();
                            _c.Advance();
                            continue;
                        }
                        _c.Advance();
                    }
                    if (_c.Peek() == '\'') _c.Advance();
                    else _partial = true;
                }
                else
                {
                    _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                    if (unclosed) _partial = true;
                }
                Emit(PsTokenKind.StringLiteral, start);
                continue;
            }

            if (LexCursor.IsDigit(ch))
            {
                Emit(PsTokenKind.NumericLiteral, _c.ReadNumber());
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
                PsTokenKind kind = Keywords.TryGetValue(text, out PsTokenKind kw) ? kw : PsTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            int pstart = _c.Position;
            char p = _c.Advance();
            PsTokenKind pk = p switch
            {
                '(' => PsTokenKind.LParen,
                ')' => PsTokenKind.RParen,
                '{' => PsTokenKind.LBrace,
                '}' => PsTokenKind.RBrace,
                '[' => PsTokenKind.LBracket,
                ']' => PsTokenKind.RBracket,
                ';' => PsTokenKind.Semicolon,
                _ => PsTokenKind.Other,
            };
            Emit(pk, pstart);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void Emit(PsTokenKind kind, TextSpan span, string? text = null)
        => _tokens.Add(new Token((int)kind, span, text));

    private void Emit(PsTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));
}
