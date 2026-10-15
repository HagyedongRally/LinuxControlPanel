using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LinuxControlPanel.Core.Linux;
using LinuxControlPanel.Core.Ssh;
using LinuxControlPanel.Views;

namespace LinuxControlPanel;

/// <summary>
/// 대시보드의 두 번째 페이지: 대상 PC 에서 돌고 있는 systemd 서비스 목록과 선택한 서비스의 상세 정보.
/// 목록은 페이지를 열 때(30초 넘게 지났으면)와 새로고침을 누를 때만 읽는다.
/// </summary>
public partial class DashboardWindow
{
    private static readonly TimeSpan ServiceListMaxAge = TimeSpan.FromSeconds(30);

    private List<ServiceUnit> _services = new();
    private DateTime _servicesLoadedAt = DateTime.MinValue;
    private bool _servicesLoading;
    private bool _restoringServiceSelection;

    /// <summary>선택을 빠르게 바꿀 때 늦게 도착한 이전 응답을 버리기 위한 번호.</summary>
    private int _detailRequest;

    private bool ServicesTabActive => PageTabs.SelectedItem == ServicesTab;

    private void PageTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged 는 라우팅 이벤트라 탭 안의 ListBox/ComboBox 에서 올라온 것도 여기로 온다.
        if (!ReferenceEquals(e.OriginalSource, PageTabs) || !IsLoaded)
        {
            return;
        }

        if (ServicesTabActive)
        {
            if (DateTime.UtcNow - _servicesLoadedAt > ServiceListMaxAge)
            {
                _ = RefreshServicesAsync();
            }
        }
        else
        {
            _ = RefreshAsync(manual: false);
        }
    }

    private void ServiceRefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshServicesAsync();

    private void ServiceFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyServiceFilter();
        }
    }

    private void ServiceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_restoringServiceSelection && ServiceList.SelectedItem is ServiceRow row)
        {
            _ = LoadServiceDetailAsync(row.Unit);
        }
    }

    private async Task RefreshServicesAsync()
    {
        if (_servicesLoading || _connectionLost)
        {
            return;
        }

        _servicesLoading = true;
        ServiceRefreshButton.IsEnabled = false;
        SetStatus("서비스 목록을 읽는 중...", isError: false);

        try
        {
            _services = await ServiceCollector.ListAsync(_session, _lifetimeCts.Token);
            _servicesLoadedAt = DateTime.UtcNow;
            ApplyServiceFilter();
            RefreshStateText.Text = "서비스 목록 " + DateTime.Now.ToString("HH:mm:ss");
            SetStatus(string.Empty, isError: false);

            // 보고 있던 서비스의 상세도 새 값으로 바꾼다.
            if (ServiceList.SelectedItem is ServiceRow selected)
            {
                _ = LoadServiceDetailAsync(selected.Unit);
            }
        }
        catch (OperationCanceledException)
        {
            // 창을 닫는 중이다.
        }
        catch (Exception ex)
        {
            SetStatus("서비스 목록을 읽지 못했습니다: " + SshErrorText.Describe(ex), isError: true);
            CheckConnection();
        }
        finally
        {
            _servicesLoading = false;
            ServiceRefreshButton.IsEnabled = !_connectionLost;
        }
    }

    /// <summary>검색어와 상태 필터로 목록을 다시 그린다. 선택은 이름으로 되살리고 상세는 다시 읽지 않는다.</summary>
    private void ApplyServiceFilter()
    {
        var keyword = ServiceSearchBox.Text.Trim();
        var filter = (ServiceFilterCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        var selectedName = (ServiceList.SelectedItem as ServiceRow)?.Unit.Name;

        var visible = _services
            .Where(unit => filter switch
            {
                "running" => unit.Health == ServiceHealth.Running,
                "failed" => unit.Health == ServiceHealth.Failed,
                "stopped" => unit.Health == ServiceHealth.Stopped,
                "enabled" => unit.UnitFileState == "enabled",
                _ => true,
            })
            .Where(unit => keyword.Length == 0 ||
                           unit.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                           unit.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        var rows = Rows.Services(visible, ServiceBrush);

        _restoringServiceSelection = true;
        try
        {
            ServiceList.ItemsSource = rows;
            ServiceList.SelectedItem = rows.FirstOrDefault(row => row.Unit.Name == selectedName);
        }
        finally
        {
            _restoringServiceSelection = false;
        }

        var running = _services.Count(unit => unit.Health == ServiceHealth.Running);
        var failed = _services.Count(unit => unit.Health == ServiceHealth.Failed);
        ServiceSummaryText.Text = $"전체 {_services.Count}개 · 실행 중 {running} · 실패 {failed} · 표시 {rows.Count}";
        ServiceSummaryText.Foreground = failed > 0 ? (Brush)FindResource("WarnBrush") : (Brush)FindResource("MutedBrush");
    }

    private async Task LoadServiceDetailAsync(ServiceUnit unit)
    {
        var request = ++_detailRequest;

        ServiceDetailTitle.Text = unit.ShortName;
        ServiceDetailSubtitle.Text = string.IsNullOrWhiteSpace(unit.Description) ? unit.Name : unit.Name + " · " + unit.Description;
        ServicePropertyList.ItemsSource = null;

        if (!ServiceCollector.IsValidUnitName(unit.Name))
        {
            ServiceStatusBox.Text = "이름에 쓸 수 없는 글자가 있어 상세 정보를 읽지 않았습니다.";
            return;
        }

        if (_connectionLost || !_session.IsConnected)
        {
            ServiceStatusBox.Text = "연결이 끊어져 상세 정보를 읽을 수 없습니다.";
            return;
        }

        ServiceStatusBox.Text = "읽는 중...";

        try
        {
            var detail = await ServiceCollector.DetailAsync(_session, unit.Name, _lifetimeCts.Token);
            if (request != _detailRequest)
            {
                return;
            }

            var description = detail.Get("Description");
            if (!string.IsNullOrWhiteSpace(description))
            {
                ServiceDetailSubtitle.Text = unit.Name + " · " + description;
            }

            ServicePropertyList.ItemsSource = Rows.ServiceProperties(detail);
            ServiceStatusBox.Text = string.IsNullOrWhiteSpace(detail.StatusText) ? "(출력 없음)" : detail.StatusText;
            ServiceStatusBox.ScrollToHome();
        }
        catch (OperationCanceledException)
        {
            // 창을 닫는 중이다.
        }
        catch (Exception ex)
        {
            if (request == _detailRequest)
            {
                ServiceStatusBox.Text = "[오류] " + SshErrorText.Describe(ex);
            }

            CheckConnection();
        }
    }

    private Brush ServiceBrush(ServiceHealth health) => health switch
    {
        ServiceHealth.Running => (Brush)FindResource("OkBrush"),
        ServiceHealth.Active => (Brush)FindResource("AccentBrush"),
        ServiceHealth.Failed => (Brush)FindResource("DangerBrush"),
        ServiceHealth.Transitioning => (Brush)FindResource("WarnBrush"),
        _ => (Brush)FindResource("MutedBrush"),
    };
}
