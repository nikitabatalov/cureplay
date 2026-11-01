using System.Windows.Media;

namespace CurePlay.Models;

public sealed record HardwareSnapshot(double? GpuLoad, double? CpuTemp, double? GpuTemp, string RamSpecification);

// Status is scoped to Windows Storage Health; sensors are optional, not proof of health.
public sealed record DiskInfoRow(string Name, string KindAndSize, string StatusShort,
    string StatusBrush, string Temperature, string SmartDetails, string SourceNote);

public sealed record ProcessInfoRow(int Pid, string Name, double CpuPercent, double RamMb,
    string Note, ImageSource? Icon);

public sealed record PingSample(DateTime Time, double? LatencyMs);
public sealed record NetworkSnapshot(string Target, double? PingMs, double? JitterMs, double LossPercent, int WindowSamples,
    int Outages, double DownloadMbps, double UploadMbps, string Status);
public sealed record FrameAnalysis(int Count, int StutterCount, double MedianMs, double P99Ms, double OnePercentLowFps, string Column);

public sealed record RamDiagnostic(string ShortLabel, string Explanation, uint? ConfiguredMt, uint? ReportedMt);
public sealed record PingPreset(string Label, string Host, string Note);
