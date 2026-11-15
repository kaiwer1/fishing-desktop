using System;
using System.Collections.Generic;

namespace FishingDesktop.GameCore
{
    // Host resolves the actual rig, equipped net and configured species rules
    // before admitting the session. Physical settings do not create assets.
    internal sealed class BottomFightConfig
    {
        internal readonly FloatFightRigRules Rig;
        internal readonly FloatLandingRules Landing;
        internal readonly FloatUnhookRules Unhook;
        readonly Dictionary<string,FloatFightFishRules> fish;
        internal BottomFightConfig(FloatFightRigRules rig,FloatLandingRules landing,
            IDictionary<string,FloatFightFishRules> speciesRules,FloatUnhookRules unhook=null)
        {
            if(rig==null||landing==null||speciesRules==null)throw new ArgumentNullException("bottom fight configuration");
            Rig=rig;Landing=landing;Unhook=unhook;fish=new Dictionary<string,FloatFightFishRules>(StringComparer.Ordinal);
            foreach(var pair in speciesRules){
                if(String.IsNullOrEmpty(pair.Key)||pair.Value==null)throw new ArgumentException("Invalid fight species rules.");
                fish.Add(pair.Key,pair.Value);
            }
        }
        internal string ValidateEnvironment(FloatBiteEnvironment environment)
        {
            if(environment==null)return "configuration-missing";
            foreach(var row in environment.Candidates){
                if(row.Weight==0)continue;
                if(!fish.ContainsKey(row.SpeciesId))return "fight-species-unconfigured";
                if(row.MinimumMassMg<10000||row.MaximumMassMg>500000000)return "fight-mass-unsupported";
            }
            return null;
        }
        internal FloatFightFishRules Fish(string species){return fish[species];}
    }

    // Owns the one bottom encounter from actual water contact through strike.
    // The caller retains this object across leases and feeds only active time.
    internal sealed class BottomFightHandoff
    {
        readonly BottomRigSimulation bottom;
        readonly FloatFightWater water;
        readonly FloatFightPoint anchor,start;
        readonly BottomFightConfig config;
        readonly int totalLineMm;
        readonly uint fightSeed;
        bool fightStarted;

        BottomFightHandoff(BottomRigSimulation bottom,FloatFightWater water,FloatFightPoint anchor,
            FloatFightPoint start,BottomFightConfig config,int totalLineMm,uint fightSeed)
        {
            this.bottom=bottom;this.water=water;this.anchor=anchor;this.start=start;
            this.config=config;this.totalLineMm=totalLineMm;this.fightSeed=fightSeed;
        }
        static FloatFightWater WorkingWater(BottomSurveyPatch patch)
        {
            var geometry=patch.Geometry;var z=new double[geometry.BedKnots.Count];var y=new double[z.Length];
            for(int i=0;i<z.Length;i++){
                var knot=geometry.BedKnots[i];z[i]=knot.Zmm/1000.0;y[i]=(geometry.SurfaceYmm-knot.DepthMm)/1000.0;
            }
            var bed=new FloatFightBed(z,y,geometry.ShelfStartXmm/1000.0,geometry.ShelfEndXmm/1000.0,Math.Abs(geometry.ShelfDepthMm)/1000.0);
            return new FloatFightWater(geometry.SurfaceYmm/1000.0,bed.MinimumY,
                patch.MinimumXmm/1000.0,patch.MaximumXmm/1000.0,patch.MinimumZmm/1000.0,patch.MaximumZmm/1000.0,bed);
        }
        internal static string AtWaterContact(Guid confirmedSession,BottomSimpleAssembly rig,BottomSurveyPatch patch,
            long expectedGeometryVersion,int xMm,int zMm,uint seed,FloatBiteEnvironment environment,BottomFightConfig config,
            out BottomFightHandoff owner)
        {
            owner=null;
            if(config==null)return "configuration-missing";
            string error=config.ValidateEnvironment(environment);
            if(error!=null)return error;
            BottomRigSimulation bottom;
            error=BottomRigSimulation.AtWaterContact(confirmedSession,rig,patch,expectedGeometryVersion,xMm,zMm,seed,environment,out bottom);
            if(error!=null)return error;
            var contact=bottom.Snapshot.Contact;var geometry=patch.Geometry;
            var bank=new FloatFightPoint(geometry.AnchorXmm/1000.0,geometry.AnchorYmm/1000.0,geometry.AnchorZmm/1000.0);
            var hook=new FloatFightPoint(contact.Xmm/1000.0,contact.BaitYmm/1000.0,contact.BaitZmm/1000.0);
            var work=WorkingWater(patch);
            if(!work.Contains(hook))return "fight-start-outside-water";
            double dx=bank.X-hook.X,dy=bank.Y-hook.Y,dz=bank.Z-hook.Z;
            double outLine=contact.RequiredLineMm/1000.0;
            if(outLine<config.Rig.MinimumOutLineM||outLine+1e-9<Math.Sqrt(dx*dx+dy*dy+dz*dz))return "fight-line-insufficient";
            owner=new BottomFightHandoff(bottom,work,bank,hook,config,rig.LineRemainingMm,seed^0x42464754u);
            return null;
        }
        internal BottomRigSnapshot Snapshot{get{return bottom.Snapshot;}}
        internal BottomActionResult Advance(Guid sessionId,int activeMilliseconds){return bottom.Advance(sessionId,activeMilliseconds);}
        internal BottomActionResult SetEnvironment(Guid sessionId,FloatBiteEnvironment next)
        {
            string error=config.ValidateEnvironment(next);
            if(error!=null)return new BottomActionResult(false,error,Snapshot,null);
            return bottom.SetEnvironment(sessionId,next);
        }
        internal BottomActionResult End(Guid sessionId)
        {
            if(fightStarted)return new BottomActionResult(false,"fight-owns-session",Snapshot,null);
            return bottom.End(sessionId);
        }
        internal BottomActionResult StrikeAndStart(Guid sessionId,Guid rigId,long rigRevision,Guid encounterId,
            bool held,out FloatFightSimulation fight)
        {
            fight=null;
            if(fightStarted)return new BottomActionResult(false,"fight-already-started",Snapshot,null);
            var result=bottom.Strike(sessionId,rigId,rigRevision,encounterId,held);
            if(!result.ShouldStartFight)return result;
            var state=result.Snapshot;
            var setup=new FloatFightSetup(state.SessionId,state.EncounterId,result.Target,state.RigRevision,anchor,start,
                totalLineMm,state.OutLineMm/1000.0,water,config.Rig,config.Fish(result.Target.SpeciesId),config.Landing,fightSeed,config.Unhook);
            fight=new FloatFightSimulation(setup);fightStarted=true;
            return result;
        }
    }
}
