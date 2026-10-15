using System.Globalization;

namespace LinuxControlPanel.Core.Linux;

/// <summary>화면에 바로 쓸 수 있게 숫자를 사람이 읽는 문자열로 바꾼다.</summary>
public static class Humanize
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Bytes(double bytes)
    {
        if (double.IsNaN(bytes) || double.IsInfinity(bytes) || bytes <= 0)
        {
            return "0 B";
        }

        var unit = 0;
        var value = bytes;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : (value >= 100 ? "0" : "0.0");
        return string.Create(CultureInfo.CurrentCulture, $"{value.ToString(format, CultureInfo.CurrentCulture)} {Units[unit]}");
    }

    public static string BytesPerSecond(double bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

    /// <summary>초를 "3일 4시간 12분" 처럼 바꾼다.</summary>
    public static string Duration(double seconds)
    {
        if (seconds <= 0)
        {
            return "-";
        }

        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}일 {span.Hours}시간 {span.Minutes}분";
        }

        if (span.TotalHours >= 1)
        {
            return $"{span.Hours}시간 {span.Minutes}분";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{span.Minutes}분 {span.Seconds}초";
        }

        return $"{span.Seconds}초";
    }

    public static string Percent(double value) => value.ToString("0.0", CultureInfo.CurrentCulture) + " %";
}
