# Window Rescue アイコン生成スクリプト
# 画面（角丸の青い四角）の右上へはみ出した点線の窓を、黄色の矢印で手元へ引き戻すデザイン。
# 使い方: pwsh -File tools/make-icon.ps1  → src/WindowRescue/Assets/WindowRescue.ico と preview PNG を出力
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\WindowRescue\Assets')
)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$blue      = [System.Drawing.ColorTranslator]::FromHtml('#0017C1')  # DADS Blue-900
$blueDark  = [System.Drawing.ColorTranslator]::FromHtml('#00118F')
$titleBar  = [System.Drawing.ColorTranslator]::FromHtml('#C5D7FB')
$yellow    = [System.Drawing.ColorTranslator]::FromHtml('#FFD43D')  # DADS Yellow-300（フォーカス色）
$white     = [System.Drawing.Color]::White

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Max(0.5, $r * 2)
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $small = $s -le 24

    # 画面（背景）
    $pad = [float]($s * 0.03)
    $bg = New-RoundRect $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ($s * 0.2)
    $g.FillPath((New-Object System.Drawing.SolidBrush $blue), $bg)

    # 画面外へはみ出した窓（半透明＋点線・背景で切り取る）
    $g.SetClip($bg)
    $gx = $s * 0.50; $gy = -$s * 0.14; $gw = $s * 0.64; $gh = $s * 0.46
    $ghost = New-RoundRect $gx $gy $gw $gh ($s * 0.06)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($(if ($small) { 120 } else { 70 }), 255, 255, 255))), $ghost)
    if (-not $small) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(230, 255, 255, 255)), ([float]($s * 0.03))
        $pen.DashPattern = @(2.2, 1.5)
        $g.DrawPath($pen, $ghost)
    }
    $g.ResetClip()

    # 救出された窓
    $wx = $s * 0.12; $wy = $s * 0.44; $ww = $s * 0.58; $wh = $s * 0.43
    $win = New-RoundRect $wx $wy $ww $wh ($s * 0.07)
    $g.FillPath((New-Object System.Drawing.SolidBrush $white), $win)
    $g.SetClip($win)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $titleBar), $wx, $wy, $ww, $wh * 0.28)
    $g.ResetClip()
    if (-not $small) {
        $dot = $s * 0.05
        $g.FillEllipse((New-Object System.Drawing.SolidBrush $blueDark), ($wx + $ww - $dot * 2.2), ($wy + $wh * 0.14 - $dot / 2), $dot, $dot)
    }

    # 引き戻す矢印（黄色・はみ出した窓から手元の窓へ）
    $penW = [float]([Math]::Max(2.0, $s * ($(if ($small) { 0.13 } else { 0.09 }))))
    $sx = $s * 0.84; $sy = $s * 0.12   # 始点
    $ex = $s * 0.60; $ey = $s * 0.36   # 矢じりの先端
    $len = [Math]::Sqrt(($ex - $sx) * ($ex - $sx) + ($ey - $sy) * ($ey - $sy))
    $ux = ($ex - $sx) / $len; $uy = ($ey - $sy) / $len
    $head = $penW * 2.1
    $bx = $ex - $ux * $head; $by = $ey - $uy * $head
    $arrow = New-Object System.Drawing.Pen $yellow, $penW
    $arrow.StartCap = 'Round'; $arrow.EndCap = 'Flat'
    $g.DrawLine($arrow, [float]$sx, [float]$sy, [float]$bx, [float]$by)
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ([float]$ex), ([float]$ey)),
        (New-Object System.Drawing.PointF ([float]($bx - $uy * $head)), ([float]($by + $ux * $head))),
        (New-Object System.Drawing.PointF ([float]($bx + $uy * $head)), ([float]($by - $ux * $head)))
    )
    $g.FillPolygon((New-Object System.Drawing.SolidBrush $yellow), $pts)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @{ Size = $s; Bytes = $ms.ToArray() }
    if ($s -eq 256) { $bmp.Save((Join-Path $OutDir 'WindowRescue-256.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# ICO（PNG 埋め込み形式）を組み立てる
$icoPath = Join-Path $OutDir 'WindowRescue.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$p.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $p.Bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p.Bytes) }
$bw.Dispose()

# 確認用の一覧プレビュー（ライト／ダーク背景）
$prev = New-Object System.Drawing.Bitmap 760, 300
$g = [System.Drawing.Graphics]::FromImage($prev)
$g.Clear([System.Drawing.Color]::White)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(32, 32, 32))), 0, 150, 760, 150)
$x = 10
foreach ($s in 16, 24, 32, 48, 64, 128) {
    $b = Draw-Icon $s
    $g.DrawImage($b, $x, 10); $g.DrawImage($b, $x, 160)
    $x += $s + 24
    $b.Dispose()
}
$g.Dispose()
$prev.Save((Join-Path $PSScriptRoot '..\docs\icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "wrote $icoPath"
