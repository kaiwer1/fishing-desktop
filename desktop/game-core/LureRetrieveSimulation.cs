using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;

namespace FishingDesktop.GameCore
{
    // The owner binds these already-owned instances after paying for a cast.
    // The 12 g spoon has an integral hook and attaches directly to the line.
    internal sealed class LureRetrieveRig
    {
        internal const string LureDefinition="coast-spoon-12g";
        internal const int LureMassMg=12000;
        internal readonly Guid RigId,RodId,ReelId,LineId,LureId;
        internal readonly long RigRevision,LureRevision;
        internal readonly int TotalLineMm;
        internal LureRetrieveRig(Guid rigId,long rigRevision,Guid rodId,Guid reelId,Guid lineId,Guid lureId,long lureRevision,int totalLineMm)
        {
            var ids=new HashSet<Guid>{rigId,rodId,reelId,lineId,lureId};
            if(ids.Contains(Guid.Empty)||ids.Count!=5)throw new ArgumentException("Each installed part requires a distinct identity.");
            if(rigRevision<1||lureRevision<1||totalLineMm<1||totalLineMm>100000)throw new ArgumentOutOfRangeException("rig specification");
            RigId=rigId;RigRevision=rigRevision;RodId=rodId;ReelId=reelId;LineId=lineId;LureId=lureId;LureRevision=lureRevision;TotalLineMm=totalLineMm;
        }
        internal bool Contains(Guid id){return id==RigId||id==RodId||id==ReelId||id==LineId||id==LureId;}
    }
    internal enum LureRetrievePhase { InWater, Following, Strikeable, BottomRecovery, RetrievePending, Hooked, Ended }
    internal enum LureWaterLayer { Surface, Upper, Middle, Deep }
    internal delegate int LureActivitySampler(FloatFightPoint workPosition,double waterDepthM,long worldMilliseconds);
    internal sealed class LureRetrieveSnapshot
    {
        internal readonly Guid SessionId,EncounterId,RetrieveEventId;
        internal readonly LureRetrieveRig Rig;
        internal readonly LureRetrievePhase Phase;
        internal readonly LureWaterLayer Layer;
        internal readonly FloatFightPoint Position,Velocity;
        internal readonly double DepthM,BedClearanceM,OutLineM,RemainingLineM,SlackM,HazardPerSecond,Exposure;
        internal readonly long Step,SearchEpoch,OpenStep,CloseStep;
        internal readonly int SpeedLevel,ActivityPermille;
        internal readonly bool Reeling,BailOpen;
        // Host-only. A presentation may signal an attack but must not reveal
        // the encounter's target, weight or hazard before a confirmed catch.
        internal readonly FloatBiteTarget Target;
        internal LureRetrieveSnapshot(Guid session,LureRetrieveRig rig,LureRetrievePhase phase,LureWaterLayer layer,FloatFightPoint position,
            FloatFightPoint velocity,double depth,double clearance,double line,double slack,double hazard,double exposure,
            long step,long epoch,long open,long close,int speed,int activityPermille,bool reeling,bool bail,Guid encounter,Guid ending,FloatBiteTarget target)
        {
            SessionId=session;Rig=rig;Phase=phase;Layer=layer;Position=position;Velocity=velocity;DepthM=depth;
            BedClearanceM=clearance;OutLineM=line;RemainingLineM=rig.TotalLineMm/1000.0-line;SlackM=slack;HazardPerSecond=hazard;
            Exposure=exposure;Step=step;SearchEpoch=epoch;OpenStep=open;CloseStep=close;SpeedLevel=speed;
            ActivityPermille=activityPermille;Reeling=reeling;BailOpen=bail;EncounterId=encounter;RetrieveEventId=ending;Target=target;
        }
    }

