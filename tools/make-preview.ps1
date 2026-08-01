# Generates About/Preview.png. Run with Windows PowerShell (System.Drawing).
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\make-preview.ps1
Add-Type -AssemblyName System.Drawing

$W = 512
$H = 512
$OutPath = Join-Path (Split-Path $PSScriptRoot -Parent) "About\Preview.png"

$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

function C([int]$a, [int]$r, [int]$gr, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $gr, $b) }

function Fill-Glow($gfx, $cx, $cy, $radius, $col, $alpha) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($cx - $radius, $cy - $radius, $radius * 2, $radius * 2)
    $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush($path)
    $brush.CenterColor = (C $alpha $col.R $col.G $col.B)
    $brush.SurroundColors = @((C 0 $col.R $col.G $col.B))
    $gfx.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}

function Fill-Vignette($gfx, $rect, $col, $alpha) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pad = 40
    $path.AddEllipse($rect.X - $pad, $rect.Y - $pad, $rect.Width + $pad * 2, $rect.Height + $pad * 2)
    $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush($path)
    $brush.CenterColor = (C 0 $col.R $col.G $col.B)
    $brush.SurroundColors = @((C $alpha $col.R $col.G $col.B))
    $brush.FocusScales = New-Object System.Drawing.PointF(0.45, 0.45)
    $gfx.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}

function Draw-Spaced($gfx, $text, $font, $brush, $centerX, $y, $spacing) {
    $fmt = [System.Drawing.StringFormat]::GenericTypographic
    $chars = $text.ToCharArray()
    $widths = @()
    foreach ($ch in $chars) {
        if ($ch -eq ' ') { $widths += [float]($font.Size * 0.42) }
        else { $widths += $gfx.MeasureString([string]$ch, $font, [System.Drawing.PointF]::new(0, 0), $fmt).Width }
    }
    $total = 0.0
    foreach ($w in $widths) { $total += $w + $spacing }
    $total -= $spacing

    $x = $centerX - $total / 2
    for ($i = 0; $i -lt $chars.Length; $i++) {
        if ($chars[$i] -ne ' ') { $gfx.DrawString([string]$chars[$i], $font, $brush, $x, $y, $fmt) }
        $x += $widths[$i] + $spacing
    }
}

# ---- background -------------------------------------------------------------
$bgRect = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($bgRect, (C 255 30 33 40), (C 255 9 10 13), 90.0)
$g.FillRectangle($bg, $bgRect)
$bg.Dispose()

$gridPen = New-Object System.Drawing.Pen((C 14 255 255 255), 1)
for ($x = 0; $x -le $W; $x += 32) { $g.DrawLine($gridPen, $x, 0, $x, $H) }
for ($y = 0; $y -le $H; $y += 32) { $g.DrawLine($gridPen, 0, $y, $W, $y) }
$gridPen.Dispose()

# ---- room geometry ----------------------------------------------------------
$wall = 18
$outerL = 84; $outerT = 54; $outerR = 428; $outerB = 298
$inL = $outerL + $wall; $inT = $outerT + $wall; $inR = $outerR - $wall; $inB = $outerB - $wall
$floor = New-Object System.Drawing.Rectangle($inL, $inT, ($inR - $inL), ($inB - $inT))

$doorHalf = 30
$doorCx = 256

# floor
$floorBrush = New-Object System.Drawing.SolidBrush((C 255 34 30 26))
$g.FillRectangle($floorBrush, $floor)
$floorBrush.Dispose()

$amber = C 255 255 198 128

# ---- light spilling out of the open doorway (drawn before the walls) --------
$spill = New-Object System.Drawing.Drawing2D.GraphicsPath
$spill.AddPolygon(@(
        (New-Object System.Drawing.Point(($doorCx - $doorHalf + 3), ($outerB - 2))),
        (New-Object System.Drawing.Point(($doorCx + $doorHalf - 3), ($outerB - 2))),
        (New-Object System.Drawing.Point(($doorCx + 64), ($outerB + 58))),
        (New-Object System.Drawing.Point(($doorCx - 64), ($outerB + 58)))
    ))
$spillRect = New-Object System.Drawing.Rectangle(($doorCx - 68), ($outerB - 4), 136, 64)
$spillBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($spillRect, (C 105 255 205 138), (C 0 255 205 138), 90.0)
$g.FillPath($spillBrush, $spill)
$spillBrush.Dispose(); $spill.Dispose()

# ---- lamp glow, clipped to the room so the walls contain it -----------------
$g.SetClip($floor)

