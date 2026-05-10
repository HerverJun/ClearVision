@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "DOTNET_EXE=dotnet"
if defined DOTNET_ROOT (
    if exist "%DOTNET_ROOT%\dotnet.exe" set "DOTNET_EXE=%DOTNET_ROOT%\dotnet.exe"
)

set "CSC_DLL="
if defined DOTNET_ROOT (
    if exist "%DOTNET_ROOT%\sdk\10.0.101\Roslyn\bincore\csc.dll" (
        set "CSC_DLL=%DOTNET_ROOT%\sdk\10.0.101\Roslyn\bincore\csc.dll"
    )
)

if defined CSC_DLL goto run_compiler

set "SDK_LIST=%TEMP%\clearvision-dotnet-sdks-%RANDOM%-%RANDOM%.txt"
"%DOTNET_EXE%" --list-sdks > "%SDK_LIST%" 2>nul

for /f "tokens=2 delims=[]" %%A in ('findstr /B /C:"10.0.101 " "%SDK_LIST%" 2^>nul') do (
    set "SDK_BASE=%%A"
)

del "%SDK_LIST%" >nul 2>nul

if defined SDK_BASE if exist "%SDK_BASE%\10.0.101\Roslyn\bincore\csc.dll" (
    set "CSC_DLL=%SDK_BASE%\10.0.101\Roslyn\bincore\csc.dll"
)

if not defined CSC_DLL (
    echo ClearVision SDK 10.0.101 csc wrapper could not find csc.dll. Install .NET SDK 10.0.101 or set DOTNET_ROOT.
    exit /b 1
)

:run_compiler
"%DOTNET_EXE%" "%CSC_DLL%" %*
exit /b %ERRORLEVEL%
