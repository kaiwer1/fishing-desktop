using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    bool GameWardrobeSelfTest { get { return gameMode=="wardrobe-self-test"; } }

    async Task WardrobeProbeMenu(ProbeWindow pane,bool open)
    {
        await SceneProbeButton(pane,open?"scene-menu":"menu");
        await Wait(pane,s=>Field(s,"snapshot")!=null && (bool)Field(s,"snapshot")["MenuOpen"]==open &&
            (!open || Field(s,"wardrobeState")!=null && (bool)Field(s,"wardrobeState")["available"]));
    }

    async Task WardrobeProbeState(ProbeWindow pane,long sequence,long balance,string active)
    {
        await Wait(pane,s=>Field(s,"snapshot")!=null && Number(Field(s,"snapshot"),"Sequence")==sequence &&
            Field(s,"wardrobeState")!=null && (string)Field(s,"wardrobeState")["activeId"]==active &&
            s["wardrobePending"]==null && game.Snapshot.State.Sequence==sequence && game.Snapshot.State.WalletMinor==balance &&
            game.Snapshot.State.WornClothingId==active,15000);
    }

    async Task WardrobeProbeBuy(ProbeWindow pane,string id,long sequence,long price)
    {
        string digest=GameShopJournalDigest();
        var before=game.Snapshot.State;
        await SceneProbeButton(pane,"wardrobe-buy-"+id);
        var quote=await SceneProbeScript(pane,@"(()=>{
            const b=document.getElementById('wardrobe-confirm'),box=b.parentElement;
            return {visible:!box.hidden && !b.disabled,text:box.querySelector('p').textContent};
        })()");
        Require((bool)quote["visible"] && ((string)quote["text"]).Contains(FloatClothingCatalog.Find(id).Name) &&
            ((string)quote["text"]).Contains((price/100).ToString()) && GameShopJournalDigest()==digest &&
            game.Snapshot.State.Sequence==sequence-1 && !game.Snapshot.State.OwnedClothingIds.Contains(id),
            "Showing a clothing price must leave ownership and the journal unchanged.");
        await SceneProbeButton(pane,"wardrobe-confirm");
        await WardrobeProbeState(pane,sequence,before.WalletMinor-price,before.WornClothingId);
        Require(game.Snapshot.State.OwnedClothingIds.Contains(id),"Confirmed clothing was not recorded as owned.");
    }

    async Task WardrobeProbeColor(ProbeWindow pane,FloatFishingWater water,string color)
    {
        var first=await Wait(pane,s=>PlayerProbeDrawn(s,water) && (string)SceneProbeRenderer(s)["sleeveColor"]==color,30000);
        long frames=Number(SceneProbeRenderer(first),"frames");
        await Wait(pane,s=>PlayerProbeDrawn(s,water) && Number(SceneProbeRenderer(s),"frames")>frames &&
            (string)SceneProbeRenderer(s)["sleeveColor"]==color,15000);
    }

    async Task WardrobeProbePhoto(ProbeWindow pane,string name)
    {
        string path=Path.Combine(root,name);
        using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,output);
    }

    // Pair the same lower-right foreground pixels. Only count changes from
    // greener to bluer hues in the sleeve's projected region, away from water.
    int WardrobeProbeBluePixels()
    {
        using(var plain=new Bitmap(Path.Combine(root,"wardrobe-default.png")))
        using(var blue=new Bitmap(Path.Combine(root,"wardrobe-coast.png"))){
            Require(plain.Size==blue.Size,"Sleeve comparison requires the same viewport.");
            int changed=0;
            for(int y=(int)(plain.Height*.82);y<plain.Height*.985;y+=2)
                for(int x=(int)(plain.Width*.685);x<plain.Width*.92;x+=2){
                    Color a=plain.GetPixel(x,y),b=blue.GetPixel(x,y);
                    if(a.G>a.B+3 && b.B>b.G+4 && (b.B-b.G)-(a.B-a.G)>16)changed++;
                }
            return changed;
        }
    }

    async void RunGameWardrobeProbe()
    {
        try{
            Require(GameWardrobeSelfTest&&windows.Count==1&&windows.ContainsKey(true),"Wardrobe probe requires a single player window.");
            var pane=windows[true];await Ready(pane);pane.ClientSize=new Size(1440,900);await Activate(pane);
            var lake=FloatFishingAtlas.Find("development-still-water");var first=game.Snapshot.State;
            Require(first.Sequence==1&&first.WalletMinor==12000&&first.OwnedClothingIds.Count==0&&first.WornClothingId==null,
                "Unexpected new wardrobe profile.");
            await WardrobeProbeColor(pane,lake,null);await WardrobeProbePhoto(pane,"wardrobe-default.png");
            checks.Add("the private single-player profile starts with no bought clothing and renders its ordinary sleeve");

            await WardrobeProbeMenu(pane,true);
            string journal=GameShopJournalDigest();
            await SceneProbeButton(pane,"wardrobe-buy-moss-jacket");
            await SceneProbeButton(pane,"wardrobe-cancel");
            Require(journal==GameShopJournalDigest()&&game.Snapshot.State.Sequence==1,"Cancelling the quoted coat charged the player.");
            checks.Add("the real HUD menu can open a clothing price and cancel without touching the journal");

            await WardrobeProbeBuy(pane,"moss-jacket",2,1800);
            checks.Add("the confirmed moss jacket purchase costs exactly eighteen silver and does not wear the item");
            await SceneProbeButton(pane,"wardrobe-wear-moss-jacket");await WardrobeProbeState(pane,3,10200,"moss-jacket");
            await WardrobeProbeMenu(pane,false);await WardrobeProbeColor(pane,lake,"#586849");
            checks.Add("wearing the owned moss jacket changes the rendered sleeve without a second charge");
            await WardrobeProbeMenu(pane,true);
            await SceneProbeButton(pane,"wardrobe-remove-moss-jacket");await WardrobeProbeState(pane,4,10200,null);
            await WardrobeProbeMenu(pane,false);await WardrobeProbeColor(pane,lake,null);
            checks.Add("removing it restores the ordinary sleeve and preserves both ownership and silver");

            await WardrobeProbeMenu(pane,true);await WardrobeProbeBuy(pane,"coast-jacket",5,2600);
            await SceneProbeButton(pane,"wardrobe-wear-coast-jacket");await WardrobeProbeState(pane,6,7600,"coast-jacket");
            await WardrobeProbeMenu(pane,false);await WardrobeProbeColor(pane,lake,"#355c79");
            await WardrobeProbePhoto(pane,"wardrobe-coast.png");
            int blue=WardrobeProbeBluePixels();
            Require(blue>=35,"The saved foreground did not visibly change from the ordinary sleeve to the blue jacket.");
            checks.Add("the coast jacket costs twenty-six silver and turns foreground sleeve pixels blue in the captured 3D view");

            await WardrobeProbeMenu(pane,true);
            await SceneProbeButton(pane,"wardrobe-remove-coast-jacket");await WardrobeProbeState(pane,7,7600,null);
            await SceneProbeButton(pane,"wardrobe-wear-coast-jacket");await WardrobeProbeState(pane,8,7600,"coast-jacket");
            await WardrobeProbeMenu(pane,false);await WardrobeProbeColor(pane,lake,"#355c79");
            checks.Add("taking the coast jacket off and wearing it again preserves the balance and both owned items");

            string old=pane.Ticket.DocumentId; journal=GameShopJournalDigest(); Reload(pane);
            await Wait(pane,s=>(string)s["documentId"]!=old && s["lease"]==null && PlayerProbeDrawn(s,lake) &&
                Field(s,"wardrobeState")!=null && (string)Field(s,"wardrobeState")["activeId"]=="coast-jacket" &&
                (string)SceneProbeRenderer(s)["sleeveColor"]=="#355c79",30000);
            Require(journal==GameShopJournalDigest(),"Reload changed the clothing journal.");
            checks.Add("reloading projects the saved blue jacket before granting a new lease and leaves the file unchanged");
            PrepareGameShutdown();Require(ExitCode==0 && gameShutdownCode=="no-active-session","Wardrobe shutdown performed additional recovery.");
            DisposeGame();
            var final=FloatJournalArchive.Inspect(Path.Combine(root,"game.fltj")).State;
            Require(final.Sequence==8 && final.WalletMinor==7600 && final.WornClothingId=="coast-jacket" &&
                final.OwnedClothingIds.Count==2 && final.OwnedClothingIds.Contains("moss-jacket") && final.OwnedClothingIds.Contains("coast-jacket") &&
                final.BaitCount==12 && final.Stamina==100 && final.Equipment.Instances.Count==6 && final.Equipment.RemainingLineMm==50000 &&
                final.Equipment.LostLineMm==0 && final.Creel.Count==0 && final.Session==null,"Closed journal clothing or equipment did not replay.");
            checks.Add("a fresh replay of the closed private file preserves the two purchases, worn jacket, gear and fifty metres of line");
            File.WriteAllText(Path.Combine(root,"webview-summary.json"),json.Serialize(new {
                passed=true,checks=checks.ToArray(),gameLoaded=true,wardrobeConnected=true,singleInteractionWindow=true,
                visibleSleeveChanged=true,blueSleevePixels=blue,confirmedPurchases=2,ownedClothing=2,wornClothing=final.WornClothingId,
                reloadVerified=true,closedJournalVerified=true,devToolsMouseInput=true,nativePhysicalMouse=false,
                desktopLayer=false,personalSaveRead=false,schemaVersion=final.SchemaVersion,sequence=final.Sequence,value=final.WalletMinor,
                baitCount=final.BaitCount,stamina=final.Stamina,equipmentInstances=final.Equipment.Instances.Count,
                remainingLineMm=final.Equipment.RemainingLineMm,lostLineMm=final.Equipment.LostLineMm,shutdown=gameShutdownCode}),Encoding.UTF8);
            Shutdown();
        }catch(Exception error){Fail(error);}
    }
}
