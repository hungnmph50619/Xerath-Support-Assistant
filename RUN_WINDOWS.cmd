@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Khong tim thay .NET SDK. Cai .NET 8 SDK va thu lai.
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
dotnet run --project "src\XerathAssistant.Desktop\XerathAssistant.Desktop.csproj"
if errorlevel 1 pause
