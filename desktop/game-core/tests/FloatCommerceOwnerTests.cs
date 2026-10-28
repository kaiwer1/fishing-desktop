using System;
using System.IO;
using System.Threading;
using FishingDesktop.GameCore;

static class FloatCommerceOwnerTests
{
    static int passed,failed,nextId,nextFile;static string root;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,6});}
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Throws<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
    static string PathFor(string label){return Path.Combine(root,(++nextFile)+"-"+label+".fltj");}
    static FloatGameState New(bool commerce=true,int stamina=70,int capacity=1)
    {
        var rules=new FloatGameRules(1,"float-rig","worm",2,100,capacity,1000000,new[]{new FloatSpeciesRule("carp",100000,2000000,1000)});
        var rig=new FixedFloatRig(G(),1,"float-rig",G(),G(),G(),50000,G(),G(),G());
        var catalog=new FloatCommerceCatalog(1,"worm","landing-net","bread",2400,5,20,20,20,1000,1000,100);
        return commerce?FloatGameState.CreateNewWithCommerce(G(),G(),12000,stamina,G(),"worm",12,rig,rules,catalog,G()):FloatGameState.CreateNew(G(),G(),12000,stamina,G(),"worm",12,rig,rules);
    }
    static BuyFloatSupply Buy(FloatGameState s,FloatSupplyProduct product,int count=1)
    {return new BuyFloatSupply(s.ProfileId,s.BranchId,G(),G(),FloatPurchaseQuote.Create(s,product,count),product==FloatSupplyProduct.Net?G():Guid.Empty,s.Sequence,1,1);}
    static EquipFloatNet Equip(FloatGameState s,Guid id){return new EquipFloatNet(s.ProfileId,s.BranchId,G(),G(),id,s.Sequence,1,1);}
    static UseFloatFood Food(FloatGameState s){return new UseFloatFood(s.ProfileId,s.BranchId,G(),G(),s.FoodStackId,s.Sequence,1,1);}
    static CastFloat Cast(FloatGameState s){return new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,1,1);}
    static HostFloatFish Fish(FloatGameState s){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),G(),"carp",500000,s.Sequence,1,1);}
    static void Commit(FloatCommitResult r){Need(r.Committed&&r.Written&&!r.IsReplay,"Expected commit: "+r.Code);}
    static void Denied(FloatCommitResult r,string code){Need(r.Code==code&&!r.Committed&&!r.Written&&r.Receipt==null&&r.State==null,"Expected authority refusal: "+code+" got "+r.Code);}
    static void Domain(FloatCommitResult r,string code){Need(r.Code==code&&!r.Committed&&!r.Written&&r.Receipt==null,"Expected domain refusal: "+code+" got "+r.Code);}
    static byte[] Bytes(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){var b=new byte[(int)f.Length];int at=0;while(at<b.Length){int n=f.Read(b,at,b.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}return b;}}
    static bool Same(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    sealed class Control
    {
        internal readonly FloatGameOwner Owner;internal FloatViewTicket View;internal FloatDocumentTicket Document;internal FloatControlLease Lease;
        internal Control(FloatGameOwner owner){Owner=owner;View=owner.OpenView(FloatViewRole.Interaction);Document=owner.AttachDocument(View);owner.SetHostPaused(false);Lease=owner.GrantControl(View,Document);}
        internal FloatGameState State{get{return Owner.Snapshot.State;}}
        internal FloatCommitResult Send(FloatCommand command){return Owner.ExecuteUser(View,Document,Lease,command);}
        internal void Land(){Commit(Send(Cast(State)));Commit(Owner.ExecuteHost(Fish(State)));}
    }
    static int Main(string[] args)
    {
        if(args.Length!=1||!Directory.Exists(args[0]))return 2;root=Path.GetFullPath(args[0]);
        Test("authorized menu buys equips and eats while simulation remains paused",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("menu"),New())){var c=new Control(owner);owner.SetMenuOpen(true);Commit(c.Send(Buy(c.State,FloatSupplyProduct.Net)));Need(c.State.EquippedNetId==Guid.Empty,"Purchase equipped net.");Commit(c.Send(Equip(c.State,c.State.OwnedNet.Id)));Commit(c.Send(Buy(c.State,FloatSupplyProduct.Bait,2)));Commit(c.Send(Buy(c.State,FloatSupplyProduct.Food,2)));Commit(c.Send(Food(c.State)));Need(!owner.Snapshot.CanSimulate&&c.State.WalletMinor==9520&&c.State.BaitCount==22&&c.State.FoodCount==1&&c.State.Stamina==90,"Menu transaction changed unrelated state.");}
        });
        Test("all supply command types require current authority before exposing state",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("unauthorized"),New())){var s=owner.Snapshot.State;foreach(var command in new FloatCommand[]{Buy(s,FloatSupplyProduct.Bait),Equip(s,G()),Food(s)})Denied(owner.ExecuteUser(null,null,null,command),"control-required");Need(owner.Snapshot.State.Sequence==0,"Unauthorized supply changed state.");}
        });
        Test("observer cannot shop with a valid interaction lease",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("observer"),New())){var c=new Control(owner);var view=owner.OpenView(FloatViewRole.Observer);var doc=owner.AttachDocument(view);Denied(owner.ExecuteUser(view,doc,c.Lease,Buy(c.State,FloatSupplyProduct.Net)),"control-required");Need(owner.ReadView(view,doc).State.OwnedNet==null,"Observer purchased net.");}
        });
        Test("old purchase receipts remain protected after revoke",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("revoke"),New())){var c=new Control(owner);var buy=Buy(c.State,FloatSupplyProduct.Food);Commit(c.Send(buy));Need(owner.RevokeControl(c.Lease),"Revoke failed.");Denied(c.Send(buy),"control-required");Need(c.State.FoodCount==1,"Receipt replay changed supply.");}
        });
        Test("document reload rejects old supply commands before receipt lookup",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("reload"),New())){var c=new Control(owner);var oldDocument=c.Document;var oldLease=c.Lease;var buy=Buy(c.State,FloatSupplyProduct.Food);Commit(c.Send(buy));c.Document=owner.AttachDocument(c.View);c.Lease=owner.GrantControl(c.View,c.Document);Denied(owner.ExecuteUser(c.View,oldDocument,oldLease,buy),"control-required");Need(c.Send(buy).IsReplay&&c.State.FoodCount==1,"Current host command replay duplicated purchase.");}
        });
        Test("host pause and resume never revive a shopping lease",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("pause"),New())){var c=new Control(owner);var buy=Buy(c.State,FloatSupplyProduct.Bait);owner.SetHostPaused(true);owner.SetHostPaused(false);Denied(c.Send(buy),"control-required");c.Lease=owner.GrantControl(c.View,c.Document);Commit(c.Send(buy));Need(c.State.BaitCount==17,"Fresh grant could not buy.");}
        });
        Test("active cast blocks buy equip and food even through an open menu",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("active"),New())){var c=new Control(owner);Commit(c.Send(Buy(c.State,FloatSupplyProduct.Net)));Commit(c.Send(Buy(c.State,FloatSupplyProduct.Food)));Commit(c.Send(Cast(c.State)));owner.SetMenuOpen(true);Domain(c.Send(Buy(c.State,FloatSupplyProduct.Bait)),"session-busy");Domain(c.Send(Equip(c.State,c.State.OwnedNet.Id)),"session-busy");Domain(c.Send(Food(c.State)),"session-busy");Need(c.State.Session!=null&&c.State.FoodCount==1&&c.State.EquippedNetId==Guid.Empty,"Active commerce leaked through.");}
        });
        Test("pending fish blocks supplies without discarding fish or awarding another",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("pending"),New())){var c=new Control(owner);c.Land();Commit(c.Send(Cast(c.State)));Commit(owner.ExecuteHost(Fish(c.State)));Guid id=c.State.Session.PendingFish.Id;owner.SetMenuOpen(true);Domain(c.Send(Buy(c.State,FloatSupplyProduct.Bait)),"session-busy");Domain(c.Send(Equip(c.State,G())),"session-busy");Domain(c.Send(Food(c.State)),"session-busy");Need(c.State.Session.PendingFish.Id==id&&c.State.Creel.Count==1,"Pending fish changed during rejected commerce.");}
        });
        Test("host simulation channel cannot submit commerce commands",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("host-channel"),New())){var c=new Control(owner);foreach(var command in new FloatCommand[]{Buy(c.State,FloatSupplyProduct.Bait),Equip(c.State,G()),Food(c.State)})Denied(owner.ExecuteHost(command),"host-command-not-allowed");}
        });
        Test("required schema rejects legacy active journal before startup settlement writes",()=>{
            string path=PathFor("legacy-open");using(var store=FloatGameStore.CreateNew(path,New(false))){Commit(store.Execute(Cast(store.State)));}byte[] before=Bytes(path);Throws<InvalidOperationException>(()=>{using(var owner=FloatGameOwner.OpenExisting(path,null,2)){}});Need(Same(before,Bytes(path)),"Schema gate settled legacy session before refusing.");using(var store=FloatGameStore.OpenExisting(path))Need(store.State.Session!=null&&store.State.Sequence==1,"Legacy active state was changed.");
        });
        Test("required schema2 open preserves owned supplies during explicit startup recovery",()=>{
            string path=PathFor("commerce-open");Guid net;using(var owner=FloatGameOwner.CreateNew(path,New())){var c=new Control(owner);Commit(c.Send(Buy(c.State,FloatSupplyProduct.Net)));Commit(c.Send(Equip(c.State,c.State.OwnedNet.Id)));Commit(c.Send(Buy(c.State,FloatSupplyProduct.Food,2)));Commit(c.Send(Cast(c.State)));net=c.State.EquippedNetId;}
            using(var owner=FloatGameOwner.OpenExisting(path,null,2)){var s=owner.Snapshot.State;Need(s.SchemaVersion==2&&s.Session==null&&s.EquippedNetId==net&&s.OwnedNet.Id==net&&s.FoodCount==2&&s.BaitCount==11&&s.Stamina==68,"Startup recovery lost supplies or refunded.");}
        });
        Test("known prewrite purchase failure retains authorization for exact retry",()=>{
            string path=PathFor("known");bool armed=false;using(var owner=FloatGameOwner.CreateNew(path,New(),point=>{if(armed&&point==FloatSaveFaultPoint.BeforeWrite)throw new IOException("fixture");})){var c=new Control(owner);var buy=Buy(c.State,FloatSupplyProduct.Food,2);byte[] before=Bytes(path);armed=true;Throws<IOException>(()=>c.Send(buy));Need(!owner.Snapshot.Faulted&&c.State.FoodCount==0&&Same(before,Bytes(path)),"Known failure altered state.");armed=false;Commit(c.Send(buy));Need(c.Send(buy).IsReplay&&c.State.FoodCount==2&&c.State.WalletMinor==11960,"Retry purchased twice.");}
        });
        Test("uncertain supply write revokes control hides state and reopens exactly once",()=>{
            string path=PathFor("unknown");bool armed=false;BuyFloatSupply buy;
            using(var owner=FloatGameOwner.CreateNew(path,New(),point=>{if(armed&&point==FloatSaveFaultPoint.AfterFlush)throw new IOException("fixture");})){var c=new Control(owner);buy=Buy(c.State,FloatSupplyProduct.Food,2);armed=true;Throws<FloatSaveUncertainException>(()=>c.Send(buy));Need(owner.Snapshot.Faulted&&owner.Snapshot.State==null,"Unknown outcome exposed stale state.");Denied(c.Send(buy),"control-required");}
            using(var owner=FloatGameOwner.OpenExisting(path,null,2)){var c=new Control(owner);var r=c.Send(buy);Need(r.IsReplay&&!r.Written&&c.State.FoodCount==2&&c.State.WalletMinor==11960,"Reopen repeated uncertain purchase.");}
        });
        Test("food use and equipment replay preserve later state instead of applying again",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("replay"),New())){var c=new Control(owner);Commit(c.Send(Buy(c.State,FloatSupplyProduct.Net)));var equip=Equip(c.State,c.State.OwnedNet.Id);Commit(c.Send(equip));Commit(c.Send(Buy(c.State,FloatSupplyProduct.Food,2)));var eat=Food(c.State);Commit(c.Send(eat));Commit(c.Send(Equip(c.State,Guid.Empty)));Need(c.Send(equip).IsReplay&&c.Send(eat).IsReplay&&c.State.EquippedNetId==Guid.Empty&&c.State.FoodCount==1&&c.State.Stamina==90,"Old receipt reapplied an inventory change.");}
        });
        Test("full-stamina food is not consumed through owner menu",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("full"),New(stamina:100))){var c=new Control(owner);Commit(c.Send(Buy(c.State,FloatSupplyProduct.Food)));owner.SetMenuOpen(true);Domain(c.Send(Food(c.State)),"stamina-full");Need(c.State.FoodCount==1&&c.State.Sequence==1,"Full stamina consumed food.");}
        });
        Test("wrong thread cannot use commerce authority",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("thread"),New())){var c=new Control(owner);var buy=Buy(c.State,FloatSupplyProduct.Bait);Exception caught=null;var thread=new Thread(()=>{try{c.Send(buy);}catch(Exception e){caught=e;}});thread.Start();thread.Join();Need(caught is InvalidOperationException&&c.State.Sequence==0,"Wrong thread changed commerce.");}
        });
        Test("reentrant supply mutation during an owned write is rejected",()=>{
            FloatGameOwner owner=null;Control c=null;bool armed=false,denied=false;
            using(owner=FloatGameOwner.CreateNew(PathFor("reentry"),New(),point=>{if(armed&&point==FloatSaveFaultPoint.BeforeWrite){try{c.Send(null);}catch(InvalidOperationException){denied=true;}}})){c=new Control(owner);armed=true;Commit(c.Send(Buy(c.State,FloatSupplyProduct.Bait)));Need(denied&&c.State.Sequence==1&&c.State.BaitCount==17,"Reentry changed transaction.");}
        });
        Test("shutdown revokes commerce and cannot expose old purchase receipt",()=>{
            using(var owner=FloatGameOwner.CreateNew(PathFor("shutdown"),New())){var c=new Control(owner);var buy=Buy(c.State,FloatSupplyProduct.Food);Commit(c.Send(buy));Need(owner.Shutdown().Confirmed,"Shutdown failed.");Denied(c.Send(buy),"control-required");Need(c.State.FoodCount==1,"Shutdown changed supply.");}
        });
        Console.WriteLine("RESULT "+passed+" passed "+failed+" failed");return failed==0?0:1;
    }
}
