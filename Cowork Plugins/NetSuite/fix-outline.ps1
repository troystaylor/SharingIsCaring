# Rebuild outline.png as a pure white silhouette on transparent background.
# Teams Dev Portal requires the outline icon to have only white (or transparent)
# pixels — colored antialiased edges trigger "outline is not transparent" errors.

param(
    [string]$Source = "C:\Users\troytaylor\OneDrive - Microsoft\SharingIsCaring\Cowork Plugins\NetSuite\color.png",
    [string]$Out    = "C:\Users\troytaylor\OneDrive - Microsoft\SharingIsCaring\Cowork Plugins\NetSuite\outline.png",
    [int]$Size      = 32
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile($Source)
Write-Host "Source: $($src.Width)x$($src.Height)"

$resized = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($resized)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)
$g.DrawImage($src, 0, 0, $Size, $Size)
$g.Dispose()
$src.Dispose()

$bmpOut = New-Object -TypeName 'System.Drawing.Bitmap' -ArgumentList @($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

$alphaThreshold = 8

for ($y = 0; $y -lt $Size; $y++) {
    for ($x = 0; $x -lt $Size; $x++) {
        $p = $resized.GetPixel($x, $y)
        if ($p.A -le $alphaThreshold) {
            $bmpOut.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
        } else {
            $bmpOut.SetPixel($x, $y, [System.Drawing.Color]::FromArgb([int]$p.A, 255, 255, 255))
        }
    }
}

$resized.Dispose()
$bmpOut.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmpOut.Dispose()

Write-Host "Wrote $Out ($Size x $Size, white-on-transparent)" -ForegroundColor Green

$verify = [System.Drawing.Image]::FromFile($Out)
$bmp = [System.Drawing.Bitmap]$verify
$badPixels = 0
for ($y = 0; $y -lt $bmp.Height; $y++) {
    for ($x = 0; $x -lt $bmp.Width; $x++) {
        $p = $bmp.GetPixel($x, $y)
        if ($p.A -gt 0 -and ($p.R -ne 255 -or $p.G -ne 255 -or $p.B -ne 255)) {
            $badPixels++
        }
    }
}
$verify.Dispose()
Write-Host ("Non-white opaque pixels: {0} (must be 0)" -f $badPixels)
