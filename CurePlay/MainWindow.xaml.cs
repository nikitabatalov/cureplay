using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using CurePlay.Models;
using CurePlay.Services;
using Forms = System.Windows.Forms;
namespace CurePlay;
public partial class MainWindow : Window
{
    private readonly AppSettings _settings = SettingsService.Load();
    private readonly SystemMonitor _monitor = new();
    private readonly DispatcherTimer _displayTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _checkTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly Forms.NotifyIcon _tray;
    private bool _isClosing;
    private bool _checking;
    private SystemSnapshot? _snapshot;
    public MainWindow()
    {
        InitializeComponent();
        BoostToggle.IsChecked = _settings.SmartBoostEnabled;
        _tray = new Forms.NotifyIcon { Icon = SystemIcons.Application, Text = "CurePlay", Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray.ContextMenuStrip = menu;
        _displayTimer.Tick += (_, _) => RefreshMetrics();
        _checkTimer.Tick += async (_, _) => await RunCheckAsync();
        _displayTimer.Start(); _checkTimer.Start();
        Loaded += async (_, _) => { RefreshMetrics(); await RunCheckAsync(); };
        AddLog("CurePlay запущен. Интервал Smart Boost: 5 минут.");
    }
    private void RefreshMetrics()
    {
        try
        {
            _snapshot = _monitor.Read(_settings.GameExecutables);
            CpuText.Text = $"{_snapshot.CpuPercent:F0}%"; CpuBar.Value = _snapshot.CpuPercent;
            RamText.Text = $"{_snapshot.RamPercent:F0}%"; RamBar.Value = _snapshot.RamPercent;
            DiskText.Text = $"{_snapshot.FreeDiskGb:F1} GB"; DiskBar.Value = _snapshot.DiskFreePercent;
            GameText.Text = _snapshot.GameRunning ? $"● Игра обнаружена: {_snapshot.GameName} — очистка заблокирована" : "● Активных игр из списка не найдено";
        }
        catch (Exception ex) { AddLog("Ошибка мониторинга: " + ex.Message); }
    }
    private async Task RunCheckAsync()
    {
        if (_checking) return;
        _checking = true;
        try
        {
            RefreshMetrics();
            if (_snapshot is null) return;
            LastCheckText.Text = "Последняя проверка: " + DateTime.Now.ToString("HH:mm:ss");
            if (!_settings.SmartBoostEnabled) { AddLog("Smart Boost выключен — только мониторинг."); return; }
            var snapshot = _snapshot;
            var result = await Task.Run(() => SmartOptimizer.Run(snapshot, CancellationToken.None));
            AddLog(result.Message);
            if (result.DeletedFiles > 0) RefreshMetrics();
        }
        catch (Exception ex) { AddLog("Ошибка проверки: " + ex.Message); }
        finally { _checking = false; }
    }
    private void AddLog(string message)
    {
        LogList.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        while (LogList.Items.Count > 100) LogList.Items.RemoveAt(LogList.Items.Count - 1);
    }
    private void BoostToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.SmartBoostEnabled = BoostToggle.IsChecked == true;
        ModeText.Text = _settings.SmartBoostEnabled ? "● SMART BOOST ACTIVE" : "○ SMART BOOST PAUSED";
        SettingsService.Save(_settings);
        AddLog(_settings.SmartBoostEnabled ? "Smart Boost включён." : "Smart Boost выключен.");
    }
    private async void CheckNow_Click(object sender, RoutedEventArgs e) => await RunCheckAsync();
    private void EditGames_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new GameListWindow(string.Join(Environment.NewLine, _settings.GameExecutables)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _settings.GameExecutables = dialog.GameNames;
        SettingsService.Save(_settings);
        RefreshMetrics(); AddLog("Список игр обновлён.");
    }
    private void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void HideToTray_Click(object sender, RoutedEventArgs e) => Hide();
    private void ExitApp() { _isClosing = true; Close(); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isClosing) { e.Cancel = true; Hide(); return; }
        _displayTimer.Stop(); _checkTimer.Stop();
        _tray.Visible = false; _tray.Dispose();
        base.OnClosing(e);
    }
}
