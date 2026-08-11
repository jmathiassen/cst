using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Dart;

internal sealed class DartLexer : IDisposable
{
    private static readonly Dictionary<string, DartTokenKind> Keywords = new()
    {
        ["class"] = DartTokenKind.KwClass,
        ["mixin"] = DartTokenKind.KwMixin,
        ["enum"] = DartTokenKind.KwEnum,
        ["extends"] = DartTokenKind.KwExtends,
        ["with"] = DartTokenKind.KwWith,
        ["implements"] = DartTokenKind.KwImplements,
        ["abstract"] = DartTokenKind.KwAbstract,
        ["base"] = DartTokenKind.KwBase,
        ["sealed"] = DartTokenKind.KwSealed,
        ["final"] = DartTokenKind.KwFinal,
        ["const"] = DartTokenKind.KwConst,
        ["factory"] = DartTokenKind.KwFactory,
        ["new"] = DartTokenKind.KwNew,
        ["return"] = DartTokenKind.KwReturn,
        ["if"] = DartTokenKind.KwIf,
        ["else"] = DartTokenKind.KwElse,
        ["for"] = DartTokenKind.KwFor,
        ["while"] = DartTokenKind.KwWhile,
        ["do"] = DartTokenKind.KwDo,
        ["switch"] = DartTokenKind.KwSwitch,
        ["case"] = DartTokenKind.KwCase,
        ["default"] = DartTokenKind.KwDefault,
        ["break"] = DartTokenKind.KwBreak,
        ["continue"] = DartTokenKind.KwContinue,
        ["try"] = DartTokenKind.KwTry,
        ["catch"] = DartTokenKind.KwCatch,
        ["finally"] = DartTokenKind.KwFinally,
        ["throw"] = DartTokenKind.KwThrow,
        ["rethrow"] = DartTokenKind.KwRethrow,
        ["var"] = DartTokenKind.KwVar,
        ["late"] = DartTokenKind.KwLate,
        ["required"] = DartTokenKind.KwRequired,
        ["void"] = DartTokenKind.KwVoid,
        ["true"] = DartTokenKind.KwTrue,
        ["false"] = DartTokenKind.KwFalse,
        ["null"] = DartTokenKind.KwNull,
        ["is"] = DartTokenKind.KwIs,
        ["as"] = DartTokenKind.KwAs,
        ["show"] = DartTokenKind.KwShow,
        ["hide"] = DartTokenKind.KwHide,
        ["export"] = DartTokenKind.KwExport,
        ["library"] = DartTokenKind.KwLibrary,
        ["part"] = DartTokenKind.KwPart,
        ["import"] = DartTokenKind.KwImport,
        ["typedef"] = DartTokenKind.KwTypedef,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;

    public DartLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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

            if (ch == 'r' && _c.Peek(1) is '"' or '\'' or '#')
            {
                _c.Advance();
                if (_c.Peek() == '"' && _c.Peek(1) == '"' && _c.Peek(2) == '"')
                {
                    _c.Advance(3);
                    SkipTripleString();
                    Emit(DartTokenKind.RawStringLiteral, start);
                }
                else if (_c.Peek() == '\'' && _c.Peek(1) == '\'' && _c.Peek(2) == '\'')
                {
                    _c.Advance(3);
                    SkipTripleString();
                    Emit(DartTokenKind.RawStringLiteral, start);
                }
                else
                {
                    _c.ReadQuotedString(out _, allowMultiline: false);
                    Emit(DartTokenKind.RawStringLiteral, start);
                }
                continue;
            }

            if ((ch == '"' && _c.Peek(1) == '"' && _c.Peek(2) == '"')
                || (ch == '\'' && _c.Peek(1) == '\'' && _c.Peek(2) == '\''))
            {
                _c.Advance(3);
                SkipTripleString();
                Emit(DartTokenKind.MultiLineStringLiteral, start);
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("DART001", "Unclosed string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(DartTokenKind.StringLiteral, start);
                continue;
            }

            if (LexCursor.IsDigit(ch))
            {
                ReadNumber();
                Emit(DartTokenKind.NumericLiteral, start);
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                _c.ReadIdentifier();
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                DartTokenKind kind = Keywords.TryGetValue(text, out DartTokenKind k) ? k : DartTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            char n = _c.Peek(1);
            DartTokenKind pk = ch switch
            {
                '(' => ConsumeSingle(DartTokenKind.LParen),
                ')' => ConsumeSingle(DartTokenKind.RParen),
                '{' => ConsumeSingle(DartTokenKind.LBrace),
                '}' => ConsumeSingle(DartTokenKind.RBrace),
                '[' => ConsumeSingle(DartTokenKind.LBracket),
                ']' => ConsumeSingle(DartTokenKind.RBracket),
                ';' => ConsumeSingle(DartTokenKind.Semicolon),
                ',' => ConsumeSingle(DartTokenKind.Comma),
                '.' when n == '.' && _c.Peek(2) == '?' => ConsumeMulti(3, DartTokenKind.DotDotQuestion),
                '.' when n == '.' => ConsumeMulti(2, DartTokenKind.DotDot),
                '.' => ConsumeSingle(DartTokenKind.Period),
                ':' when n == ':' => ConsumeMulti(2, DartTokenKind.ColonColon),
                ':' => ConsumeSingle(DartTokenKind.Colon),
                '=' when n == '=' => ConsumeMulti(2, DartTokenKind.EqualEqual),
                '=' => ConsumeSingle(DartTokenKind.Equal),
                '-' when n == '>' => ConsumeMulti(2, DartTokenKind.Arrow),
                '-' when n == '-' => ConsumeMulti(2, DartTokenKind.MinusMinus),
                '-' => ConsumeSingle(DartTokenKind.Minus),
                '+' when n == '+' => ConsumeMulti(2, DartTokenKind.PlusPlus),
                '+' => ConsumeSingle(DartTokenKind.Plus),
                '*' => ConsumeSingle(DartTokenKind.Star),
                '/' => ConsumeSingle(DartTokenKind.Slash),
                '%' => ConsumeSingle(DartTokenKind.Percent),
                '&' when n == '&' => ConsumeMulti(2, DartTokenKind.AmpAmp),
                '&' => ConsumeSingle(DartTokenKind.Amp),
                '|' when n == '|' => ConsumeMulti(2, DartTokenKind.PipePipe),
                '|' => ConsumeSingle(DartTokenKind.Pipe),
                '^' => ConsumeSingle(DartTokenKind.Caret),
                '~' => ConsumeSingle(DartTokenKind.Tilde),
                '!' when n == '=' => ConsumeMulti(2, DartTokenKind.NotEqual),
                '!' => ConsumeSingle(DartTokenKind.Exclamation),
                '<' when n == '=' => ConsumeMulti(2, DartTokenKind.LAngleEqual),
                '<' => ConsumeSingle(DartTokenKind.LAngle),
                '>' when n == '=' => ConsumeMulti(2, DartTokenKind.RAngleEqual),
                '>' => ConsumeSingle(DartTokenKind.RAngle),
                '@' => ConsumeSingle(DartTokenKind.At),
                '?' when n == '?' => ConsumeMulti(2, DartTokenKind.QuestionQuestion),
                '?' => ConsumeSingle(DartTokenKind.Question),
                '#' => ConsumeSingle(DartTokenKind.Hash),
                _ => ConsumeSingle(DartTokenKind.Question),
            };
            Emit(pk, start);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void SkipTripleString()
    {
        while (!_c.IsEof)
        {
            if ((_c.Peek() == '"' && _c.Peek(1) == '"' && _c.Peek(2) == '"')
                || (_c.Peek() == '\'' && _c.Peek(1) == '\'' && _c.Peek(2) == '\''))
            {
                _c.Advance(3);
                return;
            }
            _c.Advance();
        }
        _partial = true;
        _diagnostics.Add(new ParseDiagnostic("DART001", "Unclosed multi-line string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
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

    private DartTokenKind ConsumeSingle(DartTokenKind kind)
    {
        _c.Advance();
        return kind;
    }

    private DartTokenKind ConsumeMulti(int count, DartTokenKind kind)
    {
        _c.Advance(count);
        return kind;
    }

    private void Emit(DartTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
    }

    private void Emit(DartTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));

    public void Dispose() { }
}
