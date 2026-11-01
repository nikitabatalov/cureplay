using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
namespace CurePlay.Services;
public sealed record SystemSnapshot(double CpuPercent, double RamPercent, double UsedRamGb, double TotalRamGb, double FreeDiskGb, double DiskFreePercent, bool GameRunning, string? GameName);
public sealed class SystemMonitor
{
    // Uses GetSystemTimes for total machine CPU rather than this application's CPU.
    private ulong _lastIdle, _lastKernel, _lastUser;
    private bool _hasCpuSample;
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low; public uint High; public ulong Value => ((ulong)High << 32) | Low; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MemoryStatus
    {
        public uint Length; public uint MemoryLoad; public ulong TotalPhys; public ulong AvailPhys;
        public ulong TotalPageFile; public ulong AvailPageFile; public ulong TotalVirtual; public ulong AvailVirtual; public ulong AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    public SystemSnapshot Read(string[] gameNames)
    {
        var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref mem)) throw new InvalidOperationException("Unable to read memory status.");
        var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
        var disk = new DriveInfo(root);
        var free = disk.AvailableFreeSpace / 1073741824d;
        var diskPercent = disk.TotalSize > 0 ? disk.AvailableFreeSpace * 100d / disk.TotalSize : 0;
        string? detected = null;
        var names = new HashSet<string>(gameNames.Select(x => x.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { if (detected is null && names.Contains(p.ProcessName)) detected = p.ProcessName; }
            catch { /* Some protected processes cannot be inspected. */ }
            finally { p.Dispose(); }
        }
        double cpu = 0;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var i = idle.Value; var k = kernel.Value; var u = user.Value;
            if (_hasCpuSample)
            {
                var total = (k - _lastKernel) + (u - _lastUser);
                var idleDelta = i - _lastIdle;
                if (total > 0) cpu = Math.Clamp((total - Math.Min(total, idleDelta)) * 100d / total, 0, 100);
            }
            _lastIdle = i; _lastKernel = k; _lastUser = u; _hasCpuSample = true;
        }
        return new(cpu, mem.MemoryLoad, (mem.TotalPhys - mem.AvailPhys) / 1073741824d, mem.TotalPhys / 1073741824d, free, diskPercent, detected is not null, detected);
    }
}
