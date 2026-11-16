using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    bool GameLureSelfTest {get{return gameMode=="lure-self-test";}}

    async Task<Dictionary<string,object>> LureProbePoint(ProbeWindow pane,bool near)
    {
        string aims=near?"[[5000,9000],[5000,8500],[5400,9000]]":"[[6000,3000],[5500,3500],[5000,5500],[5000,6500]]";
        return await SceneProbeScript(pane,@"(()=>{
            const canvas=document.getElementById('scene-canvas');
            for(const [u,v] of "+aims+@"){
                const p=dualProbe.projectSceneAim(u,v);if(!p)continue;
                const x=Math.round(p.clientX),y=Math.round(p.clientY),a=dualProbe.pickSceneAim(x,y);
                if(document.elementFromPoint(x,y)===canvas && a?.geometryVersion==="+gameWater.Version+@")return {x,y,u:a.u,v:a.v};
            }return null;
        })()");
    }

    async Task<LureRetrieveSimulation> LureProbeCast(ProbeWindow pane,Dictionary<string,object> point)
    {
        var old=game.Snapshot.State;
        await SceneProbeMouse(pane,point,true);
        await Wait(pane,p=>gameCast.Snapshot.Phase==FloatCastPhase.Charging&&gameCast.Snapshot.PowerPermille==1000,15000);
        await SceneProbeMouse(pane,point,false);
        await Wait(pane,p=>gameLure!=null&&game.Snapshot.State.Sequence==old.Sequence+1&&game.Snapshot.State.Session!=null&&
            Field(p,"lureState")!=null&&(string)Field(p,"lureState")["sessionId"]==game.Snapshot.State.Session.Id.ToString("D"),20000);
        var state=game.Snapshot.State;var paid=state.Session;var lure=gameLure;var live=lure.Snapshot;var k=state.OwnedLureKit;
        Require(paid.IsLure&&!paid.IsBottom&&paid.LureKit==k&&live.Rig.LureId==k.LureId&&live.Rig.RigId==k.RigId&&
            live.SessionId==paid.Id&&paid.RequiredLineMm==gameLurePlan.RequiredLineMm&&gameFlight.Snapshot.Phase==FloatFlightPhase.Waiting&&
            gameLurePlan.Geometry.WaterId==gameFishingWater.Id&&state.BaitCount==0&&state.Stamina==old.Stamina-2&&state.WalletMinor==old.WalletMinor,
            "The fishing water, flight or paid reusable lure binding is inconsistent.");
        return lure;
    }

    async void RunGameLureProbe()
    {
        string stage="opening private player";
        try{
            Require(GameLureSelfTest&&windows.Count==1&&windows.ContainsKey(true),"Lure probe requires a standalone private player.");
            var pane=windows[true];await Ready(pane);pane.ClientSize=new Size(1280,800);await Activate(pane);
            var lake=FloatFishingAtlas.Find("development-still-water");
            var water=FloatFishingAtlas.Find(catalogSpecies=="topmouth-culter"?"river-bend":"coastal-bay");
            await Wait(pane,p=>PlayerProbeDrawn(p,lake),30000);
            var initial=game.Snapshot.State;byte[] floatGear=FloatEquipmentCodec.Encode(initial.Equipment);
            Require(initial.Sequence==1&&initial.Stamina==100&&initial.BaitCount==0&&initial.WalletMinor==12000&&
                initial.OwnedLureKit==null&&initial.OwnedNet==null&&initial.Creel.Count==0&&initial.Rules.Species.Count==11,"Wrong private genesis.");
            checks.Add("one landscape player opens a private eleven-species profile with 120 silver, zero worms and no lure equipment");

            stage="buying the reusable spoon kit and net";
            await SceneProbeButton(pane,"scene-menu");await Wait(pane,p=>Field(p,"lureShopState")!=null&&(bool)Field(p,"snapshot")["MenuOpen"]);
            var digest=GameShopJournalDigest();await SceneProbeButton(pane,"lure-buy");
            Require(await pane.View.CoreWebView2.ExecuteScriptAsync("!document.getElementById('lure-confirm').disabled && !document.getElementById('lure-confirm').hidden")=="true"&&
                digest==GameShopJournalDigest(),"Opening lure quotation charged silver.");
            await SceneProbeButton(pane,"lure-confirm");
            await Wait(pane,p=>game.Snapshot.State.Sequence==2&&game.Snapshot.State.OwnedLureKit!=null&&p["lureShopPending"]==null&&
                Field(p,"lureShopState")!=null&&(bool)Field(p,"lureShopState")["owned"]&&!(bool)Field(p,"lureShopState")["allowed"]);
            var kit=game.Snapshot.State.OwnedLureKit;
            Require(kit.Ids.Distinct().Count()==6&&game.Snapshot.State.WalletMinor==6400&&
                await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('lure-select').disabled")=="true",
                "Lure ownership, price or starting lake method restriction changed.");
            var quote=Field(Field(await CatchProbeShop(pane,"quote-net","shop-quoted",false),"shopState"),"quote");
            Require(Number(quote,"totalPriceMinor")==2400,"Wrong net price.");
            await CatchProbeShop(pane,"shop-buy","purchase-candidate",true);await CatchProbeShop(pane,"net-equip","equip-candidate",true);
            var net=game.Snapshot.State.EquippedNetId;
            Require(net!=Guid.Empty&&game.Snapshot.State.Sequence==4&&game.Snapshot.State.WalletMinor==4000,"Net settlement.");
            checks.Add("menu confirmations buy a 56-silver kit and equip a 24-silver net; the starting lake disables lure selection");

            stage="travelling to "+water.Id;
            await SceneProbeButton(pane,"spot-select-"+water.Id);
            await Wait(pane,p=>AtlasProbeProjected(p,water.Id)&&Field(p,"lureShopState")!=null&&(bool)Field(p,"lureShopState")["allowed"]);
            await SceneProbeButton(pane,"lure-select");
            await Wait(pane,p=>Field(p,"lureShopState")!=null&&(bool)Field(p,"lureShopState")["selected"]&&p["lureShopPending"]==null&&gameLureSelected);
            await SceneProbeButton(pane,"menu");var selectedView=await Wait(pane,p=>PlayerProbeDrawn(p,water)&&
                (string)SceneProbeRenderer(p)["rodType"]=="spinning"&&(string)SceneProbeRenderer(p)["rodPhase"]=="idle",30000);
            Require(gameWater.SpotId==water.Geometry.SpotId&&game.Snapshot.State.Sequence==4,"Selecting lure changed wallet or location.");
            checks.Add("the menu enables the owned spoon at "+water.Id+" and returns to the fishing view");

            pane.ClientSize=new Size(1680,749);
            await Wait(pane,p=>PlayerProbeDrawn(p,water)&&Number(SceneProbeRenderer(p),"width")>2.2*Number(SceneProbeRenderer(p),"height"),15000);
            var canvas=await SceneProbeScript(pane,@"(()=>{const r=document.getElementById('scene-canvas').getBoundingClientRect();return {width:r.width,height:r.height};})()");
            Require(Number(canvas,"width")>2.2*Number(canvas,"height"),"The fishing canvas did not resize to ultrawide.");

            stage="retrieving the first cast to shore";
            var shortPoint=await LureProbePoint(pane,true);var first=await LureProbeCast(pane,shortPoint);
            var shortStart=first.Snapshot;
            await CatchProbeKeyR(pane,true);
            for(int level=shortStart.SpeedLevel+1;level<=4;level++){
                await CatchProbeWheel(pane,shortPoint);await Wait(pane,p=>first.Snapshot.SpeedLevel==level&&
                    Number(Field(p,"lureState"),"speedLevel")==level&&Field(p,"lureInputState")!=null&&!(bool)Field(p,"lureInputState")["speedPending"]);
            }
            await CatchProbeKeyR(pane,false);await SceneProbeMouse(pane,shortPoint,true);
            var reelingView=await Wait(pane,p=>first.Snapshot.Reeling&&(bool)Field(p,"lureState")["reeling"]&&first.Snapshot.OutLineM<shortStart.OutLineM-.5&&
                (string)SceneProbeRenderer(p)["rodType"]=="spinning"&&(string)SceneProbeRenderer(p)["rodPhase"]=="lure-retrieving"&&
                (bool)SceneProbeRenderer(p)["lureVisible"]&&!(bool)SceneProbeRenderer(p)["bobberVisible"],15000);
            await WardrobeProbePhoto(pane,"lure-retrieve.png");
            await Wait(pane,p=>game.Snapshot.State.Sequence==6&&game.Snapshot.State.Session==null&&first.Snapshot.Phase==LureRetrievePhase.Ended,40000);
            await SceneProbeMouse(pane,shortPoint,false);
            var firstReceipt=game.Snapshot.State.Receipts.Last();
            Require(firstReceipt.Outcome==FloatOutcome.Recalled&&firstReceipt.SessionId==shortStart.SessionId&&firstReceipt.EventId==first.Snapshot.RetrieveEventId&&
                game.Snapshot.State.OwnedLureKit==kit&&game.Snapshot.State.Stamina==98&&game.Snapshot.State.BaitCount==0&&game.Snapshot.State.Creel.Count==0,
                "Shore pickup was not confirmed for the same spoon.");
            checks.Add("a real zero-bait cast and sustained high-speed retrieval recover that same spoon at shore with one recall and no fish");

            stage="casting the same spoon and striking a natural attack";
            await Wait(pane,p=>Field(p,"snapshot")!=null&&Number(Field(p,"snapshot"),"Sequence")==6&&Field(p,"snapshot")["SessionId"]==null);
            var point=await LureProbePoint(pane,false);var second=await LureProbeCast(pane,point);
            var pool=water.CreatePool(gameWaterLanding.Sample,game.Snapshot.State.Rules,FishingPresentation.Spoon);
            Require(second.Snapshot.SpeedLevel==2&&second.Snapshot.SessionId!=shortStart.SessionId&&second.Snapshot.Rig.LureId==shortStart.Rig.LureId,"Second cast changed the spoon.");
            await SceneProbeMouse(pane,point,true);
            stage="winding at the normal speed until the real attack window opens";
            await Wait(pane,p=>second.Snapshot.Phase==LureRetrievePhase.Strikeable&&Field(p,"lureState")!=null&&
                (string)Field(p,"lureState")["phase"]=="strikeable"&&(bool)Field(p,"lureState")["reeling"],45000);
            var encounter=second.Snapshot.EncounterId;
            stage="transferring the fresh right-button chord to the hooked fish";
            long beforeStrike=gameViews[pane].LureHighWater;
            foreach(bool down in new[]{true,false})
                await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(new{
                    type=down?"mousePressed":"mouseReleased",x=Number(point,"x"),y=Number(point,"y"),button="right",buttons=down?3:1,clickCount=1}));
            InvalidOperationException missingChord=null;
            try{
                await Wait(pane,p=>gameFight!=null&&gameFight.Snapshot.EncounterId==encounter&&
                    Field(p,"fightState")!=null&&(string)Field(p,"fightState")["phase"]=="fighting");
            }catch(InvalidOperationException e){missingChord=e;}
            if(missingChord!=null){
                var page=await pane.View.CoreWebView2.ExecuteScriptAsync("(()=>{const p=dualProbe.state();return {reply:p.lastReply,lure:p.lureState,fight:p.fightState?.phase};})()");
                throw new InvalidOperationException("Chord did not start this encounter. Input sequence "+
                    beforeStrike+" -> "+gameViews[pane].LureHighWater+", host lure phase "+second.Snapshot.Phase+
                    ", hooked emissions "+gameHookedEmissions+", page "+page,missingChord);
            }
            var locked=second.Snapshot;var fight=gameFight;var hooked=fight.Snapshot;var origin=fight.InitialFishPosition;
            Require(locked.Phase==LureRetrievePhase.Hooked&&!locked.Reeling&&!hooked.Reeling&&gameHookedEmissions==1&&
                hooked.SessionId==locked.SessionId&&Object.ReferenceEquals(locked.Target,hooked.Target)&&
                Math.Abs(origin.X-locked.Position.X)<1e-6&&Math.Abs(origin.Y-locked.Position.Y)<1e-6&&Math.Abs(origin.Z-locked.Position.Z)<1e-6,
                "Attack handoff lost the fish, lure position or fresh input requirement.");
            await SceneProbeMouse(pane,point,false);
            checks.Add("normal winding draws the local predator to the spoon and a fresh right press transfers its position and target into the fight");

            stage="manually landing the spoon catch";
            int drag=hooked.DragLevel;await CatchProbeWheel(pane,point);
            await Wait(pane,p=>fight.Snapshot.DragLevel==Math.Min(10,drag+1));
            await CatchProbeKeyR(pane,true);
            for(int speed=hooked.ReelLevel+1;speed<=10;speed++){
                await CatchProbeWheel(pane,point);await Wait(pane,p=>fight.Snapshot.ReelLevel==speed);
            }
            await CatchProbeKeyR(pane,false);await SceneProbeMouse(pane,point,true);await Wait(pane,p=>fight.Snapshot.Reeling);
            await GameWaitForFight(pane,FloatFightPhase.LandingEligible,100000);
            Require(fight.Snapshot.OutLineM<hooked.OutLineM,"No line recovered during fight.");
            await SceneProbeMouse(pane,point,false);await Wait(pane,p=>!fight.Snapshot.Reeling);await SceneProbeButton(pane,"scene-land");
            await Wait(pane,p=>game.Snapshot.State.Sequence==8&&game.Snapshot.State.Creel.Count==1&&Field(p,"snapshot")!=null&&Number(Field(p,"snapshot"),"Sequence")==8);
            var solved=game.Snapshot.State;var fish=solved.Creel.Single();
            string name=water.SpeciesFor(solved.Rules).Single(s=>s.Id==fish.SpeciesKey).Name;
            Require(solved.Session==null&&gameNaturalLandings==1&&fish.SpeciesKey==(catalogSpecies??"spotted-seabass")&&fish.SessionId==locked.SessionId&&
                pool!=null&&pool.Environment.Candidates.Any(s=>s.SpeciesId==fish.SpeciesKey)&&
                fish.EncounterId==encounter&&fish.MassMg==locked.Target.MassMg&&fish.SaleValueMinor==solved.Rules.Find(fish.SpeciesKey).Value(fish.MassMg),
                "Saved fish differs from the actual encounter.");
            checks.Add("a separate mouse hold, drag adjustment and net action land "+fish.SpeciesKey+" at its saved weight and price");

            stage="viewing and selling the spoon catch";
            await pane.View.CoreWebView2.ExecuteScriptAsync("document.activeElement?.blur()");
            foreach(bool down in new[]{true,false})
                await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",json.Serialize(new{
                    type=down?"rawKeyDown":"keyUp",key="c",code="KeyC",windowsVirtualKeyCode=67}));
            await Wait(pane,p=>Field(p,"snapshot")!=null&&(bool)Field(p,"snapshot")["MenuOpen"]&&Number(Field(p,"snapshot"),"CreelCount")==1);
            object specimen=catalogSpecies!=null?await SpecimenProbeInspect(pane,fish,name):null;
            var row=await SceneProbeScript(pane,@"(()=>{
                const fish=document.querySelectorAll('#creel .fish');if(fish.length!==1)return null;
                const box=fish[0].querySelector('input[type=checkbox]');box.scrollIntoView({block:'center'});
                const b=box.getBoundingClientRect(),x=Math.round(b.left+b.width/2),y=Math.round(b.top+b.height/2);
                return !box.disabled && document.elementFromPoint(x,y)===box ? {x,y} : null;
            })()");
            await WardrobeProbePhoto(pane,"lure-catch.png");
            await SceneProbeMouse(pane,row,true);await SceneProbeMouse(pane,row,false);await SceneProbeButton(pane,"sell");
            await Wait(pane,p=>game.Snapshot.State.Sequence==9&&Field(p,"snapshot")!=null&&Number(Field(p,"snapshot"),"CreelCount")==0);
            long expectedWallet=4000+fish.SaleValueMinor;
            Require(game.Snapshot.State.WalletMinor==expectedWallet&&game.Snapshot.State.Receipts.Last().FishIds.Single()==fish.Id,
                "Selling the checked fish changed unexpected resources.");
            checks.Add("the real creel shows "+fish.SpeciesKey+" and a checked sale credits precisely its persisted price");

            stage="reopening the privately closed journal";
            object anglerLog=catalogSpecies!=null?await AnglerProbeReopen(pane,fish,name):null;
            PrepareGameShutdown();Require(gameShutdownCode=="no-active-session"&&ExitCode==0,"Unexpected startup or shutdown recovery.");
            DisposeGame();var final=FloatJournalArchive.Inspect(Path.Combine(root,"game.fltj")).State;
            Require(final.Sequence==9&&final.WalletMinor==expectedWallet&&final.OwnedLureKit.Ids.SequenceEqual(kit.Ids)&&final.EquippedNetId==net&&
                final.Session==null&&final.BankSessions.Count==0&&final.BaitCount==0&&final.Stamina==96&&final.Creel.Count==0&&
                final.Receipts.Count(r=>r.LureRigId==kit.RigId)==2&&gameFlightEmissions==2&&floatGear.SequenceEqual(FloatEquipmentCodec.Encode(final.Equipment)),
                "Closed journal did not replay the two paid casts and sale.");
            checks.Add("private journal replay preserves the spoon and hook, both paid casts, the empty worm stack and the original float gear");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new{
                passed=true,checks=checks.ToArray(),gameLoaded=true,lureFishingConnected=true,closedJournalVerified=true,realMouseCast=true,
                equippedRod=SceneProbeRenderer(selectedView)["rodType"],retrievingRod=SceneProbeRenderer(reelingView)["rodType"],
                naturalLureAttack=true,manualReel=true,shoreRetrieveVerified=true,saleVerified=true,originalFloatGearUnchanged=true,
                waterId=water.Id,habitat=pool.Id,catalogSpecies,catchSeed,specimen,anglerLog,specimenInspectionVerified=specimen!=null,
                controlledFishFacts=false,controlledFightTime=false,privateSeed=1,nativePhysicalMouse=false,personalSaveRead=false,desktopLayer=false,
                schemaVersion=final.SchemaVersion,sequence=final.Sequence,value=final.WalletMinor,baitCount=final.BaitCount,stamina=final.Stamina,
                lureKitPriceMinor=5600,lureIds=kit.Ids.Count,lineMm=kit.LineLengthMm,creelCount=final.Creel.Count,speciesId=fish.SpeciesKey,
                massMg=fish.MassMg,saleValueMinor=fish.SaleValueMinor,gameFlightEmissions,gameHookedEmissions,gameNaturalLandings,shutdown=gameShutdownCode}),Encoding.UTF8);
            Shutdown();
        }catch(Exception e){Fail(new InvalidOperationException("Lure probe failed while "+stage+": "+e.Message,e));}
    }
}
