using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Rust;

internal sealed class RustLexer : IDisposable
{
    private static readonly Dictionary<string, RustTokenKind> Keywords = new()
    {
        ["fn"] = RustTokenKind.KwFn,
        ["pub"] = RustTokenKind.KwPub,
        ["struct"] = RustTokenKind.KwStruct,
        ["enum"] = RustTokenKind.KwEnum,
        ["impl"] = RustTokenKind.KwImpl,
        ["trait"] = RustTokenKind.KwTrait,
        ["mod"] = RustTokenKind.KwMod,
        ["use"] = RustTokenKind.KwUse,
        ["const"] = RustTokenKind.KwConst,
        ["static"] = RustTokenKind.KwStatic,
        ["let"] = RustTokenKind.KwLet,
        ["mut"] = RustTokenKind.KwMut,
        ["async"] = RustTokenKind.KwAsync,
        ["unsafe"] = RustTokenKind.KwUnsafe,
        ["extern"] = RustTokenKind.KwExtern,
        ["return"] = RustTokenKind.KwReturn,
        ["where"] = RustTokenKind.KwWhere,
        ["for"] = RustTokenKind.KwFor,
        ["in"] = RustTokenKind.KwIn,
        ["if"] = RustTokenKind.KwIf,
        ["else"] = RustTokenKind.KwElse,
        ["while"] = RustTokenKind.KwWhile,
        ["loop"] = RustTokenKind.KwLoop,
        ["match"] = RustTokenKind.KwMatch,
        ["ref"] = RustTokenKind.KwRef,
        ["type"] = RustTokenKind.KwType,
        ["move"] = RustTokenKind.KwMove,
        ["dyn"] = RustTokenKind.KwDyn,
        ["super"] = RustTokenKind.KwSuper,
        ["self"] = RustTokenKind.KwSelf,
        ["crate"] = RustTokenKind.KwCrate,
        ["true"] = RustTokenKind.KwTrue,
        ["false"] = RustTokenKind.KwFalse,
        ["union"] = RustTokenKind.KwUnion,
        ["macro_rules"] = RustTokenKind.KwMacroRules,
        ["break"] = RustTokenKind.KwBreak,
        ["continue"] = RustTokenKind.KwContinue,
        ["await"] = RustTokenKind.KwAwait,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<ParseDiagnostic> _diagnostics;
    private bool _partial;

    public RustLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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

            if (ch == '\'' && TryLexLifetimeOrChar(start))
                continue;

            if (TryLexString(ch, start))
                continue;

            if (LexCursor.IsDigit(ch))
            {
                ReadNumber();
                Emit(RustTokenKind.NumericLiteral, start);
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                if (TryLexIdentOrKeyword(start))
                    continue;
            }

            if (ch == '#' && _c.Peek(1) == '!' && _c.Position == 0)
            {
                _c.Advance(2);
                while (!_c.IsEof && _c.Advance() != '\n') { }
                continue;
            }

            char n = _c.Peek(1);
            RustTokenKind pk = ch switch
            {
                '(' => ConsumeSingle(RustTokenKind.LParen),
                ')' => ConsumeSingle(RustTokenKind.RParen),
                '{' => ConsumeSingle(RustTokenKind.LBrace),
                '}' => ConsumeSingle(RustTokenKind.RBrace),
                '[' => ConsumeSingle(RustTokenKind.LBracket),
                ']' => ConsumeSingle(RustTokenKind.RBracket),
                ';' => ConsumeSingle(RustTokenKind.Semicolon),
                ',' => ConsumeSingle(RustTokenKind.Comma),
                '.' when n == '.' && _c.Peek(2) == '.' => ConsumeMulti(3, RustTokenKind.DotDotDot),
                '.' when n == '.' && _c.Peek(2) == '=' => ConsumeMulti(3, RustTokenKind.DotDotEqual),
                '.' when n == '.' => ConsumeMulti(2, RustTokenKind.DotDot),
                '.' => ConsumeSingle(RustTokenKind.Period),
                ':' when n == ':' => ConsumeMulti(2, RustTokenKind.ColonColon),
                ':' => ConsumeSingle(RustTokenKind.Colon),
                '=' when n == '>' => ConsumeMulti(2, RustTokenKind.FatArrow),
                '=' => ConsumeSingle(RustTokenKind.Equal),
                '-' when n == '>' => ConsumeMulti(2, RustTokenKind.Arrow),
                '-' when n == '-' => ConsumeMulti(2, RustTokenKind.MinusMinus),
                '-' => ConsumeSingle(RustTokenKind.Minus),
                '+' when n == '+' => ConsumeMulti(2, RustTokenKind.PlusPlus),
                '+' => ConsumeSingle(RustTokenKind.Plus),
                '*' => ConsumeSingle(RustTokenKind.Star),
                '/' => ConsumeSingle(RustTokenKind.Slash),
                '%' => ConsumeSingle(RustTokenKind.Percent),
                '&' => ConsumeSingle(RustTokenKind.Amp),
                '|' => ConsumeSingle(RustTokenKind.Pipe),
                '^' => ConsumeSingle(RustTokenKind.Caret),
                '~' => ConsumeSingle(RustTokenKind.Tilde),
                '#' => ConsumeSingle(RustTokenKind.Hash),
                '@' => ConsumeSingle(RustTokenKind.At),
                '!' => ConsumeSingle(RustTokenKind.Exclamation),
                '<' => ConsumeSingle(RustTokenKind.LAngle),
                '>' => ConsumeSingle(RustTokenKind.RAngle),
                '?' => ConsumeSingle(RustTokenKind.Question),
                '_' => ConsumeSingle(RustTokenKind.Underscore),
                _ => ConsumeSingle(RustTokenKind.Other),
            };
            Emit(pk, start);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private bool TryLexString(char ch, int start)
    {
        if (ch is 'b' or 'B')
        {
            char n1 = _c.Peek(1);
            if (n1 is 'r' or 'R')
            {
                int rpos = _c.Position + 2;
                if (rpos < _text.Length && (_text[rpos] == '"' || _text[rpos] == '#'))
                {
                    _c.Advance(2);
                    LexRawString();
                    Emit(RustTokenKind.RawStringLiteral, start);
                    return true;
                }
            }
            if (n1 == '"')
            {
                _c.Advance();
                _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed) ReportUnclosedString();
                Emit(RustTokenKind.ByteStringLiteral, start);
                return true;
            }
            if (n1 == '\'')
            {
                _c.Advance();
                _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
                if (unclosed) ReportUnclosedString();
                Emit(RustTokenKind.CharLiteral, start);
                return true;
            }
            return false;
        }

        if (ch is 'r' or 'R')
        {
            int rpos = _c.Position + 1;
            if (rpos < _text.Length && (_text[rpos] == '"' || _text[rpos] == '#'))
            {
                _c.Advance();
                LexRawString();
                Emit(RustTokenKind.RawStringLiteral, start);
                return true;
            }
            return false;
        }

        if (ch == '"')
        {
            _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
            if (unclosed) ReportUnclosedString();
            Emit(RustTokenKind.StringLiteral, start);
            return true;
        }

        return false;
    }

