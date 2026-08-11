using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Zig;

internal sealed class ZigLexer : IDisposable
{
    private static readonly Dictionary<string, ZigTokenKind> Keywords = new()
    {
        ["fn"] = ZigTokenKind.KwFn,
        ["pub"] = ZigTokenKind.KwPub,
        ["export"] = ZigTokenKind.KwExport,
        ["const"] = ZigTokenKind.KwConst,
        ["var"] = ZigTokenKind.KwVar,
        ["struct"] = ZigTokenKind.KwStruct,
        ["enum"] = ZigTokenKind.KwEnum,
        ["union"] = ZigTokenKind.KwUnion,
        ["test"] = ZigTokenKind.KwTest,
        ["comptime"] = ZigTokenKind.KwComptime,
        ["usingnamespace"] = ZigTokenKind.KwUsingNamespace,
        ["return"] = ZigTokenKind.KwReturn,
        ["if"] = ZigTokenKind.KwIf,
        ["else"] = ZigTokenKind.KwElse,
        ["for"] = ZigTokenKind.KwFor,
        ["while"] = ZigTokenKind.KwWhile,
        ["break"] = ZigTokenKind.KwBreak,
        ["continue"] = ZigTokenKind.KwContinue,
        ["defer"] = ZigTokenKind.KwDefer,
        ["errdefer"] = ZigTokenKind.KwErrDefer,
        ["unreachable"] = ZigTokenKind.KwUnreachable,
        ["void"] = ZigTokenKind.KwVoid,
        ["null"] = ZigTokenKind.KwNull,
        ["true"] = ZigTokenKind.KwTrue,
        ["false"] = ZigTokenKind.KwFalse,
        ["anyerror"] = ZigTokenKind.KwAnyError,
        ["anytype"] = ZigTokenKind.KwAnyType,
        ["type"] = ZigTokenKind.KwType,
        ["noalias"] = ZigTokenKind.KwNoAlias,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;

    public ZigLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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

            if (ch == '"')
            {
                _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("ZIG001", "Unclosed string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(ZigTokenKind.StringLiteral, start);
                continue;
            }

            if (ch == '\'')
            {
                _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("ZIG001", "Unclosed char literal.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(ZigTokenKind.CharLiteral, start);
                continue;
            }

            if (LexCursor.IsDigit(ch))
            {
                ReadNumber();
                Emit(ZigTokenKind.NumericLiteral, start);
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                _c.ReadIdentifier();
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                ZigTokenKind kind = Keywords.TryGetValue(text, out ZigTokenKind k) ? k : ZigTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            if (ch == '@')
            {
                _c.Advance();
                if (!_c.IsEof && (char.IsLetter(_c.Peek()) || _c.Peek() == '_'))
                {
                    _c.ReadIdentifier();
                    Emit(ZigTokenKind.Identifier, start);
                }
                else
                {
                    Emit(ZigTokenKind.At, start);
                }
                continue;
            }

            char n = _c.Peek(1);
            ZigTokenKind pk = ch switch
            {
                '(' => ConsumeSingle(ZigTokenKind.LParen),
                ')' => ConsumeSingle(ZigTokenKind.RParen),
                '{' => ConsumeSingle(ZigTokenKind.LBrace),
                '}' => ConsumeSingle(ZigTokenKind.RBrace),
                '[' => ConsumeSingle(ZigTokenKind.LBracket),
                ']' => ConsumeSingle(ZigTokenKind.RBracket),
                ';' => ConsumeSingle(ZigTokenKind.Semicolon),
                ',' => ConsumeSingle(ZigTokenKind.Comma),
                '.' => ConsumeSingle(ZigTokenKind.Period),
                ':' => ConsumeSingle(ZigTokenKind.Colon),
                '=' when n == '=' => ConsumeMulti(2, ZigTokenKind.EqualEqual),
                '=' => ConsumeSingle(ZigTokenKind.Equal),
                '-' when n == '>' => ConsumeMulti(2, ZigTokenKind.Arrow),
                '-' when n == '-' => ConsumeMulti(2, ZigTokenKind.MinusMinus),
                '-' => ConsumeSingle(ZigTokenKind.Minus),
                '+' when n == '+' => ConsumeMulti(2, ZigTokenKind.PlusPlus),
                '+' => ConsumeSingle(ZigTokenKind.Plus),
                '*' => ConsumeSingle(ZigTokenKind.Star),
                '/' => ConsumeSingle(ZigTokenKind.Slash),
                '%' => ConsumeSingle(ZigTokenKind.Percent),
                '&' when n == '&' => ConsumeMulti(2, ZigTokenKind.AmpAmp),
                '&' => ConsumeSingle(ZigTokenKind.Amp),
                '|' when n == '|' => ConsumeMulti(2, ZigTokenKind.PipePipe),
                '|' => ConsumeSingle(ZigTokenKind.Pipe),
                '^' => ConsumeSingle(ZigTokenKind.Caret),
                '~' => ConsumeSingle(ZigTokenKind.Tilde),
                '!' when n == '=' => ConsumeMulti(2, ZigTokenKind.NotEqual),
                '!' => ConsumeSingle(ZigTokenKind.Exclamation),
                '<' when n == '=' => ConsumeMulti(2, ZigTokenKind.LAngleEqual),
                '<' => ConsumeSingle(ZigTokenKind.LAngle),
                '>' when n == '=' => ConsumeMulti(2, ZigTokenKind.RAngleEqual),
                '>' => ConsumeSingle(ZigTokenKind.RAngle),
                '?' => ConsumeSingle(ZigTokenKind.Question),
                '_' => ConsumeSingle(ZigTokenKind.Underscore),
                _ => ConsumeSingle(ZigTokenKind.Exclamation),
            };
            Emit(pk, start);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void ReadNumber()
    {
        if (_c.Peek() == '0' && _c.Peek(1) is 'x' or 'X' or 'o' or 'O' or 'b' or 'B')
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
            if (ch == '/' && _c.Peek(1) is '/' or '*')
            {
                if (_c.Peek(1) == '/')
                    _c.SkipLineComment();
                else
                    _c.SkipBlockComment(out _);
                continue;
            }
            return;
        }
    }

    private ZigTokenKind ConsumeSingle(ZigTokenKind kind)
    {
        _c.Advance();
        return kind;
    }

    private ZigTokenKind ConsumeMulti(int count, ZigTokenKind kind)
    {
        _c.Advance(count);
        return kind;
    }

    private void Emit(ZigTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
    }

    private void Emit(ZigTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));

    public void Dispose() { }
}
