using System.Security.Cryptography;
using LinuxControlPanel.Core.Models;
using Renci.SshNet;

namespace LinuxControlPanel.Core.Ssh;

/// <summary>
/// SSH.NET 의 SshClient 를 감싼 세션. 명령 실행은 한 번에 하나만 보내도록 잠금을 둔다
/// (대시보드 자동 갱신과 사용자가 입력한 명령이 겹칠 수 있다).
/// </summary>
public sealed class SshSession : IDisposable
{
    private readonly SshClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private SshSession(SshClient client, SshConnectRequest request, string fingerprint)
    {
        _client = client;
        Host = request.Host;
        Port = request.Port;
        Username = request.Username;
        HostKeyFingerprint = fingerprint;
    }

    public string Host { get; }

    public int Port { get; }

    public string Username { get; }

    /// <summary>접속할 때 서버가 보여 준 호스트 키 지문("SHA256:...").</summary>
    public string HostKeyFingerprint { get; }

    public bool IsConnected => !_disposed && _client.IsConnected;

    public string Target => $"{Username}@{Host}:{Port}";

    public static async Task<SshSession> ConnectAsync(SshConnectRequest request, CancellationToken cancellationToken = default)
    {
        var client = new SshClient(BuildConnectionInfo(request));
        client.KeepAliveInterval = TimeSpan.FromSeconds(30);

        var fingerprint = string.Empty;
        SshHostKeyRejectedException? rejection = null;

        // 이 이벤트는 SSH 작업 스레드에서 온다. UI 를 띄워야 하면 호출자가 Dispatcher 로 넘긴다.
        client.HostKeyReceived += (_, e) =>
        {
            fingerprint = FormatFingerprint(e.HostKey);
            var challenge = new HostKeyChallenge
            {
                Host = request.Host,
                KeyAlgorithm = e.HostKeyName ?? string.Empty,
                Fingerprint = fingerprint,
                KnownFingerprint = request.KnownHostKeyFingerprint,
            };

            // 저장된 지문과 같으면 묻지 않는다.
            if (request.KnownHostKeyFingerprint is not null && !challenge.IsChanged)
            {
                e.CanTrust = true;
                return;
            }

            var approver = request.HostKeyApprover;
            if (approver is null)
            {
                e.CanTrust = true;
                return;
            }

            var trusted = approver(challenge);
            e.CanTrust = trusted;
            if (!trusted)
            {
                rejection = new SshHostKeyRejectedException(challenge);
            }
        };

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();

            // 사용자가 호스트 키를 거절한 경우는 인증 오류보다 이 사실을 알려 주는 쪽이 정확하다.
            if (rejection is not null)
            {
                throw rejection;
            }

            throw;
        }

        if (rejection is not null)
        {
            client.Dispose();
            throw rejection;
        }

        return new SshSession(client, request, fingerprint);
    }

    public async Task<CommandResult> RunAsync(string command, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var sshCommand = _client.CreateCommand(command);
            sshCommand.CommandTimeout = timeout;
            // SSH.NET 2026.0.0 의 ExecuteAsync 는 Task 만 돌려주고 표준 출력은 Result 에 담긴다.
            await sshCommand.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            var exitCode = sshCommand.ExitStatus is int code ? code : -1;
            return new CommandResult(exitCode, sshCommand.Result ?? string.Empty, sshCommand.Error ?? string.Empty);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ConnectionInfo BuildConnectionInfo(SshConnectRequest request)
    {
        var methods = new List<AuthenticationMethod>();

        if (request.Auth == AuthMethod.PrivateKey)
        {
            if (string.IsNullOrWhiteSpace(request.PrivateKeyPath))
            {
                throw new ArgumentException("개인키 파일 경로가 비어 있습니다.", nameof(request));
            }

            var keyFile = string.IsNullOrEmpty(request.Secret)
                ? new PrivateKeyFile(request.PrivateKeyPath)
                : new PrivateKeyFile(request.PrivateKeyPath, request.Secret);
            methods.Add(new PrivateKeyAuthenticationMethod(request.Username, keyFile));
        }
        else
        {
            var password = request.Secret ?? string.Empty;
            methods.Add(new PasswordAuthenticationMethod(request.Username, password));

            // sshd 가 비밀번호를 keyboard-interactive 로만 받는 설정도 흔하다.
            var interactive = new KeyboardInteractiveAuthenticationMethod(request.Username);
            interactive.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    if (prompt.Request.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                        prompt.Request.Contains("암호", StringComparison.Ordinal))
                    {
                        prompt.Response = password;
                    }
                }
            };
            methods.Add(interactive);
        }

        return new ConnectionInfo(request.Host, request.Port, request.Username, methods.ToArray())
        {
            Timeout = request.Timeout,
        };
    }

    private static string FormatFingerprint(byte[] hostKey) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(hostKey)).TrimEnd('=');

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
        }
        catch (Exception)
        {
            // 끊는 중의 오류는 알릴 것이 없다.
        }

        _client.Dispose();
        _gate.Dispose();
    }
}
