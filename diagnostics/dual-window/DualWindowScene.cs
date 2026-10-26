using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

sealed partial class DualWindowContext
{
    readonly bool threeScene;
    int sceneLoads;
    double sceneDrawRate;
    object sceneResult;
    async Task<bool> LoadScenePage(ProbeWindow pane,ulong navigation,int generation)
    {
        await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.installLakeScene()");
        for(int attempt=0;attempt<240;attempt++){
            if(!Live(pane) || pane.Navigation!=navigation || pane.NavigationGeneration!=generation || pane.AwaitingNavigationStart)return false;
            string raw=await pane.View.CoreWebView2.ExecuteScriptAsync("window.dualProbe.state()");
            var state=json.Deserialize<Dictionary<string,object>>(raw);
            if(state!=null){
                if(state["sceneLoadError"]!=null)throw new InvalidOperationException("Placeholder lake module could not initialize.");
                if((string)state["sceneKind"]=="webgl3d"){sceneLoads++;return true;}
            }
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Placeholder lake module readiness timed out.");
    }
    async Task VerifyThreeScene(ProbeWindow pane,bool measureRate)
    {
        var timer=Stopwatch.StartNew();Dictionary<string,object> state=null,proof=null;
        while(timer.ElapsedMilliseconds<6000 && Live(pane)){
            state=await VisualState(pane);proof=Field(state,"proof");
            if(state["error"]!=null)throw new InvalidOperationException("Placeholder lake renderer reported an error.");
            if(proof!=null && Number(state,"frames")>=3)break;
            await Task.Delay(30);
        }
        Require(state!=null && (string)state["kind"]=="webgl3d" && proof!=null && Number(proof,"triangles")>10000 &&
            Number(proof,"distinctSampleColors")>=3 && Number(proof,"glError")==0 && !(bool)proof["contextLost"],"WebGL scene geometry or pixel proof was missing.");
        if(measureRate){
            var before=state;await Task.Delay(2000);state=await VisualState(pane);
            double seconds=(Convert.ToDouble(state["sampledAt"])-Convert.ToDouble(before["sampledAt"]))/1000;
            sceneDrawRate=(Number(state,"frames")-Number(before,"frames"))/seconds;
            Require(seconds>=1.8 && sceneDrawRate>=20 && sceneDrawRate<=33 && (bool)state["running"],"Placeholder lake draw callback cadence was outside its diagnostic range.");
            checks.Add("real WebView renders the original placeholder environment with nonuniform WebGL pixels and geometry without loading game state");
            checks.Add("placeholder lake uses the shared scheduler with measured draw callbacks within its 30 per second target tolerance");
        }else{
            Require(sceneLoads>=5,"Recreated desktop views did not retain three-dimensional scene selection.");
            sceneResult=new{passed=true,sceneKind="webgl3d",placeholderEnvironment=true,gameLoaded=false,sceneLoads=sceneLoads,
                finalRecreatedViewVerified=true,triangles=Number(proof,"triangles"),sampleColors=Number(proof,"distinctSampleColors"),glError=Number(proof,"glError"),
                width=Number(state,"width"),height=Number(state,"height"),targetDrawCallbacksPerSecond=30,measuredDrawCallbacksPerSecond=sceneDrawRate,
                presentedFpsMeasured=false,nativeFullscreenTested=false};
        }
    }
}
