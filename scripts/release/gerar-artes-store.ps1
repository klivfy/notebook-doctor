$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$outDir = Join-Path $repoRoot 'packaging\store\vitrine\artes'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-AppSymbol([System.Drawing.Graphics]$g, [float]$cx, [float]$cy, [float]$size) {
    $half = $size / 2.0
    $x = $cx - $half
    $y = $cy - $half
    $r = $size * 0.22

    # Sombra suave
    $shadowPath = New-RoundedRectPath ($x + $size * 0.02) ($y + $size * 0.04) $size $size $r
    $shadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(80, 0, 0, 0))
    $g.FillPath($shadowBrush, $shadowPath)
    $shadowBrush.Dispose(); $shadowPath.Dispose()

    # Fundo esmeralda com gradiente
    $badgePath = New-RoundedRectPath $x $y $size $size $r
    $rect = New-Object System.Drawing.RectangleF ($x, $y, $size, $size)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        $rect,
        [System.Drawing.Color]::FromArgb(255, 16, 152, 104),
        [System.Drawing.Color]::FromArgb(255, 10, 92, 64),
        45.0
    )
    $g.FillPath($grad, $badgePath)

    # Borda sutil brilhante
    $borderPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 255, 255, 255), [Math]::Max(1.5, $size * 0.015))
    $g.DrawPath($borderPen, $badgePath)
    $borderPen.Dispose(); $grad.Dispose(); $badgePath.Dispose()

    # Carcaça do notebook (tela)
    $scrW = $size * 0.62
    $scrH = $size * 0.42
    $scrX = $cx - ($scrW / 2.0)
    $scrY = $cy - ($size * 0.26)
    $scrPath = New-RoundedRectPath $scrX $scrY $scrW $scrH ($size * 0.04)
    $scrPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White, [Math]::Max(2.0, $size * 0.045))
    $scrFill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(35, 255, 255, 255))
    $g.FillPath($scrFill, $scrPath)
    $g.DrawPath($scrPen, $scrPath)
    $scrFill.Dispose(); $scrPen.Dispose(); $scrPath.Dispose()

    # Base do notebook
    $baseW = $size * 0.76
    $baseH = $size * 0.065
    $baseX = $cx - ($baseW / 2.0)
    $baseY = $scrY + $scrH + ($size * 0.035)
    $basePath = New-RoundedRectPath $baseX $baseY $baseW $baseH ($size * 0.03)
    $whiteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillPath($whiteBrush, $basePath)
    $basePath.Dispose()

    # Linha de pulso ECG dentro da tela
    $pulsePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 167, 243, 208), [Math]::Max(2.0, $size * 0.042))
    $pulsePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pulsePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pulsePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $py = $scrY + ($scrH * 0.54)
    $px1 = $scrX + ($scrW * 0.14)
    $px2 = $scrX + ($scrW * 0.34)
    $px3 = $scrX + ($scrW * 0.44)
    $px4 = $scrX + ($scrW * 0.56)
    $px5 = $scrX + ($scrW * 0.66)
    $px6 = $scrX + ($scrW * 0.86)

    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($px1, $py)),
        (New-Object System.Drawing.PointF ($px2, $py)),
        (New-Object System.Drawing.PointF ($px3, ($py - $scrH * 0.28))),
        (New-Object System.Drawing.PointF ($px4, ($py + $scrH * 0.24))),
        (New-Object System.Drawing.PointF ($px5, $py)),
        (New-Object System.Drawing.PointF ($px6, $py))
    )
    $g.DrawLines($pulsePen, $pts)
    $pulsePen.Dispose()
    $whiteBrush.Dispose()
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap ($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    $rect = New-Object System.Drawing.Rectangle (0, 0, $w, $h)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        $rect,
        [System.Drawing.Color]::FromArgb(255, 11, 24, 33),
        [System.Drawing.Color]::FromArgb(255, 7, 46, 38),
        135.0
    )
    $g.FillRectangle($bg, $rect)
    $bg.Dispose()

    # Brilho radial decorativo no fundo
    $glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glowSize = [Math]::Max($w, $h) * 0.9
    $glowPath.AddEllipse(($w * 0.5 - $glowSize * 0.5), ($h * 0.35 - $glowSize * 0.5), $glowSize, $glowSize)
    $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush ($glowPath)
    $pgb.CenterColor = [System.Drawing.Color]::FromArgb(55, 16, 185, 129)
    $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 16, 185, 129))
    $g.FillPath($pgb, $glowPath)
    $pgb.Dispose(); $glowPath.Dispose()

    return @{ Bitmap = $bmp; Graphics = $g }
}

