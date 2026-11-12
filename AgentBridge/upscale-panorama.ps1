Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile('E:\UnityProjects\WaterSlides\Assets\WaterSlides\Environment\TropicalLagoonEnhanced.png')
$dst = New-Object System.Drawing.Bitmap 8192,4096
$g = [System.Drawing.Graphics]::FromImage($dst)
$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$attr = New-Object System.Drawing.Imaging.ImageAttributes
$attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
$g.DrawImage($src,[System.Drawing.Rectangle]::new(0,0,8192,4096),0,0,$src.Width,$src.Height,[System.Drawing.GraphicsUnit]::Pixel,$attr)
$dst.Save('E:\UnityProjects\WaterSlides\Assets\WaterSlides\Environment\TropicalLagoon360_8K.png',[System.Drawing.Imaging.ImageFormat]::Png)
$attr.Dispose(); $g.Dispose(); $dst.Dispose(); $src.Dispose()
