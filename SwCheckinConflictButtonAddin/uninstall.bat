@echo off
setlocal
set REGASM=%windir%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe
set DLL=%~dp0bin\Release\SwCheckinConflictButtonAddin.dll
if not exist "%DLL%" set DLL=%~dp0bin\Debug\SwCheckinConflictButtonAddin.dll
if not exist "%DLL%" (
  echo 未找到 DLL，请指定已编译的 SwCheckinConflictButtonAddin.dll。
  exit /b 1
)
"%REGASM%" /unregister "%DLL%"
echo 已取消注册。
endlocal
