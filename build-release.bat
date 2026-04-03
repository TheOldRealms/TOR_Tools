@echo off
echo Building TORTools Release...
cd /d "%~dp0"

echo.
echo Creating self-contained release build...
dotnet publish src/TORTools.App/TORTools.App.csproj -c Release -r win-x64 --self-contained true -o publish

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build successful!
    echo.
    echo Output location: %~dp0publish\
    echo Run: publish\TORTools.App.exe
    echo.
) else (
    echo.
    echo Build failed with error code %ERRORLEVEL%
)

pause
