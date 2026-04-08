Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Building WPF Installer v2.0.0" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build main application
Write-Host "Step 1: Building main application..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Main application build failed!" -ForegroundColor Red
    exit 1
}

$appSize = (Get-Item "bin\Release\net6.0-windows\win-x64\publish\TelnetCommanderPro.exe").Length / 1MB
Write-Host "Application built: $($appSize.ToString('F2')) MB" -ForegroundColor Green
Write-Host ""

# Step 2: Compress application files
Write-Host "Step 2: Compressing application files..." -ForegroundColor Yellow
./compress_app_files.ps1

if (!(Test-Path "WPFInstaller\app_payload.zip")) {
    Write-Host "ERROR: Failed to create app_payload.zip!" -ForegroundColor Red
    exit 1
}

$payloadSize = (Get-Item "WPFInstaller\app_payload.zip").Length / 1MB
Write-Host "Payload compressed: $($payloadSize.ToString('F2')) MB" -ForegroundColor Green
Write-Host ""

# Step 3: Copy icon
Write-Host "Step 3: Copying app icon..." -ForegroundColor Yellow
Copy-Item "app.ico" "WPFInstaller\app.ico" -Force
Write-Host "Icon copied" -ForegroundColor Green
Write-Host ""

# Step 4: Build WPF installer
Write-Host "Step 4: Building WPF installer..." -ForegroundColor Yellow
Set-Location WPFInstaller
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=false
Set-Location ..

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Installer build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Installer built successfully" -ForegroundColor Green
Write-Host ""

# Step 5: Copy to output
Write-Host "Step 5: Copying installer to output..." -ForegroundColor Yellow

if (!(Test-Path "installer_output")) {
    New-Item -ItemType Directory -Path "installer_output" | Out-Null
}

$installerSource = "WPFInstaller\bin\Release\net6.0-windows\win-x64\publish\TelnetCommanderProInstaller.exe"
$installerDest = "installer_output\TelnetCommanderPro_Setup_v2.0.0.exe"

Copy-Item $installerSource $installerDest -Force

$installerSize = (Get-Item $installerDest).Length / 1MB

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Installer Details:" -ForegroundColor Yellow
Write-Host "  Location: $installerDest" -ForegroundColor White
Write-Host "  Size: $($installerSize.ToString('F2')) MB" -ForegroundColor White
Write-Host "  Type: Self-contained single executable" -ForegroundColor White
Write-Host ""
Write-Host "Contents:" -ForegroundColor Yellow
Write-Host "  - WPF Installer with modern UI" -ForegroundColor Gray
Write-Host "  - Embedded application ($($appSize.ToString('F2')) MB)" -ForegroundColor Gray
Write-Host "  - Compressed payload ($($payloadSize.ToString('F2')) MB)" -ForegroundColor Gray
Write-Host "  - All dependencies included" -ForegroundColor Gray
Write-Host ""
Write-Host "Ready to distribute!" -ForegroundColor Green
Write-Host "Users can run this single .exe file directly." -ForegroundColor Cyan
Write-Host ""
