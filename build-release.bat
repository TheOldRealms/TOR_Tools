@echo off
setlocal enabledelayedexpansion
echo ========================================
echo TOR Tools Release Builder
echo ========================================
cd /d "%~dp0"

echo.
echo [1/3] Cleaning previous build...
if exist "release" rmdir /s /q "release"

echo.
echo [2/3] Building self-contained release...
dotnet publish src/TORTools.App/TORTools.App.csproj -c Release -r win-x64 --self-contained true -o release

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo BUILD FAILED with error code %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] Copying schemas...
xcopy "schemas" "release\schemas\" /E /I /Y /Q

echo.
echo ========================================
echo BUILD COMPLETE!
echo ========================================
echo.
echo Output folder: %~dp0release\
echo.
echo To run: double-click TOR_Tools.bat or release\TORTools.App.exe
echo.
echo Don't forget to commit the release\ folder!
echo.
pause
