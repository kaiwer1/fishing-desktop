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
    bool GameMultiRodSelfTest {get{return gameMode=="multi-rod-self-test";}}

    async Task MultiBuy(ProbeWindow pane,string id,long price,int count,bool stand)
    {
        var s=game.Snapshot.State;string digest=GameShopJournalDigest();
        await SceneProbeButton(pane,id);
        string confirm=id=="bottom-buy"?"bottom-confirm":"bank-confirm";
        Require(await pane.View.CoreWebView2.ExecuteScriptAsync("!document.getElementById("+json.Serialize(confirm)+").hidden")=="true"&&
            GameShopJournalDigest()==digest,"Quotation modified ownership or wallet.");
        await SceneProbeButton(pane,confirm);
        await Wait(pane,page=>{
            var actual=game.Snapshot.State;var shop=Field(page,"bankKitState");
            return actual.Sequence==s.Sequence+1&&actual.WalletMinor==s.WalletMinor-price&&actual.OwnedBottomKits.Count==count&&
                (actual.OwnedBankRodStand!=null)==stand&&shop!=null&&Number(shop,"kitCount")==count&&
                (bool)shop["standOwned"]==stand&&page["bankKitPending"]==null&&page["bottomShopPending"]==null;
        });
    }

    async Task MultiTake(ProbeWindow pane,int slot,Guid expectedRig)
    {
        // Closing menus or netting leaves focus on a button. Return keyboard
        // focus to the document before dispatching a real WebView keyboard event.
        await pane.View.CoreWebView2.ExecuteScriptAsync("if(document.activeElement?.matches('button,input,textarea,select'))document.activeElement.blur()");
        await Wait(pane,s=>Field(s,"standState")!=null&&s["standPending"].Equals(false)&&
            (string)Field(s,"standState")["controlEpoch"]!="0"&&gameBankStand.At(slot)==expectedRig);
        foreach(bool down in new[]{true,false})
            await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",json.Serialize(new{
                type=down?"rawKeyDown":"keyUp",code="Digit"+slot,key=slot.ToString(),windowsVirtualKeyCode=48+slot}));
        await Wait(pane,s=>gameBankStand.Held==expectedRig&&Field(Field(s,"standState"),"heldRig")!=null&&
            (string)Field(Field(s,"standState"),"heldRig")["rigId"]==expectedRig.ToString("D")&&s["standPending"].Equals(false));
    }

    async Task<FloatSession> MultiCast(ProbeWindow pane,Guid rig)
    {
        var before=game.Snapshot.State;var point=await BottomProbeAim(pane);
        await SceneProbeMouse(pane,point,true);
        await Wait(pane,p=>gameCast.Snapshot.Phase==FloatCastPhase.Charging&&gameCast.Snapshot.PowerPermille==1000,15000);
        await SceneProbeMouse(pane,point,false);
        await Wait(pane,p=>game.Snapshot.State.Sequence==before.Sequence+1&&game.Snapshot.State.BankSessionFor(rig)!=null);
        var s=game.Snapshot.State;var rod=s.BankSessionFor(rig);
        Require(s.BaitCount==before.BaitCount-1&&s.Stamina==before.Stamina-2&&s.Session==null&&
            rod.BankRigId==rig&&rod.BottomKit==s.BottomKitFor(rig)&&s.Receipts.Last().BankRigId==rig,"Incorrect debit or rod binding.");
        await Wait(pane,p=>{
            var b=BankBottomFor(rig);var held=Field(Field(p,"standState"),"heldRig");
            return b!=null&&b.SessionId==rod.Id&&b.SinkerOnBed&&b.BaitOnBed&&held!=null&&
                (string)held["sessionId"]==rod.Id.ToString("D")&&
                ((string)held["phase"]=="waiting"||(string)held["phase"]=="biting");
        },25000);
        return rod;
    }

    async void RunGameMultiRodProbe()
    {
        string stage="opening the private player";
        try{
            Require(GameMultiRodSelfTest&&windows.Count==1&&windows.ContainsKey(true),"Expected one private player pane.");
            var pane=windows[true];await Ready(pane);pane.ClientSize=new Size(1280,800);await Activate(pane);
            var lake=FloatFishingAtlas.Find("development-still-water");
            await Wait(pane,p=>PlayerProbeDrawn(p,lake),30000);
            var start=game.Snapshot.State;
            Require(start.Sequence==1&&start.WalletMinor==30000&&start.BaitCount==12&&start.Stamina==100&&
                start.OwnedBottomKits.Count==0&&start.OwnedBankRodStand==null&&start.OwnedNet==null&&start.Rules.Species.Count==9,
                "Wrong private starting profile.");
            var original=FloatEquipmentCodec.Encode(start.Equipment);
            checks.Add("one real landscape player starts with 300 silver, a single float rig, no bank equipment and no net");

            stage="buying all three rods, stand and net";
            await SceneProbeButton(pane,"scene-menu");
            await Wait(pane,p=>Field(p,"snapshot")!=null&&(bool)Field(p,"snapshot")["MenuOpen"]&&
                Field(p,"bottomShopState")!=null&&(bool)Field(p,"bottomShopState")["allowed"]&&Field(p,"bankKitState")!=null);
            await MultiBuy(pane,"bottom-buy",6400,1,false);await MultiBuy(pane,"bank-buy-2",4800,2,false);
            await MultiBuy(pane,"bank-buy-3",4800,3,false);await MultiBuy(pane,"bank-buy-stand",1600,3,true);
            var quoted=Field(Field(await CatchProbeShop(pane,"quote-net","shop-quoted",false),"shopState"),"quote");
            Require(Number(quoted,"totalPriceMinor")==2400,"Unexpected net price.");
            await CatchProbeShop(pane,"shop-buy","purchase-candidate",true);await CatchProbeShop(pane,"net-equip","equip-candidate",true);
            var paid=game.Snapshot.State;var rigs=paid.OwnedBottomKits.Select(k=>k.Equipment.RigId).ToArray();
            var ids=paid.OwnedBottomKits.SelectMany(k=>k.Ids).Concat(new[]{paid.OwnedBankRodStand.Id}).ToArray();
            Require(paid.Sequence==7&&paid.WalletMinor==10000&&paid.EquippedNetId==paid.OwnedNet.Id&&ids.Distinct().Count()==22,
                "Purchases did not settle exactly 200 silver.");
            checks.Add("confirmed real menu purchases of three seven-ID rigs, one stand and one net cost exactly 200 silver");

            stage="entering the three-post stand";
            string digest=GameShopJournalDigest();await SceneProbeButton(pane,"bank-select");
            await Wait(pane,p=>GameBankMode&&Field(p,"standState")!=null&&Field(p,"bankKitState")!=null&&
                (bool)Field(p,"bankKitState")["selected"]&&p["bankKitPending"]==null);
            await SceneProbeButton(pane,"menu");await Wait(pane,p=>PlayerProbeDrawn(p,lake)&&Field(p,"standState")!=null,30000);
            Require(gameBankStand.Held==Guid.Empty&&gameBankStand.At(1)==rigs[0]&&gameBankStand.At(2)==rigs[1]&&
                gameBankStand.At(3)==rigs[2]&&gameBankStand.StandId==paid.OwnedBankRodStand.Id&&GameShopJournalDigest()==digest,"Incorrect stand admission.");
            checks.Add("activating the paid stand places its three owned rigs at distinct reachable posts without spending bait or silver");

            stage="casting and parking three paid rods";
            var rods=new FloatSession[3];
            for(int i=0;i<3;i++){
                stage="selecting rod "+(i+1);await MultiTake(pane,i+1,rigs[i]);
                stage="casting and settling rod "+(i+1);rods[i]=await MultiCast(pane,rigs[i]);
            }
            var active=game.Snapshot.State;
            Require(active.BankSessions.Select(r=>r.Id).SequenceEqual(rods.Select(r=>r.Id))&&active.Session==null&&
                active.Sequence==10&&active.BaitCount==9&&active.Stamina==94&&gameFlightEmissions==3&&gameHookedEmissions==0&&
                gameBankStand.At(1)==rigs[0]&&gameBankStand.At(2)==rigs[1]&&gameBankStand.Held==rigs[2],"Three physical/session owners did not agree.");
            Require(await pane.View.CoreWebView2.ExecuteScriptAsync("!document.getElementById('scene-stand').hidden && document.querySelectorAll('#scene-stand li').length===3")=="true",
                "The scene did not display the three stand slots.");
            checks.Add("digit keys and mouse casts produce three independent paid sessions, each settling its own hook on the lake bed");
            await WardrobeProbePhoto(pane,"multi-rod-stand.png");

            stage="striking the hand-held third rod";
            var point=await BottomProbeAim(pane);var third=rods[2];
            await Wait(pane,p=>BottomProbePhase(p,"bite",third.Id)&&BankBottomFor(rigs[2]).Phase==BottomPhase.Strikeable,60000);
            var bottom=BankBottomFor(rigs[2]);var encounter=bottom.EncounterId;
            foreach(bool down in new[]{true,false})
                await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(new{
                    type=down?"mousePressed":"mouseReleased",x=Number(point,"x"),y=Number(point,"y"),button="right",buttons=down?2:0,clickCount=1}));
            await Wait(pane,p=>gameFight!=null&&gameFight.Snapshot.Phase==FloatFightPhase.Fighting&&
                Field(p,"fightState")!=null&&(string)Field(p,"fightState")["phase"]=="fighting");
            var fight=gameFight;var hooked=fight.Snapshot;
            Require(hooked.SessionId==third.Id&&hooked.EncounterId==encounter&&hooked.Target!=null&&gameHookedEmissions==1&&
                game.Snapshot.State.Creel.Count==0&&game.Snapshot.State.BankSessions.Count==3,"Wrong rod struck or premature reward.");
            checks.Add("one right click in the real third rod's bite window starts its own fight while the other two rods remain parked");

            stage="reeling and netting the hooked third fish";
            int drag=hooked.DragLevel;await CatchProbeWheel(pane,point);await Wait(pane,p=>fight.Snapshot.DragLevel==Math.Min(10,drag+1));
            await CatchProbeKeyR(pane,true);
            for(int speed=fight.Snapshot.ReelLevel+1;speed<=10;speed++){
                await CatchProbeWheel(pane,point);await Wait(pane,p=>fight.Snapshot.ReelLevel==speed);
            }
            await CatchProbeKeyR(pane,false);double beforeLine=fight.Snapshot.OutLineM;
            await SceneProbeMouse(pane,point,true);await Wait(pane,p=>fight.Snapshot.Reeling);
            await GameWaitForFight(pane,FloatFightPhase.LandingEligible,65000);
            Require(fight.Snapshot.OutLineM<beforeLine,"Held mouse did not retrieve any line.");
            await SceneProbeMouse(pane,point,false);await Wait(pane,p=>!fight.Snapshot.Reeling);
            await SceneProbeButton(pane,"scene-land");
            await Wait(pane,p=>game.Snapshot.State.Creel.Count==1&&game.Snapshot.State.BankSessions.Count==2&&gameNaturalLandings==1,10000);
            var landed=game.Snapshot.State;var fish=landed.Creel.Single();
            Require(landed.Sequence==11&&fish.SessionId==third.Id&&fish.EncounterId==encounter&&fish.SpeciesKey==hooked.Target.SpeciesId&&
                fish.MassMg==hooked.Target.MassMg&&landed.Receipts.Last().BankRigId==rigs[2]&&fish.SaleValueMinor==landed.Rules.Find(fish.SpeciesKey).Value(fish.MassMg),
                "Net credited a different fish or rod.");
            checks.Add("manual drag, retrieval and netting add exactly the model's fish to the common creel at its host-calculated price");

            stage="verifying parked tip signals and manually retrieving the other rods";
            await Wait(pane,p=>BankLastParkedBite(rigs[0])!=null&&BankLastParkedBite(rigs[1])!=null,60000);
            for(int i=0;i<2;i++){
                var alarm=BankLastParkedBite(rigs[i]);
                Require(alarm.RigId==rigs[i]&&alarm.SessionId==rods[i].Id&&alarm.Kind==BankTipKind.Bite&&alarm.Slot==i+1&&
                    alarm.Step>0&&alarm.SignalSequence>=2,"Wrong parked alarm.");
                await MultiTake(pane,i+1,rigs[i]);await SceneProbeButton(pane,"scene-recall");
                await Wait(pane,p=>game.Snapshot.State.BankSessionFor(rigs[i])==null&&game.Snapshot.State.Sequence==12+i&&
                    Field(p,"lastReply")!=null&&(string)Field(p,"lastReply")["code"]=="recall-candidate"&&Field(p,"snapshot")!=null&&
                    Number(Field(p,"snapshot"),"Sequence")==12+i&&Field(Field(p,"standState"),"heldRig")!=null&&
                    Field(Field(p,"standState"),"heldRig")["sessionId"]==null);
                Require(game.Snapshot.State.Receipts.Last().BankRigId==rigs[i]&&game.Snapshot.State.Receipts.Last().Outcome==FloatOutcome.Recalled,
                    "Recall did not settle the selected parked rod.");
            }
            checks.Add("both parked slots report their own natural bite; taking and recalling each rod ends exactly that session without extra catch");

            stage="photographing the actual creel";
            await pane.View.CoreWebView2.ExecuteScriptAsync("document.activeElement?.blur()");
            foreach(bool down in new[]{true,false})
                await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",json.Serialize(new{
                    type=down?"rawKeyDown":"keyUp",key="c",code="KeyC",windowsVirtualKeyCode=67}));
            await Wait(pane,p=>Field(p,"snapshot")!=null&&(bool)Field(p,"snapshot")["MenuOpen"]&&Number(Field(p,"snapshot"),"CreelCount")==1);
            Require(await pane.View.CoreWebView2.ExecuteScriptAsync("(()=>{const rows=document.querySelectorAll('#creel .fish');if(rows.length!==1)return false;rows[0].scrollIntoView({block:'center'});return rows[0].getBoundingClientRect().height>0})()")=="true",
                "The paid fish is absent from the player's creel.");
            await WardrobeProbePhoto(pane,"multi-rod-catch.png");
            checks.Add("the player creel visibly lists the single fish after all three rods are back");

            stage="verifying the closed private save";
            PrepareGameShutdown();Require(ExitCode==0&&gameShutdownCode=="no-active-session","Idle multi-rod save received an unexpected recovery.");
            DisposeGame();var final=FloatJournalArchive.Inspect(Path.Combine(root,"game.fltj")).State;
            Require(final.Sequence==13&&final.WalletMinor==10000&&final.Session==null&&final.BankSessions.Count==0&&final.BaitCount==9&&final.Stamina==94&&
                final.Creel.Single().Id==fish.Id&&final.EquippedNetId==paid.EquippedNetId&&final.OwnedBankRodStand.Id==paid.OwnedBankRodStand.Id&&
                final.OwnedBottomKits.SelectMany(k=>k.Ids).SequenceEqual(paid.OwnedBottomKits.SelectMany(k=>k.Ids))&&
                final.Receipts.Count(r=>r.BankRigId!=Guid.Empty&&r.Outcome==FloatOutcome.CastCommitted)==3&&
                final.Receipts.Count(r=>r.BankRigId!=Guid.Empty&&r.Outcome==FloatOutcome.Recalled)==2&&
                original.SequenceEqual(FloatEquipmentCodec.Encode(final.Equipment)),"Closed profile differs from confirmed ownership and sessions.");
            checks.Add("reopening the private file preserves three paid kits, the stand, net, catch and exact debits with no active session");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new{
                passed=true,checks=checks.ToArray(),gameLoaded=true,multiRodConnected=true,ownedBottomKits=3,standSlots=3,peakBankSessions=3,
                parkedBites=2,manualRecalls=2,naturalBottomBite=true,manualReel=true,closedJournalVerified=true,nativePhysicalMouse=false,
                devToolsMouseInput=true,controlledFishFacts=false,controlledFightTime=false,personalSaveRead=false,desktopLayer=false,
                schemaVersion=final.SchemaVersion,sequence=final.Sequence,value=final.WalletMinor,baitCount=final.BaitCount,stamina=final.Stamina,
                creelCount=final.Creel.Count,speciesId=fish.SpeciesKey,massMg=fish.MassMg,saleValueMinor=fish.SaleValueMinor,
                gameFlightEmissions,gameHookedEmissions,gameNaturalLandings,shutdown=gameShutdownCode}),Encoding.UTF8);
            Shutdown();
        }catch(Exception e){Fail(new InvalidOperationException("Three-rod player test failed while "+stage+": "+e.Message,e));}
    }
}
