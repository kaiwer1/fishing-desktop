using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FishingDesktop.Diagnostics.DesktopLayer;
using FishingDesktop.Diagnostics.DesktopStartup;

static class NativeStartupProbe
{
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [STAThread]static int Main(string[] args)
    {
        bool allow=args.Length==1 && args[0]=="--prepare-missing-layer";
        if(args.Length>0 && !allow)return 5;
        try{
            using(var guard=new HostInstanceGuard()){
                if(!guard.IsOwner)return 2;
                var before=GetForegroundWindow();var screen=Screen.PrimaryScreen.Bounds;
                var api=new Win32DesktopApi();var request=new Win32ShellLayerRequest(api);
                var result=new DesktopStartupCoordinator(api,request).Prepare(new DesktopRect(screen.Left,screen.Top,screen.Right,screen.Bottom),allow);
                bool foreground=before==GetForegroundWindow();
                var report=new{available=result.Available,reusedExisting=result.ReusedExisting,allowMissingLayerRequest=allow,requestAttempted=result.RequestAttempted,
                    nativeMessageAttempts=request.MessageAttempts,reason=result.Reason,nativeError=result.NativeError,foregroundUnchanged=foreground,
                    width=screen.Width,height=screen.Height,windowsCreated=false,attached=false,freshLoginTested=false,gameLoaded=false};
                string json=new JavaScriptSerializer().Serialize(report);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native-summary.json"),json,Encoding.UTF8);
                Console.WriteLine(json);return result.Available && foreground?0:4;
            }
        }catch(Exception){return 3;}
    }
}
