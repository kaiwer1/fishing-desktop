using System;
using System.Collections.Generic;
using System.Threading;
using FishingDesktop.Diagnostics.OutputTopology;

namespace FishingDesktop.Diagnostics.MultiOutput
{
    public enum TopologyApplyResult { Stale, Unavailable, Unchanged, Replaced, Rejected, Diverged }

    // Host-only bridge. Capture serial orders observations; it is never a topology
    // generation. This is the sole writer of topology and TopologyChanging.
    public sealed class TopologyPolicyBridge
    {
        readonly OutputPolicy policy;
        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
        long lastCaptureSerial,generation;
        bool verified,diverged;
        List<TopologyOutput> previous;
        public long LastCaptureSerial { get { Check();return lastCaptureSerial; } }
        public long TopologyGeneration { get { Check();return generation; } }
        public bool HasVerifiedTopology { get { Check();return verified&&!diverged&&policy.Snapshot().TopologyGeneration==generation; } }

        public TopologyPolicyBridge(OutputPolicy policy)
        {
            if(policy==null)throw new ArgumentNullException("policy");
            if(policy.Snapshot().TopologyGeneration!=0)throw new ArgumentException("Bridge requires an unconfigured policy.","policy");
            this.policy=policy;
            policy.SetGlobal(OutputGlobalBlock.TopologyChanging,true);
        }
        void Check()
        {if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("Topology bridge requires its owner thread.");}
        void Block()
        {verified=false;policy.SetGlobal(OutputGlobalBlock.TopologyChanging,true);}
        static List<TopologyOutput> Ordered(TopologySample sample)
        {
            var rows=new List<TopologyOutput>(sample.Outputs);
            rows.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Key,b.Key));return rows;
        }
        static bool Same(List<TopologyOutput> left,List<TopologyOutput> right)
        {
            if(left==null||left.Count!=right.Count)return false;
            for(int i=0;i<left.Count;i++){
                var a=left[i];var b=right[i];
                if(a.Key!=b.Key||a.X!=b.X||a.Y!=b.Y||a.Width!=b.Width||a.Height!=b.Height||a.DpiX!=b.DpiX||a.DpiY!=b.DpiY||a.Primary!=b.Primary)return false;
            }
            return true;
        }
        public TopologyApplyResult Apply(TopologySample sample)
        {
            Check();if(sample==null)throw new ArgumentNullException("sample");
            if(sample.CaptureSerial<=0)throw new ArgumentOutOfRangeException("sample");
            if(diverged||policy.Snapshot().TopologyGeneration!=generation){
                diverged=true;Block();return TopologyApplyResult.Diverged;
            }
            if(sample.CaptureSerial<=lastCaptureSerial)return TopologyApplyResult.Stale;
            lastCaptureSerial=sample.CaptureSerial;
            if(!sample.Known){Block();return TopologyApplyResult.Unavailable;}
            var next=Ordered(sample);
            if(verified&&Same(previous,next))return TopologyApplyResult.Unchanged;
            var definitions=new List<OutputDefinition>();
            foreach(var row in next)definitions.Add(new OutputDefinition(row.Key,row.X,row.Y,row.Width,row.Height,row.DpiX,row.DpiY));
            long nextGeneration=checked(generation+1);
            try{
                // ReplaceTopology validates the whole candidate before mutating;
                // replacement unbinds every view before the barrier is cleared.
                if(!policy.ReplaceTopology(nextGeneration,definitions)){diverged=true;Block();return TopologyApplyResult.Diverged;}
            }catch(ArgumentException){Block();return TopologyApplyResult.Rejected;}
            generation=nextGeneration;previous=next;verified=true;
            policy.SetGlobal(OutputGlobalBlock.TopologyChanging,false);
            return TopologyApplyResult.Replaced;
        }
    }
}
