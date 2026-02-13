Write-Host "Starting repository cleanup..." -ForegroundColor Cyan

# 1. Remove bin and obj directories
Write-Host "Removing bin and obj directories..." -ForegroundColor Yellow
Get-ChildItem -Path . -Include bin,obj -Recurse -Force | ForEach-Object {
    if ($_.PSIsContainer) {
        Write-Host "Deleting folder: $($_.FullName)" -ForegroundColor Gray
        Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# 2. Remove specific large files and build artifacts
$filesToRemove = @(
    "node.exe",
    "onnxruntime.dll",
    "OpenCvSharpExtern.dll",
    "libonnxruntime.dylib",
    "onnxruntime.aar",
    "*.log",
    "*.pdb"
)

Write-Host "Removing specific binary and log files..." -ForegroundColor Yellow
foreach ($pattern in $filesToRemove) {
    Get-ChildItem -Path . -Include $pattern -Recurse -File | ForEach-Object {
        Write-Host "Deleting file: $($_.FullName)" -ForegroundColor Gray
        Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue
    }
}

# 3. Clean empty directories (optional, but good for cleanup)
# Write-Host "Cleaning empty directories..." -ForegroundColor Yellow
# Get-ChildItem -Path . -Recurse -Directory | Where-Object { $_.GetFileSystemInfos().Count -eq 0 } | Remove-Item -Force

Write-Host "Cleanup complete!" -ForegroundColor Green
Write-Host "Please check 'git status' to ensure no important files were deleted (though ignored files should be safe)."
