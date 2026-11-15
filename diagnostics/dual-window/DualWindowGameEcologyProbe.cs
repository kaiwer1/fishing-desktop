using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    bool GameEcologySelfTest {get{return gameMode=="ecology-self-test";}}

    // Advance only the ordinary host ecology clock in this private test.
    // Flight, encounter search, input and the journal continue on real time.
    void EcologyProbeAdvance(long goalMs)
    {
        Require(GameEcologySelfTest&&game.Snapshot.CanSimulate&&!game.Snapshot.MenuOpen&&
            game.Snapshot.State.Session==null&&goalMs>gameEcologyTimeMs,"Environment advance requires an idle private bank.");
        while(gameEcologyTimeMs<goalMs){
            long before=gameEcologyTimeMs;
            AdvanceGameEcology(Math.Min(250,goalMs-before)/1000.0);
            Require(gameEcologyTimeMs>before,"Active environment input was rejected.");
        }
        Broadcast();
    }

    static bool EcologyProbeVisible(Dictionary<string,object> page,FloatFishingWater water)
    {
        var state=Field(page,"weatherState");var drawn=Field(SceneProbeRenderer(page),"weather");
        if(!PlayerProbeDrawn(page,water)||state==null||drawn==null||
            (string)state["waterId"]!=water.Id||(string)drawn["waterId"]!=water.Id||
            (string)state["weatherCode"]!=(string)drawn["weatherCode"])return false;
        foreach(string name in new[]{"clockMinute","cloudPercent","windDeciMs","tempDeciC","lightPermille","rainPermille"})
            if(Number(state,name)!=Number(drawn,name))return false;
        return true;
    }

    bool EcologyProbeCurrent(Dictionary<string,object> page,FloatFishingWater water)
    {
        if(!EcologyProbeVisible(page,water))return false;
        var sky=Field(page,"weatherState");
        var current=SampleGameConditions();
        // A normal tick may have elapsed since WebView sent the sampled frame.
        long shownMinute=Number(sky,"clockMinute");
        long difference=(1440+current.MinuteOfDay-shownMinute)%1440;
        return current.WaterId==(string)sky["waterId"]&&current.WeatherKey==(string)sky["weatherCode"]&&difference<=2&&
            Math.Abs(current.LightPermille-Number(sky,"lightPermille"))<=3&&
            Math.Abs((current.CloudPermille+5)/10-Number(sky,"cloudPercent"))<=1&&
            Math.Abs(current.WindTenthsMps-Number(sky,"windDeciMs"))<=1&&
            Math.Abs(current.TemperatureTenthsC-Number(sky,"tempDeciC"))<=1&&
            Math.Abs(current.RainPermille-Number(sky,"rainPermille"))<=4;
    }

    async Task<Dictionary<string,object>> EcologyProbeSky(ProbeWindow pane,FloatFishingWater water)
    {
        var page=await Wait(pane,p=>EcologyProbeCurrent(p,water),15000);
        var hud=await SceneProbeScript(pane,@"(()=>{
            const e=document.getElementById('water-conditions'),s=dualProbe.state().weatherState;
            const t=[Math.floor(s.clockMinute/60),s.clockMinute%60].map(n=>String(n).padStart(2,'0')).join(':');
            return {visible:!e.hidden&&e.getBoundingClientRect().width>0&&e.getBoundingClientRect().height>0,
                matches:e.dataset.waterId===s.waterId&&e.dataset.weatherCode===s.weatherCode&&
                    Number(e.dataset.clockMinute)===s.clockMinute&&e.textContent.includes(t)&&
                    e.textContent.includes((s.tempDeciC/10).toFixed(1))&&e.textContent.includes((s.windDeciMs/10).toFixed(1))};
        })()");
        Require((bool)hud["visible"]&&(bool)hud["matches"],"The visible clock and meteorological HUD differ from the published conditions.");
        return Field(SceneProbeRenderer(page),"weather");
    }

    async Task<FloatFishingPool> EcologyProbeCast(ProbeWindow pane,Dictionary<string,object> point)
    {
        long before=game.Snapshot.State.Sequence;
        await SceneProbeMouse(pane,point,true);
        await Wait(pane,p=>gameCast.Snapshot.Phase==FloatCastPhase.Charging&&gameCast.Snapshot.PowerPermille==1000);
        await SceneProbeMouse(pane,point,false);
        var flight=await WaterProbeLanding(pane,true);
        await Wait(pane,p=>gameBite!=null&&gameBite.Snapshot.SessionId==flight.SessionId&&gameFishingPool!=null);
        var landing=gameWaterLanding.Sample;var state=game.Snapshot.State;
        var seen=gameFishingPool;var expected=gameClimate.CreatePool(gameFishingWater,landing,state.Rules,gameFishingPoolTimeMs);
        var baseline=gameFishingWater.CreatePool(landing,state.Rules);
        Require(state.Sequence==before+1&&seen.Id==baseline.Id&&seen.Id==expected.Id&&
            Math.Abs(seen.Environment.HazardPerSecond-expected.Environment.HazardPerSecond)<1e-10&&
            seen.Environment.Candidates.Count==baseline.Environment.Candidates.Count,"The landing used a different pool or encounter rate.");
        for(int index=0;index<seen.Environment.Candidates.Count;index++){
            var got=seen.Environment.Candidates[index];var want=expected.Environment.Candidates[index];var basis=baseline.Environment.Candidates[index];
            Require(got.SpeciesId==want.SpeciesId&&got.Weight==want.Weight&&got.SpeciesId==basis.SpeciesId&&
                got.MinimumMassMg==basis.MinimumMassMg&&got.MaximumMassMg==basis.MaximumMassMg,"Fish range or current habitat weight changed.");
        }
        await Wait(pane,p=>gameBite.Snapshot.SessionId==flight.SessionId&&gameBite.Snapshot.Target!=null,60000);
        var fish=gameBite.Snapshot.Target;
        Require(seen.Environment.ContainsTarget(fish.SpeciesId)&&state.Rules.Find(fish.SpeciesId)!=null&&game.Snapshot.State.Creel.Count==0,
            "Natural search selected a fish outside the local rules or awarded a free catch.");
        await SceneProbeButton(pane,"scene-recall");
        await Wait(pane,p=>game.Snapshot.State.Session==null&&game.Snapshot.State.Sequence==before+2);
        return seen;
    }

    async void RunGameEcologyProbe()
    {
        string stage="starting the private player";
        try{
            Require(GameEcologySelfTest&&windows.Count==1&&windows.ContainsKey(true),"Expected a single private ecology player.");
            var pane=windows[true];await Ready(pane);pane.ClientSize=new Size(1280,800);string lease=await Activate(pane);
            var lake=FloatFishingAtlas.Find("development-still-water");var coast=FloatFishingAtlas.Find("coastal-bay");
            var initial=game.Snapshot.State;string initialDigest=GameShopJournalDigest();
            Require(initial.Sequence==1&&initial.BaitCount==12&&initial.Stamina==100&&initial.WalletMinor==12000&&initial.Rules.Species.Count==7,
                "Wrong private ecology starting profile.");
            await EcologyProbeSky(pane,lake);
            long firstMs=gameEcologyTimeMs;await Wait(pane,p=>gameEcologyTimeMs>=firstMs+500);
            checks.Add("one real landscape player displays the lake conditions and its ordinary active clock advances");

            stage="pausing the world clock in the menu";
            await SceneProbeButton(pane,"scene-menu");
            await Wait(pane,p=>game.Snapshot.MenuOpen&&(bool)Field(p,"snapshot")["MenuOpen"]);
            long frozen=gameEcologyTimeMs;var weather=SampleGameConditions();
            await Task.Delay(800);
            Require(frozen==gameEcologyTimeMs&&weather.MinuteOfDay==SampleGameConditions().MinuteOfDay&&GameShopJournalDigest()==initialDigest,
                "The menu accrued weather time or changed the save.");
            await GameMenu(pane,lease,false);await Wait(pane,p=>PlayerProbeDrawn(p,lake));
            checks.Add("an open menu freezes the environment clock without money, bait or journal changes");

            stage="observing daylight and the first natural lake encounter";
            EcologyProbeAdvance(270000);
            var day=await EcologyProbeSky(pane,lake);var materials=new Dictionary<string,int>();
            var daylight=await PlayerProbePicture(pane,lake,"ecology-day.png",2,materials);
            var point=await SceneProbePoint(pane);var dayPool=await EcologyProbeCast(pane,point);
            checks.Add("a full midday mouse cast samples its real lake bed, selects a legal fish with the local weighted encounter rate and can be recalled");

            stage="observing night and repeating the same cast";
            EcologyProbeAdvance(990000);
            var night=await EcologyProbeSky(pane,lake);
            var nightPixels=await PlayerProbePicture(pane,lake,"ecology-night.png",2,new Dictionary<string,int>());
            Require(Number(day,"lightPermille")>Number(night,"lightPermille")+250&&
                Convert.ToDouble(day["sunIntensity"],CultureInfo.InvariantCulture)>Convert.ToDouble(night["sunIntensity"],CultureInfo.InvariantCulture)&&
                PlayerProbeDifferent(daylight,nightPixels),"Midday and midnight did not change the actual lake scenery.");
            checks.Add("the same shoreline shows a visibly different midnight sky and lower light in the three-dimensional renderer");
            var nightPool=await EcologyProbeCast(pane,point);
            Require(dayPool.Id==nightPool.Id&&Math.Abs(dayPool.Environment.HazardPerSecond-nightPool.Environment.HazardPerSecond)>1e-6&&
                dayPool.Environment.Candidates.Where((c,i)=>c.Weight!=nightPool.Environment.Candidates[i].Weight).Any(),
                "Casting twice at the same depth never changed either species weights or encounter rate.");
            checks.Add("the second real mouse cast reaches the same pool with different nighttime species weights and rate");

            stage="travelling under the same active clock";
            long beforeTravel=gameEcologyTimeMs;string afterCasts=GameShopJournalDigest();
            await PlayerProbeTravel(pane,lease,coast);await EcologyProbeSky(pane,coast);
            await PlayerProbeTravel(pane,lease,lake);await EcologyProbeSky(pane,lake);
            Require(gameEcologyTimeMs>=beforeTravel&&gameEcologyTimeMs<beforeTravel+30000&&afterCasts==GameShopJournalDigest(),
                "Travel restarted the clock or changed settled resources.");
            checks.Add("visiting the coast and returning to the lake changes the displayed local conditions while preserving one active calendar");

            stage="verifying settled resources";
            var final=game.Snapshot.State;
            Require(final.Sequence==5&&final.Session==null&&final.WalletMinor==12000&&final.BaitCount==10&&final.Stamina==96&&
                final.Creel.Count==0&&gameFlightEmissions==2&&final.Equipment.RemainingLineMm==50000,"Ecology changed the financial or gear settlement.");
            PrepareGameShutdown();Require(gameShutdownCode=="no-active-session"&&ExitCode==0,"Unexpected ecology shutdown recovery.");
            checks.Add("two casts and two recalls spend only two worms and four stamina and leave no active rod or awarded fish");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new {
                passed=true,gameLoaded=true,checks=checks.ToArray(),ecologyConnected=true,realClockSample=true,controlledEnvironmentTime=true,
                daylightPixelsDiffer=true,menuClockFrozen=true,localPoolVerified=true,realMouseCast=true,nativePhysicalMouse=false,controlledFishFacts=false,
                weatherPersisted=false,personalSaveRead=false,desktopLayer=false,actualPoolDraws=2,dayRate=dayPool.Environment.HazardPerSecond,
                nightRate=nightPool.Environment.HazardPerSecond,sequence=final.Sequence,value=final.WalletMinor,stamina=final.Stamina,
                baitCount=final.BaitCount,creelCount=final.Creel.Count,gameFlightEmissions,shutdown=gameShutdownCode}),Encoding.UTF8);
            Shutdown();
        }catch(Exception ex){Fail(new InvalidOperationException("Lake ecology probe failed while "+stage+": "+ex.Message,ex));}
    }
}
