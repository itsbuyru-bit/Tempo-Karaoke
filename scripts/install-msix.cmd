@echo off
rem Установка MSIX на ПК: доверяет сертификат и ставит пакет.
rem Положите рядом .msix и certs\TempoKaraoke.cer (или передайте пути аргументами).
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-msix.ps1" %*
set EXITCODE=%ERRORLEVEL%
if not "%EXITCODE%"=="0" pause
exit /b %EXITCODE%
