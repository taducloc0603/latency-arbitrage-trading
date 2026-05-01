using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class HwndParserTests
{
    [Fact]
    public void TryParse_AcceptsDecimal()
    {
        var ok = HwndParser.TryParse("12345", out var hwnd, out var error);

        Assert.True(ok, error);
        Assert.Equal(12345UL, hwnd);
    }

    [Fact]
    public void TryParse_AcceptsHex()
    {
        var ok = HwndParser.TryParse("0x3039", out var hwnd, out var error);

        Assert.True(ok, error);
        Assert.Equal(12345UL, hwnd);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hwnd")]
    [InlineData("0")]
    [InlineData("0x0")]
    public void TryParse_RejectsInvalidValues(string value)
    {
        var ok = HwndParser.TryParse(value, out var hwnd, out var error);

        Assert.False(ok);
        Assert.Equal(0UL, hwnd);
        Assert.NotEmpty(error);
    }
}

