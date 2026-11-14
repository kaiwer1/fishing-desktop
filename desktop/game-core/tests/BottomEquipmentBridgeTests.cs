using System;
using System.Collections.Generic;
using FishingDesktop.GameCore;

static class BottomEquipmentBridgeTests
{
    static int passed;
    static Guid Id(int n){return new Guid(n,0,0,new byte[8]);}
    static void Need(bool ok,string message){if(!ok)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
    static FloatEquipmentLedger Fixture(bool bobber=false,bool starter=false,bool otherReel=false,bool bagHook=false,
        bool? rodAllowed=true,bool hookAllowed=true,int maxCast=35000,int diameter=220,int sinkerMg=18000,
        int remaining=46500,int capacity=120000,string bait="worm")
    {
        string rod=starter?"starter-rod":"bottom-bank-rod",reel=starter?"starter-reel":"bottom-bank-reel";
        string line=starter?"nylon-018":"bottom-022-line",hook=starter?"single-hook":"bottom-single-hook",sinker=starter?"sinker-2_5g":"bottom-18g-sinker";
        int actualDiameter=starter?180:diameter;
        var defs=new[]{
            new FloatEquipmentDefinition(rod,FloatEquipmentKind.Rod,150000,0,starter?1000:10000,starter?25000:maxCast),
            new FloatEquipmentDefinition(reel,FloatEquipmentKind.Reel,200000),
            new FloatEquipmentDefinition(line,FloatEquipmentKind.Line,0,actualDiameter),
            new FloatEquipmentDefinition(hook,FloatEquipmentKind.Hook,200),
            new FloatEquipmentDefinition("float-3g",FloatEquipmentKind.Bobber,3000,0,2000,3000),
            new FloatEquipmentDefinition(sinker,FloatEquipmentKind.Sinker,starter?2500:sinkerMg)};
        var catalog=new FloatEquipmentCatalog(7,bait,defs,rodAllowed.HasValue?new[]{new FloatEquipmentPairRule(rod,reel,rodAllowed.Value)}:new FloatEquipmentPairRule[0],
            new[]{new FloatEquipmentSpoolRule(reel,line,actualDiameter,capacity)},new[]{new FloatEquipmentPairRule(hook,bait,hookAllowed)},new FloatEquipmentLossRule[0]);
        var items=new List<FloatEquipmentInstance>{
            new FloatEquipmentInstance(Id(1),rod,FloatEquipmentKind.Rod,3,FloatEquipmentLocation.RigSlot(Id(20),FloatEquipmentSlot.Rod)),
            new FloatEquipmentInstance(Id(2),reel,FloatEquipmentKind.Reel,4,FloatEquipmentLocation.RigSlot(Id(20),FloatEquipmentSlot.Reel)),
            new FloatEquipmentInstance(Id(3),line,FloatEquipmentKind.Line,9,FloatEquipmentLocation.ReelSpool(Id(otherReel?8:2)),Math.Max(50000,remaining),remaining),
            new FloatEquipmentInstance(Id(4),hook,FloatEquipmentKind.Hook,3,bagHook?FloatEquipmentLocation.Inventory():FloatEquipmentLocation.RigSlot(Id(20),FloatEquipmentSlot.Hook)),
            new FloatEquipmentInstance(Id(5),sinker,FloatEquipmentKind.Sinker,3,FloatEquipmentLocation.RigSlot(Id(20),FloatEquipmentSlot.Sinker)),
            new FloatEquipmentInstance(Id(6),"float-3g",FloatEquipmentKind.Bobber,3,bobber?FloatEquipmentLocation.RigSlot(Id(20),FloatEquipmentSlot.Bobber):FloatEquipmentLocation.Inventory()),
            new FloatEquipmentInstance(Id(7),hook,FloatEquipmentKind.Hook,2,FloatEquipmentLocation.Lost(Id(21))),
            new FloatEquipmentInstance(Id(8),reel,FloatEquipmentKind.Reel,1,FloatEquipmentLocation.Inventory())};
        return new FloatEquipmentLedger(Id(20),5,12,catalog,items);
    }
    static BottomTerminalSelection Selection(Guid? rig=null,long revision=5,long inv=12,long rules=7,
        string mode="bottom_simple",int leader=10,int bait=11,string leaderKey="bottom-016-leader")
    {
        return new BottomTerminalSelection(rig??Id(20),revision,inv,rules,mode,
            leader==0?null:new BottomRigPart(Id(leader),BottomPartKind.Leader,leaderKey),
            bait==0?null:new BottomRigPart(Id(bait),BottomPartKind.HookBait,"worm"));
    }
    static bool Refuses(FloatEquipmentLedger ledger,BottomTerminalSelection selection,string code)
    {
        BottomEquipmentBridge result;return BottomEquipmentBridge.Bind(ledger,selection,out result)==code && result==null;
    }
    public static int Main()
    {
        var ledger=Fixture();var selection=Selection();BottomEquipmentBridge b;
        string original=Convert.ToBase64String(FloatEquipmentCodec.Encode(ledger));
        Need(BottomEquipmentBridge.Bind(ledger,selection,out b)==null && b!=null && b.IsCurrent(ledger),"explicit bottom kit binds to the actual immutable ledger");
        Need(b.Assembly.ItemId(BottomPartKind.Rod)==Id(1) && b.Assembly.ItemId(BottomPartKind.Reel)==Id(2) &&
            b.Assembly.ItemId(BottomPartKind.SpoolLine)==Id(3) && b.Assembly.ItemId(BottomPartKind.Hook)==Id(4) && b.Assembly.ItemId(BottomPartKind.Sinker)==Id(5) &&
            b.Assembly.ItemId(BottomPartKind.Leader)==Id(10) && b.Assembly.ItemId(BottomPartKind.HookBait)==Id(11),"all seven part references preserve the existing identities");
        Need(b.Assembly.LineRemainingMm==46500 && b.Assembly.RigId==ledger.RigId && b.Assembly.RigRevision==5 &&
            b.InventoryRevision==12 && b.EquipmentRulesVersion==7 && b.InstalledLine==ledger.Spool(Id(2)) && b.InstalledLine.Revision==9,"mounted line, remaining length and all ownership revisions agree");
        Need(b.Assembly.EvaluateCastReadiness(1,2,true)==null && b.Assembly.EvaluateCastReadiness(0,2,true)=="hookbait-empty","selected bait stack does not invent an available bait unit");
        Need(Refuses(ledger,null,"bottom-unsupported") && Refuses(ledger,Selection(mode:"bottom_cage"),"bottom-unsupported"),"missing configuration and feeder mode never substitute bottom sinkers");
        Need(Refuses(Fixture(bobber:true),selection,"rig-incompatible") && Refuses(Fixture(starter:true),selection,"rig-incompatible"),"installed bobber and ordinary starter kit without bobber are both refused");
        Need(Refuses(ledger,Selection(leader:0),"rig-incomplete") && Refuses(ledger,Selection(bait:0),"rig-incomplete"),"both supplemental parts must be explicitly selected");
        Need(Refuses(ledger,Selection(leaderKey:"other-leader"),"rig-incompatible") && Refuses(ledger,Selection(leader:11),"part-identity-conflict"),"unsupported leader and shared stack identity refused");
        Need(Refuses(ledger,Selection(leader:6),"part-identity-conflict") && Refuses(ledger,Selection(leader:20),"part-identity-conflict") &&
            Refuses(ledger,Selection(bait:21),"part-identity-conflict"),"external references cannot alias bag, rig or historical loss owner");
        Need(Refuses(ledger,Selection(rig:Id(22)),"version-conflict") && Refuses(ledger,Selection(revision:6),"version-conflict") &&
            Refuses(ledger,Selection(inv:11),"version-conflict") && Refuses(ledger,Selection(rules:2),"version-conflict"),"wrong rig or stale selection revisions fail closed");
        Need(Refuses(Fixture(otherReel:true),selection,"rig-incomplete") && Refuses(Fixture(bagHook:true),selection,"rig-incomplete"),"line on a different reel and bagged hook cannot fill mounted slots");
        Need(Refuses(Fixture(rodAllowed:null),selection,"configuration-missing") && Refuses(Fixture(rodAllowed:false),selection,"incompatible-components"),"rod and reel require an explicit allowed pair");
        Need(Refuses(Fixture(hookAllowed:false),selection,"incompatible-bait") && Refuses(Fixture(bait:"corn"),selection,"incompatible-bait"),"hook rule and selected bait must match the equipment catalog");
        Need(Refuses(Fixture(maxCast:18000),selection,"cast-mass-out-of-range") && Refuses(Fixture(maxCast:40000),selection,"rig-incompatible"),"18.9 g total load and configured v1 rod range are checked against actual rod data");
        Need(Refuses(Fixture(diameter:180),selection,"rig-incompatible") && Refuses(Fixture(sinkerMg:2500),selection,"rig-incompatible"),"part names alone cannot override actual line diameter and sinker mass");
        Need(Refuses(Fixture(capacity:40000),selection,"line-capacity-exceeded") && Refuses(Fixture(remaining:100001),selection,"line-length-unsupported"),"overfull spool and unsupported length are rejected without truncating line");
        var copy=FloatEquipmentCodec.Decode(Convert.FromBase64String(original));
        Need(!b.IsCurrent(copy) && original==Convert.ToBase64String(FloatEquipmentCodec.Encode(ledger)) &&
            original==Convert.ToBase64String(FloatEquipmentCodec.Encode(copy)),"binding does not change equipment bytes or survive replacement of the owning ledger");
        Console.WriteLine("RESULT passed="+passed);return 0;
    }
}
