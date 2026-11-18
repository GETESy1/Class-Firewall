@echo off
setlocal
cd /d "%~dp0"

rem ============================================================
rem  Class Firewall - publish script (WinUI 3 / Windows App SDK)
rem
rem  Default: SINGLE-FILE  ->  publish\ClassFirewall.exe
rem  Optional: publish.bat folder  ->  self-contained folder build
rem
rem  Single-file needs (already set in ClassFirewall.csproj):
rem    EnableMsixTooling=true                      embeds resources.pri
rem    WindowsPackageType=None                     unpackaged only
rem    WindowsAppSdkUndockedRegFreeWinRTInitialize sets the base dir
rem                                                before WinRT activation
rem    IncludeAllContentForSelfExtract=true        DLL SxS redirection
rem
rem  The exe unpacks itself to %TEMP%\.net\ClassFirewall\ on first run,
rem  so the very first start is a bit slower.
rem ============================================================

set "OUT=publish"

echo [1/2] Cleaning old output...
if exist "%OUT%" rmdir /s /q "%OUT%"

if /i "%~1"=="folder" goto folder

echo [2/2] Publishing SINGLE FILE (Release / win-x64 / self-contained)...
dotnet publish ClassFirewall.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o "%OUT%"
if errorlevel 1 goto fail

echo.
echo ============================================================
echo  Done:  %~dp0%OUT%\ClassFirewall.exe
echo.
echo  One exe, nothing else needed on the target PC.
echo ============================================================
goto end

:folder
echo [2/2] Publishing FOLDER build (Release / win-x64 / self-contained)...
dotnet publish ClassFirewall.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:PublishSingleFile=false -o "%OUT%\win-x64"
if errorlevel 1 goto fail

echo.
echo ============================================================
echo  Done:  %~dp0%OUT%\win-x64\
echo  Entry: publish\win-x64\ClassFirewall.exe
echo  Keep the whole folder together.
echo ============================================================
goto end

:fail
echo.
echo *** Publish FAILED - see the errors above ***
if "%CFW_NOPAUSE%"=="" pause

:end
endlocal
