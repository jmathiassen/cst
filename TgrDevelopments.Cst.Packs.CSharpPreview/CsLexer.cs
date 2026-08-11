using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.CSharpPreview;

/// <summary>Hand-written C# lexer. Skips whitespace and comments as trivia.</summary>
internal sealed class CsLexer
{
    private static readonly Dictionary<string, CsTokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["namespace"] = CsTokenKind.KwNamespace,
        ["class"] = CsTokenKind.KwClass,
        ["struct"] = CsTokenKind.KwStruct,
        ["interface"] = CsTokenKind.KwInterface,
        ["record"] = CsTokenKind.KwRecord,
        ["enum"] = CsTokenKind.KwEnum,
        ["partial"] = CsTokenKind.KwPartial,
        ["static"] = CsTokenKind.KwStatic,
        ["abstract"] = CsTokenKind.KwAbstract,
        ["sealed"] = CsTokenKind.KwSealed,
        ["public"] = CsTokenKind.KwPublic,
        ["private"] = CsTokenKind.KwPrivate,
        ["protected"] = CsTokenKind.KwProtected,
        ["internal"] = CsTokenKind.KwInternal,
        ["file"] = CsTokenKind.KwFile,
        ["async"] = CsTokenKind.KwAsync,
        ["virtual"] = CsTokenKind.KwVirtual,
        ["override"] = CsTokenKind.KwOverride,
        ["new"] = CsTokenKind.KwNew,
        ["readonly"] = CsTokenKind.KwReadonly,
        ["required"] = CsTokenKind.KwRequired,
        ["where"] = CsTokenKind.KwWhere,
        ["using"] = CsTokenKind.KwUsing,
        ["extern"] = CsTokenKind.KwExtern,
        ["get"] = CsTokenKind.KwGet,
        ["set"] = CsTokenKind.KwSet,
        ["init"] = CsTokenKind.KwInit,
        ["event"] = CsTokenKind.KwEvent,
        ["delegate"] = CsTokenKind.KwDelegate,
        ["operator"] = CsTokenKind.KwOperator,
        ["implicit"] = CsTokenKind.KwImplicit,
        ["explicit"] = CsTokenKind.KwExplicit,
        ["void"] = CsTokenKind.KwVoid,
        ["if"] = CsTokenKind.KwIf,
        ["else"] = CsTokenKind.KwElse,
        ["for"] = CsTokenKind.KwFor,
        ["foreach"] = CsTokenKind.KwForeach,
        ["while"] = CsTokenKind.KwWhile,
        ["do"] = CsTokenKind.KwDo,
        ["switch"] = CsTokenKind.KwSwitch,
        ["case"] = CsTokenKind.KwCase,
        ["return"] = CsTokenKind.KwReturn,
        ["throw"] = CsTokenKind.KwThrow,
        ["try"] = CsTokenKind.KwTry,
        ["catch"] = CsTokenKind.KwCatch,
        ["finally"] = CsTokenKind.KwFinally,
        ["lock"] = CsTokenKind.KwLock,
        ["fixed"] = CsTokenKind.KwFixed,
        ["unsafe"] = CsTokenKind.KwUnsafe,
        ["checked"] = CsTokenKind.KwChecked,
        ["unchecked"] = CsTokenKind.KwUnchecked,
        ["default"] = CsTokenKind.KwDefault,
        ["this"] = CsTokenKind.KwThis,
        ["base"] = CsTokenKind.KwBase,
        ["null"] = CsTokenKind.KwNull,
        ["true"] = CsTokenKind.KwTrue,
        ["false"] = CsTokenKind.KwFalse,
        ["is"] = CsTokenKind.KwIs,
        ["as"] = CsTokenKind.KwAs,
        ["in"] = CsTokenKind.KwIn,
        ["out"] = CsTokenKind.KwOut,
        ["ref"] = CsTokenKind.KwRef,
        ["params"] = CsTokenKind.KwParams,
        ["typeof"] = CsTokenKind.KwTypeof,
        ["sizeof"] = CsTokenKind.KwSizeof,
        ["nameof"] = CsTokenKind.KwNameof,
        ["when"] = CsTokenKind.KwWhen,
        ["with"] = CsTokenKind.KwWith,
        ["and"] = CsTokenKind.KwAnd,
        ["or"] = CsTokenKind.KwOr,
        ["not"] = CsTokenKind.KwNot,
    };

    private readonly LexCursor _c;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly List<Token> _tokens = [];
    private bool _partial;

    public CsLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _c = new LexCursor(text, ct);
        _diagnostics = diagnostics;
    }

    public bool IsPartial => _partial;

    public IReadOnlyList<Token> Tokenize()
    {
        while (!_c.IsEof)
        {
            _c.ThrowIfCancellationRequested();
            _c.SkipWhitespace();
            if (_c.IsEof)
                break;

            char ch = _c.Peek();

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
                        "CS001", "Unclosed block comment.",
                        new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                }
                continue;
            }

            // preprocessor — skip whole line
            if (ch == '#')
            {
                _c.SkipUntilNewline();
                if (_c.Peek() is '\r' or '\n')
                    _c.Advance();
                continue;
            }

            // verbatim / interpolated strings
            if (ch == '@' && _c.Peek(1) == '"')
            {
                EmitVerbatimString();
                continue;
            }
            if (ch == '$' && (_c.Peek(1) == '"' || (_c.Peek(1) == '@' && _c.Peek(2) == '"')))
            {
                EmitInterpolated();
                continue;
            }
            if (ch == '@' && _c.Peek(1) == '$' && _c.Peek(2) == '"')
            {
                EmitInterpolated();
                continue;
            }

            if (ch == '"' && _c.Peek(1) == '"' && _c.Peek(2) == '"')
            {
                EmitRawString();
                continue;
            }

            if (ch == '"')
            {
                int start = _c.Position;
                TextSpan span = _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
                if (unclosed)
                {
                    _partial = true;
                    _diagnostics.Add(new ParseDiagnostic(
                        "CS001", "Unclosed string literal.",
                        span, DiagnosticSeverity.Error));
                }
                Emit(CsTokenKind.StringLiteral, span);
                continue;
            }

            if (ch == '\'')
            {
                int start = _c.Position;
                _c.Advance();
                while (!_c.IsEof && _c.Peek() != '\'')
                {
                    if (_c.Peek() == '\\')
                    {
                        _c.Advance();
                        if (!_c.IsEof)
                            _c.Advance();
                        continue;
                    }
                    if (_c.Peek() is '\n' or '\r')
                        break;
                    _c.Advance();
                }
                if (_c.Peek() == '\'')
                    _c.Advance();
                else
                    _partial = true;
                Emit(CsTokenKind.CharLiteral, new TextSpan(start, _c.Position - start));
                continue;
            }

            if (LexCursor.IsDigit(ch))
            {
                TextSpan span = _c.ReadNumber();
                Emit(CsTokenKind.NumericLiteral, span);
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                TextSpan span = _c.ReadIdentifier();
                string text = _c.Slice(span);
                // @identifier
                CsTokenKind kind = Keywords.TryGetValue(text, out CsTokenKind kw)
                    ? kw
                    : CsTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            if (ch == '@' && (char.IsLetter(_c.Peek(1)) || _c.Peek(1) == '_'))
            {
                int start = _c.Position;
                _c.Advance(); // @
                TextSpan id = _c.ReadIdentifier();
                Emit(CsTokenKind.Identifier, new TextSpan(start, _c.Position - start), _c.Slice(id));
                continue;
            }

            EmitOperatorOrPunct();
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void EmitVerbatimString()
    {
        int start = _c.Position;
        _c.Advance(); // @
        _c.Advance(); // "
        while (!_c.IsEof)
        {
            char c = _c.Peek();
            if (c == '"' && _c.Peek(1) == '"')
            {
                _c.Advance();
                _c.Advance();
                continue;
            }
            if (c == '"')
            {
                _c.Advance();
                Emit(CsTokenKind.StringLiteral, new TextSpan(start, _c.Position - start));
                return;
            }
            _c.Advance();
        }
        _partial = true;
        Emit(CsTokenKind.StringLiteral, new TextSpan(start, _c.Position - start));
    }

    private void EmitRawString()
    {
        int start = _c.Position;
        int openQuoteCount = 0;
        while (!_c.IsEof && _c.Peek() == '"')
        {
            _c.Advance();
            openQuoteCount++;
        }

        while (!_c.IsEof)
        {
            if (_c.Peek() == '"')
            {
                bool matched = true;
                for (int i = 0; i < openQuoteCount; i++)
                {
                    if (_c.Peek(i) != '"')
                    {
                        matched = false;
                        break;
                    }
                }
                if (matched)
                {
                    for (int i = 0; i < openQuoteCount; i++)
                        _c.Advance();
                    Emit(CsTokenKind.RawStringLiteral, new TextSpan(start, _c.Position - start));
                    return;
                }
            }
            _c.Advance();
        }

        _partial = true;
        _diagnostics.Add(new ParseDiagnostic(
            "CS001", "Unclosed raw string literal.",
            new TextSpan(start, _c.Position - start), DiagnosticSeverity.Error));
        Emit(CsTokenKind.RawStringLiteral, new TextSpan(start, _c.Position - start));
    }

    private void EmitInterpolated()
    {
        // coarse: scan until unescaped closing " with brace depth for holes
        int start = _c.Position;
        // consume prefix $@ or @$ or $
        if (_c.Peek() == '@')
            _c.Advance();
        if (_c.Peek() == '$')
            _c.Advance();
        if (_c.Peek() == '@')
            _c.Advance();
        if (_c.Peek() != '"')
        {
            Emit(CsTokenKind.Other, start);
            return;
        }
        _c.Advance(); // "
        int brace = 0;
        while (!_c.IsEof)
        {
            char c = _c.Peek();
            if (c == '\\' && brace == 0)
            {
                _c.Advance();
                if (!_c.IsEof)
                    _c.Advance();
                continue;
            }
            if (c == '{' && _c.Peek(1) == '{')
            {
                _c.Advance();
                _c.Advance();
                continue;
            }
            if (c == '}' && _c.Peek(1) == '}')
            {
                _c.Advance();
                _c.Advance();
                continue;
            }
            if (c == '{')
            {
                brace++;
                _c.Advance();
                continue;
            }
            if (c == '}' && brace > 0)
            {
                brace--;
                _c.Advance();
                continue;
            }
            if (c == '"' && brace == 0)
            {
                _c.Advance();
                Emit(CsTokenKind.InterpolatedString, new TextSpan(start, _c.Position - start));
                return;
            }
            _c.Advance();
        }
        _partial = true;
        Emit(CsTokenKind.InterpolatedString, new TextSpan(start, _c.Position - start));
    }

    private void EmitOperatorOrPunct()
    {
        int start = _c.Position;
        char c = _c.Advance();

        if (c == '=' && _c.TryMatch('>'))
        {
            Emit(CsTokenKind.Arrow, start);
            return;
        }
        if (c == ':' && _c.TryMatch(':'))
        {
            Emit(CsTokenKind.ColonColon, start);
            return;
        }
        if (c == '?' && _c.TryMatch('?'))
        {
            Emit(CsTokenKind.QuestionQuestion, start);
            return;
        }
        if (c == '?' && _c.TryMatch('.'))
        {
            Emit(CsTokenKind.QuestionDot, start);
            return;
        }
        if (c == '=' && _c.TryMatch('='))
        {
            Emit(CsTokenKind.EqEq, start);
            return;
        }
        if (c == '!' && _c.TryMatch('='))
        {
            Emit(CsTokenKind.BangEq, start);
            return;
        }
        if (c == '<' && _c.TryMatch('<'))
        {
            Emit(CsTokenKind.LtLt, start);
            return;
        }
        if (c == '>' && _c.Peek() == '>')
        {
            _c.Advance();
            if (_c.TryMatch('>'))
                Emit(CsTokenKind.GtGtGt, start);
            else
                Emit(CsTokenKind.GtGt, start);
            return;
        }
        if (c == '&' && _c.TryMatch('&'))
        {
            Emit(CsTokenKind.AmpAmp, start);
            return;
        }
        if (c == '|' && _c.TryMatch('|'))
        {
            Emit(CsTokenKind.PipePipe, start);
            return;
        }
        if (c == '+' && _c.TryMatch('+'))
        {
            Emit(CsTokenKind.PlusPlus, start);
            return;
        }
        if (c == '-' && _c.TryMatch('-'))
        {
            Emit(CsTokenKind.MinusMinus, start);
            return;
        }
        if (c == '<' && _c.TryMatch('='))
        {
            Emit(CsTokenKind.LtEq, start);
            return;
        }
        if (c == '>' && _c.TryMatch('='))
        {
            Emit(CsTokenKind.GtEq, start);
            return;
        }

        CsTokenKind kind = c switch
        {
            '(' => CsTokenKind.LParen,
            ')' => CsTokenKind.RParen,
            '{' => CsTokenKind.LBrace,
            '}' => CsTokenKind.RBrace,
            '[' => CsTokenKind.LBracket,
            ']' => CsTokenKind.RBracket,
            ';' => CsTokenKind.Semicolon,
            ',' => CsTokenKind.Comma,
            '.' => CsTokenKind.Dot,
            '?' => CsTokenKind.Question,
            ':' => CsTokenKind.Colon,
            '=' => CsTokenKind.Eq,
            '!' => CsTokenKind.Bang,
            '<' => CsTokenKind.Lt,
            '>' => CsTokenKind.Gt,
            '+' => CsTokenKind.Plus,
            '-' => CsTokenKind.Minus,
            '*' => CsTokenKind.Star,
            '/' => CsTokenKind.Slash,
            '%' => CsTokenKind.Percent,
            '&' => CsTokenKind.Amp,
            '|' => CsTokenKind.Pipe,
            '^' => CsTokenKind.Caret,
            '~' => CsTokenKind.Tilde,
            _ => CsTokenKind.Other,
        };
        Emit(kind, start);
    }

    private void Emit(CsTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
    }

    private void Emit(CsTokenKind kind, int start)
    {
        Emit(kind, new TextSpan(start, _c.Position - start));
    }
}
