using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Cpp;

/// <summary>
/// Hand-written C++ lexer: strings (incl. raw/prefixed), char literals, comments,
/// preprocessor directives with <c>#if 0</c> region skipping, and punctuation with
/// <c>::</c> / <c>-&gt;</c> combining.
/// </summary>
internal sealed class CppLexer
{
    private static readonly Dictionary<string, CppTokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["namespace"] = CppTokenKind.KwNamespace,
        ["class"] = CppTokenKind.KwClass,
        ["struct"] = CppTokenKind.KwStruct,
        ["union"] = CppTokenKind.KwUnion,
        ["enum"] = CppTokenKind.KwEnum,
        ["template"] = CppTokenKind.KwTemplate,
        ["typedef"] = CppTokenKind.KwTypedef,
        ["using"] = CppTokenKind.KwUsing,
        ["operator"] = CppTokenKind.KwOperator,
        ["friend"] = CppTokenKind.KwFriend,
        ["public"] = CppTokenKind.KwPublic,
        ["private"] = CppTokenKind.KwPrivate,
        ["protected"] = CppTokenKind.KwProtected,
        ["virtual"] = CppTokenKind.KwVirtual,
        ["override"] = CppTokenKind.KwOverride,
        ["final"] = CppTokenKind.KwFinal,
        ["inline"] = CppTokenKind.KwInline,
        ["export"] = CppTokenKind.KwExport,
        ["extern"] = CppTokenKind.KwExtern,
        ["const"] = CppTokenKind.KwConst,
        ["constexpr"] = CppTokenKind.KwConstexpr,
        ["static"] = CppTokenKind.KwStatic,
        ["mutable"] = CppTokenKind.KwMutable,
        ["explicit"] = CppTokenKind.KwExplicit,
        ["noexcept"] = CppTokenKind.KwNoexcept,
        ["volatile"] = CppTokenKind.KwVolatile,
        ["auto"] = CppTokenKind.KwAuto,
        ["unsigned"] = CppTokenKind.KwUnsigned,
        ["signed"] = CppTokenKind.KwSigned,
        ["long"] = CppTokenKind.KwLong,
        ["short"] = CppTokenKind.KwShort,
        ["void"] = CppTokenKind.KwVoid,
        ["char"] = CppTokenKind.KwChar,
        ["int"] = CppTokenKind.KwInt,
        ["float"] = CppTokenKind.KwFloat,
        ["double"] = CppTokenKind.KwDouble,
        ["bool"] = CppTokenKind.KwBool,
        ["sizeof"] = CppTokenKind.KwSizeof,
        ["alignof"] = CppTokenKind.KwAlignof,
        ["alignas"] = CppTokenKind.KwAlignas,
        ["decltype"] = CppTokenKind.KwDecltype,
        ["typeid"] = CppTokenKind.KwTypeid,
        ["static_assert"] = CppTokenKind.KwStaticAssert,
        ["const_cast"] = CppTokenKind.KwConstCast,
        ["reinterpret_cast"] = CppTokenKind.KwReinterpretCast,
        ["static_cast"] = CppTokenKind.KwStaticCast,
        ["dynamic_cast"] = CppTokenKind.KwDynamicCast,
    };

    private readonly string _text;
    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private readonly List<bool> _condStack = [];
    private int _inactiveCount;
    private bool _partial;

    public CppLexer(string text, CancellationToken ct)
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
            if (IsRawStringStart())
            {
                int start = _c.Position;
                ReadRawString(out bool unclosed);
                if (unclosed)
                    _partial = true;
                Emit(CppTokenKind.StringLiteral, start);
                continue;
            }
            if (ch is '"' or '\'')
            {
                int start = _c.Position;
                _c.ReadQuotedString(out bool unclosed, allowMultiline: true);
                if (unclosed)
                    _partial = true;
                Emit(CppTokenKind.StringLiteral, start);
                continue;
            }
            if (LexCursor.IsDigit(ch) || (ch == '.' && LexCursor.IsDigit(_c.Peek(1))))
            {
                int start = _c.Position;
                ReadNumber();
                Emit(CppTokenKind.NumericLiteral, start);
                continue;
            }
            if (char.IsLetter(ch) || ch is '_' or '$')
            {
                int start = _c.Position;
                _c.ReadIdentifier();
                TextSpan span = new(start, _c.Position - start);
                string text = _c.Slice(span);
                CppTokenKind kind = Keywords.TryGetValue(text, out CppTokenKind k) ? k : CppTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }

            int pstart = _c.Position;
            char p = _c.Advance();
            CppTokenKind pk;
            if (p == ':' && _c.Peek() == ':')
            {
                _c.Advance();
                pk = CppTokenKind.ColonColon;
            }
            else if (p == '-' && _c.Peek() == '>')
            {
                _c.Advance();
                pk = CppTokenKind.Arrow;
            }
            else
            {
                pk = p switch
                {
                    '(' => CppTokenKind.LParen,
                    ')' => CppTokenKind.RParen,
                    '{' => CppTokenKind.LBrace,
                    '}' => CppTokenKind.RBrace,
                    '[' => CppTokenKind.LBracket,
                    ']' => CppTokenKind.RBracket,
                    '<' => CppTokenKind.LAngle,
                    '>' => CppTokenKind.RAngle,
                    ';' => CppTokenKind.Semicolon,
                    ',' => CppTokenKind.Comma,
                    ':' => CppTokenKind.Colon,
                    '*' => CppTokenKind.Star,
                    '&' => CppTokenKind.Amp,
                    '~' => CppTokenKind.Tilde,
                    '=' => CppTokenKind.Equal,
                    _ => CppTokenKind.Other,
                };
            }
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
        TryConsumeNewline();

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

    /// <summary>True when at a raw string start: <c>R"(</c>, <c>LR"(</c>, <c>uR"(</c>, <c>UR"(</c>, <c>u8R"(</c>.</summary>
    private bool IsRawStringStart()
    {
        if (_c.Peek() == 'R' && _c.Peek(1) == '"')
            return true;
        if ((_c.Peek() is 'L' or 'u' or 'U') && _c.Peek(1) == 'R' && _c.Peek(2) == '"')
            return true;
        return _c.Peek() == 'u' && _c.Peek(1) == '8' && _c.Peek(2) == 'R' && _c.Peek(3) == '"';
    }

    private void ReadRawString(out bool unclosed)
    {
        unclosed = false;
        char c = _c.Peek();
        if (c == 'u' && _c.Peek(1) == '8')
            _c.Advance(3);
        else if (c is 'L' or 'u' or 'U')
            _c.Advance(2);
        else
            _c.Advance();
        _c.Advance();

        int delimStart = _c.Position;
        while (!_c.IsEof && _c.Peek() != '(')
            _c.Advance();
        string delim = _c.Slice(new TextSpan(delimStart, _c.Position - delimStart));
        _c.Advance();

        string close = ")" + delim + "\"";
        while (!_c.IsEof)
        {
            _c.ThrowIfCancellationRequested();
            if (_c.StartsWith(close))
            {
                _c.Advance(close.Length);
                return;
            }
            _c.Advance();
        }
        unclosed = true;
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

    private void Emit(CppTokenKind kind, TextSpan span, string? text = null)
        => _tokens.Add(new Token((int)kind, span, text));

    private void Emit(CppTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));
}
