namespace LinuxControlPanel.Core.Linux;

/// <summary>/proc/stat 한 줄에서 뽑은 누적 CPU 시간.</summary>
public readonly record struct CpuTimes(string Name, long Total, long Idle);

/// <summary>/proc/net/dev 의 누적 바이트.</summary>
public readonly record struct NetCounters(long RxBytes, long TxBytes);

public sealed record MemoryUsage(
    long TotalBytes,
    long AvailableBytes,
    long UsedBytes,
    long SwapTotalBytes,
    long SwapUsedBytes)
{
    public static readonly MemoryUsage Empty = new(0, 0, 0, 0, 0);

    public double UsedPercent => TotalBytes <= 0 ? 0 : Math.Clamp(UsedBytes * 100.0 / TotalBytes, 0, 100);

    public double SwapUsedPercent => SwapTotalBytes <= 0 ? 0 : Math.Clamp(SwapUsedBytes * 100.0 / SwapTotalBytes, 0, 100);
}

public sealed record DiskUsage(
    string FileSystem,
    string MountPoint,
    long TotalBytes,
    long UsedBytes,
    long AvailableBytes)
{
    public double UsedPercent => TotalBytes <= 0 ? 0 : Math.Clamp(UsedBytes * 100.0 / TotalBytes, 0, 100);
}

public sealed record ProcessUsage(int Pid, double CpuPercent, double MemoryPercent, string Command);

public sealed record LoggedInUser(string Name, string Terminal, string LoginTime, string From);

public sealed record CoreUsage(int Index, double Percent)
{
    public string Name => $"CPU{Index}";
}

/// <summary>인터페이스별 누적량과 (이전 표본이 있을 때) 초당 전송량.</summary>
public sealed record NetworkRate(
    string Name,
    long RxBytes,
    long TxBytes,
    double RxBytesPerSecond,
    double TxBytesPerSecond);

/// <summary>초당 전송량 계산을 위해 다음 수집까지 들고 있는 표본.</summary>
public sealed record NetSample(double UptimeSeconds, IReadOnlyDictionary<string, NetCounters> Counters)
{
    public static readonly NetSample Empty = new(0, new Dictionary<string, NetCounters>());
}
