@echo off
setlocal EnableExtensions
set REGASM=%windir%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe
set INSTALLDIR=%ProgramData%\SwCheckinConflictButtonAddin
set DLL=%INSTALLDIR%\SwCheckinConflictButtonAddin.dll
if not exist "%DLL%" set DLL=%~dp0bin\Release\SwCheckinConflictButtonAddin.dll
if not exist "%DLL%" set DLL=%~dp0bin\Debug\SwCheckinConflictButtonAddin.dll
if not exist "%DLL%" (
  echo 未找到已注册的 DLL。
  pause
  exit /b 1
)
"%REGASM%" /unregister "%DLL%"
echo 已取消注册。
pause
endlocal
