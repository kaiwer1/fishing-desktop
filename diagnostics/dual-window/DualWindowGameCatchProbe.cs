using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    async Task<Dictionary<string,object>> CatchProbeShop(ProbeWindow pane,string button,string expected,bool committed)
    {
        var before=Field(await GameShopReady(pane),"lastReply");
        string previous=before==null?null:(string)before["transportId"];
        await SceneProbeButton(pane,button);
        var after=await Wait(pane,s=>Field(s,"lastReply")!=null && (string)Field(s,"lastReply")["transportId"]!=previous);
        GameCode(after,expected,committed);return await GameShopReady(pane);
    }

    async Task CatchProbeWheel(ProbeWindow pane,Dictionary<string,object> point)
    {
        await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(new {
            type="mouseWheel",x=Number(point,"x"),y=Number(point,"y"),deltaX=0,deltaY=-90,buttons=0}));
    }

    async Task CatchProbeKeyR(ProbeWindow pane,bool down)
    {
        await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",json.Serialize(new {
            type=down?"rawKeyDown":"keyUp",key="r",code="KeyR",windowsVirtualKeyCode=82}));
    }

    async void RunGameCatchProbe()
    {
        try{
            Require(GameCatchSelfTest && windows.Count==1 && windows.ContainsKey(true),"Catch test requires the private standalone player.");
            var pane=windows[true];await Ready(pane);pane.ClientSize=new Size(1280,800);
            await Activate(pane);
            var ini=game.Snapshot.State;var lake=FloatFishingAtlas.Find("development-still-water");var station=FloatFishingAtlas.Find("coastal-bay");
            await Wait(pane,s=>PlayerProbeDrawn(s,lake),30000);
            Require(ini.Sequence==1 && ini.Rules.Species.Count==7 && ini.OwnedNet==null && ini.WalletMinor==12000 &&
                ini.BaitCount==12 && ini.Stamina==100 && ini.Creel.Count==0,"Unexpected standalone catch profile.");
            checks.Add("the private landscape player starts with seven eligible species, an empty creel and no net");

            await SceneProbeButton(pane,"scene-menu");
            await Wait(pane,s=>Field(s,"snapshot")!=null && (bool)Field(s,"snapshot")["MenuOpen"]);
            var quote=Field(Field(await CatchProbeShop(pane,"quote-net","shop-quoted",false),"shopState"),"quote");
            Require(quote!=null && (string)quote["product"]=="net" && Number(quote,"totalPriceMinor")==2400 && game.Snapshot.State.Sequence==1,
                "Net price or initial quote settlement changed.");
            await CatchProbeShop(pane,"shop-buy","purchase-candidate",true);
            var bought=game.Snapshot.State;
            Require(bought.OwnedNet!=null && bought.EquippedNetId==Guid.Empty && bought.Sequence==2 && bought.WalletMinor==9600,"Buy did not debit exactly one net.");
            await CatchProbeShop(pane,"net-equip","equip-candidate",true);
            Require(game.Snapshot.State.EquippedNetId==bought.OwnedNet.Id && game.Snapshot.State.Sequence==3,"New net was not equipped.");
            checks.Add("three hit-tested mouse clicks quote, buy and equip one landing net for 24 silver");

            await SceneProbeButton(pane,"spot-select-"+station.Id);
            await Wait(pane,s=>AtlasProbeProjected(s,station.Id));
            await SceneProbeButton(pane,"menu");
            await Wait(pane,s=>PlayerProbeDrawn(s,station),30000);
            var point=await SceneProbePoint(pane);
            await SceneProbeMouse(pane,point,true);
            await Wait(pane,s=>gameCast.Snapshot.Phase==FloatCastPhase.Charging && Field(Field(s,"inputState"),"aim")!=null);
            await SceneProbeMouse(pane,point,false);
            await Wait(pane,s=>game.Snapshot.State.Sequence==4 && gameFlightEmissions==1);
            var flight=await WaterProbeLanding(pane,false);
            Require(gameFishingWater==station && gameWaterLanding.Sample.WaterId==station.Id && game.Snapshot.State.BaitCount==11 &&
                game.Snapshot.State.Stamina==98 && game.Snapshot.State.WalletMinor==9600,"Coastal cast changed unexpected resources.");
            checks.Add("a mouse cast from the coast scene reaches real water and charges a single bait and two stamina");

            await GameWaitForBite(pane,FloatBitePhase.Strikeable);
            var bite=gameBite.Snapshot;var pool=gameFishingPool;
            Require(pool!=null && bite.Target!=null && pool.Environment.ContainsTarget(bite.Target.SpeciesId),"Biting fish is outside the real coastal pool.");
            foreach(bool down in new[]{true,false}){
                await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(new {
                    type=down?"mousePressed":"mouseReleased",x=Number(point,"x"),y=Number(point,"y"),button="right",buttons=down?2:0,clickCount=1}));
            }
            await Wait(pane,s=>gameFight!=null && gameFight.Snapshot.Phase==FloatFightPhase.Fighting &&
                Field(s,"fightState")!=null && (string)Field(s,"fightState")["phase"]=="fighting");
            var runtime=gameFight;var hooked=runtime.Snapshot;
            Require(hooked.SessionId==flight.SessionId && hooked.EncounterId==bite.EncounterId && Object.ReferenceEquals(hooked.Target,bite.Target) &&
                !hooked.Reeling && gameHookedEmissions==1,"Right-click failed to hook the current encounter.");
            checks.Add("an actual right mouse press during the bite window hooks exactly the fish selected by the host");

            int drag=hooked.DragLevel;
            await CatchProbeWheel(pane,point);
            await Wait(pane,s=>runtime.Snapshot.DragLevel==drag+1);
            await CatchProbeKeyR(pane,true);
            int initialSpeed=runtime.Snapshot.ReelLevel;
            for(int speed=initialSpeed+1;speed<=10;speed++){
                await CatchProbeWheel(pane,point);
                await Wait(pane,s=>runtime.Snapshot.ReelLevel==speed);
            }
            await CatchProbeKeyR(pane,false);
            double initialLine=runtime.Snapshot.OutLineM;
            await SceneProbeMouse(pane,point,true);
            await Wait(pane,s=>runtime.Snapshot.Reeling && Field(s,"fightState")!=null && (bool)Field(s,"fightState")["reeling"]);
            await GameWaitForFight(pane,FloatFightPhase.LandingEligible,60000);
            var netReady=runtime.Snapshot;
            Require(netReady.OutLineM<initialLine && game.Snapshot.State.Creel.Count==0 && game.Snapshot.State.Sequence==4 &&
                netReady.DragLevel==drag+1 && netReady.ReelLevel==10,"Fish became owned before landing or winding did not use physical line.");
            await SceneProbeMouse(pane,point,false);
            await Wait(pane,s=>runtime.Snapshot.Phase==FloatFightPhase.LandingEligible && !runtime.Snapshot.Reeling);
            checks.Add("wheel drag adjustment and R plus wheel speed adjustment affect the live fight and a held mouse button reels the fish into net range");

            await SceneProbeButton(pane,"scene-land");
            await GameWaitForFight(pane,FloatFightPhase.Landed,10000);
            var landed=game.Snapshot.State;
            Require(landed.Sequence==5 && landed.Session==null && landed.WalletMinor==9600 && landed.Creel.Count==1 && gameNaturalLandings==1,
                "Net landing did not create exactly one owned fish before sale.");
            var fish=landed.Creel[0];var species=station.SpeciesFor(landed.Rules);
            string name=null;foreach(var item in species)if(item.Id==fish.SpeciesKey)name=item.Name;
            var rule=landed.Rules.Find(fish.SpeciesKey);
            Require(name!=null && rule!=null && fish.SessionId==flight.SessionId && fish.EncounterId==bite.EncounterId &&
                fish.SpeciesKey==bite.Target.SpeciesId && fish.MassMg==bite.Target.MassMg && fish.SaleValueMinor==rule.Value(fish.MassMg),
                "Creel fish identity, weight or saved sale rule differs from the original encounter.");
            var card=await SceneProbeScript(pane,@"(()=>{
                const c=document.getElementById('catch-card'), plate=c.querySelector('svg[data-fish]');
                return {visible:!c.hidden, name:document.getElementById('catch-name').textContent,
                    mass:document.getElementById('catch-mass').textContent,key:plate?.dataset.fish};
            })()");
            string kg=(fish.MassMg/1000000m).ToString("0.######",CultureInfo.InvariantCulture);
            Require((bool)card["visible"] && (string)card["name"]==name && (string)card["key"]==fish.SpeciesKey &&
                (string)card["mass"]==kg+" kg","The catch card did not show the saved fish illustration, name and exact weight.");
            checks.Add("the visible net button lands the same locked fish and its illustrated catch card matches the confirmed species and mass");

            await SceneProbeButton(pane,"scene-menu");
            await Wait(pane,s=>Field(s,"snapshot")!=null && (bool)Field(s,"snapshot")["MenuOpen"] && Number(Field(s,"snapshot"),"CreelCount")==1);
            var row=await SceneProbeScript(pane,@"(()=>{
                const entries=document.querySelectorAll('#creel .fish');if(entries.length!==1)return null;
                const entry=entries[0], input=entry.querySelector('input[type=checkbox]');
                input.scrollIntoView({block:'center'});const r=input.getBoundingClientRect();
                const x=Math.round(r.left+r.width/2),y=Math.round(r.top+r.height/2);
                if(input.disabled || document.elementFromPoint(x,y)!==input)return null;
                return {x,y,label:entry.querySelector('label').textContent,key:entry.querySelector('svg[data-fish]')?.dataset.fish};
            })()");
            string rowText=name+" · "+(fish.MassMg/1000000m).ToString("F6",CultureInfo.InvariantCulture)+" kg · "+
                (fish.SaleValueMinor/100m).ToString("F2",CultureInfo.InvariantCulture)+" 银币";
            Require((string)row["key"]==fish.SpeciesKey && (string)row["label"]==rowText,"Menu row disagrees with the durable creel.");
            await SceneProbeMouse(pane,row,true);await SceneProbeMouse(pane,row,false);
            Require(await pane.View.CoreWebView2.ExecuteScriptAsync("document.querySelector('#creel input').checked && !document.getElementById('sell').disabled")=="true",
                "Selecting the owned fish did not enable its sale.");
            using(var file=new FileStream(Path.Combine(root,"game-catch.png"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
                await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
            checks.Add("the pre-sale screenshot shows the selected illustrated fish, its actual measured mass and the host sale price in the creel");

            await SceneProbeButton(pane,"sell");
            await Wait(pane,s=>Field(s,"snapshot")!=null && Number(Field(s,"snapshot"),"CreelCount")==0 && game.Snapshot.State.Sequence==6);
            var final=game.Snapshot.State;var sale=final.Receipts[final.Receipts.Count-1];
            Require(sale.Outcome==FloatOutcome.Sold && sale.FishIds.Count==1 && sale.FishIds[0]==fish.Id &&
                sale.WalletBefore==9600 && final.WalletMinor==sale.WalletBefore+fish.SaleValueMinor && sale.WalletAfter==final.WalletMinor &&
                final.Creel.Count==0 && final.Session==null && final.BaitCount==11 && final.Stamina==98 &&
                final.EquippedNetId==bought.OwnedNet.Id && final.Equipment.RemainingLineMm==50000 && final.Equipment.LostLineMm==0,
                "Sell did not settle the displayed fish exactly once.");
            Require(await pane.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('sell').disabled && document.querySelectorAll('#creel .fish').length===0")=="true",
                "Sale left another sellable copy on the page.");
            PrepareGameShutdown();Require(gameShutdownCode=="no-active-session" && ExitCode==0,"The completed sale left an unpaid session.");
            checks.Add("one real sale click credits the saved price exactly once and leaves an empty creel, the equipped net and no active session");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new {
                passed=true,checks=checks.ToArray(),gameLoaded=true,catchSaleConnected=true,actual3dCast=true,naturalBite=true,
                realMouseStrike=true,manualReel=true,netPurchased=true,ownedNetEquipped=true,catchCardVerified=true,creelPreviewVerified=true,
                saleVerified=true,devToolsMouseInput=true,devToolsKeyboardInput=true,nativePhysicalMouse=false,rawGameCommandInjected=false,
                privateModelSeeds=true,controlledFishFacts=false,controlledFightTime=false,personalSaveRead=false,desktopLayer=false,observerOpened=false,
                schemaVersion=final.SchemaVersion,sequence=final.Sequence,value=final.WalletMinor,netPriceMinor=2400,saleValueMinor=fish.SaleValueMinor,
                speciesId=fish.SpeciesKey,speciesName=name,massMg=fish.MassMg,coastalPool=pool.Id,baitCount=final.BaitCount,stamina=final.Stamina,
                creelCount=final.Creel.Count,equipmentInstances=final.Equipment.Instances.Count,remainingLineMm=final.Equipment.RemainingLineMm,
                gameFlightEmissions,gameHookedEmissions,gameNaturalLandings,shutdown=gameShutdownCode}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){Fail(error);}
    }
}
