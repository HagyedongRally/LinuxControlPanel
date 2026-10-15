using System.Globalization;
using System.Windows.Media;
using LinuxControlPanel.Core.Linux;

namespace LinuxControlPanel.Views;

public sealed record CoreRow(string Name, double Percent, string PercentText);

public sealed record DiskRow(string MountPoint, string FileSystem, double UsedPercent, string UsageText, string PercentText);

public sealed record NetRow(string Name, string DownText, string UpText, string TotalText);

public sealed record ProcRow(string Pid, string Command, string CpuText, string MemoryText);

public sealed record UserRow(string Name, string Detail);

public sealed record ServiceRow(string Name, string StateText, string EnabledText, string Description, Brush StateBrush, ServiceUnit Unit);

public sealed record PropertyRow(string Label, string Value);

/// <summary>스냅샷을 XAML 템플릿이 그대로 보여 줄 수 있는 문자열로 바꾼다.</summary>
public static class Rows
{
    public static List<CoreRow> Cores(SystemSnapshot snapshot) => snapshot.Cores
        .Select(core => new CoreRow(core.Name, core.Percent, Humanize.Percent(core.Percent)))
        .ToList();

    public static List<DiskRow> Disks(SystemSnapshot snapshot) => snapshot.Disks
        .Select(disk => new DiskRow(
            disk.MountPoint,
            disk.FileSystem,
            disk.UsedPercent,
            $"{Humanize.Bytes(disk.UsedBytes)} / {Humanize.Bytes(disk.TotalBytes)} · 남은 공간 {Humanize.Bytes(disk.AvailableBytes)}",
            Humanize.Percent(disk.UsedPercent)))
        .ToList();

    public static List<NetRow> Network(SystemSnapshot snapshot) => snapshot.Network
        .Select(rate => new NetRow(
            rate.Name,
            "받기 " + Humanize.BytesPerSecond(rate.RxBytesPerSecond),
            "보내기 " + Humanize.BytesPerSecond(rate.TxBytesPerSecond),
            $"누적 {Humanize.Bytes(rate.RxBytes)} / {Humanize.Bytes(rate.TxBytes)}"))
        .ToList();

    public static List<ProcRow> Processes(SystemSnapshot snapshot) => snapshot.TopProcesses
        .Select(process => new ProcRow(
            process.Pid.ToString(CultureInfo.CurrentCulture),
            process.Command,
            Humanize.Percent(process.CpuPercent),
            Humanize.Percent(process.MemoryPercent)))
        .ToList();

    public static List<UserRow> Users(SystemSnapshot snapshot) => snapshot.Users
        .Select(user => new UserRow(
            user.Name,
            string.Join(" · ", new[] { user.Terminal, user.LoginTime, user.From }.Where(part => !string.IsNullOrWhiteSpace(part)))))
        .ToList();

    public static List<ServiceRow> Services(IEnumerable<ServiceUnit> services, Func<ServiceHealth, Brush> brushFor) => services
        .Select(unit => new ServiceRow(
            unit.ShortName,
            $"{unit.ActiveState} ({unit.SubState})",
            string.IsNullOrEmpty(unit.UnitFileState) ? "-" : unit.UnitFileState,
            unit.Description,
            brushFor(unit.Health),
            unit))
        .ToList();

    /// <summary>상세 화면의 "항목 : 값" 줄. 값이 없는 항목은 빼서 짧게 보여 준다.</summary>
    public static List<PropertyRow> ServiceProperties(ServiceDetail detail)
    {
        string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

        var restarts = detail.Get("NRestarts");
        var restartPolicy = detail.Get("Restart");
        var restartText = string.IsNullOrEmpty(restarts) ? restartPolicy : $"{restartPolicy} · {restarts}회 재시작";

        var rows = new List<PropertyRow>
        {
            new("상태", $"{detail.Get("ActiveState")} ({detail.Get("SubState")})"),
            new("자동 시작", detail.Get("UnitFileState")),
            new("형식", detail.Get("Type")),
            new("주 PID", detail.MainPid > 0 ? detail.MainPid.ToString(CultureInfo.CurrentCulture) : string.Empty),
            new("시작 시각", detail.Get("ActiveEnterTimestamp")),
            new("메모리", detail.MemoryBytes is { } memory ? Humanize.Bytes(memory) : string.Empty),
            new("CPU 시간", detail.CpuTime is { } cpu ? CpuTimeText(cpu) : string.Empty),
            new("작업 수", detail.Tasks?.ToString(CultureInfo.CurrentCulture) ?? string.Empty),
            new("실행 사용자", detail.Properties.ContainsKey("User") ? Or(detail.Get("User"), "root") : string.Empty),
            new("재시작", restartText),
            new("유닛 파일", detail.Get("FragmentPath")),
            new("실행 명령", detail.ExecStartCommand),
        };

        return rows.Where(row => !string.IsNullOrWhiteSpace(row.Value) && row.Value != "()").ToList();
    }

    private static string CpuTimeText(TimeSpan cpu) => cpu.TotalSeconds < 60
        ? cpu.TotalSeconds.ToString("0.00", CultureInfo.CurrentCulture) + "초"
        : Humanize.Duration(cpu.TotalSeconds);
}
