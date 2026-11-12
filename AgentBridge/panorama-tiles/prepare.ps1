Add-Type -AssemblyName System.Drawing
$src=[System.Drawing.Bitmap]::FromFile('E:\UnityProjects\WaterSlides\Assets\WaterSlides\Environment\TropicalLagoon360.png')
for($row=0;$row -lt 2;$row++){for($col=0;$col -lt 4;$col++){
$x=[int][math]::Round($col*$src.Width/4.0); $y=[int][math]::Round($row*$src.Height/2.0)
$w=[int][math]::Round(($col+1)*$src.Width/4.0)-$x; $h=[int][math]::Round(($row+1)*$src.Height/2.0)-$y
$dst=New-Object System.Drawing.Bitmap 1152,1152
$g=[System.Drawing.Graphics]::FromImage($dst);$g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$attr=New-Object System.Drawing.Imaging.ImageAttributes;$attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipY)
$g.DrawImage($src,[System.Drawing.Rectangle]::new(0,0,1152,1152),($x-32),($y-32),($w+64),($h+64),[System.Drawing.GraphicsUnit]::Pixel,$attr)
$dst.Save(('E:\UnityProjects\WaterSlides\AgentBridge\panorama-tiles\input-'+$row+'-'+$col+'.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose();$dst.Dispose();$attr.Dispose()
}}
$src.Dispose()
