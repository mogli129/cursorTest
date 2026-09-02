@echo off
chcp 936 >nul
setlocal EnableExtensions
set REGASM=%windir%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe
set SRC=%~dp0bin\Release
if not exist "%SRC%\SwCheckinConflictButtonAddin.dll" set SRC=%~dp0bin\Debug
if not exist "%SRC%\SwCheckinConflictButtonAddin.dll" (
  echo [ERROR] SwCheckinConflictButtonAddin.dll not found. Build Release x64 first.
  pause
  exit /b 1
)

set INSTALLDIR=%ProgramData%\SwCheckinConflictButtonAddin
echo Install dir: %INSTALLDIR%
if not exist "%INSTALLDIR%" mkdir "%INSTALLDIR%"
copy /Y "%SRC%\*.dll" "%INSTALLDIR%\" >nul
if exist "%SRC%\SwCheckinConflictButtonAddin.dll.config" copy /Y "%SRC%\SwCheckinConflictButtonAddin.dll.config" "%INSTALLDIR%\" >nul
if exist "%SRC%\THIRD_PARTY_NOTICES.txt" copy /Y "%SRC%\THIRD_PARTY_NOTICES.txt" "%INSTALLDIR%\" >nul

if not exist "%REGASM%" (
  echo [ERROR] regasm not found: %REGASM%
  pause
  exit /b 1
)

echo Registering: %INSTALLDIR%\SwCheckinConflictButtonAddin.dll
"%REGASM%" /codebase "%INSTALLDIR%\SwCheckinConflictButtonAddin.dll"
if errorlevel 1 (
  echo [ERROR] Register failed. Right-click this script and Run as administrator.
  pause
  exit /b 1
)

echo.
echo Register OK.
echo 1. Exit SOLIDWORKS 2022 completely
echo 2. Start SOLIDWORKS again
echo 3. Tools - Add-Ins - enable: Check-in conflict button
echo Log: %TEMP%\SwCheckinConflictButtonAddin.log
echo.
pause
endlocal
