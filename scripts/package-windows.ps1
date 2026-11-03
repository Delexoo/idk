$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$issc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $issc)) { $issc = "${env:ProgramFiles}\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $issc)) { $issc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $issc)) { throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php" }

$dist = Join-Path $root "dist-setup"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$stageName = $null
$stage = $null
foreach ($name in @("stage", "ship", "payload")) {
  $candidate = Join-Path $dist $name
  if (Test-Path $candidate) { cmd /c "rmdir /s /q `"$candidate`"" }
  if (-not (Test-Path $candidate)) {
    $stageName = $name
    $stage = $candidate
    break
  }
}
if (-not $stage) { throw "Could not clear the installer payload. Close anything using dist-setup and try again." }

dotnet publish (Join-Path $root "Idk\Idk.csproj") -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=false -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

Get-ChildItem $stage -Force -Include ".env", "billing.env" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
if (-not (Test-Path (Join-Path $stage "idk.exe"))) { throw "The published app has no idk.exe." }

& $issc "/DPayload=$stageName" (Join-Path $root "installer\idk.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }

$built = Get-ChildItem $dist -Filter "idk-Setup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $built) { throw "The setup file was not written." }
$downloadDir = Join-Path $root "website\download"
New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null
Copy-Item $built.FullName (Join-Path $downloadDir "idk-Setup.exe") -Force
Write-Host "Installer written to $($built.FullName)"
Get-Item $built.FullName, (Join-Path $downloadDir "idk-Setup.exe") | Select-Object Name, Length, FullName
