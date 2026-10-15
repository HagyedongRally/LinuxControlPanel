using System.Globalization;
using LinuxControlPanel.Core.Linux;

namespace LinuxControlPanel.Views;

public sealed record CoreRow(string Name, double Percent, string PercentText);

public sealed record DiskRow(string MountPoint, string FileSystem, double UsedPercent, string UsageText, string PercentText);

public sealed record NetRow(string Name, string DownText, string UpText, string TotalText);

public sealed record ProcRow(string Pid, string Command, string CpuText, string MemoryText);

public sealed record UserRow(string Name, string Detail);

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
}
