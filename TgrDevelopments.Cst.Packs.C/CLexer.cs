using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.C;

/// <summary>Hand-written C lexer (strings, char literals, comments, preprocessor directives, #if 0 regions).</summary>
internal sealed class CLexer
{
    private static readonly Dictionary<string, CTokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["struct"] = CTokenKind.KwStruct,
        ["union"] = CTokenKind.KwUnion,
        ["enum"] = CTokenKind.KwEnum,
        ["typedef"] = CTokenKind.KwTypedef,
        ["static"] = CTokenKind.KwStatic,
        ["inline"] = CTokenKind.KwInline,
        ["extern"] = CTokenKind.KwExtern,
        ["const"] = CTokenKind.KwConst,
        ["volatile"] = CTokenKind.KwVolatile,
        ["unsigned"] = CTokenKind.KwUnsigned,
        ["signed"] = CTokenKind.KwSigned,
        ["long"] = CTokenKind.KwLong,
        ["short"] = CTokenKind.KwShort,
        ["void"] = CTokenKind.KwVoid,
        ["char"] = CTokenKind.KwChar,
        ["int"] = CTokenKind.KwInt,
        ["float"] = CTokenKind.KwFloat,
        ["double"] = CTokenKind.KwDouble,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<bool> _condStack = [];
    private int _inactiveCount;
    private bool _partial;

    public CLexer(string text, CancellationToken ct)
    {
        _text = text;
        _c = new LexCursor(text, ct);
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

            if (_c.Peek() == '#')
            {
                SkipDirective();
                continue;
            }

            if (_inactiveCount > 0)
            {
                _c.SkipUntilNewline();
                if (_c.Peek() is '\r' or '\n')
                    _c.Advance();
                continue;
            }

            char ch = _c.Peek();
            if (ch == '/' && _c.Peek(1) is '/' or '*')
            {
                if (_c.Peek(1) == '/')
                {
                    _c.SkipLineComment();
                    if (_c.Peek() is '\r' or '\n')
                        _c.Advance();
                }
                else
                {
                    _c.SkipBlockComment(out bool unclosed);
                    if (unclosed)
                        _partial = true;
                }
                continue;
            }
            if (ch is '"' or '\'')
            {
                int start = _c.Position;
                _c.ReadQuotedString(out bool unclosed, allowMultiline: ch == '"');
                if (unclosed)
                    _partial = true;
                Emit(CTokenKind.StringLiteral, start);
                continue;
            }
            if (LexCursor.IsDigit(ch) || (ch == '.' && LexCursor.IsDigit(_c.Peek(1))))
            {
                int start = _c.Position;
                ReadNumber();
                Emit(CTokenKind.NumericLiteral, start);
                continue;
            }
            if (char.IsLetter(ch) || ch is '_' or '$')
            {
                int start = _c.Position;
                _c.ReadIdentifier();
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                CTokenKind kind = Keywords.TryGetValue(text, out CTokenKind k) ? k : CTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            int pstart = _c.Position;
            char p = _c.Advance();
            CTokenKind pk = p switch
            {
                '(' => CTokenKind.LParen,
                ')' => CTokenKind.RParen,
                '{' => CTokenKind.LBrace,
                '}' => CTokenKind.RBrace,
                '[' => CTokenKind.LBracket,
                ']' => CTokenKind.RBracket,
                ';' => CTokenKind.Semicolon,
                ',' => CTokenKind.Comma,
                '*' => CTokenKind.Star,
                _ => CTokenKind.Other,
            };
            Emit(pk, pstart);
        }

        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void SkipDirective()
    {
        _c.Advance();
        _c.SkipWhitespaceHorizontal();
        string word = ReadWord();
        bool needsCondition = word is "if" or "elif";
        string rest = needsCondition ? ReadDirectiveCondition() : "";
        if (!needsCondition)
            SkipDirectiveBody();
        SkipDirectiveNewline();

        switch (word)
        {
            case "if":
                PushCondition(!rest.TrimStart().StartsWith("0"));
                break;
            case "ifdef":
            case "ifndef":
                PushCondition(true);
                break;
            case "elif":
                ReplaceCondition(!rest.TrimStart().StartsWith("0"));
                break;
            case "else":
                FlipCondition();
                break;
            case "endif":
                PopCondition();
                break;
        }
    }

    private string ReadDirectiveCondition()
    {
        StringBuilder condition = new StringBuilder();
        while (true)
        {
            int start = _c.Position;
            _c.SkipUntilNewline();
            condition.Append(_c.Slice(new TextSpan(start, _c.Position - start)));
            bool continued = _c.Position > start && _c.Peek(-1) == '\\';
            if (_c.IsEof)
                break;
            TryConsumeNewline();
            if (!continued)
                break;
        }
        return condition.ToString();
    }

    private void SkipDirectiveBody()
    {
        while (true)
        {
            int start = _c.Position;
            _c.SkipUntilNewline();
            bool continued = _c.Position > start && _c.Peek(-1) == '\\';
            if (_c.IsEof)
                return;
            TryConsumeNewline();
            if (!continued)
                return;
        }
    }

    private void SkipDirectiveNewline()
    {
        TryConsumeNewline();
    }

    private void TryConsumeNewline()
    {
        if (_c.Peek() == '\r')
        {
            _c.Advance();
            if (_c.Peek() == '\n')
                _c.Advance();
        }
        else if (_c.Peek() == '\n')
        {
            _c.Advance();
        }
    }

    private string ReadWord()
    {
        int start = _c.Position;
        while (char.IsLetter(_c.Peek()) || _c.Peek() == '_')
            _c.Advance();
        return _c.Slice(new TextSpan(start, _c.Position - start));
    }

    private void ReadNumber()
    {
        if (_c.Peek() == '0' && _c.Peek(1) is 'x' or 'X')
        {
            _c.Advance(2);
            while (_c.Peek() is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F'))
                _c.Advance();
            while (_c.Peek() is 'u' or 'U' or 'l' or 'L')
                _c.Advance();
            return;
        }
        _c.ReadNumber();
    }

    private void PushCondition(bool active)
    {
        _condStack.Add(active);
        if (!active)
            _inactiveCount++;
    }

    private void ReplaceCondition(bool active)
    {
        if (_condStack.Count == 0)
            return;
        bool old = _condStack[^1];
        _condStack[^1] = active;
        if (old && !active)
            _inactiveCount++;
        else if (!old && active)
            _inactiveCount--;
    }

    private void FlipCondition()
    {
        if (_condStack.Count == 0)
            return;
        bool old = _condStack[^1];
        _condStack[^1] = !old;
        if (old)
            _inactiveCount++;
        else
            _inactiveCount--;
    }

    private void PopCondition()
    {
        if (_condStack.Count == 0)
            return;
        bool old = _condStack[^1];
        _condStack.RemoveAt(_condStack.Count - 1);
        if (!old)
            _inactiveCount--;
    }

    private void Emit(CTokenKind kind, TextSpan span, string? text = null)
        => _tokens.Add(new Token((int)kind, span, text));

    private void Emit(CTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));
}
