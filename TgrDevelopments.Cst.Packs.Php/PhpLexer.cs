using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Php;

internal sealed class PhpLexer : IDisposable
{
    private static readonly Dictionary<string, PhpTokenKind> Keywords = new()
    {
        ["namespace"] = PhpTokenKind.KwNamespace,
        ["class"] = PhpTokenKind.KwClass,
        ["trait"] = PhpTokenKind.KwTrait,
        ["interface"] = PhpTokenKind.KwInterface,
        ["enum"] = PhpTokenKind.KwEnum,
        ["function"] = PhpTokenKind.KwFunction,
        ["public"] = PhpTokenKind.KwPublic,
        ["private"] = PhpTokenKind.KwPrivate,
        ["protected"] = PhpTokenKind.KwProtected,
        ["static"] = PhpTokenKind.KwStatic,
        ["abstract"] = PhpTokenKind.KwAbstract,
        ["final"] = PhpTokenKind.KwFinal,
        ["readonly"] = PhpTokenKind.KwReadonly,
        ["const"] = PhpTokenKind.KwConst,
        ["var"] = PhpTokenKind.KwVar,
        ["return"] = PhpTokenKind.KwReturn,
        ["if"] = PhpTokenKind.KwIf,
        ["else"] = PhpTokenKind.KwElse,
        ["elseif"] = PhpTokenKind.KwElseIf,
        ["for"] = PhpTokenKind.KwFor,
        ["while"] = PhpTokenKind.KwWhile,
        ["do"] = PhpTokenKind.KwDo,
        ["switch"] = PhpTokenKind.KwSwitch,
        ["case"] = PhpTokenKind.KwCase,
        ["break"] = PhpTokenKind.KwBreak,
        ["continue"] = PhpTokenKind.KwContinue,
        ["try"] = PhpTokenKind.KwTry,
        ["catch"] = PhpTokenKind.KwCatch,
        ["finally"] = PhpTokenKind.KwFinally,
        ["throw"] = PhpTokenKind.KwThrow,
        ["null"] = PhpTokenKind.KwNull,
        ["true"] = PhpTokenKind.KwTrue,
        ["false"] = PhpTokenKind.KwFalse,
        ["match"] = PhpTokenKind.KwMatch,
        ["fn"] = PhpTokenKind.KwFn,
        ["use"] = PhpTokenKind.KwUse,
        ["as"] = PhpTokenKind.KwAs,
        ["insteadof"] = PhpTokenKind.KwInsteadOf,
        ["extends"] = PhpTokenKind.KwExtends,
        ["implements"] = PhpTokenKind.KwImplements,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;

    public PhpLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _text = text;
        _c = new LexCursor(text, ct);
        _diagnostics = diagnostics;
    }

    public bool IsPartial => _partial;

