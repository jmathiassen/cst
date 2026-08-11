using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Graphql;

internal sealed class GqlLexer
{
    private static readonly Dictionary<string, GqlTokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["type"] = GqlTokenKind.KwType,
        ["interface"] = GqlTokenKind.KwInterface,
        ["input"] = GqlTokenKind.KwInput,
        ["enum"] = GqlTokenKind.KwEnum,
        ["union"] = GqlTokenKind.KwUnion,
        ["scalar"] = GqlTokenKind.KwScalar,
        ["query"] = GqlTokenKind.KwQuery,
        ["mutation"] = GqlTokenKind.KwMutation,
        ["subscription"] = GqlTokenKind.KwSubscription,
        ["fragment"] = GqlTokenKind.KwFragment,
        ["on"] = GqlTokenKind.KwOn,
        ["schema"] = GqlTokenKind.KwSchema,
        ["extend"] = GqlTokenKind.KwExtend,
        ["directive"] = GqlTokenKind.KwDirective,
        ["implements"] = GqlTokenKind.KwImplements,
    };

    private readonly LexCursor _c;
    private readonly List<Token> _tokens = [];
    private bool _partial;

    public GqlLexer(string text, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _ = diagnostics;
        _c = new LexCursor(text, ct);
    }

    public bool IsPartial => _partial;

    public IReadOnlyList<Token> Tokenize()
    {
        while (!_c.IsEof)
        {
            _c.ThrowIfCancellationRequested();
            _c.SkipWhitespace();
            if (_c.IsEof) break;

            char ch = _c.Peek();
            if (ch == '#')
            {
                _c.SkipHashComment();
                continue;
            }
            if (ch == '"' && _c.Peek(1) == '"' && _c.Peek(2) == '"')
            {
                int start = _c.Position;
                _c.Advance(3);
                while (!_c.IsEof)
                {
                    if (_c.Peek() == '"' && _c.Peek(1) == '"' && _c.Peek(2) == '"')
                    {
                        _c.Advance(3);
                        break;
                    }
                    _c.Advance();
                }
                if (_c.IsEof) _partial = true;
                Emit(GqlTokenKind.StringLiteral, start);
                continue;
            }
            if (ch == '"')
            {
                int start = _c.Position;
                _c.ReadQuotedString(out bool unclosed, allowMultiline: false);
                if (unclosed) _partial = true;
                Emit(GqlTokenKind.StringLiteral, start);
                continue;
            }
            if (char.IsLetter(ch) || ch == '_')
            {
                TextSpan span = _c.ReadIdentifier();
                string text = _c.Slice(span);
                GqlTokenKind kind = Keywords.TryGetValue(text, out GqlTokenKind kw) ? kw : GqlTokenKind.Identifier;
                Emit(kind, span, text);
                continue;
            }
            if (_c.StartsWith("..."))
            {
                int start = _c.Position;
                _c.Advance(3);
                Emit(GqlTokenKind.Ellipsis, start);
                continue;
            }
            int pstart = _c.Position;
            char p = _c.Advance();
            GqlTokenKind pk = p switch
            {
                '{' => GqlTokenKind.LBrace,
                '}' => GqlTokenKind.RBrace,
                '(' => GqlTokenKind.LParen,
                ')' => GqlTokenKind.RParen,
                '[' => GqlTokenKind.LBracket,
                ']' => GqlTokenKind.RBracket,
                ':' => GqlTokenKind.Colon,
                ',' => GqlTokenKind.Comma,
                '@' => GqlTokenKind.At,
                '=' => GqlTokenKind.Eq,
                '|' => GqlTokenKind.Pipe,
                '&' => GqlTokenKind.Amp,
                '!' => GqlTokenKind.Bang,
                _ => GqlTokenKind.Other,
            };
            Emit(pk, pstart);
        }
        _tokens.Add(LexCursor.Eof(_c.Position));
        return _tokens;
    }

    private void Emit(GqlTokenKind kind, TextSpan span, string? text = null)
        => _tokens.Add(new Token((int)kind, span, text));

    private void Emit(GqlTokenKind kind, int start)
        => Emit(kind, new TextSpan(start, _c.Position - start));
}
