@echo off
setlocal
cd /d "%~dp0"
dotnet run --project "tests\XerathAssistant.CoreTests\XerathAssistant.CoreTests.csproj"
pause
