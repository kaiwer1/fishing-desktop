using System;
using System.Collections.Generic;
using System.Threading;
using FishingDesktop.Diagnostics.OutputEnvironment;

namespace FishingDesktop.Diagnostics.MultiOutput
{
    public enum EnvironmentCaptureBridgeResult
    { NoTopology,TopologyBlocked,ScopeChanged,Applied,Unchanged,Unavailable,Rejected }

    // Host-only synchronous entry point. Callers cannot supply observations,
    // target geometry, an environment capability, or a request to clear its vote.
    public sealed class EnvironmentCaptureBridge
    {
        readonly OutputPolicy policy;
        readonly OutputEnvironmentCapture capture;
        readonly OutputEnvironmentSource source;
        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
        bool polling;
        long lastPollSerial,lastCaptureTopology;
        OutputEnvironmentReason lastReason=OutputEnvironmentReason.InvalidTopology;
        EnvironmentCaptureBridgeResult lastResult=EnvironmentCaptureBridgeResult.NoTopology;

        public EnvironmentCaptureBridge(OutputPolicy policy,OutputEnvironmentCapture capture)
        {
            if(policy==null)throw new ArgumentNullException("policy");
            if(capture==null)throw new ArgumentNullException("capture");
            // Snapshot enforces the policy owner thread before registration.
            policy.Snapshot();
            this.policy=policy;this.capture=capture;source=policy.RegisterEnvironmentSource();
        }
        void Check()
        {if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("Environment bridge requires its owner thread.");}
        public long LastPollSerial {get{Check();return lastPollSerial;}}
        public long LastCaptureTopology {get{Check();return lastCaptureTopology;}}
        public OutputEnvironmentReason LastReason {get{Check();return lastReason;}}
        public EnvironmentCaptureBridgeResult LastResult {get{Check();return lastResult;}}
        static bool TopologyBlocked(OutputPolicyState state)
        {return (state.GlobalBlocks&OutputGlobalBlock.TopologyChanging)!=0;}
        EnvironmentCaptureBridgeResult Finish(EnvironmentCaptureBridgeResult result,OutputEnvironmentReason reason)
        {lastResult=result;lastReason=reason;return result;}
        void UnknownAt(OutputPolicyState current,long serial)
        {
            // Generation zero cannot accept a batch; registration already owns
            // Unknown. A positive empty/replaced topology can accept only this
            // negative result, never coverage captured against old rectangles.
            if(current.TopologyGeneration>0)
                policy.ApplyEnvironmentBatch(source,current.TopologyGeneration,serial,false,null,OutputGlobalBlock.EnvironmentUnknown);
        }
        static OutputGlobalBlock MapBlocks(OutputEnvironmentGlobalBlock blocks)
        {
            var result=OutputGlobalBlock.None;
            if((blocks&OutputEnvironmentGlobalBlock.EnvironmentUnknown)!=0)result|=OutputGlobalBlock.EnvironmentUnknown;
            if((blocks&OutputEnvironmentGlobalBlock.SessionUnavailable)!=0)result|=OutputGlobalBlock.SessionUnavailable;
            return result;
        }
        public EnvironmentCaptureBridgeResult Poll(){return CaptureAndApply();}
        public EnvironmentCaptureBridgeResult CaptureAndApply()
        {
            Check();if(polling)throw new InvalidOperationException("Environment bridge cannot be reentered.");
            polling=true;
            try{
                // Allocate once BEFORE capture, including blocked attempts.
                // Topology replacement resets policy scope, never this serial.
                long serial=checked(lastPollSerial+1);lastPollSerial=serial;
                var before=policy.Snapshot();lastCaptureTopology=before.TopologyGeneration;
                if(before.TopologyGeneration<=0||before.Outputs.Count==0){
                    UnknownAt(before,serial);return Finish(EnvironmentCaptureBridgeResult.NoTopology,OutputEnvironmentReason.InvalidTopology);
                }
                if(TopologyBlocked(before)){
                    UnknownAt(before,serial);return Finish(EnvironmentCaptureBridgeResult.TopologyBlocked,OutputEnvironmentReason.InvalidTopology);
                }
                var targets=new List<OutputEnvironmentTarget>();
                foreach(var row in before.Outputs){
                    var definition=row.Definition;
                    targets.Add(new OutputEnvironmentTarget(definition.Key,definition.X,definition.Y,definition.Width,definition.Height));
                }
                OutputEnvironmentSample sample=null;
                try{sample=capture.Capture(before.TopologyGeneration,targets);}
                catch(Exception){/* No exception detail is published; fail closed below. */}
                var after=policy.Snapshot();
                // There is not yet a separate topology-barrier epoch. Reject
                // ANY policy revision change, including a raise/clear pulse,
                // even if the final generation and geometry appear unchanged.
                if(after.TopologyGeneration!=before.TopologyGeneration||after.Revision!=before.Revision||TopologyBlocked(after)){
                    UnknownAt(after,serial);return Finish(EnvironmentCaptureBridgeResult.ScopeChanged,OutputEnvironmentReason.InvalidTopology);
                }
                if(sample==null){
                    UnknownAt(after,serial);return Finish(EnvironmentCaptureBridgeResult.Unavailable,OutputEnvironmentReason.QueryFailed);
                }
                if(sample.TopologyGeneration!=before.TopologyGeneration){
                    UnknownAt(after,serial);return Finish(EnvironmentCaptureBridgeResult.ScopeChanged,OutputEnvironmentReason.InvalidTopology);
                }
                var rows=new List<OutputEnvironmentCoverage>();
                if(sample.Known)foreach(var row in sample.Outputs)rows.Add(new OutputEnvironmentCoverage(row.Key,row.Fullscreen));
                var applied=policy.ApplyEnvironmentBatch(source,before.TopologyGeneration,serial,sample.Known,rows,MapBlocks(sample.GlobalBlocks));
                switch(applied){
                    case EnvironmentBatchResult.Applied:return Finish(EnvironmentCaptureBridgeResult.Applied,sample.Reason);
                    case EnvironmentBatchResult.Unchanged:return Finish(EnvironmentCaptureBridgeResult.Unchanged,sample.Reason);
                    case EnvironmentBatchResult.Unavailable:return Finish(EnvironmentCaptureBridgeResult.Unavailable,sample.Reason);
                    case EnvironmentBatchResult.Rejected:return Finish(EnvironmentCaptureBridgeResult.Rejected,OutputEnvironmentReason.InvalidTopology);
                    default:
                        UnknownAt(policy.Snapshot(),serial);
                        return Finish(EnvironmentCaptureBridgeResult.ScopeChanged,OutputEnvironmentReason.InvalidTopology);
                }
            }finally{polling=false;}
        }
    }
}
