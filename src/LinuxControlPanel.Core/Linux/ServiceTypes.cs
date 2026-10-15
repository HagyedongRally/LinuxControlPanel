using System.Globalization;

namespace LinuxControlPanel.Core.Linux;

/// <summary>화면에서 색을 고르기 위한 서비스 상태 분류.</summary>
public enum ServiceHealth
{
    /// <summary>active + running: 프로세스가 떠 있다.</summary>
    Running,

    /// <summary>active 이지만 running 이 아니다(oneshot 이 끝난 exited 등).</summary>
    Active,

    Failed,

    /// <summary>activating / deactivating / reloading.</summary>
    Transitioning,

    Stopped,
}

/// <summary>systemctl list-units / list-unit-files 에서 읽은 서비스 한 줄.</summary>
public sealed record ServiceUnit(
    string Name,
    string LoadState,
    string ActiveState,
    string SubState,
    string UnitFileState,
    string Description)
{
    /// <summary>".service" 를 뗀 이름.</summary>
    public string ShortName => Name.EndsWith(".service", StringComparison.Ordinal) ? Name[..^".service".Length] : Name;

    public ServiceHealth Health => ActiveState switch
    {
        "active" when SubState == "running" => ServiceHealth.Running,
        "active" => ServiceHealth.Active,
        "failed" => ServiceHealth.Failed,
        "activating" or "deactivating" or "reloading" or "refreshing" => ServiceHealth.Transitioning,
        _ => ServiceHealth.Stopped,
    };
}

/// <summary>systemctl show 의 속성과 systemctl status 출력(최근 로그 포함).</summary>
public sealed record ServiceDetail(string Name, IReadOnlyDictionary<string, string> Properties, string StatusText)
{
    /// <summary>systemd 가 "값 없음" 을 나타낼 때 쓰는 UINT64_MAX.</summary>
    private const ulong NotSet = ulong.MaxValue;

    public string Get(string key) => Properties.TryGetValue(key, out var value) ? value.Trim() : string.Empty;

    public int MainPid => int.TryParse(Get("MainPID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) ? pid : 0;

    public long? MemoryBytes => ParseCounter(Get("MemoryCurrent")) is { } value ? (long)value : null;

    public long? Tasks => ParseCounter(Get("TasksCurrent")) is { } value ? (long)value : null;

    public TimeSpan? CpuTime => ParseCounter(Get("CPUUsageNSec")) is { } value
        ? TimeSpan.FromTicks((long)(value / 100))
        : null;

    /// <summary>ExecStart 의 "{ path=... ; argv[]=/usr/sbin/sshd -D ; ... }" 에서 argv[] 부분만 꺼낸다.</summary>
    public string ExecStartCommand
    {
        get
        {
            var raw = Get("ExecStart");
            const string marker = "argv[]=";
            var start = raw.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return raw;
            }

            start += marker.Length;
            var end = raw.IndexOf(" ;", start, StringComparison.Ordinal);
            return (end < 0 ? raw[start..] : raw[start..end]).Trim();
        }
    }

    /// <summary>"[not set]", 빈 값, UINT64_MAX 는 모두 값 없음으로 본다.</summary>
    private static ulong? ParseCounter(string text) =>
        ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value != NotSet
            ? value
            : null;
}
