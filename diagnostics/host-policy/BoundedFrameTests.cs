using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FishingDesktop.Diagnostics.HostPolicy;

static class BoundedFrameTests
{
    static int passed,failed;
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Deny(Action action){bool denied=false;try{action();}catch(IOException){denied=true;}Need(denied,"Oversized operation accepted.");}
    static void DenyData(Action action){bool denied=false;try{action();}catch(InvalidDataException){denied=true;}Need(denied,"Invalid image accepted.");}
    static void Test(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static int Main()
    {
        Test("capture bytes cannot exceed budget through write or write-byte",()=>{using(var s=new BoundedFrameStream(24)){s.Write(new byte[24],0,24);Deny(()=>s.WriteByte(1));Deny(()=>s.Write(new byte[1],0,1));Need(s.Length==24,"Denied write changed length.");}});
        Test("seek position and set-length cannot bypass byte budget",()=>{using(var s=new BoundedFrameStream(24)){Deny(()=>s.Position=25);Deny(()=>s.SetLength(25));Deny(()=>s.Seek(25,SeekOrigin.Begin));s.Position=24;Deny(()=>s.Seek(Int64.MaxValue,SeekOrigin.Current));Need(s.Length==0,"Invalid seek allocated storage.");}});
        Test("invalid PNG is rejected before image decoding",()=>{using(var s=new BoundedFrameStream(128)){s.Write(new byte[24],0,24);DenyData(()=>s.Decode(100));}});
        Test("declared oversized PNG dimensions are rejected before image allocation",()=>{using(var s=new BoundedFrameStream(128)){var h=new byte[]{137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82,0,1,0,0,0,1,0,0};s.Write(h,0,h.Length);DenyData(()=>s.Decode(8388608));}});
        Test("decoded own bitmap survives capture stream disposal with correct pixels",()=>{Bitmap result;using(var s=new BoundedFrameStream(65536)){using(var image=new Bitmap(32,20)){using(var g=Graphics.FromImage(image))g.Clear(Color.FromArgb(113,217,165));image.Save(s,ImageFormat.Png);}result=s.Decode(1024);}using(result){Need(result.Width==32 && result.Height==20 && result.GetPixel(5,5).ToArgb()==Color.FromArgb(113,217,165).ToArgb(),"Decoded bitmap did not own its pixels.");}});
        Test("disposed capture stream refuses subsequent writes",()=>{var s=new BoundedFrameStream(24);s.Dispose();bool denied=false;try{s.WriteByte(1);}catch(ObjectDisposedException){denied=true;}Need(denied && !s.CanWrite,"Disposed stream still writable.");});
        Console.WriteLine("RESULT "+passed+" passed / "+failed+" failed");return failed==0?0:1;
    }
}
