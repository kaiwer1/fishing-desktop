using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace FishingDesktop.Diagnostics.MultiOutput
{
    [Flags]
    public enum OutputGlobalBlock { None=0,Startup=1,Manual=2,SessionUnavailable=4,PowerSuspended=8,EnvironmentUnknown=16,TopologyChanging=32,SceneFault=64 }
    [Flags]
    public enum OutputLocalBlock { None=0,Unbound=1,NotReady=2,Portrait=4,Fullscreen=8,RendererUnavailable=16 }
    // An opaque host-side vote identity, valid only in the policy that registered
    // this exact object. It is neither a page identifier nor an input lease.
    public sealed class OutputGlobalSource
    {
        public readonly OutputGlobalBlock OwnedMask;
        internal OutputGlobalSource(OutputGlobalBlock ownedMask){OwnedMask=ownedMask;}
    }
    // A distinct reserved capability. Generic source replacement cannot address
    // this environment vote, and no page/document identity is accepted here.
    public sealed class OutputEnvironmentSource
    {
        internal OutputEnvironmentSource(){}
    }
    public sealed class OutputEnvironmentCoverage
    {
        public readonly string Key;
        public readonly bool Fullscreen;
        public OutputEnvironmentCoverage(string key,bool fullscreen){Key=key;Fullscreen=fullscreen;}
    }
    public enum EnvironmentBatchResult { WrongTopology,Stale,Applied,Unchanged,Unavailable,Rejected }
    static class OutputKeys
    {
        internal static string Canonical(string value)
        {
            if(String.IsNullOrEmpty(value)||value.Length>64)throw new ArgumentException("Invalid logical output key.","value");
            for(int i=0;i<value.Length;i++){
                char c=value[i];bool word=(c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9');
                if(!word&&(i==0||(c!='-'&&c!='_'&&c!='.')))throw new ArgumentException("Invalid logical output key.","value");
            }
            return value.ToLowerInvariant();
        }
    }
    // Declared host geometry only. This class never enumerates monitors or DPI.
    public sealed class OutputDefinition
    {
        public readonly string Key;
        public readonly int X,Y,Width,Height,DpiX,DpiY;
        public OutputDefinition(string key,int x,int y,int width,int height,int dpiX,int dpiY)
        {
            Key=OutputKeys.Canonical(key);
            if(width<=0||height<=0||(long)x+width>Int32.MaxValue||(long)y+height>Int32.MaxValue)throw new ArgumentOutOfRangeException("width");
            if(dpiX<=0||dpiY<=0)throw new ArgumentOutOfRangeException("dpiX");
            X=x;Y=y;Width=width;Height=height;DpiX=dpiX;DpiY=dpiY;
        }
    }
    public sealed class OutputState
    {
        public readonly OutputDefinition Definition;
        public readonly long ViewGeneration;
        public readonly bool Bound,Ready,Landscape,ViewFullscreen,EnvironmentFullscreen,Fullscreen,RendererAvailable,CanDraw;
        public readonly OutputLocalBlock LocalBlocks;
        public bool LocalCanDraw{get{return LocalBlocks==OutputLocalBlock.None;}}
        internal OutputState(OutputDefinition definition,long view,bool bound,bool ready,bool landscape,bool viewFullscreen,bool environmentFullscreen,bool renderer,OutputGlobalBlock global)
        {
            Definition=definition;ViewGeneration=view;Bound=bound;Ready=ready;Landscape=landscape;ViewFullscreen=viewFullscreen;EnvironmentFullscreen=environmentFullscreen;Fullscreen=viewFullscreen||environmentFullscreen;RendererAvailable=renderer;
            LocalBlocks=(!bound?OutputLocalBlock.Unbound:OutputLocalBlock.None)|(ready?OutputLocalBlock.None:OutputLocalBlock.NotReady)|
                (landscape?OutputLocalBlock.None:OutputLocalBlock.Portrait)|(Fullscreen?OutputLocalBlock.Fullscreen:OutputLocalBlock.None)|
                (renderer?OutputLocalBlock.None:OutputLocalBlock.RendererUnavailable);
            CanDraw=global==OutputGlobalBlock.None&&LocalCanDraw;
        }
    }
    public sealed class OutputPolicyState
    {
        public readonly long Revision,TopologyGeneration,InputEpoch,EnvironmentCaptureSerial;
        public readonly string ControlTarget;
        public readonly OutputGlobalBlock GlobalBlocks;
        public readonly bool CanRun,ControlEligible,EnvironmentRegistered,EnvironmentKnown;
        public readonly ReadOnlyCollection<OutputState> Outputs;
        internal OutputPolicyState(long revision,long topology,long epoch,string target,OutputGlobalBlock global,List<OutputState> outputs,bool eligible,bool environmentRegistered,bool environmentKnown,long environmentSerial)
        {
            Revision=revision;TopologyGeneration=topology;InputEpoch=epoch;ControlTarget=target;GlobalBlocks=global;Outputs=outputs.AsReadOnly();ControlEligible=eligible;
            EnvironmentRegistered=environmentRegistered;EnvironmentKnown=environmentKnown;EnvironmentCaptureSerial=environmentSerial;
            bool any=false;foreach(var output in outputs)any|=output.CanDraw;CanRun=any;
        }
    }
    // Pure owner-thread aggregation. It produces eligibility, never a lease or
    // simulation step; menu/game rules must remain separate global blockers.
    public sealed class OutputPolicy
    {
        sealed class Entry
        {
            internal readonly OutputDefinition Definition;
            internal readonly long View;
            internal readonly bool Bound,Ready,Landscape,ViewFullscreen,EnvironmentFullscreen,Renderer;
            internal Entry(OutputDefinition definition,long view=0,bool ready=false,bool landscape=false,bool fullscreen=false,bool renderer=false,bool bound=true,bool environmentFullscreen=false)
            {Definition=definition;View=view;Bound=view>0&&bound;Ready=ready;Landscape=landscape;ViewFullscreen=fullscreen;EnvironmentFullscreen=environmentFullscreen;Renderer=renderer;}
            internal OutputState State(OutputGlobalBlock global){return new OutputState(Definition,View,Bound,Ready,Landscape,ViewFullscreen,EnvironmentFullscreen,Renderer,global);}
        }
        const OutputGlobalBlock AllGlobals=(OutputGlobalBlock)127;
        const OutputGlobalBlock EnvironmentMask=OutputGlobalBlock.EnvironmentUnknown|OutputGlobalBlock.SessionUnavailable;
        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId,maximumOutputs,maximumGlobalSources;
        Dictionary<string,Entry> outputs=new Dictionary<string,Entry>(StringComparer.Ordinal);
        Dictionary<OutputGlobalSource,OutputGlobalBlock> globalSources=new Dictionary<OutputGlobalSource,OutputGlobalBlock>();
        OutputEnvironmentSource environmentSource;
        long revision,topology,inputEpoch,environmentCaptureSerial;
        string controlTarget;
        OutputGlobalBlock global=OutputGlobalBlock.Startup,legacyGlobal=OutputGlobalBlock.Startup;
        OutputGlobalBlock environmentVote=OutputGlobalBlock.None;
        public OutputPolicy(int maximumOutputs=16,int maximumGlobalSources=8)
        {
            if(maximumOutputs<=0)throw new ArgumentOutOfRangeException("maximumOutputs");
            if(maximumGlobalSources<=0||maximumGlobalSources>64)throw new ArgumentOutOfRangeException("maximumGlobalSources");
            this.maximumOutputs=maximumOutputs;this.maximumGlobalSources=maximumGlobalSources;
        }
        void Check(){if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("Output policy requires its owner thread.");}
        static bool Eligible(Dictionary<string,Entry> entries,string target,OutputGlobalBlock blocked)
        {Entry entry;return target!=null&&blocked==OutputGlobalBlock.None&&entries.TryGetValue(target,out entry)&&entry.State(blocked).CanDraw;}
        void Commit(Dictionary<string,Entry> next,long nextTopology,string nextTarget,OutputGlobalBlock nextGlobal,bool invalidate)
        {
            bool eligibilityChanged=Eligible(outputs,controlTarget,global)!=Eligible(next,nextTarget,nextGlobal);
            long nextRevision=checked(revision+1),nextEpoch=checked(inputEpoch+(invalidate||eligibilityChanged?1:0));
            outputs=next;topology=nextTopology;controlTarget=nextTarget;global=nextGlobal;revision=nextRevision;inputEpoch=nextEpoch;
        }
        public OutputPolicyState Snapshot()
        {
            Check();var keys=new List<string>(outputs.Keys);keys.Sort(StringComparer.Ordinal);var rows=new List<OutputState>();
            foreach(string key in keys)rows.Add(outputs[key].State(global));
            return new OutputPolicyState(revision,topology,inputEpoch,controlTarget,global,rows,Eligible(outputs,controlTarget,global),environmentSource!=null,environmentSource!=null&&environmentVote==OutputGlobalBlock.None,environmentCaptureSerial);
        }
        public bool ReplaceTopology(long generation,IEnumerable<OutputDefinition> definitions)
        {
            Check();if(generation<=0)throw new ArgumentOutOfRangeException("generation");if(generation<=topology)return false;
            if(definitions==null)throw new ArgumentNullException("definitions");
            var next=new Dictionary<string,Entry>(StringComparer.Ordinal);
            foreach(var definition in definitions){
                if(definition==null)throw new ArgumentException("Null output definition.","definitions");
                if(next.Count>=maximumOutputs)throw new ArgumentException("Logical output capacity exceeded.","definitions");
                if(next.ContainsKey(definition.Key))throw new ArgumentException("Duplicate logical output.","definitions");next.Add(definition.Key,new Entry(definition));
            }
            // MoveNext/Dispose may have reentered a newer (or this same)
            // generation. Do not roll topology or its environment scope back.
            if(generation<=topology)return false;
            string target=controlTarget!=null&&next.ContainsKey(controlTarget)?controlTarget:null;
            var nextEnvironment=environmentSource==null?OutputGlobalBlock.None:OutputGlobalBlock.EnvironmentUnknown;
            Commit(next,generation,target,AggregateGlobal(legacyGlobal,globalSources,nextEnvironment),true);
            environmentVote=nextEnvironment;environmentCaptureSerial=0;return true;
        }
        public bool BindView(string outputKey,long expectedTopology,long viewGeneration)
        {
            Check();string key=OutputKeys.Canonical(outputKey);if(viewGeneration<=0)throw new ArgumentOutOfRangeException("viewGeneration");Entry entry;
            if(expectedTopology!=topology||!outputs.TryGetValue(key,out entry)||viewGeneration<=entry.View)return false;
            var next=new Dictionary<string,Entry>(outputs,StringComparer.Ordinal);next[key]=new Entry(entry.Definition,viewGeneration,environmentFullscreen:entry.EnvironmentFullscreen);
            Commit(next,topology,controlTarget,global,key==controlTarget);return true;
        }
        public bool Observe(string outputKey,long expectedTopology,long expectedView,bool ready,bool landscape,bool fullscreen,bool rendererAvailable)
        {
            Check();string key=OutputKeys.Canonical(outputKey);Entry entry;
            if(expectedTopology!=topology||expectedView<=0||!outputs.TryGetValue(key,out entry)||!entry.Bound||entry.View!=expectedView)return false;
            if(entry.Ready==ready&&entry.Landscape==landscape&&entry.ViewFullscreen==fullscreen&&entry.Renderer==rendererAvailable)return true;
            var replacement=new Entry(entry.Definition,entry.View,ready,landscape,fullscreen,rendererAvailable,environmentFullscreen:entry.EnvironmentFullscreen);
            bool newBarrier=(replacement.State(global).LocalBlocks&~entry.State(global).LocalBlocks)!=0;
            var next=new Dictionary<string,Entry>(outputs,StringComparer.Ordinal);next[key]=replacement;
            Commit(next,topology,controlTarget,global,key==controlTarget&&newBarrier);return true;
        }
        public bool RetireView(string outputKey,long expectedTopology,long expectedView)
        {
            Check();string key=OutputKeys.Canonical(outputKey);Entry entry;
            if(expectedTopology!=topology||!outputs.TryGetValue(key,out entry)||!entry.Bound||entry.View!=expectedView)return false;
            var next=new Dictionary<string,Entry>(outputs,StringComparer.Ordinal);next[key]=new Entry(entry.Definition,entry.View,bound:false,environmentFullscreen:entry.EnvironmentFullscreen);
            Commit(next,topology,controlTarget,global,key==controlTarget);return true;
        }
        public bool SetGlobal(OutputGlobalBlock reason,bool blocked)
        {
            Check();int bits=(int)reason;if(bits<=0||(bits&(bits-1))!=0||(reason&~AllGlobals)!=0)throw new ArgumentOutOfRangeException("reason");
            return ReplaceGlobal(reason,blocked?reason:OutputGlobalBlock.None);
        }
        public bool ReplaceGlobal(OutputGlobalBlock mask,OutputGlobalBlock blocked)
        {
            Check();if(mask==OutputGlobalBlock.None||(mask&~AllGlobals)!=0||(blocked&~mask)!=0)throw new ArgumentOutOfRangeException("mask");
            var next=(legacyGlobal&~mask)|blocked;if(next==legacyGlobal)return false;
            return CommitGlobalVotes(next,globalSources);
        }
        static OutputGlobalBlock AggregateGlobal(OutputGlobalBlock nextLegacy,Dictionary<OutputGlobalSource,OutputGlobalBlock> nextSources,OutputGlobalBlock nextEnvironment)
        {
            OutputGlobalBlock next=nextLegacy|nextEnvironment;foreach(var vote in nextSources.Values)next|=vote;return next;
        }
        bool CommitGlobalVotes(OutputGlobalBlock nextLegacy,Dictionary<OutputGlobalSource,OutputGlobalBlock> nextSources)
        {
            OutputGlobalBlock next=AggregateGlobal(nextLegacy,nextSources,environmentVote);
            bool changed=next!=global;
            // Commit may throw on revision/epoch overflow; do it before storing
            // either ledger so a rejected operation cannot partially replace it.
            if(changed)Commit(outputs,topology,controlTarget,next,(next&~global)!=0);
            // A masked vote is still real state. Save it even when the aggregate
            // and its revision/epoch do not change.
            legacyGlobal=nextLegacy;globalSources=nextSources;return changed;
        }
        public OutputGlobalSource RegisterGlobalSource(OutputGlobalBlock mask,OutputGlobalBlock initialBlocked=OutputGlobalBlock.None)
        {
            Check();if(mask==OutputGlobalBlock.None||(mask&~AllGlobals)!=0||(initialBlocked&~mask)!=0)throw new ArgumentOutOfRangeException("mask");
            if(globalSources.Count>=maximumGlobalSources)throw new InvalidOperationException("Global source capacity exceeded.");
            var source=new OutputGlobalSource(mask);var next=new Dictionary<OutputGlobalSource,OutputGlobalBlock>(globalSources);next.Add(source,initialBlocked);
            CommitGlobalVotes(legacyGlobal,next);return source;
        }
        // Atomically replaces this source's WHOLE vote inside its fixed mask.
        // False means the aggregate did not change; a hidden vote is still saved.
        public bool ReplaceGlobalForSource(OutputGlobalSource source,OutputGlobalBlock blocked)
        {
            Check();if(source==null)throw new ArgumentNullException("source");OutputGlobalBlock previous;
            if(!globalSources.TryGetValue(source,out previous))throw new ArgumentException("Unknown or foreign global source.","source");
            if((blocked&~source.OwnedMask)!=0)throw new ArgumentOutOfRangeException("blocked");
            if(previous==blocked)return false;
            var next=new Dictionary<OutputGlobalSource,OutputGlobalBlock>(globalSources);next[source]=blocked;
            return CommitGlobalVotes(legacyGlobal,next);
        }
        public OutputEnvironmentSource RegisterEnvironmentSource()
        {
            Check();if(environmentSource!=null)throw new InvalidOperationException("An environment source is already registered.");
            var source=new OutputEnvironmentSource();var nextVote=OutputGlobalBlock.EnvironmentUnknown;
            var nextGlobal=AggregateGlobal(legacyGlobal,globalSources,nextVote);
            if(nextGlobal!=global)Commit(outputs,topology,controlTarget,nextGlobal,(nextGlobal&~global)!=0);
            environmentSource=source;environmentVote=nextVote;environmentCaptureSerial=0;return source;
        }
        bool TryCoverage(IEnumerable<OutputEnvironmentCoverage> coverage,out Dictionary<string,bool> rows)
        {
            rows=new Dictionary<string,bool>(StringComparer.Ordinal);if(coverage==null||outputs.Count==0)return false;
            try{
                foreach(var row in coverage){
                    // At most output-count + 1 MoveNext calls. An endless host
                    // iterator cannot grow memory or repeat until it succeeds.
                    if(rows.Count>=outputs.Count||rows.Count>=maximumOutputs||row==null)return false;
                    string key=OutputKeys.Canonical(row.Key);if(!outputs.ContainsKey(key)||rows.ContainsKey(key))return false;
                    rows.Add(key,row.Fullscreen);
                }
            }catch(Exception){return false;}
            return rows.Count==outputs.Count;
        }
        EnvironmentBatchResult CommitEnvironment(Dictionary<string,Entry> next,OutputGlobalBlock nextVote,long serial,bool coverageChanged,EnvironmentBatchResult result)
        {
            var nextGlobal=AggregateGlobal(legacyGlobal,globalSources,nextVote);bool changed=coverageChanged||nextGlobal!=global;
            if(changed){
                bool newTargetBarrier=false;Entry prior,replacement;
                if(controlTarget!=null&&outputs.TryGetValue(controlTarget,out prior)&&next.TryGetValue(controlTarget,out replacement))
                    newTargetBarrier=(replacement.State(nextGlobal).LocalBlocks&~prior.State(global).LocalBlocks)!=0;
                Commit(next,topology,controlTarget,nextGlobal,(nextGlobal&~global)!=0||newTargetBarrier);
            }
            // Store vote and high-water only after a possibly overflowing Commit.
            environmentVote=nextVote;environmentCaptureSerial=serial;
            return result==EnvironmentBatchResult.Applied&&!changed?EnvironmentBatchResult.Unchanged:result;
        }
        public EnvironmentBatchResult ApplyEnvironmentBatch(OutputEnvironmentSource source,long expectedTopology,long captureSerial,bool known,IEnumerable<OutputEnvironmentCoverage> completeCoverage,OutputGlobalBlock envBlocks=OutputGlobalBlock.None)
        {
            Check();if(source==null)throw new ArgumentNullException("source");
            if(!Object.ReferenceEquals(environmentSource,source))throw new ArgumentException("Unknown or foreign environment source.","source");
            if(expectedTopology<=0||expectedTopology!=topology)return EnvironmentBatchResult.WrongTopology;
            if(captureSerial<=environmentCaptureSerial)return EnvironmentBatchResult.Stale;
            bool validBlocks=(envBlocks&~EnvironmentMask)==0;
            if(!validBlocks||(known&&envBlocks!=OutputGlobalBlock.None))
                return CommitEnvironment(outputs,environmentVote|OutputGlobalBlock.EnvironmentUnknown,captureSerial,false,EnvironmentBatchResult.Rejected);
            if(!known)return CommitEnvironment(outputs,envBlocks|OutputGlobalBlock.EnvironmentUnknown,captureSerial,false,EnvironmentBatchResult.Unavailable);
            Dictionary<string,bool> coverage;
            bool validCoverage=TryCoverage(completeCoverage,out coverage);
            // Host iterators can execute code from MoveNext/Dispose. Never let
            // an outer batch overwrite a newer reentrant topology/observation.
            if(expectedTopology!=topology)return EnvironmentBatchResult.WrongTopology;
            if(captureSerial<=environmentCaptureSerial)return EnvironmentBatchResult.Stale;
            if(!validCoverage)
                return CommitEnvironment(outputs,environmentVote|OutputGlobalBlock.EnvironmentUnknown,captureSerial,false,EnvironmentBatchResult.Rejected);
            var next=new Dictionary<string,Entry>(StringComparer.Ordinal);bool changed=false;
            foreach(var pair in outputs){
                var entry=pair.Value;bool covered=coverage[pair.Key];changed|=entry.EnvironmentFullscreen!=covered;
                next.Add(pair.Key,new Entry(entry.Definition,entry.View,entry.Ready,entry.Landscape,entry.ViewFullscreen,entry.Renderer,entry.Bound,covered));
            }
            return CommitEnvironment(next,OutputGlobalBlock.None,captureSerial,changed,EnvironmentBatchResult.Applied);
        }
        public bool SelectControlTarget(string outputKey)
        {
            Check();string key=outputKey==null?null:OutputKeys.Canonical(outputKey);
            if(key!=null&&!outputs.ContainsKey(key))throw new ArgumentException("Unknown logical output.","outputKey");
            if(key==controlTarget)return false;Commit(outputs,topology,key,global,true);return true;
        }
        public bool CanActivate(string outputKey,long expectedTopology,long expectedView,long expectedInputEpoch)
        {
            Check();string key=OutputKeys.Canonical(outputKey);Entry entry;
            return key==controlTarget&&expectedTopology==topology&&expectedInputEpoch==inputEpoch&&outputs.TryGetValue(key,out entry)&&entry.View==expectedView&&
                Eligible(outputs,controlTarget,global);
        }
    }
}
