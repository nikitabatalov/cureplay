using System.IO;
namespace CurePlay.Services;
public sealed record OptimizationResult(string Message, int DeletedFiles, long ReclaimedBytes);
public static class SmartOptimizer
{
    // No RAM trimming, service manipulation, registry edits, or game process termination.
    // Only user TEMP files older than 7 days, only if disk space is critically low.
    public static OptimizationResult Run(SystemSnapshot snapshot, CancellationToken token)
    {
        if (snapshot.GameRunning) return new("Игра запущена — очистка отложена, чтобы не вызывать фризы.", 0, 0);
        if (snapshot.FreeDiskGb >= 10 && snapshot.DiskFreePercent >= 10)
            return new("Свободного места достаточно — вмешательство не требуется.", 0, 0);
        var temp = Path.GetFullPath(Path.GetTempPath());
        var localAppData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (!temp.StartsWith(localAppData + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return new("Папка TEMP вне профиля пользователя — очистка отменена.", 0, 0);
        var cutoff = DateTime.UtcNow.AddDays(-7);
        long reclaimed = 0;
        int deleted = 0;
        // Enumerate files, never follow directory symlinks/reparse points, never delete directories.
        var pending = new Stack<string>(); pending.Push(temp);
        while (pending.Count > 0 && deleted < 500 && reclaimed < 100 * 1024 * 1024)
        {
            token.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            try
            {
                foreach (var child in Directory.EnumerateDirectories(dir))
                {
                    try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child); }
                    catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    token.ThrowIfCancellationRequested();
                    if (deleted >= 500 || reclaimed >= 100 * 1024 * 1024) break;
                    try
                    {
                        var info = new FileInfo(file);
                        if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.ReadOnly | FileAttributes.System)) != 0 || info.LastWriteTimeUtc > cutoff) continue;
                        var size = info.Length;
                        if (size > 20 * 1024 * 1024 || reclaimed + size > 100 * 1024 * 1024) continue;
                        info.Delete(); deleted++; reclaimed += size;
                    }
                    catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return new($"Безопасная очистка завершена: {deleted} файлов, {reclaimed / 1048576d:F1} МБ.", deleted, reclaimed);
    }
}
