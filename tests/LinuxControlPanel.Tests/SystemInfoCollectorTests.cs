using LinuxControlPanel.Core.Linux;

namespace LinuxControlPanel.Tests;

public class SystemInfoCollectorTests
{
    [Fact]
    public void Parse_실제_서버_출력에서_기본_정보를_읽는다()
    {
        var snapshot = SystemInfoCollector.Parse(RealServerOutput.Raw, previousSample: null);

        Assert.Equal("acerver", snapshot.Hostname);
        Assert.Equal("Ubuntu 20.04.6 LTS", snapshot.OsPrettyName);
        Assert.Equal("Linux 5.4.0-216-generic", snapshot.Kernel);
        Assert.Equal("x86_64", snapshot.Architecture);
        Assert.Equal("Intel(R) Pentium(R) CPU 4405U @ 2.10GHz", snapshot.CpuModel);
        Assert.Equal(4, snapshot.CpuCount);
        Assert.Equal(4, snapshot.Cores.Count);
        Assert.Equal(647186.53, snapshot.UptimeSeconds, 2);
        Assert.Equal(3.51, snapshot.Load1, 2);
        Assert.Equal(59d, snapshot.CpuTemperatureCelsius);
        Assert.Contains("7일", snapshot.UptimeText);
    }

    [Fact]
    public void Parse_CPU_사용률은_두_표본_차이로_나온다()
    {
        var snapshot = SystemInfoCollector.Parse(RealServerOutput.Raw, previousSample: null);

        // 표본 차이: 전체 119, 유휴 68 -> 약 42.9%
        Assert.InRange(snapshot.CpuUsagePercent, 42, 44);
        Assert.All(snapshot.Cores, core => Assert.InRange(core.Percent, 0, 100));
        Assert.Equal(new[] { 0, 1, 2, 3 }, snapshot.Cores.Select(core => core.Index));
    }

    [Fact]
    public void Parse_메모리와_디스크를_읽는다()
    {
        var snapshot = SystemInfoCollector.Parse(RealServerOutput.Raw, previousSample: null);

        Assert.Equal(12146476L * 1024, snapshot.Memory.TotalBytes);
        Assert.InRange(snapshot.Memory.UsedPercent, 39, 41);
        Assert.Equal(2097148L * 1024, snapshot.Memory.SwapTotalBytes);

        Assert.Equal(2, snapshot.Disks.Count);
        Assert.Equal("/", snapshot.Disks[0].MountPoint);
        Assert.Equal("/boot", snapshot.Disks[1].MountPoint);
    }

    [Fact]
    public void Parse_상위_프로세스를_CPU_순서대로_읽는다()
    {
        var snapshot = SystemInfoCollector.Parse(RealServerOutput.Raw, previousSample: null);

        Assert.Equal(5, snapshot.TopProcesses.Count);
        Assert.Equal("tailscaled", snapshot.TopProcesses[0].Command);
        Assert.Equal(874, snapshot.TopProcesses[0].Pid);
        Assert.Empty(snapshot.Users);
    }

    [Fact]
    public void Parse_첫_수집은_초당_전송량이_0이다()
    {
        var snapshot = SystemInfoCollector.Parse(RealServerOutput.Raw, previousSample: null);

        Assert.NotEmpty(snapshot.Network);
        Assert.All(snapshot.Network, rate => Assert.Equal(0d, rate.RxBytesPerSecond));
        Assert.All(snapshot.Network, rate => Assert.Equal(0d, rate.TxBytesPerSecond));
    }

    [Fact]
    public void Parse_두번째_수집은_가동시간_차이로_초당_전송량을_구한다()
    {
        var first = SystemInfoCollector.Parse(RealServerOutput.Raw, previousSample: null);

        // 10초 뒤, enp2s0 가 받은 바이트가 10,000,000 늘어난 상황을 만든다.
        var later = RealServerOutput.Raw
            .Replace("647186.53 1910636.98", "647196.53 1910646.98")
            .Replace("enp2s0: 78789117003", "enp2s0: 78799117003");

        var second = SystemInfoCollector.Parse(later, first.Sample);

        var enp = second.Network.Single(rate => rate.Name == "enp2s0");
        Assert.Equal(1_000_000d, enp.RxBytesPerSecond, 1);
        Assert.Equal(0d, enp.TxBytesPerSecond);

        // 가장 바쁜 인터페이스가 먼저 나와야 한다.
        Assert.Equal("enp2s0", second.Network[0].Name);
    }

    [Fact]
    public void Parse_카운터가_초기화되면_음수가_아니라_0으로_본다()
    {
        var first = SystemInfoCollector.Parse(RealServerOutput.Raw, previousSample: null);

        var rebooted = RealServerOutput.Raw
            .Replace("647186.53 1910636.98", "647196.53 1910646.98")
            .Replace("enp2s0: 78789117003", "enp2s0: 1000");

        var second = SystemInfoCollector.Parse(rebooted, first.Sample);

        var enp = second.Network.Single(rate => rate.Name == "enp2s0");
        Assert.Equal(0d, enp.RxBytesPerSecond);
    }

    [Fact]
    public void Parse_출력이_비어도_예외를_던지지_않는다()
    {
        var snapshot = SystemInfoCollector.Parse(string.Empty, previousSample: null);

        Assert.Equal(string.Empty, snapshot.Hostname);
        Assert.Equal(0d, snapshot.CpuUsagePercent);
        Assert.Empty(snapshot.Disks);
        Assert.Equal(MemoryUsage.Empty, snapshot.Memory);
    }

    [Fact]
    public void 수집_명령은_모든_구역을_포함한다()
    {
        foreach (var section in new[] { "HOSTNAME", "UNAME", "OS", "CPUMODEL", "CPUCOUNT", "STAT1", "STAT2", "UPTIME", "LOADAVG", "MEM", "DISK", "NETDEV", "PS", "WHO", "TEMP", "END" })
        {
            Assert.Contains("#!" + section, SystemInfoCollector.CollectCommand);
        }
    }
}
