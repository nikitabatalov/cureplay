@echo off
setlocal
cd /d "%~dp0"
echo Publishing CurePlay 2.1 for Windows x64...
dotnet restore ".\CurePlay\CurePlay.csproj" -r win-x64
if errorlevel 1 goto :error
dotnet publish ".\CurePlay\CurePlay.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false --no-restore -o ".\publish"
if errorlevel 1 goto :error
echo.
echo SUCCESS! Open publish\CurePlay.exe and keep all files in publish together.
pause
exit /b 0
:error
echo.
echo ERROR: publish failed. Read the messages above, or build in Visual Studio with Ctrl+Shift+B.
pause
exit /b 1
