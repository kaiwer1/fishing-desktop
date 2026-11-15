using System;
using System.Collections.Generic;
using System.Linq;
using FishingDesktop.GameCore;

static class ChinaWaterConditionsTests
{
    static int passed,failed;
    static readonly ChinaWaterConditions clock=new ChinaWaterConditions(91);
    static readonly FloatFishingWater lake=FloatFishingAtlas.Find("development-still-water"),
        river=FloatFishingAtlas.Find("river-bend"),coast=FloatFishingAtlas.Find("coastal-bay");
    static readonly FloatGameRules rules=Rules(2,FloatFishingAtlas.SpeciesRules);
    static FloatGameRules Rules(long version,IEnumerable<FloatSpeciesRule> rows)
    {return new FloatGameRules(version,"float-starter","worm",2,100,2,1000000,rows);}
    static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    static void Test(string name,Action body)
    {try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static void Invalid(Action body)
    {try{body();}catch(ArgumentException){return;}throw new Exception("Invalid world input was accepted.");}
    static FloatWaterSample Land(FloatFishingWater w,bool deep)
    {return w.Geometry.Sample(0,deep?w.Geometry.AimMaximumZmm:w.Geometry.AimMinimumZmm,w.Geometry.Version).Sample;}
    static bool SameSky(ChinaWaterConditionSnapshot a,ChinaWaterConditionSnapshot b)
    {return a.WaterId==b.WaterId&&a.MinuteOfDay==b.MinuteOfDay&&a.LightPermille==b.LightPermille&&
        a.CloudPermille==b.CloudPermille&&a.RainPermille==b.RainPermille&&a.WindTenthsMps==b.WindTenthsMps&&
        a.TemperatureTenthsC==b.TemperatureTenthsC&&a.WeatherKey==b.WeatherKey;}
    static int Main()
    {
        const int day=ChinaWaterConditions.DefaultDayLengthMilliseconds;
        Test("local midnight, change of day and complete two-day cycle",()=>{
            var start=clock.Sample(lake,0);var midnight=clock.Sample(lake,990000);var last=clock.Sample(lake,989999);
            Need(start.MinuteOfDay==450&&start.DayIndex==0&&midnight.MinuteOfDay==0&&midnight.DayIndex==1&&
                last.MinuteOfDay==1439&&last.DayIndex==0,"local boundary");
            foreach(var w in FloatFishingAtlas.Waters){
                var a=clock.Sample(w,321145);var b=clock.Sample(w,321145+2L*day);
                Need(SameSky(a,b)&&b.DayIndex==a.DayIndex+2,"weather ring failed to wrap");
            }
        });
        Test("adjustable day speed scales weather and daylight together",()=>{
            var fast=new ChinaWaterConditions(91,720000);
            for(int i=0;i<2*day;i+=3074)
                Need(SameSky(clock.Sample(river,i),fast.Sample(river,i/2)),"time scale mismatch");
        });
        Test("Long.MaxValue and invalid clocks or locations",()=>{
            var tiny=new ChinaWaterConditions(UInt32.MaxValue,1000);long t=Int64.MaxValue;
            var a=tiny.Sample(coast,t);var b=tiny.Sample(coast,t%2000);
            Need(SameSky(a,b)&&a.ActiveMilliseconds==t&&a.DayIndex>0,"large elapsed time overflowed");
            Invalid(()=>new ChinaWaterConditions(0,999));Invalid(()=>new ChinaWaterConditions(0,86400001));
            Invalid(()=>clock.Sample(lake,-1));Invalid(()=>clock.Sample(null,0));
        });
        Test("weather remains continuous through front changes and midnight",()=>{
            foreach(var w in FloatFishingAtlas.Waters){
                var keys=new HashSet<string>();var previous=clock.Sample(w,0);
                for(int ms=1000;ms<=2*day;ms+=1000){
                    var s=clock.Sample(w,ms);keys.Add(s.WeatherKey);
                    Need(s.CloudPermille>=0&&s.CloudPermille<=1000&&s.RainPermille>=0&&s.RainPermille<=1000&&s.LightPermille>=0&&
                        s.LightPermille<=1000&&s.WindTenthsMps>=0&&s.WindTenthsMps<100&&s.TemperatureTenthsC>=100&&s.TemperatureTenthsC<300,"environment range");
                    Need(Math.Abs(s.CloudPermille-previous.CloudPermille)<=5&&Math.Abs(s.RainPermille-previous.RainPermille)<=5&&
                        Math.Abs(s.LightPermille-previous.LightPermille)<=8&&Math.Abs(s.WindTenthsMps-previous.WindTenthsMps)<=1&&
                        Math.Abs(s.TemperatureTenthsC-previous.TemperatureTenthsC)<=1,"jump over one local minute");
                    previous=s;
                }
                Need(keys.SetEquals(new[]{"clear","cloudy","overcast","rain"}),"missing local front");
                Need(clock.Sample(w,270000).LightPermille>clock.Sample(w,990000).LightPermille+500,"night failed to darken");
            }
        });
        Test("site, seed and view order do not create independent clocks",()=>{
            const int t=612000;var start=clock.Sample(lake,t);
            var expected=clock.CreatePool(lake,Land(lake,false),rules,t).Environment;
            var local=new HashSet<string>();
            foreach(var w in FloatFishingAtlas.Waters.Reverse()){
                var s=clock.Sample(w,t);local.Add(s.CloudPermille+"/"+s.WindTenthsMps);
                Need(s.MinuteOfDay==start.MinuteOfDay,"maps do not share time");
                for(int i=0;i<3;i++)clock.CreatePool(w,Land(w,true),rules,t);
            }
            Need(SameSky(start,clock.Sample(lake,t))&&SameSky(start,new ChinaWaterConditions(91).Sample(lake,t))&&local.Count==3,"map query changed local conditions");
            var other=new ChinaWaterConditions(92).Sample(lake,t);
            var again=clock.CreatePool(lake,Land(lake,false),rules,t).Environment;
            Need(start.MinuteOfDay==other.MinuteOfDay&&start.CloudPermille!=other.CloudPermille&&again.TotalWeight==expected.TotalWeight&&
                again.HazardPerSecond==expected.HazardPerSecond,"seed or repeated fish query");
        });
        foreach(var w in FloatFishingAtlas.Waters){
            Test(w.Id+" real depth preserves the original species and modifies each marginal rate",()=>{
                foreach(bool deep in new[]{false,true}){
                    var landing=Land(w,deep);var basis=w.CreatePool(landing,rules);
                    var c=clock.Sample(w,45000);var current=clock.CreatePool(w,landing,rules,45000);
                    Need(basis.Id==(deep?"deep":"shallow")&&current.Id==basis.Id&&current.Name==basis.Name&&
                        basis.Environment.Candidates.Count==current.Environment.Candidates.Count,"wrong working depth");
                    for(int i=0;i<current.Environment.Candidates.Count;i++){
                        var a=basis.Environment.Candidates[i];var b=current.Environment.Candidates[i];
                        int rate=c.ActivityPermille(a.SpeciesId,deep);
                        Need(rate>0&&a.SpeciesId==b.SpeciesId&&a.MinimumMassMg==b.MinimumMassMg&&a.MaximumMassMg==b.MaximumMassMg&&
                            b.Weight==a.Weight*rate,"fish range or weight changed unexpectedly");
                        double marginal=basis.Environment.HazardPerSecond*a.Weight/basis.Environment.TotalWeight*rate/1000;
                        Need(Math.Abs(current.Environment.HazardPerSecond*b.Weight/current.Environment.TotalWeight-marginal)<1e-12,"hazard and mixture disagree");
                    }
                    Need(c.ActivityPermille("unknown",deep)==0,"unconfigured species responded");
                }
            });
        }
        Test("local species respond differently to rain at the same solar time",()=>{
            foreach(var w in new[]{river,coast}){
                bool found=false;
                for(int ms=0;ms<day;ms+=6000){
                    var a=clock.Sample(w,ms);var b=clock.Sample(w,ms+day);
                    if(Math.Abs(a.RainPermille-b.RainPermille)<300)continue;
                    var wet=a.RainPermille>b.RainPermille?a:b;var dry=a.RainPermille>b.RainPermille?b:a;
                    string first=w==coast?"spotted-seabass":"blunt-snout-bream";string second=w==coast?"yellowfin-seabream":"grass-carp";
                    Need(wet.ActivityPermille(first,true)>dry.ActivityPermille(first,true)&&wet.ActivityPermille(second,true)<dry.ActivityPermille(second,true),"no opposite response");
                    found=true;break;
                }
                Need(found,"no rainy / clear pair");
            }
            Need(clock.Sample(coast,0).ActivityPermille("crucian",false)==0&&clock.Sample(lake,0).ActivityPermille("spotted-seabass",true)==0,"foreign species");
        });
        Test("actual water samples and the old saved mass and price limits remain authoritative",()=>{
            var old=Rules(1,new[]{new FloatSpeciesRule("carp",615000,615000,500)});string signature=old.Signature;
            var sample=Land(lake,true);var row=clock.CreatePool(lake,sample,old,100).Environment.Candidates.Single();
            Need(row.SpeciesId=="carp"&&row.MinimumMassMg==615000&&row.MaximumMassMg==615000&&old.Signature==signature,"old overrides lost");
            Need(clock.CreatePool(coast,Land(coast,true),old,100)==null&&clock.CreatePool(river,sample,rules,100)==null&&
                clock.CreatePool(lake,null,rules,99)==null&&clock.CreatePool(lake,new FloatWaterSample(lake.Geometry,sample.Xmm,sample.Zmm,sample.DepthMm+1),rules,100)==null&&
                clock.CreatePool(lake,sample,Rules(55,FloatFishingAtlas.SpeciesRules),99)==null,"invalid sample or unsupported local stock was filled");
        });
        Test("changing current conditions preserves cumulative search, locked encounter and mass",()=>{
            var session=Guid.NewGuid();var p=Land(coast,true);var morning=clock.CreatePool(coast,p,rules,0).Environment;
            var night=clock.CreatePool(coast,p,rules,990000).Environment;
            var sim=new FloatBiteSimulation(session,91,new FloatBiteRules(1,20,20,20),morning);
            sim.Advance(session,50);var search=sim.Snapshot;
            Need(search.Phase==FloatBitePhase.Searching,"no search");
            sim.SetEnvironment(session,night);var changed=sim.Snapshot;
            Need(changed.Exposure==search.Exposure&&changed.Threshold==search.Threshold&&changed.SearchEpoch==search.SearchEpoch,"conditions restarted search");
            sim.Advance(session,50);Need(Math.Abs(sim.Snapshot.Exposure-(search.Exposure+.05*night.HazardPerSecond))<1e-12,"old hazard advanced");
            for(int i=0;i<5000&&sim.Snapshot.Target==null;i++)sim.Advance(session,50);
            var locked=sim.Snapshot;Need(locked.Target!=null,"no natural encounter");
            sim.SetEnvironment(session,morning);sim.Advance(session,50);
            Need(Object.ReferenceEquals(sim.Snapshot.Target,locked.Target)&&sim.Snapshot.EncounterId==locked.EncounterId,"changed conditions redrew fish");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
