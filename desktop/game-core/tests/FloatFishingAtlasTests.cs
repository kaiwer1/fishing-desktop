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
    static FloatWaterSample Land(FloatFishingWater water,int power,FloatShoreStation station=null)
    {
        var g=water.Geometry;
        int u=5000,v=0;
        if(station!=null){
            var walk=new FloatBankWalk(g);walk.SetMotion(Math.Sign(station.Xmm),false);
            for(int t=0;t<15;t++)walk.Advance(250);
            Need(walk.Xmm==station.Xmm,"Named station must be reachable by walking.");
            g=walk.Geometry;u=walk.NearestAimU;
            v=(int)Math.Round(10000.0*(g.AimMaximumZmm-FloatShoreStation.SoundingZmm)/(g.AimMaximumZmm-g.AimMinimumZmm));
        }
        var planned=FloatCastLaunchPlanner.Plan(g,u,v,g.Version,power);
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
        Test("the lake retains its centre bed and all surveyed maps reject legacy aim, casts and contact",()=>{
            var original=FloatStillWaterGeometry.CreateDevelopment();
            foreach(var k in original.BedKnots)Need(still.Geometry.Sample(0,k.Zmm,still.Geometry.Version).Sample.DepthMm==k.DepthMm,"Centre survey changed.");
            Need(still.Geometry.Version==4 && river.Geometry.Version==5 && coast.Geometry.Version==6,"Survey identities changed.");
            foreach(var water in FloatFishingAtlas.Waters){
                var g=water.Geometry;
                foreach(long stale in new[]{1L,2L,3L}){
                    Need(g.ResolveAim(5000,5000,stale).Code=="geometry-version-mismatch" &&
                        g.Sample(0,19000,stale).Code=="geometry-version-mismatch" &&
                        FloatCastLaunchPlanner.Plan(g,5000,5000,stale,1000).Code=="geometry-version-mismatch",
                        water.Id+" accepted legacy survey "+stale+".");
                }
                // At the centre the old bed matches exactly. Only the survey version distinguishes this contact.
                var legacy=new FloatStillWaterGeometry(g.WaterId,g.SpotId,g.Version-3,g.AnchorXmm,g.AnchorYmm,g.AnchorZmm,g.SurfaceYmm,
                    g.MinimumXmm,g.MaximumXmm,g.MinimumZmm,g.MaximumZmm,g.AimMinimumXmm,g.AimMaximumXmm,g.AimMinimumZmm,g.AimMaximumZmm,g.BedKnots);
                var oldContact=legacy.Sample(0,19000,legacy.Version).Sample;
                var current=g.Sample(0,19000,g.Version).Sample;
                Need(current.DepthMm==oldContact.DepthMm && water.CreatePool(current,rules)!=null &&
                    water.CreatePool(oldContact,rules)==null,water.Id+" failed to isolate the old contact.");
            }
        });
        Test("station manifests expose eleven species with matching reference IDs",()=>{
            Need(still.SpeciesFor(rules).Select(x=>x.Id).SequenceEqual(new[]{"carp","crucian"}) &&
                river.SpeciesFor(rules).Select(x=>x.Id).SequenceEqual(new[]{"grass-carp","blunt-snout-bream","carp","yellow-catfish","topmouth-culter"}) &&
                coast.SpeciesFor(rules).Select(x=>x.AssetId).SequenceEqual(new[]{"sw_black_seabream","sw_spotted_seabass","sw_yellowfin_seabream","sw_black_rockfish","sw_large_yellow_croaker"}) &&
                rules.Species.Count==11 && rules.Find("carp").MinorPerKg==old.Find("carp").MinorPerKg,"Missing or substituted species.");
        });
        Test("the original seven- and nine-species float draws keep their weights and masses",()=>{
            foreach(var water in new[]{river,coast})foreach(bool deep in new[]{false,true}){
                var landing=Land(water,deep?1000:0);
                var previous=water.CreatePool(landing,FloatDomesticSpeciesCatalog.Seven).Environment;
                var nine=water.CreatePool(landing,FloatDomesticSpeciesCatalog.Nine).Environment;
                var eleven=water.CreatePool(landing,rules).Environment;
                for(uint seed=1;seed<=128;seed++){
                    var before=Draw(nine,seed);var after=Draw(eleven,seed);
                    Need(before.SpeciesId==after.SpeciesId&&before.MassMg==after.MassMg,"The float catch from an existing nine-species seed changed.");
                }
                var original=water==river?new[]{
                    new FloatBiteCandidate("grass-carp",deep?2:5,550000,2400000),new FloatBiteCandidate("blunt-snout-bream",deep?3:4,220000,1050000),new FloatBiteCandidate("carp",deep?5:1,300000,1800000)
                }:new[]{
                    new FloatBiteCandidate("black-seabream",deep?5:3,330000,1900000),new FloatBiteCandidate("spotted-seabass",deep?4:1,480000,2600000),new FloatBiteCandidate("yellowfin-seabream",deep?1:6,180000,1300000)
                };
                var expected=new FloatBiteEnvironment(.18,original);
                Need(previous.TotalWeight==10 && previous.Candidates.Count==3,"New stock leaked into old water.");
                for(uint seed=1;seed<=128;seed++){
                    var a=Draw(previous,seed);var b=Draw(expected,seed);
                    Need(a.SpeciesId==b.SpeciesId&&a.MassMg==b.MassMg,"A previously supported draw changed.");
                }
            }
        });
        Test("new predators require their own method, water, current catalog and time response",()=>{
            var clock=new ChinaWaterConditions(0x1557u);
            var sites=new[]{river,coast};var methods=new[]{FishingPresentation.Spoon,FishingPresentation.Bottom};
            var keys=new[]{"topmouth-culter","large-yellow-croaker"};
            for(int i=0;i<sites.Length;i++){
                var water=sites[i];var key=keys[i];var method=methods[i];var sample=Land(water,1000);
                var current=water.CreatePool(sample,rules,method);
                Need(current.Environment.Candidates.Any(c=>c.SpeciesId==key),"missing target "+key);
                foreach(var presentation in new[]{FishingPresentation.Float,FishingPresentation.Spoon,FishingPresentation.Bottom}){
                    var previous=water.CreatePool(sample,FloatDomesticSpeciesCatalog.Nine,presentation);
                    var other=water.CreatePool(sample,rules,presentation);
                    Need(previous==null||!previous.Environment.Candidates.Any(c=>c.SpeciesId==key),"nine-species save exposed "+key);
                    if(presentation!=method)Need(other==null||!other.Environment.Candidates.Any(c=>c.SpeciesId==key),"wrong presentation "+key);
                }
                var shallow=clock.CreatePool(water,Land(water,0),rules,0,method).Environment.Candidates.Single(c=>c.SpeciesId==key);
                var deep=clock.CreatePool(water,sample,rules,0,method).Environment.Candidates.Single(c=>c.SpeciesId==key);
                Need(i==0?shallow.Weight>deep.Weight:deep.Weight>shallow.Weight,"no habitat preference "+key);
                Need(clock.Sample(sites[1-i],0).ActivityPermille(key,true)==0,"foreign activity "+key);
                var noon=clock.Sample(water,270000).ActivityPermille(key,true);
                var preferred=clock.Sample(water,i==0?630000:990000).ActivityPermille(key,true);
                Need(preferred>noon,"no time preference "+key);
            }
        });
        Test("old two-species saves see only legal freshwater fish without changing their sale rules",()=>{
            var signature=old.Signature;
            Need(FloatFishingAtlas.Available(old).SequenceEqual(new[]{still,river}) && river.SpeciesFor(old).Single().Id=="carp" &&
                river.StationFish(old,false).SequenceEqual(new[]{"鲤鱼"})&&river.StationFish(old,true).SequenceEqual(new[]{"鲤鱼"})&&
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
            Test(water.Id+" both walkable stations produce their charted, distinct feeding zones on a 19 m cast",()=>{
                var pools=new HashSet<string>();
                foreach(var station in water.ShoreStations){
                    var actual=Land(water,1000,station);
                    var chart=water.Geometry.Sample(station.Xmm,FloatShoreStation.SoundingZmm,water.Geometry.Version).Sample;
                    var pool=water.CreatePool(actual,rules);
                    Need(Math.Abs(chart.DepthMm-actual.DepthMm)<=1&&Math.Abs(actual.Zmm-FloatShoreStation.SoundingZmm)<=2&&
                        Math.Abs(chart.Xmm-actual.Xmm)<=2&&pool.Id==water.CreatePool(chart,rules).Id&&
                        water.StationFish(rules,pool.Id=="deep").Length>0,"Chart, cast or local stock differs.");
                    pools.Add(pool.Id);
                }
                Need(pools.SetEquals(new[]{"shallow","deep"}),"The two stations cast into the same feeding zone.");
            });
            Test(water.Id+" uses different fish weights for low and full power landings toward the same target",()=>{
                var near=Land(water,0);var far=Land(water,1000);
                var shallow=water.CreatePool(near,rules);var deep=water.CreatePool(far,rules);
                Need(near.Zmm<far.Zmm && near.DepthMm<water.DeepThresholdMm && far.DepthMm>=water.DeepThresholdMm &&
                    shallow.Id=="shallow" && deep.Id=="deep" && shallow.Name!=deep.Name &&
                    shallow.Environment.TotalWeight==(water==still?10:11) && deep.Environment.TotalWeight==(water==still?10:water==river?13:14) &&
                    !shallow.Environment.Candidates.Select(x=>x.Weight).SequenceEqual(deep.Environment.Candidates.Select(x=>x.Weight)),
                    "Pools did not follow actual water depth.");
            });
        }
        Test("depth exactly on the still water threshold uses the deep pool and one millimetre nearer uses shallow",()=>{
            var edge=still.Geometry.Sample(0,21000,still.Geometry.Version).Sample;var near=still.Geometry.Sample(0,20999,still.Geometry.Version).Sample;
            Need(edge.DepthMm==still.DeepThresholdMm && still.CreatePool(edge,rules).Id=="deep" &&
                near.DepthMm==still.DeepThresholdMm-1 && still.CreatePool(near,rules).Id=="shallow","Depth boundary classified inconsistently.");
        });
        Test("real casts at all six named stations encounter every local float, spoon and bottom candidate",()=>{
            var allSeen=new HashSet<string>();
            foreach(var water in FloatFishingAtlas.Waters)foreach(var station in water.ShoreStations)
            foreach(var method in new[]{FishingPresentation.Float,FishingPresentation.Spoon,FishingPresentation.Bottom}){
                var pool=water.CreatePool(Land(water,1000,station),rules,method);
                if(pool==null)continue;
                var env=pool.Environment;
                var seen=new HashSet<string>();
                for(uint seed=1;seed<=128;seed++){
                    var t=Draw(env,seed);var c=env.Candidates.Single(x=>x.SpeciesId==t.SpeciesId);
                    Need(t.MassMg>=c.MinimumMassMg && t.MassMg<=c.MaximumMassMg,"Encounter mass outside its range.");
                    if(seed==1){var again=Draw(env,seed);Need(again.SpeciesId==t.SpeciesId && again.MassMg==t.MassMg,"Seed changed the draw.");}
                    seen.Add(t.SpeciesId);
                }
                Need(seen.SetEquals(env.Candidates.Select(x=>x.SpeciesId)),station.Name+" has an unreachable species.");
                allSeen.UnionWith(seen);
            }
            Need(allSeen.SetEquals(rules.Species.Select(x=>x.SpeciesKey)),"A catalogued species cannot be drawn from any named station.");
        });
        Test("all eleven sale rules permit host landing and a normal sale at the saved price",()=>{
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
            Need(state.Sequence==33 && state.BaitCount==1 && state.Stamina==78,"Eleven fishing sessions had unexpected resource costs.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
