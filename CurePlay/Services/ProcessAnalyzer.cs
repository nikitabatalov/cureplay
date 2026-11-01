using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CurePlay.Models;
using Drawing = System.Drawing;

namespace CurePlay.Services;

public sealed class ProcessAnalyzer
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime Moment)> _last = new();
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "svchost", "csrss", "wininit", "winlogon", "services", "lsass",
        "smss", "dwm", "explorer", "fontdrvhost", "sihost", "taskhostw", "cureplay"
    };

    private ImageSource? IconFor(Process process)
    {
        try
        {
            var executable = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(executable)) return null;
            if (_icons.TryGetValue(executable, out var existing)) return existing;
            // ImageSource is frozen before crossing the worker/UI thread boundary.
            using var icon = Drawing.Icon.ExtractAssociatedIcon(executable);
            if (icon is null) return _icons[executable] = null;
            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            _icons[executable] = source;
            return source;
        }
        catch { return null; }
    }

    public List<ProcessInfoRow> Sample()
    {
        var now = DateTime.UtcNow;
        var next = new Dictionary<int, (TimeSpan Cpu, DateTime Moment)>();
        var rows = new List<ProcessInfoRow>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == Environment.ProcessId || Protected.Contains(p.ProcessName)) continue;
                    var cpu = p.TotalProcessorTime;
                    next[p.Id] = (cpu, now);
                    double usage = 0;
                    if (_last.TryGetValue(p.Id, out var prev))
                    {
                        var dt = (now - prev.Moment).TotalSeconds;
                        if (dt > .1) usage = Math.Clamp((cpu - prev.Cpu).TotalSeconds / dt / Environment.ProcessorCount * 100, 0, 100);
                    }
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    var ram = p.WorkingSet64 / 1048576d;
                    var note = usage > 15 ? "Высокая нагрузка CPU" : ram > 1500 ? "Высокий расход RAM" : "Обычная нагрузка";
                    rows.Add(new(p.Id, p.ProcessName, usage, ram, note, IconFor(p)));
                }
                catch { /* some processes are protected */ }
            }
        }
        _last.Clear();
        foreach (var (pid, data) in next) _last[pid] = data;
        return rows.OrderByDescending(x => x.CpuPercent * 10 + x.RamMb / 200).Take(35).ToList();
    }

    public static bool TryCloseGracefully(int pid, string expectedName, out string result)
    {
        result = "";
        try
        {
            using var process = Process.GetProcessById(pid);
            if (pid == Environment.ProcessId || !string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase) ||
                Protected.Contains(process.ProcessName) || process.MainWindowHandle == IntPtr.Zero)
            { result = "Этот процесс нельзя закрыть из CurePlay."; return false; }
            if (!process.CloseMainWindow()) { result = "Приложение не приняло запрос на закрытие."; return false; }
            result = "Отправлен обычный запрос на закрытие окна. Несохранённые данные могут потребовать подтверждения.";
            return true;
        }
        catch (Exception ex) { result = ex.Message; return false; }
    }
}
