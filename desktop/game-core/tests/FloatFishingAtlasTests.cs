using System;
using System.Collections.Generic;
using System.Linq;
using FishingDesktop.GameCore;

static class FloatFishingAtlasTests
{
    static int passed,failed;
    static void Need(bool result,string message){if(!result)throw new InvalidOperationException(message);}
    static void Test(string name,Action check)
    {
        try{check();passed++;Console.WriteLine("PASS "+name);}
        catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}
    }
    static FloatGameRules Rules(long version,IEnumerable<FloatSpeciesRule> rows)
    {return new FloatGameRules(version,"float-starter","worm",2,100,2,1000000,rows);}
    static FloatWaterSample Land(FloatFishingWater water,int power)
    {
        var g=water.Geometry;
        var planned=FloatCastLaunchPlanner.Plan(g,5000,0,g.Version,power);
        Need(planned.Accepted && planned.Plan.RequiredLineMm<=50000,"Destination cannot be cast with the starter spool.");
        var p=planned.Plan;
        var launch=new FloatFlightLaunch(new FloatFlightPoint(g.AnchorXmm,g.AnchorYmm,g.AnchorZmm),
            new FloatFlightPoint(p.Target.Xmm,p.Target.SurfaceYmm,p.Target.Zmm),
            new FloatFlightWater(g.SurfaceYmm,g.MinimumXmm,g.MaximumXmm,g.MinimumZmm,g.MaximumZmm),
            new FloatFlightRules(p.MinimumSpeedMmPerSecond,p.FullSpeedMmPerSecond,p.LaunchAngleDegrees,p.GravityMmPerSecondSquared,p.MaximumFlightMilliseconds),
            power,p.RequiredLineMm);
        var session=Guid.NewGuid();var flight=new FloatFlightSimulation(session,launch);
        for(int i=0;i<100&&flight.Snapshot.Phase==FloatFlightPhase.Flying;i++)flight.Advance(session,250);
        var f=flight.Snapshot;
        Need(f.Phase==FloatFlightPhase.Waiting,"Flight did not reach the selected water.");
        var actual=g.Sample((int)Math.Round(f.Xmm),(int)Math.Round(f.Zmm),g.Version);
        Need(actual.Accepted,"No real water contact sample.");return actual.Sample;
    }
    static FloatBiteTarget Draw(FloatBiteEnvironment env,uint seed)
    {
        var session=Guid.NewGuid();var bite=new FloatBiteSimulation(session,seed,new FloatBiteRules(1,4,4,32),env);
        for(int i=0;i<1200 && bite.Snapshot.Target==null;i++)bite.Advance(session,250);
        Need(bite.Snapshot.Target!=null,"No encounter selected after five minutes of simulated active time.");
        return bite.Snapshot.Target;
    }
    static FloatGameState Adopt(FloatGameState state,FloatCommand command)
    {
        var result=FloatGameReducer.Reduce(state,command,state.Rules);
        Need(result.RequiresConfirmation && result.Candidate!=null,"Pure store candidate refused: "+result.Code);
        return result.Candidate;
    }
    static Guid G(){return Guid.NewGuid();}

    static int Main()
    {
        var rules=Rules(FloatFishingAtlas.NewRulesVersion,FloatFishingAtlas.SpeciesRules);
        var old=Rules(1,new[]{new FloatSpeciesRule("carp",100000,10000000,120),new FloatSpeciesRule("crucian",50000,3000000,80)});
        var still=FloatFishingAtlas.Find("development-still-water");
        var river=FloatFishingAtlas.Find("river-bend");
        var coast=FloatFishingAtlas.Find("coastal-bay");
        Test("three stable domestic water IDs separate freshwater from marine stocks",()=>{
            Need(FloatFishingAtlas.Waters.SequenceEqual(new[]{still,river,coast}) && FloatFishingAtlas.Available(rules).Count==3 &&
                new[]{still.Kind,river.Kind,coast.Kind}.SequenceEqual(new[]{"freshwater","freshwater","marine"}) &&
                FloatFishingAtlas.Find("COASTAL-BAY")==null && FloatFishingAtlas.Find(null)==null,"Invalid directory identity.");
        });
        Test("the first station retains its original bed while other maps reject its geometry version",()=>{
            var original=FloatStillWaterGeometry.CreateDevelopment();
            foreach(var k in original.BedKnots)Need(still.Geometry.Sample(0,k.Zmm,1).Sample.DepthMm==k.DepthMm,"First bed changed.");
            Need(river.Geometry.Version==2 && coast.Geometry.Version==3 &&
                !river.Geometry.ResolveAim(5000,5000,1).Accepted && !coast.Geometry.ResolveAim(5000,5000,2).Accepted,"A stale map aim was accepted.");
        });
        Test("station manifests expose the intended seven species with stable matching reference IDs",()=>{
            Need(still.SpeciesFor(rules).Select(x=>x.Id).SequenceEqual(new[]{"carp","crucian"}) &&
                river.SpeciesFor(rules).Select(x=>x.Id).SequenceEqual(new[]{"grass-carp","blunt-snout-bream","carp"}) &&
                coast.SpeciesFor(rules).Select(x=>x.AssetId).SequenceEqual(new[]{"sw_black_seabream","sw_spotted_seabass","sw_yellowfin_seabream"}) &&
                rules.Species.Count==7 && rules.Find("carp").MinorPerKg==old.Find("carp").MinorPerKg,"Missing or substituted species.");
        });
        Test("old two-species saves see only legal freshwater fish without changing their sale rules",()=>{
            var signature=old.Signature;
            Need(FloatFishingAtlas.Available(old).SequenceEqual(new[]{still,river}) && river.SpeciesFor(old).Single().Id=="carp" &&
                coast.SpeciesFor(old).Count==0 && coast.CreatePool(Land(coast,1000),old)==null &&
                old.Signature==signature && old.Species.Count==2,"Old save compatibility or prices changed.");
        });
        Test("unknown versions and missing rules produce no eligible water or fish pool",()=>{
            foreach(var saved in new[]{Rules(3,FloatFishingAtlas.SpeciesRules),null})
                Need(FloatFishingAtlas.Available(saved).Count==0 && still.CreatePool(Land(still,0),saved)==null,"Unknown rules inferred a stock.");
        });
        Test("saved mass ranges intersect encounter limits and nonoverlapping species disappear",()=>{
            var narrowed=Rules(1,new[]{new FloatSpeciesRule("carp",600000,600000,500),new FloatSpeciesRule("crucian",700000,900000,300)});
            var pool=still.CreatePool(Land(still,0),narrowed).Environment;
            var row=pool.Candidates.Single();
            Need(row.SpeciesId=="carp" && row.MinimumMassMg==600000 && row.MaximumMassMg==600000 &&
                narrowed.Find("carp").Value(600000)==300,"Encounter escaped the frozen save's admissible ranges.");
        });
        Test("foreign or altered landing observations cannot seed an encounter",()=>{
            var real=Land(still,1000);
            Need(river.CreatePool(real,rules)==null && still.CreatePool(null,rules)==null &&
                still.CreatePool(new FloatWaterSample(still.Geometry,real.Xmm,real.Zmm,real.DepthMm+1),rules)==null &&
                still.CreatePool(new FloatWaterSample(still.Geometry,999999,real.Zmm,real.DepthMm),rules)==null,"Malformed contact produced a fish pool.");
        });
        foreach(var water in FloatFishingAtlas.Waters){
            Test(water.Id+" uses different fish weights for low and full power landings toward the same target",()=>{
                var near=Land(water,0);var far=Land(water,1000);
                var shallow=water.CreatePool(near,rules);var deep=water.CreatePool(far,rules);
                Need(near.Zmm<far.Zmm && near.DepthMm<water.DeepThresholdMm && far.DepthMm>=water.DeepThresholdMm &&
                    shallow.Id=="shallow" && deep.Id=="deep" && shallow.Name!=deep.Name &&
                    shallow.Environment.TotalWeight==10 && deep.Environment.TotalWeight==10 &&
                    !shallow.Environment.Candidates.Select(x=>x.Weight).SequenceEqual(deep.Environment.Candidates.Select(x=>x.Weight)),
                    "Pools did not follow actual water depth.");
            });
        }
        Test("depth exactly on the still water threshold uses the deep pool and one millimetre nearer uses shallow",()=>{
            var edge=still.Geometry.Sample(0,21000,1).Sample;var near=still.Geometry.Sample(0,20999,1).Sample;
            Need(edge.DepthMm==still.DeepThresholdMm && still.CreatePool(edge,rules).Id=="deep" &&
                near.DepthMm==still.DeepThresholdMm-1 && still.CreatePool(near,rules).Id=="shallow","Depth boundary classified inconsistently.");
        });
        Test("real seeded bite draws in each pool use legal species, masses and reproducible values",()=>{
            foreach(var water in FloatFishingAtlas.Waters)foreach(int power in new[]{0,1000}){
                var env=water.CreatePool(Land(water,power),rules).Environment;
                var seen=new HashSet<string>();
                for(uint seed=1;seed<=128;seed++){
                    var t=Draw(env,seed);var c=env.Candidates.Single(x=>x.SpeciesId==t.SpeciesId);
                    Need(t.MassMg>=c.MinimumMassMg && t.MassMg<=c.MaximumMassMg,"Encounter mass outside its range.");
                    if(seed==1){var again=Draw(env,seed);Need(again.SpeciesId==t.SpeciesId && again.MassMg==t.MassMg,"Seed changed the draw.");}
                    seen.Add(t.SpeciesId);
                }
                Need(seen.Count==env.Candidates.Count,"Some enabled species never appeared in the fixed seed cohort.");
            }
        });
        Test("all seven sale rules permit host landing and a normal sale at the saved price",()=>{
            var rig=new FixedFloatRig(G(),1,"float-starter",G(),G(),G(),50000,G(),G(),G());
            var state=FloatGameState.CreateNew(G(),G(),100,100,G(),"worm",12,rig,rules);
            foreach(var rule in rules.Species){
                long startingMoney=state.WalletMinor;
                state=Adopt(state,new CastFloat(state.ProfileId,state.BranchId,G(),G(),G(),state.Sequence,1,rules.Version));
                state=Adopt(state,new HostFloatFish(state.ProfileId,state.BranchId,G(),G(),state.Session.Id,G(),G(),rule.SpeciesKey,1000000,state.Sequence,1,rules.Version));
                var fish=state.Creel.Single();
                Need(fish.SpeciesKey==rule.SpeciesKey && fish.SaleValueMinor==rule.MinorPerKg && fish.RulesVersion==rules.Version,"Fish sale value mismatch.");
                state=Adopt(state,new SellFloatFish(state.ProfileId,state.BranchId,G(),G(),new[]{fish.Id},state.Sequence,1,rules.Version));
                Need(state.WalletMinor==startingMoney+rule.MinorPerKg && state.Creel.Count==0 && state.Session==null,"Sale did not settle exactly once.");
            }
            Need(state.Sequence==21 && state.BaitCount==5 && state.Stamina==86,"Seven fishing sessions had unexpected resource costs.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
