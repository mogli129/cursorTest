@echo off
chcp 936 >nul
setlocal EnableExtensions
set REGASM=%windir%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe
set INSTALLDIR=%ProgramData%\SwCheckinConflictButtonAddin
set DLL=%INSTALLDIR%\SwCheckinConflictButtonAddin.dll
if not exist "%DLL%" set DLL=%~dp0bin\Release\SwCheckinConflictButtonAddin.dll
if not exist "%DLL%" set DLL=%~dp0bin\Debug\SwCheckinConflictButtonAddin.dll
if not exist "%DLL%" (
  echo [ERROR] DLL not found.
  pause
  exit /b 1
)
"%REGASM%" /unregister "%DLL%"
echo Unregister OK.
pause
endlocal
