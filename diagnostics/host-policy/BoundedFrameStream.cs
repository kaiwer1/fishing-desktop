using System;
using System.Drawing;
using System.IO;

namespace FishingDesktop.Diagnostics.HostPolicy
{
    // A PNG produced by our own WebView, never an external desktop capture.
    // Enforce byte limits on every write/seek/length path before allocating.
    public sealed class BoundedFrameStream : Stream
    {
        readonly MemoryStream inner=new MemoryStream();
        readonly object sync=new object();
        readonly long maximum;
        bool disposed;
        public BoundedFrameStream(long maximumBytes){if(maximumBytes<24 || maximumBytes>Int32.MaxValue)throw new ArgumentOutOfRangeException("maximumBytes");maximum=maximumBytes;}
        void Check(){if(disposed)throw new ObjectDisposedException("BoundedFrameStream");}
        void Limit(long value){if(value<0 || value>maximum)throw new IOException("Own-frame byte budget exceeded.");}
        public override bool CanRead {get{return !disposed;}}public override bool CanSeek {get{return !disposed;}}public override bool CanWrite {get{return !disposed;}}
        public override long Length {get{lock(sync){Check();return inner.Length;}}}
        public override long Position {get{lock(sync){Check();return inner.Position;}}set{lock(sync){Check();Limit(value);inner.Position=value;}}}
        public override void Flush(){lock(sync){Check();inner.Flush();}}
        public override int Read(byte[] buffer,int offset,int count){lock(sync){Check();return inner.Read(buffer,offset,count);}}
        public override long Seek(long offset,SeekOrigin origin)
        {
            lock(sync){Check();long basis=origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?inner.Position:origin==SeekOrigin.End?inner.Length:-1;
                if(basis<0)throw new ArgumentOutOfRangeException("origin");long target;try{target=checked(basis+offset);}catch(OverflowException){throw new IOException("Own-frame seek overflow.");}
                Limit(target);inner.Position=target;return target;}
        }
        public override void SetLength(long value){lock(sync){Check();Limit(value);inner.SetLength(value);}}
        public override void Write(byte[] buffer,int offset,int count)
        {
            lock(sync){Check();if(buffer==null)throw new ArgumentNullException("buffer");if(offset<0 || count<0 || offset>buffer.Length-count)throw new ArgumentOutOfRangeException("count");Limit(inner.Position+count);inner.Write(buffer,offset,count);}
        }
        public override void WriteByte(byte value){lock(sync){Check();Limit(inner.Position+1);inner.WriteByte(value);}}
        static uint Big(byte[] bytes,int at){return ((uint)bytes[at]<<24)|((uint)bytes[at+1]<<16)|((uint)bytes[at+2]<<8)|bytes[at+3];}
        public Bitmap Decode(long maximumPixels)
        {
            lock(sync){
                Check();if(maximumPixels<=0)throw new ArgumentOutOfRangeException("maximumPixels");
                var header=new byte[24];inner.Position=0;
                if(inner.Read(header,0,header.Length)!=header.Length || header[0]!=137 || header[1]!=80 || header[2]!=78 || header[3]!=71 || header[4]!=13 || header[5]!=10 || header[6]!=26 || header[7]!=10 || Big(header,8)!=13 || header[12]!=73 || header[13]!=72 || header[14]!=68 || header[15]!=82)throw new InvalidDataException("Own-frame PNG header invalid.");
                uint width=Big(header,16),height=Big(header,20);
                if(width==0 || height==0 || width>Int32.MaxValue || height>Int32.MaxValue || (ulong)width*height>(ulong)maximumPixels)throw new InvalidDataException("Own-frame pixel budget exceeded.");
                inner.Position=0;
                using(var decoded=Image.FromStream(inner)){
                    if(decoded.Width!=width || decoded.Height!=height)throw new InvalidDataException("Own-frame dimensions inconsistent.");
                    return new Bitmap(decoded); // Independent of the capture stream.
                }
            }
        }
        protected override void Dispose(bool disposing){lock(sync){if(!disposed){disposed=true;if(disposing)inner.Dispose();}}base.Dispose(disposing);}
    }
}
