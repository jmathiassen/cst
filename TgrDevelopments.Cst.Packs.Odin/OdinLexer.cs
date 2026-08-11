using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Odin;

internal sealed class OdinLexer : IDisposable
{
    private static readonly Dictionary<string, OdinTokenKind> Keywords = new()
    {
        ["package"] = OdinTokenKind.KwPackage,
        ["proc"] = OdinTokenKind.KwProc,
        ["struct"] = OdinTokenKind.KwStruct,
        ["enum"] = OdinTokenKind.KwEnum,
        ["union"] = OdinTokenKind.KwUnion,
        ["import"] = OdinTokenKind.KwImport,
        ["foreign"] = OdinTokenKind.KwForeign,
        ["using"] = OdinTokenKind.KwUsing,
        ["defer"] = OdinTokenKind.KwDefer,
        ["when"] = OdinTokenKind.KwWhen,
        ["if"] = OdinTokenKind.KwIf,
        ["else"] = OdinTokenKind.KwElse,
        ["for"] = OdinTokenKind.KwFor,
        ["return"] = OdinTokenKind.KwReturn,
        ["break"] = OdinTokenKind.KwBreak,
        ["continue"] = OdinTokenKind.KwContinue,
        ["in"] = OdinTokenKind.KwIn,
        ["not"] = OdinTokenKind.KwNot,
        ["or"] = OdinTokenKind.KwOr,
        ["and"] = OdinTokenKind.KwAnd,
        ["true"] = OdinTokenKind.KwTrue,
        ["false"] = OdinTokenKind.KwFalse,
        ["nil"] = OdinTokenKind.KwNil,
        ["make"] = OdinTokenKind.KwMake,
        ["new"] = OdinTokenKind.KwNew,
        ["delete"] = OdinTokenKind.KwDelete,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;

    public OdinLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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

            if (ch == '\'' && TryLexChar(start))
                continue;

            if (TryLexString(ch, start))
                continue;

            if (ch == '`')
            {
                _c.Advance();
                while (!_c.IsEof && _c.Advance() != '`') { }
                if (_c.IsEof && _c.Peek(-1) != '`')
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("ODIN001", "Unclosed raw string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(OdinTokenKind.RawStringLiteral, start);
                continue;
            }

            if (LexCursor.IsDigit(ch))
            {
                ReadNumber();
                Emit(OdinTokenKind.NumericLiteral, start);
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                _c.ReadIdentifier();
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                OdinTokenKind kind = Keywords.TryGetValue(text, out OdinTokenKind k) ? k : OdinTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            char n = _c.Peek(1);
            OdinTokenKind pk = ch switch
            {
                '(' => ConsumeSingle(OdinTokenKind.LParen),
                ')' => ConsumeSingle(OdinTokenKind.RParen),
                '{' => ConsumeSingle(OdinTokenKind.LBrace),
                '}' => ConsumeSingle(OdinTokenKind.RBrace),
                '[' => ConsumeSingle(OdinTokenKind.LBracket),
                ']' => ConsumeSingle(OdinTokenKind.RBracket),
                ';' => ConsumeSingle(OdinTokenKind.Semicolon),
                ',' => ConsumeSingle(OdinTokenKind.Comma),
                '.' when n == '.' => ConsumeMulti(2, OdinTokenKind.DotDot),
                '.' => ConsumeSingle(OdinTokenKind.Period),
                ':' when n == ':' => ConsumeMulti(2, OdinTokenKind.ColonColon),
                ':' => ConsumeSingle(OdinTokenKind.Colon),
                '=' when n == '>' => ConsumeMulti(2, OdinTokenKind.FatArrow),
                '=' => ConsumeSingle(OdinTokenKind.Equal),
                '-' when n == '>' => ConsumeMulti(2, OdinTokenKind.Arrow),
                '-' when n == '-' => ConsumeMulti(2, OdinTokenKind.MinusMinus),
                '-' => ConsumeSingle(OdinTokenKind.Minus),
                '+' when n == '+' => ConsumeMulti(2, OdinTokenKind.PlusPlus),
                '+' => ConsumeSingle(OdinTokenKind.Plus),
                '*' => ConsumeSingle(OdinTokenKind.Star),
                '/' => ConsumeSingle(OdinTokenKind.Slash),
                '%' => ConsumeSingle(OdinTokenKind.Percent),
                '&' => ConsumeSingle(OdinTokenKind.Amp),
                '|' => ConsumeSingle(OdinTokenKind.Pipe),
                '^' => ConsumeSingle(OdinTokenKind.Caret),
                '~' => ConsumeSingle(OdinTokenKind.Tilde),
                '#' => ConsumeSingle(OdinTokenKind.Hash),
                '@' => ConsumeSingle(OdinTokenKind.At),
                '!' => ConsumeSingle(OdinTokenKind.Exclamation),
                '<' => ConsumeSingle(OdinTokenKind.LAngle),
                '>' => ConsumeSingle(OdinTokenKind.RAngle),
                '?' => ConsumeSingle(OdinTokenKind.Question),
                '_' => ConsumeSingle(OdinTokenKind.Underscore),
                _ => ConsumeSingle(OdinTokenKind.Question),
            };
            Emit(pk, start);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private bool TryLexChar(int start)
    {
        _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
        if (unclosed)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("ODIN001", "Unclosed char literal.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
        }
        Emit(OdinTokenKind.CharLiteral, start);
        return true;
    }

    private bool TryLexString(char ch, int start)
    {
        if (ch == '"')
        {
            _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
            if (unclosed)
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("ODIN001", "Unclosed string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
            }
            Emit(OdinTokenKind.StringLiteral, start);
            return true;
        }
        return false;
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

    private OdinTokenKind ConsumeSingle(OdinTokenKind kind)
    {
        _c.Advance();
        return kind;
    }

    private OdinTokenKind ConsumeMulti(int count, OdinTokenKind kind)
    {
        _c.Advance(count);
        return kind;
    }

    private void Emit(OdinTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
    }

    private void Emit(OdinTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));

    public void Dispose() { }
}
