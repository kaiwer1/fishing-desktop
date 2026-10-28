using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Threading;

namespace FishingDesktop.GameCore
{
    // Independent, original encounter model. These values are not RF4 data,
    // inventory items or proof of a confirmed cast/landed fish.
    internal sealed class FloatBiteCandidate
    {
        internal readonly string SpeciesId;
        internal readonly int Weight;
        internal readonly long MinimumMassMg,MaximumMassMg;
        internal FloatBiteCandidate(string speciesId,int weight,long minimumMassMg,long maximumMassMg)
        {
            if(String.IsNullOrEmpty(speciesId)||speciesId.Length>64)throw new ArgumentException("Invalid species key.","speciesId");
            foreach(char c in speciesId)if(!((c>='a'&&c<='z')||(c>='0'&&c<='9')||c=='-'||c=='_'))throw new ArgumentException("Invalid species key.","speciesId");
            if(weight<0||weight>1000000)throw new ArgumentOutOfRangeException("weight");
            if(minimumMassMg<1||maximumMassMg<minimumMassMg||maximumMassMg>1000000000000L)throw new ArgumentOutOfRangeException("minimumMassMg");
            SpeciesId=speciesId;Weight=weight;MinimumMassMg=minimumMassMg;MaximumMassMg=maximumMassMg;
        }
    }
    internal sealed class FloatBiteEnvironment
    {
        internal readonly double HazardPerSecond;
        internal readonly bool Available;
        internal readonly ReadOnlyCollection<FloatBiteCandidate> Candidates;
        internal readonly int TotalWeight;
        internal FloatBiteEnvironment(double hazardPerSecond,FloatBiteCandidate[] candidates,bool available=true)
        {
            if(Double.IsNaN(hazardPerSecond)||Double.IsInfinity(hazardPerSecond)||hazardPerSecond<0||hazardPerSecond>1000)throw new ArgumentOutOfRangeException("hazardPerSecond");
            if(candidates==null)throw new ArgumentNullException("candidates");
            if(candidates.Length>64)throw new ArgumentOutOfRangeException("candidates");
            var copy=new List<FloatBiteCandidate>();var unique=new HashSet<string>(StringComparer.Ordinal);int total=0;
            foreach(var candidate in candidates){
                if(candidate==null||!unique.Add(candidate.SpeciesId))throw new ArgumentException("Null or duplicate candidate.","candidates");
                total=checked(total+candidate.Weight);copy.Add(candidate);
            }
            // A catalog's incidental row order must not alter weighted draws.
            copy.Sort((a,b)=>StringComparer.Ordinal.Compare(a.SpeciesId,b.SpeciesId));
            HazardPerSecond=hazardPerSecond;Available=available;Candidates=copy.AsReadOnly();TotalWeight=total;
        }
        internal bool ContainsTarget(string speciesId)
        {foreach(var candidate in Candidates)if(candidate.SpeciesId==speciesId&&candidate.Weight>0)return true;return false;}
    }
    internal sealed class FloatBiteRules
    {
        internal readonly int SettlingSteps,ApproachingSteps,NibblingSteps,StrikeableSteps;
        internal FloatBiteRules(int settlingSteps,int approachingSteps,int nibblingSteps,int strikeableSteps)
        {
            foreach(int value in new[]{settlingSteps,approachingSteps,nibblingSteps,strikeableSteps})if(value<1||value>12000)throw new ArgumentOutOfRangeException("duration steps");
            SettlingSteps=settlingSteps;ApproachingSteps=approachingSteps;NibblingSteps=nibblingSteps;StrikeableSteps=strikeableSteps;
        }
    }
    internal sealed class FloatBiteTarget
    {
        internal readonly string SpeciesId;
        internal readonly long MassMg;
        internal FloatBiteTarget(string speciesId,long massMg){SpeciesId=speciesId;MassMg=massMg;}
    }
    internal enum FloatBitePhase { Settling,Searching,Approaching,Nibbling,Strikeable,Hooked,RetrieveRequired,Ended }
    internal sealed class FloatBiteSnapshot
    {
        internal readonly Guid SessionId,EncounterId;
        internal readonly FloatBitePhase Phase;
        internal readonly string Reason;
        internal readonly long StepIndex,SearchEpoch,OpenStep,CloseStep;
        internal readonly int RemainderMilliseconds,BobberOffsetMm,BobberDriftMm;
        internal readonly bool BaitAttached;
        internal readonly double Exposure,Threshold;
        // Host-only simulation state: never project target or random/threshold
        // details to the player's bite view, even after a successful strike.
        internal readonly FloatBiteTarget Target;
        internal FloatBiteSnapshot(Guid session,Guid encounter,FloatBitePhase phase,string reason,long step,long epoch,long open,long close,
            int remainder,int offset,int drift,bool attached,double exposure,double threshold,FloatBiteTarget target)
        {SessionId=session;EncounterId=encounter;Phase=phase;Reason=reason;StepIndex=step;SearchEpoch=epoch;OpenStep=open;CloseStep=close;
            RemainderMilliseconds=remainder;BobberOffsetMm=offset;BobberDriftMm=drift;BaitAttached=attached;Exposure=exposure;Threshold=threshold;Target=target;}
    }
    internal sealed class FloatBiteResult
    {
        internal readonly bool Accepted,ShouldStartFight,TerminalEvent;
        internal readonly string Code;
        internal readonly FloatBiteSnapshot Snapshot;
        internal FloatBiteResult(bool accepted,bool startFight,bool terminal,string code,FloatBiteSnapshot snapshot)
        {Accepted=accepted;ShouldStartFight=startFight;TerminalEvent=terminal;Code=code;Snapshot=snapshot;}
    }
    internal sealed class FloatBiteRandom
    {
        ulong state;
        internal FloatBiteRandom(uint seed){state=seed;}
        // Explicit SplitMix64 arithmetic, independent of framework Random.
        internal ulong Next(){unchecked{state+=0x9e3779b97f4a7c15UL;ulong z=state;z=(z^(z>>30))*0xbf58476d1ce4e5b9UL;z=(z^(z>>27))*0x94d049bb133111ebUL;return z^(z>>31);}}
        internal double OpenUnit(){return ((Next()>>32)+.5)/4294967296.0;}
        internal ulong Bounded(ulong bound)
        {
            // Rejection gives an unbiased inclusive integer-mass draw without
            // double rounding or modulo bias. Config limits keep bound small.
            ulong threshold=unchecked(0UL-bound)%bound;
            while(true){ulong value=Next();if(value>=threshold)return value%bound;}
        }
    }
    internal sealed class FloatBiteSimulation
    {
        internal const int ModelVersion=1,StepMilliseconds=50;
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        readonly Guid session;
        readonly FloatBiteRules rules;
        readonly FloatBiteRandom searchRandom,speciesRandom,massRandom;
        FloatBiteEnvironment environment;
        FloatBitePhase phase=FloatBitePhase.Settling;
        string reason="settling";
        long step,phaseStarted,epoch,open,close;
        int remainder;
        double exposure,threshold;
        bool attached=true,terminalEmitted;
        Guid encounter=Guid.Empty;
        FloatBiteTarget target;

