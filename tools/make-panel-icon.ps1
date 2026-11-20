<#
.SYNOPSIS
    Generates the Spell Check panel icon (src\RhinoSpellCheck\Resources\Icons\SpellCheck.ico).

.DESCRIPTION
    The icon is a blue "A" over a red spelling squiggle - the same metaphor the
    plug-in draws in the viewport.

    Each size is drawn natively rather than downscaled from one large bitmap:
    a 16px panel tab has no room for the detail a 48px render would leave behind,
    and downscaling turns the squiggle into a grey smear.

    The "A" is stroked from line segments instead of a font glyph on purpose.
    GDI+ hinting mangles a 16px letterform, whereas three thick strokes with
    round joins stay crisp at every size.

    Colours are chosen to read on both Rhino themes: the panel tab strip is near
    white in the light theme and near black in the dark one, so a medium blue and
    a medium red are legible against either. A theme-specific dark glyph would
    disappear in one of them.

    Entries are packed as 32bpp BMP rather than PNG. System.Drawing.Icon on
    .NET Framework - which is what Rhino 8 hands the panel - is unreliable at
    decoding PNG-compressed entries, and the size penalty here is a few KB.
#>
[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\RhinoSpellCheck\Resources\Icons\SpellCheck.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Panel tabs draw at 16-24px, so 64 already covers 200% DPI with headroom. A
# 256px entry is legal but adds 270KB of uncompressed BMP for nothing.
$sizes = @(16, 20, 24, 32, 40, 48, 64)

$glyphColor    = [System.Drawing.Color]::FromArgb(255, 60, 125, 196)
$squiggleColor = [System.Drawing.Color]::FromArgb(255, 224, 49, 49)

function New-IconBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        $s = [double]$Size

        # --- the "A" ---------------------------------------------------------
        # Occupies the top ~68% of the canvas, leaving the bottom third for the
        # squiggle plus a little breathing room.
        $left   = $s * 0.09
        $right  = $s * 0.91
        $top    = $s * 0.10
        $bottom = $s * 0.66
        $apexX  = $s * 0.50

        $stroke = [Math]::Max(1.0, $s * 0.155)
        $pen = New-Object System.Drawing.Pen($glyphColor, [float]$stroke)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        try {
            # Both legs as one path so the apex joins cleanly instead of showing
            # two overlapping round caps.
            $path = New-Object System.Drawing.Drawing2D.GraphicsPath
            try {
                $path.AddLine([float]$left, [float]$bottom, [float]$apexX, [float]$top)
                $path.AddLine([float]$apexX, [float]$top, [float]$right, [float]$bottom)
                $g.DrawPath($pen, $path)
            }
            finally { $path.Dispose() }

            # Crossbar, inset so it meets the legs rather than poking through.
            $barY = $s * 0.50
            $barInset = $s * 0.215
            $g.DrawLine($pen, [float]($left + $barInset), [float]$barY, [float]($right - $barInset), [float]$barY)
        }
        finally { $pen.Dispose() }

        # --- the squiggle ----------------------------------------------------
        $sqLeft  = $s * 0.06
        $sqRight = $s * 0.94
        $sqY     = $s * 0.845
        $amp     = $s * 0.075

        # Four half-waves reads as "squiggle" at every size; more turns to mush
        # at 16px, fewer looks like a dashed line.
        $humps = 4
        $span = ($sqRight - $sqLeft) / $humps

        $points = New-Object System.Collections.Generic.List[System.Drawing.PointF]
        for ($i = 0; $i -le $humps; $i++) {
            $x = $sqLeft + $span * $i
            $y = if ($i % 2 -eq 0) { $sqY + $amp } else { $sqY - $amp }
            $points.Add((New-Object System.Drawing.PointF([float]$x, [float]$y)))
        }

        $sqStroke = [Math]::Max(1.0, $s * 0.105)
        $sqPen = New-Object System.Drawing.Pen($squiggleColor, [float]$sqStroke)
        $sqPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $sqPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
        $sqPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        try {
            # Straight zig-zag below 24px: the curve smoothing eats the amplitude
            # when there are only two or three pixels to work with.
            if ($Size -lt 24) {
                $g.DrawLines($sqPen, $points.ToArray())
            }
            else {
                $g.DrawCurve($sqPen, $points.ToArray(), 0.8)
            }
        }
        finally { $sqPen.Dispose() }
    }
    finally { $g.Dispose() }

    return $bmp
}

