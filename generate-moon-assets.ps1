param(
    [string]$Master = (Join-Path $PSScriptRoot 'Assets\moon-master.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies 'System.Drawing.Common','System.Drawing.Primitives','System.Private.Windows.GdiPlus','System.Private.Windows.Core' -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class QuotaMoonAssetGenerator
{
    public static void Generate(string sourcePath, string destinationPath, int remainingPercent)
    {
        using var source = new Bitmap(sourcePath);
        using var destination = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        var rectangle = new Rectangle(0, 0, source.Width, source.Height);
        var sourceData = source.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var destinationData = destination.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var sourcePixels = new byte[Math.Abs(sourceData.Stride) * source.Height];
            var destinationPixels = new byte[Math.Abs(destinationData.Stride) * source.Height];
            Marshal.Copy(sourceData.Scan0, sourcePixels, 0, sourcePixels.Length);

            var minX = source.Width;
            var minY = source.Height;
            var maxX = -1;
            var maxY = -1;
            for (var y = 0; y < source.Height; y++)
            {
                for (var x = 0; x < source.Width; x++)
                {
                    var index = y * sourceData.Stride + x * 4;
                    if (sourcePixels[index + 3] <= 12) continue;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            var centerX = (minX + maxX) / 2.0;
            var centerY = (minY + maxY) / 2.0;
            var radius = Math.Max(maxX - minX + 1, maxY - minY + 1) / 2.0;
            var fraction = Math.Clamp(remainingPercent / 100.0, 0.0, 1.0);
            const double darkSideBrightness = 0.055;
            const double terminatorSoftness = 0.018;

            for (var y = 0; y < source.Height; y++)
            {
                for (var x = 0; x < source.Width; x++)
                {
                    var sourceIndex = y * sourceData.Stride + x * 4;
                    var destinationIndex = y * destinationData.Stride + x * 4;
                    var alpha = sourcePixels[sourceIndex + 3];
                    if (alpha == 0) continue;

                    var blue = sourcePixels[sourceIndex];
                    var green = sourcePixels[sourceIndex + 1];
                    var red = sourcePixels[sourceIndex + 2];
                    var luminance = Math.Clamp(0.0722 * blue + 0.7152 * green + 0.2126 * red, 0, 255);
                    var neutralWhite = Math.Clamp(luminance * 1.045 + 4, 0, 255);
                    var brightness = 1.0;

                    if (remainingPercent < 100)
                    {
                        var normalizedX = (x - centerX) / radius;
                        var normalizedY = (y - centerY) / radius;
                        var verticalRadius = Math.Sqrt(Math.Max(0, 1 - normalizedY * normalizedY));
                        var terminator = (2 * fraction - 1) * verticalRadius;
                        var distance = terminator - normalizedX;
                        var t = Math.Clamp((distance + terminatorSoftness) / (2 * terminatorSoftness), 0, 1);
                        var smooth = t * t * (3 - 2 * t);
                        brightness = darkSideBrightness + (1 - darkSideBrightness) * smooth;
                    }

                    var gray = (byte)Math.Clamp((int)Math.Round(neutralWhite * brightness), 0, 255);
                    destinationPixels[destinationIndex] = gray;
                    destinationPixels[destinationIndex + 1] = gray;
                    destinationPixels[destinationIndex + 2] = gray;
                    destinationPixels[destinationIndex + 3] = alpha;
                }
            }

            Marshal.Copy(destinationPixels, 0, destinationData.Scan0, destinationPixels.Length);
        }
        finally
        {
            source.UnlockBits(sourceData);
            destination.UnlockBits(destinationData);
        }
        destination.Save(destinationPath, ImageFormat.Png);
    }
}
'@

if (-not (Test-Path -LiteralPath $Master)) {
    throw "Moon master image was not found: $Master"
}

$assets = Join-Path $PSScriptRoot 'Assets'
foreach ($percent in 100, 90, 80, 70, 60, 50, 40, 30, 20, 10) {
    $name = if ($percent -eq 100) { 'quota-wisp.png' } else { "quota-wisp-$percent.png" }
    [QuotaMoonAssetGenerator]::Generate($Master, (Join-Path $assets $name), $percent)
}

$iconSizes = 256, 64, 32, 16
$iconImages = foreach ($size in $iconSizes) {
    $source = [System.Drawing.Bitmap]::FromFile((Join-Path $assets 'quota-wisp.png'))
    try {
        $scaled = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($scaled)
            try {
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($source, 0, 0, $size, $size)
            } finally { $graphics.Dispose() }
            $stream = [System.IO.MemoryStream]::new()
            try {
                $scaled.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                ,$stream.ToArray()
            } finally { $stream.Dispose() }
        } finally { $scaled.Dispose() }
    } finally { $source.Dispose() }
}

$iconPath = Join-Path $assets 'quota-wisp.ico'
$file = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$iconSizes.Count)
    $offset = 6 + 16 * $iconSizes.Count
    for ($index = 0; $index -lt $iconSizes.Count; $index++) {
        $size = $iconSizes[$index]
        $bytes = $iconImages[$index]
        $dimension = if ($size -eq 256) { 0 } else { $size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $bytes.Length
    }
    foreach ($bytes in $iconImages) { $writer.Write($bytes) }
} finally {
    $writer.Dispose()
    $file.Dispose()
}

Get-ChildItem -LiteralPath $assets -Filter 'quota-wisp*' |
    Sort-Object Name |
    Select-Object Name, Length, LastWriteTime