    // Original near-shore model. The submerged spoon moves in the plane of
    // its cast bearing. A closed spool caps the straight line span. Paid-out
    // slack must first be wound in before the horizontal path contracts.
    internal sealed class LureRetrieveSimulation
    {
        internal const int StepMilliseconds=50;
        const double Dt=.05,FreeSink=.34,MaximumPayout=.75;
        static readonly double[] winding={.35,.70,1.10,1.50,2.0};
        static readonly double[] runningDepth={3.0,1.7,.95,.50,.22};
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        readonly Guid session;
        readonly LureRetrieveRig rig;
        readonly FloatFightWater water;
        readonly FloatFightPoint anchor;
        readonly FloatBiteCandidate bass;
        readonly LureActivitySampler sampleActivity;
        readonly FloatBiteRandom searchRandom,massRandom;
        readonly uint fightSeed;
        readonly bool net;
        readonly double dirX,dirZ,nearRadius,minimumLine,above;
        double radius,depth,line,stroke,clearance,hazard,exposure,threshold,followGap;
        int speedLevel=2,pauseSteps,steadySteps,followSteps,remainder,activityPermille=1000;
        long step,epoch,open,close,control,lastInput;
        bool controlled,reeling,bail,paused;
        LureRetrievePhase phase=LureRetrievePhase.InWater;
        LureWaterLayer layer;
        Guid encounter,ending;
        FloatBiteTarget target;
        FloatFightPoint velocity=new FloatFightPoint(0,0,0);

