@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
dotnet publish src\Wahib.Desktop\Wahib.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o published\win-x64
if errorlevel 1 (
  echo PUBLISH FAILED
  pause
  exit /b 1
)
echo Published to published\win-x64
pause
