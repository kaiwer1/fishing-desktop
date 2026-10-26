using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed partial class DualWindowContext
{
    readonly bool sceneViewport;
    async Task RunViewportProbe(ProbeWindow read,ProbeWindow control)
    {
        // Resize this hidden child control, not a physical display or desktop layer.
        var sizes=new[]{new Size(1280,720),new Size(1920,1080),new Size(2560,1440),new Size(3840,2160),new Size(2560,1600),
            new Size(3440,1440),new Size(5120,1440),new Size(844,390)};
        var rows=new List<object>();string ownerId=owner.Snapshot().OwnerId;
        read.View.Dock=DockStyle.None;read.View.Location=Point.Empty;
        double baseDpr=Convert.ToDouble((await VisualState(read))["devicePixelRatio"]);
        foreach(double zoom in new[]{1.0,1.25,1.5,2.0})foreach(var size in sizes){
            GuardPresentation(()=>{read.View.ZoomFactor=zoom;read.View.Size=size;read.PerformLayout();});
            bool settled=false;
            for(int attempt=0;attempt<50;attempt++){
                await Task.Delay(50);var pending=await VisualState(read);
                double pd=Convert.ToDouble(pending["devicePixelRatio"]),pw=Convert.ToDouble(pending["viewportWidth"]),ph=Convert.ToDouble(pending["viewportHeight"]);
                if(Math.Abs(pd-baseDpr*zoom)<.01 && Math.Abs(pw*pd-size.Width)<=2*pd && Math.Abs(ph*pd-size.Height)<=2*pd){settled=true;break;}
            }
            Require(settled,"Virtual viewport zoom or layout did not settle.");
            string raw=await read.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.visual.inspectViewport()");
            var state=json.Deserialize<Dictionary<string,object>>(raw);var proof=Field(state,"proof");
            Require(state!=null && (string)state["kind"]=="webgl3d" && state["error"]==null && proof!=null && Number(proof,"glError")==0 &&
                !(bool)proof["contextLost"] && Number(proof,"triangles")>10000 && Number(proof,"distinctSampleColors")>=3,"Virtual viewport did not render valid WebGL geometry/pixels.");
            double width=Convert.ToDouble(state["viewportWidth"]),height=Convert.ToDouble(state["viewportHeight"]),dpr=Convert.ToDouble(state["devicePixelRatio"]);
            long actualWidth=Number(state,"width"),actualHeight=Number(state,"height");
            double ratio=Math.Min(Math.Min(dpr,1.5),Math.Sqrt(1920.0*1080/(width*height)));
            Require(read.View.ClientSize==size && width>height && dpr>0 && Math.Abs(width*dpr-size.Width)<=2*dpr && Math.Abs(height*dpr-size.Height)<=2*dpr,
                "Virtual viewport or CSS dimensions differ from the requested own control.");
            Require(actualWidth>0 && actualHeight>0 && actualWidth*actualHeight<=1920L*1080 &&
                Math.Abs(actualWidth-Math.Floor(width*ratio))<=1 && Math.Abs(actualHeight-Math.Floor(height*ratio))<=1 &&
                Number(proof,"width")==actualWidth && Number(proof,"height")==actualHeight && Math.Abs(actualWidth/(double)actualHeight-width/height)<.005,
                "Virtual viewport violated the internal pixel budget or aspect ratio.");
            SameOwner(await Ready(control),ownerId,0,0);LockHeld();
            rows.Add(new{requestedWidth=size.Width,requestedHeight=size.Height,browserZoom=zoom,cssWidth=width,cssHeight=height,devicePixelRatio=dpr,
                internalWidth=actualWidth,internalHeight=actualHeight,triangles=Number(proof,"triangles"),sampleColors=Number(proof,"distinctSampleColors"),glError=Number(proof,"glError")});
            checks.Add("hidden owned WebView virtual landscape viewport "+size.Width+"x"+size.Height+" at browser zoom "+zoom+" retains WebGL proof pixel budget aspect and owner state");
        }
        File.WriteAllText(Path.Combine(root,"viewport-summary.json"),json.Serialize(new{passed=true,checks=checks.ToArray(),viewports=rows.ToArray(),
            virtualViewportsOnly=true,controlledDraws=true,browserZoomOnly=true,physicalDisplayChanged=false,dpiChanged=false,multiplePhysicalMonitorsTested=false,
            presentedFpsMeasured=false,nativeInputTested=false,gameLoaded=false,sequence=owner.Snapshot().Sequence,value=owner.Snapshot().Value,
            presentationChecks=presentationChecks,presentationChecksPassed=!presentationFailed}),Encoding.UTF8);
    }
}
