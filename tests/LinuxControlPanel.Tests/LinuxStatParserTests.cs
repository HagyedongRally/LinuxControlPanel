using LinuxControlPanel.Core.Linux;

namespace LinuxControlPanel.Tests;

public class LinuxStatParserTests
{
    [Fact]
    public void SplitSections_표시마다_구역을_나눈다()
    {
        var raw = "#!HOSTNAME\nacerver\n#!UPTIME\n647186.53 1910636.98\n#!END\n";

        var sections = LinuxStatParser.SplitSections(raw);

        Assert.Equal("acerver", sections["HOSTNAME"].Trim());
        Assert.Equal("647186.53 1910636.98", sections["UPTIME"].Trim());
        Assert.True(sections.ContainsKey("END"));
    }

    [Fact]
    public void SplitSections_표시_앞의_잡소리는_버린다()
    {
        var sections = LinuxStatParser.SplitSections("stty: standard input\n#!HOSTNAME\nbox\n");

        Assert.Single(sections);
        Assert.Equal("box", sections["HOSTNAME"].Trim());
    }

    [Fact]
    public void ParseCpuLine_유휴와_iowait를_유휴시간으로_센다()
    {
        var times = LinuxStatParser.ParseCpuLine("cpu  100 20 30 400 50 0 10 0 0 0");

        Assert.NotNull(times);
        Assert.Equal("cpu", times!.Value.Name);
        Assert.Equal(610, times.Value.Total);
        Assert.Equal(450, times.Value.Idle);
    }

    [Fact]
    public void ParseCpuLine_cpu로_시작하지_않으면_null()
    {
        Assert.Null(LinuxStatParser.ParseCpuLine("intr 12345 0 0 0"));
    }

    [Fact]
    public void ParseCpuLines_코어별로_담는다()
    {
        var block = "cpu  10 0 10 80 0 0 0 0 0 0\ncpu0 5 0 5 40 0 0 0 0 0 0\ncpu1 5 0 5 40 0 0 0 0 0 0\n";

        var lines = LinuxStatParser.ParseCpuLines(block);

        Assert.Equal(3, lines.Count);
        Assert.Equal(100, lines["cpu"].Total);
        Assert.Equal(50, lines["cpu0"].Total);
    }

    [Fact]
    public void CpuUsagePercent_두_표본의_차이로_구한다()
    {
        var before = new CpuTimes("cpu", 1000, 800);
        var after = new CpuTimes("cpu", 1100, 850);

        Assert.Equal(50d, LinuxStatParser.CpuUsagePercent(before, after), 3);
    }

    [Fact]
    public void CpuUsagePercent_표본이_거꾸로거나_같으면_0()
    {
        var first = new CpuTimes("cpu", 1100, 850);
        var second = new CpuTimes("cpu", 1000, 800);

        Assert.Equal(0d, LinuxStatParser.CpuUsagePercent(first, second));
        Assert.Equal(0d, LinuxStatParser.CpuUsagePercent(first, first));
    }

    [Fact]
    public void ParseMemInfo_kB를_바이트로_바꾸고_사용량을_구한다()
    {
        var block = string.Join("\n",
            "MemTotal:       12146476 kB",
            "MemFree:          176920 kB",
            "MemAvailable:    7292208 kB",
            "Buffers:          749800 kB",
            "Cached:          6468240 kB",
            "SwapTotal:       2097148 kB",
            "SwapFree:        2000000 kB");

        var memory = LinuxStatParser.ParseMemInfo(block);

        Assert.Equal(12146476L * 1024, memory.TotalBytes);
        Assert.Equal(7292208L * 1024, memory.AvailableBytes);
        Assert.Equal((12146476L - 7292208L) * 1024, memory.UsedBytes);
        Assert.Equal((2097148L - 2000000L) * 1024, memory.SwapUsedBytes);
        Assert.InRange(memory.UsedPercent, 39, 41);
    }

    [Fact]
    public void ParseMemInfo_MemAvailable이_없으면_Free_Buffers_Cached로_추정한다()
    {
        var block = "MemTotal:       1000 kB\nMemFree:  100 kB\nBuffers:  200 kB\nCached:   300 kB\n";

        var memory = LinuxStatParser.ParseMemInfo(block);

        Assert.Equal(600L * 1024, memory.AvailableBytes);
        Assert.Equal(400L * 1024, memory.UsedBytes);
    }

    [Fact]
    public void ParseMemInfo_스왑이_없으면_0퍼센트()
    {
        var memory = LinuxStatParser.ParseMemInfo("MemTotal: 1000 kB\nMemAvailable: 500 kB\nSwapTotal: 0 kB\nSwapFree: 0 kB\n");

        Assert.Equal(0, memory.SwapTotalBytes);
        Assert.Equal(0d, memory.SwapUsedPercent);
    }

