using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Ruby;

internal sealed class RubyLexer : IDisposable
{
    private static readonly Dictionary<string, RubyTokenKind> Keywords = new()
    {
        ["module"] = RubyTokenKind.KwModule,
        ["class"] = RubyTokenKind.KwClass,
        ["def"] = RubyTokenKind.KwDef,
        ["end"] = RubyTokenKind.KwEnd,
        ["self"] = RubyTokenKind.KwSelf,
        ["super"] = RubyTokenKind.KwSuper,
        ["if"] = RubyTokenKind.KwIf,
        ["then"] = RubyTokenKind.KwThen,
        ["else"] = RubyTokenKind.KwElse,
        ["elsif"] = RubyTokenKind.KwElseIf,
        ["unless"] = RubyTokenKind.KwUnless,
        ["while"] = RubyTokenKind.KwWhile,
        ["until"] = RubyTokenKind.KwUntil,
        ["for"] = RubyTokenKind.KwFor,
        ["in"] = RubyTokenKind.KwIn,
        ["do"] = RubyTokenKind.KwDo,
        ["next"] = RubyTokenKind.KwNext,
        ["break"] = RubyTokenKind.KwBreak,
        ["redo"] = RubyTokenKind.KwRedo,
        ["retry"] = RubyTokenKind.KwRetry,
        ["return"] = RubyTokenKind.KwReturn,
        ["yield"] = RubyTokenKind.KwYield,
        ["nil"] = RubyTokenKind.KwNil,
        ["true"] = RubyTokenKind.KwTrue,
        ["false"] = RubyTokenKind.KwFalse,
        ["and"] = RubyTokenKind.KwAnd,
        ["or"] = RubyTokenKind.KwOr,
        ["not"] = RubyTokenKind.KwNot,
        ["rescue"] = RubyTokenKind.KwRescue,
        ["ensure"] = RubyTokenKind.KwEnsure,
        ["begin"] = RubyTokenKind.KwBegin,
        ["case"] = RubyTokenKind.KwCase,
        ["when"] = RubyTokenKind.KwWhen,
        ["alias"] = RubyTokenKind.KwAlias,
        ["undef"] = RubyTokenKind.KwUndef,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;
    private bool _atLineStart = true;

    public RubyLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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

            if (_atLineStart && ch == '=' && StartsWith("=begin"))
            {
                SkipBeginEnd();
                continue;
            }
            _atLineStart = false;

            if (ch == '#' || (_atLineStart && ch == '=' && StartsWith("=end")))
            {
                while (!_c.IsEof && _c.Advance() != '\n') { }
                _atLineStart = true;
                continue;
            }

            if (ch == '%' && TryLexPercentLiteral(start))
                continue;

            if (ch == '"' || ch == '\'')
            {
                _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("RUBY001", "Unclosed string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(RubyTokenKind.StringLiteral, start);
                continue;
            }

            if (ch == ':')
            {
                if (char.IsLetter(_c.Peek(1)))
                {
                    _c.Advance();
                    _c.ReadIdentifier();
                    Emit(RubyTokenKind.SymbolLiteral, start);
                    continue;
                }
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                _c.ReadIdentifier();
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                if (_c.Peek() is '?' or '!' or '=')
                    text += _c.Advance();
                RubyTokenKind kind = Keywords.TryGetValue(text, out RubyTokenKind k) ? k : RubyTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            if (LexCursor.IsDigit(ch))
            {
                ReadNumber();
                Emit(RubyTokenKind.NumericLiteral, start);
                continue;
            }

            char n = _c.Peek(1);
            RubyTokenKind pk = ch switch
            {
                '(' => ConsumeSingle(RubyTokenKind.LParen),
                ')' => ConsumeSingle(RubyTokenKind.RParen),
                '{' => ConsumeSingle(RubyTokenKind.LBrace),
                '}' => ConsumeSingle(RubyTokenKind.RBrace),
                '[' => ConsumeSingle(RubyTokenKind.LBracket),
                ']' => ConsumeSingle(RubyTokenKind.RBracket),
                ';' => ConsumeSingle(RubyTokenKind.Semicolon),
                ',' => ConsumeSingle(RubyTokenKind.Comma),
                '.' when n == '.' && _c.Peek(2) == '.' => ConsumeMulti(3, RubyTokenKind.DotDotDot),
                '.' when n == '.' => ConsumeMulti(2, RubyTokenKind.DotDot),
                '.' => ConsumeSingle(RubyTokenKind.Period),
                ':' when n == ':' => ConsumeMulti(2, RubyTokenKind.ColonColon),
                ':' => ConsumeSingle(RubyTokenKind.Colon),
                '=' when n == '=' => ConsumeMulti(2, RubyTokenKind.EqualEqual),
                '=' => ConsumeSingle(RubyTokenKind.Equal),
                '-' when n == '>' => ConsumeMulti(2, RubyTokenKind.Arrow),
                '-' when n == '-' => ConsumeMulti(2, RubyTokenKind.MinusMinus),
                '-' => ConsumeSingle(RubyTokenKind.Minus),
                '+' when n == '+' => ConsumeMulti(2, RubyTokenKind.PlusPlus),
                '+' => ConsumeSingle(RubyTokenKind.Plus),
                '*' => ConsumeSingle(RubyTokenKind.Star),
                '/' => ConsumeSingle(RubyTokenKind.Slash),
                '%' => ConsumeSingle(RubyTokenKind.Percent),
                '&' when n == '&' => ConsumeMulti(2, RubyTokenKind.AmpAmp),
                '&' => ConsumeSingle(RubyTokenKind.Amp),
                '|' when n == '|' => ConsumeMulti(2, RubyTokenKind.PipePipe),
                '|' => ConsumeSingle(RubyTokenKind.Pipe),
                '^' => ConsumeSingle(RubyTokenKind.Caret),
                '~' => ConsumeSingle(RubyTokenKind.Tilde),
                '!' when n == '=' => ConsumeMulti(2, RubyTokenKind.NotEqual),
                '!' => ConsumeSingle(RubyTokenKind.Exclamation),
                '<' when n == '=' => ConsumeMulti(2, RubyTokenKind.LAngleEqual),
                '<' => ConsumeSingle(RubyTokenKind.LAngle),
                '>' when n == '=' => ConsumeMulti(2, RubyTokenKind.RAngleEqual),
                '>' => ConsumeSingle(RubyTokenKind.RAngle),
                '@' => ConsumeSingle(RubyTokenKind.At),
                '?' => ConsumeSingle(RubyTokenKind.Question),
                '#' => ConsumeSingle(RubyTokenKind.Hash),
                _ => ConsumeSingle(RubyTokenKind.Question),
            };
            Emit(pk, start);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private bool StartsWith(string prefix)
    {
        for (int i = 0; i < prefix.Length; i++)
            if (_c.Peek(i) != prefix[i]) return false;
        return true;
    }

    private void SkipBeginEnd()
    {
        while (!_c.IsEof)
        {
            if (_atLineStart && StartsWith("=end"))
            {
                _c.Advance(4);
                _atLineStart = false;
                return;
            }
            char c = _c.Advance();
            if (c == '\n') _atLineStart = true;
        }
    }

    private bool TryLexPercentLiteral(int start)
    {
        _c.Advance();
        char c = _c.Peek();
        if (char.IsLetter(c))
        {
            _c.Advance();
            c = _c.Peek();
        }
        char expected = c switch
        {
            '(' => ')',
            '{' => '}',
            '[' => ']',
            '<' => '>',
            _ => c,
        };
        _c.Advance();
        while (!_c.IsEof)
        {
            if (_c.Peek() == expected)
            {
                _c.Advance();
                Emit(RubyTokenKind.StringLiteral, start);
                return true;
            }
            _c.Advance();
        }
        _partial = true;
        _diagnostics.Add(new ParseDiagnostic("RUBY001", "Unclosed percent literal.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
        return true;
    }

    private void ReadNumber()
    {
        if (_c.Peek() == '0' && _c.Peek(1) is 'x' or 'X' or 'b' or 'B' or 'o' or 'O')
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
            if (ch is ' ' or '\t' or '\r')
            {
                _c.Advance();
                continue;
            }
            if (ch == '\n')
            {
                _c.Advance();
                _atLineStart = true;
                continue;
            }
            break;
        }
    }

    private RubyTokenKind ConsumeSingle(RubyTokenKind kind)
    {
        _c.Advance();
        return kind;
    }

    private RubyTokenKind ConsumeMulti(int count, RubyTokenKind kind)
    {
        _c.Advance(count);
        return kind;
    }

    private void Emit(RubyTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
    }

    private void Emit(RubyTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));

    public void Dispose() { }
}
