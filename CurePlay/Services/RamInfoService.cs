using System.Management;
using CurePlay.Models;

namespace CurePlay.Services;

public static class RamInfoService
{
    public static RamDiagnostic ReadDiagnostic()
    {
        try
        {
            using var search = new ManagementObjectSearcher("SELECT SMBIOSMemoryType, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
            using var result = search.Get();
            var configuredSpeeds = new List<uint>();
            var reportedSpeeds = new List<uint>();
            var types = new List<int>();
            foreach (ManagementObject item in result)
            {
                try
                {
                    if (uint.TryParse(item["ConfiguredClockSpeed"]?.ToString(), out var c) && c > 0) configuredSpeeds.Add(c);
                    if (uint.TryParse(item["Speed"]?.ToString(), out var s) && s > 0) reportedSpeeds.Add(s);
                    if (int.TryParse(item["SMBIOSMemoryType"]?.ToString(), out var type)) types.Add(type);
                }
                finally { item.Dispose(); }
            }
            var typeName = types.Contains(34) ? "DDR5" : types.Contains(26) ? "DDR4" : types.Contains(24) ? "DDR3" :
                types.Contains(30) ? "LPDDR4" : types.Contains(35) ? "LPDDR5" : "RAM";
            uint? configured = configuredSpeeds.Count == 0 ? null : configuredSpeeds.Min();
            uint? reported = reportedSpeeds.Count == 0 ? null : reportedSpeeds.Min();
            var current = configured ?? reported;
            string compact = current.HasValue ? $"{typeName} · {current} MT/s" : $"{typeName} · скорость неизвестна";
            string explanation;
            if (configured is > 0 && reported is > 0 && configured < reported * .92)
                explanation = $"Windows сообщает рабочую скорость {configured} MT/s и значение Speed {reported} MT/s. Возможно, модуль работает ниже заявленного SMBIOS-значения. Это НЕ подтверждение доступного XMP/EXPO; проверь паспорт памяти, материнскую плату и BIOS вручную.";
            else if (configured is > 0)
                explanation = $"Скорость по SMBIOS: {configured} MT/s. Достоверно определить доступность XMP/EXPO и максимальную частоту через обычный WMI нельзя. Автоматический разгон отключён.";
            else
                explanation = "Windows не сообщила рабочую скорость модулей. Проверка XMP/EXPO требует данных SPD/BIOS и поддержки платы.";
            return new(compact, explanation, configured, reported);
        }
        catch (Exception ex)
        {
            return new("RAM · скорость недоступна", $"Не удалось прочитать SMBIOS: {ex.Message}. Автоматические выводы о разгоне недоступны.", null, null);
        }
    }

    public static string ReadSpecification() => ReadDiagnostic().ShortLabel;
}
