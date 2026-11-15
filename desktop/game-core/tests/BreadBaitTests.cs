using System;
using System.IO;
using System.Linq;
using FishingDesktop.GameCore;

static class BreadBaitTests
{
    static Guid Id(){return Guid.NewGuid();}
    static void Need(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
    static ManageFloatBait Bait(FloatGameState s,FloatBaitAction action)
    {return new ManageFloatBait(s.ProfileId,s.BranchId,Id(),Id(),action,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static BuyFloatSupply Bread(FloatGameState s)
    {
        var quote=FloatPurchaseQuote.Create(s,FloatSupplyProduct.Food,1);
        return new BuyFloatSupply(s.ProfileId,s.BranchId,Id(),Id(),quote,Guid.Empty,s.Sequence,s.Rig.Revision,s.RulesVersion);
    }
    static CastFloatWithEquipment Cast(FloatGameState s)
    {return new CastFloatWithEquipment(s.ProfileId,s.BranchId,Id(),Id(),Id(),5000,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion);}
    static RecallFloat Recall(FloatGameState s)
    {return new RecallFloat(s.ProfileId,s.BranchId,Id(),Id(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static void Written(FloatCommitResult result){Need(result.Written&&result.Committed&&!result.IsReplay,result.Code);}
    static void Refused(FloatCommitResult result,string code){Need(!result.Committed&&!result.Written&&result.Code==code,result.Code);}
    static void LegacyAndRepeats(string path)
    {
        var initial=BottomOwnedKitTests.New();
        using(var st=FloatGameStore.CreateNew(path,initial)){
            Written(st.Execute(Cast(st.State)));Written(st.Execute(Recall(st.State)));
        }
        var old=File.ReadAllBytes(path);
        ManageFloatBait prepare,select;CastFloatWithEquipment cast;
        long saved;
        using(var st=FloatGameStore.OpenExisting(path)){
            var s=st.State;Need(s.BreadBaitCount==0&&!s.BreadBaitSelected&&s.BaitCount==11,"old float history");
            Refused(st.Execute(Bait(s,FloatBaitAction.PrepareBread)),"food-empty");
            Refused(st.Execute(Bait(s,FloatBaitAction.SelectBread)),"bait-empty");
            var bill=Bread(s);Written(st.Execute(bill));
            Need(st.Execute(bill).IsReplay&&st.State.WalletMinor==initial.WalletMinor-s.CommerceCatalog.FoodPriceMinor,"repeat bread purchase");
            s=st.State;prepare=Bait(s,FloatBaitAction.PrepareBread);Written(st.Execute(prepare));
            Need(st.Execute(prepare).IsReplay&&st.State.BreadBaitCount==3&&st.State.FoodCount==0&&!st.State.BreadBaitSelected,"craft or duplicate");
            Refused(st.Execute(new ManageFloatBait(s.ProfileId,s.BranchId,prepare.RequestId,prepare.EventId,FloatBaitAction.SelectWorm,s.Sequence,s.Rig.Revision,s.RulesVersion)),"request-conflict");
            select=Bait(st.State,FloatBaitAction.SelectBread);Written(st.Execute(select));
            Need(st.Execute(select).IsReplay&&st.State.BreadBaitSelected,"duplicate selection");
            cast=Cast(st.State);Written(st.Execute(cast));
            Need(st.Execute(cast).IsReplay&&st.State.BreadBaitCount==2&&st.State.BaitCount==11,"cast debit");
            Refused(st.Execute(Bait(st.State,FloatBaitAction.SelectWorm)),"session-busy");
            s=st.State;
            Written(st.Execute(new HostFloatFish(s.ProfileId,s.BranchId,Id(),Id(),s.Session.Id,Id(),Id(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion)));
            Need(st.State.Creel.Count==1&&st.State.BreadBaitCount==2,"land fish with prepared bait");
            saved=st.State.Sequence;
        }
        using(var st=FloatGameStore.OpenExisting(path)){
            var s=st.State;
            Need(s.Sequence==saved&&s.BreadBaitSelected&&s.BreadBaitCount==2&&s.BaitCount==11&&s.FoodCount==0&&s.Creel.Count==1,"reopen after landing");
            Need(st.Execute(prepare).IsReplay&&st.Execute(cast).IsReplay&&st.State.Sequence==saved,"repeat on reopened store");
            Written(st.Execute(Bait(s,FloatBaitAction.SelectWorm)));Written(st.Execute(Cast(st.State)));
            Need(st.State.BaitCount==10&&st.State.BreadBaitCount==2,"return to worm");
        }
        Need(File.ReadAllBytes(path).Take(old.Length).SequenceEqual(old),"old journal bytes changed");
        Console.WriteLine("PASS old journal, purchase, preparation, selection, catch, duplicate requests, reopening and switching back");
    }
    static void GestureAndEmpty(string path)
    {
        var baseState=BottomOwnedKitTests.New();
        var s=FloatGameState.CreateNewWithEquipment(baseState.ProfileId,baseState.BranchId,100,100,baseState.BaitStackId,"worm",0,
            baseState.Equipment,baseState.Rules,baseState.CommerceCatalog,baseState.FoodStackId);
        using(var owner=FloatGameOwner.CreateNew(path,s)){
            var view=owner.OpenView(FloatViewRole.Interaction);var doc=owner.AttachDocument(view);
            owner.SetHostPaused(false);var lease=owner.GrantControl(view,doc);
            Func<FloatCommand,FloatCommitResult> send=c=>owner.ExecuteUser(view,doc,lease,c);
            Refused(send(Bait(s,FloatBaitAction.PrepareBread)),"menu-required");
            owner.SetMenuOpen(true);Written(send(Bread(s)));
            Written(send(Bait(owner.Snapshot.State,FloatBaitAction.PrepareBread)));
            Written(send(Bait(owner.Snapshot.State,FloatBaitAction.SelectBread)));owner.SetMenuOpen(false);
            var input=new FloatCastPreparation(owner,new FloatCastPreparationRules(1000,100),target=>true);
            var landing=new FloatCastTarget(0,0,4000,s.RulesVersion,5000);
            var cancel=input.Begin(view,doc,lease,landing);
            Need(cancel.Accepted&&input.Cancel(cancel.Gesture).Accepted&&owner.Snapshot.State.BreadBaitCount==3,"cancelled charge consumed bait");
            for(int i=0;i<3;i++){
                var hold=input.Begin(view,doc,lease,landing);Need(hold.Accepted&&input.Release(hold.Gesture).Accepted,"mouse press/release");
                Need(input.CommitPrepared(hold.Gesture).ShouldStartFlight,"confirmed release failed");
                Need(!input.CommitPrepared(hold.Gesture).ShouldStartFlight&&owner.Snapshot.State.BreadBaitCount==2-i,"duplicate release");
                Written(send(Recall(owner.Snapshot.State)));
            }
            Need(input.Begin(view,doc,lease,landing).Code=="bait-empty"&&owner.Snapshot.State.Stamina==94,"empty bait/stamina");
        }
        Console.WriteLine("PASS zero worms, menu authority, cancelled press, three confirmed releases and empty-stack rejection");
    }
    public static int Main(string[] args)
    {
        LegacyAndRepeats(Path.Combine(args[0],"old.fltj"));GestureAndEmpty(Path.Combine(args[0],"gestures.fltj"));
        var env=new FloatBiteEnvironment(.2,new[]{new FloatBiteCandidate("carp",3,1,2),new FloatBiteCandidate("crucian",4,1,5),new FloatBiteCandidate("other",0,1,4)},false);
        var bread=env.WithBreadPaste();Need(bread.Candidates[0].Weight==9&&bread.Candidates[1].Weight==8&&bread.Candidates[2].Weight==0&&
            !bread.Available&&bread.HazardPerSecond==env.HazardPerSecond&&bread.Candidates[1].MaximumMassMg==5,"bait changed ecology boundaries");
        foreach(int schema in new[]{1,2}){
            var s=BottomOwnedKitTests.New(schema);
            Need(!s.BreadRecipeAvailable&&!FloatGameReducer.Reduce(s,Bait(s,FloatBaitAction.PrepareBread),s.Rules).HasCandidate,"legacy availability");
        }
        Console.WriteLine("PASS ecology weights, absent species, weather availability and legacy schemas");return 0;
    }
}