# 1. StoreLogo-300x300.png (1:1)
$c1 = New-Canvas 300 300
Draw-AppSymbol $c1.Graphics 150 150 220
$c1.Bitmap.Save((Join-Path $outDir 'StoreLogo-300x300.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$c1.Graphics.Dispose(); $c1.Bitmap.Dispose()

# 2. BoxArt-1080x1080.png (1:1)
$c2 = New-Canvas 1080 1080
Draw-AppSymbol $c2.Graphics 540 400 420
$sfCenter = New-Object System.Drawing.StringFormat
$sfCenter.Alignment = [System.Drawing.StringAlignment]::Center
$fontTitle = New-Object System.Drawing.Font ('Segoe UI', 54, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fontSub = New-Object System.Drawing.Font ('Segoe UI', 28, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$fontTag = New-Object System.Drawing.Font ('Segoe UI Semibold', 22, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$mint = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 167, 243, 208))
$muted = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(215, 209, 213, 219))

$c2.Graphics.DrawString('Notebook Doctor', $fontTitle, $white, 540, 680, $sfCenter)
$c2.Graphics.DrawString("Diagn$([char]0x00F3)stico, Limpeza Segura e Sa$([char]0x00FA)de da Bateria", $fontSub, $muted, 540, 765, $sfCenter)

$pillPath = New-RoundedRectPath 330 855 420 54 27
$pillFill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(45, 16, 185, 129))
$pillPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 52, 211, 153), 2)
$c2.Graphics.FillPath($pillFill, $pillPath)
$c2.Graphics.DrawPath($pillPen, $pillPath)
$c2.Graphics.DrawString("WINDOWS 10 & 11  $([char]0x2022)  100% LOCAL", $fontTag, $mint, 540, 868, $sfCenter)
$pillFill.Dispose(); $pillPen.Dispose(); $pillPath.Dispose()

