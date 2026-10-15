using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using LinuxControlPanel.Core.Models;

namespace LinuxControlPanel.Core.Storage;

/// <summary>주소록 파일(JSON) 읽기/쓰기. 쓰기는 임시 파일에 먼저 쓴 뒤 교체한다.</summary>
public sealed class AddressBookStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // 표시 이름에 한글을 써도 파일에서 그대로 읽히게 한다(\uXXXX 로 깨지지 않도록).
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public AddressBookStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    /// <summary>%APPDATA%\LinuxControlPanel\addressbook.json</summary>
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LinuxControlPanel",
        "addressbook.json");

    public async Task<List<ConnectionEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new List<ConnectionEntry>();
        }

        try
        {
            await using var stream = File.OpenRead(FilePath);
            var file = await JsonSerializer.DeserializeAsync<AddressBookFile>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            var entries = file?.Entries ?? new List<ConnectionEntry>();
            return Sort(entries);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 파일이 깨졌으면 백업만 남기고 빈 주소록으로 시작한다. 앱이 못 뜨는 쪽이 더 나쁘다.
            TryBackupBrokenFile();
            return new List<ConnectionEntry>();
        }
    }

    public async Task SaveAsync(IEnumerable<ConnectionEntry> entries, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new AddressBookFile { Entries = Sort(entries.ToList()) };
        var temporaryPath = FilePath + ".tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    /// <summary>host/port/user 가 같은 기록이 있으면 갱신하고, 없으면 새로 넣는다.</summary>
    public static void Upsert(List<ConnectionEntry> entries, ConnectionEntry entry)
    {
        var index = entries.FindIndex(e => string.Equals(e.Key, entry.Key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            entry.Id = entries[index].Id;
            entries[index] = entry;
        }
        else
        {
            entries.Insert(0, entry);
        }
    }

    public static bool Remove(List<ConnectionEntry> entries, string id) =>
        entries.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;

    private static List<ConnectionEntry> Sort(List<ConnectionEntry> entries) => entries
        .OrderByDescending(e => e.LastConnectedAt ?? DateTimeOffset.MinValue)
        .ThenBy(e => e.Label, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private void TryBackupBrokenFile()
    {
        try
        {
            var backup = FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Copy(FilePath, backup, overwrite: true);
        }
        catch (IOException)
        {
            // 백업 실패는 무시한다.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class AddressBookFile
    {
        public int Version { get; set; } = 1;

        public List<ConnectionEntry> Entries { get; set; } = new();
    }
}
