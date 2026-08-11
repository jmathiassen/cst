using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Proto;

/// <summary>Protocol Buffers language pack (hand lexer → RD outline).</summary>
public sealed class ProtoLanguagePack : ILanguagePack, IOutlineProvider
{
    private static readonly string[] Extensions = [".proto"];
    private static readonly string[] NamePatterns = Array.Empty<string>();

    /// <inheritdoc />
    public string LanguageId => "proto";
    /// <inheritdoc />
    public string DisplayName => "Protocol Buffers";
    /// <inheritdoc />
    public IReadOnlyList<string> FileExtensions => Extensions;
    /// <inheritdoc />
    public IReadOnlyList<string> FileNamePatterns => NamePatterns;
    /// <inheritdoc />
    public SyntaxQuality DefaultQuality => SyntaxQuality.Structural;

    /// <inheritdoc />
    public bool OwnsPath(string relativeOrFileName)
    {
        ArgumentNullException.ThrowIfNull(relativeOrFileName);
        return false;
    }

    /// <inheritdoc />
    public ConcreteSyntaxTree Parse(SourceText source, ParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        List<ParseDiagnostic> diagnostics = [];
        SourceText effective = ParseHelpers.ApplyMaxChars(source, options, out string text, diagnostics, out bool isPartial);
        if (ParseHelpers.IsNullOrWhiteSpace(text))
            return TextUtil.EmptyTree(LanguageId, effective, "file", diagnostics, DefaultQuality, isPartial);
        List<SyntaxNode> children = ParseDoc(text, diagnostics, ref isPartial, cancellationToken, out List<OutlineNode> outlines);
        return TextUtil.Tree(LanguageId, effective, "file", text, children, diagnostics, DefaultQuality, isPartial, outlines);
    }

    /// <inheritdoc />
    public IReadOnlyList<OutlineNode> GetOutline(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();
        if (tree.CachedOutline is not null)
            return tree.CachedOutline;
        return Array.Empty<OutlineNode>();
    }

    private static List<SyntaxNode> ParseDoc(
        string text,
        List<ParseDiagnostic> diagnostics,
        ref bool isPartial,
        CancellationToken ct,
        out List<OutlineNode> outlines)
    {
        _ = diagnostics;
        outlines = [];
        List<SyntaxNode> children = [];
        LineMap map = new(text);
        IReadOnlyList<Token> tokens = Tokenize(text, ref isPartial, ct);
        TokenReader r = new(tokens, ct);

        while (!r.IsEof)
        {
            ct.ThrowIfCancellationRequested();
            ProtoTokenKind k = (ProtoTokenKind)r.Current.Kind;
            if (k is ProtoTokenKind.KwPackage or ProtoTokenKind.KwMessage
                or ProtoTokenKind.KwEnum or ProtoTokenKind.KwService)
            {
                int start = r.Current.Start;
                string outlineKind = k switch
                {
                    ProtoTokenKind.KwPackage => "package",
                    ProtoTokenKind.KwMessage => "message",
                    ProtoTokenKind.KwEnum => "enum",
                    _ => "service",
                };
                string nodeKind = k switch
                {
                    ProtoTokenKind.KwPackage => "package_clause",
                    ProtoTokenKind.KwMessage => "message",
                    ProtoTokenKind.KwEnum => "enum",
                    _ => "service",
                };
                r.Advance();

                string name = ReadDottedName(r);
                int end;
                if (r.Check((int)ProtoTokenKind.LBrace))
                {
                    if (!r.TrySkipBalanced((int)ProtoTokenKind.LBrace, (int)ProtoTokenKind.RBrace))
                    {
                        isPartial = true;
                        end = text.Length;
                    }
                    else
                        end = r.Peek(-1).End;
                }
                else if (r.Check((int)ProtoTokenKind.Semicolon))
                {
                    end = r.Current.End;
                    r.Advance();
                }
                else
                    end = r.Current.IsEof ? text.Length : r.Current.Start;

                TextSpan span = new(start, Math.Max(0, end - start));
                children.Add(new SyntaxNode(nodeKind, span, name));
                LinePositionSpan lp = map.GetLinePositionSpan(span);
                outlines.Add(new OutlineNode(
                    outlineKind, name, span,
                    new LinePositionSpan(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1)),
                    1));
                continue;
            }

            r.Advance();
        }

