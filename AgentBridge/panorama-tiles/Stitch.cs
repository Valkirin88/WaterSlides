using System;using System.Drawing;using System.Drawing.Imaging;using System.Runtime.InteropServices;using System.IO;
public class StitchPanorama {
 public static void Run(string dir,string output) {
 const int W=4096,H=2048; float[] sums=new float[W*H*4];
 for(int row=0;row<2;row++)for(int col=0;col<4;col++) {
 int sx=(int)Math.Round(col*1774.0/4),sy=(int)Math.Round(row*887.0/2);
 int sw=(int)Math.Round((col+1)*1774.0/4)-sx,sh=(int)Math.Round((row+1)*887.0/2)-sy;
 int dx=(int)Math.Round((sx-32)*W/1774.0),dy=(int)Math.Round((sy-32)*H/887.0);
 int dw=(int)Math.Round((sw+64)*W/1774.0),dh=(int)Math.Round((sh+64)*H/887.0);
 using(var src=new Bitmap(Path.Combine(dir,"detail-"+row+"-"+col+".png")))using(var scaled=new Bitmap(dw,dh,PixelFormat.Format32bppArgb)) {
 using(var g=Graphics.FromImage(scaled)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(src,0,0,dw,dh);}
 var data=scaled.LockBits(new Rectangle(0,0,dw,dh),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
 byte[] b=new byte[data.Stride*dh];Marshal.Copy(data.Scan0,b,0,b.Length);scaled.UnlockBits(data);
 float bx=(float)(32.0*dw/(sw+64)),by=(float)(32.0*dh/(sh+64));
 for(int y=0;y<dh;y++){int oy=dy+y;if(oy<0||oy>=H)continue;float wy=Math.Min(1,Math.Min((y+.5f)/by,(dh-y-.5f)/by));
 for(int x=0;x<dw;x++){int ox=(dx+x+W)%W;float wx=Math.Min(1,Math.Min((x+.5f)/bx,(dw-x-.5f)/bx));float w=wx*wy;int a=(oy*W+ox)*4,k=y*dw*4+x*4;for(int c=0;c<3;c++)sums[a+c]+=b[k+c]*w;sums[a+3]+=w;}}
 }}
 using(var dst=new Bitmap(W,H,PixelFormat.Format32bppArgb)){
 byte[] b=new byte[W*H*4];for(int i=0;i<b.Length;i+=4){if(sums[i+3]<=0)throw new Exception("Uncovered pixel");for(int c=0;c<3;c++)b[i+c]=(byte)Math.Max(0,Math.Min(255,Math.Round(sums[i+c]/sums[i+3])));b[i+3]=255;}
 var data=dst.LockBits(new Rectangle(0,0,W,H),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);Marshal.Copy(b,0,data.Scan0,b.Length);dst.UnlockBits(data);dst.Save(output,ImageFormat.Png);
 using(var preview=new Bitmap(1600,800)){using(var g=Graphics.FromImage(preview)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(dst,0,0,1600,800);}preview.Save(Path.Combine(dir,"assembled-preview.png"),ImageFormat.Png);}
 }
 }
}