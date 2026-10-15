using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LinuxControlPanel.Core.Models;
using LinuxControlPanel.Core.Ssh;
using LinuxControlPanel.Core.Storage;
using LinuxControlPanel.Services;
using Microsoft.Win32;

namespace LinuxControlPanel;

/// <summary>
/// 첫 화면: 대상 PC 주소와 SSH 계정을 입력하거나 주소록에서 골라 접속한다.
/// 접속에 성공하면 세션을 대시보드 창에 넘기고 이 창은 닫는다.
/// </summary>
public partial class ConnectWindow : Window
{
    private readonly AddressBookStore _store = new(AddressBookStore.DefaultFilePath);
    private readonly ISecretProtector _protector = new DpapiSecretProtector();
    private readonly ObservableCollection<ConnectionEntry> _entries = new();

    private CancellationTokenSource? _connectCts;
    private bool _suppressSelectionChanged;
    private bool _sessionHandedOver;

    public ConnectWindow()
    {
        InitializeComponent();

        AddressBookList.ItemsSource = _entries;
        StorePathText.Text = "주소록 파일: " + _store.FilePath;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (var entry in await _store.LoadAsync())
            {
                _entries.Add(entry);
            }
        }
        catch (Exception ex)
        {
            SetStatus("주소록을 읽지 못했습니다: " + ex.Message, isError: true);
        }

        UpdateAddressBookChrome();

        if (_entries.Count > 0)
        {
            AddressBookList.SelectedIndex = 0;
            PasswordBoxInput.Focus();
        }
        else
        {
            HostBox.Focus();
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // 접속 중이면 취소해서 소켓이 매달려 있지 않게 한다.
        if (!_sessionHandedOver)
        {
            _connectCts?.Cancel();
        }
    }

    private void AddressBookList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DeleteEntryButton.IsEnabled = AddressBookList.SelectedItem is ConnectionEntry;

        if (_suppressSelectionChanged || AddressBookList.SelectedItem is not ConnectionEntry entry)
        {
            return;
        }

        ApplyEntryToForm(entry);
    }

    private void AddressBookList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AddressBookList.SelectedItem is ConnectionEntry && ConnectButton.IsEnabled)
        {
            _ = ConnectAsync();
        }
    }

    private void AuthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAuthPanels();

    private void BrowseKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "SSH 개인키 선택",
            Filter = "모든 파일 (*.*)|*.*|OpenSSH 키 (id_rsa;id_ed25519;*.key;*.pem)|id_rsa;id_ed25519;*.key;*.pem|PuTTY 키 (*.ppk)|*.ppk",
            FilterIndex = 2,
            InitialDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"),
        };

        if (dialog.ShowDialog(this) == true)
        {
            KeyPathBox.Text = dialog.FileName;
        }
    }

    private async void DeleteEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (AddressBookList.SelectedItem is not ConnectionEntry entry)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"주소록에서 '{entry.Label}' ({entry.Target}) 을(를) 지울까요?",
            "주소록 삭제",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var list = _entries.ToList();
        AddressBookStore.Remove(list, entry.Id);

        try
        {
            await _store.SaveAsync(list);
        }
        catch (Exception ex)
        {
            SetStatus("주소록을 저장하지 못했습니다: " + ex.Message, isError: true);
            return;
        }

        ReplaceEntries(list);
        SetStatus($"'{entry.Label}' 을(를) 주소록에서 지웠습니다.", isError: false);
    }

    private void ApplyEntryToForm(ConnectionEntry entry)
    {
        DisplayNameBox.Text = entry.DisplayName;
        HostBox.Text = entry.Host;
        PortBox.Text = entry.Port.ToString(CultureInfo.InvariantCulture);
        UserBox.Text = entry.Username;
        AuthCombo.SelectedIndex = entry.Auth == AuthMethod.PrivateKey ? 1 : 0;
        KeyPathBox.Text = entry.PrivateKeyPath ?? string.Empty;
        SaveSecretCheck.IsChecked = entry.SaveSecret;

        PasswordBoxInput.Clear();
        KeyPassphraseBox.Clear();
        SetStatus(string.Empty, isError: false);

        if (entry.ProtectedSecret is { Length: > 0 } stored)
        {
            var secret = _protector.Unprotect(stored);
            if (secret is null)
            {
                // 다른 Windows 계정/PC 에서 저장한 값은 DPAPI 로 풀 수 없다.
                SetStatus("저장된 비밀 값을 이 Windows 계정에서는 풀 수 없습니다. 다시 입력해 주세요.", isError: true);
            }
            else if (entry.Auth == AuthMethod.PrivateKey)
            {
                KeyPassphraseBox.Password = secret;
            }
            else
            {
                PasswordBoxInput.Password = secret;
            }
        }

        UpdateAuthPanels();
    }

    private void UpdateAuthPanels()
    {
        // ComboBox 의 SelectionChanged 는 InitializeComponent 도중에도 올라온다.
        // 그때는 아래 패널들이 아직 만들어지지 않았으므로 건너뛴다.
        if (PasswordPanel is null || KeyPanel is null)
        {
            return;
        }

        var usesKey = AuthCombo.SelectedIndex == 1;
        PasswordPanel.Visibility = usesKey ? Visibility.Collapsed : Visibility.Visible;
        KeyPanel.Visibility = usesKey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateAddressBookChrome()
    {
        EmptyBookText.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddressBookCountText.Text = _entries.Count == 0 ? string.Empty : $"{_entries.Count}개 저장됨";
        DeleteEntryButton.IsEnabled = AddressBookList.SelectedItem is ConnectionEntry;
    }

    private void ReplaceEntries(IEnumerable<ConnectionEntry> entries)
    {
        _suppressSelectionChanged = true;
        try
        {
            _entries.Clear();
            foreach (var entry in entries)
            {
                _entries.Add(entry);
            }
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        UpdateAddressBookChrome();
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? (System.Windows.Media.Brush)FindResource("DangerBrush")
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
    }
}
