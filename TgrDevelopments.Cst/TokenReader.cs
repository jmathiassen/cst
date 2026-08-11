using System;
using System.Collections.Generic;
using System.Threading;

namespace TgrDevelopments.Cst;

/// <summary>Forward-only reader over a token list for recursive-descent parsers.</summary>
public sealed class TokenReader
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly CancellationToken _ct;
    private int _index;

    /// <summary>Creates a reader. Last token should be EOF.</summary>
    public TokenReader(IReadOnlyList<Token> tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        _tokens = tokens;
        _ct = cancellationToken;
        _index = 0;
    }

    /// <summary>Current index into the token list.</summary>
    public int Index => _index;

    /// <summary>Token count including EOF.</summary>
    public int Count => _tokens.Count;

    /// <summary>Throws if cancelled.</summary>
    public void ThrowIfCancellationRequested() => _ct.ThrowIfCancellationRequested();

    /// <summary>Current token (or last if past end).</summary>
    public Token Current
    {
        get
        {
            if (_tokens.Count == 0)
                return LexCursor.Eof(0);
            if (_index >= _tokens.Count)
                return _tokens[_tokens.Count - 1];
            return _tokens[_index];
        }
    }

    /// <summary>Peek token at offset from current.</summary>
    public Token Peek(int offset = 0)
    {
        int i = _index + offset;
        if (_tokens.Count == 0)
            return LexCursor.Eof(0);
        if (i < 0)
            i = 0;
        if (i >= _tokens.Count)
            return _tokens[_tokens.Count - 1];
        return _tokens[i];
    }

    /// <summary>True when current is EOF.</summary>
    public bool IsEof => Current.IsEof || Current.Kind < 0;

    /// <summary>Advance to next token; returns previous current.</summary>
    public Token Advance()
    {
        Token t = Current;
        if (_index < _tokens.Count - 1)
            _index++;
        return t;
    }

    /// <summary>Sets the current index (clamped). Used for speculative parse restore.</summary>
    public void Seek(int index)
    {
        if (index < 0)
            index = 0;
        if (_tokens.Count == 0)
        {
            _index = 0;
            return;
        }

        if (index >= _tokens.Count)
            index = _tokens.Count - 1;
        _index = index;
    }

    /// <summary>If current kind matches, advance and return true.</summary>
    public bool TryEat(int kind)
    {
        if (Current.Kind != kind)
            return false;
        Advance();
        return true;
    }

    /// <summary>Eat kind or return false without advancing.</summary>
    public bool Check(int kind) => Current.Kind == kind;

    /// <summary>Skip tokens until kind matches or EOF. Does not consume the match.</summary>
    public void SkipUntil(int kind)
    {
        while (!IsEof && Current.Kind != kind)
        {
            _ct.ThrowIfCancellationRequested();
            Advance();
        }
    }

    /// <summary>Skip until any of <paramref name="kinds"/> or EOF.</summary>
    public void SkipUntilAny(ReadOnlySpan<int> kinds)
    {
        while (!IsEof)
        {
            _ct.ThrowIfCancellationRequested();
            int k = Current.Kind;
            for (int i = 0; i < kinds.Length; i++)
            {
                if (k == kinds[i])
                    return;
            }
            Advance();
        }
    }

    /// <summary>Skip a balanced <c>{…}</c> starting at current LBrace. Consumes both braces.</summary>
    public bool TrySkipBalanced(int openKind, int closeKind)
    {
        if (Current.Kind != openKind)
            return false;
        int depth = 0;
        while (!IsEof)
        {
            _ct.ThrowIfCancellationRequested();
            if (Current.Kind == openKind)
                depth++;
            else if (Current.Kind == closeKind)
            {
                depth--;
                Advance();
                if (depth == 0)
                    return true;
                continue;
            }
            Advance();
        }
        return false;
    }
}
