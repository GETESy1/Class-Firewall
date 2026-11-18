@echo off
setlocal
cd /d "%~dp0"

rem ============================================================
rem  Class Firewall - one-click publish (WinUI 3 / Windows App SDK)
rem
rem  WinUI 3 needs native DLLs next to the exe, so a real single-file
rem  build is not possible. We publish a self-contained folder instead:
rem  the target PC does not need .NET or the Windows App Runtime.
rem  Copy the whole publish\win-x64 folder.
rem ============================================================

set "OUT=publish\win-x64"

echo [1/2] Cleaning old output...
if exist "%OUT%" rmdir /s /q "%OUT%"

echo [2/2] Publishing Release / win-x64 / self-contained...
dotnet publish ClassFirewall.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o "%OUT%"

if errorlevel 1 goto fail

echo.
echo ============================================================
echo  Done:  %~dp0%OUT%
echo  Entry: %OUT%\ClassFirewall.exe
echo.
echo  Keep the whole folder together - the DLLs are required.
echo ============================================================
goto end

:fail
echo.
echo *** Publish FAILED - see the errors above ***
if "%CFW_NOPAUSE%"=="" pause

:end
endlocal
