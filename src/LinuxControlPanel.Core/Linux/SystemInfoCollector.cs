using LinuxControlPanel.Core.Ssh;

namespace LinuxControlPanel.Core.Linux;

/// <summary>
/// SSH 한 번으로 대시보드에 필요한 모든 값을 긁어온다. 왕복을 줄이려고 명령 하나에
/// 모든 구역을 "#!NAME" 표시와 함께 출력하게 만들고, 돌아온 글을 파서로 나눈다.
/// </summary>
public sealed class SystemInfoCollector
{
    /// <summary>CPU 사용률을 바로 구하려고 /proc/stat 을 0.3초 간격으로 두 번 읽는다.</summary>
    public const string CollectCommand = """
        export LC_ALL=C; echo '#!HOSTNAME'; hostname; echo '#!UNAME'; uname -sr; uname -m; echo '#!OS'; cat /etc/os-release 2>/dev/null; echo '#!CPUMODEL'; grep -m1 -i 'model name' /proc/cpuinfo 2>/dev/null; echo '#!CPUCOUNT'; grep -c '^processor' /proc/cpuinfo 2>/dev/null; echo '#!STAT1'; grep '^cpu' /proc/stat; sleep 0.3; echo '#!STAT2'; grep '^cpu' /proc/stat; echo '#!UPTIME'; cat /proc/uptime; echo '#!LOADAVG'; cat /proc/loadavg; echo '#!MEM'; cat /proc/meminfo; echo '#!DISK'; df -PB1 -x tmpfs -x devtmpfs -x squashfs -x overlay 2>/dev/null; echo '#!NETDEV'; cat /proc/net/dev; echo '#!PS'; ps -eo pid,pcpu,pmem,comm --sort=-pcpu 2>/dev/null | head -n 11; echo '#!WHO'; who 2>/dev/null; echo '#!TEMP'; for f in /sys/class/thermal/thermal_zone*/temp; do if [ -r "$f" ]; then cat "$f"; fi; done 2>/dev/null; echo '#!END'
        """;

    private static readonly TimeSpan CollectTimeout = TimeSpan.FromSeconds(20);

    private NetSample? _previousSample;

    public async Task<SystemSnapshot> CollectAsync(SshSession session, CancellationToken cancellationToken = default)
    {
        var result = await session.RunAsync(CollectCommand, CollectTimeout, cancellationToken).ConfigureAwait(false);

        // 일부 항목이 없어 df/grep 이 실패해도 나머지는 쓸 수 있으므로, 출력이 비었을 때만 오류로 본다.
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"종료 코드 {result.ExitCode}"
                : result.StandardError.Trim();
            throw new InvalidOperationException($"상태 수집 명령이 아무 것도 돌려주지 않았습니다 ({detail}).");
        }

        var snapshot = Parse(result.StandardOutput, _previousSample);
        _previousSample = snapshot.Sample;
        return snapshot;
    }

    /// <summary>수집 명령의 출력만으로 스냅샷을 만든다(테스트에서 직접 호출한다).</summary>
    public static SystemSnapshot Parse(string raw, NetSample? previousSample)
    {
        var sections = LinuxStatParser.SplitSections(raw);

        string Section(string name) => sections.TryGetValue(name, out var value) ? value : string.Empty;

        var before = LinuxStatParser.ParseCpuLines(Section("STAT1"));
        var after = LinuxStatParser.ParseCpuLines(Section("STAT2"));

        var totalCpu = before.TryGetValue("cpu", out var beforeTotal) && after.TryGetValue("cpu", out var afterTotal)
            ? LinuxStatParser.CpuUsagePercent(beforeTotal, afterTotal)
            : 0;

        var cores = new List<CoreUsage>();
        foreach (var pair in before)
        {
            if (pair.Key.Length <= 3 || !int.TryParse(pair.Key.AsSpan(3), out var index))
            {
                continue;
            }

            if (after.TryGetValue(pair.Key, out var coreAfter))
            {
                cores.Add(new CoreUsage(index, LinuxStatParser.CpuUsagePercent(pair.Value, coreAfter)));
            }
        }

        var uptime = LinuxStatParser.ParseUptimeSeconds(Section("UPTIME"));
        var counters = LinuxStatParser.ParseNetDev(Section("NETDEV"));
        var load = LinuxStatParser.ParseLoadAvg(Section("LOADAVG"));
        var uname = LinuxStatParser.ParseUname(Section("UNAME"));

        return new SystemSnapshot
        {
            Hostname = LinuxStatParser.FirstLine(Section("HOSTNAME")),
            OsPrettyName = LinuxStatParser.ParseOsPrettyName(Section("OS")),
            Kernel = uname.Kernel,
            Architecture = uname.Architecture,
            UptimeSeconds = uptime,
            CpuModel = LinuxStatParser.ParseCpuModel(Section("CPUMODEL")),
            CpuCount = LinuxStatParser.ParseCpuCount(Section("CPUCOUNT")) is var count && count > 0 ? count : cores.Count,
            CpuUsagePercent = totalCpu,
            Cores = cores.OrderBy(c => c.Index).ToList(),
            Load1 = load.One,
            Load5 = load.Five,
            Load15 = load.Fifteen,
            Memory = LinuxStatParser.ParseMemInfo(Section("MEM")),
            Disks = LinuxStatParser.ParseDf(Section("DISK")),
            Network = BuildRates(counters, uptime, previousSample),
            TopProcesses = LinuxStatParser.ParsePs(Section("PS")).Take(10).ToList(),
            Users = LinuxStatParser.ParseWho(Section("WHO")),
            CpuTemperatureCelsius = LinuxStatParser.ParseThermal(Section("TEMP")),
            Sample = new NetSample(uptime, counters),
        };
    }

    /// <summary>이전 표본이 있으면 가동 시간 차이로 초당 전송량을 구한다(첫 수집은 0).</summary>
    private static List<NetworkRate> BuildRates(
        IReadOnlyDictionary<string, NetCounters> counters,
        double uptimeSeconds,
        NetSample? previousSample)
    {
        var elapsed = previousSample is null ? 0 : uptimeSeconds - previousSample.UptimeSeconds;
        var rates = new List<NetworkRate>();

        foreach (var pair in counters)
        {
            double rxPerSecond = 0;
            double txPerSecond = 0;

            if (elapsed > 0.2 && previousSample!.Counters.TryGetValue(pair.Key, out var previous))
            {
                // 카운터가 초기화되면 음수가 나오므로 0 으로 깎는다.
                rxPerSecond = Math.Max(0, (pair.Value.RxBytes - previous.RxBytes) / elapsed);
                txPerSecond = Math.Max(0, (pair.Value.TxBytes - previous.TxBytes) / elapsed);
            }

            rates.Add(new NetworkRate(pair.Key, pair.Value.RxBytes, pair.Value.TxBytes, rxPerSecond, txPerSecond));
        }

        return rates
            .OrderByDescending(r => r.RxBytesPerSecond + r.TxBytesPerSecond)
            .ThenByDescending(r => r.RxBytes + r.TxBytes)
            .Take(6)
            .ToList();
    }
}
