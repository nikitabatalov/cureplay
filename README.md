# CurePlay v1 — Windows 10/11 gaming optimizer

A WPF desktop application using C# and .NET 10. Gaming dark UI, system monitoring, 5-minute smart checks, process-name game detection, background tray mode, and conservative cleanup of old user TEMP files **only when disk space is low and no configured game is detected**.

## Important behavior

- CPU/RAM figures are **monitoring only**. No RAM trimming, page-cache flushes, forced process termination, service disabling, or registry tweaking.
- Automatic cleanup triggers if the system disk has **less than 10 GB free OR less than 10% free**.
- Only files older than 7 days under the current user's LocalAppData TEMP folder are eligible. No directories are removed. Symlink/reparse-point directories are skipped. In-use/protected files are skipped.
- A pass deletes at most 500 files / 100 MiB. Files over 20 MiB are skipped. Cleanup is deferred while a recognized game runs.
- Game detection uses a **configurable process-name list**. Add your game executable via "Настроить игры"; this is not a universal detection engine.
- The five-minute interval is fixed in v1. The CPU meter requires a second sample to show a meaningful reading.
- The app does **not** guarantee FPS improvements. Its primary purpose is conservative system housekeeping and visibility.
- Closing the window hides it in the tray. Use tray icon → "Выход" to quit.

## Source layout

```
CurePlay/
  CurePlay.csproj
  App.xaml, App.xaml.cs
  MainWindow.xaml, MainWindow.xaml.cs
  GameListWindow.xaml, GameListWindow.xaml.cs
  Models/AppSettings.cs
  Services/SettingsService.cs
  Services/SystemMonitor.cs
  Services/SmartOptimizer.cs
.github/workflows/build-windows.yml
```

## Build locally (Windows)

1. Install the **.NET 10 SDK** (or Visual Studio 2026 with .NET desktop development workload).
2. Open PowerShell in the repository root.
3. Run:

```powershell
dotnet restore .\CurePlay\CurePlay.csproj -r win-x64
dotnet publish .\CurePlay\CurePlay.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o .\publish
```

4. Launch `publish\CurePlay.exe`. No separate .NET runtime installation is required for this self-contained build.

## Build in GitHub without installing developer tools

1. Create a **private** repository on GitHub and upload **the contents of this archive**, including the hidden `.github` folder (keep the same paths).
2. Go to **Actions → Build CurePlay Windows EXE → Run workflow**.
3. After the workflow succeeds, open the run → **Artifacts → CurePlay-v1-win-x64**.
4. Extract the ZIP to get `CurePlay.exe`. You can place the EXE anywhere and run it on Windows 10/11 x64.

The EXE is not code-signed. Windows SmartScreen may warn about an unknown publisher. Review the source and build workflow before running; do not disable antivirus.

## Settings

Saved to `%LOCALAPPDATA%\CurePlay\settings.json`.

## Limitations

The code has been authored and statically reviewed, but **not compiled or runtime-tested in this environment**, which does not include the .NET SDK or Windows. The GitHub Actions workflow provides a Windows build and will surface any compilation errors. This is a prototype, not a benchmarked performance utility.
