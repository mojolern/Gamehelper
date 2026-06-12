@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0rebuild-and-publish.ps1" %*
set EXITCODE=%ERRORLEVEL%
echo.
if %EXITCODE% neq 0 (
    echo FEHLER - Exit-Code %EXITCODE%
) else (
    echo Erfolgreich abgeschlossen.
)
echo.
pause
exit /b %EXITCODE%