        LureRetrieveSimulation(Guid session,LureRetrieveRig rig,FloatStillWaterGeometry geometry,FloatFlightSnapshot contact,
            FloatBiteCandidate bass,uint seed,bool net,double dx,double dz,double horizontal,LureActivitySampler sampleActivity)
        {
            this.session=session;this.rig=rig;this.bass=bass;this.net=net;this.sampleActivity=sampleActivity;
            searchRandom=new FloatBiteRandom(seed^0xa341316cU);massRandom=new FloatBiteRandom(seed^0xad90777dU);fightSeed=seed^0x4c555246U;
            dirX=dx/horizontal;dirZ=dz/horizontal;radius=horizontal;
            anchor=new FloatFightPoint(geometry.AnchorXmm/1000.0,geometry.AnchorYmm/1000.0,geometry.AnchorZmm/1000.0);
            var stations=new double[geometry.BedKnots.Count];var ys=new double[stations.Length];
            for(int i=0;i<stations.Length;i++){stations[i]=geometry.BedKnots[i].Zmm/1000.0;ys[i]=(geometry.SurfaceYmm-geometry.BedKnots[i].DepthMm)/1000.0;}
            var bed=new FloatFightBed(stations,ys);
            water=new FloatFightWater(geometry.SurfaceYmm/1000.0,bed.MinimumY,geometry.MinimumXmm/1000.0,geometry.MaximumXmm/1000.0,
                geometry.MinimumZmm/1000.0,geometry.MaximumZmm/1000.0,bed);
            above=anchor.Y-water.SurfaceY;nearRadius=(water.MinimumZ-anchor.Z)/dirZ;
            minimumLine=Span(nearRadius,0);line=contact.LineUsedMm/1000.0;
            clearance=water.SurfaceY-bed.Elevation(contact.Zmm/1000.0);BeginSearch();
        }
        static bool Finite(double x){return !Double.IsNaN(x)&&!Double.IsInfinity(x);}
        internal static string CheckCast(LureRetrieveRig rig,FloatCastLaunchPlan releasePlan,FloatBiteCandidate seaBass)
        {
            if(rig==null||releasePlan==null||seaBass==null)return "configuration-missing";
            var geometry=releasePlan.Geometry;
            if(geometry.WaterId!="coastal-bay"||geometry.SpotId!="rock-bank"||geometry.Version!=3)return "lure-spot-unavailable";
            if(seaBass.SpeciesId!="spotted-seabass"||seaBass.MinimumMassMg<480000||seaBass.MaximumMassMg>2600000)return "lure-species-unconfigured";
            if(releasePlan.RequiredLineMm>rig.TotalLineMm)return "lure-contact-line-mismatch";
            double dx=(releasePlan.PredictedXmm-geometry.AnchorXmm)/1000.0,dz=(releasePlan.PredictedZmm-geometry.AnchorZmm)/1000.0;
            double height=(geometry.AnchorYmm-geometry.SurfaceYmm)/1000.0;
            // The pickup radius is five metres from the rod tip. A bearing
            // that meets land too far from the angler must be refused before
            // a cast is paid for, as well as when the flight is handed over.
            double shore=(geometry.MinimumZmm-geometry.AnchorZmm)/1000.0*Math.Sqrt(dx*dx+dz*dz)/dz;
            return Math.Sqrt(shore*shore+height*height)>5?"lure-return-path-unreachable":null;
        }
        internal static string AtWaterContact(Guid session,LureRetrieveRig rig,FloatCastLaunchPlan releasePlan,
            FloatFlightSnapshot contact,FloatBiteCandidate seaBass,uint seed,bool netEquipped,out LureRetrieveSimulation run,
            LureActivitySampler sampleActivity=null)
        {
            run=null;
            string error=CheckCast(rig,releasePlan,seaBass);if(error!=null)return error;
            var geometry=releasePlan.Geometry;
            if(contact==null||session==Guid.Empty||rig.Contains(session)||contact.SessionId!=session||
                contact.Phase!=FloatFlightPhase.Waiting||contact.Reason!="water-contact")return "confirmed-contact-required";
            if(!Finite(contact.Xmm)||!Finite(contact.Ymm)||!Finite(contact.Zmm)||!Finite(contact.LineUsedMm)||
                contact.Xmm<geometry.MinimumXmm||contact.Xmm>geometry.MaximumXmm||contact.Zmm<geometry.MinimumZmm||
                contact.Zmm>geometry.MaximumZmm||Math.Abs(contact.Ymm-geometry.SurfaceYmm)>.00001||
                contact.PowerPermille!=releasePlan.PowerPermille||Math.Abs(contact.Xmm-releasePlan.PredictedXmm)>.00001||
                Math.Abs(contact.Zmm-releasePlan.PredictedZmm)>.00001)return "lure-contact-invalid";
            double dx=(contact.Xmm-geometry.AnchorXmm)/1000.0,dz=(contact.Zmm-geometry.AnchorZmm)/1000.0;
            double horizontal=Math.Sqrt(dx*dx+dz*dz),height=(geometry.AnchorYmm-geometry.SurfaceYmm)/1000.0;
            double span=Math.Sqrt(horizontal*horizontal+height*height);
            if(contact.LineUsedMm>rig.TotalLineMm||Math.Abs(span*1000-contact.LineUsedMm)>.00001)return "lure-contact-line-mismatch";
            run=new LureRetrieveSimulation(session,rig,geometry,contact,seaBass,seed,netEquipped,dx,dz,horizontal,sampleActivity);return null;
        }
        void Check(){if(thread!=Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Use the lure owner's thread.");}
        bool Active{get{return phase!=LureRetrievePhase.Hooked&&phase!=LureRetrievePhase.RetrievePending&&phase!=LureRetrievePhase.Ended;}}
        double Span(double h,double d){return Math.Sqrt(h*h+(above+d)*(above+d));}
        FloatFightPoint Position(){return new FloatFightPoint(anchor.X+dirX*radius,water.SurfaceY-depth,Math.Max(water.MinimumZ,anchor.Z+dirZ*radius));}
        internal LureRetrieveSnapshot Snapshot{get{Check();return new LureRetrieveSnapshot(session,rig,phase,layer,Position(),velocity,depth,
            clearance,line,Math.Max(0,line-Span(radius,depth)),hazard,exposure,step,epoch,open,close,speedLevel,activityPermille,reeling,bail,encounter,ending,target);}}
        Guid Identity(byte kind,long serial)
        {
            var data=new byte[32];Array.Copy(session.ToByteArray(),data,16);
            for(int i=0;i<8;i++)data[16+i]=(byte)((ulong)serial>>(8*i));
            data[24]=76;data[25]=85;data[26]=82;data[27]=69;data[28]=kind;data[29]=1;
            byte[] digest;using(var sha=SHA256.Create())digest=sha.ComputeHash(data);
            var bytes=new byte[16];Array.Copy(digest,bytes,16);bytes[7]=(byte)((bytes[7]&15)|128);bytes[8]=(byte)((bytes[8]&63)|128);return new Guid(bytes);
        }
        void ClearTarget(){target=null;encounter=Guid.Empty;open=close=0;}
        void BeginSearch()
        {
            ClearTarget();phase=LureRetrievePhase.InWater;epoch=checked(epoch+1);exposure=0;
            threshold=-Math.Log(1-searchRandom.OpenUnit());
        }
        void StopInput(){reeling=false;stroke=0;steadySteps=0;pauseSteps=0;}
        internal void SetControl(long generation,bool allowed)
        {
            Check();if(generation<=control)throw new ArgumentOutOfRangeException("generation");
            control=generation;controlled=allowed;lastInput=0;StopInput();
        }
        string Input(Guid rigId,Guid sessionId,long generation,long sequence)
        {
            Check();
            if(rigId!=rig.RigId||sessionId!=session)return "lure-not-current";
            if(!controlled||paused||control!=generation)return "control-required";
            if(sequence<=lastInput)return "stale-input";
            lastInput=sequence;return Active?null:"lure-not-active";
        }
        internal string SetReeling(Guid rigId,Guid sessionId,long generation,long sequence,bool start)
        {
            string error=Input(rigId,sessionId,generation,sequence);if(error!=null)return error;
            if(start){bail=false;if(!reeling){reeling=true;stroke=0;steadySteps=0;pauseSteps=0;}}
            else if(reeling){pauseSteps=stroke>=.65&&steadySteps>=5?12:0;reeling=false;steadySteps=0;stroke=0;}
            return null;
        }
        internal string SetBail(Guid rigId,Guid sessionId,long generation,long sequence,bool opened)
        {
            string error=Input(rigId,sessionId,generation,sequence);if(error!=null)return error;
            if(opened)StopInput();bail=opened;return null;
        }
        internal string SetSpeed(Guid rigId,Guid sessionId,long generation,long sequence,int level)
        {
            string error=Input(rigId,sessionId,generation,sequence);if(error!=null)return error;
            if(level<0||level>=winding.Length)return "speed-out-of-range";
            if(speedLevel!=level){steadySteps=0;stroke=0;}
            speedLevel=level;return null;
        }
        void Tick(long worldMilliseconds)
        {
            step=checked(step+1);
            var previous=Position();double previousRadius=radius,previousDepth=depth;
            bool taut=line-Span(radius,depth)<=.001;
            double vertical=FreeSink;
            if(reeling&&taut){
                double desired=phase==LureRetrievePhase.BottomRecovery?0:runningDepth[speedLevel];
                vertical=Math.Max(-.42,Math.Min(.24,(desired-depth)/Dt));
            }
            double candidateDepth=Math.Max(0,depth+vertical*Dt);
            if(reeling)line=Math.Max(minimumLine,line-winding[speedLevel]*Dt);
            else if(bail)line=Math.Min(rig.TotalLineMm/1000.0,Math.Max(line,Math.Min(Span(radius,candidateDepth),line+MaximumPayout*Dt)));
            // Only the near-edge pickup may shorten the vertical leg instead
            // of drawing the spoon through land. Every move stays on its ray.
            candidateDepth=Math.Min(candidateDepth,Math.Max(0,Math.Sqrt(Math.Max(0,line*line-nearRadius*nearRadius))-above));
            double verticalSpan=above+candidateDepth;
            radius=Math.Max(nearRadius,Math.Min(radius,Math.Sqrt(Math.Max(0,line*line-verticalSpan*verticalSpan))));
            var z=Math.Max(water.MinimumZ,anchor.Z+dirZ*radius);
            double bottomDepth=water.SurfaceY-water.Bed.Elevation(z);
            depth=Math.Min(candidateDepth,bottomDepth);clearance=bottomDepth-depth;
            if(candidateDepth>=bottomDepth&&phase!=LureRetrievePhase.BottomRecovery){ClearTarget();phase=LureRetrievePhase.BottomRecovery;hazard=0;exposure=0;}
            layer=depth<.12?LureWaterLayer.Surface:depth<.55?LureWaterLayer.Upper:depth<=1.60?LureWaterLayer.Middle:LureWaterLayer.Deep;
            var point=Position();velocity=new FloatFightPoint((point.X-previous.X)/Dt,(point.Y-previous.Y)/Dt,(point.Z-previous.Z)/Dt);
            if(reeling&&radius<=nearRadius+.000001&&depth<.002&&line<=minimumLine+.000001){
                ClearTarget();StopInput();phase=LureRetrievePhase.RetrievePending;ending=Identity(2,0);hazard=0;return;
            }
            if(phase==LureRetrievePhase.BottomRecovery)return;
            double moved=previousRadius-radius;
            if(reeling&&moved>.001){steadySteps++;stroke+=moved;}else if(reeling){steadySteps=0;stroke=0;}
            double horizontalSpeed=moved/Dt;
            double motionFactor=reeling&&steadySteps>=5&&stroke>=.25&&horizontalSpeed>=.30&&horizontalSpeed<=1.80?1:
                !reeling&&pauseSteps>0&&depth>previousDepth?.4:0;
            if(!reeling&&pauseSteps>0)pauseSteps--;
            double waterFactor=layer==LureWaterLayer.Upper?.45:layer==LureWaterLayer.Middle?1:layer==LureWaterLayer.Deep?.3:0;
            double presentationRate=clearance>=.12&&bass.Weight>0?.60*motionFactor*waterFactor:0;
            if(sampleActivity!=null){
                activityPermille=sampleActivity(point,bottomDepth,worldMilliseconds);
                if(activityPermille<0||activityPermille>10000)throw new InvalidOperationException("Lure activity must be in [0,10000] permille.");
            }
            hazard=presentationRate*(activityPermille/1000.0);
            if(phase==LureRetrievePhase.InWater){
                exposure+=hazard*Dt;
                if(exposure>=threshold){
                    target=new FloatBiteTarget(bass.SpeciesId,bass.MinimumMassMg+(long)massRandom.Bounded((ulong)(bass.MaximumMassMg-bass.MinimumMassMg+1)));
                    encounter=Identity(1,epoch);followGap=.60;followSteps=0;phase=LureRetrievePhase.Following;
                }
            }else if(phase==LureRetrievePhase.Following){
                // Weather governs new searches. An already locked follower
                // still leaves only for the original physical reasons.
                if(presentationRate==0||++followSteps>40){BeginSearch();return;}
                double travel=Math.Sqrt(moved*moved+(depth-previousDepth)*(depth-previousDepth));
                followGap=Math.Max(0,followGap+travel-2.35*Dt);
                if(followGap<=.08){phase=LureRetrievePhase.Strikeable;open=step;close=checked(step+20);}
            }else if(phase==LureRetrievePhase.Strikeable&&(step>=close||bail||clearance<.12||line-Span(radius,depth)>.12))BeginSearch();
        }
        internal string Advance(Guid sessionId,int activeMilliseconds,bool isPaused=false,long? worldEndMilliseconds=null)
        {
            Check();if(activeMilliseconds<0||activeMilliseconds>250)throw new ArgumentOutOfRangeException("activeMilliseconds");
            if(session!=sessionId)return "lure-not-current";
            paused=isPaused;if(isPaused||!controlled){StopInput();return null;}
            if(!Active)return "lure-not-active";
            if(sampleActivity!=null&&activeMilliseconds>0&&(!worldEndMilliseconds.HasValue||worldEndMilliseconds.Value<activeMilliseconds))
                throw new ArgumentOutOfRangeException("worldEndMilliseconds");
            remainder+=activeMilliseconds;
            while(remainder>=StepMilliseconds&&Active){
                remainder-=StepMilliseconds;
                Tick(worldEndMilliseconds.GetValueOrDefault()-remainder);
            }
            return null;
        }
        internal string StrikeAndStart(Guid rigId,Guid sessionId,long generation,long sequence,Guid encounterId,out FloatFightSimulation fight)
        {
            fight=null;string error=Input(rigId,sessionId,generation,sequence);if(error!=null)return error;
            if(encounterId!=encounter)return "encounter-not-current";
            if(phase!=LureRetrievePhase.Strikeable){if(phase==LureRetrievePhase.Following)BeginSearch();return "empty-strike";}
            if(bail||line-Span(radius,depth)>.12)return "lure-line-not-engaged";
            var setup=new FloatFightSetup(session,encounter,target,rig.RigRevision,anchor,Position(),rig.TotalLineMm,line,
                water,new FloatFightRigRules(80,1,40,200,1,20,.2,1.6,40,3,2.5,5,5),
                new FloatFightFishRules(3,2,20,.05,.005,10),new FloatLandingRules(net,5,1,1,.5),fightSeed,new FloatUnhookRules(.25,60,2));
            fight=new FloatFightSimulation(setup);StopInput();phase=LureRetrievePhase.Hooked;hazard=0;return null;
        }
        internal string ConfirmRetrieve(Guid sessionId,Guid eventId)
        {
            Check();if(sessionId!=session||eventId==Guid.Empty||eventId!=ending)return "retrieve-not-current";
            if(phase==LureRetrievePhase.Ended)return "retrieve-replayed";
            if(phase!=LureRetrievePhase.RetrievePending)return "lure-not-ready";
            phase=LureRetrievePhase.Ended;return null;
        }
    }
}
