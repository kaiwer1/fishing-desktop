using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

sealed partial class DualWindowContext
{
    object visualResult;
    async Task<Dictionary<string,object>> VisualState(ProbeWindow pane)
    {
        return json.Deserialize<Dictionary<string,object>>(await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.visual.state()"));
    }
    async Task VerifyVisibleDesktop(ProbeWindow pane)
    {
        Require(visibleDesktopTest && pane.DesktopReadOnly,"Visible test requires the existing read-only desktop child.");
        CheckDesktop(pane);
        // WinForms ShowWithoutActivation and WS_EX_NOACTIVATE are both set.
        // No Activate()/BringToFront()/Explorer mutation is used here.
        desktopShown.Add(pane);GuardPresentation(()=>pane.Show());CheckDesktop(pane);
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.visual.start()");
        var timer=Stopwatch.StartNew();Dictionary<string,object> state=null;
        while(timer.ElapsedMilliseconds<5000 && Live(pane)){
            state=await VisualState(pane);
            if(Number(state,"frames")>=3)break;
            await Task.Delay(50);
        }
        Require(state!=null && Number(state,"frames")>=3,"Visible diagnostic did not produce animation frames.");
        CheckDesktop(pane);
        string imagePath=Path.Combine(root,"own-webview-visible.png");
        using(var stream=new FileStream(imagePath,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await pane.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        int width,height;
        using(var bitmap=new Bitmap(imagePath)){
            width=bitmap.Width;height=bitmap.Height;
            Require(width==pane.View.ClientSize.Width && height==pane.View.ClientSize.Height && width>=640 && height>=360,"Captured WebView size differs from native viewport.");
            var green=bitmap.GetPixel(64,64);var blue=bitmap.GetPixel(232,64);
            Require(Math.Abs(green.R-113)<=12 && Math.Abs(green.G-217)<=12 && Math.Abs(green.B-165)<=12 &&
                Math.Abs(blue.R-71)<=12 && Math.Abs(blue.G-125)<=12 && Math.Abs(blue.B-183)<=12,"Own WebView image did not contain expected rendering markers.");
        }
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.visual.stop()");
        long stopped=Number(await VisualState(pane),"frames");await Task.Delay(150);
        Require(Number(await VisualState(pane),"frames")==stopped,"Stopped visual kept rendering.");
        GuardPresentation(()=>pane.Hide());desktopShown.Remove(pane);CheckDesktop(pane);
        visualResult=new{passed=true,renderedFrames=stopped,width=width,height=height,markersVerified=true,
            ownWebViewImageOnly=true,desktopCompositionCaptured=false,foregroundUnchanged=true};
        checks.Add("visible no-activate desktop child renders expected pixels and stops its own animation without changing foreground");
    }
}
