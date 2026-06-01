param(
    [string]$Url = "https://scontent.fagc1-1.fna.fbcdn.net/v/t39.30808-1/425292937_771493275024718_2142402868898879247_n.jpg?stp=dst-jpg_s200x200_tt6&_nc_cat=110&ccb=1-7&_nc_sid=2d3e12&_nc_ohc=HiBHvb_4xzUQ7kNvwHaXzS9&_nc_oc=AdpdDhdOcqImsyZjqfRPQnT4mYFESADWLghaxAPDLvm_SG-JsHwxuBZisQYQiNOQhMM&_nc_zt=24&_nc_ht=scontent.fagc1-1.fna&_nc_gid=4iUSPCrvasyMsXWmebmziw&_nc_ss=7a2a8&oh=00_Af_wrn9Ph9ZhoXsXm3Bu6yeXsgrTzaNba0cpSBqqk7J_cg&oe=6A23C5CF"
)

$ErrorActionPreference = "Stop"

$netsuiteDir = "C:\Users\troytaylor\OneDrive - Microsoft\SharingIsCaring\Cowork Plugins\NetSuite"
$tmp = Join-Path $env:TEMP "netsuite-source.png"

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

Resize-Png -source $src -size 192 -outPath (Join-Path $netsuiteDir "color.png")
Resize-Png -source $src -size 32 -outPath (Join-Path $netsuiteDir "outline.png")
$src.Dispose()
Remove-Item $tmp

Get-Item (Join-Path $netsuiteDir "color.png"), (Join-Path $netsuiteDir "outline.png") | Format-Table Name, Length
