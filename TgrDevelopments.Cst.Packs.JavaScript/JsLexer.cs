using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.JavaScript;

/// <summary>Hand-written JavaScript lexer. Skips whitespace and comments as trivia (not emitted).</summary>
internal sealed class JsLexer
{
    private static readonly Dictionary<string, JsTokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["function"] = JsTokenKind.KwFunction,
        ["class"] = JsTokenKind.KwClass,
        ["async"] = JsTokenKind.KwAsync,
        ["static"] = JsTokenKind.KwStatic,
        ["get"] = JsTokenKind.KwGet,
        ["set"] = JsTokenKind.KwSet,
        ["constructor"] = JsTokenKind.KwConstructor,
        ["extends"] = JsTokenKind.KwExtends,
        ["export"] = JsTokenKind.KwExport,
        ["default"] = JsTokenKind.KwDefault,
        ["import"] = JsTokenKind.KwImport,
        ["from"] = JsTokenKind.KwFrom,
        ["const"] = JsTokenKind.KwConst,
        ["let"] = JsTokenKind.KwLet,
        ["var"] = JsTokenKind.KwVar,
        ["return"] = JsTokenKind.KwReturn,
        ["if"] = JsTokenKind.KwIf,
        ["else"] = JsTokenKind.KwElse,
        ["for"] = JsTokenKind.KwFor,
        ["while"] = JsTokenKind.KwWhile,
        ["do"] = JsTokenKind.KwDo,
        ["switch"] = JsTokenKind.KwSwitch,
        ["case"] = JsTokenKind.KwCase,
        ["break"] = JsTokenKind.KwBreak,
        ["continue"] = JsTokenKind.KwContinue,
        ["try"] = JsTokenKind.KwTry,
        ["catch"] = JsTokenKind.KwCatch,
        ["finally"] = JsTokenKind.KwFinally,
        ["throw"] = JsTokenKind.KwThrow,
        ["new"] = JsTokenKind.KwNew,
        ["this"] = JsTokenKind.KwThis,
        ["super"] = JsTokenKind.KwSuper,
        ["typeof"] = JsTokenKind.KwTypeof,
        ["instanceof"] = JsTokenKind.KwInstanceof,
        ["void"] = JsTokenKind.KwVoid,
        ["delete"] = JsTokenKind.KwDelete,
        ["in"] = JsTokenKind.KwIn,
        ["of"] = JsTokenKind.KwOf,
        ["yield"] = JsTokenKind.KwYield,
        ["await"] = JsTokenKind.KwAwait,
        ["with"] = JsTokenKind.KwWith,
        ["debugger"] = JsTokenKind.KwDebugger,
        ["true"] = JsTokenKind.KwTrue,
        ["false"] = JsTokenKind.KwFalse,
        ["null"] = JsTokenKind.KwNull,
        ["undefined"] = JsTokenKind.KwUndefined,
    };

    private readonly LexCursor _c;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly List<Token> _tokens = [];
    private bool _partial;
    private Token? _prevSignificant;

    public JsLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _c = new LexCursor(text, ct);
        _diagnostics = diagnostics;
    }

    public bool IsPartial => _partial;

    public IReadOnlyList<Token> Tokenize()
    {
        // shebang
        if (_c.StartsWith("#!"))
        {
            _c.SkipUntilNewline();
            if (_c.Peek() is '\r' or '\n')
                _c.Advance();
        }

        while (!_c.IsEof)
        {
            _c.ThrowIfCancellationRequested();
            _c.SkipWhitespace();
            if (_c.IsEof)
                break;

            char ch = _c.Peek();

            // comments
            if (ch == '/' && _c.Peek(1) == '/')
            {
                _c.SkipLineComment();
                continue;
            }
            if (ch == '/' && _c.Peek(1) == '*')
            {
                _c.SkipBlockComment(out bool unclosed);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic(
                        "JS001", "Unclosed block comment.",
                        new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                continue;
            }

            // regex vs slash — only when previous token allows
            if (ch == '/' && CanStartRegex())
            {
                EmitRegex();
                continue;
            }

            if (ch is '"' or '\'')
            {
                int start = _c.Position;
                TextSpan span = _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic(
                        "JS001", "Unclosed string literal.",
                        span, DiagnosticSeverity.Error));
                }
                Emit(JsTokenKind.StringLiteral, span);
                continue;
            }

            if (ch == '`')
            {
                TextSpan span = _c.ReadTemplateString(out bool unclosed);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic(
                        "JS001", "Unclosed template literal.",
                        span, DiagnosticSeverity.Error));
                }
                Emit(JsTokenKind.TemplateLiteral, span);
                continue;
            }

            if (LexCursor.IsDigit(ch) || (ch == '.' && LexCursor.IsDigit(_c.Peek(1))))
            {
                TextSpan span = _c.ReadNumber();
                Emit(JsTokenKind.NumericLiteral, span);
                continue;
            }

            if (ch == '#' && (char.IsLetter(_c.Peek(1)) || _c.Peek(1) is '_' or '$'))
            {
                int start = _c.Position;
                _c.Advance(); // #
                TextSpan id = _c.ReadIdentifier();
                TextSpan span = new(start, id.End - start);
                string text = _c.Slice(span);
                Emit(JsTokenKind.PrivateIdentifier, span, text);
                continue;
            }

            if (char.IsLetter(ch) || ch is '_' or '$')
            {
                TextSpan span = _c.ReadIdentifier();
                string text = _c.Slice(span);
                JsTokenKind kind = Keywords.TryGetValue(text, out JsTokenKind kw)
                    ? kw
                    : JsTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            EmitOperatorOrPunct();
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private bool CanStartRegex()
    {
        if (_prevSignificant is null)
            return true;
        JsTokenKind k = (JsTokenKind)_prevSignificant.Value.Kind;
        return k is JsTokenKind.LParen or JsTokenKind.LBracket or JsTokenKind.LBrace
            or JsTokenKind.Comma or JsTokenKind.Colon or JsTokenKind.Semicolon
            or JsTokenKind.Eq or JsTokenKind.EqEq or JsTokenKind.EqEqEq
            or JsTokenKind.Bang or JsTokenKind.BangEq or JsTokenKind.BangEqEq
            or JsTokenKind.Lt or JsTokenKind.LtEq or JsTokenKind.Gt or JsTokenKind.GtEq
            or JsTokenKind.Plus or JsTokenKind.Minus or JsTokenKind.Star or JsTokenKind.Slash
            or JsTokenKind.Percent or JsTokenKind.Amp or JsTokenKind.AmpAmp
            or JsTokenKind.Pipe or JsTokenKind.PipePipe or JsTokenKind.Caret
            or JsTokenKind.Tilde or JsTokenKind.Arrow or JsTokenKind.Question
            or JsTokenKind.KwReturn or JsTokenKind.KwThrow or JsTokenKind.KwCase
            or JsTokenKind.KwElse or JsTokenKind.KwIn or JsTokenKind.KwOf
            or JsTokenKind.KwTypeof or JsTokenKind.KwVoid or JsTokenKind.KwDelete
            or JsTokenKind.KwAwait or JsTokenKind.KwYield or JsTokenKind.KwNew;
    }

    private void EmitRegex()
    {
        int start = _c.Position;
        _c.Advance(); // /
        while (!_c.IsEof)
        {
            char c = _c.Peek();
            if (c == '\\')
            {
                _c.Advance();
                if (!_c.IsEof)
                    _c.Advance();
                continue;
            }
            if (c is '\n' or '\r')
            {
                _partial = true;
                break;
            }
            if (c == '/')
            {
                _c.Advance();
                while (char.IsLetter(_c.Peek()))
                    _c.Advance(); // flags
                Emit(JsTokenKind.RegexLiteral, new TextSpan(start, _c.Position - start));
                return;
            }
            if (c == '[')
            {
                _c.Advance();
                while (!_c.IsEof && _c.Peek() != ']')
                {
                    if (_c.Peek() == '\\')
                    {
                        _c.Advance();
                        if (!_c.IsEof)
                            _c.Advance();
                        continue;
                    }
                    _c.Advance();
                }
                if (_c.Peek() == ']')
                    _c.Advance();
                continue;
            }
            _c.Advance();
        }
        Emit(JsTokenKind.RegexLiteral, new TextSpan(start, _c.Position - start));
    }

    private void EmitOperatorOrPunct()
    {
        int start = _c.Position;
        char c = _c.Advance();

        // multi-char
        if (c == '=' && _c.TryMatch('>'))
        {
            Emit(JsTokenKind.Arrow, start);
            return;
        }
        if (c == '.' && _c.Peek() == '.' && _c.Peek(1) == '.')
        {
            _c.Advance();
            _c.Advance();
            Emit(JsTokenKind.Ellipsis, start);
            return;
        }
        if (c == '?' && _c.TryMatch('?'))
        {
            Emit(JsTokenKind.QuestionQuestion, start);
            return;
        }
        if (c == '?' && _c.TryMatch('.'))
        {
            Emit(JsTokenKind.QuestionDot, start);
            return;
        }
        if (c == '=' && _c.Peek() == '=')
        {
            _c.Advance();
            if (_c.TryMatch('='))
                Emit(JsTokenKind.EqEqEq, start);
            else
                Emit(JsTokenKind.EqEq, start);
            return;
        }
        if (c == '!' && _c.Peek() == '=')
        {
            _c.Advance();
            if (_c.TryMatch('='))
                Emit(JsTokenKind.BangEqEq, start);
            else
                Emit(JsTokenKind.BangEq, start);
            return;
        }
        if (c == '<' && _c.TryMatch('<'))
        {
            Emit(JsTokenKind.LtLt, start);
            return;
        }
        if (c == '>' && _c.Peek() == '>')
        {
            _c.Advance();
            if (_c.TryMatch('>'))
                Emit(JsTokenKind.GtGtGt, start);
            else
                Emit(JsTokenKind.GtGt, start);
            return;
        }
        if (c == '&' && _c.TryMatch('&'))
        {
            Emit(JsTokenKind.AmpAmp, start);
            return;
        }
        if (c == '|' && _c.TryMatch('|'))
        {
            Emit(JsTokenKind.PipePipe, start);
            return;
        }
        if (c == '+' && _c.TryMatch('+'))
        {
            Emit(JsTokenKind.PlusPlus, start);
            return;
        }
        if (c == '-' && _c.TryMatch('-'))
        {
            Emit(JsTokenKind.MinusMinus, start);
            return;
        }
        if (c == '*' && _c.TryMatch('*'))
        {
            Emit(JsTokenKind.StarStar, start);
            return;
        }
        if (c == '+' && _c.TryMatch('='))
        {
            Emit(JsTokenKind.PlusEq, start);
            return;
        }
        if (c == '-' && _c.TryMatch('='))
        {
            Emit(JsTokenKind.MinusEq, start);
            return;
        }
        if (c == '*' && _c.TryMatch('='))
        {
            Emit(JsTokenKind.StarEq, start);
            return;
        }
        if (c == '/' && _c.TryMatch('='))
        {
            Emit(JsTokenKind.SlashEq, start);
            return;
        }
        if (c == '%' && _c.TryMatch('='))
        {
            Emit(JsTokenKind.PercentEq, start);
            return;
        }
        if (c == '<' && _c.TryMatch('='))
        {
            Emit(JsTokenKind.LtEq, start);
            return;
        }
        if (c == '>' && _c.TryMatch('='))
        {
            Emit(JsTokenKind.GtEq, start);
            return;
        }

        JsTokenKind kind = c switch
        {
            '(' => JsTokenKind.LParen,
            ')' => JsTokenKind.RParen,
            '{' => JsTokenKind.LBrace,
            '}' => JsTokenKind.RBrace,
            '[' => JsTokenKind.LBracket,
            ']' => JsTokenKind.RBracket,
            ';' => JsTokenKind.Semicolon,
            ',' => JsTokenKind.Comma,
            '.' => JsTokenKind.Dot,
            '?' => JsTokenKind.Question,
            ':' => JsTokenKind.Colon,
            '=' => JsTokenKind.Eq,
            '!' => JsTokenKind.Bang,
            '<' => JsTokenKind.Lt,
            '>' => JsTokenKind.Gt,
            '+' => JsTokenKind.Plus,
            '-' => JsTokenKind.Minus,
            '*' => JsTokenKind.Star,
            '/' => JsTokenKind.Slash,
            '%' => JsTokenKind.Percent,
            '&' => JsTokenKind.Amp,
            '|' => JsTokenKind.Pipe,
            '^' => JsTokenKind.Caret,
            '~' => JsTokenKind.Tilde,
            _ => JsTokenKind.Other,
        };
        Emit(kind, start);
    }

    private void Emit(JsTokenKind kind, TextSpan span, string? text = null)
    {
        Token t = new((int)kind, span, text);
        _tokens.Add(t);
        _prevSignificant = t;
    }

    private void Emit(JsTokenKind kind, int start)
    {
        Emit(kind, new TextSpan(start, _c.Position - start));
    }
}
