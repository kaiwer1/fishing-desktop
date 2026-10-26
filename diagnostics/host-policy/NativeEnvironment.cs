using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FishingDesktop.Diagnostics.HostPolicy
{
    public sealed class EnvironmentSample
    {
        public readonly bool Known,InteractiveSession,Fullscreen;
        public readonly string Reason;
        internal EnvironmentSample(bool known,bool interactive,bool fullscreen,string reason){Known=known;InteractiveSession=interactive;Fullscreen=fullscreen;Reason=reason;}
        public PauseReason BlockingReasons {get{return (Known?PauseReason.None:PauseReason.EnvironmentUnknown)|
            (InteractiveSession?PauseReason.None:PauseReason.SessionUnavailable)|(Fullscreen?PauseReason.Fullscreen:PauseReason.None);}}
    }
    // Raw query seam: fake implementations supply observations, not policy
    // decisions. Native allocation/handle lifetime remains in the Win32 adapter.
    public struct EnvironmentWindowRect
    {
        public readonly int Left,Top,Right,Bottom;
        public EnvironmentWindowRect(int left,int top,int right,int bottom){Left=left;Top=top;Right=right;Bottom=bottom;}
    }
    public interface INativeEnvironmentQueries
    {
        bool SessionActive(int sessionId);
        bool DefaultInputDesktop();
        IntPtr ForegroundWindow();
        IntPtr ShellWindow();
        bool WindowExists(IntPtr window);
        bool WindowVisible(IntPtr window);
        bool WindowIconic(IntPtr window);
        bool TryProcessId(IntPtr window,out uint processId);
        bool TryWindowRect(IntPtr window,out EnvironmentWindowRect rect);
    }
    // Read-only sampling: no screenshots, titles, command lines, hooks, input,
    // process mutation or desktop switching. All opened native handles are freed.
    public sealed class NativeEnvironment
    {
        public const PauseReason EnvironmentMask=PauseReason.EnvironmentUnknown|PauseReason.SessionUnavailable|PauseReason.Fullscreen;
        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
        readonly int ownPid,sessionId;
        readonly INativeEnvironmentQueries queries;
        public NativeEnvironment(){using(var process=Process.GetCurrentProcess()){ownPid=process.Id;sessionId=process.SessionId;}queries=new Win32EnvironmentQueries();}
        public NativeEnvironment(INativeEnvironmentQueries queries,int ownProcessId,int currentSessionId)
        {
            if(queries==null)throw new ArgumentNullException("queries");
            if(ownProcessId<=0)throw new ArgumentOutOfRangeException("ownProcessId");
            if(currentSessionId<0)throw new ArgumentOutOfRangeException("currentSessionId");
            this.queries=queries;ownPid=ownProcessId;sessionId=currentSessionId;
        }
        static EnvironmentSample Unknown(bool interactive,string reason){return new EnvironmentSample(false,interactive,false,reason);}
        static bool Valid(Rectangle value){return value.Width>0 && value.Height>0 && (long)value.Left+value.Width<=Int32.MaxValue && (long)value.Top+value.Height<=Int32.MaxValue;}
        // One physical pixel tolerance, including monitors with negative origins.
        public static bool Covers(Rectangle window,Rectangle target)
        {
            return Valid(window) && Valid(target) && (long)window.Left<=target.Left+1L && (long)window.Top<=target.Top+1L &&
                (long)window.Left+window.Width>=(long)target.Left+target.Width-1 && (long)window.Top+window.Height>=(long)target.Top+target.Height-1;
        }
        public EnvironmentSample Capture(Rectangle target)
        {
            if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("Native policy sampling requires its owner thread.");
            if(!Valid(target))throw new ArgumentOutOfRangeException("target");
            if(!queries.SessionActive(sessionId))return Unknown(false,"session-not-active-or-unavailable");
            if(!queries.DefaultInputDesktop())return Unknown(false,"input-desktop-unavailable");
            IntPtr foreground=queries.ForegroundWindow();uint pid;
            if(foreground==IntPtr.Zero || !queries.WindowExists(foreground) || !queries.WindowVisible(foreground) || queries.WindowIconic(foreground) || !queries.TryProcessId(foreground,out pid))return Unknown(true,"foreground-unavailable");
            IntPtr shell=queries.ShellWindow();uint shellPid;
            if(shell==IntPtr.Zero || !queries.TryProcessId(shell,out shellPid))return Unknown(true,"shell-unavailable");
            bool ignored=pid==ownPid || pid==shellPid;
            EnvironmentWindowRect rect=new EnvironmentWindowRect();
            if(!ignored && !queries.TryWindowRect(foreground,out rect))return Unknown(true,"foreground-geometry-unavailable");
            uint afterPid;
            if(queries.ForegroundWindow()!=foreground || !queries.TryProcessId(foreground,out afterPid) || afterPid!=pid)return Unknown(true,"foreground-changed-during-sample");
            if(ignored)return new EnvironmentSample(true,true,false,"own-or-shell-foreground");
            long width=(long)rect.Right-rect.Left,height=(long)rect.Bottom-rect.Top;
            if(width<=0 || height<=0 || width>Int32.MaxValue || height>Int32.MaxValue)return Unknown(true,"invalid-foreground-geometry");
            bool fullscreen=Covers(new Rectangle(rect.Left,rect.Top,(int)width,(int)height),target);
            return new EnvironmentSample(true,true,fullscreen,fullscreen?"target-covered":"target-not-covered");
        }
    }
    sealed class Win32EnvironmentQueries : INativeEnvironmentQueries
    {
        public bool SessionActive(int sessionId)
        {
            IntPtr buffer=IntPtr.Zero;int bytes;
            try{return WTSQuerySessionInformation(IntPtr.Zero,sessionId,8,out buffer,out bytes) && buffer!=IntPtr.Zero && bytes>=4 && Marshal.ReadInt32(buffer)==0;}
            finally{if(buffer!=IntPtr.Zero)WTSFreeMemory(buffer);}
        }
        public bool DefaultInputDesktop()
        {
            IntPtr desktop=OpenInputDesktop(0,false,1); // DESKTOP_READOBJECTS only
            if(desktop==IntPtr.Zero)return false;
            try{
                var name=new StringBuilder(256);uint needed;
                return GetUserObjectInformation(desktop,2,name,name.Capacity*2,out needed) && String.Equals(name.ToString(),"Default",StringComparison.OrdinalIgnoreCase);
            }finally{CloseDesktop(desktop);}
        }
        public IntPtr ForegroundWindow(){return GetForegroundWindow();}
        public IntPtr ShellWindow(){return GetShellWindow();}
        public bool WindowExists(IntPtr window){return IsWindow(window);}
        public bool WindowVisible(IntPtr window){return IsWindowVisible(window);}
        public bool WindowIconic(IntPtr window){return IsIconic(window);}
        public bool TryProcessId(IntPtr window,out uint processId){return GetWindowThreadProcessId(window,out processId)!=0;}
        public bool TryWindowRect(IntPtr window,out EnvironmentWindowRect rect)
        {
            RECT native;bool success=GetWindowRect(window,out native);
            rect=new EnvironmentWindowRect(native.Left,native.Top,native.Right,native.Bottom);return success;
        }
        [StructLayout(LayoutKind.Sequential)]struct RECT{public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rect);
        [DllImport("user32.dll")]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
        [DllImport("user32.dll",EntryPoint="GetUserObjectInformationW",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder value,int bytes,out uint needed);
        [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("wtsapi32.dll",EntryPoint="WTSQuerySessionInformationW")]static extern bool WTSQuerySessionInformation(IntPtr server,int session,int info,out IntPtr buffer,out int bytes);
        [DllImport("wtsapi32.dll")]static extern void WTSFreeMemory(IntPtr buffer);
    }
}
