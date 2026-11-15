using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    bool GameShoreSelfTest {get{return gameMode=="shore-stations-self-test";}}

    async Task ShoreProbeOpenMap(ProbeWindow pane)
    {
        await SceneProbeButton(pane,"scene-menu");
        await Wait(pane,p=>game.Snapshot.MenuOpen);
        await SceneProbeButton(pane,"camp-go-map");
    }

    async Task ShoreProbeWalkTo(ProbeWindow pane,FloatFishingWater water,FloatShoreStation station)
    {
        int start=gameWalk.Xmm,revision=gameWalk.PoseRevision;
        string journal=GameShopJournalDigest();
        bool east=station.Xmm>start;
        string key=east?"d":"a",code=east?"KeyD":"KeyA";int vk=east?68:65;
        var before=await SceneProbeScript(pane,"({line:document.getElementById('bank-stations').innerText})");
        string arrow=east?"向右走":"向左走";
        Require(((string)before["line"]).Contains(station.Name+" · "+arrow),"The HUD does not point toward "+station.Name);
        await WalkProbeKey(pane,key,code,vk,true);
        Dictionary<string,object> last=null;
        try{
            await Wait(pane,p=>{last=p;return gameWalk.Direction==(east?1:-1) && (east?gameWalk.Xmm>=station.Xmm-220:gameWalk.Xmm<=station.Xmm+220);},16000);
        }catch(Exception error){
            throw new InvalidOperationException("Walk to "+station.Name+" from "+start+" stopped at "+gameWalk.Xmm+
                " (direction "+gameWalk.Direction+", reply "+json.Serialize(Field(last,"lastReply"))+").",error);
        }
        await WalkProbeKey(pane,key,code,vk,false);
        await Wait(pane,p=>PlayerProbeDrawn(p,water)&&WalkProbeStopped(p));
        var after=await SceneProbeScript(pane,"({line:document.getElementById('bank-stations').innerText})");
        Require(Math.Abs(gameWalk.Xmm-station.Xmm)<=1000 && gameWalk.PoseRevision>revision && Math.Abs(gameWalk.Xmm-start)>=4000 &&
            ((string)after["line"]).Contains(station.Name+" · 已到达") && GameShopJournalDigest()==journal,
            "The visible arrival or continuous host-clock walk did not match "+station.Name);
    }

    async Task ShoreProbeMap(ProbeWindow pane,FloatFishingWater water,FloatShoreStation station,
        FloatWaterSample sounding,FloatFishingPool habitat)
    {
        int pose=gameWalk.PoseRevision,x=gameWalk.Xmm;
        string journal=GameShopJournalDigest();
        await ShoreProbeOpenMap(pane);
        var chart=await SceneProbeScript(pane,@"(()=>{
            const water="+json.Serialize(water.Id)+@", station="+json.Serialize(station.Id)+@";
            const card=document.querySelector('.spot-list article[data-water='+water+']');
            const row=card?.querySelector('li[data-station='+station+']');
            return {text:row?.innerText,visible:!!row?.getClientRects().length,stations:card?.querySelectorAll('li[data-station]').length,
                noStationButtons:!card?.querySelector('li[data-station] button'),current:!!card?.querySelector('button:disabled')};
        })()");
        string text=(string)chart["text"];
        var depthMatch=System.Text.RegularExpressions.Regex.Match(text,@"水深 ([0-9]+(?:\.[0-9]+)?) 米");
        decimal displayed;
        bool showsDepth=depthMatch.Success && decimal.TryParse(depthMatch.Groups[1].Value,NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,out displayed) && Math.Abs(displayed*1000-sounding.DepthMm)<=5;
        Require((bool)chart["visible"]&&Number(chart,"stations")==2&&(bool)chart["current"]&&(bool)chart["noStationButtons"]&&
            text.Contains(station.Name)&&text.Contains("已到达")&&text.Contains("离岸 19 米")&&showsDepth&&text.Contains(habitat.Name),
            "The current camp map does not show the actual reached station and survey.");
        foreach(string fish in water.StationFish(game.Snapshot.State.Rules,habitat.Id=="deep"))
            Require(text.Contains(fish),"The station map omitted "+fish);
        await SceneProbeButton(pane,"menu");
        await Wait(pane,p=>PlayerProbeDrawn(p,water)&&WalkProbeStopped(p));
        Require(gameWalk.Xmm==x&&gameWalk.PoseRevision==pose&&GameShopJournalDigest()==journal,"Opening the map moved the angler.");
    }

    async Task<object> ShoreProbeCast(ProbeWindow pane,FloatFishingWater water,FloatShoreStation station)
    {
        var geom=water.Geometry;var rules=game.Snapshot.State.Rules;
        var survey=geom.Sample(station.Xmm,FloatShoreStation.SoundingZmm,geom.Version).Sample;
        var habitat=water.CreatePool(survey,rules);
        await ShoreProbeMap(pane,water,station,survey,habitat);
        int u=(int)Math.Round(10000.0*(survey.Xmm-geom.AimMinimumXmm)/(geom.AimMaximumXmm-geom.AimMinimumXmm));
        int v=(int)Math.Round(10000.0*(geom.AimMaximumZmm-survey.Zmm)/(geom.AimMaximumZmm-geom.AimMinimumZmm));
        var point=await SceneProbeScript(pane,@"(()=>{
            const p=dualProbe.projectSceneAim("+u+","+v+@");if(!p)return null;
            const x=Math.round(p.clientX),y=Math.round(p.clientY);
            return document.elementFromPoint(x,y)===document.getElementById('scene-canvas')?{x,y}:null;
        })()");
        long sequence=game.Snapshot.State.Sequence;int anchor=gameWalk.Xmm,castPose=gameWalk.PoseRevision;
        await SceneProbeMouse(pane,point,true);
        await Wait(pane,p=>gameCast.Snapshot.Phase==FloatCastPhase.Charging&&gameCast.Snapshot.PowerPermille==1000);
        Require(gameWaterPlan.AnchorXmm==anchor,"The cast plan did not retain the walked anchor.");
        await SceneProbeMouse(pane,point,false);
        await Wait(pane,p=>game.Snapshot.State.Sequence==sequence+1&&game.Snapshot.State.Session!=null);
        var flight=await WaterProbeLanding(pane,true);var landing=gameWaterLanding.Sample;
        await Wait(pane,p=>gameBite!=null&&gameBite.Snapshot.SessionId==flight.SessionId&&gameBite.Snapshot.Target!=null,40000);
        var observed=gameFishingPool;var tick=gameFishingPoolTimeMs;var fish=gameBite.Snapshot.Target;
        var expected=gameClimate.CreatePool(water,landing,rules,tick);
        Require(gameWaterPlan.Geometry==gameWalk.Geometry&&gameFlightOrigin.Xmm==anchor&&gameWalk.PoseRevision==castPose&&
            Math.Abs(landing.Xmm-survey.Xmm)<=650&&Math.Abs(landing.Zmm-survey.Zmm)<=650&&
            geom.Sample(landing.Xmm,landing.Zmm,geom.Version).Sample.DepthMm==landing.DepthMm&&
            Math.Abs(landing.DepthMm-survey.DepthMm)<=180&&observed.Id==habitat.Id&&observed.Id==expected.Id&&
            observed.Environment.Candidates.Count==expected.Environment.Candidates.Count&&observed.Environment.ContainsTarget(fish.SpeciesId),
            "Actual trajectory, bed or naturally selected fish disagrees with the marked water at "+station.Name);
        for(int i=0;i<expected.Environment.Candidates.Count;i++){
            var a=expected.Environment.Candidates[i];var b=observed.Environment.Candidates[i];
            Require(a.SpeciesId==b.SpeciesId&&a.Weight==b.Weight&&a.MinimumMassMg==b.MinimumMassMg&&a.MaximumMassMg==b.MaximumMassMg,
                "Encounter weights or masses differ from the real climate and bed.");
        }
        await WardrobeProbePhoto(pane,"shore-"+station.Id+".png");
        await SceneProbeButton(pane,"scene-recall");
        await Wait(pane,p=>game.Snapshot.State.Sequence==sequence+2&&game.Snapshot.State.Session==null&&
            Field(p,"snapshot")!=null&&Field(p,"snapshot")["SessionId"]==null&&Number(Field(p,"snapshot"),"Sequence")==sequence+2&&
            Field(p,"lastReply")!=null&&(string)Field(p,"lastReply")["code"]=="recall-candidate");
        return new {site=water.Id,station=station.Id,name=station.Name,stationXmm=station.Xmm,walkedXmm=anchor,
            referenceZmm=survey.Zmm,referenceDepthMm=survey.DepthMm,landingXmm=landing.Xmm,landingZmm=landing.Zmm,
            landingDepthMm=landing.DepthMm,habitat=observed.Id,climateTimeMs=tick,selectedSpecies=fish.SpeciesId};
    }

    async Task RunGameShoreProbe(ProbeWindow pane)
    {
        var evidence=new List<object>();
        foreach(var water in FloatFishingAtlas.Waters){
            if(water!=gameFishingWater){
                await ShoreProbeOpenMap(pane);await SceneProbeButton(pane,"spot-select-"+water.Id);
                await Wait(pane,p=>AtlasProbeProjected(p,water.Id));
                await SceneProbeButton(pane,"menu");
            }
            await Wait(pane,p=>PlayerProbeDrawn(p,water)&&WalkProbeStopped(p),30000);
            Require(gameWalk.Xmm==water.Geometry.AnchorXmm&&water.ShoreStations.Count==2,"Map did not start at the bank's central reference.");
            var habitats=new HashSet<string>();
            foreach(var station in water.ShoreStations){
                var sounding=water.Geometry.Sample(station.Xmm,FloatShoreStation.SoundingZmm,water.Geometry.Version).Sample;
                habitats.Add(water.CreatePool(sounding,game.Snapshot.State.Rules).Id);
                await ShoreProbeWalkTo(pane,water,station);evidence.Add(await ShoreProbeCast(pane,water,station));
                checks.Add("walked, cast at the named marker, observed the correct bed and fish pool, and recalled at "+water.Id+"/"+station.Id);
            }
            Require(habitats.SetEquals(new[]{"shallow","deep"}),"The two reference casts at the same range did not cross habitat bands.");
        }
        var final=game.Snapshot.State;
        Require(evidence.Count==6&&gameFlightEmissions==6&&final.Sequence==13&&final.BaitCount==6&&final.Stamina==88&&
            final.WalletMinor==12000&&final.Equipment.RemainingLineMm==50000&&final.Creel.Count==0&&final.Session==null,
            "The six ordinary casts and retrieves did not preserve cash and gear or spend the expected supplies.");
        PrepareGameShutdown();Require(gameShutdownCode=="no-active-session"&&ExitCode==0,"Shore probe did not finish idle.");
        File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new {passed=true,checks=checks.ToArray(),stations=evidence,
            devToolsKeyboardInput=true,devToolsMouseInput=true,controlledCharge=false,controlledFishFacts=false,rawGameCommandInjected=false,
            sequence=final.Sequence,baitCount=final.BaitCount,stamina=final.Stamina,value=final.WalletMinor,gameFlightEmissions,shutdown=gameShutdownCode}),Encoding.UTF8);
        Shutdown();
    }
}
