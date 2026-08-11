using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Lua;

internal sealed class LuaLexer : IDisposable
{
    private static readonly Dictionary<string, LuaTokenKind> Keywords = new()
    {
        ["function"] = LuaTokenKind.KwFunction,
        ["local"] = LuaTokenKind.KwLocal,
        ["end"] = LuaTokenKind.KwEnd,
        ["if"] = LuaTokenKind.KwIf,
        ["then"] = LuaTokenKind.KwThen,
        ["else"] = LuaTokenKind.KwElse,
        ["elseif"] = LuaTokenKind.KwElseIf,
        ["for"] = LuaTokenKind.KwFor,
        ["while"] = LuaTokenKind.KwWhile,
        ["do"] = LuaTokenKind.KwDo,
        ["repeat"] = LuaTokenKind.KwRepeat,
        ["until"] = LuaTokenKind.KwUntil,
        ["return"] = LuaTokenKind.KwReturn,
        ["break"] = LuaTokenKind.KwBreak,
        ["goto"] = LuaTokenKind.KwGoto,
        ["in"] = LuaTokenKind.KwIn,
        ["nil"] = LuaTokenKind.KwNil,
        ["true"] = LuaTokenKind.KwTrue,
        ["false"] = LuaTokenKind.KwFalse,
        ["and"] = LuaTokenKind.KwAnd,
        ["or"] = LuaTokenKind.KwOr,
        ["not"] = LuaTokenKind.KwNot,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;

    public LuaLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _text = text;
        _c = new LexCursor(text, ct);
        _diagnostics = diagnostics;
    }

    public bool IsPartial => _partial;

