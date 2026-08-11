using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Kotlin;

internal sealed class KotlinLexer : IDisposable
{
    private static readonly Dictionary<string, KotlinTokenKind> Keywords = new()
    {
        ["package"] = KotlinTokenKind.KwPackage,
        ["import"] = KotlinTokenKind.KwImport,
        ["class"] = KotlinTokenKind.KwClass,
        ["interface"] = KotlinTokenKind.KwInterface,
        ["object"] = KotlinTokenKind.KwObject,
        ["enum"] = KotlinTokenKind.KwEnum,
        ["fun"] = KotlinTokenKind.KwFun,
        ["val"] = KotlinTokenKind.KwVal,
        ["var"] = KotlinTokenKind.KwVar,
        ["data"] = KotlinTokenKind.KwData,
        ["sealed"] = KotlinTokenKind.KwSealed,
        ["open"] = KotlinTokenKind.KwOpen,
        ["abstract"] = KotlinTokenKind.KwAbstract,
        ["override"] = KotlinTokenKind.KwOverride,
        ["private"] = KotlinTokenKind.KwPrivate,
        ["protected"] = KotlinTokenKind.KwProtected,
        ["internal"] = KotlinTokenKind.KwInternal,
        ["public"] = KotlinTokenKind.KwPublic,
        ["companion"] = KotlinTokenKind.KwCompanion,
        ["init"] = KotlinTokenKind.KwInit,
        ["constructor"] = KotlinTokenKind.KwConstructor,
        ["by"] = KotlinTokenKind.KwBy,
        ["lazy"] = KotlinTokenKind.KwLazy,
        ["lateinit"] = KotlinTokenKind.KwLateInit,
        ["inline"] = KotlinTokenKind.KwInline,
        ["suspend"] = KotlinTokenKind.KwSuspend,
        ["operator"] = KotlinTokenKind.KwOperator,
        ["infix"] = KotlinTokenKind.KwInfix,
        ["tailrec"] = KotlinTokenKind.KwTailRec,
        ["external"] = KotlinTokenKind.KwExternal,
        ["annotation"] = KotlinTokenKind.KwAnnotation,
        ["inner"] = KotlinTokenKind.KwInner,
        ["return"] = KotlinTokenKind.KwReturn,
        ["if"] = KotlinTokenKind.KwIf,
        ["else"] = KotlinTokenKind.KwElse,
        ["for"] = KotlinTokenKind.KwFor,
        ["while"] = KotlinTokenKind.KwWhile,
        ["when"] = KotlinTokenKind.KwWhen,
        ["break"] = KotlinTokenKind.KwBreak,
        ["continue"] = KotlinTokenKind.KwContinue,
        ["null"] = KotlinTokenKind.KwNull,
        ["true"] = KotlinTokenKind.KwTrue,
        ["false"] = KotlinTokenKind.KwFalse,
        ["is"] = KotlinTokenKind.KwIs,
        ["!is"] = KotlinTokenKind.KwNot,
        ["in"] = KotlinTokenKind.KwIn,
        ["!in"] = KotlinTokenKind.KwIn,
        ["typealias"] = KotlinTokenKind.KwTypeAlias,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;

    public KotlinLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
                    _diagnostics.Add(new ParseDiagnostic("KOTLIN001", "Unclosed raw string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(KotlinTokenKind.RawStringLiteral, start);
                continue;
            }

            if (ch == '"')
            {
                _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("KOTLIN001", "Unclosed string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(KotlinTokenKind.StringLiteral, start);
                continue;
            }

            if (ch == '\'')
            {
                _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("KOTLIN001", "Unclosed char literal.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(KotlinTokenKind.CharLiteral, start);
                continue;
            }

            if (ch == '`')
            {
                _c.Advance();
                while (!_c.IsEof && _c.Advance() != '`') { }
                Emit(KotlinTokenKind.Identifier, start);
                continue;
            }

            if (LexCursor.IsDigit(ch))
            {
                ReadNumber();
                Emit(KotlinTokenKind.NumericLiteral, start);
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                _c.ReadIdentifier();
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                KotlinTokenKind kind = Keywords.TryGetValue(text, out KotlinTokenKind k) ? k : KotlinTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            char n = _c.Peek(1);
            KotlinTokenKind pk = ch switch
            {
                '(' => ConsumeSingle(KotlinTokenKind.LParen),
                ')' => ConsumeSingle(KotlinTokenKind.RParen),
                '{' => ConsumeSingle(KotlinTokenKind.LBrace),
                '}' => ConsumeSingle(KotlinTokenKind.RBrace),
                '[' => ConsumeSingle(KotlinTokenKind.LBracket),
                ']' => ConsumeSingle(KotlinTokenKind.RBracket),
                ';' => ConsumeSingle(KotlinTokenKind.Semicolon),
                ',' => ConsumeSingle(KotlinTokenKind.Comma),
                '.' => ConsumeSingle(KotlinTokenKind.Period),
                ':' when n == ':' => ConsumeMulti(2, KotlinTokenKind.ColonColon),
                ':' => ConsumeSingle(KotlinTokenKind.Colon),
                '=' when n == '=' => ConsumeMulti(2, KotlinTokenKind.EqualEqual),
                '=' => ConsumeSingle(KotlinTokenKind.Equal),
                '-' when n == '>' => ConsumeMulti(2, KotlinTokenKind.Arrow),
                '-' when n == '-' => ConsumeMulti(2, KotlinTokenKind.MinusMinus),
                '-' => ConsumeSingle(KotlinTokenKind.Minus),
                '+' when n == '+' => ConsumeMulti(2, KotlinTokenKind.PlusPlus),
                '+' => ConsumeSingle(KotlinTokenKind.Plus),
                '*' => ConsumeSingle(KotlinTokenKind.Star),
                '/' => ConsumeSingle(KotlinTokenKind.Slash),
                '%' => ConsumeSingle(KotlinTokenKind.Percent),
                '&' when n == '&' => ConsumeMulti(2, KotlinTokenKind.AmpAmp),
                '&' => ConsumeSingle(KotlinTokenKind.Amp),
                '|' when n == '|' => ConsumeMulti(2, KotlinTokenKind.PipePipe),
                '|' => ConsumeSingle(KotlinTokenKind.Pipe),
                '^' => ConsumeSingle(KotlinTokenKind.Caret),
                '~' => ConsumeSingle(KotlinTokenKind.Tilde),
                '!' when n == '=' => ConsumeMulti(2, KotlinTokenKind.NotEqual),
                '!' => ConsumeSingle(KotlinTokenKind.Exclamation),
                '<' when n == '=' => ConsumeMulti(2, KotlinTokenKind.LAngleEqual),
                '<' => ConsumeSingle(KotlinTokenKind.LAngle),
                '>' when n == '=' => ConsumeMulti(2, KotlinTokenKind.RAngleEqual),
                '>' => ConsumeSingle(KotlinTokenKind.RAngle),
                '@' => ConsumeSingle(KotlinTokenKind.At),
                '?' => ConsumeSingle(KotlinTokenKind.Question),
                _ => ConsumeSingle(KotlinTokenKind.Question),
            };
            Emit(pk, start);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void ReadNumber()
    {
        if (_c.Peek() == '0' && _c.Peek(1) is 'x' or 'X' or 'b' or 'B')
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

    private KotlinTokenKind ConsumeSingle(KotlinTokenKind kind)
    {
        _c.Advance();
        return kind;
    }

    private KotlinTokenKind ConsumeMulti(int count, KotlinTokenKind kind)
    {
        _c.Advance(count);
        return kind;
    }

    private void Emit(KotlinTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
    }

    private void Emit(KotlinTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));

    public void Dispose() { }
}
