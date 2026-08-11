using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Java;

internal sealed class JavaLexer : IDisposable
{
    private static readonly Dictionary<string, JavaTokenKind> Keywords = new()
    {
        ["package"] = JavaTokenKind.KwPackage,
        ["import"] = JavaTokenKind.KwImport,
        ["class"] = JavaTokenKind.KwClass,
        ["interface"] = JavaTokenKind.KwInterface,
        ["enum"] = JavaTokenKind.KwEnum,
        ["record"] = JavaTokenKind.KwRecord,
        ["extends"] = JavaTokenKind.KwExtends,
        ["implements"] = JavaTokenKind.KwImplements,
        ["public"] = JavaTokenKind.KwPublic,
        ["private"] = JavaTokenKind.KwPrivate,
        ["protected"] = JavaTokenKind.KwProtected,
        ["static"] = JavaTokenKind.KwStatic,
        ["abstract"] = JavaTokenKind.KwAbstract,
        ["final"] = JavaTokenKind.KwFinal,
        ["void"] = JavaTokenKind.KwVoid,
        ["return"] = JavaTokenKind.KwReturn,
        ["if"] = JavaTokenKind.KwIf,
        ["else"] = JavaTokenKind.KwElse,
        ["for"] = JavaTokenKind.KwFor,
        ["while"] = JavaTokenKind.KwWhile,
        ["do"] = JavaTokenKind.KwDo,
        ["switch"] = JavaTokenKind.KwSwitch,
        ["case"] = JavaTokenKind.KwCase,
        ["default"] = JavaTokenKind.KwDefault,
        ["break"] = JavaTokenKind.KwBreak,
        ["continue"] = JavaTokenKind.KwContinue,
        ["new"] = JavaTokenKind.KwNew,
        ["this"] = JavaTokenKind.KwThis,
        ["super"] = JavaTokenKind.KwSuper,
        ["null"] = JavaTokenKind.KwNull,
        ["true"] = JavaTokenKind.KwTrue,
        ["false"] = JavaTokenKind.KwFalse,
        ["instanceof"] = JavaTokenKind.KwInstanceOf,
        ["throws"] = JavaTokenKind.KwThrows,
        ["throw"] = JavaTokenKind.KwThrow,
        ["try"] = JavaTokenKind.KwTry,
        ["catch"] = JavaTokenKind.KwCatch,
        ["finally"] = JavaTokenKind.KwFinally,
        ["native"] = JavaTokenKind.KwNative,
        ["synchronized"] = JavaTokenKind.KwSynchronized,
        ["volatile"] = JavaTokenKind.KwVolatile,
        ["transient"] = JavaTokenKind.KwTransient,
        ["strictfp"] = JavaTokenKind.KwStrictFp,
        ["var"] = JavaTokenKind.KwVar,
        ["yield"] = JavaTokenKind.KwYield,
        ["sealed"] = JavaTokenKind.KwSealed,
        ["permits"] = JavaTokenKind.KwPermits,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;

    public JavaLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
                ReadTextBlock();
                Emit(JavaTokenKind.TextBlock, start);
                continue;
            }

            if (ch == '"')
            {
                _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("JAVA001", "Unclosed string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(JavaTokenKind.StringLiteral, start);
                continue;
            }

            if (ch == '\'')
            {
                _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("JAVA001", "Unclosed char literal.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(JavaTokenKind.CharLiteral, start);
                continue;
            }

            if (LexCursor.IsDigit(ch))
            {
                ReadNumber();
                Emit(JavaTokenKind.NumericLiteral, start);
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                _c.ReadIdentifier();
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                JavaTokenKind kind = Keywords.TryGetValue(text, out JavaTokenKind k) ? k : JavaTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            char n = _c.Peek(1);
            JavaTokenKind pk = ch switch
            {
                '(' => ConsumeSingle(JavaTokenKind.LParen),
                ')' => ConsumeSingle(JavaTokenKind.RParen),
                '{' => ConsumeSingle(JavaTokenKind.LBrace),
                '}' => ConsumeSingle(JavaTokenKind.RBrace),
                '[' => ConsumeSingle(JavaTokenKind.LBracket),
                ']' => ConsumeSingle(JavaTokenKind.RBracket),
                ';' => ConsumeSingle(JavaTokenKind.Semicolon),
                ',' => ConsumeSingle(JavaTokenKind.Comma),
                '.' => ConsumeSingle(JavaTokenKind.Period),
                ':' when n == ':' => ConsumeMulti(2, JavaTokenKind.ColonColon),
                ':' => ConsumeSingle(JavaTokenKind.Colon),
                '=' when n == '=' => ConsumeMulti(2, JavaTokenKind.EqualEqual),
                '=' => ConsumeSingle(JavaTokenKind.Equal),
                '-' when n == '>' => ConsumeMulti(2, JavaTokenKind.Arrow),
                '-' when n == '-' => ConsumeMulti(2, JavaTokenKind.MinusMinus),
                '-' => ConsumeSingle(JavaTokenKind.Minus),
                '+' when n == '+' => ConsumeMulti(2, JavaTokenKind.PlusPlus),
                '+' => ConsumeSingle(JavaTokenKind.Plus),
                '*' => ConsumeSingle(JavaTokenKind.Star),
                '/' => ConsumeSingle(JavaTokenKind.Slash),
                '%' => ConsumeSingle(JavaTokenKind.Percent),
                '&' when n == '&' => ConsumeMulti(2, JavaTokenKind.AmpAmp),
                '&' => ConsumeSingle(JavaTokenKind.Amp),
                '|' when n == '|' => ConsumeMulti(2, JavaTokenKind.PipePipe),
                '|' => ConsumeSingle(JavaTokenKind.Pipe),
                '^' => ConsumeSingle(JavaTokenKind.Caret),
                '~' => ConsumeSingle(JavaTokenKind.Tilde),
                '!' when n == '=' => ConsumeMulti(2, JavaTokenKind.NotEqual),
                '!' => ConsumeSingle(JavaTokenKind.Exclamation),
                '<' when n == '=' => ConsumeMulti(2, JavaTokenKind.LAngleEqual),
                '<' => ConsumeSingle(JavaTokenKind.LAngle),
                '>' when n == '=' => ConsumeMulti(2, JavaTokenKind.RAngleEqual),
                '>' => ConsumeSingle(JavaTokenKind.RAngle),
                '@' => ConsumeSingle(JavaTokenKind.At),
                '?' => ConsumeSingle(JavaTokenKind.Question),
                _ => ConsumeSingle(JavaTokenKind.Question),
            };
            Emit(pk, start);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void ReadTextBlock()
    {
        _c.Advance(3);
        while (!_c.IsEof)
        {
            if (_c.Peek() == '"' && _c.Peek(1) == '"' && _c.Peek(2) == '"')
            {
                _c.Advance(3);
                return;
            }
            _c.Advance();
        }
        _partial = true;
        _diagnostics.Add(new ParseDiagnostic("JAVA001", "Unclosed text block.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
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
        if (_c.Peek() is 'L' or 'l' or 'F' or 'f' or 'D' or 'd')
            _c.Advance();
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

    private JavaTokenKind ConsumeSingle(JavaTokenKind kind)
    {
        _c.Advance();
        return kind;
    }

    private JavaTokenKind ConsumeMulti(int count, JavaTokenKind kind)
    {
        _c.Advance(count);
        return kind;
    }

    private void Emit(JavaTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
    }

    private void Emit(JavaTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));

    public void Dispose() { }
}
