@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
dotnet run --project tests\Wahib.Tests\Wahib.Tests.csproj -c Debug
if errorlevel 1 echo Some tests FAILED.
pause
