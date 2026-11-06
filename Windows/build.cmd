@echo off
rem Builds Kaleido Wallpaper into the "publish" folder. Needs the .NET 8 SDK: https://dotnet.microsoft.com/download
cd /d "%~dp0"
dotnet publish KaleidoWallpaper.csproj -c Release -r win-x64 --self-contained false -o publish
if errorlevel 1 exit /b 1
echo.
echo Klaar: publish\KaleidoWallpaper.exe
