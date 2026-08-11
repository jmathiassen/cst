using Xunit;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Tests;

public class LexCursorTests
{
    [Fact]
    public void ReadIdentifier_Simple_Works()
    {
        LexCursor c = new("foo_bar12 rest");
        TextSpan id = c.ReadIdentifier();
        Assert.Equal("foo_bar12", c.Slice(id));
        Assert.Equal(' ', c.Peek());
    }

    [Fact]
    public void SkipLineComment_ConsumesToNewline()
    {
        LexCursor c = new("// hello\nworld");
        TextSpan span = c.SkipLineComment();
        Assert.Equal("// hello", c.Slice(span));
        Assert.Equal('\n', c.Peek());
    }

    [Fact]
    public void SkipBlockComment_NestedStars()
    {
        LexCursor c = new("/* a * b */x");
        TextSpan span = c.SkipBlockComment(out bool unclosed);
        Assert.False(unclosed);
        Assert.Equal("/* a * b */", c.Slice(span));
        Assert.Equal('x', c.Peek());
    }

    [Fact]
    public void ReadQuotedString_Escapes()
    {
        LexCursor c = new("\"a\\\"b\"c");
        TextSpan span = c.ReadQuotedString(out bool unclosed);
        Assert.False(unclosed);
        Assert.Equal("\"a\\\"b\"", c.Slice(span));
        Assert.Equal('c', c.Peek());
    }

    [Fact]
    public void ReadTemplateString_Simple()
    {
        LexCursor c = new("`hi ${x}`!");
        TextSpan span = c.ReadTemplateString(out bool unclosed);
        Assert.False(unclosed);
        Assert.Equal("`hi ${x}`", c.Slice(span));
        Assert.Equal('!', c.Peek());
    }

    [Fact]
    public void ReadNumber_FloatExponent()
    {
        LexCursor c = new("1.5e+10;");
        TextSpan span = c.ReadNumber();
        Assert.Equal("1.5e+10", c.Slice(span));
    }

    [Fact]
    public void TokenReader_TrySkipBalanced()
    {
        // kinds: 1={ 2=} 3=id
        Token[] tokens =
        [
            new Token(1, new TextSpan(0, 1)),
            new Token(3, new TextSpan(1, 1), "a"),
            new Token(1, new TextSpan(2, 1)),
            new Token(2, new TextSpan(3, 1)),
            new Token(2, new TextSpan(4, 1)),
            LexCursor.Eof(5),
        ];
        TokenReader r = new(tokens);
        Assert.True(r.TrySkipBalanced(1, 2));
        Assert.True(r.IsEof);
    }

    [Fact]
    public void TokenReader_Seek_RestoresIndex()
    {
        Token[] tokens =
        [
            new Token(1, new TextSpan(0, 1)),
            new Token(2, new TextSpan(1, 1)),
            LexCursor.Eof(2),
        ];
        TokenReader r = new(tokens);
        Assert.Equal(1, r.Current.Kind);
        r.Advance();
        Assert.Equal(2, r.Current.Kind);
        r.Seek(0);
        Assert.Equal(1, r.Current.Kind);
        Assert.Equal(0, r.Index);
    }
}
