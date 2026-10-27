using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using FishingDesktop.GameCore;

static class FloatJournalCodecTests
{
    static int passed,failed,nextId;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,1});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Invalid(Action action){try{action();}catch(InvalidDataException){return;}throw new InvalidOperationException("Expected InvalidDataException.");}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static FloatGameRules Rules(int capacity=2,IEnumerable<FloatSpeciesRule> species=null)
    {return new FloatGameRules(1,"float-fixture","worm",4,100,capacity,Int64.MaxValue,species??new[]{new FloatSpeciesRule("carp",100000,2000000,1000),new FloatSpeciesRule("pike",100000,3000000,2000)});}
    static FloatGameState New(bool zero=false,int capacity=2,FloatGameRules rules=null)
    {
        var rig=new FixedFloatRig(G(),3,"float-fixture",G(),G(),G(),125000,G(),G(),G());
        return FloatGameState.CreateNew(G(),G(),zero?0:100,zero?0:100,G(),"worm",zero?0:10,rig,rules??Rules(capacity));
    }
    static CastFloat Cast(FloatGameState s){return new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatFish Host(FloatGameState s){return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,G(),G(),"carp",500000,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static FloatGameState Adopt(FloatGameState state,FloatCommand command)
    {
        var decoded=FloatJournalCodec.DecodeCommand(FloatJournalCodec.EncodeCommand(command));Need(decoded.GetType()==command.GetType()&&decoded.Fingerprint==command.Fingerprint,"Typed command facts changed in transport.");
        var result=FloatGameReducer.Reduce(state,decoded,state.Rules);Need(result.HasCandidate&&result.RequiresConfirmation&&!result.IsReplay,"No candidate to explicitly adopt in pure replay.");return result.Candidate;
    }
    static FloatGameState LandOne(FloatGameState s){var active=Adopt(s,Cast(s));return Adopt(active,Host(active));}
    static byte[] Copy(byte[] data){return (byte[])data.Clone();}
    static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static byte[] Prefix(byte[] data,int count){var copy=new byte[count];Array.Copy(data,copy,count);return copy;}
    static byte[] Extra(byte[] data){var copy=new byte[data.Length+1];Array.Copy(data,copy,data.Length);copy[copy.Length-1]=0x7f;return copy;}
    static int Find(byte[] data,string ascii)
    {
        byte[] key=Encoding.ASCII.GetBytes(ascii);for(int i=0;i<=data.Length-key.Length;i++){
            bool match=true;for(int j=0;j<key.Length;j++)if(data[i+j]!=key[j]){match=false;break;}if(match)return i;
        }throw new InvalidOperationException("Missing protocol fixture text.");
    }
    static void I64(byte[] data,int at,long value){Array.Copy(BitConverter.GetBytes(value),0,data,at,8);}
    static FloatCommand[] Commands(FloatGameState s)
    {
        Guid session=G();return new FloatCommand[]{
            new CastFloat(s.ProfileId,s.BranchId,G(),G(),session,17,3,1),
            new RecallFloat(s.ProfileId,s.BranchId,G(),G(),session,17,3,1),
            new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,G(),G(),session,17,3,1),
            new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new[]{G(),G()},17,3,1),
            new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),session,G(),G(),"carp",1234567,17,3,1)};
    }
    sealed class Unsupported : FloatCommand
    {
        internal Unsupported(FloatGameState s):base("unsupported",s.ProfileId,s.BranchId,G(),G(),G(),0,3,1){}
    }
    static int Main()
    {
        Test("explicit zero-resource genesis round trips every fixed initial asset and rule",()=>{
            var s=New(true);byte[] payload=FloatJournalCodec.EncodeGenesis(s);var copy=FloatJournalCodec.DecodeGenesis(payload);
            Need(copy.ProfileId==s.ProfileId&&copy.BranchId==s.BranchId&&copy.Sequence==0&&copy.WalletMinor==0&&copy.Stamina==0&&copy.BaitCount==0&&copy.BaitStackId==s.BaitStackId&&copy.Session==null&&copy.Creel.Count==0&&copy.Receipts.Count==0,"Genesis silently supplied resources or lost identity.");
            Need(copy.Rig.Id==s.Rig.Id&&copy.Rig.RodId==s.Rig.RodId&&copy.Rig.ReelId==s.Rig.ReelId&&copy.Rig.LineId==s.Rig.LineId&&copy.Rig.HookId==s.Rig.HookId&&copy.Rig.BobberId==s.Rig.BobberId&&copy.Rig.SinkerId==s.Rig.SinkerId&&copy.Rig.LineLengthMm==125000&&copy.Rig.Revision==3&&copy.Rules.Signature==s.Rules.Signature,"Genesis changed explicit assembly or rule facts.");
            Need(Equal(payload,FloatJournalCodec.EncodeGenesis(copy)),"Canonical genesis changed on re-encode.");
        });
        Test("genesis decoding creates independent immutable collections and objects",()=>{
            var rows=new List<FloatSpeciesRule>{new FloatSpeciesRule("carp",100000,2000000,1000)};var s=New(rules:Rules(species:rows));var bytes=FloatJournalCodec.EncodeGenesis(s);var copy=FloatJournalCodec.DecodeGenesis(bytes);rows.Clear();Array.Clear(bytes,0,bytes.Length);
            Need(!Object.ReferenceEquals(s,copy)&&!Object.ReferenceEquals(s.Rig,copy.Rig)&&!Object.ReferenceEquals(s.Rules,copy.Rules)&&copy.Rules.Species.Count==1&&copy.Rules.Species[0].SpeciesKey=="carp"&&copy.BaitCount==10,"Decoded state retained mutable input or shared constructed objects.");
            try{((IList<FloatSpeciesRule>)copy.Rules.Species).Clear();throw new InvalidOperationException("Rules mutable.");}catch(NotSupportedException){}
        });
        Test("noninitial active ended landed and pending states cannot be encoded as genesis",()=>{
            var initial=New(capacity:1);var active=Adopt(initial,Cast(initial));var ended=Adopt(active,new RecallFloat(active.ProfileId,active.BranchId,G(),G(),active.Session.Id,active.Sequence,3,1));var landed=LandOne(ended);var again=Adopt(landed,Cast(landed));var pending=Adopt(again,Host(again));
            foreach(var state in new[]{active,ended,landed,pending})Invalid(()=>FloatJournalCodec.EncodeGenesis(state));
        });
        Test("all five command types preserve common identity versions and exact payload",()=>{
            var s=New();foreach(var command in Commands(s)){
                byte[] bytes=FloatJournalCodec.EncodeCommand(command);var copy=FloatJournalCodec.DecodeCommand(bytes);
                Need(copy.GetType()==command.GetType()&&copy.ProfileId==command.ProfileId&&copy.BranchId==command.BranchId&&copy.RequestId==command.RequestId&&copy.EventId==command.EventId&&copy.SessionId==command.SessionId&&copy.ExpectedSequence==17&&copy.ExpectedRigRevision==3&&copy.ExpectedRulesVersion==1&&copy.Fingerprint==command.Fingerprint,"Typed command fields changed.");
                Need(Equal(bytes,FloatJournalCodec.EncodeCommand(copy)),"Canonical command changed on re-encode.");
                var host=copy as HostFloatFish;if(host!=null){var original=(HostFloatFish)command;Need(host.FishId==original.FishId&&host.EncounterId==original.EncounterId&&host.MassMg==1234567&&host.SpeciesKey=="carp","Host facts changed.");}
            }
        });
        Test("genesis and typed events replay the pure cast land sale chain without double payment",()=>{
            var original=New();var s=FloatJournalCodec.DecodeGenesis(FloatJournalCodec.EncodeGenesis(original));var cast=Cast(s);s=Adopt(s,cast);var host=Host(s);s=Adopt(s,host);var sale=new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new[]{s.Creel[0].Id},s.Sequence,3,1);s=Adopt(s,sale);
            var replay=FloatGameReducer.Reduce(s,FloatJournalCodec.DecodeCommand(FloatJournalCodec.EncodeCommand(sale)),s.Rules);
            Need(s.Sequence==3&&s.BaitCount==9&&s.Stamina==96&&s.WalletMinor==600&&s.Creel.Count==0&&s.Session==null&&replay.IsReplay&&!replay.HasCandidate,"Decoded replay changed confirmed resource conservation.");
        });
        Test("decoded recall preserves spent resources and does not manufacture a fish",()=>{
            var initial=New();var active=Adopt(initial,Cast(initial));var ended=Adopt(active,new RecallFloat(active.ProfileId,active.BranchId,G(),G(),active.Session.Id,active.Sequence,3,1));
            Need(ended.BaitCount==9&&ended.Stamina==96&&ended.Creel.Count==0&&ended.Session==null&&initial.BaitCount==10,"Decoded recall refunded or mutated the source.");
        });
        Test("decoded capacity outcome and explicit release preserve the same pending fish",()=>{
            var s=LandOne(New(capacity:1));Guid old=s.Creel[0].Id;s=Adopt(s,Cast(s));var host=Host(s);s=Adopt(s,host);Need(s.Session.CapacityBlocked&&s.Session.PendingFish.Id==host.FishId,"Decoded host outcome lost its pending fish.");
            s=Adopt(s,new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,3,1));Need(s.Session==null&&s.Creel.Count==1&&s.Creel[0].Id==old&&s.BaitCount==8&&s.Stamina==92&&s.WalletMinor==100,"Decoded release replaced old assets or refunded.");
        });
        Test("sale input order normalizes identically and decoded selection is independently read only",()=>{
            var s=New();Guid a=G(),b=G(),request=G(),sale=G();var selection=new List<Guid>{b,a};var c=new SellFloatFish(s.ProfileId,s.BranchId,request,sale,selection,4,3,1);selection.Clear();selection.Add(G());
            var reordered=new SellFloatFish(s.ProfileId,s.BranchId,request,sale,new[]{a,b},4,3,1);byte[] bytes=FloatJournalCodec.EncodeCommand(c);Need(Equal(bytes,FloatJournalCodec.EncodeCommand(reordered)),"Sale order affected canonical encoding.");var copy=(SellFloatFish)FloatJournalCodec.DecodeCommand(bytes);Array.Clear(bytes,0,bytes.Length);
            Need(copy.FishIds.Count==2&&copy.FishIds[0].CompareTo(copy.FishIds[1])<0&&copy.Fingerprint==c.Fingerprint,"Sale collection retained byte/list input.");try{((IList<Guid>)copy.FishIds).Clear();throw new InvalidOperationException("Sale mutable.");}catch(NotSupportedException){}
        });
        Test("null empty and oversize payloads uniformly reject without fallback",()=>{
            foreach(byte[] bytes in new byte[][]{null,new byte[0],new byte[FloatJournalCodec.MaximumPayloadBytes+1]}){Invalid(()=>FloatJournalCodec.DecodeGenesis(bytes));Invalid(()=>FloatJournalCodec.DecodeCommand(bytes));}
            Invalid(()=>FloatJournalCodec.EncodeGenesis(null));Invalid(()=>FloatJournalCodec.EncodeCommand(null));Invalid(()=>FloatJournalCodec.EncodeCommand(new Unsupported(New())));
        });
        Test("every truncated prefix of valid genesis and every command rejects",()=>{
            var s=New();byte[] genesis=FloatJournalCodec.EncodeGenesis(s);for(int i=0;i<genesis.Length;i++){byte[] bytes=Prefix(genesis,i);Invalid(()=>FloatJournalCodec.DecodeGenesis(bytes));}
            foreach(var c in Commands(s)){byte[] command=FloatJournalCodec.EncodeCommand(c);for(int i=0;i<command.Length;i++){byte[] bytes=Prefix(command,i);Invalid(()=>FloatJournalCodec.DecodeCommand(bytes));}}
        });
        Test("trailing bytes and cross-kind decoding reject otherwise complete records",()=>{
            var s=New();byte[] genesis=FloatJournalCodec.EncodeGenesis(s);Invalid(()=>FloatJournalCodec.DecodeGenesis(Extra(genesis)));Invalid(()=>FloatJournalCodec.DecodeCommand(genesis));
            foreach(var c in Commands(s)){byte[] bytes=FloatJournalCodec.EncodeCommand(c);Invalid(()=>FloatJournalCodec.DecodeCommand(Extra(bytes)));Invalid(()=>FloatJournalCodec.DecodeGenesis(bytes));}
        });
        Test("unknown marker version record kind and command type are explicit errors",()=>{
            var s=New();byte[] genesis=FloatJournalCodec.EncodeGenesis(s);foreach(int offset in new[]{0,4,5,6}){byte[] bytes=Copy(genesis);bytes[offset]=0x7f;Invalid(()=>FloatJournalCodec.DecodeGenesis(bytes));}
            byte[] command=FloatJournalCodec.EncodeCommand(Cast(s));foreach(int offset in new[]{0,4,5,6,7}){byte[] bytes=Copy(command);bytes[offset]=0x7f;Invalid(()=>FloatJournalCodec.DecodeCommand(bytes));}
            byte[] zeroType=Copy(command);zeroType[7]=0;Invalid(()=>FloatJournalCodec.DecodeCommand(zeroType));
        });
        Test("invalid UTF8 encodings cannot be replaced or normalized into keys",()=>{
            byte[] original=FloatJournalCodec.EncodeGenesis(New());int at=Find(original,"carp");foreach(var bad in new[]{new byte[]{0xc0,0xaf,0x61,0x62},new byte[]{0x80,0x61,0x62,0x63},new byte[]{0xed,0xa0,0x80,0x61},new byte[]{0xf4,0x90,0x80,0x80}}){
                byte[] bytes=Copy(original);Array.Copy(bad,0,bytes,at,bad.Length);Invalid(()=>FloatJournalCodec.DecodeGenesis(bytes));
            }
            var host=(HostFloatFish)Commands(New())[4];byte[] command=FloatJournalCodec.EncodeCommand(host);command[Find(command,"carp")]=0xff;Invalid(()=>FloatJournalCodec.DecodeCommand(command));
        });
        Test("empty or overlength text and malformed byte lengths reject before allocation",()=>{
            byte[] original=FloatJournalCodec.EncodeGenesis(New());int at=Find(original,"carp")-2;foreach(ushort length in new ushort[]{0,257,UInt16.MaxValue}){
                byte[] bytes=Copy(original);bytes[at]=(byte)length;bytes[at+1]=(byte)(length>>8);Invalid(()=>FloatJournalCodec.DecodeGenesis(bytes));
            }
        });
        Test("duplicate and unsorted species rules cannot silently collapse or reorder",()=>{
            byte[] original=FloatJournalCodec.EncodeGenesis(New());int carp=Find(original,"carp"),pike=Find(original,"pike");byte[] duplicate=Copy(original);Array.Copy(original,carp,duplicate,pike,4);Invalid(()=>FloatJournalCodec.DecodeGenesis(duplicate));
            byte[] swapped=Copy(original);Array.Copy(original,pike,swapped,carp,4);Array.Copy(original,carp,swapped,pike,4);Invalid(()=>FloatJournalCodec.DecodeGenesis(swapped));
        });
        Test("genesis requires an explicit nonempty species count within its bound",()=>{
            byte[] original=FloatJournalCodec.EncodeGenesis(New());int at=Find(original,"carp")-4;foreach(ushort count in new ushort[]{0,129,UInt16.MaxValue}){byte[] bytes=Copy(original);bytes[at]=(byte)count;bytes[at+1]=(byte)(count>>8);Invalid(()=>FloatJournalCodec.DecodeGenesis(bytes));}
            var rows=new List<FloatSpeciesRule>();for(int i=0;i<128;i++)rows.Add(new FloatSpeciesRule("fish"+i.ToString("000"),1,2,1));var allowed=New(rules:Rules(species:rows));Need(FloatJournalCodec.DecodeGenesis(FloatJournalCodec.EncodeGenesis(allowed)).Rules.Species.Count==128,"Declared species count limit failed.");
            rows.Add(new FloatSpeciesRule("fish128",1,2,1));var excessive=New(rules:Rules(species:rows));Invalid(()=>FloatJournalCodec.EncodeGenesis(excessive));
        });
        Test("mutated noninitial sequence negative wallet or empty identity reject genesis",()=>{
            byte[] original=FloatJournalCodec.EncodeGenesis(New());byte[] sequence=Copy(original);I64(sequence,39,1);Invalid(()=>FloatJournalCodec.DecodeGenesis(sequence));byte[] wallet=Copy(original);I64(wallet,47,-1);Invalid(()=>FloatJournalCodec.DecodeGenesis(wallet));
            byte[] identity=Copy(original);Array.Clear(identity,7,16);Invalid(()=>FloatJournalCodec.DecodeGenesis(identity));
        });
        Test("core constructor failures are wrapped as InvalidDataException",()=>{
            byte[] original=FloatJournalCodec.EncodeGenesis(New());int at=Find(original,"carp")+4;byte[] negativeMinimum=Copy(original);I64(negativeMinimum,at,-1);Invalid(()=>FloatJournalCodec.DecodeGenesis(negativeMinimum));
            byte[] duplicateAsset=Copy(original); // Rig ID and Rod ID are located by the fixed schema prefix.
            using(var memory=new MemoryStream(duplicateAsset))using(var reader=new BinaryReader(memory)){
                memory.Position=75;int baitLength=reader.ReadUInt16();memory.Position+=baitLength+4;int rigAt=(int)memory.Position;memory.Position+=16+8;int rigLength=reader.ReadUInt16();memory.Position+=rigLength;Array.Copy(duplicateAsset,rigAt,duplicateAsset,(int)memory.Position,16);
            }Invalid(()=>FloatJournalCodec.DecodeGenesis(duplicateAsset));
        });
        Test("negative versions and empty required command identities reject",()=>{
            byte[] original=FloatJournalCodec.EncodeCommand(Cast(New()));foreach(int at in new[]{8,24,40,56,72}){byte[] bytes=Copy(original);Array.Clear(bytes,at,16);Invalid(()=>FloatJournalCodec.DecodeCommand(bytes));}
            foreach(int at in new[]{88,96,104}){byte[] bytes=Copy(original);I64(bytes,at,-1);Invalid(()=>FloatJournalCodec.DecodeCommand(bytes));}
        });
        Test("sales reject noncanonical duplicate empty or excessive identities",()=>{
            var s=New();var sale=(SellFloatFish)Commands(s)[3];byte[] original=FloatJournalCodec.EncodeCommand(sale);byte[] duplicate=Copy(original);Array.Copy(original,114,duplicate,130,16);Invalid(()=>FloatJournalCodec.DecodeCommand(duplicate));
            byte[] unsorted=Copy(original);Array.Copy(original,114,unsorted,130,16);Array.Copy(original,130,unsorted,114,16);Invalid(()=>FloatJournalCodec.DecodeCommand(unsorted));
            byte[] emptyId=Copy(original);Array.Clear(emptyId,114,16);Invalid(()=>FloatJournalCodec.DecodeCommand(emptyId));
            foreach(ushort count in new ushort[]{0,4097,UInt16.MaxValue}){byte[] bytes=Copy(original);bytes[112]=(byte)count;bytes[113]=(byte)(count>>8);Invalid(()=>FloatJournalCodec.DecodeCommand(bytes));}
            Invalid(()=>FloatJournalCodec.EncodeCommand(new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new Guid[0],0,3,1)));
            Guid id=G();Invalid(()=>FloatJournalCodec.EncodeCommand(new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),new[]{id,id},0,3,1)));
        });
        Test("overall payload cap applies even when sale count is within its local limit",()=>{
            var s=New();var ids=new List<Guid>();for(int i=0;i<4088;i++)ids.Add(G());var allowed=new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),ids,0,3,1);byte[] bytes=FloatJournalCodec.EncodeCommand(allowed);
            Need(bytes.Length<=FloatJournalCodec.MaximumPayloadBytes&&((SellFloatFish)FloatJournalCodec.DecodeCommand(bytes)).FishIds.Count==4088,"Bounded large sale failed.");
            ids.Add(G());var tooLarge=new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),ids,0,3,1);Invalid(()=>FloatJournalCodec.EncodeCommand(tooLarge));
            while(ids.Count<4097)ids.Add(G());Invalid(()=>FloatJournalCodec.EncodeCommand(new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),ids,0,3,1)));
        });
        Test("sale session bytes and invalid host facts cannot be silently discarded",()=>{
            var commands=Commands(New());byte[] sale=FloatJournalCodec.EncodeCommand(commands[3]);sale[72]=1;Invalid(()=>FloatJournalCodec.DecodeCommand(sale));
            byte[] host=FloatJournalCodec.EncodeCommand(commands[4]);byte[] emptyFish=Copy(host);Array.Clear(emptyFish,112,16);Invalid(()=>FloatJournalCodec.DecodeCommand(emptyFish));
            foreach(long mass in new[]{0L,-1L}){byte[] bytes=Copy(host);I64(bytes,bytes.Length-8,mass);Invalid(()=>FloatJournalCodec.DecodeCommand(bytes));}
        });
        Test("codec is an internal typed event helper and has no public deserialization endpoint",()=>{
            Need(typeof(FloatJournalCodec).IsNotPublic&&typeof(FloatJournalCodec).GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly).Length==0,"Journal decoder became a public command endpoint.");
            Need(FloatJournalCodec.MaximumPayloadBytes==65536,"Payload bound changed.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed; schema1 in-memory codec and pure reducer replay only, no persistence or UI.");return failed==0?0:1;
    }
}