        // Caller establishes valid confirmed water contact and keeps this same
        // instance across page reload/leases. GUID identity is not authority.
        internal FloatBiteSimulation(Guid confirmedSessionId,uint seed,FloatBiteRules rules,FloatBiteEnvironment environment)
        {
            if(confirmedSessionId==Guid.Empty)throw new ArgumentException("A confirmed session identity is required.","confirmedSessionId");
            if(rules==null||environment==null)throw new ArgumentNullException("bite dependency");
            session=confirmedSessionId;this.rules=rules;this.environment=environment;
            searchRandom=new FloatBiteRandom(seed^0xa341316cu);speciesRandom=new FloatBiteRandom(seed^0xc8013ea4u);massRandom=new FloatBiteRandom(seed^0xad90777du);
        }
        void Check(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Use the bite owner's host thread.");}
        bool Active{get{return phase!=FloatBitePhase.Hooked&&phase!=FloatBitePhase.RetrieveRequired&&phase!=FloatBitePhase.Ended;}}
        void Enter(FloatBitePhase next,string why){phase=next;reason=why;phaseStarted=step;}
        bool RequireRetrieve(string why)
        {attached=false;remainder=0;Enter(FloatBitePhase.RetrieveRequired,why);bool emit=!terminalEmitted;terminalEmitted=true;return emit;}
        void BeginSearch()
        {
            epoch=checked(epoch+1);exposure=0;threshold=-Math.Log(1-searchRandom.OpenUnit());
            Enter(FloatBitePhase.Searching,"searching");
        }
        Guid EncounterId()
        {
            // Stable domain separated identity; target randomness uses other
            // streams and never participates in session/encounter ownership.
            var bytes=new byte[32];var id=session.ToByteArray();Array.Copy(id,bytes,16);
            for(int i=0;i<8;i++)bytes[16+i]=(byte)((ulong)epoch>>(8*i));
            bytes[24]=70;bytes[25]=68;bytes[26]=66;bytes[27]=66;bytes[28]=66;bytes[29]=73;bytes[30]=84;bytes[31]=1;
            byte[] hash;using(var sha=SHA256.Create())hash=sha.ComputeHash(bytes);
            var value=new byte[16];Array.Copy(hash,value,16);value[7]=(byte)((value[7]&15)|128);value[8]=(byte)((value[8]&63)|128);return new Guid(value);
        }
        void SelectTarget()
        {
            ulong pick=speciesRandom.Bounded((ulong)environment.TotalWeight);
            foreach(var candidate in environment.Candidates){
                if(pick>=(ulong)candidate.Weight){pick-=(ulong)candidate.Weight;continue;}
                long mass=candidate.MinimumMassMg+(long)massRandom.Bounded((ulong)(candidate.MaximumMassMg-candidate.MinimumMassMg+1));
                target=new FloatBiteTarget(candidate.SpeciesId,mass);encounter=EncounterId();
                Enter(FloatBitePhase.Approaching,"approaching");return;
            }
            throw new InvalidOperationException("A validated weighted pool did not produce a target.");
        }
        bool Tick()
        {
            step=checked(step+1);
            if(!environment.Available)return RequireRetrieve("environment-unavailable");
            switch(phase){
                case FloatBitePhase.Settling:
                    if(step-phaseStarted>=rules.SettlingSteps)BeginSearch();break;
                case FloatBitePhase.Searching:
                    if(environment.TotalWeight==0){reason="no-target";break;}
                    if(environment.HazardPerSecond==0){reason="zero-rate";break;}
                    reason="searching";exposure+=environment.HazardPerSecond*StepMilliseconds/1000.0;
                    if(exposure>=threshold)SelectTarget();break;
                case FloatBitePhase.Approaching:
                    if(step-phaseStarted>=rules.ApproachingSteps)Enter(FloatBitePhase.Nibbling,"nibbling");break;
                case FloatBitePhase.Nibbling:
                    if(step-phaseStarted>=rules.NibblingSteps){Enter(FloatBitePhase.Strikeable,"strikeable");open=step;close=checked(step+rules.StrikeableSteps);}break;
                case FloatBitePhase.Strikeable:
                    // Expiry is processed before any command at this new step.
                    if(step>=close)return RequireRetrieve("bite-missed");break;
            }
            return false;
        }
        static int Triangle(long current,int period,int amplitude)
        {int part=(int)(current%period);return part<=period/2?-amplitude+4*amplitude*part/period:3*amplitude-4*amplitude*part/period;}
        FloatBiteSnapshot Describe()
        {
            long age=step-phaseStarted;int offset=0,drift=0;
            switch(phase){
                case FloatBitePhase.Settling:offset=40-(int)(40*age/rules.SettlingSteps);break;
                case FloatBitePhase.Searching:case FloatBitePhase.Approaching:offset=Triangle(step,24,3);drift=Triangle(step,32,2);break;
                case FloatBitePhase.Nibbling:offset=Triangle(age,8,18);drift=Triangle(age,12,12);break;
                case FloatBitePhase.Strikeable:offset=-45-(int)(95*Math.Min(age,5)/5);drift=(int)(30*Math.Min(age,6)/6);break;
                case FloatBitePhase.Hooked:offset=-160;break;
            }
            return new FloatBiteSnapshot(session,encounter,phase,reason,step,epoch,open,close,remainder,offset,drift,attached,exposure,threshold,target);
        }
        internal FloatBiteSnapshot Snapshot{get{Check();return Describe();}}
        FloatBiteResult Result(bool accepted,bool fight,bool terminal,string code){return new FloatBiteResult(accepted,fight,terminal,code,Describe());}
        internal FloatBiteResult Advance(Guid sessionId,int activeMilliseconds)
        {
            Check();if(activeMilliseconds<0||activeMilliseconds>250)throw new ArgumentOutOfRangeException("activeMilliseconds");
            if(sessionId!=session)return Result(false,false,false,"session-not-current");
            if(!Active)return Result(false,false,false,"bite-not-active");
            remainder+=activeMilliseconds;bool terminal=false;
            while(remainder>=StepMilliseconds&&Active){remainder-=StepMilliseconds;terminal=Tick()||terminal;}
            return Result(true,false,terminal,reason);
        }
        internal FloatBiteResult SetEnvironment(Guid sessionId,FloatBiteEnvironment next)
        {
            Check();if(next==null)throw new ArgumentNullException("next");
            if(sessionId!=session)return Result(false,false,false,"session-not-current");
            if(!Active)return Result(false,false,false,"bite-not-active");
            environment=next;
            if(!next.Available || (target!=null&&!next.ContainsTarget(target.SpeciesId)))
                return Result(true,false,RequireRetrieve("environment-interrupted"),reason);
            // No threshold, exposure, target, body size or clock reset.
            return Result(true,false,false,"environment-updated");
        }
        internal FloatBiteResult Strike(Guid sessionId,Guid encounterId)
        {
            Check();if(sessionId!=session)return Result(false,false,false,"session-not-current");
            if(encounterId!=encounter)return Result(false,false,false,"encounter-not-current");
            if(!Active)return Result(false,false,false,"bite-not-active");
            if(phase==FloatBitePhase.Strikeable&&step>=open&&step<close&&attached){
                remainder=0;Enter(FloatBitePhase.Hooked,"hooked");return Result(true,true,false,"hooked");
            }
            string why=target==null?"empty-strike":"strike-too-early";
            return Result(true,false,RequireRetrieve(why),why);
        }
        internal FloatBiteResult End(Guid sessionId)
        {
            Check();if(sessionId!=session)return Result(false,false,false,"session-not-current");
            if(phase==FloatBitePhase.Ended)return Result(false,false,false,"bite-not-active");
            remainder=0;Enter(FloatBitePhase.Ended,"ended");bool emit=!terminalEmitted;terminalEmitted=true;
            return Result(true,false,emit,"ended");
        }
    }
}
