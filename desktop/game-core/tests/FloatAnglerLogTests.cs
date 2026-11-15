using System;
using System.IO;
using System.Linq;
using FishingDesktop.GameCore;

static class FloatAnglerLogTests
{
    static int failed;
    static string folder;
    static Guid G(){return Guid.NewGuid();}
    static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Test(string label,Action body){try{body();Console.WriteLine("PASS "+label);}catch(Exception e){failed++;Console.WriteLine("FAIL "+label+": "+e);}}
    static string FileName(){return Path.Combine(folder,G().ToString("N")+".fltj");}
    static FloatGameState New(int capacity=4)
    {
        var t=BottomOwnedKitTests.New(balance:40000);
        var rules=new FloatGameRules(2,t.Rig.DefinitionKey,"worm",2,100,capacity,1000000,
            new[]{new FloatSpeciesRule("carp",1,9000000,120),new FloatSpeciesRule("spotted-seabass",1,9000000,165)});
        return FloatGameState.CreateNewWithEquipment(t.ProfileId,t.BranchId,t.WalletMinor,100,t.BaitStackId,"worm",12,t.Equipment,rules,t.CommerceCatalog,t.FoodStackId);
    }
    static void Write(FloatGameStore store,FloatCommand c)
    {var r=store.Execute(c);Need(r.Written&&r.Committed&&!r.IsReplay,"expected write: "+r.Code);}
    static FloatCommand FloatCast(FloatGameState s)
    {
        if(s.SchemaVersion<3)return new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion);
        return new CastFloatWithEquipment(s.ProfileId,s.BranchId,G(),G(),G(),15000,s.Sequence,s.Rig.Revision,s.RulesVersion,s.Equipment.InventoryRevision,s.Equipment.RulesVersion);
    }
    static HostFloatFish Fish(FloatGameState s,string species,long mass)
    {return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),G(),species,mass,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static RecallFloat Recall(FloatGameState s)
    {return new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static SellFloatFish Sell(FloatGameState s)
    {return new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Creel.Select(f=>f.Id),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static void BuyBottom(FloatGameStore store)
    {
        var s=store.State;
        Write(store,new BuyBottomKit(s.ProfileId,s.BranchId,G(),G(),new BottomOwnedKit(G(),G(),G(),G(),G(),G(),G()),s.Sequence,s.Rig.Revision,s.RulesVersion));
    }
    static void Zero(FloatAnglerLog log)
    {Need(log.TotalCasts==0&&log.TotalCaught==0&&log.TotalSold==0&&log.SaleMinor==0&&log.TotalXp==0&&log.Species.Count==0&&
        log.Methods.Select(m=>m.Id).SequenceEqual(new[]{"float","bottom","lure"}),"unexpected empty log");}
    public static int Main(string[] args)
    {
        if(args.Length>1&&args[0]=="--read"){
            foreach(string path in args.Skip(1)){
                var log=FloatAnglerLog.Build(FloatJournalArchive.Inspect(path).State);
                Console.WriteLine("seq="+log.Sequence+" casts="+log.TotalCasts+" caught="+log.TotalCaught+" sold="+log.TotalSold+
                    " minor="+log.SaleMinor+" xp="+log.TotalXp+" methods="+String.Join(",",log.Methods.Select(x=>x.Id+":"+x.Caught))+
                    " records="+String.Join(",",log.Species.Select(x=>x.Id+":"+x.MaxMassMg)));
            }
            return 0;
        }
        folder=args[0];
        Test("original catch XP rounds half a point up and handles the largest representable mass",()=>{
            foreach(long mass in new[]{1L,249999,250000,250001,1000000,1999999,Int64.MaxValue})
                Need(FloatAnglerLog.CatchXp(mass)==10+(long)Decimal.Round(6m*mass/1000000m,0,MidpointRounding.AwayFromZero),"mass rounding");
            try{FloatAnglerLog.CatchXp(0);throw new Exception("zero mass accepted");}catch(ArgumentOutOfRangeException){}
        });
        foreach(int schema in new[]{1,2})Test("schema "+schema+" casts, sale and closed-file replay rebuild the original catch",()=>{
            string path=FileName();var genesis=BottomOwnedKitTests.New(schema,1);
            using(var store=FloatGameStore.CreateNew(path,genesis)){
                Zero(FloatAnglerLog.Build(store.State));Write(store,FloatCast(store.State));Write(store,Fish(store.State,"carp",500000));
                Write(store,Sell(store.State));Write(store,FloatCast(store.State));Write(store,Recall(store.State));
            }
            byte[] frozen=File.ReadAllBytes(path);var result=FloatAnglerLog.Build(FloatJournalArchive.Inspect(path).State);
            Need(result.Sequence==5&&result.TotalCasts==2&&result.TotalCaught==1&&result.TotalSold==1&&result.SaleMinor==60&&result.TotalXp==13&&
                result.Methods[0].Casts==2&&result.Methods[0].Caught==1&&result.Methods[0].Xp==13&&result.Species[0].MaxMassMg==500000&&
                frozen.SequenceEqual(File.ReadAllBytes(path)),"legacy catch disappeared or inspection wrote data");
        });
        Test("three paid methods and a combined sale preserve records and exactly the experience of landed fish",()=>{
            string path=FileName();
            using(var store=FloatGameStore.CreateNew(path,New())){
                BuyBottom(store);var s=store.State;
                Write(store,new BuyLureKit(s.ProfileId,s.BranchId,G(),G(),new OwnedLureKit(G(),G(),G(),G(),G(),G()),s.Sequence,s.Rig.Revision,s.RulesVersion));
                Zero(FloatAnglerLog.Build(store.State));Write(store,FloatCast(store.State));Write(store,Fish(store.State,"carp",500000));
                Write(store,CastBottomKitWithEquipment.Create(store.State,16000));Write(store,Fish(store.State,"carp",1250000));
                Write(store,CastLureWithEquipment.Create(store.State,20000));Write(store,Fish(store.State,"spotted-seabass",2450000));
                var kept=FloatAnglerLog.Build(store.State);Need(kept.TotalXp==56&&kept.TotalSold==0&&kept.SaleMinor==0,"catch or purchase affected sales");
                var sale=Sell(store.State);Write(store,sale);Need(store.Execute(sale).IsReplay,"sale replay");
            }
            using(var store=FloatGameStore.OpenExisting(path)){
                var log=FloatAnglerLog.Build(store.State);Need(store.State.Creel.Count==0&&log.TotalSold==3&&log.SaleMinor==614&&
                    log.TotalCasts==3&&log.TotalCaught==3&&log.TotalXp==56,"sale doubled rewards or removed history");
                Need(log.Methods.Select(m=>m.Xp).SequenceEqual(new long[]{13,18,25})&&log.Methods.All(m=>m.Casts==1&&m.Caught==1)&&
                    log.Species.Select(f=>f.Id).SequenceEqual(new[]{"carp","spotted-seabass"})&&
                    log.Species[0].Caught==2&&log.Species[0].MaxMassMg==1250000&&log.Species[1].MaxMassMg==2450000,"method or species record");
            }
        });
        Test("full creel, pending fish, explicit release and a stale request award no catch or XP",()=>{
            using(var store=FloatGameStore.CreateNew(FileName(),New(1))){
                Write(store,FloatCast(store.State));var landed=Fish(store.State,"carp",250000);Write(store,landed);
                Need(store.Execute(landed).IsReplay,"landing retry");Write(store,FloatCast(store.State));
                var pending=Fish(store.State,"carp",8800000);var r=store.Execute(pending);
                Need(r.Written&&!r.OperationSucceeded&&r.Receipt.LandedSpecies==null&&r.Receipt.LandedMassMg==0&&store.Execute(pending).IsReplay,"pending fish counted");
                var s=store.State;Write(store,new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion));
                Write(store,Sell(store.State));Need(store.Execute(landed).IsReplay,"late landing retry");
                var log=FloatAnglerLog.Build(store.State);
                Need(log.TotalCasts==2&&log.TotalCaught==1&&log.TotalXp==12&&log.Species.Single().MaxMassMg==250000,"full net replaced record");
            }
        });
        Test("interleaved bank rods pair each outcome to its own paid bottom session",()=>{
            using(var st=FloatGameStore.CreateNew(FileName(),New(1))){
                BuyBottom(st);var s=st.State;var kit=new BottomOwnedKit(G(),G(),G(),G(),G(),G(),G());
                Write(st,new BuyAdditionalBottomKit(s.ProfileId,s.BranchId,G(),G(),2,kit,s.Sequence,s.Rig.Revision,s.RulesVersion));
                s=st.State;Write(st,new BuyBankRodStand(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion));
                var left=st.State.OwnedBottomKits[0].Equipment.RigId;var right=kit.Equipment.RigId;
                var a=CastBankBottomRod.Create(st.State,left,15000);Write(st,a);
                var b=CastBankBottomRod.Create(st.State,right,18000);Write(st,b);
                var fish=SettleBankBottomRod.Land(st.State,right,b.SessionId,G(),G(),"carp",750000);Write(st,fish);
                var pending=SettleBankBottomRod.Land(st.State,left,a.SessionId,G(),G(),"carp",2000000);Write(st,pending);
                Write(st,SettleBankBottomRod.Create(st.State,left,a.SessionId,BankRodEnding.Release));
                Write(st,Sell(st.State));Need(st.Execute(fish).IsReplay,"bank replay");
                var log=FloatAnglerLog.Build(st.State);Need(log.TotalCasts==2&&log.TotalCaught==1&&log.Methods[1].Casts==2&&
                    log.Methods[1].Caught==1&&log.Methods[1].Xp==15&&log.TotalXp==15&&log.Species.Single().MaxMassMg==750000,"bank method mismatch");
            }
        });
        Test("a definite prewrite failure publishes no reward; the same command can be confirmed once after retry",()=>{
            string path=FileName();using(var st=FloatGameStore.CreateNew(path,New()))Write(st,FloatCast(st.State));
            bool armed=true;
            using(var st=FloatGameStore.OpenExisting(path,point=>{if(armed&&point==FloatSaveFaultPoint.BeforeWrite){armed=false;throw new IOException("private prewrite");}})){
                var command=Fish(st.State,"carp",1000000);
                try{st.Execute(command);throw new Exception("fault not observed");}catch(IOException){}
                var failedLog=FloatAnglerLog.Build(st.State);Need(st.State.Sequence==1&&failedLog.TotalXp==0&&failedLog.TotalCaught==0&&failedLog.TotalCasts==1,"speculative reward");
                Write(st,command);Need(st.Execute(command).IsReplay,"not idempotent");
            }
            var saved=FloatAnglerLog.Build(FloatJournalArchive.Inspect(path).State);
            Need(saved.TotalXp==16&&saved.TotalCaught==1&&saved.Species.Single().Caught==1,"failed write counted");
        });
        return failed==0?0:1;
    }
}