        if (outlines.Count > 1)
        {
            List<OutlineNode> nested = OutlineNesting.NestByLineContainment(outlines);
            outlines.Clear();
            outlines.AddRange(nested);
        }

        return children;
    }

    private static string ReadDottedName(TokenReader r)
    {
        StringBuilder sb = new();
        while (!r.IsEof
               && ((ProtoTokenKind)r.Current.Kind is ProtoTokenKind.Identifier or ProtoTokenKind.Dot
                   or ProtoTokenKind.StringLiteral))
        {
            if ((ProtoTokenKind)r.Current.Kind == ProtoTokenKind.Identifier && r.Current.Text is not null)
            {
                if (sb.Length > 0)
                    sb.Append('.');
                sb.Append(r.Current.Text);
            }
            r.Advance();
            // stop before { or ;
            if (r.Check((int)ProtoTokenKind.LBrace) || r.Check((int)ProtoTokenKind.Semicolon))
                break;
            if ((ProtoTokenKind)r.Current.Kind is not (ProtoTokenKind.Identifier or ProtoTokenKind.Dot))
                break;
        }
        return sb.Length > 0 ? sb.ToString() : "*";
    }

    private static IReadOnlyList<Token> Tokenize(string text, ref bool isPartial, CancellationToken ct)
    {
        LexCursor c = new(text, ct);
        List<Token> tokens = [];
        while (!c.IsEof)
        {
            ct.ThrowIfCancellationRequested();
            c.SkipWhitespace();
            if (c.IsEof)
                break;

            char ch = c.Peek();
            if (ch == '/' && c.Peek(1) == '/')
            {
                c.SkipLineComment();
                continue;
            }
            if (ch == '/' && c.Peek(1) == '*')
            {
                c.SkipBlockComment(out bool unclosed);
                if (unclosed)
                    isPartial = true;
                continue;
            }
            if (ch == '"')
            {
                int s = c.Position;
                c.ReadQuotedString(out bool u, allowMultiline: false);
                if (u)
                    isPartial = true;
                tokens.Add(new Token((int)ProtoTokenKind.StringLiteral, new TextSpan(s, c.Position - s)));
                continue;
            }
            if (char.IsLetter(ch) || ch == '_')
            {
                TextSpan span = c.ReadIdentifier();
                string w = c.Slice(span);
                ProtoTokenKind kind = w switch
                {
                    "package" => ProtoTokenKind.KwPackage,
                    "message" => ProtoTokenKind.KwMessage,
                    "enum" => ProtoTokenKind.KwEnum,
                    "service" => ProtoTokenKind.KwService,
                    "rpc" => ProtoTokenKind.KwRpc,
                    _ => ProtoTokenKind.Identifier,
                };
                tokens.Add(new Token((int)kind, span, w));
                continue;
            }

            int ps = c.Position;
            char p = c.Advance();
            ProtoTokenKind pk = p switch
            {
                '{' => ProtoTokenKind.LBrace,
                '}' => ProtoTokenKind.RBrace,
                '(' => ProtoTokenKind.LParen,
                ')' => ProtoTokenKind.RParen,
                ';' => ProtoTokenKind.Semicolon,
                '.' => ProtoTokenKind.Dot,
                _ => ProtoTokenKind.Other,
            };
            tokens.Add(new Token((int)pk, new TextSpan(ps, c.Position - ps)));
        }

        tokens.Add(LexCursor.Eof(c.Position));
        return tokens;
    }
}
