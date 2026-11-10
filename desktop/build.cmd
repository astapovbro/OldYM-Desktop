@echo off
setlocal
cd /d "%~dp0"
set "MSB=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
if not exist "%MSB%" (
  echo MSBuild .NET Framework 4.x was not found.
  exit /b 1
)
if not exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\Microsoft.WinFX.targets" (
  echo WPF build targets were not found. Install the .NET Framework targeting pack / Windows SDK.
  exit /b 1
)
"%MSB%" OldYM.Wpf.csproj /p:Configuration=Release /p:Platform=x86 /v:minimal
if errorlevel 1 exit /b 1
echo Built: bin\Release\OldYM.exe
