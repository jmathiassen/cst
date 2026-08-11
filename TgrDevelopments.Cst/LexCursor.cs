using System;
using System.Globalization;
using System.Text;
using System.Threading;

namespace TgrDevelopments.Cst;

/// <summary>
/// Shared character cursor for hand-written lexers. Packs own token kinds and language rules;
/// this type supplies safe peek/advance and common literal/comment scanners.
/// </summary>
public sealed class LexCursor
{
    private readonly string _text;
    private readonly CancellationToken _ct;
    private int _pos;

    /// <summary>Creates a cursor over <paramref name="text"/>.</summary>
    public LexCursor(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _ct = cancellationToken;
        _pos = 0;
    }

    /// <summary>Full source text.</summary>
    public string Text => _text;

    /// <summary>Current UTF-16 index.</summary>
    public int Position => _pos;

    /// <summary>True at end of input.</summary>
    public bool IsEof => _pos >= _text.Length;

    /// <summary>Throws if cancellation requested.</summary>
    public void ThrowIfCancellationRequested() => _ct.ThrowIfCancellationRequested();

    /// <summary>Peek character at current position, or <c>\0</c> at EOF.</summary>
    public char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

    /// <summary>Peek character at <paramref name="offset"/> from current, or <c>\0</c>.</summary>
    public char Peek(int offset)
    {
        int i = _pos + offset;
        return i >= 0 && i < _text.Length ? _text[i] : '\0';
    }

    /// <summary>Advance one character; returns it, or <c>\0</c> at EOF.</summary>
    public char Advance()
    {
        if (_pos >= _text.Length)
            return '\0';
        char c = _text[_pos];
        _pos++;
        return c;
    }

    /// <summary>Advance <paramref name="count"/> characters (clamped to EOF).</summary>
    public void Advance(int count)
    {
        if (count <= 0)
            return;
        _pos = Math.Min(_text.Length, _pos + count);
    }

    /// <summary>True if next chars equal <paramref name="s"/> (no advance).</summary>
    public bool StartsWith(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (_pos + s.Length > _text.Length)
            return false;
        return string.CompareOrdinal(_text, _pos, s, 0, s.Length) == 0;
    }

    /// <summary>If next chars equal <paramref name="s"/>, advance and return true.</summary>
    public bool TryMatch(string s)
    {
        if (!StartsWith(s))
            return false;
        _pos += s.Length;
        return true;
    }

    /// <summary>If peek equals <paramref name="c"/>, advance and return true.</summary>
    public bool TryMatch(char c)
    {
        if (Peek() != c)
            return false;
        _pos++;
        return true;
    }

    /// <summary>Skip ASCII whitespace (space, tab, CR, LF, FF, VT).</summary>
    public void SkipWhitespace()
    {
        while (_pos < _text.Length)
        {
            char c = _text[_pos];
            if (c is ' ' or '\t' or '\r' or '\n' or '\f' or '\v')
                _pos++;
            else
                break;
        }
    }

    /// <summary>Skip horizontal whitespace only (space, tab, FF, VT) — not CR/LF.</summary>
    public void SkipWhitespaceHorizontal()
    {
        while (_pos < _text.Length)
        {
            char c = _text[_pos];
            if (c is ' ' or '\t' or '\f' or '\v')
                _pos++;
            else
                break;
        }
    }

    /// <summary>Skip until end of line (does not consume the newline).</summary>
    public void SkipUntilNewline()
    {
        while (_pos < _text.Length)
        {
            char c = _text[_pos];
            if (c is '\n' or '\r')
                break;
            _pos++;
        }
    }

    /// <summary>
    /// Skip <c>//</c> line comment (call when at first <c>/</c>).
    /// Returns span of comment including <c>//</c>; leaves cursor at newline or EOF.
    /// </summary>
    public TextSpan SkipLineComment()
    {
        int start = _pos;
        if (!TryMatch("//"))
            return new TextSpan(start, 0);
        SkipUntilNewline();
        return new TextSpan(start, _pos - start);
    }

    /// <summary>
    /// Skip <c>/* … */</c> block comment (call when at <c>/</c>).
    /// Unclosed → consumes to EOF. Returns comment span.
    /// </summary>
    public TextSpan SkipBlockComment(out bool unclosed)
    {
        int start = _pos;
        unclosed = false;
        if (!TryMatch("/*"))
            return new TextSpan(start, 0);
        while (_pos < _text.Length)
        {
            _ct.ThrowIfCancellationRequested();
            if (TryMatch("*/"))
                return new TextSpan(start, _pos - start);
            _pos++;
        }
        unclosed = true;
        return new TextSpan(start, _pos - start);
    }

    /// <summary>
    /// Skip <c>#</c> to end of line (shell/python/yaml style). Call when at <c>#</c>.
    /// </summary>
    public TextSpan SkipHashComment()
    {
        int start = _pos;
        if (!TryMatch('#'))
            return new TextSpan(start, 0);
        SkipUntilNewline();
        return new TextSpan(start, _pos - start);
    }

