using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FishingDesktop.GameCore;

static class FloatEquipmentCodecTests
{
    static int passed,failed,nextId;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,37});}
    static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void Bad(Action action){try{action();}catch(InvalidDataException){return;}throw new InvalidOperationException("Expected InvalidDataException.");}
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
    static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static byte[] Change(byte[] bytes,int offset,params byte[] value){var copy=(byte[])bytes.Clone();Array.Copy(value,0,copy,offset,value.Length);return copy;}
    static byte[] I64(byte[] bytes,int offset,long value){return Change(bytes,offset,BitConverter.GetBytes(value));}
    static byte[] I32(byte[] bytes,int offset,int value){return Change(bytes,offset,BitConverter.GetBytes(value));}
    static void InvalidBytes(byte[] bytes){Bad(()=>FloatEquipmentCodec.Decode(bytes));}
    static FloatEquipmentCatalog Catalog(bool reversed=false)
    {
        var definitions=new List<FloatEquipmentDefinition>{new FloatEquipmentDefinition("rod",FloatEquipmentKind.Rod,200000,0,1000,20000),
            new FloatEquipmentDefinition("reel",FloatEquipmentKind.Reel,200000),new FloatEquipmentDefinition("reel-b",FloatEquipmentKind.Reel,210000),
            new FloatEquipmentDefinition("line",FloatEquipmentKind.Line,0,200),new FloatEquipmentDefinition("hook",FloatEquipmentKind.Hook,500),
            new FloatEquipmentDefinition("hook-b",FloatEquipmentKind.Hook,600),new FloatEquipmentDefinition("bobber",FloatEquipmentKind.Bobber,1000,0,500,8000),new FloatEquipmentDefinition("sinker",FloatEquipmentKind.Sinker,3000)};
        var pairs=new List<FloatEquipmentPairRule>{new FloatEquipmentPairRule("rod","reel",true),new FloatEquipmentPairRule("rod","reel-b",true)};
        var spools=new List<FloatEquipmentSpoolRule>{new FloatEquipmentSpoolRule("reel","line",200,50000),new FloatEquipmentSpoolRule("reel-b","line",200,60000)};
        var hooks=new List<FloatEquipmentPairRule>{new FloatEquipmentPairRule("hook","worm",true),new FloatEquipmentPairRule("hook-b","worm",true)};
        var losses=new List<FloatEquipmentLossRule>{new FloatEquipmentLossRule("hook-loss",new[]{FloatEquipmentSlot.Hook}),new FloatEquipmentLossRule("terminal-loss",new[]{FloatEquipmentSlot.Sinker,FloatEquipmentSlot.Hook,FloatEquipmentSlot.Bobber})};
        if(reversed){definitions.Reverse();pairs.Reverse();spools.Reverse();hooks.Reverse();losses.Reverse();}
        return new FloatEquipmentCatalog(9,"worm",definitions,pairs,spools,hooks,losses);
    }
    sealed class Fixture
    {
        internal readonly Guid Rig=G(),Rod=G(),Reel=G(),Line=G(),Hook=G(),Bobber=G(),Sinker=G(),SpareHook=G(),SpareReel=G(),SpareLine=G();
        internal readonly FloatEquipmentLedger State;
        internal Fixture()
        {
            State=new FloatEquipmentLedger(Rig,3,8,Catalog(),new[]{
                new FloatEquipmentInstance(Rod,"rod",FloatEquipmentKind.Rod,2,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Rod)),
                new FloatEquipmentInstance(Reel,"reel",FloatEquipmentKind.Reel,3,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Reel)),
                new FloatEquipmentInstance(Line,"line",FloatEquipmentKind.Line,4,FloatEquipmentLocation.ReelSpool(Reel),50000,50000),
                new FloatEquipmentInstance(Hook,"hook",FloatEquipmentKind.Hook,5,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Hook)),
                new FloatEquipmentInstance(Bobber,"bobber",FloatEquipmentKind.Bobber,6,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Bobber)),
                new FloatEquipmentInstance(Sinker,"sinker",FloatEquipmentKind.Sinker,7,FloatEquipmentLocation.RigSlot(Rig,FloatEquipmentSlot.Sinker)),
                new FloatEquipmentInstance(SpareHook,"hook-b",FloatEquipmentKind.Hook,8,FloatEquipmentLocation.Inventory()),
                new FloatEquipmentInstance(SpareReel,"reel-b",FloatEquipmentKind.Reel,9,FloatEquipmentLocation.Inventory()),
                new FloatEquipmentInstance(SpareLine,"line",FloatEquipmentKind.Line,10,FloatEquipmentLocation.Inventory(),20000,20000)});
        }
    }
    sealed class Row
    {
        internal int Start,End,Key,KeyLength,Kind,Mass,Diameter,Minimum,Maximum,Id,Revision,Place,Owner,Slot,Initial,Remaining,Allowed,SlotCount,Slots;
        internal Guid Identity;
    }
    sealed class Layout
    {
        internal int CatalogVersion,BaitKeyLength,BaitKey,DefinitionCount,PairCount,SpoolCount,HookCount,LossCount,RigId,RigRevision,InventoryRevision,InstanceCount;
        internal readonly List<Row> Definitions=new List<Row>(),Pairs=new List<Row>(),Spools=new List<Row>(),Hooks=new List<Row>(),Losses=new List<Row>(),Items=new List<Row>();
        internal Row Item(Guid id){foreach(var row in Items)if(row.Identity==id)return row;throw new InvalidOperationException("Missing test item.");}
    }
    static int Pos(BinaryReader r){return (int)r.BaseStream.Position;}
    static void Key(BinaryReader r,Row row){row.KeyLength=Pos(r);int n=r.ReadUInt16();row.Key=Pos(r);r.ReadBytes(n);}
    static void Pairs(BinaryReader r,List<Row> rows){int n=r.ReadUInt16();for(int i=0;i<n;i++){var row=new Row{Start=Pos(r)};Key(r,row);var ignored=new Row();Key(r,ignored);row.Allowed=Pos(r);r.ReadByte();row.End=Pos(r);rows.Add(row);}}
    // Offset inventory for mutations of our own valid fixtures, not a second
    // production decoder. The tested decoder remains the only validator.
    static Layout Inspect(byte[] bytes)
    {
        var l=new Layout();using(var r=new BinaryReader(new MemoryStream(bytes))){r.ReadBytes(6);l.CatalogVersion=Pos(r);r.ReadInt64();l.BaitKeyLength=Pos(r);int bait=r.ReadUInt16();l.BaitKey=Pos(r);r.ReadBytes(bait);
            l.DefinitionCount=Pos(r);int n=r.ReadUInt16();for(int i=0;i<n;i++){var row=new Row{Start=Pos(r)};Key(r,row);row.Kind=Pos(r);r.ReadByte();row.Mass=Pos(r);r.ReadInt64();row.Diameter=Pos(r);r.ReadInt32();row.Minimum=Pos(r);r.ReadInt64();row.Maximum=Pos(r);r.ReadInt64();row.End=Pos(r);l.Definitions.Add(row);}
            l.PairCount=Pos(r);Pairs(r,l.Pairs);l.SpoolCount=Pos(r);n=r.ReadUInt16();for(int i=0;i<n;i++){var row=new Row{Start=Pos(r)};Key(r,row);var ignored=new Row();Key(r,ignored);row.Diameter=Pos(r);r.ReadInt32();row.Maximum=Pos(r);r.ReadInt64();row.End=Pos(r);l.Spools.Add(row);}
            l.HookCount=Pos(r);Pairs(r,l.Hooks);l.LossCount=Pos(r);n=r.ReadUInt16();for(int i=0;i<n;i++){var row=new Row{Start=Pos(r)};Key(r,row);row.SlotCount=Pos(r);int slots=r.ReadByte();row.Slots=Pos(r);r.ReadBytes(slots);row.End=Pos(r);l.Losses.Add(row);}
            l.RigId=Pos(r);r.ReadBytes(16);l.RigRevision=Pos(r);r.ReadInt64();l.InventoryRevision=Pos(r);r.ReadInt64();l.InstanceCount=Pos(r);n=r.ReadUInt16();for(int i=0;i<n;i++){var row=new Row{Start=Pos(r),Id=Pos(r)};row.Identity=new Guid(r.ReadBytes(16));Key(r,row);row.Kind=Pos(r);r.ReadByte();row.Revision=Pos(r);r.ReadInt64();row.Place=Pos(r);r.ReadByte();row.Owner=Pos(r);r.ReadBytes(16);row.Slot=Pos(r);r.ReadByte();row.Initial=Pos(r);r.ReadInt64();row.Remaining=Pos(r);r.ReadInt64();row.End=Pos(r);l.Items.Add(row);}
            Need(Pos(r)==bytes.Length,"Test layout differs from payload.");}return l;
    }
    static byte[] Swap(byte[] input,Row first,Row second)
    {
        Need(first.End==second.Start,"Swap rows must be adjacent.");var output=(byte[])input.Clone();int a=first.End-first.Start,b=second.End-second.Start;
        Array.Copy(input,second.Start,output,first.Start,b);Array.Copy(input,first.Start,output,first.Start+b,a);return output;
    }
    static FloatEquipmentLedger RoundTrip(FloatEquipmentLedger ledger)
    {var bytes=FloatEquipmentCodec.Encode(ledger);var copy=FloatEquipmentCodec.Decode(bytes);Need(Equal(bytes,FloatEquipmentCodec.Encode(copy)),"Payload lost information or changed canonical form.");return copy;}
    static FloatEquipmentCatalog LargeCatalog(int relations)
    {
        var definitions=new List<FloatEquipmentDefinition>();var rods=new List<string>();var reels=new List<string>();var lines=new List<string>();
        for(int i=0;i<16;i++){
            string suffix=i.ToString("d2")+new string('x',90);string rod="rod-"+suffix,reel="ree-"+suffix,line="lin-"+suffix;rods.Add(rod);reels.Add(reel);lines.Add(line);
            definitions.Add(new FloatEquipmentDefinition(rod,FloatEquipmentKind.Rod,1000,0,1,10000));definitions.Add(new FloatEquipmentDefinition(reel,FloatEquipmentKind.Reel,1000));definitions.Add(new FloatEquipmentDefinition(line,FloatEquipmentKind.Line,0,200));}
        var pairs=new List<FloatEquipmentPairRule>();var spools=new List<FloatEquipmentSpoolRule>();for(int i=0;i<relations;i++){pairs.Add(new FloatEquipmentPairRule(rods[i/16],reels[i%16],true));spools.Add(new FloatEquipmentSpoolRule(reels[i/16],lines[i%16],200,100000));}
        var losses=new List<FloatEquipmentLossRule>();for(int i=0;i<relations;i++)losses.Add(new FloatEquipmentLossRule("loss"+i.ToString("d3")+new string('x',89),new[]{FloatEquipmentSlot.Hook}));
        return new FloatEquipmentCatalog(1,"worm",definitions,pairs,spools,new FloatEquipmentPairRule[0],losses);
    }
    static int Main()
    {
        Test("complete assembly roundtrip preserves catalogue identity values instances ownership and revisions",()=>{
            var f=new Fixture();var s=RoundTrip(f.State);Need(s.RigId==f.Rig&&s.RigRevision==3&&s.InventoryRevision==8&&s.RulesVersion==9&&s.Instances.Count==9&&s.Find(f.Line).Revision==4&&s.Spool(f.Reel).Id==f.Line,"Identity or revision changed.");
            Need(s.Catalog.Definitions.Count==8&&s.Catalog.RodReelRules.Count==2&&s.Catalog.SpoolRules.Count==2&&s.Catalog.HookBaitRules.Count==2&&s.Catalog.LossRules.Count==2&&s.InitialLineMm==70000&&s.RemainingLineMm==70000,"Catalogue or line budget lost.");
        });
        Test("legal missing hook and missing reel remain editable through roundtrip",()=>{
            var f=new Fixture();var noHook=f.State.ReplaceSlot(FloatEquipmentGate.Idle,3,8,FloatEquipmentSlot.Hook,null).Candidate;
            var s=RoundTrip(noHook);Need(s.ValidateAssembly().Valid&&s.Equipped(FloatEquipmentSlot.Hook)==null&&!s.EvaluateCastReadiness(FloatEquipmentGate.Idle,1,1,100,2).Valid,"Missing hook was repaired or rejected.");
            var noReel=s.ReplaceSlot(FloatEquipmentGate.Idle,s.RigRevision,s.InventoryRevision,FloatEquipmentSlot.Reel,null).Candidate;s=RoundTrip(noReel);
            Need(s.Spool(f.Reel).Id==f.Line&&s.Find(f.Reel).Location.Place==FloatEquipmentPlace.Inventory,"Detached loaded reel lost line ownership.");
        });
        Test("partial loss roundtrip preserves exact remaining initial and loss provenance",()=>{
            var f=new Fixture();Guid ev=G();var changed=f.State.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,f.State.LockForSession(G()),ev,"hook-loss",10000,3000).Candidate;var s=RoundTrip(changed);
            Need(s.Find(f.Line).InitialLineMm==50000&&s.Find(f.Line).RemainingLineMm==43000&&s.LostLineMm==7000&&s.Find(f.Hook).Location.Place==FloatEquipmentPlace.Lost&&s.Find(f.Hook).Location.OwnerId==ev,"Partial loss was reset or misattributed.");
        });
        Test("zero remaining line tombstone roundtrip cannot be reused as inventory",()=>{
            var f=new Fixture();Guid ev=G();var changed=f.State.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,f.State.LockForSession(G()),ev,"terminal-loss",50000,0).Candidate;var s=RoundTrip(changed);
            Need(s.Find(f.Line).RemainingLineMm==0&&s.Find(f.Line).InitialLineMm==50000&&s.Find(f.Line).Location.OwnerId==ev&&s.Find(f.Line).Location.Place==FloatEquipmentPlace.Lost&&s.Spool(f.Reel)==null&&s.Instances.Count==9,"Lost line was dropped or refilled.");
            Need(!s.MoveLine(FloatEquipmentGate.Idle,s.RigRevision,s.InventoryRevision,f.Line,f.Reel).Accepted,"Decoded lost line became reusable.");
        });
        Test("empty but legal assembly payload is accepted without starter equipment",()=>{
            var empty=new FloatEquipmentLedger(G(),1,1,Catalog(),new FloatEquipmentInstance[0]);var s=RoundTrip(empty);Need(s.Instances.Count==0&&s.ValidateAssembly().Valid&&!s.EvaluateCastReadiness(FloatEquipmentGate.Idle,1,1,100,2).Valid,"Decoder invented a default assembly.");
        });
        Test("collection permutations encode to identical canonical bytes",()=>{
            var f=new Fixture();var items=new List<FloatEquipmentInstance>(f.State.Instances);items.Reverse();var reordered=new FloatEquipmentLedger(f.Rig,3,8,Catalog(true),items);
            Need(Equal(FloatEquipmentCodec.Encode(f.State),FloatEquipmentCodec.Encode(reordered)),"Enumeration order changed bytes.");
        });
        Test("encoded buffers and decoded objects share no mutable backing data",()=>{
            var f=new Fixture();byte[] bytes=FloatEquipmentCodec.Encode(f.State);var copy=FloatEquipmentCodec.Decode(bytes);Array.Clear(bytes,0,bytes.Length);
            Need(copy.Find(f.Line).RemainingLineMm==50000&&copy.ValidateAssembly().Valid&&RoundTrip(f.State).RigId==f.Rig,"Buffer mutation changed retained state.");
            Need(!Object.ReferenceEquals(copy.Catalog,f.State.Catalog)&&!Object.ReferenceEquals(copy.Find(f.Hook),f.State.Find(f.Hook)),"Decoder reused mutable caller objects.");
        });
        Test("all strict prefixes of a complete payload reject as truncated",()=>{
            byte[] bytes=FloatEquipmentCodec.Encode(new Fixture().State);for(int length=0;length<bytes.Length;length++){var part=new byte[length];Array.Copy(bytes,part,length);InvalidBytes(part);}
            Console.WriteLine("TRUNCATION "+bytes.Length+" strict prefixes rejected");
        });
        Test("unknown marker versions null and payloads beyond 64 KiB are rejected",()=>{
            byte[] bytes=FloatEquipmentCodec.Encode(new Fixture().State);InvalidBytes(null);InvalidBytes(Change(bytes,0,0));InvalidBytes(Change(bytes,4,0,0));InvalidBytes(Change(bytes,4,2,0));
            InvalidBytes(new byte[FloatEquipmentCodec.MaximumPayloadBytes+1]);Bad(()=>FloatEquipmentCodec.Encode(null));
        });
        Test("trailing data including a second complete payload is rejected",()=>{
            byte[] bytes=FloatEquipmentCodec.Encode(new Fixture().State);var extra=new byte[bytes.Length+1];Array.Copy(bytes,extra,bytes.Length);InvalidBytes(extra);
            var joined=new byte[bytes.Length*2];Array.Copy(bytes,joined,bytes.Length);Array.Copy(bytes,0,joined,bytes.Length,bytes.Length);InvalidBytes(joined);
        });
        Test("UTF8 invalid sequences and invalid key characters are never normalized",()=>{
            byte[] bytes=FloatEquipmentCodec.Encode(new Fixture().State);var l=Inspect(bytes);
            InvalidBytes(Change(bytes,l.BaitKey,0xff));InvalidBytes(Change(bytes,l.BaitKey,0xc0,0xaf));InvalidBytes(Change(bytes,l.BaitKey,0xed,0xa0,0x80));
            InvalidBytes(Change(bytes,l.BaitKey,(byte)'W'));InvalidBytes(Change(bytes,l.BaitKey,0));InvalidBytes(Change(bytes,l.BaitKey,(byte)' '));
        });
        Test("key length limits reject empty oversized and truncated strings before allocation",()=>{
            byte[] bytes=FloatEquipmentCodec.Encode(new Fixture().State);var l=Inspect(bytes);
            InvalidBytes(Change(bytes,l.BaitKeyLength,0,0));InvalidBytes(Change(bytes,l.BaitKeyLength,97,0));InvalidBytes(Change(bytes,l.BaitKeyLength,255,255));
        });
        Test("every catalogue and instance collection has a hard count bound",()=>{
            byte[] bytes=FloatEquipmentCodec.Encode(new Fixture().State);var l=Inspect(bytes);
            foreach(int position in new[]{l.DefinitionCount,l.PairCount,l.SpoolCount,l.HookCount,l.LossCount,l.InstanceCount}){InvalidBytes(Change(bytes,position,129,0));InvalidBytes(Change(bytes,position,255,255));}
        });
        Test("definitions and instances reject unknown enum values",()=>{
            var f=new Fixture();byte[] bytes=FloatEquipmentCodec.Encode(f.State);var l=Inspect(bytes);
            InvalidBytes(Change(bytes,l.Definitions[0].Kind,255));InvalidBytes(Change(bytes,l.Item(f.Hook).Kind,255));InvalidBytes(Change(bytes,l.Item(f.Hook).Place,255));InvalidBytes(Change(bytes,l.Item(f.Hook).Slot,255));
        });
        Test("encoded compatibility booleans accept only zero or one",()=>{
            byte[] bytes=FloatEquipmentCodec.Encode(new Fixture().State);var l=Inspect(bytes);InvalidBytes(Change(bytes,l.Pairs[0].Allowed,2));InvalidBytes(Change(bytes,l.Hooks[0].Allowed,255));
        });
        Test("numeric catalogue ranges and revisions cannot overflow or become nonpositive",()=>{
            var f=new Fixture();byte[] bytes=FloatEquipmentCodec.Encode(f.State);var l=Inspect(bytes);
            InvalidBytes(I64(bytes,l.CatalogVersion,0));InvalidBytes(I64(bytes,l.RigRevision,0));InvalidBytes(I64(bytes,l.InventoryRevision,-1));InvalidBytes(I64(bytes,l.Item(f.Hook).Revision,Int64.MinValue));
            InvalidBytes(I64(bytes,l.Definitions[0].Mass,Int64.MaxValue));InvalidBytes(I32(bytes,l.Spools[0].Diameter,-1));InvalidBytes(I64(bytes,l.Spools[0].Maximum,Int64.MaxValue));
        });
        Test("negative or impossible line lengths and nonline length fields are rejected",()=>{
            var f=new Fixture();byte[] bytes=FloatEquipmentCodec.Encode(f.State);var l=Inspect(bytes);var line=l.Item(f.Line);
            InvalidBytes(I64(bytes,line.Initial,0));InvalidBytes(I64(bytes,line.Remaining,-1));InvalidBytes(I64(bytes,line.Remaining,50001));InvalidBytes(I64(bytes,line.Remaining,0));InvalidBytes(I64(bytes,l.Item(f.Hook).Initial,1));
        });
        Test("duplicate asset IDs rig ID reuse and noncanonical instance order reject",()=>{
            var f=new Fixture();byte[] bytes=FloatEquipmentCodec.Encode(f.State);var l=Inspect(bytes);
            InvalidBytes(Change(bytes,l.Items[1].Id,l.Items[0].Identity.ToByteArray()));InvalidBytes(Change(bytes,l.Item(f.Rod).Id,f.Rig.ToByteArray()));InvalidBytes(Swap(bytes,l.Items[0],l.Items[1]));InvalidBytes(Change(bytes,l.Item(f.Rod).Id,Guid.Empty.ToByteArray()));
        });
        Test("decoder rejects collection reordering instead of silently sorting malformed bytes",()=>{
            byte[] bytes=FloatEquipmentCodec.Encode(new Fixture().State);var l=Inspect(bytes);
            foreach(var rows in new[]{l.Definitions,l.Pairs,l.Spools,l.Hooks,l.Losses})InvalidBytes(Swap(bytes,rows[0],rows[1]));
        });
        Test("duplicate definition and relation records are refused",()=>{
            byte[] bytes=FloatEquipmentCodec.Encode(new Fixture().State);var l=Inspect(bytes);
            // Duplicate a row and remove the original adjacent row, keeping
            // the count intact even when encoded keys have different sizes.
            foreach(var rows in new[]{l.Definitions,l.Pairs,l.Spools,l.Hooks,l.Losses}){
                var a=rows[0];var b=rows[1];int delta=(a.End-a.Start)-(b.End-b.Start);var copy=new byte[bytes.Length+delta];
                Array.Copy(bytes,0,copy,0,b.Start);Array.Copy(bytes,a.Start,copy,b.Start,a.End-a.Start);Array.Copy(bytes,b.End,copy,b.Start+a.End-a.Start,bytes.Length-b.End);InvalidBytes(copy);
            }
        });
        Test("illegal inventory owner slots and non-rig slot bytes reject",()=>{
            var f=new Fixture();byte[] bytes=FloatEquipmentCodec.Encode(f.State);var l=Inspect(bytes);
            InvalidBytes(Change(bytes,l.Item(f.SpareHook).Owner,f.Rig.ToByteArray()));InvalidBytes(Change(bytes,l.Item(f.SpareHook).Slot,(byte)FloatEquipmentSlot.Hook));
            InvalidBytes(Change(bytes,l.Item(f.Line).Slot,(byte)FloatEquipmentSlot.Reel));InvalidBytes(Change(bytes,l.Item(f.Hook).Owner,Guid.Empty.ToByteArray()));
        });
        Test("duplicate rig slots and foreign rig references reject through structural validation",()=>{
            var f=new Fixture();byte[] bytes=FloatEquipmentCodec.Encode(f.State);var l=Inspect(bytes);var hook=l.Item(f.SpareHook);
            var duplicate=Change(bytes,hook.Place,(byte)FloatEquipmentPlace.RigSlot);duplicate=Change(duplicate,hook.Owner,f.Rig.ToByteArray());duplicate=Change(duplicate,hook.Slot,(byte)FloatEquipmentSlot.Hook);InvalidBytes(duplicate);
            InvalidBytes(Change(bytes,l.Item(f.Hook).Owner,G().ToByteArray()));
        });
        Test("orphan wrong-kind and duplicate spool parents reject through structural validation",()=>{
            var f=new Fixture();byte[] bytes=FloatEquipmentCodec.Encode(f.State);var l=Inspect(bytes);
            InvalidBytes(Change(bytes,l.Item(f.Line).Owner,G().ToByteArray()));InvalidBytes(Change(bytes,l.Item(f.Line).Owner,f.Hook.ToByteArray()));
            var duplicate=Change(bytes,l.Item(f.SpareLine).Place,(byte)FloatEquipmentPlace.ReelSpool);duplicate=Change(duplicate,l.Item(f.SpareLine).Owner,f.Reel.ToByteArray());InvalidBytes(duplicate);
        });
        Test("valid binary shape with incompatible equipment still fails shared assembly validation",()=>{
            var f=new Fixture();byte[] bytes=FloatEquipmentCodec.Encode(f.State);var l=Inspect(bytes);
            InvalidBytes(Change(bytes,l.Pairs[0].Allowed,0));InvalidBytes(Change(bytes,l.Hooks[0].Allowed,0));InvalidBytes(I64(bytes,l.Spools[0].Maximum,49999));
            InvalidBytes(Change(bytes,l.Item(f.Hook).Key,Encoding.ASCII.GetBytes("none")));
        });
        Test("encoder refuses invalid configuration rather than writing a payload decoder will reject",()=>{
            var f=new Fixture();var rows=new List<FloatEquipmentInstance>(f.State.Instances);for(int i=0;i<rows.Count;i++)if(rows[i].Id==f.SpareHook)rows[i]=new FloatEquipmentInstance(f.SpareHook,"unknown",FloatEquipmentKind.Hook,8,FloatEquipmentLocation.Inventory());
            var invalid=new FloatEquipmentLedger(f.Rig,3,8,f.State.Catalog,rows);Need(!invalid.ValidateAssembly().Valid,"Fixture is not invalid.");Bad(()=>FloatEquipmentCodec.Encode(invalid));
        });
        Test("terminal loss policy counts ordering slots and required hook are strict",()=>{
            byte[] bytes=FloatEquipmentCodec.Encode(new Fixture().State);var l=Inspect(bytes);
            InvalidBytes(Change(bytes,l.Losses[0].SlotCount,0));InvalidBytes(Change(bytes,l.Losses[0].SlotCount,4));InvalidBytes(Change(bytes,l.Losses[0].Slots,(byte)FloatEquipmentSlot.Rod));
            InvalidBytes(Change(bytes,l.Losses[0].Slots,(byte)FloatEquipmentSlot.Sinker));InvalidBytes(Change(bytes,l.Losses[1].Slots+1,(byte)FloatEquipmentSlot.Hook));
        });
        Test("lost line payload cannot claim nonzero remaining line or an asset as loss event",()=>{
            var f=new Fixture();var state=f.State.PlanTerminalLoss(FloatEquipmentGate.ActiveSession,f.State.LockForSession(G()),G(),"terminal-loss",50000,0).Candidate;
            byte[] bytes=FloatEquipmentCodec.Encode(state);var l=Inspect(bytes);InvalidBytes(I64(bytes,l.Item(f.Line).Remaining,1));InvalidBytes(Change(bytes,l.Item(f.Hook).Owner,f.Hook.ToByteArray()));
        });
        Test("large valid payload roundtrips while oversized valid catalogue is refused by total budget",()=>{
            var large=new FloatEquipmentLedger(G(),1,1,LargeCatalog(100),new FloatEquipmentInstance[0]);byte[] bytes=FloatEquipmentCodec.Encode(large);Need(bytes.Length>40000&&bytes.Length<=65536,"Large fixture did not exercise total budget.");RoundTrip(large);
            var oversized=new FloatEquipmentLedger(G(),1,1,LargeCatalog(128),new FloatEquipmentInstance[0]);Need(oversized.ValidateAssembly().Valid,"Oversized fixture is structurally invalid.");Bad(()=>FloatEquipmentCodec.Encode(oversized));
            Console.WriteLine("LARGE valid payload "+bytes.Length+" bytes; oversized catalogue refused");
        });
        Test("maximum instance count is losslessly encoded without dropping retained identities",()=>{
            var f=new Fixture();var rows=new List<FloatEquipmentInstance>(f.State.Instances);while(rows.Count<128)rows.Add(new FloatEquipmentInstance(G(),"hook-b",FloatEquipmentKind.Hook,1,FloatEquipmentLocation.Inventory()));
            var s=RoundTrip(new FloatEquipmentLedger(f.Rig,3,8,f.State.Catalog,rows));Need(s.Instances.Count==128&&s.Find(rows[127].Id)!=null,"Instance limit was truncated.");
        });
        Test("mutated payloads either reject or re-encode exactly with validated ownership",()=>{
            byte[] bytes=FloatEquipmentCodec.Encode(new Fixture().State);uint random=0x5bd1e995;int rejected=0,accepted=0;
            for(int i=0;i<1000;i++){random=unchecked(random*1664525+1013904223);int offset=(int)(random%(uint)bytes.Length);random=unchecked(random*1664525+1013904223);var mutated=Change(bytes,offset,(byte)(bytes[offset]^(1<<(int)(random%8))));
                try{var state=FloatEquipmentCodec.Decode(mutated);Need(state.ValidateAssembly().Valid&&Equal(mutated,FloatEquipmentCodec.Encode(state)),"Decoder normalized or accepted invalid mutated bytes.");accepted++;}catch(InvalidDataException){rejected++;}}
            Need(rejected>0&&accepted>0,"Mutation check did not exercise both valid and invalid outcomes.");Console.WriteLine("MUTATION "+rejected+" rejected; "+accepted+" valid canonical payloads");
        });
        Console.WriteLine("RESULT "+passed+" passed; "+failed+" failed");return failed==0?0:1;
    }
}
