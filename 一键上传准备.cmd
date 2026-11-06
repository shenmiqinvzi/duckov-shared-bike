@echo off
setlocal
cd /d "%~dp0"
echo ================================================================
echo  Duckov Shared Bike - build, install, workshop preparation
echo ================================================================
echo.

echo [1/4] Running domain + mod tests...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Test.ps1"
if errorlevel 1 goto :fail

echo.
echo [2/4] Building the package...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build.ps1"
if errorlevel 1 goto :fail

echo.
echo [3/4] Installing into the game (Duckov must be closed)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install.ps1"
if errorlevel 1 goto :fail

echo.
echo [4/4] Preparing the folder for the in-game Workshop upload...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Prepare-Workshop.ps1"
if errorlevel 1 goto :fail

echo.
echo ================================================================
echo  All four steps finished.
echo  Next: start Duckov with Steam online, open the Mods menu,
echo        find the Shared Bike entry and press its upload button.
echo ================================================================
pause
exit /b 0

:fail
echo.
echo ================================================================
echo  A step failed - read the messages above this line.
echo  Most common cause: Duckov is still running (close it first).
echo ================================================================
pause
exit /b 1
