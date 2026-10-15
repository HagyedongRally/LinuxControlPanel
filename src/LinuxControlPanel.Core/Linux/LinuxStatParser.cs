using System.Globalization;
using System.Text;

namespace LinuxControlPanel.Core.Linux;

/// <summary>
/// /proc, df, ps, who 출력을 구조체로 바꾸는 순수 함수 모음.
/// UI 없이 단위 테스트할 수 있도록 상태를 갖지 않는다.
/// </summary>
public static class LinuxStatParser
{
    public const string SectionMarker = "#!";

    private static readonly char[] Whitespace = { ' ', '\t' };

    private static readonly string[] PseudoFileSystems =
    {
        "tmpfs", "devtmpfs", "overlay", "squashfs", "udev", "none", "shm", "cgroup", "efivarfs",
    };

    private static readonly string[] IgnoredMountPrefixes =
    {
        "/snap", "/sys", "/proc", "/dev", "/run",
    };

    /// <summary>"#!NAME" 표시로 구분된 한 번의 수집 결과를 구역별로 쪼갠다.</summary>
    public static Dictionary<string, string> SplitSections(string raw)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw))
        {
            return sections;
        }

        string? current = null;
        var buffer = new StringBuilder();

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith(SectionMarker, StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    sections[current] = buffer.ToString();
                }

                current = line.Substring(SectionMarker.Length).Trim();
                buffer.Clear();
                continue;
            }

            if (current is null)
            {
                continue;
            }

            buffer.Append(line).Append('\n');
        }

        if (current is not null)
        {
            sections[current] = buffer.ToString();
        }

        return sections;
    }

    public static CpuTimes? ParseCpuLine(string line)
    {
        var fields = Fields(line);
        if (fields.Length < 5 || !fields[0].StartsWith("cpu", StringComparison.Ordinal))
        {
            return null;
        }

        long total = 0;
        long idle = 0;
        for (var i = 1; i < fields.Length; i++)
        {
            if (!long.TryParse(fields[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            total += value;

            // /proc/stat 순서: user nice system idle iowait irq softirq steal ...
            if (i == 4 || i == 5)
            {
                idle += value;
            }
        }

        return new CpuTimes(fields[0], total, idle);
    }

    public static Dictionary<string, CpuTimes> ParseCpuLines(string block)
    {
        var result = new Dictionary<string, CpuTimes>(StringComparer.Ordinal);
        foreach (var line in Lines(block))
        {
            if (ParseCpuLine(line) is { } value)
            {
                result[value.Name] = value;
            }
        }

        return result;
    }

    /// <summary>두 표본 사이의 CPU 사용률(%). 표본이 뒤집혀 있으면 0 을 돌려준다.</summary>
    public static double CpuUsagePercent(CpuTimes before, CpuTimes after)
    {
        var totalDelta = after.Total - before.Total;
        var idleDelta = after.Idle - before.Idle;
        if (totalDelta <= 0)
        {
            return 0;
        }

        return Math.Clamp((totalDelta - idleDelta) * 100.0 / totalDelta, 0, 100);
    }

    public static MemoryUsage ParseMemInfo(string block)
    {
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(block))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line.Substring(0, colon).Trim();
            var rest = Fields(line.Substring(colon + 1));
            if (rest.Length == 0 || !long.TryParse(rest[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            {
                continue;
            }

            values[key] = rest.Length > 1 && rest[1].Equals("kB", StringComparison.OrdinalIgnoreCase)
                ? amount * 1024
                : amount;
        }

        long Get(string key) => values.TryGetValue(key, out var value) ? value : 0;

        var total = Get("MemTotal");

        // MemAvailable 이 없는 오래된 커널만 Free+Buffers+Cached 로 추정한다.
        var available = values.ContainsKey("MemAvailable")
            ? Get("MemAvailable")
            : Get("MemFree") + Get("Buffers") + Get("Cached");

        available = Math.Clamp(available, 0, total);
        var swapTotal = Get("SwapTotal");
        var swapUsed = Math.Clamp(swapTotal - Get("SwapFree"), 0, swapTotal);

        return new MemoryUsage(total, available, Math.Max(0, total - available), swapTotal, swapUsed);
    }

    /// <summary>df -PB1 출력. 가상 파일 시스템과 /snap 류 마운트는 버린다.</summary>
    public static List<DiskUsage> ParseDf(string block)
    {
        var disks = new List<DiskUsage>();
        foreach (var line in Lines(block))
        {
            var fields = Fields(line);
            if (fields.Length < 6)
            {
                continue;
            }

            if (!long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ||
                !long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var used) ||
                !long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var available))
            {
                continue; // 머리글 줄
            }

            var fileSystem = fields[0];
            var mountPoint = string.Join(" ", fields.Skip(5));

            if (total <= 0 ||
                PseudoFileSystems.Any(x => fileSystem.StartsWith(x, StringComparison.OrdinalIgnoreCase)) ||
                IgnoredMountPrefixes.Any(x => mountPoint.StartsWith(x, StringComparison.Ordinal)))
            {
                continue;
            }

            disks.Add(new DiskUsage(fileSystem, mountPoint, total, used, available));
        }

        return disks
            .OrderByDescending(d => d.MountPoint == "/")
            .ThenByDescending(d => d.TotalBytes)
            .ToList();
    }

    public static Dictionary<string, NetCounters> ParseNetDev(string block)
    {
        var result = new Dictionary<string, NetCounters>(StringComparer.Ordinal);
        foreach (var line in Lines(block))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = line.Substring(0, colon).Trim();
            var fields = Fields(line.Substring(colon + 1));
            if (name.Length == 0 || fields.Length < 10)
            {
                continue;
            }

            if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rx) ||
                !long.TryParse(fields[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tx))
            {
                continue;
            }

            result[name] = new NetCounters(rx, tx);
        }

        return result;
    }

    public static List<ProcessUsage> ParsePs(string block)
    {
        var processes = new List<ProcessUsage>();
        foreach (var line in Lines(block))
        {
            var fields = Fields(line);
            if (fields.Length < 4 ||
                !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
            {
                continue; // 머리글 줄
            }

            processes.Add(new ProcessUsage(pid, ParseDouble(fields[1]), ParseDouble(fields[2]), string.Join(" ", fields.Skip(3))));
        }

        return processes;
    }

    public static List<LoggedInUser> ParseWho(string block)
    {
        var users = new List<LoggedInUser>();
        foreach (var line in Lines(block))
        {
            var fields = Fields(line);
            if (fields.Length < 2)
            {
                continue;
            }

            var loginTime = fields.Length >= 4 ? fields[2] + " " + fields[3] : string.Empty;
            var from = string.Empty;
            var open = line.IndexOf('(');
            var close = line.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                from = line.Substring(open + 1, close - open - 1);
            }

            users.Add(new LoggedInUser(fields[0], fields[1], loginTime, from));
        }

        return users;
    }

    public static double ParseUptimeSeconds(string block)
    {
        foreach (var line in Lines(block))
        {
            var fields = Fields(line);
            if (fields.Length >= 1)
            {
                return ParseDouble(fields[0]);
            }
        }

        return 0;
    }

    public static (double One, double Five, double Fifteen) ParseLoadAvg(string block)
    {
        foreach (var line in Lines(block))
        {
            var fields = Fields(line);
            if (fields.Length >= 3)
            {
                return (ParseDouble(fields[0]), ParseDouble(fields[1]), ParseDouble(fields[2]));
            }
        }

        return (0, 0, 0);
    }

    /// <summary>/etc/os-release 의 PRETTY_NAME. 없으면 NAME + VERSION 으로 조립한다.</summary>
    public static string ParseOsPrettyName(string block)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(block))
        {
            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim().Trim('"');
        }

        if (values.TryGetValue("PRETTY_NAME", out var pretty) && pretty.Length > 0)
        {
            return pretty;
        }

        values.TryGetValue("NAME", out var name);
        values.TryGetValue("VERSION", out var version);
        return string.Join(" ", new[] { name, version }.Where(v => !string.IsNullOrWhiteSpace(v))).Trim();
    }

    /// <summary>"model name : Intel(R) ..." 에서 모델명만 꺼낸다.</summary>
    public static string ParseCpuModel(string block)
    {
        foreach (var line in Lines(block))
        {
            var colon = line.IndexOf(':');
            if (colon >= 0 && colon + 1 < line.Length)
            {
                return line.Substring(colon + 1).Trim();
            }
        }

        return string.Empty;
    }

    public static int ParseCpuCount(string block)
    {
        foreach (var line in Lines(block))
        {
            if (int.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
            {
                return count;
            }
        }

        return 0;
    }

    /// <summary>thermal_zone 온도(1/1000 도) 중 가장 높은 값. 읽을 수 없으면 null.</summary>
    public static double? ParseThermal(string block)
    {
        double? hottest = null;
        foreach (var line in Lines(block))
        {
            if (!long.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var milli))
            {
                continue;
            }

            var celsius = milli / 1000.0;
            if (celsius is <= 0 or > 150)
            {
                continue;
            }

            if (hottest is null || celsius > hottest)
            {
                hottest = celsius;
            }
        }

        return hottest;
    }

    /// <summary>uname -sr 과 uname -m 두 줄.</summary>
    public static (string Kernel, string Architecture) ParseUname(string block)
    {
        var lines = Lines(block).ToList();
        return (lines.Count > 0 ? lines[0].Trim() : string.Empty, lines.Count > 1 ? lines[1].Trim() : string.Empty);
    }

    public static string FirstLine(string block) => Lines(block).FirstOrDefault()?.Trim() ?? string.Empty;

    private static double ParseDouble(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string[] Fields(string line) => line.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<string> Lines(string block) => (block ?? string.Empty)
        .Split('\n')
        .Select(line => line.TrimEnd('\r'))
        .Where(line => line.Trim().Length > 0);
}
