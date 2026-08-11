using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Csv;

/// <summary>CSV/TSV language pack.</summary>
public sealed class CsvLanguagePack : ILanguagePack, IOutlineProvider
{
    /// <summary>Max data rows included in outline.</summary>
    public const int CsvOutlineMaxRows = 20;

    private static readonly string[] Extensions = [".csv", ".tsv"];

    /// <inheritdoc />
    public string LanguageId => "csv";

    /// <inheritdoc />
    public string DisplayName => "CSV";

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

        char delimiter = ResolveDelimiter(source.FilePath);
        string delimiterProp = delimiter == '\t' ? "tab" : "comma";

        if (ParseHelpers.IsNullOrWhiteSpace(text))
        {
            Dictionary<string, string> emptyProps = new() { ["delimiter"] = delimiterProp };
            SyntaxNode emptyRoot = new("empty", new TextSpan(0, 0), properties: emptyProps);
            return new ConcreteSyntaxTree(
                LanguageId, effective, emptyRoot, diagnostics,
                SyntaxConfidence.Syntax, DefaultQuality, isPartial,
                cachedOutline: Array.Empty<OutlineNode>());
        }

        LineMap map = new(text);
        List<RowData> rows = ParseRows(text, delimiter, map, diagnostics, cancellationToken, ref isPartial);

        List<SyntaxNode> rowNodes = [];
        int headerFieldCount = -1;

        for (int r = 0; r < rows.Count; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RowData row = rows[r];
            string kind = r == 0 ? "header" : "row";

            if (r == 0)
                headerFieldCount = row.Fields.Count;
            else if (headerFieldCount >= 0 && row.Fields.Count != headerFieldCount)
            {
                diagnostics.Add(new ParseDiagnostic(
                    "CSV001",
                    $"Ragged row: expected {headerFieldCount} fields, found {row.Fields.Count}.",
                    row.Span,
                    DiagnosticSeverity.Warning));
            }

            List<SyntaxNode> fieldNodes = [];
            for (int f = 0; f < row.Fields.Count; f++)
            {
                FieldData field = row.Fields[f];
                Dictionary<string, string> fieldProps = new() { ["index"] = f.ToString() };
                string? name = r == 0 ? field.Text : field.Text;
                fieldNodes.Add(new SyntaxNode(
                    "field", field.Span, name, properties: fieldProps));
            }

            rowNodes.Add(new SyntaxNode(kind, row.Span, children: fieldNodes));
        }

        Dictionary<string, string> docProps = new() { ["delimiter"] = delimiterProp };
        SyntaxNode root = new(
            "document",
            new TextSpan(0, text.Length),
            children: rowNodes,
            properties: docProps);