    public IReadOnlyList<Token> Tokenize()
    {
        while (!_c.IsEof)
        {
            SkipTrivia();
            if (_c.IsEof)
                break;
            _c.ThrowIfCancellationRequested();

            char ch = _c.Peek();
            int start = _c.Position;

            if (ch == '"' || ch == '\'')
            {
                _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("LUA001", "Unclosed string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(LuaTokenKind.StringLiteral, start);
                continue;
            }

            if (ch == '[')
            {
                int eqCount = 0;
                while (_c.Peek(1 + eqCount) == '=') eqCount++;
                if (eqCount > 0 || _c.Peek(1) == '[')
                {
                    _c.Advance(1 + eqCount + 1);
                    SkipLongString(eqCount);
                    Emit(LuaTokenKind.LongStringLiteral, start);
                    continue;
                }
            }

            if (LexCursor.IsDigit(ch))
            {
                ReadNumber();
                Emit(LuaTokenKind.NumericLiteral, start);
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                _c.ReadIdentifier();
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                LuaTokenKind kind = Keywords.TryGetValue(text, out LuaTokenKind k) ? k : LuaTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            char n = _c.Peek(1);
            LuaTokenKind pk = ch switch
            {
                '(' => ConsumeSingle(LuaTokenKind.LParen),
                ')' => ConsumeSingle(LuaTokenKind.RParen),
                '{' => ConsumeSingle(LuaTokenKind.LBrace),
                '}' => ConsumeSingle(LuaTokenKind.RBrace),
                '[' => ConsumeSingle(LuaTokenKind.LBracket),
                ']' => ConsumeSingle(LuaTokenKind.RBracket),
                ';' => ConsumeSingle(LuaTokenKind.Semicolon),
                ',' => ConsumeSingle(LuaTokenKind.Comma),
                '.' when n == '.' && _c.Peek(2) == '.' => ConsumeMulti(3, LuaTokenKind.DotDotDot),
                '.' when n == '.' => ConsumeMulti(2, LuaTokenKind.DotDot),
                '.' => ConsumeSingle(LuaTokenKind.Period),
                ':' => ConsumeSingle(LuaTokenKind.Colon),
                '=' when n == '=' => ConsumeMulti(2, LuaTokenKind.EqualEqual),
                '=' => ConsumeSingle(LuaTokenKind.Equal),
                '-' when n == '-' => ConsumeMulti(2, LuaTokenKind.MinusMinus),
                '-' => ConsumeSingle(LuaTokenKind.Minus),
                '+' when n == '+' => ConsumeMulti(2, LuaTokenKind.PlusPlus),
                '+' => ConsumeSingle(LuaTokenKind.Plus),
                '*' => ConsumeSingle(LuaTokenKind.Star),
                '/' => ConsumeSingle(LuaTokenKind.Slash),
                '%' => ConsumeSingle(LuaTokenKind.Percent),
                '^' => ConsumeSingle(LuaTokenKind.Caret),
                '~' when n == '=' => ConsumeMulti(2, LuaTokenKind.NotEqual),
                '~' => ConsumeSingle(LuaTokenKind.Tilde),
                '!' => ConsumeSingle(LuaTokenKind.Exclamation),
                '<' when n == '=' => ConsumeMulti(2, LuaTokenKind.LAngleEqual),
                '<' => ConsumeSingle(LuaTokenKind.LAngle),
                '>' when n == '=' => ConsumeMulti(2, LuaTokenKind.RAngleEqual),
                '>' => ConsumeSingle(LuaTokenKind.RAngle),
                '#' => ConsumeSingle(LuaTokenKind.Hash),
                '?' => ConsumeSingle(LuaTokenKind.Question),
                _ => ConsumeSingle(LuaTokenKind.Question),
            };
            Emit(pk, start);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void SkipLongString(int eqCount)
    {
        while (!_c.IsEof)
        {
            if (_c.Peek() == ']')
            {
                bool match = true;
                for (int i = 1; i <= eqCount; i++)
                {
                    if (_c.Peek(i) != '=')
                    { match = false; break; }
                }
                if (match && _c.Peek(1 + eqCount) == ']')
                {
                    _c.Advance(2 + eqCount);
                    return;
                }
            }
            _c.Advance();
        }
        _partial = true;
        _diagnostics.Add(new ParseDiagnostic("LUA001", "Unclosed long string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
    }

    private void SkipTrivia()
    {
        while (!_c.IsEof)
        {
            char ch = _c.Peek();
            if (ch is ' ' or '\t' or '\r' or '\n')
            {
                _c.Advance();
                continue;
            }
            if (ch == '-' && _c.Peek(1) == '-')
            {
                _c.Advance(2);
                if (_c.Peek() == '[')
                {
                    int eqCount = 0;
                    while (_c.Peek(1 + eqCount) == '=') eqCount++;
                    if (eqCount > 0 || _c.Peek(1) == '[')
                    {
                        _c.Advance(1 + eqCount + 1);
                        SkipLongString(eqCount);
                        continue;
                    }
                }
                while (!_c.IsEof && _c.Advance() != '\n') { }
                continue;
            }
            return;
        }
    }

    private void ReadNumber()
    {
        if (_c.Peek() == '0' && _c.Peek(1) is 'x' or 'X')
        {
            _c.Advance(2);
            while (char.IsLetterOrDigit(_c.Peek()) || _c.Peek() == '_')
                _c.Advance();
            return;
        }
        while (char.IsDigit(_c.Peek()) || _c.Peek() == '_')
            _c.Advance();
        if (_c.Peek() == '.')
        {
            if (char.IsDigit(_c.Peek(1)))
            {
                _c.Advance();
                while (char.IsDigit(_c.Peek()) || _c.Peek() == '_')
                    _c.Advance();
            }
        }
    }

    private LuaTokenKind ConsumeSingle(LuaTokenKind kind)
    {
        _c.Advance();
        return kind;
    }

    private LuaTokenKind ConsumeMulti(int count, LuaTokenKind kind)
    {
        _c.Advance(count);
        return kind;
    }

    private void Emit(LuaTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
    }

    private void Emit(LuaTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));

    public void Dispose() { }
}
