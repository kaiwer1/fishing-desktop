using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using FishingDesktop.Diagnostics.OutputTopology;

static class OutputTopologyProbe
{
    static object PublicSample(TopologySample sample)
    {
        var rows=new List<object>();
        foreach(var row in sample.Outputs)rows.Add(new {key=row.Key,x=row.X,y=row.Y,width=row.Width,height=row.Height,dpiX=row.DpiX,dpiY=row.DpiY,primary=row.Primary});
        return new {captureSerial=sample.CaptureSerial,known=sample.Known,reason=sample.Reason.ToString(),outputs=rows};
    }
    [STAThread]
    static int Main(string[] args)
    {
        if(args.Length!=0)return 2;
        // A stuck native call cannot leave an unbounded diagnostic process. A
        // deadline exit is failure and is never counted as normal cleanup.
        using(var deadline=new Timer(delegate{Environment.Exit(5);},null,10000,Timeout.Infinite)){
            try{
                // Give the launching runner a fixed bounded interval to verify
                // this exact process path and start time before the short probe.
                Thread.Sleep(400);
                var query=new Win32TopologyQuery();var capture=new TopologyCapture(query);
                var first=capture.Capture();var second=capture.Capture();
                bool clean=!query.CleanupFailed&&query.OutstandingWindows==0&&query.WindowsCreated==query.WindowsReleased;
                bool passed=first.Known&&second.Known&&first.CaptureSerial==1&&second.CaptureSerial==2&&clean&&query.WindowsCreated==4*first.Outputs.Count;
                var report=new {schemaVersion=1,passed=passed,samples=new[]{PublicSample(first),PublicSample(second)},
                    windowsCreated=query.WindowsCreated,windowsReleased=query.WindowsReleased,outstandingWindows=query.OutstandingWindows,cleanupConfirmed=clean,
                    currentDeviceOnly=true,physicalMultiMonitorAcceptance=false,mixedDpiAcceptance=false,displaySettingsChanged=false,desktopAttached=false,
                    externalWindowsQueried=false,contentCaptured=false,hostIntegrated=false,normalCompletion=true};
                string target=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native-report.json");
                File.WriteAllText(target,new JavaScriptSerializer().Serialize(report));return passed?0:3;
            }catch(Exception){return 4;}
        }
    }
}
