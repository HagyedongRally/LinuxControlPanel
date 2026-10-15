using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LinuxControlPanel.Core.Linux;
using LinuxControlPanel.Core.Models;
using LinuxControlPanel.Core.Ssh;
using LinuxControlPanel.Views;

namespace LinuxControlPanel;

/// <summary>
/// 로그인 후 첫 화면. 대상 PC 의 CPU/메모리/디스크/네트워크/프로세스를 주기적으로 읽어 보여 준다.
/// 수집은 SSH 한 번으로 끝내고, UI 갱신은 항상 Dispatcher 스레드에서만 한다.
/// </summary>
public partial class DashboardWindow : Window
{
    private readonly SshSession _session;
    private readonly ConnectionEntry _entry;
    private readonly SystemInfoCollector _collector = new();
    private readonly DispatcherTimer _timer = new();
    private readonly CancellationTokenSource _lifetimeCts = new();

    private bool _refreshing;
    private bool _connectionLost;
    private int _failureCount;

    public DashboardWindow(SshSession session, ConnectionEntry entry)
    {
        _session = session;
        _entry = entry;

        InitializeComponent();

        Title = "Linux Control Panel — " + session.Target;
        HostTitleText.Text = string.IsNullOrWhiteSpace(entry.DisplayName) ? session.Host : entry.DisplayName;
        HostSubtitleText.Text = session.Target;
        FingerprintText.Text = "호스트 키 " + session.HostKeyFingerprint;

        _timer.Interval = TimeSpan.FromSeconds(3);
        _timer.Tick += OnTimerTick;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppendOutput($"{_session.Target} 에 연결되었습니다.");

        await RefreshAsync(manual: true);

        if (AutoRefreshCheck.IsChecked == true && !_connectionLost)
        {
            _timer.Start();
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;

        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();

        // 창이 닫히면 SSH 연결도 끊는다.
        _session.Dispose();
    }

    private async void OnTimerTick(object? sender, EventArgs e) => await RefreshAsync(manual: false);

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync(manual: true);

    private void AutoRefreshCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (AutoRefreshCheck.IsChecked == true && !_connectionLost)
        {
            _timer.Start();
            _ = RefreshAsync(manual: false);
        }
        else
        {
            _timer.Stop();
        }
    }

    private void IntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IntervalCombo.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out var seconds))
        {
            _timer.Interval = TimeSpan.FromSeconds(seconds);
        }
    }

    /// <summary>수집 한 번. 겹쳐 들어오면 건너뛴다(느린 회선에서 요청이 쌓이지 않게).</summary>
    private async Task RefreshAsync(bool manual)
    {
        if (_refreshing || _connectionLost)
        {
            return;
        }

        _refreshing = true;
        RefreshButton.IsEnabled = false;

        if (manual)
        {
            SetStatus("상태를 읽는 중...", isError: false);
        }

        try
        {
            var snapshot = await _collector.CollectAsync(_session, _lifetimeCts.Token);
            ApplySnapshot(snapshot);

            _failureCount = 0;
            RefreshStateText.Text = "마지막 갱신 " + snapshot.CollectedAt.ToLocalTime().ToString("HH:mm:ss");
            SetStatus(string.Empty, isError: false);
        }
        catch (OperationCanceledException)
        {
            // 창을 닫는 중이다. 알릴 것이 없다.
        }
        catch (Exception ex)
        {
            _failureCount++;
            SetStatus("상태를 읽지 못했습니다: " + SshErrorText.Describe(ex), isError: true);

            // 연결이 끊긴 게 분명하거나 연속으로 실패하면 자동 갱신을 멈춘다.
            if (!_session.IsConnected || _failureCount >= 3)
            {
                _connectionLost = true;
                _timer.Stop();
                AutoRefreshCheck.IsChecked = false;
                RefreshStateText.Text = "연결 끊김";
                SetStatus("연결이 끊어졌습니다. [연결 끊기] 를 눌러 접속 화면으로 돌아가 다시 접속하세요.", isError: true);
            }
        }
        finally
        {
            _refreshing = false;
            RefreshButton.IsEnabled = !_connectionLost;
        }
    }

    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        var fallbackName = string.IsNullOrWhiteSpace(_entry.DisplayName) ? _session.Host : _entry.DisplayName;
        HostTitleText.Text = string.IsNullOrWhiteSpace(snapshot.Hostname) ? fallbackName : snapshot.Hostname;

        var kernel = string.IsNullOrWhiteSpace(snapshot.Architecture)
            ? snapshot.Kernel
            : snapshot.Kernel + " " + snapshot.Architecture;

        HostSubtitleText.Text = string.Join(" · ", new[]
        {
            _session.Target,
            snapshot.OsPrettyName,
            kernel,
            "가동 " + snapshot.UptimeText,
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

        CpuPercentText.Text = Humanize.Percent(snapshot.CpuUsagePercent);
        CpuMeter.Value = snapshot.CpuUsagePercent;
        CpuMeter.Foreground = MeterBrush(snapshot.CpuUsagePercent);
        CpuModelText.Text = string.IsNullOrWhiteSpace(snapshot.CpuModel) ? "CPU 모델 정보 없음" : snapshot.CpuModel;

        var cpuDetails = new List<string> { $"코어 {snapshot.CpuCount}개", "로드 " + snapshot.LoadText };
        if (snapshot.CpuTemperatureCelsius is { } temperature)
        {
            cpuDetails.Add($"온도 {temperature:0.0} ℃");
        }

        CpuDetailText.Text = string.Join(" · ", cpuDetails);
        CoreList.ItemsSource = Rows.Cores(snapshot);

        var memory = snapshot.Memory;
        MemoryPercentText.Text = Humanize.Percent(memory.UsedPercent);
        MemoryMeter.Value = memory.UsedPercent;
        MemoryMeter.Foreground = MeterBrush(memory.UsedPercent);
        MemoryDetailText.Text =
            $"{Humanize.Bytes(memory.UsedBytes)} / {Humanize.Bytes(memory.TotalBytes)} 사용 중 · 여유 {Humanize.Bytes(memory.AvailableBytes)}";

        if (memory.SwapTotalBytes > 0)
        {
            SwapDetailText.Text =
                $"스왑 {Humanize.Bytes(memory.SwapUsedBytes)} / {Humanize.Bytes(memory.SwapTotalBytes)} ({Humanize.Percent(memory.SwapUsedPercent)})";
            SwapMeter.Visibility = Visibility.Visible;
            SwapMeter.Value = memory.SwapUsedPercent;
            SwapMeter.Foreground = MeterBrush(memory.SwapUsedPercent);
        }
        else
        {
            SwapDetailText.Text = "스왑 없음";
            SwapMeter.Visibility = Visibility.Collapsed;
        }

        DiskList.ItemsSource = Rows.Disks(snapshot);
        NetworkList.ItemsSource = Rows.Network(snapshot);
        ProcessList.ItemsSource = Rows.Processes(snapshot);

        var users = Rows.Users(snapshot);
        UserList.ItemsSource = users;
        NoUsersText.Visibility = users.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>70% 넘으면 노랑, 90% 넘으면 빨강으로 바꿔 한눈에 보이게 한다.</summary>
    private Brush MeterBrush(double percent) => percent switch
    {
        >= 90 => (Brush)FindResource("DangerBrush"),
        >= 70 => (Brush)FindResource("WarnBrush"),
        _ => (Brush)FindResource("AccentBrush"),
    };

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? (Brush)FindResource("DangerBrush") : (Brush)FindResource("MutedBrush");
    }
}