    public IReadOnlyList<Token> Tokenize()
    {
        SkipToPhpTag();
        while (!_c.IsEof)
        {
            SkipTrivia();
            if (_c.IsEof)
                break;
            if (_c.Peek() == '?' && _c.Peek(1) == '>')
            {
                _c.Advance(2);
                SkipToPhpTag();
                continue;
            }

            _c.ThrowIfCancellationRequested();
            char ch = _c.Peek();
            int start = _c.Position;

            if (ch == '"')
            {
                _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("PHP001", "Unclosed string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(PhpTokenKind.StringLiteral, start);
                continue;
            }

            if (ch == '\'')
            {
                _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic("PHP001", "Unclosed string.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                Emit(PhpTokenKind.StringLiteral, start);
                continue;
            }

            if (ch == '<' && _c.Peek(1) == '<' && _c.Peek(2) == '<')
            {
                _c.Advance(3);
                LexHeredoc();
                Emit(PhpTokenKind.HeredocLiteral, start);
                continue;
            }

            if (LexCursor.IsDigit(ch))
            {
                ReadNumber();
                Emit(PhpTokenKind.NumericLiteral, start);
                continue;
            }

            if (ch == '$' || char.IsLetter(ch) || ch == '_')
            {
                int pos = _c.Position;
                if (ch == '$')
                    _c.Advance();
                _c.ReadIdentifier();
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                string key = ch == '$' ? text : text;
                PhpTokenKind kind = Keywords.TryGetValue(key, out PhpTokenKind k) ? k : PhpTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            char n = _c.Peek(1);
            PhpTokenKind pk = ch switch
            {
                '(' => ConsumeSingle(PhpTokenKind.LParen),
                ')' => ConsumeSingle(PhpTokenKind.RParen),
                '{' => ConsumeSingle(PhpTokenKind.LBrace),
                '}' => ConsumeSingle(PhpTokenKind.RBrace),
                '[' => ConsumeSingle(PhpTokenKind.LBracket),
                ']' => ConsumeSingle(PhpTokenKind.RBracket),
                ';' => ConsumeSingle(PhpTokenKind.Semicolon),
                ',' => ConsumeSingle(PhpTokenKind.Comma),
                '.' when n == '.' => ConsumeMulti(2, PhpTokenKind.DotDot),
                '.' => ConsumeSingle(PhpTokenKind.Period),
                ':' when n == ':' => ConsumeMulti(2, PhpTokenKind.ColonColon),
                ':' => ConsumeSingle(PhpTokenKind.Colon),
                '=' when n == '=' && _c.Peek(2) == '=' => ConsumeMulti(3, PhpTokenKind.EqualEqualEqual),
                '=' when n == '=' => ConsumeMulti(2, PhpTokenKind.EqualEqual),
                '=' => ConsumeSingle(PhpTokenKind.Equal),
                '-' when n == '>' => ConsumeMulti(2, PhpTokenKind.Arrow),
                '-' when n == '-' => ConsumeMulti(2, PhpTokenKind.MinusMinus),
                '-' => ConsumeSingle(PhpTokenKind.Minus),
                '+' when n == '+' => ConsumeMulti(2, PhpTokenKind.PlusPlus),
                '+' => ConsumeSingle(PhpTokenKind.Plus),
                '*' => ConsumeSingle(PhpTokenKind.Star),
                '/' => ConsumeSingle(PhpTokenKind.Slash),
                '%' => ConsumeSingle(PhpTokenKind.Percent),
                '&' when n == '&' => ConsumeMulti(2, PhpTokenKind.AmpAmp),
                '&' => ConsumeSingle(PhpTokenKind.Amp),
                '|' when n == '|' => ConsumeMulti(2, PhpTokenKind.PipePipe),
                '|' => ConsumeSingle(PhpTokenKind.Pipe),
                '^' => ConsumeSingle(PhpTokenKind.Caret),
                '~' => ConsumeSingle(PhpTokenKind.Tilde),
                '!' when n == '=' && _c.Peek(2) == '=' => ConsumeMulti(3, PhpTokenKind.NotEqualEqual),
                '!' when n == '=' => ConsumeMulti(2, PhpTokenKind.NotEqual),
                '!' => ConsumeSingle(PhpTokenKind.Exclamation),
                '<' when n == '=' => ConsumeMulti(2, PhpTokenKind.LAngleEqual),
                '<' => ConsumeSingle(PhpTokenKind.LAngle),
                '>' when n == '=' => ConsumeMulti(2, PhpTokenKind.RAngleEqual),
                '>' => ConsumeSingle(PhpTokenKind.RAngle),
                '@' => ConsumeSingle(PhpTokenKind.At),
                '?' => ConsumeSingle(PhpTokenKind.Question),
                '#' => ConsumeSingle(PhpTokenKind.Hash),
                '\\' => ConsumeSingle(PhpTokenKind.Backslash),
                _ => ConsumeSingle(PhpTokenKind.Question),
            };
            Emit(pk, start);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void SkipToPhpTag()
    {
        while (!_c.IsEof)
        {
            if (_c.Peek() == '<' && (_c.Peek(1) == '?'))
            {
                _c.Advance(2);
                if (_c.Peek() == 'p' && _c.Peek(1) == 'h' && _c.Peek(2) == 'p')
                    _c.Advance(3);
                return;
            }
            _c.Advance();
        }
    }

    private void LexHeredoc()
    {
        bool nowdoc = _c.Peek() == '\'';
        if (nowdoc)
            _c.Advance();
        string label = "";
        while (!_c.IsEof && char.IsLetterOrDigit(_c.Peek()) && _c.Peek() != '\n' && _c.Peek() != ';' && _c.Peek() != '\r')
        {
            label += _c.Advance();
        }
        if (nowdoc && _c.Peek() == '\'')
            _c.Advance();
        if (_c.Peek() == ';')
            _c.Advance();
        while (!_c.IsEof && _c.Advance() != '\n') { }

        while (!_c.IsEof)
        {
            if (_c.Peek() == '\n')
            {
                _c.Advance();
                continue;
            }
            bool found = true;
            for (int i = 0; i < label.Length; i++)
            {
                if (_c.Peek(i) != label[i]) { found = false; break; }
            }
            if (found && (_c.Peek(label.Length) == ';' || _c.Peek(label.Length) == '\n' || _c.Peek(label.Length) == '\r' || _c.Peek(label.Length) == '\0'))
            {
                _c.Advance(label.Length);
                if (_c.Peek() == ';')
                    _c.Advance();
                return;
            }
            if (_c.Peek() == '\r') _c.Advance();
            if (_c.Peek() == '\n') _c.Advance();
            else _c.Advance();
        }
        _partial = true;
        _diagnostics.Add(new ParseDiagnostic("PHP001", "Unclosed heredoc.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
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
            if (ch == '#')
            {
                while (!_c.IsEof && _c.Advance() != '\n') { }
                continue;
            }
            return;
        }
    }

    private PhpTokenKind ConsumeSingle(PhpTokenKind kind)
    {
        _c.Advance();
        return kind;
    }

    private PhpTokenKind ConsumeMulti(int count, PhpTokenKind kind)
    {
        _c.Advance(count);
        return kind;
    }

    private void Emit(PhpTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
    }

    private void Emit(PhpTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));

    public void Dispose() { }
}
