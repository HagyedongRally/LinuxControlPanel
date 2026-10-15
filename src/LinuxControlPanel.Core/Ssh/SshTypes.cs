using LinuxControlPanel.Core.Models;

namespace LinuxControlPanel.Core.Ssh;

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>접속할 때 사용자에게 확인을 받아야 하는 호스트 키 정보.</summary>
public sealed class HostKeyChallenge
{
    public required string Host { get; init; }

    public required string KeyAlgorithm { get; init; }

    /// <summary>"SHA256:..." 형식.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>주소록에 저장된 지문(처음 접속이면 null).</summary>
    public string? KnownFingerprint { get; init; }

    /// <summary>저장된 지문과 다르면 가로채기 가능성이 있으므로 강하게 경고해야 한다.</summary>
    public bool IsChanged => KnownFingerprint is not null &&
        !string.Equals(KnownFingerprint, Fingerprint, StringComparison.Ordinal);
}

/// <summary>사용자가 호스트 키를 믿지 않겠다고 했을 때.</summary>
public sealed class SshHostKeyRejectedException : Exception
{
    public SshHostKeyRejectedException(HostKeyChallenge challenge)
        : base($"호스트 키를 신뢰하지 않아 접속을 취소했습니다 ({challenge.Fingerprint}).")
    {
        Challenge = challenge;
    }

    public HostKeyChallenge Challenge { get; }
}

public sealed class SshConnectRequest
{
    public required string Host { get; init; }

    public int Port { get; init; } = 22;

    public required string Username { get; init; }

    public AuthMethod Auth { get; init; } = AuthMethod.Password;

    /// <summary>비밀번호 또는 개인키 암호.</summary>
    public string? Secret { get; init; }

    public string? PrivateKeyPath { get; init; }

    public string? KnownHostKeyFingerprint { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>호스트 키를 믿을지 묻는 콜백. UI 스레드로 넘기는 일은 호출자가 맡는다.</summary>
    public Func<HostKeyChallenge, bool>? HostKeyApprover { get; init; }
}
