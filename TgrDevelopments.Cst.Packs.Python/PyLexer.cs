using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Python;

/// <summary>
/// Hand-written Python lexer with NEWLINE / INDENT / DEDENT (CPython-style, simplified).
/// Skips comments; tracks implicit line joining inside (), [], {}.
/// </summary>
internal sealed class PyLexer
{
    private static readonly Dictionary<string, PyTokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["class"] = PyTokenKind.KwClass,
        ["def"] = PyTokenKind.KwDef,
        ["async"] = PyTokenKind.KwAsync,
        ["lambda"] = PyTokenKind.KwLambda,
        ["return"] = PyTokenKind.KwReturn,
        ["if"] = PyTokenKind.KwIf,
        ["elif"] = PyTokenKind.KwElif,
        ["else"] = PyTokenKind.KwElse,
        ["for"] = PyTokenKind.KwFor,
        ["while"] = PyTokenKind.KwWhile,
        ["try"] = PyTokenKind.KwTry,
        ["except"] = PyTokenKind.KwExcept,
        ["finally"] = PyTokenKind.KwFinally,
        ["with"] = PyTokenKind.KwWith,
        ["as"] = PyTokenKind.KwAs,
        ["import"] = PyTokenKind.KwImport,
        ["from"] = PyTokenKind.KwFrom,
        ["pass"] = PyTokenKind.KwPass,
        ["raise"] = PyTokenKind.KwRaise,
        ["yield"] = PyTokenKind.KwYield,
        ["break"] = PyTokenKind.KwBreak,
        ["continue"] = PyTokenKind.KwContinue,
        ["global"] = PyTokenKind.KwGlobal,
        ["nonlocal"] = PyTokenKind.KwNonlocal,
        ["assert"] = PyTokenKind.KwAssert,
        ["del"] = PyTokenKind.KwDel,
        ["in"] = PyTokenKind.KwIn,
        ["is"] = PyTokenKind.KwIs,
        ["not"] = PyTokenKind.KwNot,
        ["and"] = PyTokenKind.KwAnd,
        ["or"] = PyTokenKind.KwOr,
        ["True"] = PyTokenKind.KwTrue,
        ["False"] = PyTokenKind.KwFalse,
        ["None"] = PyTokenKind.KwNone,
        ["match"] = PyTokenKind.KwMatch,
        ["case"] = PyTokenKind.KwCase,
    };

    private readonly LexCursor _c;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly List<Token> _tokens = [];
    private readonly Stack<int> _indents = new();
    private bool _partial;
    private bool _atLineStart = true;
    private int _parenDepth;

    public PyLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _c = new LexCursor(text, ct);
        _diagnostics = diagnostics;
        _indents.Push(0);
    }

    public bool IsPartial => _partial;

    public IReadOnlyList<Token> Tokenize()
    {
        // shebang / encoding comment on first lines handled as comments
        while (!_c.IsEof)
        {
            _c.ThrowIfCancellationRequested();

            if (_atLineStart && _parenDepth == 0)
            {
                if (!EmitIndentation())
                    continue; // blank or comment-only line
            }

            _c.SkipWhitespaceHorizontal();
            if (_c.IsEof)
                break;

            char ch = _c.Peek();

            if (ch == '#')
            {
                _c.SkipHashComment();
                continue;
            }

            if (ch is '\r' or '\n')
            {
                ConsumeNewline(emitToken: _parenDepth == 0);
                continue;
            }

            _atLineStart = false;

            // string prefixes: r, b, f, u, fr, rf, br, rb (case-insensitive-ish simplified)
            if (IsStringStart())
            {
                EmitString();
                continue;
            }

            if (LexCursor.IsDigit(ch) || (ch == '.' && LexCursor.IsDigit(_c.Peek(1))))
            {
                TextSpan span = _c.ReadNumber();
                Emit(PyTokenKind.NumericLiteral, span);
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                TextSpan span = _c.ReadIdentifier();
                string text = _c.Slice(span);
                PyTokenKind kind = Keywords.TryGetValue(text, out PyTokenKind kw)
                    ? kw
                    : PyTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            EmitOperatorOrPunct();
        }

        // trailing newline if last line had content without NL
        if (!_atLineStart && _parenDepth == 0)
            Emit(PyTokenKind.Newline, new TextSpan(_c.Position, 0));

        // close remaining indents
        while (_indents.Count > 1)
        {
            _indents.Pop();
            Emit(PyTokenKind.Dedent, new TextSpan(_c.Position, 0));
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    /// <summary>Returns false if the line was blank/comment-only (no INDENT emit needed beyond skip).</summary>
    private bool EmitIndentation()
    {
        int start = _c.Position;
        int col = 0;
        while (!_c.IsEof)
        {
            char c = _c.Peek();
            if (c == ' ')
            {
                col++;
                _c.Advance();
            }
            else if (c == '\t')
            {
                // treat tab as 8 spaces (common); mark partial if mixed later
                col = (col / 8 + 1) * 8;
                _c.Advance();
            }
            else
                break;
        }

        if (_c.IsEof)
            return false;

        char ch = _c.Peek();
        if (ch is '\r' or '\n')
        {
            // blank line — don't change indent stack
            ConsumeNewline(emitToken: false);
            return false;
        }
        if (ch == '#')
        {
            _c.SkipHashComment();
            if (_c.Peek() is '\r' or '\n')
                ConsumeNewline(emitToken: false);
            return false;
        }

        int current = _indents.Peek();
        if (col == current)
        {
            _atLineStart = false;
            return true;
        }

        if (col > current)
        {
            _indents.Push(col);
            Emit(PyTokenKind.Indent, new TextSpan(start, _c.Position - start));
            _atLineStart = false;
            return true;
        }

        // dedent one or more levels
        while (_indents.Count > 1 && _indents.Peek() > col)
        {
            _indents.Pop();
            Emit(PyTokenKind.Dedent, new TextSpan(start, 0));
        }

        if (_indents.Peek() != col)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "PY001", "Inconsistent indentation.",
                new TextSpan(start, Math.Max(0, _c.Position - start)),
                DiagnosticSeverity.Error));
            // force-align
            _indents.Push(col);
        }

        _atLineStart = false;
        return true;
    }

    private void ConsumeNewline(bool emitToken)
    {
        int start = _c.Position;
        if (_c.Peek() == '\r')
            _c.Advance();
        if (_c.Peek() == '\n')
            _c.Advance();
        if (emitToken)
            Emit(PyTokenKind.Newline, new TextSpan(start, _c.Position - start));
        _atLineStart = true;
    }

    private bool IsStringStart()
    {
        char c = _c.Peek();
        if (c is '"' or '\'')
            return true;
        // prefixes
        if (c is 'r' or 'R' or 'b' or 'B' or 'f' or 'F' or 'u' or 'U')
        {
            char n1 = _c.Peek(1);
            if (n1 is '"' or '\'')
                return true;
            if (n1 is 'r' or 'R' or 'b' or 'B' or 'f' or 'F')
            {
                char n2 = _c.Peek(2);
                if (n2 is '"' or '\'')
                    return true;
            }
        }
        return false;
    }

    private void EmitString()
    {
        int start = _c.Position;
        // skip prefixes
        while (!_c.IsEof && _c.Peek() is not '"' and not '\'')
        {
            char p = _c.Peek();
            if (p is 'r' or 'R' or 'b' or 'B' or 'f' or 'F' or 'u' or 'U')
                _c.Advance();
            else
                break;
        }

        if (_c.IsEof || _c.Peek() is not '"' and not '\'')
        {
            Emit(PyTokenKind.Other, start);
            return;
        }

        char quote = _c.Peek();
        bool triple = _c.Peek(1) == quote && _c.Peek(2) == quote;
        if (triple)
        {
            _c.Advance();
            _c.Advance();
            _c.Advance();
            while (!_c.IsEof)
            {
                if (_c.Peek() == '\\')
                {
                    _c.Advance();
                    if (!_c.IsEof)
                        _c.Advance();
                    continue;
                }
                if (_c.Peek() == quote && _c.Peek(1) == quote && _c.Peek(2) == quote)
                {
                    _c.Advance();
                    _c.Advance();
                    _c.Advance();
                    Emit(PyTokenKind.StringLiteral, new TextSpan(start, _c.Position - start));
                    return;
                }
                _c.Advance();
            }
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "PY001", "Unclosed triple-quoted string.",
                new TextSpan(start, _c.Position - start), DiagnosticSeverity.Error));
            Emit(PyTokenKind.StringLiteral, new TextSpan(start, _c.Position - start));
            return;
        }

        TextSpan span = _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
        // ReadQuotedString starts at quote — but we may have advanced prefixes only.
        // Rebuild span from start.
        if (unclosed)
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic(
                "PY001", "Unclosed string literal.",
                new TextSpan(start, _c.Position - start), DiagnosticSeverity.Error));
        }
        Emit(PyTokenKind.StringLiteral, new TextSpan(start, _c.Position - start));
        _ = span;
    }

    private void EmitOperatorOrPunct()
    {
        int start = _c.Position;
        char c = _c.Advance();

        if (c == '-' && _c.TryMatch('>'))
        {
            Emit(PyTokenKind.Arrow, start);
            return;
        }
        if (c == ':' && _c.TryMatch('='))
        {
            Emit(PyTokenKind.ColonEq, start);
            return;
        }
        if (c == '.' && _c.Peek() == '.' && _c.Peek(1) == '.')
        {
            _c.Advance();
            _c.Advance();
            Emit(PyTokenKind.Ellipsis, start);
            return;
        }
        if (c == '*' && _c.TryMatch('*'))
        {
            Emit(PyTokenKind.StarStar, start);
            return;
        }
        if (c == '/' && _c.TryMatch('/'))
        {
            Emit(PyTokenKind.SlashSlash, start);
            return;
        }
        if (c == '=' && _c.TryMatch('='))
        {
            Emit(PyTokenKind.EqEq, start);
            return;
        }
        if (c == '!' && _c.TryMatch('='))
        {
            Emit(PyTokenKind.BangEq, start);
            return;
        }
        if (c == '<' && _c.TryMatch('<'))
        {
            Emit(PyTokenKind.LtLt, start);
            return;
        }
        if (c == '>' && _c.TryMatch('>'))
        {
            Emit(PyTokenKind.GtGt, start);
            return;
        }
        if (c == '<' && _c.TryMatch('='))
        {
            Emit(PyTokenKind.LtEq, start);
            return;
        }
        if (c == '>' && _c.TryMatch('='))
        {
            Emit(PyTokenKind.GtEq, start);
            return;
        }

        PyTokenKind kind = c switch
        {
            '(' => PyTokenKind.LParen,
            ')' => PyTokenKind.RParen,
            '{' => PyTokenKind.LBrace,
            '}' => PyTokenKind.RBrace,
            '[' => PyTokenKind.LBracket,
            ']' => PyTokenKind.RBracket,
            ':' => PyTokenKind.Colon,
            ',' => PyTokenKind.Comma,
            '.' => PyTokenKind.Dot,
            ';' => PyTokenKind.Semicolon,
            '@' => PyTokenKind.At,
            '=' => PyTokenKind.Eq,
            '<' => PyTokenKind.Lt,
            '>' => PyTokenKind.Gt,
            '+' => PyTokenKind.Plus,
            '-' => PyTokenKind.Minus,
            '*' => PyTokenKind.Star,
            '/' => PyTokenKind.Slash,
            '%' => PyTokenKind.Percent,
            '&' => PyTokenKind.Amp,
            '|' => PyTokenKind.Pipe,
            '^' => PyTokenKind.Caret,
            '~' => PyTokenKind.Tilde,
            _ => PyTokenKind.Other,
        };

        if (kind == PyTokenKind.LParen || kind == PyTokenKind.LBrace || kind == PyTokenKind.LBracket)
            _parenDepth++;
        else if (kind == PyTokenKind.RParen || kind == PyTokenKind.RBrace || kind == PyTokenKind.RBracket)
            _parenDepth = Math.Max(0, _parenDepth - 1);

        Emit(kind, start);
    }

    private void Emit(PyTokenKind kind, TextSpan span, string? text = null)
    {
        _tokens.Add(new Token((int)kind, span, text));
    }

    private void Emit(PyTokenKind kind, int start)
    {
        Emit(kind, new TextSpan(start, _c.Position - start));
    }
}
