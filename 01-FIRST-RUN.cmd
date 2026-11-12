@echo off
setlocal EnableExtensions
chcp 65001 >nul
title Al-Wahib ERP - First Run
cd /d "%~dp0"
echo =====================================================
echo            AL-WAHIB ERP - FIRST RUN
 echo =====================================================
where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: .NET 10 SDK is not installed, or PATH is missing.
  echo Visit https://dotnet.microsoft.com/download/dotnet/10.0
  pause
  exit /b 1
)
dotnet --version
if errorlevel 1 goto :fail
echo [1/4] Restoring NuGet dependencies... Internet required once.
dotnet restore AlWahibERP.slnx
if errorlevel 1 goto :fail
echo [2/4] Building solution...
dotnet build AlWahibERP.slnx -c Debug --no-restore
if errorlevel 1 goto :fail
echo [3/4] Running integration verification...
dotnet run --project tests\Wahib.Tests\Wahib.Tests.csproj -c Debug --no-restore
if errorlevel 1 goto :fail
echo [4/4] Starting Windows application...
dotnet run --project src\Wahib.Desktop\Wahib.Desktop.csproj -c Debug --no-restore
if errorlevel 1 goto :fail
exit /b 0
:fail
echo.
echo FAILED. Read INSTALL_AND_RUN_AR.txt and review the errors above.
pause
exit /b 1
