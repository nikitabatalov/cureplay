using System.ComponentModel;
using System.Diagnostics;
using System.Media;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CurePlay.Models;
using CurePlay.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using Button = System.Windows.Controls.Button;

namespace CurePlay;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = SettingsService.Load();
    private readonly SystemMonitor _monitor = new();
    private readonly HardwareService _hardware = new();
    private readonly HistoryStore _history = new();
    private readonly ProcessAnalyzer _processAnalyzer = new();
    private readonly object _hardwareGate = new();
    private readonly CureNetService _net;
    private readonly DispatcherTimer _displayTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _checkTimer = new();
    private readonly DispatcherTimer _processTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Forms.NotifyIcon _tray;
    private readonly List<ProcessInfoRow> _processRows = new();
    private SystemSnapshot? _snapshot;
    private bool _isClosing;
    private bool _checking;
    private bool _reading;
    private bool _readingProcesses;
    private DateTime _lastAlert = DateTime.MinValue;
    private DateTime _lastHitch = DateTime.MinValue;
    private int _hitchCount;
    private string _ramSpec = "RAM · определение...";
    private string _currentPage = "Dashboard";
    private bool _isReady;

    private static readonly PingPreset[] PingPresets =
    [
        new("Cloudflare DNS · Anycast", "1.1.1.1", "Глобальная Anycast-сеть; IP не привязан к конкретному городу."),
        new("Google Public DNS · Anycast", "8.8.8.8", "Глобальная Anycast-сеть Google; география конечного узла может меняться."),
        new("Quad9 DNS · Anycast", "9.9.9.9", "Публичный резолвер Quad9; Anycast, не адрес игрового сервера."),
        new("OpenDNS · Anycast", "208.67.222.222", "Публичный DNS OpenDNS; Anycast, не адрес игрового сервера."),
        new("Amazon AWS · Frankfurt API (ICMP может быть закрыт)", "ec2.eu-central-1.amazonaws.com", "Официальный региональный API AWS, не ping-сервер: ICMP может быть полностью заблокирован."),
        new("Amazon AWS · Stockholm API (ICMP может быть закрыт)", "ec2.eu-north-1.amazonaws.com", "Официальный региональный API AWS, не ping-сервер: ICMP может быть полностью заблокирован."),
        new("Frankfurt · Vultr test", "fra-de-ping.vultr.com", "Географический тестовый хост Vultr; доступность ICMP не гарантируется."),
        new("Stockholm · Vultr test", "sto-se-ping.vultr.com", "Географический тестовый хост Vultr; доступность ICMP не гарантируется."),
        new("Warsaw · Vultr test", "waw-pl-ping.vultr.com", "Географический тестовый хост Vultr; доступность ICMP не гарантируется."),
        new("Amsterdam · Vultr test", "ams-nl-ping.vultr.com", "Географический тестовый хост Vultr; доступность ICMP не гарантируется."),
        new("Москва · свой адрес", "", "Для Москвы укажи собственный проверенный IP или домен. Не подставляем фиктивный сервер."),
        new("Другой адрес · вручную", "", "Введи публичный IP-адрес или домен, который согласен отвечать на ICMP.")
    ];

    public MainWindow()
    {
        InitializeComponent();
        _settings.CheckIntervalMinutes = Math.Clamp(_settings.CheckIntervalMinutes, 1, 60);
        _settings.TemperatureThresholdC = Math.Clamp(_settings.TemperatureThresholdC, 60, 110);
        if (!ValidHost(_settings.PingTarget)) _settings.PingTarget = "1.1.1.1";
        ThermalAlertsToggle.IsChecked = _settings.TemperatureAlertsEnabled;
        TemperatureSlider.Value = _settings.TemperatureThresholdC;
        ThresholdText.Text = $"{_settings.TemperatureThresholdC} °C";
        NetEnabledToggle.IsChecked = _settings.NetworkMonitoringEnabled;
        PingTargetBox.Text = _settings.PingTarget;
        PingPresetBox.ItemsSource = PingPresets;
        PingPresetBox.DisplayMemberPath = nameof(PingPreset.Label);
        PingPresetBox.SelectedItem = PingPresets.FirstOrDefault(x => x.Host == _settings.PingTarget) ?? PingPresets[^1];
        ApplyBoostModeVisual();
        _net = new CureNetService(_settings.PingTarget, _settings.NetworkMonitoringEnabled);
        _net.EventOccurred += text =>
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => AddLog(text)));
        };
        _tray = new Forms.NotifyIcon { Icon = Drawing.SystemIcons.Application, Text = "CurePlay 2.1", Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowWindow));
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть CurePlay", null, (_, _) => Dispatcher.BeginInvoke(new Action(ShowWindow)));
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.BeginInvoke(new Action(ExitApp)));
        _tray.ContextMenuStrip = menu;
        _checkTimer.Interval = TimeSpan.FromMinutes(_settings.CheckIntervalMinutes);
        _displayTimer.Tick += async (_, _) => await RefreshMetricsAsync();
        _checkTimer.Tick += async (_, _) => await RunCheckAsync();
        _processTimer.Tick += async (_, _) => { if (_currentPage == "Processes") await RefreshProcessesAsync(); };
        _isReady = true;
        Loaded += async (_, _) =>
        {
            try
            {
                var diagnostic = await Task.Run(RamInfoService.ReadDiagnostic);
                _ramSpec = diagnostic.ShortLabel;
                RamSpeedText.Text = _ramSpec;
                RamDiagnosticText.Text = diagnostic.Explanation;
                await RefreshMetricsAsync();
                await RunCheckAsync();
                _displayTimer.Start(); _checkTimer.Start(); _processTimer.Start();
            }
            catch (Exception ex) { AddLog("Инициализация: " + ex.Message); }
        };
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        AddLog("CurePlay 2.1 запущен. Проверка каждые 5 минут; мониторинг каждые 2 секунды.");
    }

    private static string DisplayPing(double? ping) => ping is null ? "—" : ping < 1 ? "<1 ms" : $"{ping:0} ms";
    private async Task RefreshMetricsAsync()
    {
        if (_reading) return;
        _reading = true;
        try
        {
            var names = _settings.GameExecutables.ToArray();
            var (s, h) = await Task.Run(() =>
            {
                var system = _monitor.Read(names);
                HardwareSnapshot hardware;
                lock (_hardwareGate) hardware = _hardware.Read(_ramSpec);
                return (system, hardware);
            });
            _snapshot = s;
            CpuText.Text = $"{s.CpuPercent:0}%"; CpuBar.Value = s.CpuPercent;
            GpuText.Text = h.GpuLoad.HasValue ? $"{h.GpuLoad:0}%" : "N/A"; GpuBar.Value = h.GpuLoad ?? 0;
            RamText.Text = $"{s.RamPercent:0}%"; RamBar.Value = s.RamPercent;
            RamSpeedText.Text = h.RamSpecification;
            DiskText.Text = $"{s.FreeDiskGb:0.0} GB свободно"; DiskBar.Value = s.DiskFreePercent;
            DiskSubText.Text = $"{s.DiskFreePercent:0.0}% свободно на системном разделе";
            var critical = s.FreeDiskGb < 10 || s.DiskFreePercent < 8;
            var attention = s.FreeDiskGb < 20 || s.DiskFreePercent < 15;
            DiskStatusText.Text = critical ? "● Критично" : attention ? "● Внимание" : "● Норма";
            DiskStatusText.Foreground = Brush(critical ? "#FF8585" : attention ? "#FFCE78" : "#B4FF78");
            GameText.Text = s.GameRunning ? $"● Игра обнаружена: {s.GameName}. Очистка TEMP отложена." : "● Игры из списка не обнаружены";
            CpuTempText.Text = h.CpuTemp.HasValue ? $"CPU  {h.CpuTemp:0.#} °C" : "CPU  N/A";
            GpuTempText.Text = h.GpuTemp.HasValue ? $"GPU  {h.GpuTemp:0.#} °C" : "GPU  N/A";
            ThermalStatusText.Text = $"Порог сигнала {_settings.TemperatureThresholdC} °C  ·  датчики могут быть недоступны";
            CheckTemperature(h);
            _history.Add("CPU", s.CpuPercent); _history.Add("GPU", h.GpuLoad); _history.Add("RAM", s.RamPercent);
            var n = _net.Read();
            PingText.Text = DisplayPing(n.PingMs);
            PingCaption.Text = $"ICMP • {n.Target}";
            NetPingText.Text = PingText.Text;
            JitterText.Text = n.JitterMs.HasValue ? $"{n.JitterMs:0.0} ms" : "—";
            LossText.Text = n.WindowSamples == 0 ? "—" : $"{n.LossPercent:0.#}%";
            NetStatusText.Text = $"{n.Target}  ·  {n.Status}  ·  {n.WindowSamples} последних запросов";
            TrafficText.Text = $"↓ {n.DownloadMbps:0.00} Mbps    ↑ {n.UploadMbps:0.00} Mbps";
            OutageText.Text = $"Зафиксировано серий из трёх неответов: {n.Outages}";
            _history.Add("PING", n.PingMs);
            ObserveSystemHitches(s, h, n);
        }
        catch (Exception ex) { AddLog("Мониторинг: " + ex.Message); }
        finally { _reading = false; }
    }

    private void ObserveSystemHitches(SystemSnapshot s, HardwareSnapshot h, NetworkSnapshot n)
    {
        if (!s.GameRunning)
        {
            HitchStatusText.Text = "Ожидание запущенной игры";
            return;
        }
        var reasons = new List<string>();
        if (s.CpuPercent >= 95) reasons.Add($"CPU {s.CpuPercent:0}%");
        if (s.RamPercent >= 95) reasons.Add($"RAM {s.RamPercent:0}%");
        if (h.GpuLoad >= 99) reasons.Add($"GPU {h.GpuLoad:0}%");
        if (n.PingMs > 200 && _settings.NetworkMonitoringEnabled) reasons.Add($"ping {n.PingMs:0} ms до тестового узла");
        if (reasons.Count == 0)
        {
            HitchStatusText.Text = "Грубых перегрузок пока не видно";
            return;
        }
        HitchStatusText.Text = "Возможная системная перегрузка";
        if (DateTime.Now - _lastHitch < TimeSpan.FromSeconds(20)) return;
        _lastHitch = DateTime.Now; _hitchCount++;
        HitchCountText.Text = $"Событий нагрузки за сеанс: {_hitchCount}";
        AddLog("System Hitch Watch: " + string.Join(", ", reasons) + " (не подтверждённый микрофриз)");
    }

    private void CheckTemperature(HardwareSnapshot h)
    {
        if (!_settings.TemperatureAlertsEnabled) return;
        var hot = new List<string>();
        if (h.CpuTemp >= _settings.TemperatureThresholdC) hot.Add($"CPU {h.CpuTemp:0.#} °C");
        if (h.GpuTemp >= _settings.TemperatureThresholdC) hot.Add($"GPU {h.GpuTemp:0.#} °C");
        if (hot.Count == 0 || DateTime.Now - _lastAlert < TimeSpan.FromMinutes(2)) return;
        _lastAlert = DateTime.Now;
        AddLog("ВЫСОКАЯ ТЕМПЕРАТУРА: " + string.Join(", ", hot));
        try { SystemSounds.Exclamation.Play(); } catch { }
        try { _tray.ShowBalloonTip(4000, "CurePlay · Высокая температура", string.Join(", ", hot), Forms.ToolTipIcon.Warning); }
        catch { }
    }

    private async Task RunCheckAsync()
    {
        if (_checking) return;
        _checking = true;
        BoostNowButton.IsEnabled = false;
        BoostStateText.Text = "Scanning  ·  анализ системы...";
        BeginBoostPulse();
        try
        {
            if (_snapshot is null) await RefreshMetricsAsync();
            if (_snapshot is null) { BoostStateText.Text = "Нет данных мониторинга"; return; }
            LastCheckText.Text = "Проверка: " + DateTime.Now.ToString("HH:mm:ss");
            if (!_settings.SmartBoostEnabled)
            {
                BoostStateText.Text = "Paused  ·  Smart Boost выключен";
                AddLog("Smart Boost выключен — мониторинг продолжается.");
                return;
            }
            var result = await Task.Run(() => SmartOptimizer.Run(_snapshot, CancellationToken.None));
            BoostStateText.Text = result.DeletedFiles > 0
                ? $"Ready  ·  очищено {result.ReclaimedBytes / 1048576d:0.0} MB"
                : "Ready  ·  вмешательство не требуется";
            AddLog(result.Message);
            if (result.DeletedFiles > 0) await RefreshMetricsAsync();
        }
        catch (Exception ex)
        {
            BoostStateText.Text = "Error  ·  проверка не завершена";
            AddLog("Smart Boost: " + ex.Message);
        }
        finally
        {
            _checking = false;
            BoostNowButton.IsEnabled = _settings.SmartBoostEnabled;
        }
    }

    private void BeginBoostPulse()
    {
        if (BoostPulseRing.RenderTransform is not ScaleTransform scale) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var animation = new DoubleAnimation(1, 1.24, TimeSpan.FromMilliseconds(750)) { AutoReverse = true, EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        BoostPulseRing.BeginAnimation(OpacityProperty,
            new DoubleAnimation(.75, .12, TimeSpan.FromMilliseconds(750)) { AutoReverse = true });
    }

    private static SolidColorBrush Brush(string hex) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    private void ApplyBoostModeVisual()
    {
        var on = _settings.SmartBoostEnabled;
        BoostModeButton.Content = on ? "●  Smart Boost: ON" : "○  Smart Boost: OFF";
        BoostModeButton.Background = Brush(on ? "#B4FF78" : "#34434C");
        BoostModeButton.Foreground = Brush(on ? "#0C1512" : "#D8E6E2");
        BoostNowButton.IsEnabled = on;
        if (SideStatus is not null)
            SideStatus.Text = on ? "●  SMART BOOST ACTIVE" : "○  MONITORING ONLY";
    }
    private void BoostMode_Click(object sender, RoutedEventArgs e)
    {
        _settings.SmartBoostEnabled = !_settings.SmartBoostEnabled;
        ApplyBoostModeVisual(); SaveSettings();
        BoostStateText.Text = _settings.SmartBoostEnabled ? "Ready  ·  Smart Boost включён" : "Paused  ·  Smart Boost выключен";
        AddLog(_settings.SmartBoostEnabled ? "Smart Boost включён" : "Smart Boost выключен");
    }

    private void AddLog(string text)
    {
        if (LogList is null) return;
        LogList.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {text}");
        while (LogList.Items.Count > 100) LogList.Items.RemoveAt(LogList.Items.Count - 1);
    }
    private void SaveSettings()
    {
        try { SettingsService.Save(_settings); }
        catch (Exception ex) { AddLog("Не удалось сохранить настройки: " + ex.Message); }
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _currentPage = btn.Tag?.ToString() ?? "Dashboard";
        PageDashboard.Visibility = _currentPage == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        PageNetwork.Visibility = _currentPage == "Network" ? Visibility.Visible : Visibility.Collapsed;
        PageProcesses.Visibility = _currentPage == "Processes" ? Visibility.Visible : Visibility.Collapsed;
        PageDisks.Visibility = _currentPage == "Disks" ? Visibility.Visible : Visibility.Collapsed;
        PageStutter.Visibility = _currentPage == "Stutter" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = _currentPage == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var b in new[] { NavDashboard, NavNetwork, NavProcesses, NavDisks, NavStutter, NavSettings })
        {
            var selected = b.Tag?.ToString() == _currentPage;
            b.Background = Brush(selected ? "#294439" : "#0E1720");
            b.Foreground = Brush(selected ? "#B4FF78" : "#A4B5BC");
        }
        if (_currentPage == "Processes") _ = RefreshProcessesAsync();
        if (_currentPage == "Disks") _ = RefreshDisksAsync();
    }
    private void OpenMetric_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string key) OpenChart(key);
    }
    private void OpenPingChart_Click(object sender, RoutedEventArgs e) => OpenChart("PING");
    private void OpenChart(string key) => new ChartWindow(_history, key) { Owner = this }.Show();
    private async void CheckNow_Click(object sender, RoutedEventArgs e) => await RunCheckAsync();
    private void EditGames_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new GameListWindow(string.Join(Environment.NewLine, _settings.GameExecutables)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _settings.GameExecutables = dialog.GameNames;
        SaveSettings(); AddLog("Список игр обновлён");
    }
    private static bool ValidHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253) return false;
        if (IPAddress.TryParse(value, out _)) return true;
        return value.Length >= 3 && Regex.IsMatch(value, @"^(?=.{1,253}$)[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?)+$");
    }
    private void PingPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady || PingPresetBox.SelectedItem is not PingPreset preset) return;
        if (!string.IsNullOrEmpty(preset.Host)) PingTargetBox.Text = preset.Host;
        else PingTargetBox.Text = "";
        PresetHintText.Text = preset.Note;
    }
    private void ApplyPingTarget_Click(object sender, RoutedEventArgs e)
    {
        var target = PingTargetBox.Text.Trim();
        if (!ValidHost(target))
        {
            System.Windows.MessageBox.Show("Введи корректный IP-адрес или домен, например 1.1.1.1.", "CureNet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _settings.PingTarget = target;
        _net.Configure(target, _settings.NetworkMonitoringEnabled);
        SaveSettings(); AddLog("CureNet: цель " + target);
        PingText.Text = "—"; NetPingText.Text = "—"; JitterText.Text = "—"; LossText.Text = "—";
    }
    private void NetworkSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        _settings.NetworkMonitoringEnabled = NetEnabledToggle.IsChecked == true;
        _net.Configure(_settings.PingTarget, _settings.NetworkMonitoringEnabled);
        SaveSettings();
    }
    private void ThermalSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (ThresholdText is null || TemperatureSlider is null || ThermalAlertsToggle is null) return;
        var value = (int)TemperatureSlider.Value;
        ThresholdText.Text = $"{value} °C";
        if (!_isReady) return;
        _settings.TemperatureThresholdC = value;
        _settings.TemperatureAlertsEnabled = ThermalAlertsToggle.IsChecked == true;
        SaveSettings();
    }

    private async Task RefreshProcessesAsync()
    {
        if (_readingProcesses) return;
        _readingProcesses = true;
        try
        {
            var rows = await Task.Run(_processAnalyzer.Sample);
            _processRows.Clear(); _processRows.AddRange(rows);
            ApplyProcessFilter();
        }
        catch (Exception ex) { AddLog("Процессы: " + ex.Message); }
        finally { _readingProcesses = false; }
    }
    private void ApplyProcessFilter()
    {
        IEnumerable<ProcessInfoRow> rows = _processRows;
        if (OnlyHeavyToggle.IsChecked == true) rows = rows.Where(r => r.CpuPercent >= 15 || r.RamMb >= 1500);
        rows = ProcessSortBox.SelectedIndex switch
        {
            1 => rows.OrderByDescending(r => r.CpuPercent),
            2 => rows.OrderByDescending(r => r.RamMb),
            _ => rows.OrderByDescending(r => r.CpuPercent * 10 + r.RamMb / 200)
        };
        var result = rows.ToArray();
        ProcessCards.ItemsSource = result;
        ProcessSummaryText.Text = result.Length == 0 ? "Нет подходящих приложений" : $"Приложений: {result.Length} · данные обновляются раз в 5 секунд";
    }
    private void ProcessFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_isReady) ApplyProcessFilter();
    }
    private async void RefreshProcesses_Click(object sender, RoutedEventArgs e) => await RefreshProcessesAsync();
    private void CloseProcessCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProcessInfoRow row }) return;
        if (_snapshot?.GameRunning == true && string.Equals(_snapshot.GameName, row.Name, StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show("CurePlay не закрывает обнаруженную игру.", "CurePlay"); return;
        }
        var answer = System.Windows.MessageBox.Show($"Отправить запрос закрытия «{row.Name}» (PID {row.Pid})?\nСначала сохрани несохранённые документы.",
            "Подтвердить действие", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _ = ProcessAnalyzer.TryCloseGracefully(row.Pid, row.Name, out var result);
        AddLog($"{row.Name}: {result}");
        System.Windows.MessageBox.Show(result, "CurePlay");
    }

    private static string NormalizedName(string name) => Regex.Replace(name.ToUpperInvariant(), "[^A-Z0-9]", "");
    private static bool SameDriveName(string a, string b)
    {
        var left = NormalizedName(a); var right = NormalizedName(b);
        return left.Length >= 7 && right.Length >= 7 && (left.Contains(right) || right.Contains(left));
    }
    private async Task RefreshDisksAsync()
    {
        try
        {
            var list = await Task.Run(() =>
            {
                var windows = StorageService.ReadWindowsDiskHealth();
                List<DiskInfoRow> sensors;
                lock (_hardwareGate) sensors = _hardware.ReadStorageSensors();
                var matched = new HashSet<int>();
                for (var i = 0; i < windows.Count; i++)
                {
                    var disk = windows[i];
                    for (var k = 0; k < sensors.Count; k++)
                    {
                        if (matched.Contains(k) || !SameDriveName(disk.Name, sensors[k].Name)) continue;
                        var sensor = sensors[k];
                        windows[i] = disk with
                        {
                            Temperature = sensor.Temperature,
                            SmartDetails = sensor.SmartDetails,
                            SourceNote = disk.SourceNote + " Датчики: " + sensor.SourceNote
                        };
                        matched.Add(k);
                        break;
                    }
                }
                // If a sensor has no reliable identity match, display it separately instead of merging into the wrong disk.
                for (var k = 0; k < sensors.Count; k++) if (!matched.Contains(k)) windows.Add(sensors[k]);
                return windows;
            });
            if (list.Count == 0) list.Add(new DiskInfoRow("Данные накопителей недоступны", "WMI/SMART не ответили",
                "●  Нет данных", "#A4B4BC", "—", "Windows и датчики не предоставили сведения.",
                "Попробуй проверить разрешения и драйвер контроллера. Отсутствие данных не означает здоровье диска."));
            DiskCards.ItemsSource = list;
            DiskDetailPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { AddLog("Диски: " + ex.Message); }
    }
    private async void RefreshDisks_Click(object sender, RoutedEventArgs e) => await RefreshDisksAsync();
    private void DiskDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DiskInfoRow row }) return;
        DiskDetailTitle.Text = row.Name;
        DiskDetailStatus.Text = row.StatusShort + "  ·  " + row.KindAndSize;
        DiskDetailStatus.Foreground = Brush(row.StatusBrush);
        DiskDetailTemp.Text = "Температура: " + row.Temperature;
        DiskDetailMore.Text = row.SmartDetails + "\n\n" + row.SourceNote;
        DiskDetailPanel.Visibility = Visibility.Visible;
    }
    private void HideDiskDetails_Click(object sender, RoutedEventArgs e) => DiskDetailPanel.Visibility = Visibility.Collapsed;

    private async void ImportFrames_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "CSV файлы (*.csv)|*.csv|Все файлы (*.*)|*.*", Title = "Выбери CSV с измеренным временем кадра" };
        if (dialog.ShowDialog(this) != true) return;
        StutterResultText.Text = "Анализ записи...";
        try
        {
            var result = await Task.Run(() => FrameAnalysisService.ReadCsv(dialog.FileName));
            StutterResultText.Text = $"Кадров: {result.Count:N0}\nМедиана: {result.MedianMs:0.00} мс  ·  P99: {result.P99Ms:0.00} мс\n" +
                $"Возможные микрофризы: {result.StutterCount:N0}\nОценка 1% Low: {result.OnePercentLowFps:0.0} FPS\nИсточник: {result.Column}";
            AddLog($"Stutter Lab: обработано {result.Count:N0} кадров");
        }
        catch (Exception ex) { StutterResultText.Text = "Не удалось прочитать CSV: " + ex.Message; }
    }
    private void OpenPresentMon_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/GameTechDev/PresentMon/releases") { UseShellExecute = true }); }
        catch (Exception ex) { AddLog("Не удалось открыть ссылку: " + ex.Message); }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Hide();
    private void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void HideToTray_Click(object sender, RoutedEventArgs e) => Hide();
    private void ExitApp() { _isClosing = true; Close(); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isClosing) { e.Cancel = true; Hide(); return; }
        _displayTimer.Stop(); _checkTimer.Stop(); _processTimer.Stop();
        _net.Dispose();
        lock (_hardwareGate) _hardware.Dispose();
        _tray.Visible = false; _tray.Dispose();
        base.OnClosing(e);
    }
}