# Packs 32bpp BMP entries into an .ico. The BMP inside an icon stores a doubled
# height (colour rows followed by the legacy AND mask) and rows run bottom-up.
function Convert-BitmapToIconEntry {
    param([System.Drawing.Bitmap]$Bitmap)

    $w = $Bitmap.Width
    $h = $Bitmap.Height

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $xorSize = $w * $h * 4
        $maskStride = [int]([Math]::Floor(($w + 31) / 32) * 4)
        $andSize = $maskStride * $h

        # BITMAPINFOHEADER
        $writer.Write([int]40)
        $writer.Write([int]$w)
        $writer.Write([int]($h * 2))
        $writer.Write([int16]1)
        $writer.Write([int16]32)
        $writer.Write([int]0)
        $writer.Write([int]($xorSize + $andSize))
        $writer.Write([int]0); $writer.Write([int]0)
        $writer.Write([int]0); $writer.Write([int]0)

        # Colour rows, bottom-up, BGRA.
        $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
        $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                                 [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $row = New-Object byte[] ($w * 4)
            for ($y = $h - 1; $y -ge 0; $y--) {
                $ptr = [IntPtr]::Add($data.Scan0, $y * $data.Stride)
                [System.Runtime.InteropServices.Marshal]::Copy($ptr, $row, 0, $row.Length)
                $writer.Write($row)
            }
        }
        finally { $Bitmap.UnlockBits($data) }

        # AND mask: left at zero. The alpha channel above is what actually
        # controls transparency; this exists only to satisfy the format.
        $writer.Write((New-Object byte[] $andSize))

        $writer.Flush()
        return $stream.ToArray()
    }
    finally { $writer.Dispose(); $stream.Dispose() }
}

$entries = @()
$bitmaps = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap -Size $size
    $bitmaps += $bmp
    $entries += ,@{ Size = $size; Data = (Convert-BitmapToIconEntry -Bitmap $bmp) }
}

$directory = Split-Path -Parent $OutputPath
if (-not (Test-Path $directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
try {
    $bw.Write([int16]0)                  # reserved
    $bw.Write([int16]1)                  # type: icon
    $bw.Write([int16]$entries.Count)

    $offset = 6 + (16 * $entries.Count)
    foreach ($entry in $entries) {
        # 256 is stored as 0 - the field is a single byte.
        $dim = if ($entry.Size -ge 256) { 0 } else { $entry.Size }
        $bw.Write([byte]$dim)
        $bw.Write([byte]$dim)
        $bw.Write([byte]0)               # palette size
        $bw.Write([byte]0)               # reserved
        $bw.Write([int16]1)              # colour planes
        $bw.Write([int16]32)             # bits per pixel
        $bw.Write([int]$entry.Data.Length)
        $bw.Write([int]$offset)
        $offset += $entry.Data.Length
    }

    # The cast and the three-argument overload are both load bearing: reading
    # .Data off the hashtable yields a wrapped object, and PowerShell then binds
    # Write(byte) instead of Write(byte[]) - writing one byte per entry and
    # producing a header-only file.
    foreach ($entry in $entries) {
        [byte[]]$payload = $entry.Data
        $bw.Write($payload, 0, $payload.Length)
    }

    $bw.Flush()
    [System.IO.File]::WriteAllBytes($OutputPath, $out.ToArray())
}
finally { $bw.Dispose(); $out.Dispose() }

# Also drop a PNG preview of each size next to the script's output, so the icon
# can be eyeballed without opening the .ico in something that understands it.
$previewDir = Join-Path $directory 'preview'
if (-not (Test-Path $previewDir)) { New-Item -ItemType Directory -Force -Path $previewDir | Out-Null }
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $bitmaps[$i].Save((Join-Path $previewDir ("SpellCheck-{0}.png" -f $sizes[$i])),
                      [System.Drawing.Imaging.ImageFormat]::Png)
}

# Verify the result actually parses as an icon before claiming success.
$check = New-Object System.Drawing.Icon($OutputPath, 16, 16)
try {
    if ($check.Width -ne 16) { throw "Icon did not round-trip at 16x16 (got $($check.Width))." }
}
finally { $check.Dispose() }

foreach ($b in $bitmaps) { $b.Dispose() }

Write-Host "Wrote $OutputPath ($([Math]::Round((Get-Item $OutputPath).Length / 1KB, 1)) KB, $($sizes.Count) sizes)" -ForegroundColor Green
Write-Host "Previews: $previewDir"
