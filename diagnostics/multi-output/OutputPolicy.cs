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
        public readonly bool Bound,Ready,Landscape,Fullscreen,RendererAvailable,CanDraw;
        public readonly OutputLocalBlock LocalBlocks;
        public bool LocalCanDraw{get{return LocalBlocks==OutputLocalBlock.None;}}
        internal OutputState(OutputDefinition definition,long view,bool bound,bool ready,bool landscape,bool fullscreen,bool renderer,OutputGlobalBlock global)
        {
            Definition=definition;ViewGeneration=view;Bound=bound;Ready=ready;Landscape=landscape;Fullscreen=fullscreen;RendererAvailable=renderer;
            LocalBlocks=(!bound?OutputLocalBlock.Unbound:OutputLocalBlock.None)|(ready?OutputLocalBlock.None:OutputLocalBlock.NotReady)|
                (landscape?OutputLocalBlock.None:OutputLocalBlock.Portrait)|(fullscreen?OutputLocalBlock.Fullscreen:OutputLocalBlock.None)|
                (renderer?OutputLocalBlock.None:OutputLocalBlock.RendererUnavailable);
            CanDraw=global==OutputGlobalBlock.None&&LocalCanDraw;
        }
    }
    public sealed class OutputPolicyState
    {
        public readonly long Revision,TopologyGeneration,InputEpoch;
        public readonly string ControlTarget;
        public readonly OutputGlobalBlock GlobalBlocks;
        public readonly bool CanRun,ControlEligible;
        public readonly ReadOnlyCollection<OutputState> Outputs;
        internal OutputPolicyState(long revision,long topology,long epoch,string target,OutputGlobalBlock global,List<OutputState> outputs,bool eligible)
        {
            Revision=revision;TopologyGeneration=topology;InputEpoch=epoch;ControlTarget=target;GlobalBlocks=global;Outputs=outputs.AsReadOnly();ControlEligible=eligible;
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
            internal readonly bool Bound,Ready,Landscape,Fullscreen,Renderer;
            internal Entry(OutputDefinition definition,long view=0,bool ready=false,bool landscape=false,bool fullscreen=false,bool renderer=false,bool bound=true)
            {Definition=definition;View=view;Bound=view>0&&bound;Ready=ready;Landscape=landscape;Fullscreen=fullscreen;Renderer=renderer;}
            internal OutputState State(OutputGlobalBlock global){return new OutputState(Definition,View,Bound,Ready,Landscape,Fullscreen,Renderer,global);}
        }
        const OutputGlobalBlock AllGlobals=(OutputGlobalBlock)127;
        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId,maximumOutputs,maximumGlobalSources;
        Dictionary<string,Entry> outputs=new Dictionary<string,Entry>(StringComparer.Ordinal);
        Dictionary<OutputGlobalSource,OutputGlobalBlock> globalSources=new Dictionary<OutputGlobalSource,OutputGlobalBlock>();
        long revision,topology,inputEpoch;
        string controlTarget;
        OutputGlobalBlock global=OutputGlobalBlock.Startup,legacyGlobal=OutputGlobalBlock.Startup;
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
            return new OutputPolicyState(revision,topology,inputEpoch,controlTarget,global,rows,Eligible(outputs,controlTarget,global));
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
            string target=controlTarget!=null&&next.ContainsKey(controlTarget)?controlTarget:null;
            Commit(next,generation,target,global,true);return true;
        }
        public bool BindView(string outputKey,long expectedTopology,long viewGeneration)
        {
            Check();string key=OutputKeys.Canonical(outputKey);if(viewGeneration<=0)throw new ArgumentOutOfRangeException("viewGeneration");Entry entry;
            if(expectedTopology!=topology||!outputs.TryGetValue(key,out entry)||viewGeneration<=entry.View)return false;
            var next=new Dictionary<string,Entry>(outputs,StringComparer.Ordinal);next[key]=new Entry(entry.Definition,viewGeneration);
            Commit(next,topology,controlTarget,global,key==controlTarget);return true;
        }
        public bool Observe(string outputKey,long expectedTopology,long expectedView,bool ready,bool landscape,bool fullscreen,bool rendererAvailable)
        {
            Check();string key=OutputKeys.Canonical(outputKey);Entry entry;
            if(expectedTopology!=topology||expectedView<=0||!outputs.TryGetValue(key,out entry)||!entry.Bound||entry.View!=expectedView)return false;
            if(entry.Ready==ready&&entry.Landscape==landscape&&entry.Fullscreen==fullscreen&&entry.Renderer==rendererAvailable)return true;
            var replacement=new Entry(entry.Definition,entry.View,ready,landscape,fullscreen,rendererAvailable);
            bool newBarrier=(replacement.State(global).LocalBlocks&~entry.State(global).LocalBlocks)!=0;
            var next=new Dictionary<string,Entry>(outputs,StringComparer.Ordinal);next[key]=replacement;
            Commit(next,topology,controlTarget,global,key==controlTarget&&newBarrier);return true;
        }
        public bool RetireView(string outputKey,long expectedTopology,long expectedView)
        {
            Check();string key=OutputKeys.Canonical(outputKey);Entry entry;
            if(expectedTopology!=topology||!outputs.TryGetValue(key,out entry)||!entry.Bound||entry.View!=expectedView)return false;
            var next=new Dictionary<string,Entry>(outputs,StringComparer.Ordinal);next[key]=new Entry(entry.Definition,entry.View,bound:false);
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
        bool CommitGlobalVotes(OutputGlobalBlock nextLegacy,Dictionary<OutputGlobalSource,OutputGlobalBlock> nextSources)
        {
            OutputGlobalBlock next=nextLegacy;foreach(var vote in nextSources.Values)next|=vote;
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
