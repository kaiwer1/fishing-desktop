using System;
using System.Runtime.InteropServices;
using System.Drawing;
using FishingDesktop.Diagnostics.DesktopLayer;
using FishingDesktop.Diagnostics.HostPolicy;

namespace FishingDesktop.Diagnostics.DesktopStartup
{
    public sealed class Win32ShellLayerRequest : IShellLayerRequest
    {
        readonly Win32DesktopApi api;
        readonly int thread;
        readonly NativeEnvironment environment=new NativeEnvironment();
        bool attempted;
        public int MessageAttempts {get{return attempted?1:0;}}
        public Win32ShellLayerRequest(Win32DesktopApi api){if(api==null)throw new ArgumentNullException("api");this.api=api;thread=api.CurrentThreadId;}
        public NativeResult Request(ShellLayerIntent intent)
        {
            if(attempted || intent==null || api.CurrentThreadId!=thread)return NativeResult.Error(87);
            var target=intent.Target;
            var sample=environment.Capture(new Rectangle(target.Left,target.Top,target.Right-target.Left,target.Bottom-target.Top));
            if(!sample.Known || !sample.InteractiveSession)return NativeResult.Error(5);
            var current=DesktopStartupCoordinator.MissingIntent(api,api.CaptureDesktop(),intent.Target);
            if(!intent.Matches(current))return NativeResult.Error(1168);
            attempted=true;
            UIntPtr result;SetLastError(0);
            // Fixed scalar-only shell protocol, observed in Lively v2.2.1.0.
            // No broadcast, no retry, no NOTIMEOUTIFNOTHUNG, no external HWND mutation.
            IntPtr sent=SendMessageTimeout(new IntPtr(intent.ShellWindow),0x052C,new UIntPtr(0xD),new IntPtr(1),0x0001|0x0002|0x0020,1000,out result);
            int error=Marshal.GetLastWin32Error();return sent!=IntPtr.Zero?NativeResult.Ok():NativeResult.Error(error);
        }
        [DllImport("kernel32.dll")]static extern void SetLastError(uint code);
        [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW",SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,UIntPtr wParam,IntPtr lParam,uint flags,uint timeout,out UIntPtr result);
    }
}
