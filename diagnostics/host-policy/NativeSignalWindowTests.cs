using System;
using System.Runtime.InteropServices;
using System.Threading;
using FishingDesktop.Diagnostics.HostPolicy;

static class NativeSignalWindowTests
{
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
    static void Require(bool value){if(!value)throw new Exception("Invariant failed.");}
    [STAThread]static int Main()
    {
        NativeSignalWindow window=null;
        try{
            int powers=0,sessions=0,displays=0;long sessionId=-1;Exception callbackError=null;
            window=new NativeSignalWindow(delegate(int code,long data){Require(code==4);powers++;},delegate(int code,long data){Require(code==7);sessionId=data;sessions++;},delegate{displays++;},delegate(Exception error){callbackError=error;});
            IntPtr handle=window.Handle;Require(handle!=IntPtr.Zero && IsWindow(handle) && !IsWindowVisible(handle) && GetAncestor(handle,2)==handle);
            Console.WriteLine("PASS current-session registered window exists hidden and top-level");
            Require(!window.DispatchNotification(0x218,99,0) && !window.DispatchNotification(0x111,4,0) && powers==0 && sessions==0);
            Console.WriteLine("PASS unrelated message decoding has no callback");
            Require(window.DispatchNotification(0x218,4,0) && window.DispatchNotification(0x2B1,7,123) && window.DispatchNotification(0x7E,32,0) && window.DispatchNotification(0x2E0,192,Int64.MaxValue) && powers==1 && sessions==1 && displays==2 && sessionId==123 && callbackError==null);
            Console.WriteLine("PASS direct controlled decoder routes notification payloads without broadcasting");
            Exception foreignError=null;var worker=new Thread(delegate(){try{window.Dispose();}catch(Exception error){foreignError=error;}});worker.Start();worker.Join();Require(foreignError is InvalidOperationException && IsWindow(handle));
            Console.WriteLine("PASS foreign thread cannot destroy notification window");
            window.Dispose();window.Dispose();Require(!IsWindow(handle) && !window.DispatchNotification(0x218,4,0));window=null;
            Console.WriteLine("PASS unregister and repeated disposal release only the owned handle");return 0;
        }catch(Exception error){Console.WriteLine("FAIL "+error.GetType().Name+": "+error.Message);return 1;}
        finally{if(window!=null)window.Dispose();}
    }
}
