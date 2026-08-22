@echo off
setlocal EnableExtensions

set "CONFIG=%~1"
if not defined CONFIG set "CONFIG=Release"
if /I not "%CONFIG%"=="Debug" if /I not "%CONFIG%"=="Release" (
  echo Usage: Build-Solution.cmd [Debug^|Release] 1>&2
  exit /b 2
)

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "VSINSTALL="
if exist "%VSWHERE%" for /f "usebackq delims=" %%V in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%V"
if not defined VSINSTALL if exist "%ProgramFiles%\Microsoft Visual Studio\18\Enterprise\VC\Auxiliary\Build\vcvars64.bat" set "VSINSTALL=%ProgramFiles%\Microsoft Visual Studio\18\Enterprise"
if not defined VSINSTALL (
  echo Visual Studio C++ tools were not found. Install the Desktop development with C++ workload. 1>&2
  exit /b 1
)
if not exist "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" (
  echo Visual Studio C++ environment script is missing: "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" 1>&2
  exit /b 1
)
if not exist "%VSINSTALL%\VC\Auxiliary\Build\vcvarsall.bat" (
  echo Visual Studio C++ environment script is incomplete: "%VSINSTALL%\VC\Auxiliary\Build\vcvarsall.bat" 1>&2
  exit /b 1
)
call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b %errorlevel%

pushd "%~dp0..\.." || exit /b 1
msbuild ShellCommand.sln /p:Configuration=%CONFIG% /p:Platform=x64 /m
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
