@echo off
set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%"
dotnet build "ClearVision.Product\src\ClearVision.Product.Application\ClearVision.Product.Application.csproj" > "%SCRIPT_DIR%build_output.txt"
set "EXIT_CODE=%ERRORLEVEL%"
popd
exit /b %EXIT_CODE%
