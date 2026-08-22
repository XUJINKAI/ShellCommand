@echo off
setlocal EnableExtensions

set "CONFIG=%~1"
if not defined CONFIG set "CONFIG=Release"
if /I not "%CONFIG%"=="Debug" if /I not "%CONFIG%"=="Release" (
  echo Usage: Build.cmd [Debug^|Release] 1>&2
  exit /b 2
)

pushd "%~dp0..\.." || exit /b 1
set "ROOT=%CD%"
set "ARTIFACTS=%ROOT%\artifacts\%CONFIG%"
if not exist "%ARTIFACTS%" mkdir "%ARTIFACTS%"

call "%ROOT%\packaging\scripts\Build-Solution.cmd" "%CONFIG%"
if errorlevel 1 (
  echo Solution build failed. 1>&2
  popd
  exit /b 1
)

xcopy /E /I /Y "%ROOT%\src\ShellCommand.App\bin\%CONFIG%\net10.0-windows\*" "%ARTIFACTS%\" >nul
if errorlevel 1 (
  echo Could not stage the App output. 1>&2
  popd
  exit /b 1
)
xcopy /E /I /Y "%ROOT%\src\ShellCommand.Broker\bin\%CONFIG%\net10.0\*" "%ARTIFACTS%\" >nul
if errorlevel 1 (
  echo Could not stage the Broker output. 1>&2
  popd
  exit /b 1
)

if exist "%ROOT%\src\ShellCommand.Explorer\x64\%CONFIG%\ShellCommand.Explorer.dll" copy /Y "%ROOT%\src\ShellCommand.Explorer\x64\%CONFIG%\ShellCommand.Explorer.dll" "%ARTIFACTS%\" >nul
echo Build output: %ARTIFACTS%
popd
exit /b 0
