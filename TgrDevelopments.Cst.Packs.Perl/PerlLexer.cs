using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Perl;

sealed class PerlLexer
{
    private readonly LexCursor _c;
    private readonly List<ParseDiagnostic> _diag;
    private readonly List<Token> _tokens = [];

    private bool _expectRegex;

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "sub", "package", "my", "our", "local", "use", "require",
        "if", "unless", "else", "elsif", "while", "for", "foreach",
        "do", "eval", "BEGIN", "END", "return", "last", "next",
    };

    private static readonly HashSet<string> RegexOps = new(StringComparer.Ordinal)
    {
        "m", "qr", "s", "tr", "y",
    };

    public bool IsPartial { get; private set; }

    public PerlLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _c = new LexCursor(text, ct);
        _diag = diagnostics;
    }

    public IReadOnlyList<Token> Tokenize()
    {
        while (!_c.IsEof)
        {
            _c.SkipWhitespaceHorizontal();
            int start = _c.Position;
            char ch = _c.Peek();

            if (_c.IsEof)
                break;

            if (ch == '\n' || ch == '\r')
            {
                _c.Advance();
                if (ch == '\r' && _c.Peek() == '\n')
                    _c.Advance();
                _expectRegex = false;
                continue;
            }

            if (ch == '=' && IsPodStart())
            {
                SkipPod();
                continue;
            }

            if (ch == '#')
            {
                _c.SkipUntilNewline();
                continue;
            }

            if (ch == '\'')
            {
                ReadQuoted('\'', false);
                Emit(PerlTokenKind.StringLiteral, start);
                _expectRegex = false;
                continue;
            }

            if (ch == '"')
            {
                ReadQuoted('"', true);
                Emit(PerlTokenKind.StringLiteral, start);
                _expectRegex = false;
                continue;
            }

            if (ch == '`')
            {
                ReadQuoted('`', false);
                Emit(PerlTokenKind.BacktickString, start);
                _expectRegex = false;
                continue;
            }

            if (ch == '=' && _c.Peek(1) == '~')
            {
                _c.Advance(2);
                Emit(PerlTokenKind.MatchBind, start);
                _expectRegex = true;
                continue;
            }

            if (ch == '!' && _c.Peek(1) == '~')
            {
                _c.Advance(2);
                Emit(PerlTokenKind.NotMatchBind, start);
                _expectRegex = true;
                continue;
            }

            // Expecting regex after =~ / !~
            if (_expectRegex && IsDelimiter(ch))
            {
                ReadRegexLiteral(start);
                _expectRegex = false;
                continue;
            }

            if (ch == '$') { _c.Advance(); Emit(PerlTokenKind.SigilScalar, start); _expectRegex = false; continue; }
            if (ch == '@') { _c.Advance(); Emit(PerlTokenKind.SigilArray, start); _expectRegex = false; continue; }
            if (ch == '%') { _c.Advance(); Emit(PerlTokenKind.SigilHash, start); _expectRegex = false; continue; }
            if (ch == '&') { _c.Advance(); Emit(PerlTokenKind.SigilSub, start); _expectRegex = false; continue; }
            if (ch == '*') { _c.Advance(); Emit(PerlTokenKind.Star, start); _expectRegex = false; continue; }

            if (ch == ';') { _c.Advance(); Emit(PerlTokenKind.Semicolon, start); _expectRegex = false; continue; }
            if (ch == ',') { _c.Advance(); Emit(PerlTokenKind.Comma, start); _expectRegex = false; continue; }
            if (ch == '(') { _c.Advance(); Emit(PerlTokenKind.LParen, start); _expectRegex = false; continue; }
            if (ch == ')') { _c.Advance(); Emit(PerlTokenKind.RParen, start); _expectRegex = false; continue; }
            if (ch == '{') { _c.Advance(); Emit(PerlTokenKind.LBrace, start); _expectRegex = false; continue; }
            if (ch == '}') { _c.Advance(); Emit(PerlTokenKind.RBrace, start); _expectRegex = false; continue; }
            if (ch == '[') { _c.Advance(); Emit(PerlTokenKind.LBracket, start); _expectRegex = false; continue; }
            if (ch == ']') { _c.Advance(); Emit(PerlTokenKind.RBracket, start); _expectRegex = false; continue; }

            if (ch == ':')
            {
                _c.Advance();
                if (_c.Peek() == ':')
                    _c.Advance();
                else
                    Emit(PerlTokenKind.Colon, start);
                _expectRegex = false;
                continue;
            }

            if (ch == '=')
            {
                _c.Advance();
                if (_c.Peek() == '>') { _c.Advance(); Emit(PerlTokenKind.FatComma, start); }
                else Emit(PerlTokenKind.Assign, start);
                _expectRegex = false;
                continue;
            }

            if (ch == '-')
            {
                _c.Advance();
                if (_c.Peek() == '>') { _c.Advance(); Emit(PerlTokenKind.Arrow, start); }
                else Emit(PerlTokenKind.Minus, start);
                _expectRegex = false;
                continue;
            }

            if (ch == '+') { _c.Advance(); Emit(PerlTokenKind.Plus, start); _expectRegex = false; continue; }

            // // is always a regex (can't be division)
            if (ch == '/' && _c.Peek(1) == '/')
            {
                ReadRegexLiteral(start);
                _expectRegex = false;
                continue;
            }

            // lone / — treat as division operator
            if (ch == '/') { _c.Advance(); Emit(PerlTokenKind.Slash, start); _expectRegex = false; continue; }

            if (char.IsDigit(ch) || (ch == '.' && _c.Peek(1) >= '0' && _c.Peek(1) <= '9'))
            {
                ReadNumber();
                Emit(PerlTokenKind.Number, start);
                _expectRegex = false;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                if (TryReadRegexOp(start))
                    continue;
                ReadIdentifier(out bool wasKeyword);
                _expectRegex = false;
                if (!wasKeyword)
                    Emit(PerlTokenKind.Identifier, start);
                continue;
            }

            _expectRegex = false;
            _c.Advance();
        }

        _tokens.Add(new Token((int)PerlTokenKind.Eof, new TextSpan(_c.Position, 0)));
        return _tokens;
    }

    private bool TryReadRegexOp(int start)
    {
        string word = PeekIdentifier();
        if (word.Length == 0 || !RegexOps.Contains(word))
            return false;

        // Peek ahead past the keyword + optional whitespace for a delimiter
        int offset = word.Length;
        while (_c.Peek(offset) == ' ' || _c.Peek(offset) == '\t')
            offset++;
        if (_c.Peek(offset) == '\0' || !IsDelimiter(_c.Peek(offset)))
            return false;

        // It is a regex op — consume the keyword
        _c.Advance(word.Length);
        _c.SkipWhitespaceHorizontal();
        ReadRegexLiteral(start);
        _expectRegex = false;
        return true;
    }

    private string PeekIdentifier()
    {
        int i = 0;
        while (char.IsLetter(_c.Peek(i)) || _c.Peek(i) == '_')
            i++;
        return _c.Text.Substring(_c.Position, i);
    }

    private void ReadRegexLiteral(int start)
    {
        char delim = _c.Peek();
        bool isPaired = IsPairedDelimiter(delim);
        char openDelim = delim;
        char closeDelim = isPaired ? MatchingClose(delim) : delim;

        _c.Advance(); // skip opener
        int patternStart = _c.Position;

        // Determine if this is a two-pair op (s///, tr///, y///)
        // Detect by checking last-emitted token or by checking word prefix from start position
        bool hasReplacement = false;
        string? prefix = GetPrefix(start);
        if (prefix is "s" or "tr" or "y")
            hasReplacement = true;

        // Read pattern
        SkipRegexBody(openDelim, closeDelim, isPaired, out bool patternUnclosed);

        // Read replacement if two-pair op
        if (hasReplacement && !patternUnclosed)
        {
            // After the first close delimiter, read second delimiter + replacement
            char delim2 = closeDelim;
            bool isPaired2 = isPaired;
            char openDelim2 = openDelim;
            char closeDelim2 = closeDelim;

            // The second delimiter may differ from the first
            // For s/foo/bar/, after first / we read replacement until next /
            // For s{foo}{bar}, after } we expect { again
            if (!_c.IsEof && IsDelimiter(_c.Peek()))
            {
                delim2 = _c.Peek();
                isPaired2 = IsPairedDelimiter(delim2);
                openDelim2 = delim2;
                closeDelim2 = isPaired2 ? MatchingClose(delim2) : delim2;
                _c.Advance();
                SkipRegexBody(openDelim2, closeDelim2, isPaired2, out _);
            }
        }

        // Skip optional flags [msixpogc]
        while (!_c.IsEof && char.IsLetter(_c.Peek()) && _c.Peek() >= 'a' && _c.Peek() <= 'z')
            _c.Advance();

        Emit(PerlTokenKind.RegexLiteral, start);
    }

    private void SkipRegexBody(char openDelim, char closeDelim, bool isPaired, out bool unclosed)
    {
        unclosed = false;
        int depth = 0;
        while (!_c.IsEof)
        {
            char c = _c.Peek();
            if (isPaired && c == openDelim)
            {
                depth++;
                _c.Advance();
                continue;
            }
            if ((isPaired && depth > 0) || c == '\\')
            {
                _c.Advance();
                if (!_c.IsEof)
                    _c.Advance();
                continue;
            }
            if (c == closeDelim && depth == 0)
            {
                _c.Advance();
                return;
            }
            if (isPaired && c == closeDelim)
            {
                depth--;
                _c.Advance();
                continue;
            }
            if (c == '\n' || c == '\r')
            {
                unclosed = true;
                _diag.Add(new ParseDiagnostic("PERL001", "Unclosed regex literal.",
                    new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                IsPartial = true;
                return;
            }
            _c.Advance();
        }
        unclosed = true;
        _diag.Add(new ParseDiagnostic("PERL001", "Unclosed regex literal at EOF.",
            new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
        IsPartial = true;
    }

    private string? GetPrefix(int start)
    {
        int len = _c.Position - start;
        return len > 0 ? _c.Text.Substring(start, len).Trim() : null;
    }

    private static bool IsDelimiter(char ch)
    {
        if (char.IsLetterOrDigit(ch) || ch == '_' || ch == ' ' || ch == '\t')
            return false;
        return ch is not ('\n' or '\r');
    }

    private static bool IsPairedDelimiter(char ch) =>
        ch is '{' or '[' or '(' or '<';

    private static char MatchingClose(char open) => open switch
    {
        '{' => '}',
        '[' => ']',
        '(' => ')',
        '<' => '>',
        _ => open,
    };

    private bool IsPodStart()
    {
        int i = 1;
        while (char.IsLetter(_c.Peek(i)))
            i++;
        return i > 1;
    }

    private void SkipPod()
    {
        while (!_c.IsEof)
        {
            if (_c.Peek() == '=' && _c.Peek(1) == 'c' && _c.Peek(2) == 'u' && _c.Peek(3) == 't')
            {
                _c.Advance(4);
                if (_c.Peek() == '\r') _c.Advance();
                if (_c.Peek() == '\n') _c.Advance();
                return;
            }
            if (_c.Peek() == '\n' || _c.Peek() == '\r')
            {
                if (_c.Peek() == '\r') _c.Advance();
                if (_c.Peek() == '\n') _c.Advance();
                continue;
            }
            _c.Advance();
        }
    }

    private void ReadQuoted(char delim, bool interpolated)
    {
        _c.Advance();
        while (!_c.IsEof)
        {
            char c = _c.Peek();
            if (c == delim && (!interpolated || _c.Peek(-1) != '\\'))
            {
                _c.Advance();
                return;
            }
            if (c == '\\' && (delim == '"' || delim == '\'' || delim == '`'))
            {
                _c.Advance();
                if (!_c.IsEof)
                    _c.Advance();
                continue;
            }
            if (c == '\n' || c == '\r')
            {
                if (delim == '\'')
                {
                    if (_c.Peek() == '\r') _c.Advance();
                    if (_c.Peek() == '\n') _c.Advance();
                    continue;
                }
                _diag.Add(new ParseDiagnostic("PERL001", $"Unclosed {delim} string.",
                    new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
                IsPartial = true;
                return;
            }
            _c.Advance();
        }
        _diag.Add(new ParseDiagnostic("PERL001", $"Unclosed {delim} string at EOF.",
            new TextSpan(_c.Position, 0), DiagnosticSeverity.Error));
        IsPartial = true;
    }

    private void ReadNumber()
    {
        if (_c.Peek() == '0' && (_c.Peek(1) == 'x' || _c.Peek(1) == 'X'))
        {
            _c.Advance(2);
            while (IsHex(_c.Peek())) _c.Advance();
            return;
        }
        if (_c.Peek() == '0' && (_c.Peek(1) == 'b' || _c.Peek(1) == 'B'))
        {
            _c.Advance(2);
            while (_c.Peek() == '0' || _c.Peek() == '1' || _c.Peek() == '_') _c.Advance();
            return;
        }
        while (char.IsDigit(_c.Peek()) || _c.Peek() == '_') _c.Advance();
        if (_c.Peek() == '.')
        {
            _c.Advance();
            while (char.IsDigit(_c.Peek()) || _c.Peek() == '_') _c.Advance();
        }
        if (_c.Peek() == 'e' || _c.Peek() == 'E')
        {
            _c.Advance();
            if (_c.Peek() == '+' || _c.Peek() == '-') _c.Advance();
            while (char.IsDigit(_c.Peek()) || _c.Peek() == '_') _c.Advance();
        }
    }

    private void ReadIdentifier(out bool wasKeyword)
    {
        int start = _c.Position;
        while (char.IsLetterOrDigit(_c.Peek()) || _c.Peek() == '_')
            _c.Advance();
        while (_c.Peek() == ':' && _c.Peek(1) == ':')
        {
            _c.Advance(2);
            while (char.IsLetterOrDigit(_c.Peek()) || _c.Peek() == '_')
                _c.Advance();
        }
        string word = _c.Text.Substring(start, _c.Position - start);
        wasKeyword = Keywords.Contains(word);
        if (wasKeyword)
        {
            PerlTokenKind kind = word switch
            {
                "sub" => PerlTokenKind.KeywordSub,
                "package" => PerlTokenKind.KeywordPackage,
                "my" => PerlTokenKind.KeywordMy,
                "our" => PerlTokenKind.KeywordOur,
                "local" => PerlTokenKind.KeywordLocal,
                "use" => PerlTokenKind.KeywordUse,
                "require" => PerlTokenKind.KeywordRequire,
                "if" => PerlTokenKind.KeywordIf,
                "unless" => PerlTokenKind.KeywordUnless,
                "else" => PerlTokenKind.KeywordElse,
                "elsif" => PerlTokenKind.KeywordElsif,
                "while" => PerlTokenKind.KeywordWhile,
                "for" => PerlTokenKind.KeywordFor,
                "foreach" => PerlTokenKind.KeywordForeach,
                "do" => PerlTokenKind.KeywordDo,
                "eval" => PerlTokenKind.KeywordEval,
                "BEGIN" => PerlTokenKind.KeywordBegin,
                "END" => PerlTokenKind.KeywordEnd,
                "return" => PerlTokenKind.KeywordReturn,
                "last" => PerlTokenKind.KeywordLast,
                "next" => PerlTokenKind.KeywordNext,
                _ => PerlTokenKind.Identifier,
            };
            Emit(kind, start);
        }
    }

    private static bool IsHex(char ch) =>
        (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');

    private void Emit(PerlTokenKind kind, int start)
    {
        _tokens.Add(new Token((int)kind, new TextSpan(start, _c.Position - start)));
    }
}
