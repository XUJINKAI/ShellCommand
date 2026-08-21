@echo off
setlocal
call "%ProgramFiles%\Microsoft Visual Studio\18\Enterprise\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b %errorlevel%
msbuild "%~dp0..\..\src\ShellCommand.Explorer\ShellCommand.Explorer.vcxproj" /p:Configuration=%1 /p:Platform=x64 /m
