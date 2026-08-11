using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.TypeScript;

/// <summary>Hand-written TypeScript lexer. Skips whitespace and comments as trivia (not emitted).</summary>
internal sealed class TsLexer
{
    private static readonly Dictionary<string, TsTokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["function"] = TsTokenKind.KwFunction,
        ["class"] = TsTokenKind.KwClass,
        ["async"] = TsTokenKind.KwAsync,
        ["static"] = TsTokenKind.KwStatic,
        ["get"] = TsTokenKind.KwGet,
        ["set"] = TsTokenKind.KwSet,
        ["constructor"] = TsTokenKind.KwConstructor,
        ["extends"] = TsTokenKind.KwExtends,
        ["export"] = TsTokenKind.KwExport,
        ["default"] = TsTokenKind.KwDefault,
        ["import"] = TsTokenKind.KwImport,
        ["from"] = TsTokenKind.KwFrom,
        ["const"] = TsTokenKind.KwConst,
        ["let"] = TsTokenKind.KwLet,
        ["var"] = TsTokenKind.KwVar,
        ["return"] = TsTokenKind.KwReturn,
        ["if"] = TsTokenKind.KwIf,
        ["else"] = TsTokenKind.KwElse,
        ["for"] = TsTokenKind.KwFor,
        ["while"] = TsTokenKind.KwWhile,
        ["do"] = TsTokenKind.KwDo,
        ["switch"] = TsTokenKind.KwSwitch,
        ["case"] = TsTokenKind.KwCase,
        ["break"] = TsTokenKind.KwBreak,
        ["continue"] = TsTokenKind.KwContinue,
        ["try"] = TsTokenKind.KwTry,
        ["catch"] = TsTokenKind.KwCatch,
        ["finally"] = TsTokenKind.KwFinally,
        ["throw"] = TsTokenKind.KwThrow,
        ["new"] = TsTokenKind.KwNew,
        ["this"] = TsTokenKind.KwThis,
        ["super"] = TsTokenKind.KwSuper,
        ["typeof"] = TsTokenKind.KwTypeof,
        ["instanceof"] = TsTokenKind.KwInstanceof,
        ["void"] = TsTokenKind.KwVoid,
        ["delete"] = TsTokenKind.KwDelete,
        ["in"] = TsTokenKind.KwIn,
        ["of"] = TsTokenKind.KwOf,
        ["yield"] = TsTokenKind.KwYield,
        ["await"] = TsTokenKind.KwAwait,
        ["with"] = TsTokenKind.KwWith,
        ["debugger"] = TsTokenKind.KwDebugger,
        ["true"] = TsTokenKind.KwTrue,
        ["false"] = TsTokenKind.KwFalse,
        ["null"] = TsTokenKind.KwNull,
        ["undefined"] = TsTokenKind.KwUndefined,
        ["interface"] = TsTokenKind.KwInterface,
        ["type"] = TsTokenKind.KwType,
        ["enum"] = TsTokenKind.KwEnum,
        ["namespace"] = TsTokenKind.KwNamespace,
        ["module"] = TsTokenKind.KwModule,
        ["implements"] = TsTokenKind.KwImplements,
        ["declare"] = TsTokenKind.KwDeclare,
        ["abstract"] = TsTokenKind.KwAbstract,
        ["readonly"] = TsTokenKind.KwReadonly,
        ["public"] = TsTokenKind.KwPublic,
        ["private"] = TsTokenKind.KwPrivate,
        ["protected"] = TsTokenKind.KwProtected,
        ["override"] = TsTokenKind.KwOverride,
    };

    private readonly LexCursor _c;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly List<Token> _tokens = [];
    private bool _partial;
    private Token? _prevSignificant;

    public TsLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
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
                        "TS001", "Unclosed block comment.",
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
                        "TS001", "Unclosed string literal.",
                        span, DiagnosticSeverity.Error));
                }
                Emit(TsTokenKind.StringLiteral, span);
                continue;
            }

            if (ch == '`')
            {
                TextSpan span = _c.ReadTemplateString(out bool unclosed);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic(
                        "TS001", "Unclosed template literal.",
                        span, DiagnosticSeverity.Error));
                }
                Emit(TsTokenKind.TemplateLiteral, span);
                continue;
            }

            if (LexCursor.IsDigit(ch) || (ch == '.' && LexCursor.IsDigit(_c.Peek(1))))
            {
                TextSpan span = _c.ReadNumber();
                Emit(TsTokenKind.NumericLiteral, span);
                continue;
            }

            if (char.IsLetter(ch) || ch is '_' or '$')
            {
                TextSpan span = _c.ReadIdentifier();
                string text = _c.Slice(span);
                TsTokenKind kind = Keywords.TryGetValue(text, out TsTokenKind kw)
                    ? kw
                    : TsTokenKind.Identifier;
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
        TsTokenKind k = (TsTokenKind)_prevSignificant.Value.Kind;
        return k is TsTokenKind.LParen or TsTokenKind.LBracket or TsTokenKind.LBrace
            or TsTokenKind.Comma or TsTokenKind.Colon or TsTokenKind.Semicolon
            or TsTokenKind.Eq or TsTokenKind.EqEq or TsTokenKind.EqEqEq
            or TsTokenKind.Bang or TsTokenKind.BangEq or TsTokenKind.BangEqEq
            or TsTokenKind.Lt or TsTokenKind.LtEq or TsTokenKind.Gt or TsTokenKind.GtEq
            or TsTokenKind.Plus or TsTokenKind.Minus or TsTokenKind.Star or TsTokenKind.Slash
            or TsTokenKind.Percent or TsTokenKind.Amp or TsTokenKind.AmpAmp
            or TsTokenKind.Pipe or TsTokenKind.PipePipe or TsTokenKind.Caret
            or TsTokenKind.Tilde or TsTokenKind.Arrow or TsTokenKind.Question
            or TsTokenKind.KwReturn or TsTokenKind.KwThrow or TsTokenKind.KwCase
            or TsTokenKind.KwElse or TsTokenKind.KwIn or TsTokenKind.KwOf
            or TsTokenKind.KwTypeof or TsTokenKind.KwVoid or TsTokenKind.KwDelete
            or TsTokenKind.KwAwait or TsTokenKind.KwYield or TsTokenKind.KwNew;
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
                Emit(TsTokenKind.RegexLiteral, new TextSpan(start, _c.Position - start));
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
        Emit(TsTokenKind.RegexLiteral, new TextSpan(start, _c.Position - start));
    }

    private void EmitOperatorOrPunct()
    {
        int start = _c.Position;
        char c = _c.Advance();

        // multi-char
        if (c == '=' && _c.TryMatch('>'))
        {
            Emit(TsTokenKind.Arrow, start);
            return;
        }
        if (c == '.' && _c.Peek() == '.' && _c.Peek(1) == '.')
        {
            _c.Advance();
            _c.Advance();
            Emit(TsTokenKind.Ellipsis, start);
            return;
        }
        if (c == '?' && _c.TryMatch('?'))
        {
            Emit(TsTokenKind.QuestionQuestion, start);
            return;
        }
        if (c == '?' && _c.TryMatch('.'))
        {
            Emit(TsTokenKind.QuestionDot, start);
            return;
        }
        if (c == '=' && _c.Peek() == '=')
        {
            _c.Advance();
            if (_c.TryMatch('='))
                Emit(TsTokenKind.EqEqEq, start);
            else
                Emit(TsTokenKind.EqEq, start);
            return;
        }
        if (c == '!' && _c.Peek() == '=')
        {
            _c.Advance();
            if (_c.TryMatch('='))
                Emit(TsTokenKind.BangEqEq, start);
            else
                Emit(TsTokenKind.BangEq, start);
            return;
        }
        if (c == '<' && _c.TryMatch('<'))
        {
            Emit(TsTokenKind.LtLt, start);
            return;
        }
        if (c == '>' && _c.Peek() == '>')
        {
            _c.Advance();
            if (_c.TryMatch('>'))
                Emit(TsTokenKind.GtGtGt, start);
            else
                Emit(TsTokenKind.GtGt, start);
            return;
        }
        if (c == '&' && _c.TryMatch('&'))
        {
            Emit(TsTokenKind.AmpAmp, start);
            return;
        }
        if (c == '|' && _c.TryMatch('|'))
        {
            Emit(TsTokenKind.PipePipe, start);
            return;
        }
        if (c == '+' && _c.TryMatch('+'))
        {
            Emit(TsTokenKind.PlusPlus, start);
            return;
        }
        if (c == '-' && _c.TryMatch('-'))
        {
            Emit(TsTokenKind.MinusMinus, start);
            return;
        }
        if (c == '*' && _c.TryMatch('*'))
        {
            Emit(TsTokenKind.StarStar, start);
            return;
        }
        if (c == '+' && _c.TryMatch('='))
        {
            Emit(TsTokenKind.PlusEq, start);
            return;
        }
        if (c == '-' && _c.TryMatch('='))
        {
            Emit(TsTokenKind.MinusEq, start);
            return;
        }
        if (c == '*' && _c.TryMatch('='))
        {
            Emit(TsTokenKind.StarEq, start);
            return;
        }
        if (c == '/' && _c.TryMatch('='))
        {
            Emit(TsTokenKind.SlashEq, start);
            return;
        }
        if (c == '%' && _c.TryMatch('='))
        {
            Emit(TsTokenKind.PercentEq, start);
            return;
        }
        if (c == '<' && _c.TryMatch('='))
        {
            Emit(TsTokenKind.LtEq, start);
            return;
        }
        if (c == '>' && _c.TryMatch('='))
        {
            Emit(TsTokenKind.GtEq, start);
            return;
        }

        TsTokenKind kind = c switch
        {
            '(' => TsTokenKind.LParen,
            ')' => TsTokenKind.RParen,
            '{' => TsTokenKind.LBrace,
            '}' => TsTokenKind.RBrace,
            '[' => TsTokenKind.LBracket,
            ']' => TsTokenKind.RBracket,
            ';' => TsTokenKind.Semicolon,
            ',' => TsTokenKind.Comma,
            '.' => TsTokenKind.Dot,
            '?' => TsTokenKind.Question,
            ':' => TsTokenKind.Colon,
            '=' => TsTokenKind.Eq,
            '!' => TsTokenKind.Bang,
            '<' => TsTokenKind.Lt,
            '>' => TsTokenKind.Gt,
            '+' => TsTokenKind.Plus,
            '-' => TsTokenKind.Minus,
            '*' => TsTokenKind.Star,
            '/' => TsTokenKind.Slash,
            '%' => TsTokenKind.Percent,
            '&' => TsTokenKind.Amp,
            '|' => TsTokenKind.Pipe,
            '^' => TsTokenKind.Caret,
            '~' => TsTokenKind.Tilde,
            _ => TsTokenKind.Other,
        };
        Emit(kind, start);
    }

    private void Emit(TsTokenKind kind, TextSpan span, string? text = null)
    {
        Token t = new((int)kind, span, text);
        _tokens.Add(t);
        _prevSignificant = t;
    }

    private void Emit(TsTokenKind kind, int start)
    {
        Emit(kind, new TextSpan(start, _c.Position - start));
    }
}
