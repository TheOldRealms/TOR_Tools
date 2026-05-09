@echo off
setlocal enabledelayedexpansion
echo ========================================
echo TORTools Release Builder
echo ========================================
cd /d "%~dp0"

echo.
echo [1/4] Cleaning previous build...
if exist "dist" rmdir /s /q "dist"

echo.
echo [2/4] Building self-contained release...
dotnet publish src/TORTools.App/TORTools.App.csproj -c Release -r win-x64 --self-contained true -o dist\TORTools

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo BUILD FAILED with error code %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/4] Copying schemas and README...
xcopy "schemas" "dist\TORTools\schemas\" /E /I /Y /Q

:: Create README
(
echo TOR XML Editor ^(TORTools^)
echo =========================
echo.
echo A desktop application for editing XML data files of "The Old Realms"
echo Mount ^& Blade II: Bannerlord mod.
echo.
echo.
echo INSTALLATION
echo ------------
echo 1. Extract this entire folder to:
echo.
echo    [Your Bannerlord Install]\Modules\TORTools\
echo.
echo    Example:
echo    C:\Program Files ^(x86^)\Steam\steamapps\common\Mount ^& Blade II Bannerlord\Modules\TORTools\
echo.
echo 2. The folder structure should look like:
echo.
echo    Modules\
echo      TOR_Core\
echo      TOR_Armory\
echo      TOR_Environment\
echo      TORTools\           ^<-- This folder
echo        TORTools.App.exe
echo        schemas\
echo        *.dll
echo.
echo.
echo RUNNING
echo -------
echo Double-click TORTools.App.exe
echo.
echo The app will automatically detect the TOR mod folders by looking for
echo the "Modules" directory in the path above it.
echo.
echo.
echo SUPPORT
echo -------
echo Report issues to the TOR development team.
) > "dist\TORTools\README.txt"

echo.
echo [4/4] Creating distribution zip...
powershell -Command "Compress-Archive -Path 'dist\TORTools' -DestinationPath 'dist\TORTools.zip' -Force"

echo.
echo ========================================
echo BUILD COMPLETE!
echo ========================================
echo.
echo Output folder: %~dp0dist\TORTools\
echo Distribution:  %~dp0dist\TORTools.zip
echo.
echo To test locally, run: dist\TORTools\TORTools.App.exe
echo.
pause
