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
    bool GameBottomSelfTest { get { return gameMode=="bottom-self-test"; } }

    static bool BottomProbePhase(Dictionary<string,object> page,string phase,Guid session)
    {
        var b=Field(page,"bottomState");
        return b!=null && b.Count==3 && (string)b["phase"]==phase &&
            (string)b["sessionId"]==session.ToString("D") && b.ContainsKey("tipPermille");
    }

    async Task<Dictionary<string,object>> BottomProbeAim(ProbeWindow pane)
    {
        return await SceneProbeScript(pane,@"(()=>{
            const canvas=document.getElementById('scene-canvas');
            for(const [u,v] of [[5000,9500],[4700,9250],[5400,9000],[5000,8500]]) {
                const p=dualProbe.projectSceneAim(u,v);if(!p)continue;
                const x=Math.round(p.clientX),y=Math.round(p.clientY);
                if(document.elementFromPoint(x,y)!==canvas)continue;
                const a=dualProbe.pickSceneAim(x,y);if(a)return {x,y,u:a.u,v:a.v};
            }
            return null;
        })()");
    }

    async void RunGameBottomProbe()
    {
        string stage="private player startup";
        try{
            Require(GameBottomSelfTest&&windows.Count==1&&windows.ContainsKey(true),"Bottom mode requires a private standalone player.");
            var pane=windows[true];await Ready(pane);pane.ClientSize=new Size(1280,800);await Activate(pane);
            var lake=FloatFishingAtlas.Find("development-still-water");
            var ini=game.Snapshot.State;
            await Wait(pane,s=>PlayerProbeDrawn(s,lake),30000);
            Require(ini.Sequence==1&&ini.WalletMinor==12000&&ini.OwnedBottomKit==null&&ini.OwnedNet==null&&
                ini.BaitCount==12&&ini.Stamina==100&&ini.Creel.Count==0,"Unexpected starting ownership.");
            byte[] originalFloatGear=FloatEquipmentCodec.Encode(ini.Equipment);
            checks.Add("one private landscape player starts at the lake with the float rig, twelve worms and no bottom kit or net");

            stage="buying the bottom kit";
            await SceneProbeButton(pane,"scene-menu");
            await Wait(pane,s=>Field(s,"snapshot")!=null&&(bool)Field(s,"snapshot")["MenuOpen"]&&
                Field(s,"bottomShopState")!=null&&(bool)Field(s,"bottomShopState")["allowed"]);
            string journal=GameShopJournalDigest();
            await SceneProbeButton(pane,"bottom-buy");
            Require(await pane.View.CoreWebView2.ExecuteScriptAsync("!document.getElementById('bottom-confirm').hidden && !document.getElementById('bottom-confirm').disabled")=="true"&&
                journal==GameShopJournalDigest()&&game.Snapshot.State.Sequence==1,"Viewing the price spent silver.");
            await SceneProbeButton(pane,"bottom-confirm");
            await Wait(pane,s=>game.Snapshot.State.Sequence==2&&game.Snapshot.State.OwnedBottomKit!=null&&
                Field(s,"bottomShopState")!=null&&(bool)Field(s,"bottomShopState")["owned"]&&s["bottomShopPending"]==null);
            var kit=game.Snapshot.State.OwnedBottomKit;
            Require(game.Snapshot.State.WalletMinor==5600&&kit.Ids.Distinct().Count()==7&&kit.Equipment.RemainingLineMm==50000&&
                game.Snapshot.State.Receipts.Last().Outcome==FloatOutcome.BottomKitPurchased&&
                originalFloatGear.SequenceEqual(FloatEquipmentCodec.Encode(game.Snapshot.State.Equipment)),"Incorrect kit settlement.");
            checks.Add("confirming the sixty-four silver kit records its seven unique identities and leaves the float equipment byte-identical");

            stage="buying and equipping the landing net";
            var quoted=Field(Field(await CatchProbeShop(pane,"quote-net","shop-quoted",false),"shopState"),"quote");
            Require(quoted!=null&&Number(quoted,"totalPriceMinor")==2400&&game.Snapshot.State.Sequence==2,"Net quote changed funds.");
            await CatchProbeShop(pane,"shop-buy","purchase-candidate",true);
            var net=game.Snapshot.State.OwnedNet;Require(net!=null&&game.Snapshot.State.Sequence==3&&game.Snapshot.State.EquippedNetId==Guid.Empty,"Net purchase.");
            await CatchProbeShop(pane,"net-equip","equip-candidate",true);
            Require(game.Snapshot.State.EquippedNetId==net.Id&&game.Snapshot.State.Sequence==4&&game.Snapshot.State.WalletMinor==3200,"Net debit/equipment.");
            checks.Add("a second menu purchase costs twenty-four silver and equipping the purchased net leaves thirty-two silver");

            stage="selecting the paid bottom rig";
            journal=GameShopJournalDigest();await SceneProbeButton(pane,"select-bottom");
            await Wait(pane,s=>Field(s,"bottomShopState")!=null&&(string)Field(s,"bottomShopState")["method"]=="bottom"&&
                s["bottomShopPending"]==null&&gameBottomSelected);
            Require(journal==GameShopJournalDigest()&&game.Snapshot.State.Sequence==4,"Selecting an owned method spent resources.");
            await SceneProbeButton(pane,"menu");await Wait(pane,s=>PlayerProbeDrawn(s,lake),30000);
            checks.Add("selecting bottom fishing projects the chosen method and makes no journal transaction");

            stage="aiming and casting";
            var point=await BottomProbeAim(pane);await SceneProbeMouse(pane,point,true);
            await Wait(pane,s=>gameCast.Snapshot.Phase==FloatCastPhase.Charging&&gameCast.Snapshot.PowerPermille==1000&&
                Field(Field(s,"inputState"),"aim")!=null,20000);
            await SceneProbeMouse(pane,point,false);
            await Wait(pane,s=>game.Snapshot.State.Sequence==5&&gameFlightEmissions==1&&game.Snapshot.State.Session!=null);
            var cast=game.Snapshot.State.Session;
            Require(cast.IsBottom&&cast.BottomKit.Ids.SequenceEqual(kit.Ids)&&cast.RequiredLineMm>0&&game.Snapshot.State.BaitCount==11&&
                game.Snapshot.State.Stamina==98&&game.Snapshot.State.WalletMinor==3200,"Charged a different method or cast cost.");
            checks.Add("holding and releasing a real browser mouse commits one bottom cast against the purchased kit and pays one worm and two stamina");

            stage="submerging to the surveyed bed";
            await Wait(pane,s=>gameBottom!=null&&gameFlight!=null&&gameFlight.Snapshot.SessionId==cast.Id&&
                BottomProbePhase(s,"sinking",cast.Id),15000);
            var bottom=gameBottom;var entry=bottom.Snapshot;
            Require(entry.Phase==BottomPhase.Sinking&&entry.RigId==kit.Equipment.RigId&&entry.RigRevision==kit.Equipment.RigRevision&&
                entry.SessionId==cast.Id&&entry.SinkerYmm>=entry.Contact.SinkerYmm&&entry.Contact.BaitYmm< -300&&
                entry.Contact.BaitZmm<entry.Contact.Zmm&&game.Snapshot.State.Creel.Count==0,"Bottom water contact was not surveyed.");
            await Wait(pane,s=>BottomProbePhase(s,"settled",cast.Id)&&bottom.Snapshot.SinkerOnBed&&bottom.Snapshot.BaitOnBed,20000);
            var stable=bottom.Snapshot;
            Require(stable.SinkerYmm==entry.Contact.SinkerYmm&&stable.BaitYmm==entry.Contact.BaitYmm&&
                stable.OutLineMm+stable.OnSpoolMm==50000&&stable.SignalSequence==0,"Inherited float depth, lost line or premature signal.");
            await WardrobeProbePhoto(pane,"bottom-settled.png");
            checks.Add("the sinker and trailing hook land at their own sampled bed heights before a settled tip appears");

            stage="waiting for the tip and striking";
            var bitePage=await Wait(pane,s=>BottomProbePhase(s,"bite",cast.Id)&&bottom.Snapshot.Phase==BottomPhase.Strikeable,60000);
            var window=bottom.Snapshot;Guid encounter=window.EncounterId;
            Require(encounter!=Guid.Empty&&window.SignalSequence==2&&Number(Field(bitePage,"bottomState"),"tipPermille")>0&&
                gameFight==null&&game.Snapshot.State.Sequence==5,"Tip disclosed a fish or created an automatic fight.");
            foreach(bool down in new[]{true,false})
                await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(new{
                    type=down?"mousePressed":"mouseReleased",x=Number(point,"x"),y=Number(point,"y"),
                    button="right",buttons=down?2:0,clickCount=1}));
            await Wait(pane,s=>gameFight!=null&&Field(s,"fightState")!=null&&(string)Field(s,"fightState")["phase"]=="fighting");
            var runtime=gameFight;var hooked=runtime.Snapshot;var origin=runtime.InitialFishPosition;
            Require(hooked.EncounterId==encounter&&hooked.SessionId==cast.Id&&!hooked.Reeling&&hooked.Target!=null&&
                Math.Abs(origin.X-window.Contact.Xmm/1000d)<1e-8&&Math.Abs(origin.Y-window.BaitYmm/1000d)<1e-8&&
                Math.Abs(origin.Z-window.Contact.BaitZmm/1000d)<1e-8&&gameHookedEmissions==1,"Hook handoff did not preserve the bottom hook or encounter.");
            checks.Add("a fresh right-button press during the real tip window starts one physical fight from the submerged bait position");

            stage="adjusting drag, winding to shore and netting";
            int drag=hooked.DragLevel;await CatchProbeWheel(pane,point);
            await Wait(pane,s=>runtime.Snapshot.DragLevel==Math.Min(10,drag+1));
            await CatchProbeKeyR(pane,true);
            for(int speed=runtime.Snapshot.ReelLevel+1;speed<=10;speed++){
                await CatchProbeWheel(pane,point);await Wait(pane,s=>runtime.Snapshot.ReelLevel==speed);
            }
            await CatchProbeKeyR(pane,false);double initialLine=runtime.Snapshot.OutLineM;
            await SceneProbeMouse(pane,point,true);await Wait(pane,s=>runtime.Snapshot.Reeling);
            await GameWaitForFight(pane,FloatFightPhase.LandingEligible,65000);
            var ready=runtime.Snapshot;
            Require(ready.OutLineM<initialLine&&ready.Position.Y>origin.Y&&game.Snapshot.State.Creel.Count==0&&
                Object.ReferenceEquals(ready.Target,hooked.Target),"Fish did not climb the surveyed bank under manual reeling.");
            await SceneProbeMouse(pane,point,false);await Wait(pane,s=>!runtime.Snapshot.Reeling&&runtime.Snapshot.Phase==FloatFightPhase.LandingEligible);
            await SceneProbeButton(pane,"scene-land");await GameWaitForFight(pane,FloatFightPhase.Landed,12000);
            var landed=game.Snapshot.State;
            Require(landed.Sequence==6&&landed.Creel.Count==1&&landed.Session==null&&landed.WalletMinor==3200&&
                gameNaturalLandings==1,"Landing did not commit exactly once.");
            var fish=landed.Creel[0];var price=landed.Rules.Find(fish.SpeciesKey);
            Require(price!=null&&fish.MassMg==hooked.Target.MassMg&&fish.SpeciesKey==hooked.Target.SpeciesId&&fish.SessionId==cast.Id&&
                fish.EncounterId==encounter&&fish.SaleValueMinor==price.Value(fish.MassMg),"Saved fish differs from physical target.");
            await WardrobeProbePhoto(pane,"bottom-catch.png");
            checks.Add("wheel and R-wheel set real reel controls and holding the mouse brings the same fish up the slope into the equipped net");

            stage="selling the owned catch";
            await SceneProbeButton(pane,"scene-menu");
            await Wait(pane,s=>Field(s,"snapshot")!=null&&(bool)Field(s,"snapshot")["MenuOpen"]&&Number(Field(s,"snapshot"),"CreelCount")==1);
            await SceneProbeButton(pane,"camp-go-creel");
            var row=await SceneProbeScript(pane,@"(()=>{
                const rows=document.querySelectorAll('#creel .fish');if(rows.length!==1)return null;
                const checkbox=rows[0].querySelector('input[type=checkbox]');checkbox.scrollIntoView({block:'center'});
                const r=checkbox.getBoundingClientRect(),x=Math.round(r.left+r.width/2),y=Math.round(r.top+r.height/2);
                return !checkbox.disabled && document.elementFromPoint(x,y)===checkbox ? {x,y} : null;
            })()");
            await SceneProbeMouse(pane,row,true);await SceneProbeMouse(pane,row,false);await SceneProbeButton(pane,"sell");
            await Wait(pane,s=>game.Snapshot.State.Sequence==7&&Field(s,"snapshot")!=null&&Number(Field(s,"snapshot"),"CreelCount")==0);
            var sale=game.Snapshot.State.Receipts.Last();long expectedWallet=3200+fish.SaleValueMinor;
            Require(sale.Outcome==FloatOutcome.Sold&&sale.FishIds.Count==1&&sale.FishIds[0]==fish.Id&&
                sale.WalletBefore==3200&&sale.WalletAfter==expectedWallet&&game.Snapshot.State.WalletMinor==expectedWallet,"Sale balance mismatch.");
            checks.Add("selecting the sole owned fish and selling it credits precisely its saved price and clears the creel");

            stage="reopening the private journal";
            PrepareGameShutdown();Require(ExitCode==0&&gameShutdownCode=="no-active-session","Unexpected recovery after sale.");
            DisposeGame();var final=FloatJournalArchive.Inspect(Path.Combine(root,"game.fltj")).State;
            Require(final.Sequence==7&&final.OwnedBottomKit!=null&&final.OwnedBottomKit.Ids.SequenceEqual(kit.Ids)&&
                FloatEquipmentCodec.Encode(final.OwnedBottomKit.Equipment).SequenceEqual(FloatEquipmentCodec.Encode(kit.Equipment))&&
                FloatEquipmentCodec.Encode(final.Equipment).SequenceEqual(originalFloatGear)&&final.EquippedNetId==net.Id&&
                final.Receipts.Count(r=>r.Outcome==FloatOutcome.BottomKitPurchased)==1&&final.WalletMinor==expectedWallet&&
                final.Creel.Count==0&&final.Session==null&&final.BaitCount==11&&final.Stamina==98,"Private journal failed exact replay.");
            checks.Add("closing and reopening the private file replays the kit identities, equipped net, balance and original untouched float equipment");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new{
                passed=true,checks=checks.ToArray(),gameLoaded=true,bottomFishingConnected=true,realMouseCast=true,
                naturalBottomBite=true,realMouseStrike=true,manualReel=true,netPurchased=true,saleVerified=true,
                originalFloatGearUnchanged=true,closedJournalVerified=true,personalSaveRead=false,desktopLayer=false,nativePhysicalMouse=false,
                controlledFishFacts=false,controlledFightTime=false,schemaVersion=final.SchemaVersion,sequence=final.Sequence,value=final.WalletMinor,
                kitPriceMinor=6400,netPriceMinor=2400,ownedBottomIds=kit.Ids.Count,remainingBottomLineMm=kit.Equipment.RemainingLineMm,
                equipmentInstances=final.Equipment.Instances.Count,remainingLineMm=final.Equipment.RemainingLineMm,
                sinkerYmm=window.SinkerYmm,baitYmm=window.BaitYmm,fishStartZm=origin.Z,landingYmetres=ready.Position.Y,
                speciesId=fish.SpeciesKey,massMg=fish.MassMg,saleValueMinor=fish.SaleValueMinor,baitCount=final.BaitCount,stamina=final.Stamina,
                creelCount=final.Creel.Count,gameFlightEmissions,gameHookedEmissions,gameNaturalLandings,shutdown=gameShutdownCode}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){Fail(new InvalidOperationException("Bottom probe failed while "+stage+": "+error.Message,error));}
    }
}
