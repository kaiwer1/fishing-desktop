using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    bool GameFloatSkillSelfTest {get{return gameMode=="float-skill-self-test";}}
    bool GameAnglerLogSelfTest {get{return gameMode=="angler-log-self-test"||catalogSpecies!=null||GameFloatSkillSelfTest;}}
    bool gameLogReopening;

    static long AnglerProbeXp(FloatFish fish)
    {
        return 10+(long)Math.Round(fish.MassMg*6m/1000000m,MidpointRounding.AwayFromZero);
    }

    async Task<string> AnglerProbeRead(ProbeWindow pane,FloatFish fish,string name,bool sold,bool photo)
    {
        var state=game.Snapshot.State;string digest=GameShopJournalDigest();
        var projected=await Wait(pane,p=>Field(p,"snapshot")!=null&&(bool)Field(p,"snapshot")["MenuOpen"]&&
            Field(p,"anglerLogState")!=null&&Number(Field(p,"anglerLogState"),"sequence")==state.Sequence);
        Require(!(bool)projected["anglerLogOpen"],"Log was already open before the player selected it.");
        await SceneProbeButton(pane,"camp-go-log");
        var shown=await Wait(pane,p=>(bool)p["anglerLogOpen"]&&Number(Field(p,"anglerLogState"),"sequence")==state.Sequence);
        var log=Field(shown,"anglerLogState");long xp=AnglerProbeXp(fish),value=sold?fish.SaleValueMinor:0;
        Require(Number(log,"totalCasts")==1&&Number(log,"totalCaught")==1&&Number(log,"totalSold")==(sold?1:0)&&
            Number(log,"saleMinor")==value&&Number(log,"totalXp")==xp&&state.Creel.Count==(sold?0:1),
            "The current log has a different catch, sale or experience total.");
        var dom=await SceneProbeScript(pane,@"(()=>{
            const dialog=document.querySelector('#angler-log-dialog[open]');if(!dialog)return null;
            const get=k=>dialog.querySelector('[data-log-field='+k+']')?.textContent;
            const method=dialog.querySelector('[data-log-method=float]');
            const rows=dialog.querySelectorAll('[data-log-species]');if(!method||rows.length!==1)return null;
            const row=rows[0];return {casts:get('totalCasts'),caught:get('totalCaught'),sold:get('totalSold'),xp:get('totalXp'),
                value:get('saleMinor'),methodCounts:[...method.querySelectorAll('td')].map(n=>n.textContent).join('/'),
                species:row.dataset.logSpecies,name:row.querySelector('th').textContent,
                speciesCaught:row.querySelector('td').textContent,heaviest:row.lastElementChild.textContent};
        })()");
        string money=(value/100m).ToString("F2",CultureInfo.InvariantCulture);
        string mass=(fish.MassMg/(fish.MassMg<1000000?1000m:1000000m)).ToString("0.######",CultureInfo.InvariantCulture)+
            (fish.MassMg<1000000?" 克":" 千克");
        Require(Number(dom,"casts")==1&&Number(dom,"caught")==1&&Number(dom,"sold")==(sold?1:0)&&Number(dom,"xp")==xp&&
            (string)dom["value"]==money&&(string)dom["methodCounts"]=="1/1/"+xp.ToString(CultureInfo.InvariantCulture)&&
            (string)dom["species"]==fish.SpeciesKey&&(string)dom["name"]==name&&Number(dom,"speciesCaught")==1&&(string)dom["heaviest"]==mass,
            "The visible figures, method breakdown or personal record disagree with the landed fish.");
        if(photo){
            if(GameFloatSkillSelfTest)Require((bool)Field(log,"steadyStrike")["learned"]&&
                await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('steady-strike-status').textContent.includes('已学会') && document.getElementById('learn-steady-strike').hidden")=="true",
                "Reopened log did not show the learned skill.");
            await WardrobeProbePhoto(pane,"angler-log.png");
        }
        await SceneProbeButton(pane,"angler-log-close");await Wait(pane,p=>!(bool)p["anglerLogOpen"]);
        Require(game.Snapshot.State==state&&GameShopJournalDigest()==digest,"Reading the log wrote a save transaction.");
        return json.Serialize(dom);
    }

    async Task<object> AnglerProbeReopen(ProbeWindow pane,FloatFish fish,string name)
    {
        string soldLog=await AnglerProbeRead(pane,fish,name,true,false);
        checks.Add("after selling the only catch the real log still lists its exact species record, experience and sale income");
        if(GameFloatSkillSelfTest){
            var previous=game.Snapshot.State;
            await SceneProbeButton(pane,"camp-go-log");
            Require(await pane.View.CoreWebView2.ExecuteScriptAsync("!document.getElementById('learn-steady-strike').disabled && document.getElementById('steady-strike-effect').textContent.includes('4.75')")=="true",
                "Qualified real float catch did not show the skill and its effect.");
            await SceneProbeButton(pane,"learn-steady-strike");
            await Wait(pane,p=>Field(p,"anglerLogState")!=null&&Field(Field(p,"anglerLogState"),"steadyStrike")!=null&&
                (bool)Field(Field(p,"anglerLogState"),"steadyStrike")["learned"]);
            Require(game.Snapshot.State.SteadyStrikeLearned&&game.Snapshot.State.Sequence==previous.Sequence+1&&
                previous.FloatXp==game.Snapshot.State.FloatXp&&previous.WalletMinor==game.Snapshot.State.WalletMinor,"Learning changed money or experience.");
            await SceneProbeButton(pane,"angler-log-close");
        }
        var original=game.Snapshot.State;string digest=GameShopJournalDigest();string oldDocument=pane.Ticket.DocumentId;
        // Close the real owner first. The open-existing path must replay the
        // same file before we attach a new WebView document and control lease.
        Require(PrepareReload(pane),"Could not retire the original log document.");
        PrepareGameShutdown();Require(gameShutdownCode=="no-active-session"&&ExitCode==0,"Unexpected recovery before reopening the log.");
        DisposeGame();gameShutdownAttempted=false;gameShutdownCode=null;
        gameLogReopening=true;
        try{InitializeGame();}finally{gameLogReopening=false;}
        GameOpen(pane);pane.View.CoreWebView2.Reload();
        var opened=await Wait(pane,p=>(string)p["documentId"]!=oldDocument&&p["lease"]==null&&Field(p,"snapshot")!=null&&
            Number(Field(p,"snapshot"),"Sequence")==original.Sequence,30000);
        Require(!(bool)Field(opened,"snapshot")["MenuOpen"],"The new owner inherited the previous menu.");
        SyncGamePolicy();await Activate(pane);
        await Wait(pane,p=>PlayerProbeDrawn(p,FloatFishingAtlas.Find("development-still-water")),20000);
        await SceneProbeButton(pane,"scene-menu");
        string reopenedLog=await AnglerProbeRead(pane,fish,name,true,true);
        var reopened=game.Snapshot.State;var domain=FloatAnglerLog.Build(reopened);
        var best=domain.Species.Single();var m=domain.Methods.Single(row=>row.Id=="float");
        Require(reopened!=original&&reopened.ProfileId==original.ProfileId&&reopened.BranchId==original.BranchId&&
            reopened.Sequence==(GameFloatSkillSelfTest?7:6)&&reopened.SteadyStrikeLearned==GameFloatSkillSelfTest&&reopened.WalletMinor==original.WalletMinor&&reopened.Creel.Count==0&&reopened.Session==null&&
            reopened.Stamina==98&&reopened.BaitCount==11&&reopened.EquippedNetId==original.EquippedNetId&&
            best.Id==fish.SpeciesKey&&best.Caught==1&&best.MaxMassMg==fish.MassMg&&m.Casts==1&&m.Caught==1&&
            m.Xp==AnglerProbeXp(fish)&&domain.TotalSold==1&&domain.SaleMinor==fish.SaleValueMinor&&
            reopenedLog==soldLog&&digest==GameShopJournalDigest(),"Reopening the saved history changed the log, catch or economy.");
        checks.Add("a new owner and a newly authorized real document reopen the same file and show the identical log with an empty creel and no extra credits");
        return new { profileReopened=true,newDocument=true,savedJournalUnchanged=true,casts=domain.TotalCasts,caught=domain.TotalCaught,
            sold=domain.TotalSold,experience=domain.TotalXp,saleMinor=domain.SaleMinor,speciesId=best.Id,maximumMassMg=best.MaxMassMg };
    }

    async Task<long> AnglerProbeLateStrike(ProbeWindow pane)
    {
        await SceneProbeButton(pane,"menu");
        await Wait(pane,p=>PlayerProbeDrawn(p,FloatFishingAtlas.Find("development-still-water")));
        var point=await SceneProbePoint(pane);
        await SceneProbeMouse(pane,point,true);await Wait(pane,p=>gameCast.Snapshot.Phase==FloatCastPhase.Charging);
        await SceneProbeMouse(pane,point,false);
        await WaterProbeLanding(pane,false);await GameWaitForBite(pane,FloatBitePhase.Strikeable);
        var runtime=gameBite;var opening=runtime.Snapshot;
        Require(opening.CloseStep-opening.OpenStep==95,"Real float bite did not use the trained window.");
        await Wait(pane,p=>runtime.Snapshot.StepIndex-opening.OpenStep>=82,12000);
        foreach(bool down in new[]{true,false}){
            await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(new {
                type=down?"mousePressed":"mouseReleased",x=Number(point,"x"),y=Number(point,"y"),button="right",buttons=down?2:0,clickCount=1}));
        }
        await Wait(pane,p=>runtime.Snapshot.Phase==FloatBitePhase.Hooked&&gameFight!=null&&gameFight.Snapshot.Phase==FloatFightPhase.Fighting);
        long lateMs=(runtime.Snapshot.StepIndex-opening.OpenStep)*FloatBiteSimulation.StepMilliseconds;
        Require(lateMs>=4100&&lateMs<4750,"Right click was outside the skill-only interval.");
        await SceneProbeButton(pane,"scene-recall");await Wait(pane,p=>game.Snapshot.State.Session==null);
        return lateMs;
    }
}
