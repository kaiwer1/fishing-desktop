using System;
using FishingDesktop.Diagnostics.HostPolicy;

static class DrawProgressTests
{
    static int passed;
    static void Check(bool value,string label){if(!value)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
    static bool Reject(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    static int Main()
    {
        try{
            var g=new DrawProgressGuard();g.Observe(true,"one",1,0,true);g.Observe(true,"one",1,1,true);Check(g.Checks==1,"positive progress in a stable document and revision is checked");
            Check(Reject(()=>g.Observe(true,"one",1,1,true)),"running flag cannot conceal a stalled frame counter");
            Check(Reject(()=>g.Observe(true,"one",1,0,true)),"counter rollback in a stable view rejected");
            Check(Reject(()=>new DrawProgressGuard().Observe(true,"one",1,10,false)),"stopped active loop rejected immediately");
            g=new DrawProgressGuard();g.Observe(true,"one",1,100,true);g.Observe(false,null,0,0,false);g.Observe(true,"one",1,0,true);Check(g.Checks==0,"in-flight or paused interval resets comparison without inventing progress");
            g.Observe(true,"two",1,0,true);Check(g.Checks==0,"new document may start a fresh frame counter");
            g.Observe(true,"two",2,0,true);Check(g.Checks==0,"new policy revision starts a fresh comparison");
            Check(Reject(()=>new DrawProgressGuard().Observe(true,null,0,0,true)) && Reject(()=>new DrawProgressGuard().Observe(true,"one",-1,0,true)) && Reject(()=>new DrawProgressGuard().Observe(true,"one",0,-1,true)),"invalid comparable identity or count rejected");
            return passed==8?0:1;
        }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
    }
}
