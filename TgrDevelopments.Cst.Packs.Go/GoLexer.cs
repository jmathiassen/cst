using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Go;

/// <summary>
/// Hand-written Go lexer: strings, backtick raw strings, rune literals, line/block comments,
/// numbers, identifiers, keywords, and Go automatic-semicolon insertion (ASI).
/// </summary>
internal sealed class GoLexer
{
    private static readonly Dictionary<string, GoTokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["package"] = GoTokenKind.KwPackage,
        ["import"] = GoTokenKind.KwImport,
        ["func"] = GoTokenKind.KwFunc,
        ["type"] = GoTokenKind.KwType,
        ["var"] = GoTokenKind.KwVar,
        ["const"] = GoTokenKind.KwConst,
        ["struct"] = GoTokenKind.KwStruct,
        ["interface"] = GoTokenKind.KwInterface,
        ["map"] = GoTokenKind.KwMap,
        ["chan"] = GoTokenKind.KwChan,
        ["go"] = GoTokenKind.KwGo,
        ["defer"] = GoTokenKind.KwDefer,
        ["return"] = GoTokenKind.KwReturn,
        ["break"] = GoTokenKind.KwBreak,
        ["continue"] = GoTokenKind.KwContinue,
        ["fallthrough"] = GoTokenKind.KwFallthrough,
        ["if"] = GoTokenKind.KwIf,
        ["else"] = GoTokenKind.KwElse,
        ["for"] = GoTokenKind.KwFor,
        ["range"] = GoTokenKind.KwRange,
        ["switch"] = GoTokenKind.KwSwitch,
        ["case"] = GoTokenKind.KwCase,
        ["default"] = GoTokenKind.KwDefault,
        ["select"] = GoTokenKind.KwSelect,
        ["goto"] = GoTokenKind.KwGoto,
    };

    private static readonly HashSet<GoTokenKind> AsiSet = new()
    {
        GoTokenKind.Identifier,
        GoTokenKind.NumericLiteral,
        GoTokenKind.StringLiteral,
        GoTokenKind.RawStringLiteral,
        GoTokenKind.RuneLiteral,
        GoTokenKind.RParen,
        GoTokenKind.RBracket,
        GoTokenKind.RBrace,
        GoTokenKind.PlusPlus,
        GoTokenKind.MinusMinus,
        GoTokenKind.KwBreak,
        GoTokenKind.KwContinue,
        GoTokenKind.KwFallthrough,
        GoTokenKind.KwReturn,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<ParseDiagnostic> _diagnostics;
    private GoTokenKind _lastKind;
    private bool _partial;

    public GoLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
            _c.ThrowIfCancellationRequested();
            SkipTriviaWithAsi();
            if (_c.IsEof)
                break;

            char ch = _c.Peek();
            if (ch == '/' && _c.Peek(1) is '/' or '*')
            {
                if (_c.Peek(1) == '/')
                {
                    _c.SkipLineComment();
                }
                else
                {
                    _c.SkipBlockComment(out bool unclosed);
                    if (unclosed)
                    {
                        _partial = true;
                        _diagnostics.Add(new ParseDiagnostic("GO001", "Unclosed block comment.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                    }
                }
                continue;
            }
            if (ch == '"')
            {
                int start = _c.Position;
                _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
                if (unclosed)
                    ReportUnclosedString();
                Emit(GoTokenKind.StringLiteral, start);
                continue;
            }
            if (ch == '`')
            {
                int start = _c.Position;
                ReadRawString();
                Emit(GoTokenKind.RawStringLiteral, start);
                continue;
            }
            if (ch == '\'')
            {
                int start = _c.Position;
                _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
                if (unclosed)
                    ReportUnclosedString();
                Emit(GoTokenKind.RuneLiteral, start);
                continue;
            }
            if (LexCursor.IsDigit(ch) || (ch == '.' && LexCursor.IsDigit(_c.Peek(1))))
            {
                int start = _c.Position;
                ReadNumber();
                Emit(GoTokenKind.NumericLiteral, start);
                continue;
            }
            if (char.IsLetter(ch) || ch == '_')
            {
                int start = _c.Position;
                _c.ReadIdentifier();
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                GoTokenKind kind = Keywords.TryGetValue(text, out GoTokenKind k) ? k : GoTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            int pstart = _c.Position;
            char p = _c.Advance();
            GoTokenKind pk;
            if (p == '+' && _c.Peek() == '+')
            {
                _c.Advance();
                pk = GoTokenKind.PlusPlus;
            }
            else if (p == '-' && _c.Peek() == '-')
            {
                _c.Advance();
                pk = GoTokenKind.MinusMinus;
            }
            else
            {
                pk = p switch
                {
                    '(' => GoTokenKind.LParen,
                    ')' => GoTokenKind.RParen,
                    '[' => GoTokenKind.LBracket,
                    ']' => GoTokenKind.RBracket,
                    '{' => GoTokenKind.LBrace,
                    '}' => GoTokenKind.RBrace,
                    ',' => GoTokenKind.Comma,
                    '.' => GoTokenKind.Period,
                    ':' => GoTokenKind.Colon,
                    '=' => GoTokenKind.Equal,
                    '+' => GoTokenKind.Plus,
                    '-' => GoTokenKind.Minus,
                    '*' => GoTokenKind.Star,
                    '/' => GoTokenKind.Slash,
                    '%' => GoTokenKind.Percent,
                    '&' => GoTokenKind.Amp,
                    '|' => GoTokenKind.Pipe,
                    '^' => GoTokenKind.Caret,
                    '!' => GoTokenKind.Exclamation,
                    '~' => GoTokenKind.Tilde,
                    '?' => GoTokenKind.Question,
                    '<' => GoTokenKind.LAngle,
                    '>' => GoTokenKind.RAngle,
                    _ => GoTokenKind.Other,
                };
            }
            Emit(pk, pstart);
        }

        if (AsiSet.Contains(_lastKind))
        {
            _tokens.Add(new Token((int)GoTokenKind.Semicolon, new TextSpan(_c.Position, 1)));
            _lastKind = GoTokenKind.Semicolon;
        }
        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    /// <summary>Skipped trivia. Go's ASI: a semicolon is inserted at a newline when the last
    /// token is in the ASI set and the newline is not inside parentheses/brackets.</summary>
    private void SkipTriviaWithAsi()
    {
        while (!_c.IsEof)
        {
            _c.ThrowIfCancellationRequested();
            char ch = _c.Peek();
            if (ch is ' ' or '\t' or '\r')
            {
                _c.Advance();
                continue;
            }
            if (ch == '\n')
            {
                int pos = _c.Position;
                _c.Advance();
                if (AsiSet.Contains(_lastKind))
                {
                    _tokens.Add(new Token((int)GoTokenKind.Semicolon, new TextSpan(pos, 1)));
                    _lastKind = GoTokenKind.Semicolon;
                }
                continue;
            }
            return;
        }
    }

    private void ReadRawString()
    {
        _c.Advance();
        while (!_c.IsEof)
        {
            _c.ThrowIfCancellationRequested();
            if (_c.Peek() == '`')
            {
                _c.Advance();
                return;
            }
            _c.Advance();
        }
        ReportUnclosedString();
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
            _c.Advance();
            while (char.IsDigit(_c.Peek()) || _c.Peek() == '_')
                _c.Advance();
        }
        if (_c.Peek() is 'e' or 'E')
        {
            _c.Advance();
            if (_c.Peek() is '+' or '-')
                _c.Advance();
            while (char.IsDigit(_c.Peek()) || _c.Peek() == '_')
                _c.Advance();
        }
    }

    private void ReportUnclosedString()
    {
        _partial = true;
        _diagnostics.Add(new ParseDiagnostic("GO001", "Unclosed string literal.", new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
    }

    private void Emit(GoTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
        _lastKind = kind;
    }

    private void Emit(GoTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));
}
