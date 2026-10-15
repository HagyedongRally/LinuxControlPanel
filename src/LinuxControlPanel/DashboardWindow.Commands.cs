using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LinuxControlPanel.Core.Ssh;

namespace LinuxControlPanel;

/// <summary>대시보드의 명령 실행과 연결 종료 부분.</summary>
public partial class DashboardWindow
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    private bool _commandRunning;

    private void RunCommandButton_Click(object sender, RoutedEventArgs e) => _ = RunCommandAsync(CommandBox.Text);

    private void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        _ = RunCommandAsync(CommandBox.Text);
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string command })
        {
            return;
        }

        CommandBox.Text = command;
        _ = RunCommandAsync(command);
    }

    private async Task RunCommandAsync(string rawCommand)
    {
        var command = (rawCommand ?? string.Empty).Trim();
        if (command.Length == 0 || _commandRunning)
        {
            return;
        }

        if (_connectionLost || !_session.IsConnected)
        {
            AppendOutput("연결이 끊어져 명령을 보낼 수 없습니다.");
            return;
        }

        _commandRunning = true;
        RunCommandButton.IsEnabled = false;
        AppendOutput("$ " + command);

        try
        {
            var result = await _session.RunAsync(command, CommandTimeout, _lifetimeCts.Token);

            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                AppendOutput(result.StandardOutput.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                AppendOutput("[stderr] " + result.StandardError.TrimEnd());
            }

            if (result.ExitCode != 0)
            {
                AppendOutput($"[종료 코드 {result.ExitCode}]");
            }
        }
        catch (OperationCanceledException)
        {
            AppendOutput("[취소됨]");
        }
        catch (Exception ex)
        {
            AppendOutput("[오류] " + SshErrorText.Describe(ex));
        }
        finally
        {
            _commandRunning = false;
            RunCommandButton.IsEnabled = true;
            AppendOutput(string.Empty);
        }
    }

    private void AppendOutput(string text)
    {
        CommandOutputBox.AppendText(text + Environment.NewLine);
        CommandOutputBox.ScrollToEnd();
    }

    /// <summary>접속 화면으로 돌아간다. 이 창이 닫힐 때 세션도 함께 정리된다.</summary>
    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        var connectWindow = new ConnectWindow();
        connectWindow.Show();
        Close();
    }
}
