@echo off
echo ==========================================
echo  ClearVision Publish Script
echo ==========================================
echo.

set VERSION=1.0.0
set SOLUTION_DIR=%~dp0..
set PUBLISH_DIR=%SOLUTION_DIR%\..\..\publish
set DESKTOP_PROJ=%SOLUTION_DIR%\src\Acme.Product.Desktop\Acme.Product.Desktop.csproj
set POWERSHELL_PATH=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe

echo [1/4] Cleaning old publish files...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
mkdir "%PUBLISH_DIR%"

echo [2/4] Publishing Release build...
dotnet publish "%DESKTOP_PROJ%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%PUBLISH_DIR%\ClearVision-%VERSION%"

if errorlevel 1 (
  echo [ERROR] Publish failed!
  pause
  exit /b 1
)

echo [3/4] Creating launcher script...
(
  echo @echo off
  echo chcp 65001 ^>nul
  echo echo Starting ClearVision...
  echo start "" "%%~dp0Acme.Product.Desktop.exe"
) > "%PUBLISH_DIR%\ClearVision-%VERSION%\Launch ClearVision.bat"

echo [4/4] Creating ZIP archive...

REM Try using PowerShell with absolute path
if exist "%POWERSHELL_PATH%" (
  "%POWERSHELL_PATH%" -NoProfile -Command "Compress-Archive -Path '%PUBLISH_DIR%\ClearVision-%VERSION%\*' -DestinationPath '%PUBLISH_DIR%\ClearVision-%VERSION%-win-x64.zip' -Force"
) else (
  REM Fallback to tar if PowerShell is not found (Windows 10 1803+)
  echo [WARN] PowerShell not found at expected path. Trying tar...
  tar -a -c -f "%PUBLISH_DIR%\ClearVision-%VERSION%-win-x64.zip" -C "%PUBLISH_DIR%\ClearVision-%VERSION%" *
)

if errorlevel 1 (
  echo [ERROR] ZIP archive creation failed!
  pause
  exit /b 1
)

echo.
echo ==========================================
echo  Publish complete!
echo  Output: %PUBLISH_DIR%
echo  Version: %VERSION%
echo ==========================================
echo.
pause