[CmdletBinding()]
param(
    [string]$SourcePath = (Join-Path $PSScriptRoot 'C-wave-refinement-v2.png'),
    [string]$DestinationDirectory = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourceFile = (Resolve-Path -LiteralPath $SourcePath).Path
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
[IO.Directory]::CreateDirectory($destination) | Out-Null
$approvedHash = '2563E17F48DD126E1A172AB10DA4AEDDFDA68E8ABE4715A880E3ABEC44D9893B'
$sourceHash = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash
if ($sourceHash -ne $approvedHash) { throw '승인된 C-wave-refinement-v2 원본과 SHA-256이 다릅니다. 원본을 확인하세요.' }

# Only downsample the entire approved square. No crop, recolor, sharpening or redraw.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()
$source = [Drawing.Bitmap]::FromFile($sourceFile)
try {
    if ($source.Width -ne $source.Height) { throw '승인 원본은 정사각형이어야 합니다.' }
    $sourceWidth = $source.Width
    $sourceHeight = $source.Height
    foreach ($size in $sizes) {
        $frame = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($frame)
        $attributes = [Drawing.Imaging.ImageAttributes]::new()
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $target = [Drawing.Rectangle]::new(0, 0, $size, $size)
            $graphics.DrawImage($source, $target, 0, 0, $source.Width, $source.Height, [Drawing.GraphicsUnit]::Pixel, $attributes)
            $frame.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
        }
        finally { $stream.Dispose(); $attributes.Dispose(); $graphics.Dispose(); $frame.Dispose() }
    }
}
finally { $source.Dispose() }

$iconPath = Join-Path $destination 'SonoHUD.ico'
$buffer = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($buffer)
try {
    $writer.Write([uint16]0) # reserved
    $writer.Write([uint16]1) # icon, not cursor
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { [byte]0 } else { [byte]$sizes[$index] }
        $writer.Write($dimension); $writer.Write($dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    $writer.Flush()
    [IO.File]::WriteAllBytes($iconPath, $buffer.ToArray())
}
finally { $writer.Dispose(); $buffer.Dispose() }

$manifest = [ordered]@{
    approvedAsset = 'C-wave-refinement-v2.png'
    sourceSha256 = $sourceHash
    sourceWidth = $sourceWidth
    sourceHeight = $sourceHeight
    output = 'SonoHUD.ico'
    outputSha256 = (Get-FileHash -LiteralPath $iconPath -Algorithm SHA256).Hash
    sizes = $sizes
    frameEncoding = 'PNG, 32-bit RGBA inside ICO'
    transformation = 'Full-image proportional downsampling only; original transparency preserved.'
    generator = 'Generate-Icons.ps1 / System.Drawing HighQualityBicubic'
}
$manifestJson = $manifest | ConvertTo-Json -Depth 3
[IO.File]::WriteAllText((Join-Path $destination 'branding-manifest.json'), $manifestJson, [Text.UTF8Encoding]::new($false))
$manifest | ConvertTo-Json -Depth 3
