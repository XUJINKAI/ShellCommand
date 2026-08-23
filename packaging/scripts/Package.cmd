@echo off
setlocal EnableExtensions

set "CONFIG=%~1"
if not defined CONFIG set "CONFIG=Release"
if /I not "%CONFIG%"=="Debug" if /I not "%CONFIG%"=="Release" (
  echo Usage: Package.cmd [Debug^|Release] 1>&2
  exit /b 2
)

pushd "%~dp0..\.." || exit /b 1
set "ROOT=%CD%"
call "%ROOT%\packaging\scripts\Build.cmd" "%CONFIG%"
if errorlevel 1 (
  popd
  exit /b 1
)

set "STAGE=%ROOT%\artifacts\.package-staging"
set "WORK=%ROOT%\artifacts\.package-working"
set "APP_WORK_DIR=%WORK%\app"
set "BROKER_WORK_DIR=%WORK%\broker"
set "ZIP=%ROOT%\artifacts\ShellCommand-11.0.0-win-x64.zip"
if exist "%STAGE%" rmdir /S /Q "%STAGE%"
if exist "%WORK%" rmdir /S /Q "%WORK%"
if exist "%ZIP%" del /Q "%ZIP%"
rem Remove names used by the old broken packaging flow.
if exist "%ROOT%\artifacts\ShellCommand11-portable" rmdir /S /Q "%ROOT%\artifacts\ShellCommand11-portable"
if exist "%ROOT%\artifacts\publish-app" rmdir /S /Q "%ROOT%\artifacts\publish-app"
if exist "%ROOT%\artifacts\publish-broker" rmdir /S /Q "%ROOT%\artifacts\publish-broker"
mkdir "%STAGE%\Assets"
mkdir "%WORK%"

dotnet publish "%ROOT%\src\ShellCommand.App\ShellCommand.App.csproj" -c "%CONFIG%" -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%APP_WORK_DIR%"
if errorlevel 1 (
  echo App publish failed. 1>&2
  goto :package_failed
)
dotnet publish "%ROOT%\src\ShellCommand.Broker\ShellCommand.Broker.csproj" -c "%CONFIG%" -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%BROKER_WORK_DIR%"
if errorlevel 1 (
  echo Broker publish failed. 1>&2
  goto :package_failed
)

if not exist "%APP_WORK_DIR%\ShellCommand.exe" goto :package_failed
if not exist "%BROKER_WORK_DIR%\ShellCommand.Broker.exe" goto :package_failed
copy /Y "%APP_WORK_DIR%\ShellCommand.exe" "%STAGE%\" >nul
copy /Y "%BROKER_WORK_DIR%\ShellCommand.Broker.exe" "%STAGE%\" >nul
if not exist "%ROOT%\x64\%CONFIG%\ShellCommand.Explorer.dll" (
  echo Native Explorer DLL was not built. 1>&2
  goto :package_failed
)
copy /Y "%ROOT%\x64\%CONFIG%\ShellCommand.Explorer.dll" "%STAGE%\" >nul
copy /Y "%ROOT%\packaging\manifest\AppxManifest.xml" "%STAGE%\" >nul
copy /Y "%ROOT%\docs\screenshot\win10-preview.png" "%STAGE%\Assets\StoreLogo.png" >nul
copy /Y "%ROOT%\docs\screenshot\win10-preview.png" "%STAGE%\Assets\Square150x150Logo.png" >nul
copy /Y "%ROOT%\docs\screenshot\win10-preview.png" "%STAGE%\Assets\Square44x44Logo.png" >nul
copy /Y "%ROOT%\README_cn.md" "%STAGE%\README.md" >nul
copy /Y "%ROOT%\packaging\scripts\Uninstall.cmd" "%STAGE%\Uninstall.cmd" >nul

where powershell.exe >nul 2>&1
if errorlevel 1 (
  echo powershell.exe was not found. Windows is required to create the ZIP. 1>&2
  goto :package_failed
)
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%ROOT%\packaging\scripts\Create-Zip.ps1" -SourceDirectory "%STAGE%" -Destination "%ZIP%"
if errorlevel 1 (
  echo ZIP creation failed. 1>&2
  goto :package_failed
)
echo Portable ZIP: %ZIP%
set "RESULT=0"
goto :cleanup

:package_failed
if not defined RESULT set "RESULT=1"
echo Portable package creation failed. 1>&2

:cleanup
if exist "%STAGE%" rmdir /S /Q "%STAGE%"
if exist "%WORK%" rmdir /S /Q "%WORK%"
popd
exit /b %RESULT%
