@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
 echo Install Microsoft .NET Framework 4 Full first.
 exit /b 1
)
if exist bin (
 set "BACKUP=build-backups\bin-%RANDOM%-%RANDOM%"
 call :backup
 if errorlevel 1 exit /b 1
)
if not exist bin\assets mkdir bin\assets
"%CSC%" /nologo /langversion:4 /target:winexe /platform:x86 /optimize+ /win32icon:assets\oldym.ico /out:bin\OldYM.Desktop.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:lib\BouncyCastle.Crypto.dll src\*.cs
if errorlevel 1 exit /b 1
copy /y lib\BouncyCastle.Crypto.dll bin\ >nul
copy /y OldYM.Desktop.exe.config bin\ >nul
copy /y assets\roots.pem bin\assets\ >nul
copy /y assets\roots-source.txt bin\assets\ >nul
copy /y assets\oldym.ico bin\assets\ >nul
echo Built: bin\OldYM.Desktop.exe
exit /b 0
:backup
xcopy bin "%BACKUP%\" /e /i /h /y >nul
exit /b %errorlevel%
