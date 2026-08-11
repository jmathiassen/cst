using System;

namespace TgrDevelopments.Cst;

/// <summary>Maps UTF-16 offsets to 1-based line/column positions.</summary>
public sealed class LineMap
{
    private readonly int[] _lineStarts;
    private readonly string _text;

    /// <summary>Creates a line map for the given text.</summary>
    /// <param name="text">Source text.</param>
    public LineMap(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _lineStarts = BuildLineStarts(text);
    }

    /// <summary>Gets the number of lines (at least 1 for empty text).</summary>
    public int LineCount => _lineStarts.Length;

    /// <summary>Gets the 1-based line containing <paramref name="offset"/>.</summary>
    public int GetLineNumber(int offset)
    {
        if (_text.Length == 0)
            return 1;
        if (offset < 0)
            offset = 0;
        if (offset > _text.Length)
            offset = _text.Length;

        int index = Array.BinarySearch(_lineStarts, offset);
        if (index < 0)
            index = ~index - 1;
        if (index < 0)
            index = 0;
        return index + 1;
    }

    /// <summary>Converts an offset to a 1-based <see cref="LinePosition"/>.</summary>
    public LinePosition GetPosition(int offset)
    {
        if (_text.Length == 0)
            return new LinePosition(1, 1);
        if (offset < 0)
            offset = 0;
        if (offset > _text.Length)
            offset = _text.Length;

        int line = GetLineNumber(offset);
        int lineStart = _lineStarts[line - 1];
        int column = offset - lineStart + 1;
        return new LinePosition(line, column);
    }

    /// <summary>Converts a text span to an inclusive 1-based line position span.</summary>
    public LinePositionSpan GetLinePositionSpan(TextSpan span)
    {
        if (_text.Length == 0)
            return new LinePositionSpan(new LinePosition(1, 1), new LinePosition(1, 1));

        int start = span.Start;
        int endExclusive = span.End;
        if (start < 0)
            start = 0;
        if (endExclusive < start)
            endExclusive = start;
        if (endExclusive > _text.Length)
            endExclusive = _text.Length;

        LinePosition startPos = GetPosition(start);
        int endOffset = endExclusive > start ? endExclusive - 1 : start;
        LinePosition endPos = GetPosition(endOffset);
        return new LinePositionSpan(startPos, endPos);
    }

    /// <summary>Creates a line span covering inclusive 1-based from/to lines.</summary>
    public LinePositionSpan GetLineSpan(int fromLine, int toLine)
    {
        if (fromLine < 1)
            fromLine = 1;
        if (toLine < fromLine)
            toLine = fromLine;
        if (fromLine > LineCount)
            fromLine = LineCount;
        if (toLine > LineCount)
            toLine = LineCount;

        return new LinePositionSpan(
            new LinePosition(fromLine, 1),
            new LinePosition(toLine, 1));
    }

    /// <summary>Gets the UTF-16 start offset of a 1-based line.</summary>
    public int GetLineStartOffset(int line)
    {
        if (line < 1)
            line = 1;
        if (line > LineCount)
            line = LineCount;
        return _lineStarts[line - 1];
    }

    /// <summary>Gets the exclusive UTF-16 end offset of a 1-based line (start of next line or EOF).</summary>
    public int GetLineEndOffset(int line)
    {
        if (line < 1)
            line = 1;
        if (line >= LineCount)
            return _text.Length;
        return _lineStarts[line];
    }

    /// <summary>Builds a text span covering inclusive 1-based from/to lines.</summary>
    public TextSpan GetSpanForLines(int fromLine, int toLine)
    {
        if (_text.Length == 0)
            return new TextSpan(0, 0);
        if (fromLine < 1)
            fromLine = 1;
        if (toLine < fromLine)
            toLine = fromLine;
        if (fromLine > LineCount)
            fromLine = LineCount;
        if (toLine > LineCount)
            toLine = LineCount;

        int start = GetLineStartOffset(fromLine);
        int end = GetLineEndOffset(toLine);
        return new TextSpan(start, end - start);
    }

    private static int[] BuildLineStarts(string text)
    {
        if (text.Length == 0)
            return [0];

        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
                count++;
            else if (c == '\r')
            {
                count++;
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
            }
        }

        int[] starts = new int[count];
        starts[0] = 0;
        int line = 1;
        for (int i = 0; i < text.Length && line < count; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                starts[line++] = i + 1;
            }
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    starts[line++] = i + 2;
                    i++;
                }
                else
                {
                    starts[line++] = i + 1;
                }
            }
        }

        return starts;
    }
}