$c2.Bitmap.Save((Join-Path $outDir 'BoxArt-1080x1080.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$c2.Graphics.Dispose(); $c2.Bitmap.Dispose()

# 3. Poster-720x1080.png (2:3)
$c3 = New-Canvas 720 1080
Draw-AppSymbol $c3.Graphics 360 360 320
$fontPosterTitle = New-Object System.Drawing.Font ('Segoe UI', 44, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fontPosterSub = New-Object System.Drawing.Font ('Segoe UI', 23, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$c3.Graphics.DrawString('Notebook Doctor', $fontPosterTitle, $white, 360, 585, $sfCenter)
$c3.Graphics.DrawString("Diagn$([char]0x00F3)stico transparente e manuten$([char]0x00E7)$([char]0x00E3)o", $fontPosterSub, $muted, 360, 655, $sfCenter)
$c3.Graphics.DrawString('preventiva para o seu Windows', $fontPosterSub, $muted, 360, 690, $sfCenter)

$pillPath3 = New-RoundedRectPath 170 780 380 50 25
$pillFill3 = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(45, 16, 185, 129))
$pillPen3 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 52, 211, 153), 2)
$c3.Graphics.FillPath($pillFill3, $pillPath3)
$c3.Graphics.DrawPath($pillPen3, $pillPath3)
$c3.Graphics.DrawString("MODO SIMULA$([char]0x00C7)$([char]0x00C3)O + DESFAZER", $fontTag, $mint, 360, 792, $sfCenter)
$pillFill3.Dispose(); $pillPen3.Dispose(); $pillPath3.Dispose()

$c3.Bitmap.Save((Join-Path $outDir 'Poster-720x1080.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$c3.Graphics.Dispose(); $c3.Bitmap.Dispose()
$fontPosterTitle.Dispose(); $fontPosterSub.Dispose()

# 4. Banner-Hero-1920x1080.png (16:9) — compõe com captura real da interface se existir
$c4 = New-Canvas 1920 1080
Draw-AppSymbol $c4.Graphics 210 240 160
$fontHeroTitle = New-Object System.Drawing.Font ('Segoe UI', 64, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fontHeroSub = New-Object System.Drawing.Font ('Segoe UI', 28, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$fontHeroBullet = New-Object System.Drawing.Font ('Segoe UI Semibold', 24, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)

$c4.Graphics.DrawString('Notebook Doctor', $fontHeroTitle, $white, 120, 360)
$c4.Graphics.DrawString("Sa$([char]0x00FA)de, limpeza segura e efici$([char]0x00EA)ncia de bateria", $fontHeroSub, $mint, 124, 450)
$c4.Graphics.DrawString('para notebooks e PCs com Windows 10 e 11.', $fontHeroSub, $muted, 124, 492)

$chk = [char]0x2713
$bullets = @(
    "$chk  Nota de Sa$([char]0x00FA)de (0 a 100) e diagn$([char]0x00F3)stico em tempo real",
    "$chk  Limpeza segura com pr$([char]0x00E9)via de arquivos antes de apagar",
    "$chk  Modo Simula$([char]0x00E7)$([char]0x00E3)o e aba A$([char]0x00E7)$([char]0x00F5)es com Desfazer em 1 clique",
    "$chk  Controle de inicializa$([char]0x00E7)$([char]0x00E3)o, processos e bateria em Watts",
    "$chk  100% local, sem an$([char]0x00FA)ncios e sem necessidade de conta"
)
$by = 585
foreach ($b in $bullets) {
    $c4.Graphics.DrawString($b, $fontHeroBullet, $white, 128, $by)
    $by += 54
}

$shotPath = Join-Path $repoRoot 'packaging\store\vitrine\screenshots\pt-BR\01-inicio-claro.png'
if (Test-Path $shotPath) {
    $shotImg = [System.Drawing.Image]::FromFile($shotPath)
    $sx = 930; $sy = 150; $sw = 910; $sh = 512
    $cardShadow = New-RoundedRectPath ($sx + 12) ($sy + 18) $sw $sh 18
    $shBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 0, 0, 0))
    $c4.Graphics.FillPath($shBrush, $cardShadow)
    $shBrush.Dispose(); $cardShadow.Dispose()

    $clipPath = New-RoundedRectPath $sx $sy $sw $sh 16
    $state = $c4.Graphics.Save()
    $c4.Graphics.SetClip($clipPath)
    $c4.Graphics.DrawImage($shotImg, (New-Object System.Drawing.Rectangle ($sx, $sy, $sw, $sh)))
    $c4.Graphics.Restore($state)
    $framePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(140, 255, 255, 255), 2)
    $c4.Graphics.DrawPath($framePen, $clipPath)
    $framePen.Dispose(); $clipPath.Dispose()
    $shotImg.Dispose()
}

$darkShotPath = Join-Path $repoRoot 'packaging\store\vitrine\screenshots\pt-BR\02-inicio-escuro.png'
if (Test-Path $darkShotPath) {
    $darkImg = [System.Drawing.Image]::FromFile($darkShotPath)
    $dx = 1040; $dy = 480; $dw = 820; $dh = 461
    $cardShadow2 = New-RoundedRectPath ($dx + 12) ($dy + 18) $dw $dh 18
    $shBrush2 = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(140, 0, 0, 0))
    $c4.Graphics.FillPath($shBrush2, $cardShadow2)
    $shBrush2.Dispose(); $cardShadow2.Dispose()

    $clipPath2 = New-RoundedRectPath $dx $dy $dw $dh 16
    $state2 = $c4.Graphics.Save()
    $c4.Graphics.SetClip($clipPath2)
    $c4.Graphics.DrawImage($darkImg, (New-Object System.Drawing.Rectangle ($dx, $dy, $dw, $dh)))
    $c4.Graphics.Restore($state2)
    $framePen2 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(160, 52, 211, 153), 2)
    $c4.Graphics.DrawPath($framePen2, $clipPath2)
    $framePen2.Dispose(); $clipPath2.Dispose()
    $darkImg.Dispose()
}

$c4.Bitmap.Save((Join-Path $outDir 'Banner-Hero-1920x1080.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$c4.Graphics.Dispose(); $c4.Bitmap.Dispose()

$fontHeroTitle.Dispose(); $fontHeroSub.Dispose(); $fontHeroBullet.Dispose()
$fontTitle.Dispose(); $fontSub.Dispose(); $fontTag.Dispose()
$white.Dispose(); $mint.Dispose(); $muted.Dispose(); $sfCenter.Dispose()

Write-Host "Artes promocionais geradas em: $outDir"
