@echo off
setlocal
set REGASM=%windir%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe
set DLL=%~dp0bin\Release\SwCheckinConflictButtonAddin.dll
if not exist "%DLL%" set DLL=%~dp0bin\Debug\SwCheckinConflictButtonAddin.dll
if not exist "%DLL%" (
  echo 未找到 SwCheckinConflictButtonAddin.dll，请先用 Visual Studio 以 x64 / Release 编译。
  exit /b 1
)
echo 正在注册: %DLL%
"%REGASM%" /codebase "%DLL%"
if errorlevel 1 (
  echo 注册失败。请右键以管理员身份运行本脚本。
  exit /b 1
)
echo 注册成功。请重启 SOLIDWORKS 2022，并在 工具 - 插件 中勾选“检入冲突窗口按钮”。
endlocal
