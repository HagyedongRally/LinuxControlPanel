namespace LinuxControlPanel.Core.Linux;

/// <summary>한 번의 수집으로 본 리눅스 PC 전반의 상태.</summary>
public sealed class SystemSnapshot
{
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.Now;

    public string Hostname { get; init; } = string.Empty;

    public string OsPrettyName { get; init; } = string.Empty;

    public string Kernel { get; init; } = string.Empty;

    public string Architecture { get; init; } = string.Empty;

    public double UptimeSeconds { get; init; }

    public string CpuModel { get; init; } = string.Empty;

    public int CpuCount { get; init; }

    public double CpuUsagePercent { get; init; }

    public IReadOnlyList<CoreUsage> Cores { get; init; } = Array.Empty<CoreUsage>();

    public double Load1 { get; init; }

    public double Load5 { get; init; }

    public double Load15 { get; init; }

    public MemoryUsage Memory { get; init; } = MemoryUsage.Empty;

    public IReadOnlyList<DiskUsage> Disks { get; init; } = Array.Empty<DiskUsage>();

    public IReadOnlyList<NetworkRate> Network { get; init; } = Array.Empty<NetworkRate>();

    public IReadOnlyList<ProcessUsage> TopProcesses { get; init; } = Array.Empty<ProcessUsage>();

    public IReadOnlyList<LoggedInUser> Users { get; init; } = Array.Empty<LoggedInUser>();

    public double? CpuTemperatureCelsius { get; init; }

    /// <summary>다음 수집에서 초당 전송량을 계산할 때 쓰는 표본.</summary>
    public NetSample Sample { get; init; } = NetSample.Empty;

    public string UptimeText => Humanize.Duration(UptimeSeconds);

    public string LoadText => $"{Load1:0.00} / {Load5:0.00} / {Load15:0.00}";
}
