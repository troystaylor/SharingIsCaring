param(
    [string]$Url = "https://play-lh.googleusercontent.com/mzJpTCsTW_FuR6YqOPaLHrSEVCSJuXzCljdxnCKhVZMcu6EESZBQTCHxMh8slVtnKqo=s512-rp"
)

$ErrorActionPreference = "Stop"

$slackDir = "C:\Users\troytaylor\OneDrive - Microsoft\SharingIsCaring\Cowork Plugins\Slack"
$tmp = Join-Path $env:TEMP "slack-source.png"

Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing -UserAgent "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile($tmp)
Write-Host "Source: $($src.Width)x$($src.Height)"

function Resize-Png {
    param($source, $size, $outPath)
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($source, 0, 0, $size, $size)
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
}

Resize-Png -source $src -size 192 -outPath (Join-Path $slackDir "color.png")
Resize-Png -source $src -size 32 -outPath (Join-Path $slackDir "outline.png")
$src.Dispose()
Remove-Item $tmp

Get-Item (Join-Path $slackDir "color.png"), (Join-Path $slackDir "outline.png") | Format-Table Name, Length