    [Fact]
    public void ParseDf_마운트를_읽고_가상_파일시스템과_snap은_버린다()
    {
        var block = string.Join("\n",
            "Filesystem                            1-blocks        Used   Available Capacity Mounted on",
            "/dev/mapper/ubuntu--vg-ubuntu--lv 105089261568 46139383808 53564391424      47% /",
            "/dev/sda2                           2040373248   119132160  1797091328       7% /boot",
            "tmpfs                               1244311552           0  1244311552       0% /run/user/1000",
            "/dev/loop3                            68157440    68157440           0     100% /snap/core20/2434");

        var disks = LinuxStatParser.ParseDf(block);

        Assert.Equal(2, disks.Count);
        Assert.Equal("/", disks[0].MountPoint);
        Assert.Equal("/boot", disks[1].MountPoint);
        Assert.Equal(105089261568L, disks[0].TotalBytes);
        Assert.Equal("/dev/mapper/ubuntu--vg-ubuntu--lv", disks[0].FileSystem);
        Assert.InRange(disks[0].UsedPercent, 43, 45);
    }

    [Fact]
    public void ParseNetDev_받기와_보내기_누적_바이트를_읽는다()
    {
        var block = string.Join("\n",
            "Inter-|   Receive                                                |  Transmit",
            " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed",
            "enp2s0: 78789117003 486768494    0 323194    0     0          0    190619 90620213240 696267796   22    0    0     0       0          0",
            "    lo: 53530331711 197903845    0    0    0     0          0         0 53530331711 197903845    0    0    0     0       0          0");

        var counters = LinuxStatParser.ParseNetDev(block);

        Assert.Equal(2, counters.Count);
        Assert.Equal(78789117003L, counters["enp2s0"].RxBytes);
        Assert.Equal(90620213240L, counters["enp2s0"].TxBytes);
        Assert.Equal(53530331711L, counters["lo"].RxBytes);
    }

    [Fact]
    public void ParsePs_머리글을_건너뛰고_프로세스를_읽는다()
    {
        var block = string.Join("\n",
            "    PID %CPU %MEM COMMAND",
            "    874 35.0  0.9 tailscaled",
            "2456402  5.1  1.8 dotnet");

        var processes = LinuxStatParser.ParsePs(block);

        Assert.Equal(2, processes.Count);
        Assert.Equal(874, processes[0].Pid);
        Assert.Equal(35.0, processes[0].CpuPercent, 3);
        Assert.Equal("tailscaled", processes[0].Command);
        Assert.Equal(2456402, processes[1].Pid);
    }

    [Fact]
    public void ParseWho_사용자와_접속_위치를_읽는다()
    {
        var users = LinuxStatParser.ParseWho("turtlelab pts/0        2026-10-15 09:12 (192.168.219.2)\n");

        var user = Assert.Single(users);
        Assert.Equal("turtlelab", user.Name);
        Assert.Equal("pts/0", user.Terminal);
        Assert.Equal("2026-10-15 09:12", user.LoginTime);
        Assert.Equal("192.168.219.2", user.From);
    }

    [Fact]
    public void ParseWho_아무도_없으면_빈_목록()
    {
        Assert.Empty(LinuxStatParser.ParseWho(string.Empty));
    }

    [Fact]
    public void ParseOsPrettyName_PRETTY_NAME을_쓴다()
    {
        var block = "NAME=\"Ubuntu\"\nVERSION=\"20.04.6 LTS (Focal Fossa)\"\nPRETTY_NAME=\"Ubuntu 20.04.6 LTS\"\n";

        Assert.Equal("Ubuntu 20.04.6 LTS", LinuxStatParser.ParseOsPrettyName(block));
    }

    [Fact]
    public void ParseOsPrettyName_없으면_NAME과_VERSION을_합친다()
    {
        Assert.Equal("Debian GNU/Linux 12", LinuxStatParser.ParseOsPrettyName("NAME=\"Debian GNU/Linux\"\nVERSION=\"12\"\n"));
    }

    [Fact]
    public void ParseThermal_가장_높은_온도를_섭씨로_준다()
    {
        Assert.Equal(59d, LinuxStatParser.ParseThermal("57000\n51000\n55500\n59000\n"));
    }

    [Fact]
    public void ParseThermal_값이_없거나_이상하면_null()
    {
        Assert.Null(LinuxStatParser.ParseThermal(string.Empty));
        Assert.Null(LinuxStatParser.ParseThermal("0\n-5000\n9999000\n"));
    }

    [Fact]
    public void ParseUptimeSeconds와_ParseLoadAvg와_ParseUname을_읽는다()
    {
        Assert.Equal(647186.53, LinuxStatParser.ParseUptimeSeconds("647186.53 1910636.98\n"), 2);

        var load = LinuxStatParser.ParseLoadAvg("3.51 2.26 2.43 1/13591 34068\n");
        Assert.Equal(3.51, load.One, 2);
        Assert.Equal(2.26, load.Five, 2);
        Assert.Equal(2.43, load.Fifteen, 2);

        var uname = LinuxStatParser.ParseUname("Linux 5.4.0-216-generic\nx86_64\n");
        Assert.Equal("Linux 5.4.0-216-generic", uname.Kernel);
        Assert.Equal("x86_64", uname.Architecture);
    }

    [Fact]
    public void ParseCpuModel과_ParseCpuCount를_읽는다()
    {
        Assert.Equal(
            "Intel(R) Pentium(R) CPU 4405U @ 2.10GHz",
            LinuxStatParser.ParseCpuModel("model name\t: Intel(R) Pentium(R) CPU 4405U @ 2.10GHz\n"));

        Assert.Equal(4, LinuxStatParser.ParseCpuCount("4\n"));
        Assert.Equal(0, LinuxStatParser.ParseCpuCount(string.Empty));
    }
}
