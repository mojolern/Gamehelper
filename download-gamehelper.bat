@echo off
setlocal
cd /d "%~dp0"

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=%~dp0GameHelper"

echo Zielordner: %TARGET%
echo.

if exist "%~dp0GameHelperDownloader.exe" (
    "%~dp0GameHelperDownloader.exe" "%TARGET%"
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\download-gamehelper.ps1" -TargetDir "%TARGET%"
)
set EXITCODE=%ERRORLEVEL%

echo.
if %EXITCODE% neq 0 (
    echo Download fehlgeschlagen - Exit-Code %EXITCODE%
) else (
    echo Download erfolgreich.
)
echo.
pause
exit /b %EXITCODE%
