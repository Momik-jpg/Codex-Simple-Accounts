$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing.Common

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$output = Join-Path $PSScriptRoot '..\assets\Codex-Konten.ico'
$output = [System.IO.Path]::GetFullPath($output)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($output)) | Out-Null

function New-RoundedRectanglePath([float]$size, [float]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
    $path.AddArc($size - $diameter, 0, $diameter, $diameter, 270, 90)
    $path.AddArc($size - $diameter, $size - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc(0, $size - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconFrame([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

        $margin = [Math]::Max(1, [Math]::Round($size * 0.045))
        $innerSize = $size - (2 * $margin)
        $radius = $innerSize * 0.25
        $graphics.TranslateTransform($margin, $margin)
        $path = New-RoundedRectanglePath $innerSize $radius
        try {
            $bounds = [System.Drawing.RectangleF]::new(0, 0, $innerSize, $innerSize)
            $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                $bounds,
                [System.Drawing.Color]::FromArgb(72, 159, 238),
                [System.Drawing.Color]::FromArgb(31, 100, 183),
                135
            )
            try {
                $graphics.FillPath($gradient, $path)
            }
            finally {
                $gradient.Dispose()
            }

            $fontSize = $innerSize * 0.53
            $font = [System.Drawing.Font]::new('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
            $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
            $format = [System.Drawing.StringFormat]::new()
            try {
                $format.Alignment = [System.Drawing.StringAlignment]::Center
                $format.LineAlignment = [System.Drawing.StringAlignment]::Center
                $textBounds = [System.Drawing.RectangleF]::new(0, -($innerSize * 0.025), $innerSize, $innerSize)
                $graphics.DrawString('C', $font, $brush, $textBounds, $format)
            }
            finally {
                $format.Dispose()
                $brush.Dispose()
                $font.Dispose()
            }
        }
        finally {
            $path.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    $stream = [System.IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

$frames = @($sizes | ForEach-Object { New-IconFrame $_ })
$file = [System.IO.File]::Create($output)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)
    for ($index = 0; $index -lt $frames.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }

    foreach ($frame in $frames) {
        $writer.Write($frame)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Output $output
