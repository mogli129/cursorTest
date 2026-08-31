@echo off
setlocal EnableExtensions
set REGASM=%windir%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe
set SRC=%~dp0bin\Release
if not exist "%SRC%\SwCheckinConflictButtonAddin.dll" set SRC=%~dp0bin\Debug
if not exist "%SRC%\SwCheckinConflictButtonAddin.dll" (
  echo 未找到 SwCheckinConflictButtonAddin.dll，请先用 Visual Studio 以 x64 / Release 编译。
  pause
  exit /b 1
)

set INSTALLDIR=%ProgramData%\SwCheckinConflictButtonAddin
echo 安装目录: %INSTALLDIR%
if not exist "%INSTALLDIR%" mkdir "%INSTALLDIR%"
copy /Y "%SRC%\*.dll" "%INSTALLDIR%\" >nul
if exist "%SRC%\SwCheckinConflictButtonAddin.dll.config" copy /Y "%SRC%\SwCheckinConflictButtonAddin.dll.config" "%INSTALLDIR%\" >nul

if not exist "%REGASM%" (
  echo 找不到 64 位 regasm: %REGASM%
  pause
  exit /b 1
)

echo 正在注册: %INSTALLDIR%\SwCheckinConflictButtonAddin.dll
"%REGASM%" /codebase "%INSTALLDIR%\SwCheckinConflictButtonAddin.dll"
if errorlevel 1 (
  echo 注册失败。请右键“以管理员身份运行”本脚本。
  pause
  exit /b 1
)

echo.
echo 注册成功。请完全退出并重新打开 SOLIDWORKS 2022，
echo 然后在 工具 - 插件 中勾选“检入冲突窗口按钮”。
echo 日志: %TEMP%\SwCheckinConflictButtonAddin.log
echo.
pause
endlocal
