@echo off
setlocal EnableExtensions

set "CONFIG=%~1"
if not defined CONFIG set "CONFIG=Release"
if /I not "%CONFIG%"=="Debug" if /I not "%CONFIG%"=="Release" (
  echo Usage: Test.cmd [Debug^|Release] 1>&2
  exit /b 2
)

pushd "%~dp0..\.." || exit /b 1
set "ROOT=%CD%"
dotnet test "%ROOT%\tests\ShellCommand.Core.Tests\ShellCommand.Core.Tests.csproj" -c "%CONFIG%" --no-restore
if errorlevel 1 (
  popd
  exit /b 1
)
dotnet test "%ROOT%\tests\ShellCommand.Config.Tests\ShellCommand.Config.Tests.csproj" -c "%CONFIG%" --no-restore
if errorlevel 1 (
  popd
  exit /b 1
)
dotnet test "%ROOT%\tests\ShellCommand.Broker.Tests\ShellCommand.Broker.Tests.csproj" -c "%CONFIG%" --no-restore
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
