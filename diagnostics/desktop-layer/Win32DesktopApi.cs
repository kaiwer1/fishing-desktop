using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FishingDesktop.Diagnostics.DesktopLayer
{
    // This adapter has no show/activate API and never sends Explorer messages.
    // All mutation entry points re-check the current process AND UI thread.
    public sealed class Win32DesktopApi : IDesktopLayerApi
    {
        const int GwlStyle=-16, GwlExStyle=-20;
        const uint GaParent=1;
        readonly int processId,ownerThread;
        readonly string explorerPath;
        ProcessIdentity trustedShell;
        long trustedShellWindow;
        public Win32DesktopApi(){processId=Process.GetCurrentProcess().Id;ownerThread=(int)GetCurrentThreadId();explorerPath=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"));}
        public int CurrentProcessId {get{return processId;}}
        public int CurrentThreadId {get{return (int)GetCurrentThreadId();}}
        public string ExpectedExplorerPath {get{return explorerPath;}}
        static IntPtr H(long handle){return new IntPtr(handle);}
        static long L(IntPtr handle){return handle.ToInt64();}
        static long ParentOf(long handle){long parent=L(GetAncestor(H(handle),GaParent));return parent==L(GetDesktopWindow())?0:parent;}
        static int Owner(long handle,out int thread){uint pid;thread=(int)GetWindowThreadProcessId(H(handle),out pid);return (int)pid;}
        static ProcessIdentity Identity(int id)
        {
            using(var process=Process.GetProcessById(id)){
                var identity=new ProcessIdentity{Id=id,StartTimeUtcTicks=process.StartTime.ToUniversalTime().Ticks,Path=Path.GetFullPath(process.MainModule.FileName)};
                if(process.HasExited)return null;
                return identity;
            }
        }
        static string Class(long handle)
        {
            var name=new StringBuilder(256);
            if(GetClassName(H(handle),name,name.Capacity)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            return name.ToString();
        }
        static long GetStyle(long handle,int index)
        {
            SetLastError(0);
            long value=IntPtr.Size==8?L(GetWindowLongPtr64(H(handle),index)):GetWindowLong32(H(handle),index);
            int error=Marshal.GetLastWin32Error();
            if(value==0 && error!=0)throw new Win32Exception(error);
            return value&0xffffffffL;
        }
        static WindowState ReadCore(long handle)
        {
            if(!IsWindow(H(handle)))return null;
            int thread;int pid=Owner(handle,out thread);
            RECT rect;
            if(pid<=0 || thread<=0 || !GetWindowRect(H(handle),out rect))return null;
            var window=new WindowState{Handle=handle,Parent=ParentOf(handle),ProcessId=pid,ThreadId=thread,
                ClassName=Class(handle),Visible=IsWindowVisible(H(handle)),Bounds=new DesktopRect(rect.Left,rect.Top,rect.Right,rect.Bottom),
                Style=GetStyle(handle,GwlStyle),ExtendedStyle=GetStyle(handle,GwlExStyle),DpiContext=L(GetWindowDpiAwarenessContext(H(handle)))};
            int afterThread;
            if(!IsWindow(H(handle)) || Owner(handle,out afterThread)!=pid || afterThread!=thread)return null;
            return window;
        }
        bool OnOwnerThread(){return CurrentThreadId==ownerThread;}
        bool IsOwn(long window)
        {
            int thread;return OnOwnerThread() && window!=0 && IsWindow(H(window)) && Owner(window,out thread)==processId && thread==ownerThread;
        }
        bool ShellStillMatches()
        {
            try{
                int thread;
                return trustedShell!=null && L(GetShellWindow())==trustedShellWindow && IsWindow(H(trustedShellWindow)) &&
                    Owner(trustedShellWindow,out thread)==trustedShell.Id && trustedShell.Same(Identity(trustedShell.Id));
            }catch(Exception){return false;}
        }
        bool TrustedParent(long parent)
        {
            if(parent==0)return true;
            int thread;
            return ShellStillMatches() && IsWindow(H(parent)) && Owner(parent,out thread)==trustedShell.Id &&
                ParentOf(parent)==0 && Class(parent)=="WorkerW";
        }
        bool UnderOwnBranch(long window,long root)
        {
            var visited=new HashSet<long>();long cursor=window;
            for(int depth=0;depth<64 && cursor!=0 && cursor!=root && visited.Add(cursor);depth++){
                int thread;
                if(Owner(cursor,out thread)==processId && thread==ownerThread)return true;
                cursor=ParentOf(cursor);
            }
            return false;
        }
        public DesktopSnapshot CaptureDesktop()
        {
            var snapshot=new DesktopSnapshot();
            if(!OnOwnerThread())return snapshot;
            trustedShell=null;trustedShellWindow=0;
            long shell=L(GetShellWindow());int shellThread;
            if(shell==0 || !IsWindow(H(shell)))return snapshot;
            int shellPid=Owner(shell,out shellThread);
            var identity=Identity(shellPid);
            if(identity==null || !String.Equals(identity.Path,explorerPath,StringComparison.OrdinalIgnoreCase))return snapshot;
            snapshot.ShellWindow=shell;snapshot.ShellProcess=identity;
            snapshot.ThreadDpiContext=L(GetThreadDpiAwarenessContext());
            int left=GetSystemMetrics(76),top=GetSystemMetrics(77),width=GetSystemMetrics(78),height=GetSystemMetrics(79);
            snapshot.VirtualBounds=new DesktopRect(left,top,checked(left+width),checked(top+height));
            bool valid=true;
            EnumWindowsProc collectTop=delegate(IntPtr hwnd,IntPtr parameter){
                try{
                long handle=L(hwnd);int thread;
                if(Owner(handle,out thread)!=shellPid)return true;
                var window=ReadCore(handle);
                if(window==null || window.ProcessId!=shellPid){valid=false;return false;}
                snapshot.TopLevels.Add(window);return true;
                }catch(Exception){valid=false;return false;}
            };
            if(!EnumWindows(collectTop,IntPtr.Zero))valid=false;
            foreach(var topWindow in snapshot.TopLevels){
                EnumWindowsProc collectChild=delegate(IntPtr hwnd,IntPtr parameter){
                    try{
                    long handle=L(hwnd);int thread;int pid=Owner(handle,out thread);
                    if(pid==shellPid){var child=ReadCore(handle);if(child==null || child.ProcessId!=shellPid){valid=false;return false;}snapshot.Descendants.Add(child);}
                    else if(!UnderOwnBranch(handle,topWindow.Handle))topWindow.ForeignDescendantCount++;
                    return true;
                    }catch(Exception){valid=false;return false;}
                };
                // EnumChildWindows returns zero both for no children and when
                // the callback ends; the callback's flag carries query failure.
                EnumChildWindows(H(topWindow.Handle),collectChild,IntPtr.Zero);
                if(!valid)break;
            }
            int afterThread;
            snapshot.Stable=valid && L(GetShellWindow())==shell && IsWindow(H(shell)) && Owner(shell,out afterThread)==shellPid &&
                afterThread==shellThread && identity.Same(Identity(shellPid));
            if(snapshot.Stable){trustedShell=identity;trustedShellWindow=shell;}
            return snapshot;
        }
        public WindowState ReadWindow(long handle)
        {
            if(!OnOwnerThread())return null;
            int thread;int pid=Owner(handle,out thread);
            if(pid!=processId && !(trustedShell!=null && pid==trustedShell.Id && ShellStillMatches()))return null;
            return ReadCore(handle);
        }
        public bool DpiContextsEqual(long left,long right)
        {
            if(left==0 || right==0)return false;
            try{return AreDpiAwarenessContextsEqual(H(left),H(right));}catch(EntryPointNotFoundException){return false;}
        }
        NativeResult SetBits(long window,long bits,int index)
        {
            if(!IsOwn(window))return NativeResult.Error(5);
            SetLastError(0);
            long previous=IntPtr.Size==8?L(SetWindowLongPtr64(H(window),index,H(bits))):SetWindowLong32(H(window),index,unchecked((int)bits));
            return NativeResult.FromPreviousValue(previous,Marshal.GetLastWin32Error());
        }
        public NativeResult SetStyle(long window,long style){return SetBits(window,style,GwlStyle);}
        public NativeResult SetExtendedStyle(long window,long style){return SetBits(window,style,GwlExStyle);}
        public NativeResult SetParent(long window,long parent)
        {
            if(!IsOwn(window) || !TrustedParent(parent))return NativeResult.Error(5);
            if(parent!=0 && !DpiContextsEqual(L(GetWindowDpiAwarenessContext(H(window))),L(GetWindowDpiAwarenessContext(H(parent)))))return NativeResult.Error(87);
            // All prior checks may call native functions. Clear last error last.
            SetLastError(0);long previous=L(NativeSetParent(H(window),H(parent)));int error=Marshal.GetLastWin32Error();
            return NativeResult.FromPreviousValue(previous,error);
        }
        public NativeResult Position(long window,long parent,DesktopRect screenBounds,uint flags)
        {
            if(!IsOwn(window) || !TrustedParent(parent))return NativeResult.Error(5);
            if(!screenBounds.IsValid || flags!=DesktopLayerController.SafePositionFlags || ParentOf(window)!=parent)return NativeResult.Error(87);
            var point=new POINT{X=screenBounds.Left,Y=screenBounds.Top};
            if(parent!=0){SetLastError(0);int mapped=MapWindowPoints(IntPtr.Zero,H(parent),ref point,1);int mapError=Marshal.GetLastWin32Error();if(mapped==0 && mapError!=0)return NativeResult.Error(mapError);}
            if(!IsOwn(window) || !TrustedParent(parent) || ParentOf(window)!=parent)return NativeResult.Error(5);
            SetLastError(0);
            bool success=SetWindowPos(H(window),IntPtr.Zero,point.X,point.Y,screenBounds.Right-screenBounds.Left,screenBounds.Bottom-screenBounds.Top,flags);
            return success?NativeResult.Ok():NativeResult.Error(Marshal.GetLastWin32Error());
        }
        public NativeResult Hide(long window)
        {
            if(!IsOwn(window))return NativeResult.Error(5);
            // ShowWindow's return is previous visibility, not a success flag.
            ShowWindow(H(window),0);
            return !IsWindow(H(window)) || (IsOwn(window) && !IsWindowVisible(H(window)))?NativeResult.Ok():NativeResult.Error(5);
        }
        public NativeResult Close(long window)
        {
            if(!IsOwn(window))return NativeResult.Error(5);
            SetLastError(0);bool success=DestroyWindow(H(window));int error=Marshal.GetLastWin32Error();
            return success && !IsWindow(H(window))?NativeResult.Ok():NativeResult.Error(error);
        }
        [StructLayout(LayoutKind.Sequential)]struct RECT{public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)]struct POINT{public int X,Y;}
        delegate bool EnumWindowsProc(IntPtr hwnd,IntPtr parameter);
        [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")]static extern void SetLastError(uint error);
        [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")]static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")]static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
        [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern int GetClassName(IntPtr hwnd,StringBuilder className,int maxCount);
        [DllImport("user32.dll",SetLastError=true)]static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)]static extern IntPtr GetWindowLongPtr64(IntPtr hwnd,int index);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW",SetLastError=true)]static extern int GetWindowLong32(IntPtr hwnd,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]static extern IntPtr SetWindowLongPtr64(IntPtr hwnd,int index,IntPtr value);
        [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)]static extern int SetWindowLong32(IntPtr hwnd,int index,int value);
        [DllImport("user32.dll",EntryPoint="SetParent",SetLastError=true)]static extern IntPtr NativeSetParent(IntPtr hwnd,IntPtr parent);
        [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll",SetLastError=true)]static extern int MapWindowPoints(IntPtr from,IntPtr to,ref POINT point,uint count);
        [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr hwnd,int command);
        [DllImport("user32.dll",SetLastError=true)]static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll",SetLastError=true)]static extern bool EnumWindows(EnumWindowsProc callback,IntPtr parameter);
        [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr parent,EnumWindowsProc callback,IntPtr parameter);
        [DllImport("user32.dll")]static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")]static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")]static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")]static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
    }
}
