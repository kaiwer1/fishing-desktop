using System.Diagnostics;
using FishingDesktop.Diagnostics.HostPolicy;
using FishingDesktop.Diagnostics.OutputTopology;

namespace FishingDesktop.Diagnostics.OutputEnvironment
{
    public sealed partial class OutputEnvironmentCapture
    {
        // Compile together with the existing NativeEnvironment.cs so its
        // internal raw adapter is reused without copying or changing its API.
        public static OutputEnvironmentCapture CreateForCurrentProcess(int maximumOutputs=16)
        {
            var coordinates=new Win32TopologyQuery();
            using(var process=Process.GetCurrentProcess()){
                return new OutputEnvironmentCapture(new Win32EnvironmentQueries(),process.Id,process.SessionId,()=>coordinates.IsPerMonitorV2,maximumOutputs);
            }
        }
    }
}
