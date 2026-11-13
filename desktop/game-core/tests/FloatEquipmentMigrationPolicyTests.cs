using System;
using System.Collections.Generic;
using FishingDesktop.GameCore;

static class FloatEquipmentMigrationPolicyTests
{
    static int failures;
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Test(string name,Action action){try{action();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static Guid G(){return Guid.NewGuid();}
    static FloatEquipmentCatalog Catalog(long capacity=50000,bool compatible=true,bool reverse=false,long rodMass=150000,bool hookRule=true)
    {
        var defs=new List<FloatEquipmentDefinition>{new FloatEquipmentDefinition("rod",FloatEquipmentKind.Rod,rodMass,0,1000,25000),
            new FloatEquipmentDefinition("reel",FloatEquipmentKind.Reel,200000),new FloatEquipmentDefinition("line",FloatEquipmentKind.Line,0,180),
            new FloatEquipmentDefinition("hook",FloatEquipmentKind.Hook,200),new FloatEquipmentDefinition("bobber",FloatEquipmentKind.Bobber,3000,0,2000,3000),
            new FloatEquipmentDefinition("sinker",FloatEquipmentKind.Sinker,2500)};
        if(reverse)defs.Reverse();
        return new FloatEquipmentCatalog(1,"worm",defs,new[]{new FloatEquipmentPairRule("rod","reel",compatible)},
            new[]{new FloatEquipmentSpoolRule("reel","line",180,capacity)},hookRule?new[]{new FloatEquipmentPairRule("hook","worm",true)}:new FloatEquipmentPairRule[0],
            new[]{new FloatEquipmentLossRule("terminal",new[]{FloatEquipmentSlot.Hook})});
    }
    static FloatEquipmentMigrationPolicy Policy(FloatEquipmentCatalog catalog=null,long version=1,string source="legacy-float")
    {return new FloatEquipmentMigrationPolicy(version,source,catalog??Catalog(),"rod","reel","line","hook","bobber","sinker");}
    static FloatGameState State(long length=17321,int schema=2,long wallet=12000,int bait=12,int stamina=100)
    {
        var rig=new FixedFloatRig(G(),7,"legacy-float",G(),G(),G(),length,G(),G(),G());
        var rules=new FloatGameRules(2,"legacy-float","worm",2,100,2,1000000,new[]{new FloatSpeciesRule("carp",100000,10000000,120)});
        var commerce=new FloatCommerceCatalog(1,"worm","net","bread",2400,5,20,20,20,1000,1000,100);
        return schema==1?FloatGameState.CreateNew(G(),G(),wallet,stamina,G(),"worm",bait,rig,rules):
            FloatGameState.CreateNewWithCommerce(G(),G(),wallet,stamina,G(),"worm",bait,rig,rules,commerce,G());
    }
    static string Bytes(byte[] value){return Convert.ToBase64String(value);}
    static int Main()
    {
        Test("explicit mapping preserves every legacy identity and exact nonstandard line length",()=>{
            var s=State();var e=Policy().Map(s);var old=s.LegacyRig;
            Need(e.RigId==old.Id&&e.RigRevision==7&&e.InventoryRevision==1&&e.Instances.Count==6,"Rig identity or count changed.");
            foreach(var id in new[]{old.RodId,old.ReelId,old.LineId,old.HookId,old.BobberId,old.SinkerId})Need(e.Find(id)!=null&&e.Find(id).Revision==1,"New identity substituted.");
            Need(e.Spool(old.ReelId).Id==old.LineId&&e.InitialLineMm==17321&&e.RemainingLineMm==17321&&e.LostLineMm==0,"Length refilled or ownership changed.");
        });
        Test("pure mapping neither mutates source nor emits offers resources or receipts",()=>{
            var s=State(wallet:0,bait:0,stamina:0);string before=Bytes(FloatJournalCodec.EncodeGenesis(s));var e=Policy().Map(s);
            Need(before==Bytes(FloatJournalCodec.EncodeGenesis(s))&&s.Sequence==0&&s.SchemaVersion==2&&s.Equipment==null&&s.EquipmentOffers==null&&s.WalletMinor==0&&s.BaitCount==0&&s.Stamina==0&&s.Receipts.Count==0,"Source was upgraded or gifted resources.");
            Need(e.ValidateAssembly().Valid&&!e.EvaluateCastReadiness(FloatEquipmentGate.Idle,1,0,0,2).Valid,"Mapping bypassed resource readiness.");
        });
        Test("mapping is deterministic across repeated calls and catalogue order",()=>{
            var s=State();var first=Policy();var reordered=Policy(Catalog(reverse:true));
            Need(first.Signature==reordered.Signature&&Bytes(FloatEquipmentCodec.Encode(first.Map(s)))==Bytes(FloatEquipmentCodec.Encode(reordered.Map(s))),"Mapping differs.");
            Need(Bytes(FloatEquipmentCodec.Encode(first.Map(s)))==Bytes(FloatEquipmentCodec.Encode(first.Map(s))),"Repeated mapping differs.");
        });
        Test("policy identity binds version source definition and complete catalogue contents",()=>{
            string original=Policy().Signature;
            Need(original!=Policy(version:2).Signature&&original!=Policy(source:"other").Signature&&original!=Policy(Catalog(rodMass:150001)).Signature,"Policy field omitted from signature.");
            Need(original!=Policy(Catalog(capacity:60000)).Signature,"Spool rule omitted from signature.");
        });
        Test("missing definitions wrong categories and invalid policy versions refuse",()=>{
            Throws<ArgumentException>(()=>new FloatEquipmentMigrationPolicy(1,"legacy-float",Catalog(),"missing","reel","line","hook","bobber","sinker"));
            Throws<ArgumentException>(()=>new FloatEquipmentMigrationPolicy(1,"legacy-float",Catalog(),"hook","reel","line","hook","bobber","sinker"));
            Throws<ArgumentOutOfRangeException>(()=>Policy(version:0));Throws<ArgumentNullException>(()=>new FloatEquipmentMigrationPolicy(1,"legacy-float",null,"rod","reel","line","hook","bobber","sinker"));
        });
        Test("wrong legacy rig definition cannot silently take a starter mapping",()=>Throws<InvalidOperationException>(()=>Policy(source:"different-rig").Map(State())));
        Test("different bait policy cannot reinterpret the old bait stack",()=>{
            var c=Catalog();var other=new FloatEquipmentCatalog(c.Version,"maggot",c.Definitions,c.RodReelRules,c.SpoolRules,new[]{new FloatEquipmentPairRule("hook","maggot",true)},c.LossRules);
            Throws<InvalidOperationException>(()=>Policy(other).Map(State()));
        });
        Test("schema1 and already-owned schema3 require their separate migration paths",()=>{
            Throws<InvalidOperationException>(()=>Policy().Map(State(schema:1)));var s=State();var e=Policy().Map(s);
            var fresh=FloatGameState.CreateNewWithEquipment(s.ProfileId,s.BranchId,s.WalletMinor,s.Stamina,s.BaitStackId,s.BaitDefinitionKey,s.BaitCount,e,s.Rules,s.CommerceCatalog,s.FoodStackId);
            Throws<InvalidOperationException>(()=>Policy().Map(fresh));Throws<ArgumentNullException>(()=>Policy().Map(null));
        });
        Test("incompatible rod reel and missing hook rule refuse instead of inventing compatibility",()=>{
            Throws<InvalidOperationException>(()=>Policy(Catalog(compatible:false)).Map(State()));
            Throws<InvalidOperationException>(()=>Policy(Catalog(hookRule:false)).Map(State()));
        });
        Test("over-capacity and oversized legacy lines refuse without shortening or refilling",()=>{
            var s=State(length:50001);string before=Bytes(FloatJournalCodec.EncodeGenesis(s));Throws<InvalidOperationException>(()=>Policy().Map(s));
            Need(before==Bytes(FloatJournalCodec.EncodeGenesis(s)),"Rejected mapping changed source.");
            Throws<ArgumentOutOfRangeException>(()=>Policy().Map(State(length:100000001)));
        });
        Test("one millimetre and exact spool capacity preserve their integer lengths",()=>{
            foreach(long length in new long[]{1,50000}){var e=Policy().Map(State(length));Need(e.InitialLineMm==length&&e.RemainingLineMm==length&&e.LostLineMm==0,"Boundary length changed.");}
        });
        Test("active legacy session is inspected without settlement refund or new cast",()=>{
            var start=State();var cast=new CastFloat(start.ProfileId,start.BranchId,G(),G(),G(),start.Sequence,start.Rig.Revision,start.RulesVersion);
            var s=FloatGameReducer.Reduce(start,cast,start.Rules).Candidate;Need(s!=null&&s.Session!=null,"Fixture failed.");var session=s.Session;var e=Policy().Map(s);
            Need(Object.ReferenceEquals(s.Session,session)&&s.Sequence==1&&s.BaitCount==11&&s.Stamina==98&&s.Receipts.Count==1&&e.RigRevision==s.Rig.Revision,"Mapping settled or changed a session.");
        });
        Test("mapped payload roundtrip retains all original asset locations",()=>{
            var s=State();var e=Policy().Map(s);var restored=FloatEquipmentCodec.Decode(FloatEquipmentCodec.Encode(e));
            Need(restored.Equipped(FloatEquipmentSlot.Rod).Id==s.LegacyRig.RodId&&restored.Equipped(FloatEquipmentSlot.Hook).Id==s.LegacyRig.HookId&&restored.Spool(s.LegacyRig.ReelId).Id==s.LegacyRig.LineId&&restored.RemainingLineMm==17321,"Payload changed mapped ownership.");
        });
        return failures==0?0:1;
    }
}
