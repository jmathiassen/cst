using Xunit;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Tests;

public class LineMapTests
{
    [Fact]
    public void GetLineNumber_Empty_ReturnsOne()
    {
        // Arrange
        LineMap map = new("");

        // Act / Assert
        Assert.Equal(1, map.GetLineNumber(0));
        Assert.Equal(1, map.LineCount);
    }

    [Fact]
    public void GetLineNumber_Multiline_MapsOffsets()
    {
        // Arrange
        LineMap map = new("a\nb\nc");

        // Act / Assert
        Assert.Equal(1, map.GetLineNumber(0));
        Assert.Equal(2, map.GetLineNumber(2));
        Assert.Equal(3, map.GetLineNumber(4));
        Assert.Equal(3, map.LineCount);
    }

    [Fact]
    public void GetLinePositionSpan_CoversInclusiveLines()
    {
        // Arrange
        LineMap map = new("hello\nworld");
        TextSpan span = new(0, 11);

        // Act
        LinePositionSpan lps = map.GetLinePositionSpan(span);

        // Assert
        Assert.Equal(1, lps.Start.Line);
        Assert.Equal(2, lps.End.Line);
    }

    [Fact]
    public void GetSpanForLines_ReturnsLineRange()
    {
        // Arrange
        LineMap map = new("one\ntwo\nthree");

        // Act
        TextSpan span = map.GetSpanForLines(2, 2);

        // Assert
        Assert.Equal("two\n", "one\ntwo\nthree".Substring(span.Start, span.Length));
    }
}
