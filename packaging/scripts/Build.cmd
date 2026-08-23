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
set "APP_OUTPUT=%ROOT%\src\ShellCommand.App\bin\%CONFIG%\net10.0-windows"
set "BROKER_OUTPUT=%ROOT%\src\ShellCommand.Broker\bin\%CONFIG%\net10.0"
set "EXPLORER_OUTPUT=%ROOT%\x64\%CONFIG%\ShellCommand.Explorer.dll"

call "%ROOT%\packaging\scripts\Build-Solution.cmd" "%CONFIG%"
if errorlevel 1 (
  echo Solution build failed. 1>&2
  popd
  exit /b 1
)

if exist "%ARTIFACTS%" rmdir /S /Q "%ARTIFACTS%"
mkdir "%ARTIFACTS%\Assets"

rem Copy only files from the output roots. A recursive copy would leak a stale
rem publish\win-x64 runtime directory into the developer package.
for %%F in ("%APP_OUTPUT%\*") do if not exist "%%~fF\" copy /Y "%%~fF" "%ARTIFACTS%\" >nul
for %%F in ("%BROKER_OUTPUT%\*") do if not exist "%%~fF\" copy /Y "%%~fF" "%ARTIFACTS%\" >nul
copy /Y "%EXPLORER_OUTPUT%" "%ARTIFACTS%\" >nul
copy /Y "%ROOT%\packaging\manifest\AppxManifest.xml" "%ARTIFACTS%\" >nul
copy /Y "%ROOT%\docs\screenshot\win10-preview.png" "%ARTIFACTS%\Assets\StoreLogo.png" >nul
copy /Y "%ROOT%\docs\screenshot\win10-preview.png" "%ARTIFACTS%\Assets\Square150x150Logo.png" >nul
copy /Y "%ROOT%\docs\screenshot\win10-preview.png" "%ARTIFACTS%\Assets\Square44x44Logo.png" >nul
copy /Y "%ROOT%\README_cn.md" "%ARTIFACTS%\README.md" >nul
copy /Y "%ROOT%\packaging\scripts\Uninstall.cmd" "%ARTIFACTS%\Uninstall.cmd" >nul

if not exist "%ARTIFACTS%\ShellCommand.exe" goto :stage_failed
if not exist "%ARTIFACTS%\ShellCommand.Broker.exe" goto :stage_failed
if not exist "%ARTIFACTS%\ShellCommand.Explorer.dll" goto :stage_failed
if not exist "%ARTIFACTS%\AppxManifest.xml" goto :stage_failed
echo Build output: %ARTIFACTS%
popd
exit /b 0

:stage_failed
echo Developer package is incomplete. 1>&2
popd
exit /b 1
