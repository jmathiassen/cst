using System;

using Xunit;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Tests;

public class BlockScannersTests
{
    [Fact]
    public void FindBraceBlockEnd_Simple_ReturnsCloseLine()
    {
        string[] lines = ["fn() {", "  x();", "}"];
        bool partial = false;
        int end = BlockScanners.FindBraceBlockEnd(lines, 0, ref partial);
        Assert.Equal(3, end);
        Assert.False(partial);
    }

    [Fact]
    public void FindBraceBlockEnd_BraceInString_Ignored()
    {
        string[] lines = ["s = \"{\";", "real() {", "}", "after"];
        bool partial = false;
        int end = BlockScanners.FindBraceBlockEnd(lines, 1, ref partial);
        Assert.Equal(3, end);
        Assert.False(partial);
    }

    [Fact]
    public void FindBraceBlockEnd_Unclosed_SetsPartial()
    {
        string[] lines = ["fn() {", "  x();"];
        bool partial = false;
        int end = BlockScanners.FindBraceBlockEnd(lines, 0, ref partial);
        Assert.Equal(2, end);
        Assert.True(partial);
    }

    [Fact]
    public void FindEndKeyword_RubyStyle_FindsEnd()
    {
        string[] lines = ["class A", "  def x", "  end", "end"];
        bool partial = false;
        int end = BlockScanners.FindEndKeyword(
            lines, 0, ["end"], BlockScanners.DefaultOpenPrefixes, ref partial);
        Assert.Equal(4, end);
        Assert.False(partial);
    }
}
