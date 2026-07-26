# Downloads ffmpeg.exe (essentials build) and places it in tools/ for bundling
$url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
$zip = "$env:TEMP\ffmpeg-release-essentials.zip"
Write-Host "Downloading ffmpeg from gyan.dev..." -ForegroundColor Cyan
try {
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing -TimeoutSec 300
    Write-Host "Extracting ffmpeg.exe..." -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    $entry = $archive.Entries | Where-Object { $_.Name -eq "ffmpeg.exe" } | Select-Object -First 1
    if ($entry) {
        $dest = Join-Path $PSScriptRoot "ffmpeg.exe"
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dest, $true)
        Write-Host "Saved to $dest" -ForegroundColor Green
    }
    $archive.Dispose()
    Remove-Item $zip -Force
} catch {
    Write-Host "Download failed: $_" -ForegroundColor Red
    Write-Host "Please download manually from https://www.gyan.dev/ffmpeg/builds/ and place ffmpeg.exe in the tools/ folder." -ForegroundColor Yellow
}
