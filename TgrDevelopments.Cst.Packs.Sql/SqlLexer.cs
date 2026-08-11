using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Sql;

internal sealed class SqlLexer
{
    private static readonly Dictionary<string, SqlTokenKind> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CREATE"] = SqlTokenKind.KwCreate,
        ["OR"] = SqlTokenKind.KwOr,
        ["REPLACE"] = SqlTokenKind.KwReplace,
        ["TABLE"] = SqlTokenKind.KwTable,
        ["VIEW"] = SqlTokenKind.KwView,
        ["PROCEDURE"] = SqlTokenKind.KwProcedure,
        ["PROC"] = SqlTokenKind.KwProc,
        ["FUNCTION"] = SqlTokenKind.KwFunction,
        ["TRIGGER"] = SqlTokenKind.KwTrigger,
        ["INDEX"] = SqlTokenKind.KwIndex,
        ["TYPE"] = SqlTokenKind.KwType,
        ["SCHEMA"] = SqlTokenKind.KwSchema,
        ["IF"] = SqlTokenKind.KwIf,
        ["NOT"] = SqlTokenKind.KwNot,
        ["EXISTS"] = SqlTokenKind.KwExists,
        ["GO"] = SqlTokenKind.KwGo,
    };

    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private bool _partial;

    public SqlLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
            if (ch == '-' && _c.Peek(1) == '-')
            {
                _c.Advance(2);
                _c.SkipUntilNewline();
                continue;
            }
            if (ch == '/' && _c.Peek(1) == '*')
            {
                _c.SkipBlockComment(out bool unclosed);
                if (unclosed) _partial = true;
                continue;
            }
            if (ch is '\'' or '"')
            {
                int start = _c.Position;
                char q = ch;
                _c.Advance();
                while (!_c.IsEof)
                {
                    if (_c.Peek() == q && _c.Peek(1) == q)
                    {
                        _c.Advance(2);
                        continue;
                    }
                    if (_c.Peek() == q)
                    {
                        _c.Advance();
                        break;
                    }
                    _c.Advance();
                }
                Emit(SqlTokenKind.StringLiteral, start);
                continue;
            }
            if (ch == '[')
            {
                int start = _c.Position;
                _c.Advance();
                while (!_c.IsEof && _c.Peek() != ']')
                    _c.Advance();
                if (_c.Peek() == ']') _c.Advance();
                string text = _c.Slice(new TextSpan(start, _c.Position - start));
                Emit(SqlTokenKind.Identifier, start, text.Trim('[', ']'));
                continue;
            }
            if (LexCursor.IsDigit(ch))
            {
                Emit(SqlTokenKind.NumericLiteral, _c.ReadNumber());
                continue;
            }
            if (char.IsLetter(ch) || ch == '_' || ch == '@' || ch == '#')
            {
                int start = _c.Position;
                while (!_c.IsEof)
                {
                    char c = _c.Peek();
                    if (char.IsLetterOrDigit(c) || c is '_' or '@' or '#' or '$')
                        _c.Advance();
                    else
                        break;
                }
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                SqlTokenKind kind = Keywords.TryGetValue(text, out SqlTokenKind kw) ? kw : SqlTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }
            int pstart = _c.Position;
            char p = _c.Advance();
            SqlTokenKind pk = p switch
            {
                '(' => SqlTokenKind.LParen,
                ')' => SqlTokenKind.RParen,
                '{' => SqlTokenKind.LBrace,
                '}' => SqlTokenKind.RBrace,
                '[' => SqlTokenKind.LBracket,
                ']' => SqlTokenKind.RBracket,
                ';' => SqlTokenKind.Semicolon,
                ',' => SqlTokenKind.Comma,
                '.' => SqlTokenKind.Dot,
                _ => SqlTokenKind.Other,
            };
            Emit(pk, pstart);
        }
        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void Emit(SqlTokenKind kind, TextSpan span, string? text = null)
        => _tokens.Add(new Token((int)kind, span, text));

    private void Emit(SqlTokenKind kind, int start, string? text = null)
        => Emit(kind, new TextSpan(start, _c.Position - start), text);
}
