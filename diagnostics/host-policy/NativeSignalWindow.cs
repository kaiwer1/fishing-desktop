using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace FishingDesktop.Diagnostics.HostPolicy
{
    // A hidden top-level window receives broadcasts; a message-only HWND would
    // not receive all power broadcasts. It is never shown, activated or parented
    // into Explorer, and outlives either WebView independently.
    public sealed class NativeSignalWindow : NativeWindow,IDisposable
    {
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        readonly Action<int,long> power,session;
        readonly Action<Exception> failure;
        bool registered,closed;
        public NativeSignalWindow(Action<int,long> onPower,Action<int,long> onSession,Action<Exception> onFailure)
        {
            if(onPower==null || onSession==null || onFailure==null)throw new ArgumentNullException("callback");
            power=onPower;session=onSession;failure=onFailure;
            try{
                CreateHandle(new CreateParams{Caption="Fishing policy notifications",Style=unchecked((int)0x80000000),ExStyle=0x08000080,Width=0,Height=0});
                if(!WTSRegisterSessionNotification(Handle,0))throw new Win32Exception(Marshal.GetLastWin32Error(),"Current-session notification registration failed.");
                registered=true;
            }catch{if(Handle!=IntPtr.Zero)DestroyHandle();throw;}
        }
        internal bool DispatchNotification(int message,long code,long data)
        {
            Check();if(closed)return false;
            if(message==0x218 && (code==4 || code==7 || code==18)){power((int)code,data);return true;}
            if(message==0x2B1 && code>=1 && code<=15){session((int)code,data);return true;}
            return false;
        }
        protected override void WndProc(ref Message message)
        {
            try{
                if(DispatchNotification(message.Msg,message.WParam.ToInt64(),message.LParam.ToInt64()) && message.Msg==0x218){message.Result=new IntPtr(1);return;}
            }catch(Exception error){failure(error);}
            base.WndProc(ref message);
        }
        void Check(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Notification window requires its owner thread.");}
        public void Dispose()
        {
            Check();if(closed)return;closed=true;
            bool unregistered=true;int error=0;
            if(registered){unregistered=WTSUnRegisterSessionNotification(Handle);error=Marshal.GetLastWin32Error();registered=false;}
            if(Handle!=IntPtr.Zero)DestroyHandle();
            if(!unregistered)throw new Win32Exception(error,"Session notification unregister failed.");
        }
        [DllImport("wtsapi32.dll",SetLastError=true)]static extern bool WTSRegisterSessionNotification(IntPtr window,uint flags);
        [DllImport("wtsapi32.dll",SetLastError=true)]static extern bool WTSUnRegisterSessionNotification(IntPtr window);
    }
}
