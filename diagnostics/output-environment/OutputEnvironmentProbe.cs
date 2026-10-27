using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using FishingDesktop.Diagnostics.OutputEnvironment;
using FishingDesktop.Diagnostics.OutputTopology;

static class OutputEnvironmentProbe
{
    // Explicit caller topology token, intentionally unrelated to capture serial.
    const long DeclaredTopologyGeneration=7;
    static object PublicSample(OutputEnvironmentSample sample)
    {
        var rows=new List<object>();foreach(var row in sample.Outputs)rows.Add(new{key=row.Key,fullscreen=row.Fullscreen});
        return new{topologyGeneration=sample.TopologyGeneration,known=sample.Known,reason=sample.Reason.ToString(),globalBlocks=sample.GlobalBlocks.ToString(),outputs=rows};
    }
    static bool SameTopology(TopologySample a,TopologySample b)
    {
        if(!a.Known||!b.Known||a.Outputs.Count!=b.Outputs.Count)return false;
        for(int i=0;i<a.Outputs.Count;i++){
            var x=a.Outputs[i];var y=b.Outputs[i];
            if(x.Key!=y.Key||x.X!=y.X||x.Y!=y.Y||x.Width!=y.Width||x.Height!=y.Height||x.DpiX!=y.DpiX||x.DpiY!=y.DpiY||x.Primary!=y.Primary)return false;
        }
        return true;
    }
    static bool ValidSample(OutputEnvironmentSample sample,int targetCount)
    {
        if(sample.TopologyGeneration!=DeclaredTopologyGeneration)return false;
        if(!sample.Known)return sample.Outputs.Count==0&&(sample.GlobalBlocks&OutputEnvironmentGlobalBlock.EnvironmentUnknown)!=0;
        return sample.Outputs.Count==targetCount&&sample.GlobalBlocks==OutputEnvironmentGlobalBlock.None;
    }
    [STAThread]
    static int Main(string[] args)
    {
        if(args.Length!=0)return 2;
        using(var deadline=new Timer(delegate{Environment.Exit(5);},null,10000,Timeout.Infinite)){
            try{
                Thread.Sleep(400);
                var query=new Win32TopologyQuery();var topology=new TopologyCapture(query);var before=topology.Capture();
                var samples=new List<OutputEnvironmentSample>();var publicSamples=new List<object>();int known=0,unknown=0;bool anyFullscreen=false;
                if(before.Known){
                    var targets=new List<OutputEnvironmentTarget>();foreach(var row in before.Outputs)targets.Add(new OutputEnvironmentTarget(row.Key,row.X,row.Y,row.Width,row.Height));
                    var environment=OutputEnvironmentCapture.CreateForCurrentProcess();
                    // Every call obtains a new whole-environment observation.
                    for(int i=0;i<2;i++){
                        var sample=environment.Capture(DeclaredTopologyGeneration,targets);samples.Add(sample);publicSamples.Add(PublicSample(sample));
                        if(sample.Known)known++;else unknown++;
                        foreach(var row in sample.Outputs)anyFullscreen|=row.Fullscreen;
                    }
                }
                var after=topology.Capture();bool stable=SameTopology(before,after);
                bool clean=!query.CleanupFailed&&query.OutstandingWindows==0&&query.WindowsCreated==query.WindowsReleased;
                bool valid=samples.Count==2;foreach(var sample in samples)valid&=ValidSample(sample,before.Outputs.Count);
                // Unknown is a legitimate conservative observation. Passing this
                // contract does not assert that the current environment is Known.
                bool passed=stable&&clean&&valid&&query.WindowsCreated==4*before.Outputs.Count;
                var report=new{schemaVersion=1,passed=passed,readOnlyObservationContract=passed,normalCompletion=true,declaredTopologyGeneration=DeclaredTopologyGeneration,
                    topologyKnownBefore=before.Known,topologyKnownAfter=after.Known,topologyReasonBefore=before.Reason.ToString(),topologyReasonAfter=after.Reason.ToString(),
                    topologyCaptureSerialBefore=before.CaptureSerial,topologyCaptureSerialAfter=after.CaptureSerial,topologyStableAcrossObservations=stable,currentOutputCount=before.Outputs.Count,
                    samples=publicSamples,environmentCaptureCompleted=samples.Count==2,knownEnvironmentSamples=known,unknownEnvironmentSamples=unknown,environmentAvailabilityConfirmed=known==2,
                    anyFullscreenObserved=anyFullscreen,nativeFullscreenAcceptance=false,physicalMultiMonitorAcceptance=false,mixedDpiAcceptance=false,actualSessionTransitionTested=false,
                    topologyTemporaryWindowsCreated=query.WindowsCreated,topologyTemporaryWindowsReleased=query.WindowsReleased,topologyOutstandingWindows=query.OutstandingWindows,topologyTemporaryWindowCleanupConfirmed=clean,
                    currentDeviceOnly=true,declaredTargetsValidatedByTopologyCapture=before.Known,foregroundMetadataReadOnly=true,externalWindowContentRead=false,externalWindowTitlesRead=false,
                    processCommandsRead=false,fixtureCreated=false,inputInjected=false,focusChanged=false,displaySettingsChanged=false,desktopAttached=false,browserStarted=false,hostIntegrated=false};
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native-report.json"),new JavaScriptSerializer().Serialize(report));return passed?0:3;
            }catch(Exception){return 4;}
        }
    }
}
