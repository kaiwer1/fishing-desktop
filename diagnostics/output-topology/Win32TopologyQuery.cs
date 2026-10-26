using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FishingDesktop.Diagnostics.OutputTopology
{
    // Only monitor metadata and this adapter's immediately-created HWND are
    // queried. No display-setting, awareness-setting, show, focus, desktop,
    // external-window, registry, EDID or content-capture API is imported here.
    public sealed class Win32TopologyQuery : ITopologyQuery
    {
        const uint Popup=0x80000000,Visible=0x10000000,NoActivate=0x08000000,ToolWindow=0x00000080;
        readonly uint ownerThread,processId;
        bool poisoned;
        int created,released;
        public Win32TopologyQuery()
        {
            ownerThread=GetCurrentThreadId();
            using(var process=Process.GetCurrentProcess()){processId=(uint)process.Id;}
        }
        public int WindowsCreated {get{return created;}}
        public int WindowsReleased {get{return released;}}
        public int OutstandingWindows {get{return created-released;}}
        public bool CleanupFailed {get{return poisoned;}}
        bool OnOwnerThread(){return GetCurrentThreadId()==ownerThread;}
        static bool PerMonitorV2(IntPtr context)
        {return context!=IntPtr.Zero&&AreDpiAwarenessContextsEqual(context,new IntPtr(-4));}
        public bool IsPerMonitorV2
        {
            get{
                if(!OnOwnerThread()||poisoned)return false;
                try{return PerMonitorV2(GetThreadDpiAwarenessContext());}
                catch(EntryPointNotFoundException){return false;}
                catch(DllNotFoundException){return false;}
            }
        }
        static RawMonitor ReadMonitor(IntPtr monitor)
        {
            if(monitor==IntPtr.Zero)return null;
            var info=new MONITORINFO();info.Size=(uint)Marshal.SizeOf(typeof(MONITORINFO));
            if(!GetMonitorInfo(monitor,ref info))return null;
            long width=(long)info.Bounds.Right-info.Bounds.Left,height=(long)info.Bounds.Bottom-info.Bounds.Top;
            if(width<=0||width>Int32.MaxValue||height<=0||height>Int32.MaxValue)return null;
            return new RawMonitor(monitor.ToInt64(),info.Bounds.Left,info.Bounds.Top,(int)width,(int)height,(info.Flags&1)!=0);
        }
        public IEnumerable<RawMonitor> Enumerate()
        {
            if(!IsPerMonitorV2)return null;
            var rows=new List<RawMonitor>();bool valid=true;
            MonitorEnum callback=delegate(IntPtr monitor,IntPtr dc,IntPtr rectangle,IntPtr data){
                try{
                    // A hard native cap also bounds work before pure validation.
                    if(rows.Count>=64){valid=false;return false;}
                    var row=ReadMonitor(monitor);if(row==null){valid=false;return false;}rows.Add(row);return true;
                }catch(Exception){valid=false;return false;}
            };
            bool completed=EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,callback,IntPtr.Zero);
            GC.KeepAlive(callback);
            return completed&&valid&&IsPerMonitorV2?rows:null;
        }
        bool Own(IntPtr window)
        {
            if(!OnOwnerThread()||window==IntPtr.Zero||!IsWindow(window))return false;
            uint pid;return GetWindowThreadProcessId(window,out pid)==ownerThread&&pid==processId;
        }
        static uint Style(IntPtr window,int index)
        {return unchecked((uint)(IntPtr.Size==8?GetWindowLongPtr(window,index).ToInt64():GetWindowLong(window,index)));}
        static bool Positioned(IntPtr window,int x,int y)
        {
            RECT rect;return GetWindowRect(window,out rect)&&rect.Left==x&&rect.Top==y&&(long)rect.Right==x+1L&&(long)rect.Bottom==y+1L;
        }
        static DpiProbeResult Failed(bool released)
        {return new DpiProbeResult(0,false,false,false,false,false,released);}
        public DpiProbeResult Measure(RawMonitor monitor)
        {
            if(monitor==null||!IsPerMonitorV2||!TopologyValidation.RectValid(monitor.X,monitor.Y,monitor.Width,monitor.Height))return Failed(!poisoned);
            int x=(int)((long)monitor.X+monitor.Width/2),y=(int)((long)monitor.Y+monitor.Height/2);
            // Avoid CW_USEDEFAULT even for a deliberately hostile fake rectangle.
            if(x==Int32.MinValue||y==Int32.MinValue)return Failed(true);
            if(!TopologyValidation.Same(monitor,ReadMonitor(new IntPtr(monitor.Handle))))return Failed(true);
            IntPtr window=IntPtr.Zero;uint dpi=0;
            bool own=false,hidden=false,noActivate=false,awareness=false,matches=false,clean=true;
            try{
                window=CreateWindowEx(NoActivate|ToolWindow,"STATIC",String.Empty,Popup,x,y,1,1,IntPtr.Zero,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);
                if(window!=IntPtr.Zero){
                    created++;
                    own=Own(window);
                    if(own){
                        hidden=!IsWindowVisible(window)&&(Style(window,-16)&Visible)==0;
                        noActivate=(Style(window,-20)&NoActivate)!=0;
                        awareness=PerMonitorV2(GetWindowDpiAwarenessContext(window))&&IsPerMonitorV2;
                        matches=Positioned(window,x,y)&&MonitorFromWindow(window,0).ToInt64()==monitor.Handle;
                        if(hidden&&noActivate&&awareness&&matches){
                            uint first=GetDpiForWindow(window),second=GetDpiForWindow(window);
                            if(first==second)dpi=first;
                        }
                        // Recheck the complete window scope after querying DPI.
                        own=Own(window);
                        hidden=hidden&&own&&!IsWindowVisible(window)&&(Style(window,-16)&Visible)==0;
                        noActivate=noActivate&&own&&(Style(window,-20)&NoActivate)!=0;
                        awareness=awareness&&own&&PerMonitorV2(GetWindowDpiAwarenessContext(window))&&IsPerMonitorV2;
                        matches=matches&&own&&Positioned(window,x,y)&&MonitorFromWindow(window,0).ToInt64()==monitor.Handle&&
                            TopologyValidation.Same(monitor,ReadMonitor(new IntPtr(monitor.Handle)));
                    }
                }
            }catch(Exception){dpi=0;}
            finally{
                if(window!=IntPtr.Zero){
                    // This exact HWND came from this synchronous call; do not
                    // adopt a replacement by name, title or process alone.
                    clean=Own(window)&&DestroyWindow(window)&&!IsWindow(window);
                    if(clean)released++;else poisoned=true;
                }
            }
            return new DpiProbeResult(dpi,own,hidden,noActivate,awareness,matches,clean);
        }
        [StructLayout(LayoutKind.Sequential)]struct RECT{public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)]struct MONITORINFO{public uint Size;public RECT Bounds,Work;public uint Flags;}
        delegate bool MonitorEnum(IntPtr monitor,IntPtr dc,IntPtr rectangle,IntPtr data);
        [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")]static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")]static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
        [DllImport("user32.dll")]static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
        [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")]static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorEnum callback,IntPtr data);
        [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")]static extern bool GetMonitorInfo(IntPtr monitor,ref MONITORINFO info);
        [DllImport("user32.dll",EntryPoint="CreateWindowExW",CharSet=CharSet.Unicode)]static extern IntPtr CreateWindowEx(uint extendedStyle,string className,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr module,IntPtr parameter);
        [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rectangle);
        [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr window);
    }
}
