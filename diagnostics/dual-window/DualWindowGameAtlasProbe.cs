using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    Dictionary<string,object> AtlasProbeSelect(ProbeWindow pane,string lease,string id,long version)
    {
        return new Dictionary<string,object>{{"kind","game-spot-select"},{"documentId",pane.Ticket.DocumentId},{"lease",lease},
            {"transportId",Guid.NewGuid().ToString("D")},{"spotId",id},{"expectedVersion",version}};
    }

    static bool AtlasProbeProjected(Dictionary<string,object> page,string id)
    {
        var spots=Field(page,"spotState");var water=Field(page,"waterState");
        return spots!=null && water!=null && (string)spots["activeId"]==id && (string)water["waterId"]==id &&
            Number(water,"geometryVersion")==FloatFishingAtlas.Find(id).Geometry.Version && page["spotPending"]==null;
    }

    async Task AtlasProbeSwitch(ProbeWindow pane,ProbeWindow read,string id)
    {
        string before=GameShopJournalDigest();
        await SceneProbeButton(pane,"spot-select-"+id);
        await Wait(pane,s=>AtlasProbeProjected(s,id));await Wait(read,s=>AtlasProbeProjected(s,id));
        Require(gameFishingWater.Id==id && Object.ReferenceEquals(gameWater,gameFishingWater.Geometry) &&
            gameWaterPlan==null && gameFishingPool==null && gameWaterLanding==null && before==GameShopJournalDigest(),
            "Switching the view did not clear previous water observations or wrote game progress.");
    }

    async Task<string> AtlasProbeFish(ProbeWindow pane,ProbeWindow read,string lease,string poolId,bool full,bool checkBusy)
    {
        long before=game.Snapshot.State.Sequence;int flights=gameFlightEmissions;
        await WaterProbeMouse(pane,5000,1000,true);
        await Wait(pane,s=>Field(Field(s,"inputState"),"aim")!=null && gameCast.Snapshot.Phase==FloatCastPhase.Charging &&
            (!full || gameCast.Snapshot.PowerPermille==1000),12000);
        await WaterProbeMouse(pane,5000,1000,false);
        await Wait(pane,s=>gameFlightEmissions==flights+1 && game.Snapshot.State.Sequence==before+1);
        var flight=await WaterProbeLanding(pane,full);
        await Wait(pane,s=>gameFishingPool!=null && gameBite!=null && gameBite.Snapshot.SessionId==flight.SessionId);
        var station=gameFishingWater;var observation=gameWaterLanding.Sample;var actual=gameFishingPool;
        var expected=station.CreatePool(observation,game.Snapshot.State.Rules);
        Require(expected!=null && actual.Id==poolId && expected.Id==actual.Id && actual.Name==expected.Name &&
            expected.Environment.TotalWeight==actual.Environment.TotalWeight && expected.Environment.Candidates.Count==actual.Environment.Candidates.Count &&
            observation.GeometryVersion==station.Geometry.Version && observation.WaterId==station.Id,"Host fish pool did not follow the actual landing bed.");
        for(int i=0;i<actual.Environment.Candidates.Count;i++){
            var got=actual.Environment.Candidates[i];var want=expected.Environment.Candidates[i];
            Require(got.SpeciesId==want.SpeciesId && got.Weight==want.Weight && got.MinimumMassMg==want.MinimumMassMg &&
                got.MaximumMassMg==want.MaximumMassMg,"Encounter weights or admissible masses changed.");
        }
        if(checkBusy){
            await GameMenu(pane,lease,true);string journal=GameShopJournalDigest();
            GameCode(await WaterProbeWire(pane,AtlasProbeSelect(pane,lease,"coastal-bay",station.Geometry.Version)),"spot-switch-busy",false);
            Require(Object.ReferenceEquals(station,gameFishingWater) && Object.ReferenceEquals(actual,gameFishingPool) &&
                game.Snapshot.State.Session.Id==flight.SessionId && GameShopJournalDigest()==journal,"Busy selection moved an active cast.");
            await GameMenu(pane,lease,false);
            checks.Add("a station switch during a paid cast is rejected without rewriting its session, geometry or fish pool");
        }
        await Wait(pane,s=>gameBite.Snapshot.SessionId==flight.SessionId && gameBite.Snapshot.Target!=null,60000);
        var fish=gameBite.Snapshot.Target;
        Require(actual.Environment.ContainsTarget(fish.SpeciesId) && game.Snapshot.State.Rules.Find(fish.SpeciesId)!=null,
            "Natural bite selected a species absent from this water or the saved sale rules.");
        await Wait(read,s=>Field(Field(s,"waterState"),"landing")!=null &&
            Number(Field(Field(s,"waterState"),"landing"),"depthMm")==observation.DepthMm);
        await WaterProbeRecall(pane,lease);
        return fish.SpeciesId;
    }

    async void RunGameAtlasProbe()
    {
        const string start="development-still-water",river="river-bend",coast="coastal-bay";
        try{
            Require(GameAtlasSelfTest,"Atlas probe requires its explicit private mode.");
            var pane=windows[true];var read=windows[false];await Ready(read);await Ready(pane);
            var initial=game.Snapshot.State;
            var page=await Wait(pane,s=>AtlasProbeProjected(s,start));
            await Wait(read,s=>AtlasProbeProjected(s,start));
            var rows=Field(page,"spotState")["spots"] as IEnumerable;
            Require(rows!=null && initial.Sequence==1 && initial.RulesVersion==FloatFishingAtlas.NewRulesVersion &&
                initial.Rules.Species.Count==7 && initial.WalletMinor==12000 && initial.BaitCount==12 && initial.Stamina==100,"Unexpected atlas profile.");
            var projected=new HashSet<string>(StringComparer.Ordinal);
            foreach(Dictionary<string,object> row in rows){
                var station=FloatFishingAtlas.Find((string)row["id"]);
                Require(station!=null && projected.Add(station.Id) && (string)row["name"]==station.Name,"Duplicate or unknown projected station.");
                var names=new HashSet<string>(StringComparer.Ordinal);
                foreach(string name in (IEnumerable)row["species"])names.Add(name);
                var wanted=station.SpeciesFor(initial.Rules);
                Require(names.Count==wanted.Count,"Station preview listed an unexpected fish count.");
                foreach(var fish in wanted)Require(names.Contains(fish.Name),"Station preview omitted a saved species.");
            }
            Require(projected.SetEquals(new[]{start,river,coast}),"Station menu is incomplete.");
            checks.Add("the new seven-species profile projects three distinct fishing stations and the eligible domestic fish names");

            string lease=await Activate(pane);await GameMenu(pane,lease,true);
            string journal=GameShopJournalDigest();
            GameCode(await WaterProbeWire(read,AtlasProbeSelect(read,lease,coast,1)),"control-required",false);
            var extra=AtlasProbeSelect(pane,lease,river,1);extra["depthMm"]=2000;
            GameCode(await WaterProbeWire(pane,extra),"invalid-intent",false);
            GameCode(await WaterProbeWire(pane,AtlasProbeSelect(pane,lease,"absent-station",1)),"spot-unavailable",false);
            GameCode(await WaterProbeWire(pane,AtlasProbeSelect(pane,lease,start,1)),"spot-unchanged",false);
            Require(GameShopJournalDigest()==journal && gameWater.WaterId==start,"Denied or unchanged station request mutated a save.");
            checks.Add("observer requests, extra client data, unknown stations and selecting the current station leave the journal unchanged");

            string previousLease=lease;Revoke(pane);await Wait(pane,s=>s["lease"]==null);lease=await Activate(pane);
            GameCode(await WaterProbeWire(pane,AtlasProbeSelect(pane,previousLease,coast,1)),"control-required",false);
            Require(GameShopJournalDigest()==journal && gameFishingWater.Id==start,"An expired grant moved the player.");
            checks.Add("a revoked control grant cannot change station after the same pane is authorized again");

            await AtlasProbeSwitch(pane,read,river);
            GameCode(await WaterProbeWire(pane,AtlasProbeSelect(pane,lease,coast,1)),"spot-stale",false);
            Require(gameWater.WaterId==river && GameShopJournalDigest()==journal,"Stale selection moved the player.");
            checks.Add("clicking the river station updates both pages and a delayed selection for the starting geometry is rejected");
            await GameMenu(pane,lease,false);
            string riverSpecies=await AtlasProbeFish(pane,read,lease,"shallow",false,true);
            checks.Add("a quick river throw enters its actual shallow pool and the real bite simulation selects a legal freshwater fish");

            await GameMenu(pane,lease,true);await AtlasProbeSwitch(pane,read,coast);
            checks.Add("the menu travels to the coast without creating a save transaction and clears the previous river observation");
            await GameMenu(pane,lease,false);
            string coastSpecies=await AtlasProbeFish(pane,read,lease,"deep",true,false);
            checks.Add("a full real-time coast throw enters the deeper saltwater pool and naturally selects a legal marine fish");

            await GameMenu(pane,lease,true);
            await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('spot-menu').scrollIntoView({block:'center'})");
            using(var file=new FileStream(Path.Combine(root,"game-atlas-panel.png"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
                await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
            await AtlasProbeSwitch(pane,read,start);await GameMenu(pane,lease,false);
            checks.Add("the real menu returns both panes to the original lake and disposes the previous marine pool");

            var final=game.Snapshot.State;
            Require(final.SchemaVersion==3 && final.Sequence==5 && final.Session==null && final.WalletMinor==12000 && final.BaitCount==10 &&
                final.Stamina==96 && final.Creel.Count==0 && final.Equipment.Instances.Count==6 && final.Equipment.RemainingLineMm==50000 &&
                final.Equipment.LostLineMm==0 && gameFlightEmissions==2,"Two casts and recalls did not conserve resources.");
            PrepareGameShutdown();Require(gameShutdownCode=="no-active-session" && ExitCode==0,"Returning to the lake left a recovery to perform.");
            checks.Add("two casts and recalls spend two bait and four stamina, preserve all money and tackle and exit without recovery");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new {
                passed=true,checks=checks.ToArray(),gameLoaded=true,atlasConnected=true,mapCount=projected.Count,speciesCount=final.Rules.Species.Count,
                actualPoolDraws=2,riverPool="shallow",coastalPool="deep",riverSpecies,coastalSpecies=coastSpecies,returnedToStartingWater=true,
                spotPersisted=false,devToolsMouseInput=true,nativePhysicalMouse=false,controlledCharge=false,controlledFishFacts=false,
                schemaVersion=final.SchemaVersion,sequence=final.Sequence,value=final.WalletMinor,baitCount=final.BaitCount,stamina=final.Stamina,
                equipmentInstances=final.Equipment.Instances.Count,remainingLineMm=final.Equipment.RemainingLineMm,lostLineMm=final.Equipment.LostLineMm,
                gameFlightEmissions,shutdown=gameShutdownCode}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){Fail(error);}
    }
}
