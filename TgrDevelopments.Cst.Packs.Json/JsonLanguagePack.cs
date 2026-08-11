using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Json;

/// <summary>JSON / JSONC language pack.</summary>
public sealed class JsonLanguagePack : ILanguagePack, IOutlineProvider
{
    /// <summary>Max nesting depth for tree/outline.</summary>
    public const int JsonMaxDepth = 32;

    /// <summary>Max outline nodes per file.</summary>
    public const int JsonOutlineMaxNodes = 200;

    /// <summary>Max array elements shown in outline.</summary>
    public const int JsonOutlineMaxArrayItems = 50;

    private static readonly string[] Extensions = [".json", ".jsonc"];

    /// <inheritdoc />
    public string LanguageId => "json";

    /// <inheritdoc />
    public string DisplayName => "JSON";

    /// <inheritdoc />
    public IReadOnlyList<string> FileExtensions => Extensions;

    /// <inheritdoc />
    public IReadOnlyList<string> FileNamePatterns => Array.Empty<string>();

    /// <inheritdoc />
    public SyntaxQuality DefaultQuality => SyntaxQuality.Structural;

    /// <inheritdoc />
    public bool OwnsPath(string relativeOrFileName) => false;

    /// <inheritdoc />
    public ConcreteSyntaxTree Parse(
        SourceText source,
        ParseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        List<ParseDiagnostic> diagnostics = [];
        SourceText effective = ParseHelpers.ApplyMaxChars(
            source, options, out string text, diagnostics, out bool isPartial);

        bool allowCommentsSilently = IsJsonc(source.FilePath);

        if (ParseHelpers.IsNullOrWhiteSpace(text))
        {
            SyntaxNode emptyRoot = new("empty", new TextSpan(0, 0));
            return new ConcreteSyntaxTree(
                LanguageId, effective, emptyRoot, diagnostics,
                SyntaxConfidence.Syntax, DefaultQuality, isPartial,
                cachedOutline: Array.Empty<OutlineNode>());
        }

        Parser parser = new(
            text, diagnostics, allowCommentsSilently, cancellationToken);
        SyntaxNode value;
        try
        {
            value = parser.ParseValue(0);
            parser.SkipTrivia();
            if (parser.Position < text.Length)
            {
                diagnostics.Add(new ParseDiagnostic(
                    "JSON001",
                    "Unexpected trailing content.",
                    new TextSpan(parser.Position, text.Length - parser.Position),
                    DiagnosticSeverity.Error));
                isPartial = true;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            isPartial = true;
            value = new SyntaxNode("truncated", new TextSpan(0, text.Length));
        }

        if (parser.HitDepthLimit || parser.IsPartial)
            isPartial = true;

        SyntaxNode root = new(
            "document",
            new TextSpan(0, text.Length),
            children: [value]);

        ConcreteSyntaxTree provisional = new(
            LanguageId, effective, root, diagnostics,
            SyntaxConfidence.Syntax, DefaultQuality, isPartial);
        IReadOnlyList<OutlineNode> outline = BuildOutlineRoots(provisional, cancellationToken);
        return new ConcreteSyntaxTree(
            LanguageId, effective, root, diagnostics,
            SyntaxConfidence.Syntax, DefaultQuality, isPartial, outline);
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
        return BuildOutlineRoots(tree, cancellationToken);
    }

    private static IReadOnlyList<OutlineNode> BuildOutlineRoots(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken)
    {
        if (tree.Root.Kind == "empty" || tree.Root.Children.Count == 0)
            return Array.Empty<OutlineNode>();

        LineMap map = new(tree.Source.Text);
        int nodeCount = 0;
        bool hitCap = false;
        List<OutlineNode> roots = [];

        SyntaxNode value = tree.Root.Children[0];
        OutlineNode? node = BuildOutline(
            value, 1, map, ref nodeCount, ref hitCap, cancellationToken);
        if (node is not null)
            roots.Add(node);

        return roots;
    }

    private static OutlineNode? BuildOutline(
        SyntaxNode node,
        int level,
        LineMap map,
        ref int nodeCount,
        ref bool hitCap,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (hitCap || nodeCount >= JsonOutlineMaxNodes)
        {
            hitCap = true;
            return null;
        }

        int startLine = map.GetLineNumber(node.Span.Start);
        int endLine = map.GetLineNumber(
            node.Span.Length > 0 ? node.Span.End - 1 : node.Span.Start);
        LinePositionSpan lineSpan = map.GetLineSpan(startLine, endLine);

        if (node.Kind == "object")
        {
            int keyCount = 0;
            foreach (SyntaxNode child in node.Children)
            {
                if (child.Kind == "member")
                    keyCount++;
            }

            string label = keyCount == 0 ? "{}" : $"{{{keyCount} keys}}";
            if (level == 1 && keyCount > 0)
                label = $"{{{keyCount} keys}}";

            nodeCount++;
            List<OutlineNode> children = [];
            foreach (SyntaxNode member in node.Children)
            {
                if (member.Kind != "member")
                    continue;
                if (nodeCount >= JsonOutlineMaxNodes)
                {
                    hitCap = true;
                    break;
                }

                OutlineNode? child = BuildMemberOutline(
                    member, level + 1, map, ref nodeCount, ref hitCap, cancellationToken);
                if (child is not null)
                    children.Add(child);
            }

            return new OutlineNode("object", label, node.Span, lineSpan, level, children);
        }

        if (node.Kind == "array")
        {
            int count = node.Children.Count;
            string label = $"[{count} items]";
            nodeCount++;
            List<OutlineNode> children = [];
            int limit = Math.Min(count, JsonOutlineMaxArrayItems);
            for (int i = 0; i < limit; i++)
            {
                if (nodeCount >= JsonOutlineMaxNodes)
                {
                    hitCap = true;
                    break;
                }

                OutlineNode? child = BuildIndexOutline(
                    node.Children[i], i, level + 1, map, ref nodeCount, ref hitCap, cancellationToken);
                if (child is not null)
                    children.Add(child);
            }

            return new OutlineNode("array", label, node.Span, lineSpan, level, children);
        }

        // scalar root
        nodeCount++;
        string scalarLabel = Truncate(ScalarDisplay(node), 40);
        return new OutlineNode("value", scalarLabel, node.Span, lineSpan, level);
    }

    private static OutlineNode? BuildMemberOutline(
        SyntaxNode member,
        int level,
        LineMap map,
        ref int nodeCount,
        ref bool hitCap,
        CancellationToken cancellationToken)
    {
        if (hitCap || nodeCount >= JsonOutlineMaxNodes)
        {
            hitCap = true;
            return null;
        }

        nodeCount++;
        string label = member.Name ?? "";
        int startLine = map.GetLineNumber(member.Span.Start);
        int endLine = map.GetLineNumber(
            member.Span.Length > 0 ? member.Span.End - 1 : member.Span.Start);
        LinePositionSpan lineSpan = map.GetLineSpan(startLine, endLine);

        List<OutlineNode> children = [];
        if (member.Children.Count > 0)
        {
            SyntaxNode value = member.Children[0];
            if (value.Kind is "object" or "array")
            {
                OutlineNode? nested = BuildOutline(
                    value, level + 1, map, ref nodeCount, ref hitCap, cancellationToken);
                if (nested is not null)
                    children.Add(nested);
            }
        }

        return new OutlineNode("key", label, member.Span, lineSpan, level, children);
    }

    private static OutlineNode? BuildIndexOutline(
        SyntaxNode element,
        int index,
        int level,
        LineMap map,
        ref int nodeCount,
        ref bool hitCap,
        CancellationToken cancellationToken)
    {
        if (hitCap || nodeCount >= JsonOutlineMaxNodes)
        {
            hitCap = true;
            return null;
        }

        nodeCount++;
        string label = $"[{index}]";
        int startLine = map.GetLineNumber(element.Span.Start);
        int endLine = map.GetLineNumber(
            element.Span.Length > 0 ? element.Span.End - 1 : element.Span.Start);
        LinePositionSpan lineSpan = map.GetLineSpan(startLine, endLine);

        List<OutlineNode> children = [];
        if (element.Kind is "object" or "array")
        {
            OutlineNode? nested = BuildOutline(
                element, level + 1, map, ref nodeCount, ref hitCap, cancellationToken);
            if (nested is not null)
                children.Add(nested);
        }

        return new OutlineNode("index", label, element.Span, lineSpan, level, children);
    }

    private static string ScalarDisplay(SyntaxNode node)
    {
        return node.Kind switch
        {
            "string" => node.Name is not null ? $"\"{node.Name}\"" : "\"\"",
            "number" => node.Name ?? "0",
            "true" => "true",
            "false" => "false",
            "null" => "null",
            _ => node.Kind
        };
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;
        return value.Substring(0, max - 1) + "…";
    }

    private static bool IsJsonc(string? path)
    {
        if (path is null)
            return false;
        return string.Equals(Path.GetExtension(path), ".jsonc", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly List<ParseDiagnostic> _diagnostics;
        private readonly bool _allowCommentsSilently;
        private readonly CancellationToken _ct;
        private int _pos;

        public int Position => _pos;
        public bool HitDepthLimit { get; private set; }
        public bool IsPartial { get; private set; }

        public Parser(
            string text,
            List<ParseDiagnostic> diagnostics,
            bool allowCommentsSilently,
            CancellationToken ct)
        {
            _text = text;
            _diagnostics = diagnostics;
            _allowCommentsSilently = allowCommentsSilently;
            _ct = ct;
        }

        public void SkipTrivia()
        {
            while (_pos < _text.Length)
            {
                _ct.ThrowIfCancellationRequested();
                char c = _text[_pos];
                if (char.IsWhiteSpace(c))
                {
                    _pos++;
                    continue;
                }

                if (c == '/' && _pos + 1 < _text.Length)
                {
                    char n = _text[_pos + 1];
                    if (n == '/')
                    {
                        if (!_allowCommentsSilently)
                        {
                            _diagnostics.Add(new ParseDiagnostic(
                                "JSON003",
                                "Comment in JSON (non-jsonc).",
                                new TextSpan(_pos, 2),
                                DiagnosticSeverity.Warning));
                        }

                        _pos += 2;
                        while (_pos < _text.Length && _text[_pos] is not ('\n' or '\r'))
                            _pos++;
                        continue;
                    }

                    if (n == '*')
                    {
                        int start = _pos;
                        if (!_allowCommentsSilently)
                        {
                            _diagnostics.Add(new ParseDiagnostic(
                                "JSON003",
                                "Comment in JSON (non-jsonc).",
                                new TextSpan(_pos, 2),
                                DiagnosticSeverity.Warning));
                        }

                        _pos += 2;
                        while (_pos + 1 < _text.Length
                               && !(_text[_pos] == '*' && _text[_pos + 1] == '/'))
                            _pos++;
                        if (_pos + 1 < _text.Length)
                            _pos += 2;
                        else
                        {
                            IsPartial = true;
                            _diagnostics.Add(new ParseDiagnostic(
                                "JSON001",
                                "Unclosed block comment.",
                                new TextSpan(start, _text.Length - start),
                                DiagnosticSeverity.Error));
                            _pos = _text.Length;
                        }

                        continue;
                    }
                }

                break;
            }
        }

        public SyntaxNode ParseValue(int depth)
        {
            _ct.ThrowIfCancellationRequested();
            SkipTrivia();
            if (_pos >= _text.Length)
            {
                IsPartial = true;
                _diagnostics.Add(new ParseDiagnostic(
                    "JSON001",
                    "Unexpected end of input.",
                    new TextSpan(Math.Max(0, _text.Length - 1), 0),
                    DiagnosticSeverity.Error));
                return new SyntaxNode("null", new TextSpan(_pos, 0));
            }

            if (depth > JsonMaxDepth)
            {
                HitDepthLimit = true;
                IsPartial = true;
                _diagnostics.Add(new ParseDiagnostic(
                    "JSON005",
                    "Max depth exceeded.",
                    new TextSpan(_pos, 0),
                    DiagnosticSeverity.Warning));
                SkipValueShallow();
                return new SyntaxNode(
                    "truncated",
                    new TextSpan(_pos, 0),
                    properties: new Dictionary<string, string> { ["truncated"] = "depth" });
            }

            char c = _text[_pos];
            if (c == '{')
                return ParseObject(depth);
            if (c == '[')
                return ParseArray(depth);
            if (c == '"')
                return ParseString();
            if (c is '-' or >= '0' and <= '9')
                return ParseNumber();
            if (MatchLiteral("true"))
                return new SyntaxNode("true", new TextSpan(_pos - 4, 4));
            if (MatchLiteral("false"))
                return new SyntaxNode("false", new TextSpan(_pos - 5, 5));
            if (MatchLiteral("null"))
                return new SyntaxNode("null", new TextSpan(_pos - 4, 4));

            IsPartial = true;
            int err = _pos;
            _pos++;
            _diagnostics.Add(new ParseDiagnostic(
                "JSON001",
                $"Unexpected token '{c}'.",
                new TextSpan(err, 1),
                DiagnosticSeverity.Error));
            return new SyntaxNode("truncated", new TextSpan(err, 1));
        }

        private SyntaxNode ParseObject(int depth)
        {
            int start = _pos;
            _pos++; // {
            List<SyntaxNode> members = [];
            HashSet<string> seenKeys = new(StringComparer.Ordinal);

            SkipTrivia();
            if (_pos < _text.Length && _text[_pos] == '}')
            {
                _pos++;
                return new SyntaxNode("object", new TextSpan(start, _pos - start), children: members);
            }

            while (_pos < _text.Length)
            {
                _ct.ThrowIfCancellationRequested();
                SkipTrivia();
                if (_pos >= _text.Length)
                {
                    IsPartial = true;
                    _diagnostics.Add(new ParseDiagnostic(
                        "JSON001",
                        "Unclosed object.",
                        new TextSpan(start, _text.Length - start),
                        DiagnosticSeverity.Error));
                    break;
                }

                if (_text[_pos] == '}')
                {
                    _pos++;
                    break;
                }

                if (_text[_pos] != '"')
                {
                    IsPartial = true;
                    _diagnostics.Add(new ParseDiagnostic(
                        "JSON001",
                        "Expected property name.",
                        new TextSpan(_pos, 1),
                        DiagnosticSeverity.Error));
                    // resync
                    while (_pos < _text.Length && _text[_pos] is not (',' or '}'))
                        _pos++;
                }
                else
                {
                    int memberStart = _pos;
                    SyntaxNode keyNode = ParseString();
                    string key = keyNode.Name ?? "";
                    if (!seenKeys.Add(key))
                    {
                        _diagnostics.Add(new ParseDiagnostic(
                            "JSON004",
                            $"Duplicate key '{key}'.",
                            keyNode.Span,
                            DiagnosticSeverity.Warning));
                    }

                    SkipTrivia();
                    if (_pos < _text.Length && _text[_pos] == ':')
                        _pos++;
                    else
                    {
                        IsPartial = true;
                        _diagnostics.Add(new ParseDiagnostic(
                            "JSON001",
                            "Expected ':' after property name.",
                            new TextSpan(_pos, 0),
                            DiagnosticSeverity.Error));
                    }

                    SyntaxNode value = ParseValue(depth + 1);
                    int memberEnd = _pos;
                    members.Add(new SyntaxNode(
                        "member",
                        new TextSpan(memberStart, memberEnd - memberStart),
                        key,
                        keyNode.NameSpan,
                        children: [value]));
                }

                SkipTrivia();
                if (_pos < _text.Length && _text[_pos] == ',')
                {
                    int commaPos = _pos;
                    _pos++;
                    SkipTrivia();
                    if (_pos < _text.Length && _text[_pos] == '}')
                    {
                        _diagnostics.Add(new ParseDiagnostic(
                            "JSON002",
                            "Trailing comma in object.",
                            new TextSpan(commaPos, 1),
                            DiagnosticSeverity.Warning));
                        _pos++;
                        break;
                    }

                    continue;
                }

                if (_pos < _text.Length && _text[_pos] == '}')
                {
                    _pos++;
                    break;
                }

                if (_pos >= _text.Length)
                {
                    IsPartial = true;
                    _diagnostics.Add(new ParseDiagnostic(
                        "JSON001",
                        "Unclosed object.",
                        new TextSpan(start, _text.Length - start),
                        DiagnosticSeverity.Error));
                    break;
                }
            }

            return new SyntaxNode(
                "object",
                new TextSpan(start, _pos - start),
                children: members);
        }

        private SyntaxNode ParseArray(int depth)
        {
            int start = _pos;
            _pos++; // [
            List<SyntaxNode> elements = [];

            SkipTrivia();
            if (_pos < _text.Length && _text[_pos] == ']')
            {
                _pos++;
                return new SyntaxNode("array", new TextSpan(start, _pos - start), children: elements);
            }

            int index = 0;
            while (_pos < _text.Length)
            {
                _ct.ThrowIfCancellationRequested();
                SkipTrivia();
                if (_pos >= _text.Length)
                {
                    IsPartial = true;
                    _diagnostics.Add(new ParseDiagnostic(
                        "JSON001",
                        "Unclosed array.",
                        new TextSpan(start, _text.Length - start),
                        DiagnosticSeverity.Error));
                    break;
                }

                if (_text[_pos] == ']')
                {
                    _pos++;
                    break;
                }

                SyntaxNode value = ParseValue(depth + 1);
                Dictionary<string, string> props = new() { ["index"] = index.ToString() };
                // wrap is not required — store index on value via properties copy
                elements.Add(new SyntaxNode(
                    value.Kind,
                    value.Span,
                    value.Name,
                    value.NameSpan,
                    value.Children,
                    MergeProps(value.Properties, props)));
                index++;

                SkipTrivia();
                if (_pos < _text.Length && _text[_pos] == ',')
                {
                    int commaPos = _pos;
                    _pos++;
                    SkipTrivia();
                    if (_pos < _text.Length && _text[_pos] == ']')
                    {
                        _diagnostics.Add(new ParseDiagnostic(
                            "JSON002",
                            "Trailing comma in array.",
                            new TextSpan(commaPos, 1),
                            DiagnosticSeverity.Warning));
                        _pos++;
                        break;
                    }

                    continue;
                }

                if (_pos < _text.Length && _text[_pos] == ']')
                {
                    _pos++;
                    break;
                }

                if (_pos >= _text.Length)
                {
                    IsPartial = true;
                    _diagnostics.Add(new ParseDiagnostic(
                        "JSON001",
                        "Unclosed array.",
                        new TextSpan(start, _text.Length - start),
                        DiagnosticSeverity.Error));
                    break;
                }
            }

            return new SyntaxNode(
                "array",
                new TextSpan(start, _pos - start),
                children: elements);
        }

        private SyntaxNode ParseString()
        {
            int start = _pos;
            _pos++; // "
            StringBuilder sb = new();
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (c == '"')
                {
                    _pos++;
                    string value = sb.ToString();
                    TextSpan span = new(start, _pos - start);
                    TextSpan nameSpan = new(start + 1, Math.Max(0, _pos - start - 2));
                    return new SyntaxNode("string", span, value, nameSpan);
                }

                if (c == '\\' && _pos + 1 < _text.Length)
                {
                    _pos++;
                    char esc = _text[_pos];
                    sb.Append(esc switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'b' => '\b',
                        'f' => '\f',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        'u' when _pos + 4 < _text.Length => DecodeUnicode(),
                        _ => esc
                    });
                    _pos++;
                    continue;
                }

                if (c is '\n' or '\r')
                {
                    IsPartial = true;
                    _diagnostics.Add(new ParseDiagnostic(
                        "JSON001",
                        "Unterminated string.",
                        new TextSpan(start, _pos - start),
                        DiagnosticSeverity.Error));
                    break;
                }

                sb.Append(c);
                _pos++;
            }

            if (_pos >= _text.Length)
            {
                IsPartial = true;
                _diagnostics.Add(new ParseDiagnostic(
                    "JSON001",
                    "Unterminated string.",
                    new TextSpan(start, _text.Length - start),
                    DiagnosticSeverity.Error));
            }

            string partial = sb.ToString();
            return new SyntaxNode(
                "string",
                new TextSpan(start, _pos - start),
                partial,
                new TextSpan(start + 1, Math.Max(0, _pos - start - 1)));
        }

        private char DecodeUnicode()
        {
            // _pos at 'u'
            if (_pos + 4 >= _text.Length)
                return 'u';
            string hex = _text.Substring(_pos + 1, 4);
            _pos += 4;
            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                return (char)code;
            return 'u';
        }

        private SyntaxNode ParseNumber()
        {
            int start = _pos;
            if (_text[_pos] == '-')
                _pos++;
            while (_pos < _text.Length && char.IsDigit(_text[_pos]))
                _pos++;
            if (_pos < _text.Length && _text[_pos] == '.')
            {
                _pos++;
                while (_pos < _text.Length && char.IsDigit(_text[_pos]))
                    _pos++;
            }

            if (_pos < _text.Length && _text[_pos] is 'e' or 'E')
            {
                _pos++;
                if (_pos < _text.Length && _text[_pos] is '+' or '-')
                    _pos++;
                while (_pos < _text.Length && char.IsDigit(_text[_pos]))
                    _pos++;
            }

            string raw = _text.Substring(start, _pos - start);
            return new SyntaxNode("number", new TextSpan(start, _pos - start), raw);
        }

        private bool MatchLiteral(string literal)
        {
            if (_pos + literal.Length > _text.Length)
                return false;
            for (int i = 0; i < literal.Length; i++)
            {
                if (_text[_pos + i] != literal[i])
                    return false;
            }

            // boundary
            int after = _pos + literal.Length;
            if (after < _text.Length && (char.IsLetterOrDigit(_text[after]) || _text[after] == '_'))
                return false;
            _pos = after;
            return true;
        }

        private void SkipValueShallow()
        {
            SkipTrivia();
            if (_pos >= _text.Length)
                return;
            char c = _text[_pos];
            if (c == '{')
            {
                int depth = 0;
                while (_pos < _text.Length)
                {
                    if (_text[_pos] == '{')
                        depth++;
                    else if (_text[_pos] == '}')
                    {
                        depth--;
                        _pos++;
                        if (depth == 0)
                            return;
                        continue;
                    }
                    else if (_text[_pos] == '"')
                    {
                        ParseString();
                        continue;
                    }

                    _pos++;
                }
            }
            else if (c == '[')
            {
                int depth = 0;
                while (_pos < _text.Length)
                {
                    if (_text[_pos] == '[')
                        depth++;
                    else if (_text[_pos] == ']')
                    {
                        depth--;
                        _pos++;
                        if (depth == 0)
                            return;
                        continue;
                    }
                    else if (_text[_pos] == '"')
                    {
                        ParseString();
                        continue;
                    }

                    _pos++;
                }
            }
            else if (c == '"')
                ParseString();
            else
            {
                while (_pos < _text.Length
                       && _text[_pos] is not (',' or ']' or '}' or ' ' or '\t' or '\n' or '\r'))
                    _pos++;
            }
        }

        private static IReadOnlyDictionary<string, string>? MergeProps(
            IReadOnlyDictionary<string, string>? existing,
            Dictionary<string, string> extra)
        {
            if (existing is null || existing.Count == 0)
                return extra;
            Dictionary<string, string> merged = new(existing);
            foreach (KeyValuePair<string, string> kv in extra)
                merged[kv.Key] = kv.Value;
            return merged;
        }
    }
}
