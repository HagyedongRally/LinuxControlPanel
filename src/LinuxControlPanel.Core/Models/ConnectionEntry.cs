using System.Text.Json.Serialization;

namespace LinuxControlPanel.Core.Models;

/// <summary>SSH 인증 방식.</summary>
public enum AuthMethod
{
    Password = 0,
    PrivateKey = 1,
}

/// <summary>
/// 주소록에 남는 접속 기록 한 건. 비밀 값은 평문으로 보관하지 않고
/// <see cref="ProtectedSecret"/> 에 DPAPI 로 암호화한 Base64 문자열만 남긴다.
/// </summary>
public sealed class ConnectionEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public AuthMethod Auth { get; set; } = AuthMethod.Password;

    public string? PrivateKeyPath { get; set; }

    /// <summary>비밀번호(또는 개인키 암호)를 주소록에 저장할지 여부.</summary>
    public bool SaveSecret { get; set; }

    /// <summary>DPAPI 로 암호화된 비밀 값. 다른 Windows 계정에서는 복호화되지 않는다.</summary>
    public string? ProtectedSecret { get; set; }

    /// <summary>처음 접속할 때 기록한 호스트 키 지문. 값이 바뀌면 사용자에게 경고한다.</summary>
    public string? HostKeyFingerprint { get; set; }

    public DateTimeOffset? LastConnectedAt { get; set; }

    public int ConnectCount { get; set; }

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Host : DisplayName;

    [JsonIgnore]
    public string Target => $"{Username}@{Host}:{Port}";

    [JsonIgnore]
    public string AuthText => Auth == AuthMethod.PrivateKey ? "개인키" : "비밀번호";

    [JsonIgnore]
    public string LastConnectedText => LastConnectedAt is null
        ? "접속 기록 없음"
        : $"최근 접속 {LastConnectedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm} · {ConnectCount}회";

    /// <summary>host/port/user 조합이 같으면 같은 대상으로 본다.</summary>
    [JsonIgnore]
    public string Key => MakeKey(Host, Port, Username);

    public static string MakeKey(string host, int port, string username) =>
        $"{host.Trim().ToLowerInvariant()}|{port}|{username.Trim()}";

    public ConnectionEntry Clone() => (ConnectionEntry)MemberwiseClone();
}
