using System.Management;
using CurePlay.Models;

namespace CurePlay.Services;

public static class StorageService
{
    public static List<DiskInfoRow> ReadWindowsDiskHealth()
    {
        var disks = new List<DiskInfoRow>();
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();
            using var query = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT FriendlyName, HealthStatus, MediaType, Size FROM MSFT_PhysicalDisk"));
            using var rows = query.Get();
            foreach (ManagementObject row in rows)
            {
                try
                {
                    var (label, brush, details) = row["HealthStatus"]?.ToString() switch
                    {
                        "0" => ("●  Здоров", "#B4FF78", "Windows Storage Health: Healthy"),
                        "1" => ("●  Внимание", "#FFCB75", "Windows Storage Health: Warning"),
                        "2" => ("●  Проблема", "#FF8282", "Windows Storage Health: Unhealthy"),
                        _ => ("●  Нет данных", "#A4B4BC", "Windows не сообщил состояние")
                    };
                    var media = row["MediaType"]?.ToString() switch { "3" => "HDD", "4" => "SSD", "5" => "SCM", _ => "Диск" };
                    var size = ulong.TryParse(row["Size"]?.ToString(), out var bytes) ? $"{bytes / 1_000_000_000d:0} GB" : "Объём неизвестен";
                    disks.Add(new(row["FriendlyName"]?.ToString() ?? "Накопитель", $"{media}  ·  {size}", label, brush,
                        "Неизвестна", "SMART-атрибуты пока не получены", details + ". Оценка Windows не является полной SMART-диагностикой."));
                }
                finally { row.Dispose(); }
            }
        }
        catch { /* Storage WMI may be restricted, including external/RAID disks. */ }
        return disks;
    }
}