    private bool TryLexLifetimeOrChar(int start)
    {
        char n1 = _c.Peek(1);
        if (n1 == '\\')
        {
            _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
            if (unclosed) ReportUnclosedString();
            Emit(RustTokenKind.CharLiteral, start);
            return true;
        }
        if (n1 == '\'')
        {
            _c.Advance(2);
            Emit(RustTokenKind.CharLiteral, start);
            return true;
        }
        if (char.IsLetter(n1) || n1 == '_')
        {
            int afterQuote = _c.Position + 1;
            ScanIdentifier();
            if (!_c.IsEof && _c.Peek() == '\'')
            {
                _c.Advance();
                Emit(RustTokenKind.CharLiteral, start);
            }
            else
            {
                _tokens.Add(new Token((int)RustTokenKind.Lifetime, new TextSpan(start, _c.Position - start), _text[afterQuote.._c.Position]));
            }
            return true;
        }
        if (n1 is not '\'' and not '\0')
        {
            _c.Advance();
            _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
            if (unclosed) ReportUnclosedString();
            Emit(RustTokenKind.CharLiteral, start);
            return true;
        }
        return false;
    }

    private bool TryLexIdentOrKeyword(int start)
    {
        if (_c.Peek() == '_' && !char.IsLetterOrDigit(_c.Peek(1)))
        {
            _c.Advance();
            Emit(RustTokenKind.Underscore, start);
            return true;
        }
        ScanIdentifier();
        TextSpan span = new(start, _c.Position - start);
        string text = _c.Slice(span);
        if (text == "r" || text == "R")
            return false;
        RustTokenKind kind = Keywords.TryGetValue(text, out RustTokenKind k) ? k : RustTokenKind.Identifier;
        Emit(kind, span, text);
        return true;
    }