        ConcreteSyntaxTree tree = new(
            LanguageId, effective, root, diagnostics,
            SyntaxConfidence.Syntax, DefaultQuality, isPartial);
        IReadOnlyList<OutlineNode> outline = BuildOutline(tree, cancellationToken);
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
        return BuildOutline(tree, cancellationToken);
    }

    private IReadOnlyList<OutlineNode> BuildOutline(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken)
    {
        if (tree.Root.Kind == "empty" || tree.Root.Children.Count == 0)
            return Array.Empty<OutlineNode>();

        LineMap map = new(tree.Source.Text);
        List<OutlineNode> result = [];

        SyntaxNode? header = null;
        List<SyntaxNode> dataRows = [];
        foreach (SyntaxNode child in tree.Root.Children)
        {
            if (child.Kind == "header" && header is null)
                header = child;
            else if (child.Kind == "row")
                dataRows.Add(child);
        }

        // If no explicit header kind, treat first row as header when present
        if (header is null && tree.Root.Children.Count > 0)
        {
            header = tree.Root.Children[0];
            dataRows.Clear();
            for (int i = 1; i < tree.Root.Children.Count; i++)
                dataRows.Add(tree.Root.Children[i]);
        }

        if (header is not null)
        {
            List<string> names = [];
            List<OutlineNode> columns = [];
            foreach (SyntaxNode field in header.Children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string label = string.IsNullOrEmpty(field.Name)
                    ? $"Column{columns.Count}"
                    : field.Name!;
                names.Add(label);
                int line = map.GetLineNumber(field.Span.Start);
                columns.Add(new OutlineNode(
                    "column",
                    label,
                    field.Span,
                    map.GetLineSpan(line, line),
                    2));
            }

            string headerLabel = names.Count == 0
                ? "columns"
                : Truncate(string.Join(", ", names), 80);

            int hStart = map.GetLineNumber(header.Span.Start);
            int hEnd = map.GetLineNumber(
                header.Span.Length > 0 ? header.Span.End - 1 : header.Span.Start);

            result.Add(new OutlineNode(
                "header",
                headerLabel,
                header.Span,
                map.GetLineSpan(hStart, hEnd),
                1,
                columns));
        }

        int rowLimit = Math.Min(dataRows.Count, CsvOutlineMaxRows);
        for (int i = 0; i < rowLimit; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyntaxNode row = dataRows[i];
            string label = row.Children.Count > 0 && !string.IsNullOrEmpty(row.Children[0].Name)
                ? row.Children[0].Name!
                : $"Row {i + 1}";
            int rStart = map.GetLineNumber(row.Span.Start);
            int rEnd = map.GetLineNumber(row.Span.Length > 0 ? row.Span.End - 1 : row.Span.Start);
            result.Add(new OutlineNode(
                "row",
                label,
                row.Span,
                map.GetLineSpan(rStart, rEnd),
                2));
        }

        return result;
    }

    private static char ResolveDelimiter(string? filePath)
    {
        if (filePath is null)
            return ',';
        string ext = Path.GetExtension(filePath);
        if (string.Equals(ext, ".tsv", StringComparison.OrdinalIgnoreCase))
            return '\t';
        return ',';
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;
        return value.Substring(0, max - 1) + "…";
    }

    private sealed class FieldData
    {
        public required string Text { get; init; }
        public required TextSpan Span { get; init; }
    }

    private sealed class RowData
    {
        public required List<FieldData> Fields { get; init; }
        public required TextSpan Span { get; init; }
    }

    private static List<RowData> ParseRows(
        string text,
        char delimiter,
        LineMap map,
        List<ParseDiagnostic> diagnostics,
        CancellationToken cancellationToken,
        ref bool isPartial)
    {
        List<RowData> rows = [];
        List<FieldData> fields = [];
        StringBuilder field = new();
        int fieldStart = 0;
        int rowStart = 0;
        bool inQuotes = false;
        int i = 0;

        void EndField(int endExclusive)
        {
            fields.Add(new FieldData
            {
                Text = field.ToString(),
                Span = new TextSpan(fieldStart, Math.Max(0, endExclusive - fieldStart))
            });
            field.Clear();
        }

        void EndRow(int endExclusive)
        {
            if (fields.Count == 0 && field.Length == 0 && endExclusive == rowStart)
            {
                // completely empty line — skip as row
                rowStart = endExclusive;
                fieldStart = endExclusive;
                return;
            }

            EndField(endExclusive);
            // skip empty trailing row at EOF only if nothing
            bool allEmpty = fields.Count == 1 && fields[0].Text.Length == 0 && endExclusive == rowStart + (endExclusive > rowStart && text[rowStart] is '\n' or '\r' ? 0 : 0);
            // Always keep non-empty content rows; keep header-capable rows
            if (!(fields.Count == 1 && fields[0].Text.Length == 0 && IsOnlyNewlineRange(text, rowStart, endExclusive)))
            {
                rows.Add(new RowData
                {
                    Fields = fields,
                    Span = new TextSpan(rowStart, Math.Max(0, endExclusive - rowStart))
                });
            }

            fields = [];
            rowStart = endExclusive;
            fieldStart = endExclusive;
        }

        while (i < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                field.Append(c);
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                i++;
                continue;
            }

            if (c == delimiter)
            {
                EndField(i);
                i++;
                fieldStart = i;
                continue;
            }

            if (c == '\n')
            {
                EndRow(i);
                i++;
                rowStart = i;
                fieldStart = i;
                continue;
            }

            if (c == '\r')
            {
                EndRow(i);
                i++;
                if (i < text.Length && text[i] == '\n')
                    i++;
                rowStart = i;
                fieldStart = i;
                continue;
            }

            field.Append(c);
            i++;
        }

        if (inQuotes)
        {
            isPartial = true;
            diagnostics.Add(new ParseDiagnostic(
                "CSV002",
                "Unclosed quote in CSV field.",
                new TextSpan(fieldStart, Math.Max(0, text.Length - fieldStart)),
                DiagnosticSeverity.Error));
        }

        if (fields.Count > 0 || field.Length > 0 || rowStart < text.Length)
            EndRow(text.Length);

        return rows;
    }

    private static bool IsOnlyNewlineRange(string text, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (text[i] is not ('\n' or '\r'))
                return false;
        }
        return true;
    }
}
