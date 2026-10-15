using LinuxControlPanel.Core.Linux;

namespace LinuxControlPanel.Tests;

public class HumanizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-5, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(3221225472, "3.0 GB")]
    public void Bytes_단위를_올려_표시한다(double value, string expected)
    {
        Assert.Equal(expected, Humanize.Bytes(value));
    }

    [Fact]
    public void BytesPerSecond_는_초당을_붙인다()
    {
        Assert.Equal("1.0 MB/s", Humanize.BytesPerSecond(1048576));
    }

    [Theory]
    [InlineData(0, "-")]
    [InlineData(45, "45초")]
    [InlineData(150, "2분 30초")]
    [InlineData(7200, "2시간 0분")]
    [InlineData(647186, "7일 11시간 46분")]
    public void Duration_일시분으로_표시한다(double seconds, string expected)
    {
        Assert.Equal(expected, Humanize.Duration(seconds));
    }

    [Fact]
    public void Percent_소수점_한자리()
    {
        Assert.Equal("42.9 %", Humanize.Percent(42.857));
    }
}
