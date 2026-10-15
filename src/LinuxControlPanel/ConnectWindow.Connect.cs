using System.Globalization;
using System.Windows;
using LinuxControlPanel.Core.Models;
using LinuxControlPanel.Core.Ssh;
using LinuxControlPanel.Core.Storage;

namespace LinuxControlPanel;

/// <summary>ConnectWindow 의 접속 처리 부분.</summary>
public partial class ConnectWindow
{
    private void ConnectButton_Click(object sender, RoutedEventArgs e) => _ = ConnectAsync();

    private void CancelConnectButton_Click(object sender, RoutedEventArgs e)
    {
        _connectCts?.Cancel();
        SetStatus("접속을 취소하고 있습니다...", isError: false);
    }

    private async Task ConnectAsync()
    {
        if (!TryBuildEntryFromForm(out var entry, out var secret, out var error))
        {
            SetStatus(error, isError: true);
            return;
        }

        // 같은 대상이 이미 주소록에 있으면 접속 횟수와 저장된 호스트 키 지문을 이어 쓴다.
        var existing = _entries.FirstOrDefault(x => string.Equals(x.Key, entry.Key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            entry.Id = existing.Id;
            entry.ConnectCount = existing.ConnectCount;
        }

        _connectCts = new CancellationTokenSource();
        SetBusy(true);
        SetStatus($"{entry.Target} 에 접속하는 중...", isError: false);

        SshSession? session = null;
        try
        {
            var request = new SshConnectRequest
            {
                Host = entry.Host,
                Port = entry.Port,
                Username = entry.Username,
                Auth = entry.Auth,
                Secret = secret,
                PrivateKeyPath = entry.PrivateKeyPath,
                KnownHostKeyFingerprint = existing?.HostKeyFingerprint,
                HostKeyApprover = ConfirmHostKey,
            };

            session = await SshSession.ConnectAsync(request, _connectCts.Token);

            entry.HostKeyFingerprint = session.HostKeyFingerprint;
            entry.LastConnectedAt = DateTimeOffset.Now;
            entry.ConnectCount += 1;
            entry.ProtectedSecret = entry.SaveSecret && secret.Length > 0
                ? _protector.Protect(secret)
                : null;

            var list = _entries.ToList();
            AddressBookStore.Upsert(list, entry);

            try
            {
                await _store.SaveAsync(list);
            }
            catch (Exception ex)
            {
                // 주소록 저장 실패가 접속을 막을 이유는 없다. 알리고 계속 간다.
                MessageBox.Show(
                    this,
                    "접속은 됐지만 주소록을 저장하지 못했습니다: " + ex.Message,
                    "주소록 저장 실패",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            ReplaceEntries(list);

            // 세션 소유권을 대시보드로 넘긴다(여기서 Dispose 하지 않는다).
            var dashboard = new DashboardWindow(session, entry);
            _sessionHandedOver = true;
            session = null;

            dashboard.Show();
            Close();
        }
        catch (Exception ex)
        {
            SetStatus(SshErrorText.Describe(ex), isError: true);
        }
        finally
        {
            session?.Dispose();
            _connectCts?.Dispose();
            _connectCts = null;

            if (!_sessionHandedOver)
            {
                SetBusy(false);
            }
        }
    }

    /// <summary>호스트 키 확인은 SSH 작업 스레드에서 올라오므로 UI 스레드로 넘겨 묻는다.</summary>
    private bool ConfirmHostKey(HostKeyChallenge challenge) => Dispatcher.Invoke(() =>
    {
        var newLine = Environment.NewLine;
        var message = challenge.IsChanged
            ? $"경고: {challenge.Host} 의 호스트 키가 지난 번과 다릅니다.{newLine}{newLine}" +
              $"저장된 지문: {challenge.KnownFingerprint}{newLine}" +
              $"현재 지문: {challenge.Fingerprint}{newLine}{newLine}" +
              "서버를 다시 설치했거나, 중간에서 가로채는 중일 수 있습니다. 그래도 접속하시겠습니까?"
            : $"{challenge.Host} 에 처음 접속합니다.{newLine}{newLine}" +
              $"호스트 키 지문 ({challenge.KeyAlgorithm}):{newLine}{challenge.Fingerprint}{newLine}{newLine}" +
              "이 키를 신뢰하고 접속하시겠습니까?";

        var result = MessageBox.Show(
            this,
            message,
            challenge.IsChanged ? "호스트 키가 바뀌었습니다" : "호스트 키 확인",
            MessageBoxButton.YesNo,
            challenge.IsChanged ? MessageBoxImage.Warning : MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    });

    private bool TryBuildEntryFromForm(out ConnectionEntry entry, out string secret, out string error)
    {
        entry = new ConnectionEntry();
        secret = string.Empty;
        error = string.Empty;

        var host = HostBox.Text.Trim();
        if (host.Length == 0)
        {
            error = "호스트 주소 또는 IP 를 입력하세요.";
            HostBox.Focus();
            return false;
        }

        if (!int.TryParse(PortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            error = "포트는 1 ~ 65535 사이의 숫자여야 합니다.";
            PortBox.Focus();
            return false;
        }

        var username = UserBox.Text.Trim();
        if (username.Length == 0)
        {
            error = "사용자 이름을 입력하세요.";
            UserBox.Focus();
            return false;
        }

        var auth = AuthCombo.SelectedIndex == 1 ? AuthMethod.PrivateKey : AuthMethod.Password;
        string? keyPath = null;

        if (auth == AuthMethod.PrivateKey)
        {
            keyPath = KeyPathBox.Text.Trim();
            if (keyPath.Length == 0)
            {
                error = "개인키 파일을 고르세요.";
                return false;
            }

            if (!System.IO.File.Exists(keyPath))
            {
                error = "개인키 파일을 찾을 수 없습니다: " + keyPath;
                return false;
            }

            secret = KeyPassphraseBox.Password;
        }
        else
        {
            secret = PasswordBoxInput.Password;
            if (secret.Length == 0)
            {
                error = "비밀번호를 입력하세요.";
                PasswordBoxInput.Focus();
                return false;
            }
        }

        entry = new ConnectionEntry
        {
            DisplayName = DisplayNameBox.Text.Trim(),
            Host = host,
            Port = port,
            Username = username,
            Auth = auth,
            PrivateKeyPath = keyPath,
            SaveSecret = SaveSecretCheck.IsChecked == true,
        };

        return true;
    }

    private void SetBusy(bool busy)
    {
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.IsEnabled = !busy;
        CancelConnectButton.IsEnabled = busy;
        AddressBookList.IsEnabled = !busy;
        DeleteEntryButton.IsEnabled = !busy && AddressBookList.SelectedItem is ConnectionEntry;
    }
}
