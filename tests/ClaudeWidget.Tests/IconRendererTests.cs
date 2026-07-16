using System.Drawing;
using ClaudeWidget.UI;

namespace ClaudeWidget.Tests;

public class IconRendererTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(69.9)]
    public void GreenBelow70(double pct)
        => Assert.Equal(IconRenderer.Green, IconRenderer.ColorFor(pct, false, false));

    [Theory]
    [InlineData(70)]
    [InlineData(89.9)]
    public void AmberFrom70(double pct)
        => Assert.Equal(IconRenderer.Amber, IconRenderer.ColorFor(pct, false, false));

    [Theory]
    [InlineData(90)]
    [InlineData(100)]
    public void RedFrom90(double pct)
        => Assert.Equal(IconRenderer.Red, IconRenderer.ColorFor(pct, false, false));

    [Fact]
    public void GrayWhenStaleAuthErrorOrNoData()
    {
        Assert.Equal(IconRenderer.Gray, IconRenderer.ColorFor(42, stale: true, authError: false));
        Assert.Equal(IconRenderer.Gray, IconRenderer.ColorFor(42, stale: false, authError: true));
        Assert.Equal(IconRenderer.Gray, IconRenderer.ColorFor(null, false, false));
    }

    [Theory]
    [InlineData(42.4, "42")]
    [InlineData(0, "0")]
    [InlineData(99.5, "!!")]
    [InlineData(100, "!!")]
    public void IconTextForPercent(double pct, string expected)
        => Assert.Equal(expected, IconRenderer.IconText(pct, authError: false));

    [Fact]
    public void IconTextSpecialStates()
    {
        Assert.Equal("!", IconRenderer.IconText(42, authError: true));
        Assert.Equal("?", IconRenderer.IconText(null, authError: false));
    }

    [Fact]
    public void RenderProducesIcon()
    {
        using var icon = IconRenderer.Render("42", IconRenderer.Green);
        Assert.Equal(32, icon.Width);
        Assert.Equal(32, icon.Height);
    }
}