    private void ScanIdentifier()
    {
        while (!_c.IsEof && (char.IsLetterOrDigit(_c.Peek()) || _c.Peek() == '_'))
            _c.Advance();
    }

    private void LexRawString()
    {
        int hashCount = 0;
        while (!_c.IsEof && _c.Peek() == '#')
        {
            hashCount++;
            _c.Advance();
        }
        if (_c.IsEof || _c.Peek() != '"')
        {
            ReportUnclosedString();
            return;
        }
        _c.Advance();
        while (!_c.IsEof)
        {
            _c.ThrowIfCancellationRequested();
            if (_c.Peek() == '"')
            {
                bool match = true;
                for (int i = 1; i <= hashCount; i++)
                {
                    if (_c.Peek(i) != '#')
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    _c.Advance(1 + hashCount);
                    return;
                }
            }
            _c.Advance();
        }
        ReportUnclosedString();
    }

    private void SkipBlockCommentNested()
    {
        int depth = 1;
        _c.Advance(2);
        while (!_c.IsEof && depth > 0)
        {
            _c.ThrowIfCancellationRequested();
            if (_c.TryMatch("/*"))
            {
                depth++;
                continue;
            }
            if (_c.TryMatch("*/"))
            {
                depth--;
                continue;
            }
            _c.Advance();
        }
        if (depth > 0)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed block comment.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
        }
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
        if (_c.Peek() is 'e' or 'E' or 'f' or 'F')
        {
            _c.Advance();
            if (_c.Peek() is '+' or '-')
                _c.Advance();
            while (char.IsDigit(_c.Peek()) || _c.Peek() == '_')
                _c.Advance();
            return;
        }
        if (_c.Peek() == '.')
        {
            if (char.IsDigit(_c.Peek(1)))
            {
                _c.Advance();
                while (char.IsDigit(_c.Peek()) || _c.Peek() == '_')
                    _c.Advance();
                if (_c.Peek() is 'e' or 'E')
                {
                    _c.Advance();
                    if (_c.Peek() is '+' or '-')
                        _c.Advance();
                    while (char.IsDigit(_c.Peek()) || _c.Peek() == '_')
                        _c.Advance();
                }
            }
            else if (_c.Peek(1) is '.' or '=' or ' ' or '\t' or '\n' or ';' or ',' or ')' or ']' or '}')
            {
                return;
            }
        }
        if (_c.Peek() is 'u' or 'U' or 'i' or 'I')
        {
            ScanIdentifier();
        }
    }

    private void ReportUnclosedString()
    {
        _partial = true;
        _diagnostics.Add(new ParseDiagnostic("RS001", "Unclosed string literal.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
    }

    private RustTokenKind ConsumeSingle(RustTokenKind kind)
    {
        _c.Advance();
        return kind;
    }

    private RustTokenKind ConsumeMulti(int count, RustTokenKind kind)
    {
        _c.Advance(count);
        return kind;
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

    private void Emit(RustTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
    }

    private void Emit(RustTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));

    public void Dispose() { }
}
