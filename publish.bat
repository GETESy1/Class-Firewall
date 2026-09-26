@echo off
chcp 65001 >nul
setlocal

echo =====================================
echo   Class Firewall - 单文件发布
echo   自包含模式，目标电脑无需预装 .NET 8
echo =====================================
echo.

dotnet publish -c Release

if errorlevel 1 (
    echo.
    echo [失败] 发布未成功，请检查上面的错误信息。
    pause
    exit /b 1
)

set "EXE=bin\Release\net8.0-windows\win-x64\publish\ClassFirewall.exe"

echo.
echo =====================================
echo  发布完成！exe 位置：
echo    %EXE%
echo.
for %%F in ("%EXE%") do echo  文件大小：%%~zF 字节
echo =====================================
pause
