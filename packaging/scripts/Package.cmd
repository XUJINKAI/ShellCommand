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

set "STAGE=%ROOT%\artifacts\ShellCommand11-portable"
set "PUBLISH_APP=%ROOT%\artifacts\publish-app"
set "PUBLISH_BROKER=%ROOT%\artifacts\publish-broker"
set "ZIP=%ROOT%\artifacts\ShellCommand-11.0.0-win-x64.zip"
if exist "%STAGE%" rmdir /S /Q "%STAGE%"
if exist "%PUBLISH_APP%" rmdir /S /Q "%PUBLISH_APP%"
if exist "%PUBLISH_BROKER%" rmdir /S /Q "%PUBLISH_BROKER%"
if exist "%ZIP%" del /Q "%ZIP%"
mkdir "%STAGE%\Assets"

dotnet publish "%ROOT%\src\ShellCommand.App\ShellCommand.App.csproj" -c "%CONFIG%" -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%PUBLISH_APP"
if errorlevel 1 (
  echo App publish failed. 1>&2
  popd
  exit /b 1
)
dotnet publish "%ROOT%\src\ShellCommand.Broker\ShellCommand.Broker.csproj" -c "%CONFIG%" -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%PUBLISH_BROKER"
if errorlevel 1 (
  echo Broker publish failed. 1>&2
  popd
  exit /b 1
)

copy /Y "%PUBLISH_APP%\ShellCommand.exe" "%STAGE%\" >nul
copy /Y "%PUBLISH_BROKER%\ShellCommand.Broker.exe" "%STAGE%\" >nul
if not exist "%ROOT%\src\ShellCommand.Explorer\x64\%CONFIG%\ShellCommand.Explorer.dll" (
  echo Native Explorer DLL was not built. 1>&2
  popd
  exit /b 1
)
copy /Y "%ROOT%\src\ShellCommand.Explorer\x64\%CONFIG%\ShellCommand.Explorer.dll" "%STAGE%\" >nul
copy /Y "%ROOT%\packaging\manifest\AppxManifest.xml" "%STAGE%\" >nul
copy /Y "%ROOT%\docs\screenshot\win10-preview.png" "%STAGE%\Assets\StoreLogo.png" >nul
copy /Y "%ROOT%\docs\screenshot\win10-preview.png" "%STAGE%\Assets\Square150x150Logo.png" >nul
copy /Y "%ROOT%\docs\screenshot\win10-preview.png" "%STAGE%\Assets\Square44x44Logo.png" >nul
copy /Y "%ROOT%\README_cn.md" "%STAGE%\README.md" >nul

where tar.exe >nul 2>&1
if errorlevel 1 (
  echo tar.exe was not found. Windows 11 is required to create the ZIP. 1>&2
  popd
  exit /b 1
)
tar.exe -a -c -f "%ZIP%" -C "%STAGE%" .
if errorlevel 1 (
  echo ZIP creation failed. 1>&2
  popd
  exit /b 1
)
echo Portable ZIP: %ZIP%
popd
exit /b 0
