using System;
using System.Threading;

namespace FishingDesktop.Diagnostics.HostPolicy
{
    public sealed class DisplayBarrier
    {
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        long revision;bool blocked;
        void Check(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Display barrier requires its owner thread.");}
        public long Revision {get{Check();return revision;}}
        public bool Blocked {get{Check();return blocked;}}
        public long Begin(){Check();long next=checked(revision+1);blocked=true;return revision=next;}
        public bool Complete(long expectedRevision,bool verified)
        {
            Check();if(!blocked || expectedRevision!=revision || !verified)return false;
            blocked=false;return true;
        }
        public static bool Landscape(int width,int height){return width>0 && height>0 && width>=height;}
    }
}
