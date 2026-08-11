using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Swift;

internal sealed class SwiftLexer : IDisposable
{
    private static readonly Dictionary<string, SwiftTokenKind> Keywords = new()
    {
        ["class"] = SwiftTokenKind.KwClass,
        ["struct"] = SwiftTokenKind.KwStruct,
        ["enum"] = SwiftTokenKind.KwEnum,
        ["protocol"] = SwiftTokenKind.KwProtocol,
        ["extension"] = SwiftTokenKind.KwExtension,
        ["func"] = SwiftTokenKind.KwFunc,
        ["init"] = SwiftTokenKind.KwInit,
        ["deinit"] = SwiftTokenKind.KwDeinit,
        ["let"] = SwiftTokenKind.KwLet,
        ["var"] = SwiftTokenKind.KwVar,
        ["public"] = SwiftTokenKind.KwPublic,
        ["private"] = SwiftTokenKind.KwPrivate,
        ["internal"] = SwiftTokenKind.KwInternal,
        ["fileprivate"] = SwiftTokenKind.KwFilePrivate,
        ["open"] = SwiftTokenKind.KwOpen,
        ["static"] = SwiftTokenKind.KwStatic,
        ["override"] = SwiftTokenKind.KwOverride,
        ["throws"] = SwiftTokenKind.KwThrows,
        ["rethrows"] = SwiftTokenKind.KwRethrows,
        ["async"] = SwiftTokenKind.KwAsync,
        ["await"] = SwiftTokenKind.KwAwait,
        ["inout"] = SwiftTokenKind.KwInOut,
        ["mutating"] = SwiftTokenKind.KwMutating,
        ["nonmutating"] = SwiftTokenKind.KwNonMutating,
        ["convenience"] = SwiftTokenKind.KwConvenience,
        ["required"] = SwiftTokenKind.KwRequired,
        ["weak"] = SwiftTokenKind.KwWeak,
        ["unowned"] = SwiftTokenKind.KwUnowned,
        ["lazy"] = SwiftTokenKind.KwLazy,
        ["dynamic"] = SwiftTokenKind.KwDynamic,
        ["final"] = SwiftTokenKind.KwFinal,
        ["return"] = SwiftTokenKind.KwReturn,
        ["if"] = SwiftTokenKind.KwIf,
        ["else"] = SwiftTokenKind.KwElse,
        ["for"] = SwiftTokenKind.KwFor,
        ["while"] = SwiftTokenKind.KwWhile,
        ["do"] = SwiftTokenKind.KwDo,
        ["switch"] = SwiftTokenKind.KwSwitch,
        ["case"] = SwiftTokenKind.KwCase,
        ["break"] = SwiftTokenKind.KwBreak,
        ["continue"] = SwiftTokenKind.KwContinue,
        ["try"] = SwiftTokenKind.KwTry,
        ["catch"] = SwiftTokenKind.KwCatch,
        ["throw"] = SwiftTokenKind.KwThrow,
        ["nil"] = SwiftTokenKind.KwNil,
        ["true"] = SwiftTokenKind.KwTrue,
        ["false"] = SwiftTokenKind.KwFalse,
        ["self"] = SwiftTokenKind.KwSelf,
        ["super"] = SwiftTokenKind.KwSuper,
        ["typealias"] = SwiftTokenKind.KwTypeAlias,
        ["where"] = SwiftTokenKind.KwWhere,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;

    public SwiftLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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

            if (ch == '"' && _c.Peek(1) == '"' && _c.Peek(2) == '"')
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
                if (_c.IsEof)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("SWIFT001", "Unclosed multi-line string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(SwiftTokenKind.MultiLineStringLiteral, start);
                continue;
            }

            if (ch == '"')
            {
                _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("SWIFT001", "Unclosed string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(SwiftTokenKind.StringLiteral, start);
                continue;
            }

            if (ch == '`')
            {
                _c.Advance();
                if (!_c.IsEof && (char.IsLetter(_c.Peek()) || _c.Peek() == '_'))
                {
                    _c.ReadIdentifier();
                    Emit(SwiftTokenKind.Identifier, start);
                }
                if (!_c.IsEof && _c.Peek() == '`')
                    _c.Advance();
                continue;
            }

            if (LexCursor.IsDigit(ch))
            {
                ReadNumber();
                Emit(SwiftTokenKind.NumericLiteral, start);
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                _c.ReadIdentifier();
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                SwiftTokenKind kind = Keywords.TryGetValue(text, out SwiftTokenKind k) ? k : SwiftTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            char n = _c.Peek(1);
            SwiftTokenKind pk = ch switch
            {
                '(' => ConsumeSingle(SwiftTokenKind.LParen),
                ')' => ConsumeSingle(SwiftTokenKind.RParen),
                '{' => ConsumeSingle(SwiftTokenKind.LBrace),
                '}' => ConsumeSingle(SwiftTokenKind.RBrace),
                '[' => ConsumeSingle(SwiftTokenKind.LBracket),
                ']' => ConsumeSingle(SwiftTokenKind.RBracket),
                ';' => ConsumeSingle(SwiftTokenKind.Semicolon),
                ',' => ConsumeSingle(SwiftTokenKind.Comma),
                '.' => ConsumeSingle(SwiftTokenKind.Period),
                ':' when n == ':' => ConsumeMulti(2, SwiftTokenKind.ColonColon),
                ':' => ConsumeSingle(SwiftTokenKind.Colon),
                '=' when n == '=' => ConsumeMulti(2, SwiftTokenKind.EqualEqual),
                '=' => ConsumeSingle(SwiftTokenKind.Equal),
                '-' when n == '>' => ConsumeMulti(2, SwiftTokenKind.Arrow),
                '-' when n == '-' => ConsumeMulti(2, SwiftTokenKind.MinusMinus),
                '-' => ConsumeSingle(SwiftTokenKind.Minus),
                '+' when n == '+' => ConsumeMulti(2, SwiftTokenKind.PlusPlus),
                '+' => ConsumeSingle(SwiftTokenKind.Plus),
                '*' => ConsumeSingle(SwiftTokenKind.Star),
                '/' => ConsumeSingle(SwiftTokenKind.Slash),
                '%' => ConsumeSingle(SwiftTokenKind.Percent),
                '&' when n == '&' => ConsumeMulti(2, SwiftTokenKind.AmpAmp),
                '&' => ConsumeSingle(SwiftTokenKind.Amp),
                '|' when n == '|' => ConsumeMulti(2, SwiftTokenKind.PipePipe),
                '|' => ConsumeSingle(SwiftTokenKind.Pipe),
                '^' => ConsumeSingle(SwiftTokenKind.Caret),
                '~' => ConsumeSingle(SwiftTokenKind.Tilde),
                '!' when n == '=' => ConsumeMulti(2, SwiftTokenKind.NotEqual),
                '!' => ConsumeSingle(SwiftTokenKind.Exclamation),
                '<' when n == '=' => ConsumeMulti(2, SwiftTokenKind.LAngleEqual),
                '<' => ConsumeSingle(SwiftTokenKind.LAngle),
                '>' when n == '=' => ConsumeMulti(2, SwiftTokenKind.RAngleEqual),
                '>' => ConsumeSingle(SwiftTokenKind.RAngle),
                '@' => ConsumeSingle(SwiftTokenKind.At),
                '?' when n == '?' => ConsumeMulti(2, SwiftTokenKind.QuestionQuestion),
                '?' => ConsumeSingle(SwiftTokenKind.Question),
                '#' => ConsumeSingle(SwiftTokenKind.Hash),
                _ => ConsumeSingle(SwiftTokenKind.Question),
            };
            Emit(pk, start);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
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
                    SkipBlockCommentNested();
                continue;
            }
            return;
        }
    }

    private void SkipBlockCommentNested()
    {
        int depth = 1;
        _c.Advance(2);
        while (!_c.IsEof && depth > 0)
        {
            _c.ThrowIfCancellationRequested();
            if (_c.TryMatch("/*"))
            { depth++; continue; }
            if (_c.TryMatch("*/"))
            { depth--; continue; }
            _c.Advance();
        }
        if (depth > 0)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("SWIFT001", "Unclosed block comment.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
        }
    }

    private SwiftTokenKind ConsumeSingle(SwiftTokenKind kind)
    {
        _c.Advance();
        return kind;
    }

    private SwiftTokenKind ConsumeMulti(int count, SwiftTokenKind kind)
    {
        _c.Advance(count);
        return kind;
    }

    private void Emit(SwiftTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
    }

    private void Emit(SwiftTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));

    public void Dispose() { }
}
