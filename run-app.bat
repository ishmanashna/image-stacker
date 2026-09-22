@echo off
setlocal
cd /d "%~dp0"

set "PUBLISHED=dist\app\ImageStacker.App.exe"
set "DEV=src\ImageStacker.App\bin\Release\net8.0-windows\ImageStacker.App.exe"

if exist "%PUBLISHED%" (
  start "" "%PUBLISHED%"
  exit /b 0
)

if exist "%DEV%" (
  start "" "%DEV%"
  exit /b 0
)

echo Building Image Stacker (first run)...
dotnet build "src\ImageStacker.App\ImageStacker.App.csproj" -c Release
if errorlevel 1 (
  echo.
  echo Build failed. Is the .NET 8 SDK installed?
  pause
  exit /b 1
)

if exist "%DEV%" (
  start "" "%DEV%"
  exit /b 0
)

echo Could not find ImageStacker.App.exe after build.
pause
exit /b 1