    /// <summary>
    /// Read a double- or single-quoted string with <c>\\</c> escapes (not template literals).
    /// Call when at opening quote. Unclosed → to EOF / newline policy via <paramref name="allowMultiline"/>.
    /// </summary>
    public TextSpan ReadQuotedString(out bool unclosed, bool allowMultiline = false)
    {
        int start = _pos;
        unclosed = false;
        char q = Peek();
        if (q is not ('"' or '\''))
            return new TextSpan(start, 0);
        Advance();
        while (_pos < _text.Length)
        {
            _ct.ThrowIfCancellationRequested();
            char c = Advance();
            if (c == '\\' && _pos < _text.Length)
            {
                Advance();
                continue;
            }
            if (c == q)
                return new TextSpan(start, _pos - start);
            if (!allowMultiline && c is '\n' or '\r')
            {
                unclosed = true;
                return new TextSpan(start, _pos - start);
            }
        }
        unclosed = true;
        return new TextSpan(start, _pos - start);
    }

    /// <summary>
    /// Read JS/TS template literal <c>`…`</c> with best-effort <c>${}</c> nesting.
    /// Call when at opening backtick.
    /// </summary>
    public TextSpan ReadTemplateString(out bool unclosed)
    {
        int start = _pos;
        unclosed = false;
        if (!TryMatch('`'))
            return new TextSpan(start, 0);
        int depth = 0;
        while (_pos < _text.Length)
        {
            _ct.ThrowIfCancellationRequested();
            char c = Advance();
            if (c == '\\' && _pos < _text.Length)
            {
                Advance();
                continue;
            }
            if (c == '`' && depth == 0)
                return new TextSpan(start, _pos - start);
            if (c == '$' && Peek() == '{')
            {
                Advance();
                depth++;
                continue;
            }
            if (c == '{' && depth > 0)
            {
                depth++;
                continue;
            }
            if (c == '}' && depth > 0)
            {
                depth--;
                continue;
            }
        }
        unclosed = true;
        return new TextSpan(start, _pos - start);
    }

    /// <summary>
    /// Read C#-style verbatim string <c>@"…"</c> (doubled quotes). Call when at <c>@</c> before quote.
    /// </summary>
    public TextSpan ReadVerbatimString(out bool unclosed)
    {
        int start = _pos;
        unclosed = false;
        if (!TryMatch("@\""))
            return new TextSpan(start, 0);
        while (_pos < _text.Length)
        {
            _ct.ThrowIfCancellationRequested();
            char c = Advance();
            if (c == '"')
            {
                if (Peek() == '"')
                {
                    Advance();
                    continue;
                }
                return new TextSpan(start, _pos - start);
            }
        }
        unclosed = true;
        return new TextSpan(start, _pos - start);
    }

    /// <summary>Read identifier: letter/underscore/$ start, then letter/digit/_/$.</summary>
    public TextSpan ReadIdentifier()
    {
        int start = _pos;
        char c = Peek();
        if (!(char.IsLetter(c) || c is '_' or '$'))
            return new TextSpan(start, 0);
        Advance();
        while (_pos < _text.Length)
        {
            c = Peek();
            if (char.IsLetterOrDigit(c) || c is '_' or '$')
                Advance();
            else
                break;
        }
        return new TextSpan(start, _pos - start);
    }

    /// <summary>Slice text for span.</summary>
    public string Slice(TextSpan span)
    {
        if (span.Start < 0 || span.Length < 0 || span.End > _text.Length)
            return string.Empty;
        return _text.Substring(span.Start, span.Length);
    }

    /// <summary>True if <paramref name="c"/> is ASCII digit.</summary>
    public static bool IsDigit(char c) => c is >= '0' and <= '9';

    /// <summary>Read a simple number (digits, optional one dot, optional exponent). Call at digit or dot+digit.</summary>
    public TextSpan ReadNumber()
    {
        int start = _pos;
        if (Peek() == '.' && IsDigit(Peek(1)))
            Advance();
        else if (!IsDigit(Peek()))
            return new TextSpan(start, 0);
        while (IsDigit(Peek()))
            Advance();
        if (Peek() == '.' && IsDigit(Peek(1)))
        {
            Advance();
            while (IsDigit(Peek()))
                Advance();
        }
        char e = Peek();
        if (e is 'e' or 'E')
        {
            int save = _pos;
            Advance();
            if (Peek() is '+' or '-')
                Advance();
            if (!IsDigit(Peek()))
            {
                _pos = save;
                return new TextSpan(start, _pos - start);
            }
            while (IsDigit(Peek()))
                Advance();
        }
        // optional suffixes (C#/JS style letters)
        while (char.IsLetter(Peek()))
            Advance();
        return new TextSpan(start, _pos - start);
    }

    /// <summary>Build a token from kind and absolute span.</summary>
    public static Token Tok(int kind, int start, int end, string? text = null)
        => new(kind, new TextSpan(start, Math.Max(0, end - start)), text);

    /// <summary>EOF sentinel (Kind = -1).</summary>
    public static Token Eof(int position) => new(-1, new TextSpan(position, 0));
}