# faint floor tiling, so the lit area is not a flat wash
$tilePen = New-Object System.Drawing.Pen((C 10 255 255 255), 1)
for ($x = $inL; $x -le $inR; $x += 25) { $g.DrawLine($tilePen, $x, $inT, $x, $inB) }
for ($y = $inT; $y -le $inB; $y += 25) { $g.DrawLine($tilePen, $inL, $y, $inR, $y) }
$tilePen.Dispose()

foreach ($lx in @(168, 344)) {
    Fill-Glow $g $lx 168 180 $amber 85
    Fill-Glow $g $lx 168 82 $amber 115
}
Fill-Vignette $g $floor (C 255 10 8 6) 150
$g.ResetClip()

# ---- walls ------------------------------------------------------------------
$wallFill = New-Object System.Drawing.SolidBrush((C 255 74 79 88))
$wallEdge = New-Object System.Drawing.SolidBrush((C 255 96 103 114))

function Wall($x, $y, $w, $h) {
    $g.FillRectangle($wallFill, $x, $y, $w, $h)
    $g.FillRectangle($wallEdge, $x, $y, $w, 3)
}

Wall $outerL $outerT ($outerR - $outerL) $wall                       # top
Wall $outerL $outerT $wall ($outerB - $outerT)                       # left
Wall ($outerR - $wall) $outerT $wall ($outerB - $outerT)             # right
Wall $outerL ($outerB - $wall) (($doorCx - $doorHalf) - $outerL) $wall
Wall ($doorCx + $doorHalf) ($outerB - $wall) ($outerR - ($doorCx + $doorHalf)) $wall

# ---- open door: two panels slid apart --------------------------------------
$doorBrush = New-Object System.Drawing.SolidBrush((C 255 112 146 172))
$doorEdge = New-Object System.Drawing.SolidBrush((C 255 146 182 208))
foreach ($side in @(-1, 1)) {
    $pw = 16
    if ($side -lt 0) { $px = $doorCx - $doorHalf } else { $px = $doorCx + $doorHalf - $pw }
    $g.FillRectangle($doorBrush, $px, ($outerB - $wall), $pw, $wall)
    $g.FillRectangle($doorEdge, $px, ($outerB - $wall), $pw, 3)
}
$doorBrush.Dispose(); $doorEdge.Dispose()
$wallFill.Dispose(); $wallEdge.Dispose()

# ---- lamps ------------------------------------------------------------------
function Lamp($cx, $cy) {
    Fill-Glow $g $cx $cy 34 (C 255 255 226 176) 210
    $core = New-Object System.Drawing.SolidBrush((C 255 255 244 214))
    $g.FillEllipse($core, $cx - 9, $cy - 9, 18, 18)
    $core.Dispose()
    $ring = New-Object System.Drawing.Pen((C 190 255 205 138), 2)
    $g.DrawEllipse($ring, $cx - 14, $cy - 14, 28, 28)
    $ring.Dispose()
}
Lamp 168 168
Lamp 344 168

# warm rim where the inner wall faces the lit room
$rimPen = New-Object System.Drawing.Pen((C 70 255 205 138), 2)
$g.DrawRectangle($rimPen, $inL, $inT, ($inR - $inL), ($inB - $inT))
$rimPen.Dispose()

# ---- link between the lamps: the whole point of the mod ---------------------
$linkPen = New-Object System.Drawing.Pen((C 120 255 205 138), 2)
$linkPen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash
$g.DrawLine($linkPen, 190, 168, 322, 168)
$linkPen.Dispose()

$nodeBrush = New-Object System.Drawing.SolidBrush((C 255 255 205 138))
$g.FillEllipse($nodeBrush, ($doorCx - 5), 163, 10, 10)
$nodeBrush.Dispose()

# ---- title ------------------------------------------------------------------
$titleFont = New-Object System.Drawing.Font("Segoe UI", 34, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$subFont = New-Object System.Drawing.Font("Segoe UI", 15, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$titleBrush = New-Object System.Drawing.SolidBrush((C 255 243 238 228))
$subBrush = New-Object System.Drawing.SolidBrush((C 255 232 183 120))

Draw-Spaced $g "ROOM AUTO LIGHT" $titleFont $titleBrush ($W / 2) 380 3.0

$accent = New-Object System.Drawing.SolidBrush((C 255 232 183 120))
$g.FillRectangle($accent, ($W / 2 - 60), 428, 120, 2)
$accent.Dispose()

Draw-Spaced $g ("ONE ROOM " + [char]0x2022 + " ONE SWITCH") $subFont $subBrush ($W / 2) 444 2.5

$titleFont.Dispose(); $subFont.Dispose(); $titleBrush.Dispose(); $subBrush.Dispose()

# ---- save -------------------------------------------------------------------
$g.Dispose()
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "wrote $OutPath"
