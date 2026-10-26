using FishingDesktop.Diagnostics.DesktopLayer;
using FishingDesktop.Diagnostics.DesktopStartup;

sealed partial class DualWindowContext
{
    readonly bool startupPreparation;
    DesktopStartupCoordinator startupCoordinator;
    Win32ShellLayerRequest shellLayerRequest;
    int startupCalls,startupReuses,startupRequestCalls;
    DiscoveryResult DiscoverForHost(DesktopRect target)
    {
        if(!startupPreparation)return desktopController.Discover(target);
        if(startupCoordinator==null){shellLayerRequest=new Win32ShellLayerRequest(desktopApi);startupCoordinator=new DesktopStartupCoordinator(desktopApi,shellLayerRequest);}
        var result=startupCoordinator.Prepare(target,true);startupCalls++;
        if(result.ReusedExisting)startupReuses++;if(result.RequestAttempted)startupRequestCalls++;
        return new DiscoveryResult{Candidate=result.Candidate,Reason=result.Reason,NativeError=result.NativeError};
    }
    object StartupSummary()
    {
        if(!startupPreparation)return null;
        return new{enabled=true,prepareCalls=startupCalls,reusedExistingCalls=startupReuses,requestCalls=startupRequestCalls,
            nativeMessageAttempts=shellLayerRequest==null?0:shellLayerRequest.MessageAttempts,freshLoginTested=false};
    }
}
