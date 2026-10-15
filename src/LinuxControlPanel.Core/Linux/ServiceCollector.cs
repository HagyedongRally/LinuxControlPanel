using System.Text.RegularExpressions;
using LinuxControlPanel.Core.Ssh;

namespace LinuxControlPanel.Core.Linux;

/// <summary>
/// systemd 서비스 목록과 한 서비스의 상세 정보를 읽는다. 목록은 SSH 한 번으로
/// list-units(현재 올라온 유닛)와 list-unit-files(자동 시작 설정)를 함께 받아 합친다.
/// </summary>
public static partial class ServiceCollector
{
    public const string ListCommand = """
        export LC_ALL=C; command -v systemctl >/dev/null 2>&1 || echo '#!NOSYSTEMD'; echo '#!UNITS'; systemctl list-units --type=service --all --no-pager --no-legend --plain 2>/dev/null; echo '#!FILES'; systemctl list-unit-files --type=service --no-pager --no-legend 2>/dev/null; echo '#!END'
        """;

    /// <summary>상세 화면에서 보여 줄 systemctl show 속성.</summary>
    public static readonly string[] DetailProperties =
    {
        "Id", "Description", "LoadState", "ActiveState", "SubState", "UnitFileState", "Type",
        "MainPID", "ActiveEnterTimestamp", "MemoryCurrent", "TasksCurrent", "CPUUsageNSec",
        "User", "Restart", "NRestarts", "FragmentPath", "ExecStart",
    };

    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(20);

    private static readonly char[] Whitespace = { ' ', '\t' };

    public static async Task<List<ServiceUnit>> ListAsync(SshSession session, CancellationToken cancellationToken = default)
    {
        var result = await session.RunAsync(ListCommand, ListTimeout, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidOperationException($"서비스 목록 명령이 아무 것도 돌려주지 않았습니다 (종료 코드 {result.ExitCode}).");
        }

        return ParseList(result.StandardOutput);
    }

    public static async Task<ServiceDetail> DetailAsync(SshSession session, string unitName, CancellationToken cancellationToken = default)
    {
        var result = await session.RunAsync(DetailCommand(unitName), ListTimeout, cancellationToken).ConfigureAwait(false);
        return ParseDetail(unitName, result.StandardOutput);
    }

    /// <summary>
    /// 유닛 이름은 원격 셸에 그대로 들어가므로 systemd 가 허용하는 글자만 받고 작은따옴표로 감싼다.
    /// (systemd 이름의 "\x2d" 같은 이스케이프 때문에 역슬래시는 허용한다.)
    /// </summary>
    public static string DetailCommand(string unitName)
    {
        if (!IsValidUnitName(unitName))
        {
            throw new ArgumentException($"서비스 이름으로 쓸 수 없는 글자가 있습니다: {unitName}", nameof(unitName));
        }

        var quoted = "'" + unitName + "'";
        return "export LC_ALL=C; echo '#!SHOW'; systemctl show " + quoted + " --no-pager -p " + string.Join(",", DetailProperties) +
               " 2>&1; echo '#!STATUS'; systemctl status " + quoted + " --no-pager -l -n 30 2>&1; echo '#!END'";
    }

    public static bool IsValidUnitName(string unitName) =>
        !string.IsNullOrEmpty(unitName) && unitName.Length <= 256 && UnitNamePattern().IsMatch(unitName);

    /// <summary>목록 명령의 출력만으로 서비스 목록을 만든다(테스트에서 직접 호출한다).</summary>
    public static List<ServiceUnit> ParseList(string raw)
    {
        var sections = LinuxStatParser.SplitSections(raw);
        if (sections.ContainsKey("NOSYSTEMD"))
        {
            throw new InvalidOperationException("이 PC 에는 systemd(systemctl) 가 없어 서비스 목록을 읽을 수 없습니다.");
        }

        var units = ParseUnits(sections.TryGetValue("UNITS", out var unitBlock) ? unitBlock : string.Empty);
        var files = ParseUnitFiles(sections.TryGetValue("FILES", out var fileBlock) ? fileBlock : string.Empty);
        return Merge(units, files);
    }

    /// <summary>"UNIT LOAD ACTIVE SUB DESCRIPTION..." 줄들. 찾을 수 없는 유닛 앞에 붙는 "●" 는 뗀다.</summary>
    public static List<ServiceUnit> ParseUnits(string block)
    {
        var units = new List<ServiceUnit>();

        foreach (var rawLine in block.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('●', '*').Trim();
            var fields = line.Split(Whitespace, 5, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || !fields[0].EndsWith(".service", StringComparison.Ordinal))
            {
                continue;
            }

            var description = fields.Length > 4 ? fields[4].Trim() : string.Empty;
            units.Add(new ServiceUnit(fields[0], fields[1], fields[2], fields[3], string.Empty, description));
        }

        return units;
    }

    /// <summary>"UNIT FILE STATE [VENDOR PRESET]" 줄들을 이름 -> 상태로 만든다.</summary>
    public static Dictionary<string, string> ParseUnitFiles(string block)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in block.Split('\n'))
        {
            var fields = rawLine.Trim().Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 && fields[0].EndsWith(".service", StringComparison.Ordinal))
            {
                files[fields[0]] = fields[1];
            }
        }

        return files;
    }

    /// <summary>
    /// 올라온 유닛에 자동 시작 설정을 붙이고, 파일만 있고 올라오지 않은 서비스(대개 disabled)는 중지 상태로 더한다.
    /// "getty@.service" 같은 템플릿 자체는 실행 대상이 아니므로 빼고, 인스턴스는 템플릿의 설정을 따른다.
    /// </summary>
    public static List<ServiceUnit> Merge(IReadOnlyList<ServiceUnit> units, IReadOnlyDictionary<string, string> files)
    {
        var merged = new Dictionary<string, ServiceUnit>(StringComparer.Ordinal);

        foreach (var unit in units)
        {
            merged[unit.Name] = unit with { UnitFileState = LookupFileState(unit.Name, files) };
        }

        foreach (var pair in files)
        {
            if (IsTemplate(pair.Key) || merged.ContainsKey(pair.Key))
            {
                continue;
            }

            merged[pair.Key] = new ServiceUnit(pair.Key, "not-loaded", "inactive", "dead", pair.Value, string.Empty);
        }

        return merged.Values
            .OrderBy(unit => unit.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static ServiceDetail ParseDetail(string unitName, string raw)
    {
        var sections = LinuxStatParser.SplitSections(raw ?? string.Empty);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        if (sections.TryGetValue("SHOW", out var show))
        {
            foreach (var rawLine in show.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var equals = line.IndexOf('=');
                if (equals > 0)
                {
                    properties[line[..equals]] = line[(equals + 1)..];
                }
            }
        }

        var status = sections.TryGetValue("STATUS", out var statusBlock) ? statusBlock.TrimEnd() : string.Empty;
        return new ServiceDetail(unitName, properties, status);
    }

    private static string LookupFileState(string name, IReadOnlyDictionary<string, string> files)
    {
        if (files.TryGetValue(name, out var state))
        {
            return state;
        }

        // "getty@tty1.service" -> "getty@.service"
        var at = name.IndexOf('@');
        if (at > 0 && files.TryGetValue(name[..(at + 1)] + ".service", out var templateState))
        {
            return templateState;
        }

        return string.Empty;
    }

    private static bool IsTemplate(string name) => name.EndsWith("@.service", StringComparison.Ordinal);

    // 첫 글자가 "-" 이면 systemctl 이 옵션으로 읽으므로 막는다.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9:_.@\\-]*$")]
    private static partial Regex UnitNamePattern();
}
