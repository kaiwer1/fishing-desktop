using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using FishingDesktop.GameCore;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    bool GameSpecimenSelfTest {get{return gameMode=="specimen-self-test";}}

    static Dictionary<string,object> SpecimenProbeViewer(Dictionary<string,object> page)
    {
        return Field(Field(page,"specimenState"),"viewer");
    }

    // Sample only the interior of the modal canvas, excluding the fish label,
    // toolbar and underlying creel. Keep just the rotated view as a PNG.
    async Task<int[]> SpecimenProbePixels(ProbeWindow pane,Dictionary<string,object> area,string filename=null)
    {
        using(var buffer=new MemoryStream()){
            await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,buffer);
            if(filename!=null){
                using(var output=new FileStream(Path.Combine(root,filename),FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    buffer.WriteTo(output);
            }
            buffer.Position=0;
            using(var image=new Bitmap(buffer)){
                var pixels=new List<int>();var colors=new HashSet<int>();
                for(int row=1;row<20;row++)for(int col=1;col<40;col++){
                    double px=(Number(area,"left")+Number(area,"width")*col/40.0)/Number(area,"viewWidth");
                    double py=(Number(area,"top")+Number(area,"height")*row/20.0)/Number(area,"viewHeight");
                    var c=image.GetPixel((int)(image.Width*px),(int)(image.Height*py));
                    pixels.Add(c.ToArgb());colors.Add((c.R>>3)<<10|(c.G>>3)<<5|c.B>>3);
                }
                Require(colors.Count>=8,"The specimen canvas contains too few distinct colors.");
                return pixels.ToArray();
            }
        }
    }

    async Task<object> SpecimenProbeInspect(ProbeWindow pane,FloatFish fish,string name)
    {
        var before=game.Snapshot.State;string digest=GameShopJournalDigest();
        string id=fish.Id.ToString("D");
        Require(GameSpecimenSelfTest&&before.Creel.Count==1&&before.Creel[0].Id==fish.Id&&before.Session==null,
            "Inspection requires the fish from the confirmed landing.");
        var opener=await SceneProbeScript(pane,@"(()=>{
            const b=document.querySelector('#creel button[data-specimen-id]');if(!b)return null;
            b.scrollIntoView({block:'center'});const r=b.getBoundingClientRect(),x=Math.round(r.left+r.width/2),y=Math.round(r.top+r.height/2);
            return !b.disabled&&b.contains(document.elementFromPoint(x,y))?{x,y,id:b.dataset.specimenId}:null;
        })()");
        Require((string)opener["id"]==id,"Creel inspection button targets a different catch.");
        await SceneProbeMouse(pane,opener,true);await SceneProbeMouse(pane,opener,false);
        var opened=await Wait(pane,p=>{
            var state=Field(p,"specimenState");var viewer=SpecimenProbeViewer(p);
            return state!=null&&(bool)state["open"]&&!(bool)state["loading"]&&state["error"]==null&&viewer!=null&&
                (bool)viewer["active"]&&Number(viewer,"frames")>0&&Number(viewer,"triangles")>0;
        },20000);
        var record=Field(Field(opened,"specimenState"),"fish");var view=SpecimenProbeViewer(opened);
        var area=await SceneProbeScript(pane,@"(()=>{
            const d=document.querySelector('#specimen-dialog[open]'),c=d?.querySelector('#specimen-canvas');
            if(!c)return null;const r=c.getBoundingClientRect(),x=Math.round(r.left+r.width/2),y=Math.round(r.top+r.height/2);
            return document.elementFromPoint(x,y)===c?{x,y,left:Math.round(r.left),top:Math.round(r.top),width:Math.round(r.width),
                height:Math.round(r.height),viewWidth:innerWidth,viewHeight:innerHeight,id:d.dataset.fishId,
                title:document.querySelector('#specimen-species').textContent,mass:document.querySelector('#specimen-mass').textContent}:null;
        })()");
        string unit=fish.MassMg<1000000?" 克":" 千克";
        string weight=(fish.MassMg/(fish.MassMg<1000000?1000m:1000000m)).ToString("0.######",CultureInfo.InvariantCulture)+unit;
        Require((string)area["id"]==id&&(string)area["title"]==name&&(string)area["mass"]==weight&&
            (string)record["id"]==id&&(string)record["speciesId"]==fish.SpeciesKey&&Number(record,"massMg")==fish.MassMg&&
            (string)view["speciesId"]==fish.SpeciesKey&&Number(view,"width")>0&&Number(view,"height")>0,
            "The visible model, species or weight differs from the actual owned fish.");
        checks.Add("the real creel button opens a rendered three-dimensional model matching the landed fish ID, species and exact saved weight");

        var initial=await SpecimenProbePixels(pane,area);
        await SceneProbeMouse(pane,area,true);
        var moved=new Dictionary<string,object>{{"x",Number(area,"x")+Number(area,"width")/9},{"y",Number(area,"y")+Number(area,"height")/8}};
        await pane.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(new{
            type="mouseMoved",x=Number(moved,"x"),y=Number(moved,"y"),button="none",buttons=1}));
        await SceneProbeMouse(pane,moved,false);
        var rotated=SpecimenProbeViewer(await Wait(pane,p=>{
            var v=SpecimenProbeViewer(p);
            return v!=null&&Number(v,"frames")>Number(view,"frames")&&
                Math.Abs(Convert.ToDouble(v["yaw"])-Convert.ToDouble(view["yaw"]))>.1&&
                Math.Abs(Convert.ToDouble(v["pitch"])-Convert.ToDouble(view["pitch"]))>.1;
        }));
        await CatchProbeWheel(pane,moved);
        await Wait(pane,p=>SpecimenProbeViewer(p)!=null&&Number(SpecimenProbeViewer(p),"frames")>Number(rotated,"frames")&&
            Convert.ToDouble(SpecimenProbeViewer(p)["zoom"])>Convert.ToDouble(rotated["zoom"]));
        var turned=await SpecimenProbePixels(pane,area,"creel-specimen.png");
        int changed=0;
        for(int i=0;i<initial.Length;i++){
            var a=Color.FromArgb(initial[i]);var b=Color.FromArgb(turned[i]);
            if(Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B)>25)changed++;
        }
        Require(changed>=12,"Moving the camera did not change rendered pixels inside the specimen canvas.");
        checks.Add("a real left drag rotates the model and a wheel gesture zooms it, changing pixels inside the specimen canvas");

        await SceneProbeButton(pane,"specimen-close");
        await Wait(pane,p=>{
            var s=Field(p,"specimenState");
            return s!=null&&!(bool)s["open"]&&s["fish"]==null&&s["viewer"]==null;
        });
        Require(await pane.View.CoreWebView2.ExecuteScriptAsync(
            "!document.querySelector('#specimen-dialog[open]')&&!document.querySelector('#specimen-canvas')&&!document.querySelector('#creel input').checked")=="true"&&
            game.Snapshot.State==before&&digest==GameShopJournalDigest(),"Viewing the specimen or closing it changed ownership, selection or the journal.");
        checks.Add("closing inspection removes the viewer and returns to the same unselected owned catch without any save transaction");
        return new {speciesId=fish.SpeciesKey, massMg=fish.MassMg, triangles=Number(view,"triangles"), changedPixels=changed,
            sampledPixels=initial.Length, framesBefore=Number(view,"frames"), framesAfterRotation=Number(rotated,"frames"),
            readOnlyJournalVerified=true};
    }
}
