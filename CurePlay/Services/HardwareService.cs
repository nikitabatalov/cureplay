using LibreHardwareMonitor.Hardware;
using CurePlay.Models;

namespace CurePlay.Services;

// Hardware sensor availability varies by motherboard, device, permissions and driver.
public sealed class HardwareService : IDisposable
{
    private readonly Computer _computer;
    private bool _ready;
    public HardwareService()
    {
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsStorageEnabled = true, IsMotherboardEnabled = false,
            IsMemoryEnabled = false, IsNetworkEnabled = false, IsControllerEnabled = false };
        try { _computer.Open(); _ready = true; } catch { _ready = false; }
    }

    private static IEnumerable<IHardware> Walk(IHardware hw)
    {
        yield return hw;
        foreach (var child in hw.SubHardware)
            foreach (var node in Walk(child)) yield return node;
    }
    private IEnumerable<IHardware> Scan()
    {
        if (!_ready) yield break;
        foreach (var hw in _computer.Hardware)
            foreach (var entry in Walk(hw))
            {
                try { entry.Update(); } catch { }
                yield return entry;
            }
    }
    private static double? BestTemp(IHardware hw, string[] names)
    {
        var list = hw.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue).ToList();
        foreach (var name in names)
        {
            var selected = list.FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (selected is not null) return selected.Value;
        }
        return list.FirstOrDefault()?.Value;
    }
    public HardwareSnapshot Read(string ramSpecification)
    {
        double? cpuTemp = null, gpuTemp = null, gpuLoad = null;
        try
        {
            foreach (var hw in Scan())
            {
                if (hw.HardwareType == HardwareType.Cpu)
                    cpuTemp ??= BestTemp(hw, ["Package", "Tctl", "Tdie", "Core Average"]);
                if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                {
                    gpuTemp ??= BestTemp(hw, ["GPU Core", "GPU Temperature", "Core"]);
                    gpuLoad ??= hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Value.HasValue &&
                        (s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase)))?.Value;
                }
            }
        }
        catch { /* lack of accessible sensors is not a fatal error */ }
        return new(gpuLoad, cpuTemp, gpuTemp, ramSpecification);
    }
    // Sensor-only readings are not used to guess a healthy/unhealthy SMART status.
    public List<DiskInfoRow> ReadStorageSensors()
    {
        var items = new List<DiskInfoRow>();
        try
        {
            foreach (var hw in Scan().Where(x => x.HardwareType == HardwareType.Storage))
            {
                var temp = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value.HasValue);
                var details = hw.Sensors.Where(s => s.Value.HasValue && (
                    s.Name.Contains("Life", StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Contains("Spare", StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Contains("Wearout", StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Contains("Power On", StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Contains("Host Writes", StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Contains("Error", StringComparison.OrdinalIgnoreCase)))
                    .Take(10).Select(s => $"{s.Name}: {s.Value:0.#}").ToArray();
                items.Add(new DiskInfoRow(hw.Name, "Датчики SMART · объём не указан", "●  Нет оценки", "#A4B4BC",
                    temp is null ? "Неизвестна" : $"{temp.Value:0.#} °C",
                    details.Length == 0 ? "SMART-поля контроллером не предоставлены" : string.Join("  ·  ", details),
                    "LibreHardwareMonitor. Оценка состояния диска из датчиков не выводится."));
            }
        }
        catch { }
        return items;
    }
    public void Dispose()
    {
        if (!_ready) return;
        try { _computer.Close(); } catch { }
    }
}
