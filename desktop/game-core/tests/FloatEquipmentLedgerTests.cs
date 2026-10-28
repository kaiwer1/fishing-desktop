using System;
using System.Collections.Generic;
using FishingDesktop.GameCore;

static class FloatEquipmentLedgerTests
{
    static int passed,failed,nextId;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,31});}
    static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static bool Has(FloatEquipmentCheck check,string code){foreach(var issue in check.Issues)if(issue.Code==code)return true;return false;}
    static FloatEquipmentCatalog Catalog(bool? rodAllowed=true,bool? hookAllowed=true,long spareCapacity=60000,bool includeHook=true)
    {
        var definitions=new List<FloatEquipmentDefinition>{
            new FloatEquipmentDefinition("rod",FloatEquipmentKind.Rod,200000,0,1000,20000),new FloatEquipmentDefinition("reel",FloatEquipmentKind.Reel,250000),
            new FloatEquipmentDefinition("reel-b",FloatEquipmentKind.Reel,220000),new FloatEquipmentDefinition("line",FloatEquipmentKind.Line,0,200),
            new FloatEquipmentDefinition("hook-b",FloatEquipmentKind.Hook,600),new FloatEquipmentDefinition("bobber",FloatEquipmentKind.Bobber,1000,0,500,8000),
            new FloatEquipmentDefinition("sinker",FloatEquipmentKind.Sinker,3000),new FloatEquipmentDefinition("heavy-sinker",FloatEquipmentKind.Sinker,9000)};
        if(includeHook)definitions.Add(new FloatEquipmentDefinition("hook",FloatEquipmentKind.Hook,500));
        var rods=new List<FloatEquipmentPairRule>{new FloatEquipmentPairRule("rod","reel-b",true)};
        if(rodAllowed.HasValue)rods.Add(new FloatEquipmentPairRule("rod","reel",rodAllowed.Value));
        var hooks=new List<FloatEquipmentPairRule>{new FloatEquipmentPairRule("hook-b","worm",true)};
        if(includeHook&&hookAllowed.HasValue)hooks.Add(new FloatEquipmentPairRule("hook","worm",hookAllowed.Value));
        return new FloatEquipmentCatalog(7,"worm",definitions,rods,
            new[]{new FloatEquipmentSpoolRule("reel","line",200,50000),new FloatEquipmentSpoolRule("reel-b","line",200,spareCapacity)},hooks,
            new[]{new FloatEquipmentLossRule("hook-connection",new[]{FloatEquipmentSlot.Hook}),new FloatEquipmentLossRule("terminal-chain",new[]{FloatEquipmentSlot.Hook,FloatEquipmentSlot.Bobber,FloatEquipmentSlot.Sinker})});
    }
    sealed class Fixture
    {
        internal readonly Guid Rig=G(),Rod=G(),Reel=G(),Line=G(),Hook=G(),Bobber=G(),Sinker=G(),SpareHook=G(),SpareReel=G(),SpareLine=G();
        internal readonly List<FloatEquipmentInstance> Items;
        internal readonly FloatEquipmentLedger State;
        internal Fixture(FloatEquipmentCatalog catalog=null,long rigRevision=1,long inventoryRevision=1,long spareHookRevision=1)
        {
            Items=new List<FloatEquipmentInstance>{
                Item(Rod,"rod",FloatEquipmentKind.Rod,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Rod)),
                Item(Reel,"reel",FloatEquipmentKind.Reel,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Reel)),
                new FloatEquipmentInstance(Line,"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.ReelSpool(Reel),50000,50000),
                Item(Hook,"hook",FloatEquipmentKind.Hook,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Hook)),
                Item(Bobber,"bobber",FloatEquipmentKind.Bobber,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Bobber)),
                Item(Sinker,"sinker",FloatEquipmentKind.Sinker,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Sinker)),
                new FloatEquipmentInstance(SpareHook,"hook-b",FloatEquipmentKind.Hook,spareHookRevision,FloatEquipmentLocation.Inventory()),
                Item(SpareReel,"reel-b",FloatEquipmentKind.Reel,FloatEquipmentLocation.Inventory()),
                new FloatEquipmentInstance(SpareLine,"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.Inventory(),20000,20000)};
            State=new FloatEquipmentLedger(Rig,rigRevision,inventoryRevision,catalog??Catalog(),Items);
        }
    }
    static FloatEquipmentInstance Item(Guid id,string key,FloatEquipmentKind kind,FloatEquipmentLocation location){return new FloatEquipmentInstance(id,key,kind,1,location);}
    static FloatEquipmentChange Replace(FloatEquipmentLedger state,FloatEquipmentSlot slot,Guid? id)
    {return state.ReplaceSlot(FloatEquipmentGate.Idle,state.RigRevision,state.InventoryRevision,slot,id);}
    static FloatEquipmentChange Move(FloatEquipmentLedger state,Guid line,Guid? reel)
    {return state.MoveLine(FloatEquipmentGate.Idle,state.RigRevision,state.InventoryRevision,line,reel);}
    static FloatEquipmentCheck Ready(FloatEquipmentLedger state,long line=10000,int bait=1,int stamina=100)
    {return state.EvaluateCastReadiness(FloatEquipmentGate.Idle,line,bait,stamina,2);}
    static FloatEquipmentChange Loss(FloatEquipmentLedger state,long outLine,long cut,string rule="hook-connection",Guid? eventId=null)
    {return state.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,state.LockForSession(G()),eventId??G(),rule,outLine,cut);}
    static void Refused(FloatEquipmentLedger original,FloatEquipmentChange change,string code)
    {Need(!change.Accepted&&change.Code==code&&Object.ReferenceEquals(change.Candidate,original),"Expected unchanged rejection "+code+"; got "+change.Code);}
    static void Conserved(FloatEquipmentLedger before,FloatEquipmentChange result,long loss=0)
    {
        Need(result.Accepted&&result.Candidate.InitialLineMm==before.InitialLineMm&&result.Candidate.RemainingLineMm+loss==before.RemainingLineMm&&
            result.Candidate.LostLineMm==before.LostLineMm+loss&&result.LostLineMm==loss,"Line conservation failed.");
    }
    static int Main()
    {
        Test("explicit complete fixture is valid and cast readiness uses real installed line",()=>{
            var f=new Fixture();Need(f.State.ValidateAssembly().Valid&&Ready(f.State,50000).Valid,"Complete assembly rejected.");
            Need(f.State.InitialLineMm==70000&&f.State.RemainingLineMm==70000&&f.State.LostLineMm==0&&f.State.Spool(f.Reel).Id==f.Line,"Line inventory was duplicated.");
            Need(Has(Ready(f.State,50001),"insufficient-line"),"Spare bag line was counted as installed.");
        });
        Test("instance and catalogue input lists are frozen against caller mutation",()=>{
            var f=new Fixture();f.Items.Clear();Need(f.State.Instances.Count==9&&Ready(f.State).Valid,"Ledger retained mutable input list.");
            var definitions=new List<FloatEquipmentDefinition>{new FloatEquipmentDefinition("hook",FloatEquipmentKind.Hook,100)};
            var slots=new List<FloatEquipmentSlot>{FloatEquipmentSlot.Hook};var rules=new List<FloatEquipmentLossRule>{new FloatEquipmentLossRule("loss",slots)};
            var catalog=new FloatEquipmentCatalog(1,"worm",definitions,new FloatEquipmentPairRule[0],new FloatEquipmentSpoolRule[0],new FloatEquipmentPairRule[0],rules);
            definitions.Clear();slots.Add(FloatEquipmentSlot.Rod);rules.Clear();Need(catalog.Definitions.Count==1&&catalog.LossRules.Count==1&&catalog.LossRules[0].LostSlots.Count==1,"Catalogue retained mutable rows.");
            Reject<NotSupportedException>(()=>((IList<FloatEquipmentInstance>)f.State.Instances).Clear());
        });
        Test("missing configuration differs from an explicit incompatibility decision",()=>{
            Need(Has(new Fixture(Catalog(rodAllowed:null)).State.ValidateAssembly(),"configuration-missing"),"Missing rod rule was guessed.");
            Need(Has(new Fixture(Catalog(rodAllowed:false)).State.ValidateAssembly(),"incompatible-components"),"Explicit denied connection ignored.");
            Need(Has(new Fixture(Catalog(includeHook:false)).State.ValidateAssembly(),"configuration-missing"),"Missing item definition was guessed.");
            Need(Has(new Fixture(Catalog(hookAllowed:false)).State.ValidateAssembly(),"incompatible-bait"),"Bait compatibility ignored.");
        });
        Test("a missing hook remains editable but cannot be cast or session locked",()=>{
            var f=new Fixture();var result=Replace(f.State,FloatEquipmentSlot.Hook,null);Conserved(f.State,result);
            Need(result.Candidate.ValidateAssembly().Valid&&Has(Ready(result.Candidate),"missing-component")&&!Has(result.Candidate.ValidateAssembly(),"cast-mass-out-of-range"),"Missing part was treated as invalid saved assembly.");
            Need(result.Candidate.Find(f.Hook).Location.Place==FloatEquipmentPlace.Inventory,"Detached hook lost ownership.");Reject<InvalidOperationException>(()=>result.Candidate.LockForSession(G()));
        });
        Test("hook replacement moves old and new instances atomically and increments exact versions",()=>{
            var f=new Fixture();var result=Replace(f.State,FloatEquipmentSlot.Hook,f.SpareHook);Conserved(f.State,result);var after=result.Candidate;
            Need(after.Equipped(FloatEquipmentSlot.Hook).Id==f.SpareHook&&after.Find(f.Hook).Location.Place==FloatEquipmentPlace.Inventory&&after.RigRevision==2&&after.InventoryRevision==2,"Replacement was partial.");
            Need(after.Find(f.Hook).Revision==2&&after.Find(f.SpareHook).Revision==2&&Object.ReferenceEquals(after.Find(f.Line),f.State.Find(f.Line))&&f.State.Equipped(FloatEquipmentSlot.Hook).Id==f.Hook,"Replacement changed unrelated instance or original.");
        });
        Test("wrong kind missing identity and occupied instance changes preserve the original assembly",()=>{
            var f=new Fixture();Refused(f.State,Replace(f.State,FloatEquipmentSlot.Hook,f.SpareReel),"slot-type-mismatch");
            Refused(f.State,Replace(f.State,FloatEquipmentSlot.Hook,G()),"item-not-found");Refused(f.State,Replace(f.State,FloatEquipmentSlot.Hook,Guid.Empty),"item-identity-invalid");
            Refused(f.State,Replace(f.State,(FloatEquipmentSlot)99,null),"slot-type-mismatch");
        });
        Test("unchanged slot and line requests never create new revisions",()=>{
            var f=new Fixture();Refused(f.State,Replace(f.State,FloatEquipmentSlot.Hook,f.Hook),"assembly-unchanged");Refused(f.State,Move(f.State,f.Line,f.Reel),"line-unchanged");
            Refused(f.State,Move(f.State,f.SpareLine,null),"line-unchanged");var missing=Replace(f.State,FloatEquipmentSlot.Hook,null).Candidate;Refused(missing,Replace(missing,FloatEquipmentSlot.Hook,null),"assembly-unchanged");
        });
        Test("active pending and unknown states refuse ordinary assembly and line edits",()=>{
            var f=new Fixture();foreach(var pair in new[]{Tuple.Create(FloatEquipmentGate.ActiveSession,"rig-busy"),Tuple.Create(FloatEquipmentGate.PendingResult,"result-pending"),Tuple.Create(FloatEquipmentGate.PersistenceUnknown,"persistence-uncertain")}){
                Refused(f.State,f.State.ReplaceSlot(pair.Item1,1,1,FloatEquipmentSlot.Hook,f.SpareHook),pair.Item2);Refused(f.State,f.State.MoveLine(pair.Item1,1,1,f.Line,null),pair.Item2);
                Need(Has(f.State.EvaluateCastReadiness(pair.Item1,10000,1,100,2),pair.Item2),"Readiness omitted busy reason.");}
        });
        Test("both inventory and rig optimistic versions are required",()=>{
            var f=new Fixture();Refused(f.State,f.State.ReplaceSlot(FloatEquipmentGate.Idle,2,1,FloatEquipmentSlot.Hook,null),"stale-rig");
            Refused(f.State,f.State.MoveLine(FloatEquipmentGate.Idle,1,2,f.Line,null),"stale-inventory");
        });
        Test("swapping two reels keeps each original line attached to its own reel",()=>{
            var f=new Fixture();var loaded=Move(f.State,f.SpareLine,f.SpareReel);Conserved(f.State,loaded);Need(loaded.Candidate.RigRevision==1,"Bag spool edit changed equipped assembly revision.");
            var swapped=Replace(loaded.Candidate,FloatEquipmentSlot.Reel,f.SpareReel);Conserved(loaded.Candidate,swapped);var s=swapped.Candidate;
            Need(s.Spool(f.Reel).Id==f.Line&&s.Spool(f.SpareReel).Id==f.SpareLine&&s.Find(f.Reel).Location.Place==FloatEquipmentPlace.Inventory&&s.Equipped(FloatEquipmentSlot.Reel).Id==f.SpareReel,"Reel swap copied or reassigned old line.");
            Need(Ready(s,20000).Valid&&Has(Ready(s,20001),"insufficient-line"),"Readiness used the detached reel's line.");
        });
        Test("detaching a loaded reel leaves its line inside the inventory reel",()=>{
            var f=new Fixture();var result=Replace(f.State,FloatEquipmentSlot.Reel,null);Conserved(f.State,result);
            Need(result.Candidate.Spool(f.Reel).Id==f.Line&&result.Candidate.Find(f.Line).Location.Place==FloatEquipmentPlace.ReelSpool&&result.Candidate.ValidateAssembly().Valid&&Has(Ready(result.Candidate),"missing-component"),"Detached reel duplicated or orphaned its line.");
        });
        Test("whole line removal and reinstallation retain identity length and origin budget",()=>{
            var f=new Fixture();var detached=Move(f.State,f.Line,null);Conserved(f.State,detached);
            Need(detached.Candidate.Find(f.Line).Location.Place==FloatEquipmentPlace.Inventory&&detached.Candidate.Spool(f.Reel)==null&&Has(Ready(detached.Candidate),"missing-line"),"Line did not move as one segment.");
            var restored=Move(detached.Candidate,f.Line,f.Reel);Conserved(detached.Candidate,restored);Need(restored.Candidate.Find(f.Line).RemainingLineMm==50000&&restored.Candidate.Instances.Count==9&&Ready(restored.Candidate).Valid,"Reinstall generated a new line.");
        });
        Test("moving a line between inventory reels changes inventory but not equipped rig version",()=>{
            var f=new Fixture();var result=Move(f.State,f.SpareLine,f.SpareReel);Conserved(f.State,result);
            Need(result.Candidate.RigRevision==1&&result.Candidate.InventoryRevision==2&&result.Candidate.Find(f.SpareReel).Revision==2&&result.Candidate.Find(f.SpareLine).Revision==2,"Bag-only move revised wrong objects.");
        });
        Test("occupied or unavailable spools refuse a line transfer without displacing existing line",()=>{
            var f=new Fixture();Refused(f.State,Move(f.State,f.SpareLine,f.Reel),"spool-occupied");Refused(f.State,Move(f.State,f.SpareLine,f.Hook),"reel-unavailable");
            Refused(f.State,Move(f.State,f.SpareHook,f.SpareReel),"slot-type-mismatch");Refused(f.State,Move(f.State,f.SpareLine,G()),"item-not-found");
        });
        Test("spool capacity is explicit and rejected transfers preserve both source and destination",()=>{
            var f=new Fixture(Catalog(spareCapacity:19999));Refused(f.State,Move(f.State,f.SpareLine,f.SpareReel),"line-capacity-exceeded");
            var exact=new Fixture(Catalog(spareCapacity:20000));Need(Move(exact.State,exact.SpareLine,exact.SpareReel).Accepted,"Exact capacity rejected.");
        });
        Test("bobber loading rejects an incompatible replacement as one atomic change",()=>{
            var f=new Fixture();var items=new List<FloatEquipmentInstance>(f.Items);Guid heavy=G();items.Add(Item(heavy,"heavy-sinker",FloatEquipmentKind.Sinker,FloatEquipmentLocation.Inventory()));
            var state=new FloatEquipmentLedger(f.Rig,1,1,f.State.Catalog,items);Refused(state,Replace(state,FloatEquipmentSlot.Sinker,heavy),"bobber-load-out-of-range");Need(state.Equipped(FloatEquipmentSlot.Sinker).Id==f.Sinker,"Failed replacement removed the old sinker.");
        });
        Test("partial terminal loss removes only the configured hook and exact downstream line length",()=>{
            var f=new Fixture();var result=Loss(f.State,10000,3000);Conserved(f.State,result,7000);var s=result.Candidate;
            Need(s.Find(f.Line).RemainingLineMm==43000&&s.Find(f.Line).InitialLineMm==50000&&s.Find(f.Line).LostLineMm==7000&&s.Find(f.Line).Location.OwnerId==f.Reel,"Partial cut did not preserve spool plus proximal line.");
            Need(result.LostItemIds.Count==1&&result.LostItemIds[0]==f.Hook&&s.Find(f.Hook).Location.Place==FloatEquipmentPlace.Lost&&s.Equipped(FloatEquipmentSlot.Hook)==null&&s.Equipped(FloatEquipmentSlot.Bobber).Id==f.Bobber&&s.Equipped(FloatEquipmentSlot.Sinker).Id==f.Sinker,"Loss expanded beyond configured terminal.");
            Need(s.RigRevision==2&&s.InventoryRevision==2&&s.Find(f.Line).Revision==2&&s.Find(f.Reel).Revision==2&&f.State.Find(f.Line).RemainingLineMm==50000,"Loss versions or original state changed incorrectly.");
        });
        Test("full line loss leaves a zero-length tombstone and removes no undeclared rod or reel",()=>{
            var f=new Fixture();var result=Loss(f.State,50000,0,"terminal-chain");Conserved(f.State,result,50000);var s=result.Candidate;
            Need(result.LostItemIds.Count==4&&s.Find(f.Line).RemainingLineMm==0&&s.Find(f.Line).Location.Place==FloatEquipmentPlace.Lost&&s.Spool(f.Reel)==null&&s.Instances.Count==9,"Full loss deleted or duplicated identity.");
            Need(s.Equipped(FloatEquipmentSlot.Rod).Id==f.Rod&&s.Equipped(FloatEquipmentSlot.Reel).Id==f.Reel&&s.ValidateAssembly().Valid&&Has(Ready(s),"missing-line"),"Terminal loss destroyed retained tackle or claimed readiness.");
            Refused(s,Move(s,f.Line,f.Reel),"item-unavailable");Refused(s,Replace(s,FloatEquipmentSlot.Hook,f.Hook),"item-unavailable");
        });
        Test("zero-millimeter connector loss changes only the declared terminal and assembly",()=>{
            var f=new Fixture();var result=Loss(f.State,12000,12000);Conserved(f.State,result,0);
            Need(Object.ReferenceEquals(result.Candidate.Find(f.Line),f.State.Find(f.Line))&&Object.ReferenceEquals(result.Candidate.Find(f.Reel),f.State.Find(f.Reel))&&result.LostItemIds.Count==1,"Zero line loss silently shortened or revised a line.");
        });
        Test("lost identities cannot be resurrected by movement or duplicate construction",()=>{
            var f=new Fixture();var s=Loss(f.State,50000,0).Candidate;var rows=new List<FloatEquipmentInstance>(s.Instances);
            rows.Add(new FloatEquipmentInstance(f.Line,"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.Inventory(),50000,50000));
            Reject<ArgumentException>(()=>new FloatEquipmentLedger(f.Rig,s.RigRevision,s.InventoryRevision,s.Catalog,rows));
            Reject<ArgumentException>(()=>new FloatEquipmentInstance(G(),"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.Inventory(),100,0));
            Reject<ArgumentException>(()=>new FloatEquipmentInstance(G(),"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.Lost(G()),100,1));
            Reject<InvalidOperationException>(()=>s.Find(f.Hook).Move(FloatEquipmentLocation.Inventory()));
        });
        Test("invalid cut coordinates cannot lose unexposed or negative line",()=>{
            var f=new Fixture();foreach(var pair in new[]{new long[]{-1,0},new long[]{0,0},new long[]{50001,0},new long[]{1000,-1},new long[]{1000,1001},new long[]{Int64.MaxValue,0}})
                Refused(f.State,Loss(f.State,pair[0],pair[1]),"line-loss-out-of-range");
        });
        Test("session locks bind exact immutable ledger and catalogue rather than matching version numbers",()=>{
            var f=new Fixture();var locked=f.State.LockForSession(G());var same=new FloatEquipmentLedger(f.Rig,1,1,f.State.Catalog,f.Items);
            var changedRules=new FloatEquipmentLedger(f.Rig,1,1,Catalog(),f.Items);
            Refused(same,same.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,locked,G(),"hook-connection",1000,0),"session-rig-not-current");
            Refused(changedRules,changedRules.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,locked,G(),"hook-connection",1000,0),"session-rig-not-current");
        });
        Test("an old locked loss cannot be applied to its resulting candidate a second time",()=>{
            var f=new Fixture();var locked=f.State.LockForSession(G());Guid ev=G();var first=f.State.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,locked,ev,"hook-connection",10000,3000);Conserved(f.State,first,7000);
            Refused(first.Candidate,first.Candidate.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,locked,ev,"hook-connection",10000,3000),"session-rig-not-current");
            var repeated=f.State.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,locked,ev,"hook-connection",10000,3000);Need(repeated.Accepted&&repeated.Candidate.RemainingLineMm==first.Candidate.RemainingLineMm,"Pure planning mutated its input or added loss.");
            var repaired=Replace(first.Candidate,FloatEquipmentSlot.Hook,f.SpareHook).Candidate;
            Refused(repaired,repaired.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,repaired.LockForSession(G()),ev,"hook-connection",1000,0),"loss-event-used");
        });
        Test("unknown loss rule colliding event identities and non-active gates reject unchanged",()=>{
            var f=new Fixture();var locked=f.State.LockForSession(G());Refused(f.State,Loss(f.State,1000,0,"missing"),"configuration-missing");
            foreach(Guid ev in new[]{Guid.Empty,f.Rig,f.Hook,locked.SessionId})Refused(f.State,f.State.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,locked,ev,"hook-connection",1000,0),"loss-identity-invalid");
            foreach(var pair in new[]{Tuple.Create(FloatEquipmentGate.Idle,"session-required"),Tuple.Create(FloatEquipmentGate.PendingResult,"result-pending"),Tuple.Create(FloatEquipmentGate.PersistenceUnknown,"persistence-uncertain")})
                Refused(f.State,f.State.PlanTerminalLoss(pair.Item1,locked,G(),"hook-connection",1000,0),pair.Item2);
        });
        Test("loss policy cannot silently destroy rods reels or omit the disconnected hook",()=>{
            foreach(var slots in new[]{new[]{FloatEquipmentSlot.Rod,FloatEquipmentSlot.Hook},new[]{FloatEquipmentSlot.Reel,FloatEquipmentSlot.Hook},new[]{FloatEquipmentSlot.Bobber},new[]{FloatEquipmentSlot.Hook,FloatEquipmentSlot.Hook}})
                Reject<ArgumentException>(()=>new FloatEquipmentLossRule("bad",slots));
        });
        Test("structural ownership refuses duplicate IDs slots orphan spools and foreign rigs",()=>{
            var f=new Fixture();var duplicate=new List<FloatEquipmentInstance>(f.Items);duplicate.Add(f.Items[0]);Reject<ArgumentException>(()=>new FloatEquipmentLedger(f.Rig,1,1,f.State.Catalog,duplicate));
            var slot=new List<FloatEquipmentInstance>(f.Items);slot.Add(Item(G(),"hook-b",FloatEquipmentKind.Hook,FloatEquipmentLocation.RigSlot(f.Rig,FloatEquipmentSlot.Hook)));Reject<ArgumentException>(()=>new FloatEquipmentLedger(f.Rig,1,1,f.State.Catalog,slot));
            foreach(Guid owner in new[]{G(),f.Hook,f.Line}){var items=new List<FloatEquipmentInstance>(f.Items);items[2]=new FloatEquipmentInstance(f.Line,"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.ReelSpool(owner),50000,50000);Reject<ArgumentException>(()=>new FloatEquipmentLedger(f.Rig,1,1,f.State.Catalog,items));}
            var foreign=new[]{Item(G(),"hook",FloatEquipmentKind.Hook,FloatEquipmentLocation.RigSlot(G(),FloatEquipmentSlot.Hook))};Reject<ArgumentException>(()=>new FloatEquipmentLedger(f.Rig,1,1,f.State.Catalog,foreign));
        });
        Test("bounded definitions dimensions and instance collections reject invalid or oversized inputs",()=>{
            Reject<ArgumentOutOfRangeException>(()=>new FloatEquipmentDefinition("line",FloatEquipmentKind.Line,0));
            Reject<ArgumentOutOfRangeException>(()=>new FloatEquipmentDefinition("rod",FloatEquipmentKind.Rod,100));
            Reject<ArgumentException>(()=>new FloatEquipmentDefinition("BAD",FloatEquipmentKind.Hook,100));
            Reject<ArgumentOutOfRangeException>(()=>new FloatEquipmentInstance(G(),"line",FloatEquipmentKind.Line,1,FloatEquipmentLocation.Inventory(),100000001,100000001));
            var rows=new List<FloatEquipmentInstance>();for(int i=0;i<129;i++)rows.Add(Item(G(),"hook",FloatEquipmentKind.Hook,FloatEquipmentLocation.Inventory()));
            Reject<ArgumentException>(()=>new FloatEquipmentLedger(G(),1,1,Catalog(),rows));
            Reject<ArgumentOutOfRangeException>(()=>new FloatEquipmentLedger(G(),0,1,Catalog(),new FloatEquipmentInstance[0]));
        });
        Test("revision overflow rejects the whole candidate without moving any item",()=>{
            var inventory=new Fixture(inventoryRevision:Int64.MaxValue);Refused(inventory.State,Replace(inventory.State,FloatEquipmentSlot.Hook,inventory.SpareHook),"revision-overflow");
            var rig=new Fixture(rigRevision:Int64.MaxValue);Refused(rig.State,Replace(rig.State,FloatEquipmentSlot.Hook,rig.SpareHook),"revision-overflow");
            var item=new Fixture(spareHookRevision:Int64.MaxValue);Refused(item.State,Replace(item.State,FloatEquipmentSlot.Hook,item.SpareHook),"revision-overflow");
            Need(inventory.State.Equipped(FloatEquipmentSlot.Hook).Id==inventory.Hook&&rig.State.Equipped(FloatEquipmentSlot.Hook).Id==rig.Hook,"Overflow published partial work.");
        });
        Test("replacing lost hook restores readiness using shortened existing line without a refund",()=>{
            var f=new Fixture();var loss=Loss(f.State,10000,3000);var changed=Replace(loss.Candidate,FloatEquipmentSlot.Hook,f.SpareHook);Conserved(loss.Candidate,changed);
            Need(Ready(changed.Candidate,43000).Valid&&Has(Ready(changed.Candidate,43001),"insufficient-line")&&changed.Candidate.Find(f.Hook).Location.Place==FloatEquipmentPlace.Lost&&changed.Candidate.Find(f.Line).RemainingLineMm==43000,"Replacement restored lost line or old hook.");
        });
        Test("full line and hook loss can be repaired only with separately owned spare instances",()=>{
            var f=new Fixture();var loss=Loss(f.State,50000,0);var hook=Replace(loss.Candidate,FloatEquipmentSlot.Hook,f.SpareHook);var line=Move(hook.Candidate,f.SpareLine,f.Reel);Conserved(hook.Candidate,line);
            Need(Ready(line.Candidate,20000).Valid&&Has(Ready(line.Candidate,20001),"insufficient-line")&&line.Candidate.LostLineMm==50000&&line.Candidate.Instances.Count==9&&line.Candidate.Find(f.Line).Location.Place==FloatEquipmentPlace.Lost,"Repair created free inventory or line.");
        });
        Test("readiness resource failures are reported without consuming bait stamina or equipment",()=>{
            var f=new Fixture();var check=Ready(f.State,50001,0,1);Need(Has(check,"bait-empty")&&Has(check,"stamina-insufficient")&&Has(check,"insufficient-line")&&f.State.RigRevision==1&&f.State.InventoryRevision==1,"Readiness changed state or omitted conditions.");
            Reject<ArgumentOutOfRangeException>(()=>f.State.EvaluateCastReadiness(FloatEquipmentGate.Idle,0,1,100,2));Reject<ArgumentOutOfRangeException>(()=>f.State.EvaluateCastReadiness((FloatEquipmentGate)99,1,1,100,2));
            Reject<ArgumentException>(()=>f.State.LockForSession(Guid.Empty));Reject<ArgumentException>(()=>f.State.LockForSession(f.Line));
        });
        Test("manually constructed locks cannot target a spare reel line or invalid session identity",()=>{
            var f=new Fixture();var state=Move(f.State,f.SpareLine,f.SpareReel).Candidate;
            Reject<ArgumentException>(()=>new FloatEquipmentSessionLock(G(),state,state.Find(f.SpareLine)));
            Reject<ArgumentException>(()=>new FloatEquipmentSessionLock(Guid.Empty,state,state.Find(f.Line)));
            Reject<ArgumentException>(()=>new FloatEquipmentSessionLock(state.RigId,state,state.Find(f.Line)));
            Reject<ArgumentException>(()=>new FloatEquipmentSessionLock(f.Hook,state,state.Find(f.Line)));
        });
        Console.WriteLine("RESULT "+passed+" passed; "+failed+" failed");return failed==0?0:1;
    }
}
