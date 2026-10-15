using System.Text;
using LinuxControlPanel.Core.Models;
using LinuxControlPanel.Core.Storage;

namespace LinuxControlPanel.Tests;

public sealed class AddressBookStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly AddressBookStore _store;

    public AddressBookStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new AddressBookStore(Path.Combine(_directory, "addressbook.json"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static ConnectionEntry NewEntry(string host, string user, string name = "") => new()
    {
        DisplayName = name,
        Host = host,
        Port = 22,
        Username = user,
    };

    [Fact]
    public async Task 파일이_없으면_빈_주소록을_준다()
    {
        Assert.Empty(await _store.LoadAsync());
    }

    [Fact]
    public async Task 저장하고_불러오면_같은_값이_나온다()
    {
        var entry = NewEntry("192.168.219.54", "turtlelab", "acerver");
        entry.Auth = AuthMethod.PrivateKey;
        entry.PrivateKeyPath = @"C:\keys\id_ed25519";
        entry.HostKeyFingerprint = "SHA256:abc123";
        entry.LastConnectedAt = DateTimeOffset.Now;
        entry.ConnectCount = 3;

        await _store.SaveAsync(new[] { entry });
        var loaded = Assert.Single(await _store.LoadAsync());

        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal("acerver", loaded.DisplayName);
        Assert.Equal("192.168.219.54", loaded.Host);
        Assert.Equal(22, loaded.Port);
        Assert.Equal("turtlelab", loaded.Username);
        Assert.Equal(AuthMethod.PrivateKey, loaded.Auth);
        Assert.Equal(@"C:\keys\id_ed25519", loaded.PrivateKeyPath);
        Assert.Equal("SHA256:abc123", loaded.HostKeyFingerprint);
        Assert.Equal(3, loaded.ConnectCount);
    }

    [Fact]
    public void Upsert_같은_대상이면_Id를_유지하며_덮어쓴다()
    {
        var existing = NewEntry("10.0.0.5", "root", "old");
        existing.ConnectCount = 7;
        var entries = new List<ConnectionEntry> { existing };

        var updated = NewEntry("10.0.0.5", "root", "new");
        AddressBookStore.Upsert(entries, updated);

        var single = Assert.Single(entries);
        Assert.Equal(existing.Id, single.Id);
        Assert.Equal("new", single.DisplayName);
    }

    [Fact]
    public void Upsert_대소문자가_달라도_같은_호스트로_본다()
    {
        var entries = new List<ConnectionEntry> { NewEntry("Server.Local", "kim") };

        AddressBookStore.Upsert(entries, NewEntry("server.local", "kim", "같은 대상"));

        Assert.Single(entries);
    }

    [Fact]
    public void Upsert_사용자나_포트가_다르면_새_항목이_된다()
    {
        var entries = new List<ConnectionEntry> { NewEntry("10.0.0.5", "root") };

        AddressBookStore.Upsert(entries, NewEntry("10.0.0.5", "admin"));

        var other = NewEntry("10.0.0.5", "root");
        other.Port = 2222;
        AddressBookStore.Upsert(entries, other);

        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void Remove_아이디로_지운다()
    {
        var entry = NewEntry("10.0.0.5", "root");
        var entries = new List<ConnectionEntry> { entry };

        Assert.True(AddressBookStore.Remove(entries, entry.Id));
        Assert.Empty(entries);
        Assert.False(AddressBookStore.Remove(entries, entry.Id));
    }

    [Fact]
    public async Task 최근_접속한_항목이_먼저_나온다()
    {
        var old = NewEntry("10.0.0.1", "a");
        old.LastConnectedAt = DateTimeOffset.Now.AddDays(-2);

        var recent = NewEntry("10.0.0.2", "b");
        recent.LastConnectedAt = DateTimeOffset.Now;

        var never = NewEntry("10.0.0.3", "c");

        await _store.SaveAsync(new[] { old, never, recent });
        var loaded = await _store.LoadAsync();

        Assert.Equal("10.0.0.2", loaded[0].Host);
        Assert.Equal("10.0.0.1", loaded[1].Host);
        Assert.Equal("10.0.0.3", loaded[2].Host);
    }

    [Fact]
    public async Task 비밀값은_보호된_형태로만_파일에_들어간다()
    {
        var protector = new ReverseProtector();
        const string secret = "sup3r-secret-pw";

        var entry = NewEntry("192.168.219.54", "turtlelab");
        entry.SaveSecret = true;
        entry.ProtectedSecret = protector.Protect(secret);

        await _store.SaveAsync(new[] { entry });
        var text = await File.ReadAllTextAsync(_store.FilePath);

        Assert.DoesNotContain(secret, text);
        Assert.Contains(entry.ProtectedSecret!, text);

        // 화면 표시용 계산 속성은 파일에 들어가지 않는다.
        Assert.DoesNotContain("\"Target\"", text);
        Assert.DoesNotContain("\"LastConnectedText\"", text);

        var loaded = Assert.Single(await _store.LoadAsync());
        Assert.Equal(secret, protector.Unprotect(loaded.ProtectedSecret!));
    }

    [Fact]
    public async Task 한글_표시_이름이_파일에_그대로_저장된다()
    {
        var entry = NewEntry("10.0.0.7", "root", "사무실 서버");

        await _store.SaveAsync(new[] { entry });
        var text = await File.ReadAllTextAsync(_store.FilePath);

        Assert.Contains("사무실 서버", text);
        Assert.Equal("사무실 서버", (await _store.LoadAsync()).Single().DisplayName);
    }

    [Fact]
    public async Task 깨진_파일이면_빈_주소록으로_시작하고_백업을_남긴다()
    {
        await File.WriteAllTextAsync(_store.FilePath, "{ 이건 JSON 이 아니다");

        Assert.Empty(await _store.LoadAsync());
        Assert.NotEmpty(Directory.GetFiles(_directory, "*.broken-*"));
    }

    [Fact]
    public async Task 저장은_임시파일을_남기지_않는다()
    {
        await _store.SaveAsync(new[] { NewEntry("10.0.0.9", "root") });

        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.True(File.Exists(_store.FilePath));
    }

    /// <summary>DPAPI 대신 쓰는 테스트용 보호 장치(뒤집어 Base64).</summary>
    private sealed class ReverseProtector : ISecretProtector
    {
        public string? Protect(string plainText) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(new string(plainText.Reverse().ToArray())));

        public string? Unprotect(string protectedValue) =>
            new string(Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue)).Reverse().ToArray());
    }
}
