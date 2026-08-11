using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.FSharp;

/// <summary>F# lexer with simplified NEWLINE (indent tracked for structure parsing via column).</summary>
internal sealed class FsLexer
{
    private static readonly Dictionary<string, FsTokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["namespace"] = FsTokenKind.KwNamespace,
        ["module"] = FsTokenKind.KwModule,
        ["type"] = FsTokenKind.KwType,
        ["let"] = FsTokenKind.KwLet,
        ["rec"] = FsTokenKind.KwRec,
        ["member"] = FsTokenKind.KwMember,
        ["and"] = FsTokenKind.KwAnd,
        ["open"] = FsTokenKind.KwOpen,
    };

    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private bool _partial;
    private bool _atLineStart = true;

    public FsLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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

            if (_atLineStart)
            {
                // record leading spaces as indent via zero-width? emit Newline already handled.
                // skip pure indent; column is in token start
                while (_c.Peek() is ' ' or '\t')
                    _c.Advance();
                if (_c.Peek() is '\r' or '\n')
                {
                    ConsumeNl(emit: false);
                    continue;
                }
                if (_c.Peek() == '/' && _c.Peek(1) == '/')
                {
                    _c.SkipLineComment();
                    if (_c.Peek() is '\r' or '\n') ConsumeNl(emit: false);
                    continue;
                }
                _atLineStart = false;
            }

            _c.SkipWhitespaceHorizontal();
            if (_c.IsEof) break;

            char ch = _c.Peek();
            if (ch is '\r' or '\n')
            {
                ConsumeNl(emit: true);
                continue;
            }
            if (ch == '/' && _c.Peek(1) == '/')
            {
                _c.SkipLineComment();
                continue;
            }
            if (ch == '(' && _c.Peek(1) == '*')
            {
                // (* ... *)
                int start = _c.Position;
                _c.Advance(2);
                int depth = 1;
                while (!_c.IsEof && depth > 0)
                {
                    if (_c.Peek() == '(' && _c.Peek(1) == '*')
                    {
                        depth++;
                        _c.Advance(2);
                    }
                    else if (_c.Peek() == '*' && _c.Peek(1) == ')')
                    {
                        depth--;
                        _c.Advance(2);
                    }
                    else
                        _c.Advance();
                }
                if (depth != 0) _partial = true;
                _ = start;
                continue;
            }
            if (ch is '"' or '\'')
            {
                int start = _c.Position;
                if (ch == '"')
                {
                    // triple?
                    if (_c.Peek(1) == '"' && _c.Peek(2) == '"')
                    {
                        _c.Advance(3);
                        while (!_c.IsEof)
                        {
                            if (_c.Peek() == '"' && _c.Peek(1) == '"' && _c.Peek(2) == '"')
                            {
                                _c.Advance(3);
                                break;
                            }
                            _c.Advance();
                        }
                    }
                    else
                    {
                        _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
                        if (unclosed) _partial = true;
                    }
                }
                else
                {
                    _c.Advance();
                    while (!_c.IsEof && _c.Peek() != '\'')
                    {
                        if (_c.Peek() == '\\') _c.Advance();
                        if (!_c.IsEof) _c.Advance();
                    }
                    if (_c.Peek() == '\'') _c.Advance();
                }
                Emit(FsTokenKind.StringLiteral, start);
                continue;
            }
            if (LexCursor.IsDigit(ch))
            {
                Emit(FsTokenKind.NumericLiteral, _c.ReadNumber());
                continue;
            }
            if (char.IsLetter(ch) || ch == '_')
            {
                TextSpan span = _c.ReadIdentifier();
                // allow dotted names as separate Dot tokens later; ReadIdentifier is one segment
                string text = _c.Slice(span);
                FsTokenKind kind = Keywords.TryGetValue(text, out FsTokenKind kw) ? kw : FsTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }
            int pstart = _c.Position;
            char p = _c.Advance();
            FsTokenKind pk = p switch
            {
                '=' => FsTokenKind.Eq,
                '(' => FsTokenKind.LParen,
                ')' => FsTokenKind.RParen,
                '{' => FsTokenKind.LBrace,
                '}' => FsTokenKind.RBrace,
                '[' => FsTokenKind.LBracket,
                ']' => FsTokenKind.RBracket,
                ':' => FsTokenKind.Colon,
                '.' => FsTokenKind.Dot,
                _ => FsTokenKind.Other,
            };
            Emit(pk, pstart);
        }
        if (!_atLineStart)
            Emit(FsTokenKind.Newline, new TextSpan(_c.Position, 0));
        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void ConsumeNl(bool emit)
    {
        int start = _c.Position;
        if (_c.Peek() == '\r') _c.Advance();
        if (_c.Peek() == '\n') _c.Advance();
        if (emit) Emit(FsTokenKind.Newline, start);
        _atLineStart = true;
    }

    private void Emit(FsTokenKind kind, TextSpan span, string? text = null)
        => _tokens.Add(new Token((int)kind, span, text));

    private void Emit(FsTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));
}
