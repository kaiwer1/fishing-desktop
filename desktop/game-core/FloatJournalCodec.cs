using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FishingDesktop.GameCore
{
    // Private host journal events only. This is NOT a WebView/JSON command
    // deserializer or a generic mutable-state save format. All integers use
    // BinaryWriter little endian; GUIDs use the .NET 16-byte representation.
    internal static class FloatJournalCodec
    {
        internal const int MaximumPayloadBytes=65536;
        const int MaximumStringBytes=256,MaximumSpecies=128,MaximumSaleIds=4096;
        const ushort Schema=1;
        const byte Genesis=1,Command=2,Cast=1,Recall=2,Release=3,Sale=4,HostFish=5,Recovery=6;
        static readonly byte[] Magic={0x46,0x4c,0x54,0x4a}; // FLTJ
        static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        static InvalidDataException Invalid(string message){return new InvalidDataException(message);}

        sealed class Writer : IDisposable
        {
            readonly MemoryStream stream=new MemoryStream();
            readonly BinaryWriter writer;
            internal Writer(){writer=new BinaryWriter(stream,Utf8);}
            void Space(int count){if(count<0||stream.Length>MaximumPayloadBytes-count)throw Invalid("Journal payload exceeds its bound.");}
            internal void Bytes(byte[] value){Space(value.Length);writer.Write(value);}
            internal void Byte(byte value){Space(1);writer.Write(value);}
            internal void U16(ushort value){Space(2);writer.Write(value);}
            internal void I32(int value){Space(4);writer.Write(value);}
            internal void I64(long value){Space(8);writer.Write(value);}
            internal void Guid(Guid value){Bytes(value.ToByteArray());}
            internal void Text(string value)
            {
                if(value==null)throw Invalid("Missing journal text.");
                int count=Utf8.GetByteCount(value);if(count<1||count>MaximumStringBytes)throw Invalid("Journal text length is invalid.");
                Space(2+count);U16((ushort)count);Bytes(Utf8.GetBytes(value));
            }
            internal byte[] Finish(){writer.Flush();return stream.ToArray();}
            public void Dispose(){writer.Dispose();stream.Dispose();}
        }
        sealed class Reader : IDisposable
        {
            readonly MemoryStream stream;
            readonly BinaryReader reader;
            internal Reader(byte[] payload)
            {
                if(payload==null||payload.Length==0||payload.Length>MaximumPayloadBytes)throw Invalid("Journal payload length is invalid.");
                stream=new MemoryStream(payload,false);reader=new BinaryReader(stream,Utf8);
            }
            void Need(int count){if(count<0||stream.Length-stream.Position<count)throw Invalid("Truncated journal payload.");}
            internal byte[] Bytes(int count){Need(count);var result=reader.ReadBytes(count);if(result.Length!=count)throw Invalid("Truncated journal payload.");return result;}
            internal byte Byte(){Need(1);return reader.ReadByte();}
            internal ushort U16(){Need(2);return reader.ReadUInt16();}
            internal int I32(){Need(4);return reader.ReadInt32();}
            internal long I64(){Need(8);return reader.ReadInt64();}
            internal Guid Guid(){return new Guid(Bytes(16));}
            internal string Text(){int count=U16();if(count<1||count>MaximumStringBytes)throw Invalid("Journal text length is invalid.");return Utf8.GetString(Bytes(count));}
            internal void End(){if(stream.Position!=stream.Length)throw Invalid("Trailing journal payload data.");}
            public void Dispose(){reader.Dispose();stream.Dispose();}
        }
        static void Header(Writer writer,byte kind){writer.Bytes(Magic);writer.U16(Schema);writer.Byte(kind);}
        static void Header(Reader reader,byte kind)
        {
            foreach(byte expected in Magic)if(reader.Byte()!=expected)throw Invalid("Unknown journal payload marker.");
            if(reader.U16()!=Schema)throw Invalid("Unsupported journal payload version.");
            if(reader.Byte()!=kind)throw Invalid("Wrong journal payload kind.");
        }
        static T Decode<T>(byte[] payload,byte kind,Func<Reader,T> body)
        {
            try{using(var reader=new Reader(payload)){Header(reader,kind);T result=body(reader);reader.End();return result;}}
            catch(InvalidDataException){throw;}
            catch(Exception error){throw new InvalidDataException("Invalid typed journal payload.",error);}
        }
        static byte[] Encode(byte kind,Action<Writer> body)
        {
            try{using(var writer=new Writer()){Header(writer,kind);body(writer);return writer.Finish();}}
            catch(InvalidDataException){throw;}
            catch(Exception error){throw new InvalidDataException("Invalid typed journal event.",error);}
        }
        internal static byte[] EncodeGenesis(FloatGameState state)
        {
            return Encode(Genesis,writer=>{
                if(state==null||state.Sequence!=0||state.Session!=null||state.Creel.Count!=0||state.Receipts.Count!=0)throw Invalid("Genesis must be an explicit initial profile.");
                var rig=state.Rig;var rules=state.Rules;
                if(rig==null||rules==null||state.RulesVersion!=rules.Version)throw Invalid("Genesis configuration is invalid.");
                writer.Guid(state.ProfileId);writer.Guid(state.BranchId);writer.I64(state.Sequence);writer.I64(state.WalletMinor);writer.I32(state.Stamina);
                writer.Guid(state.BaitStackId);writer.Text(state.BaitDefinitionKey);writer.I32(state.BaitCount);
                writer.Guid(rig.Id);writer.I64(rig.Revision);writer.Text(rig.DefinitionKey);writer.Guid(rig.RodId);writer.Guid(rig.ReelId);writer.Guid(rig.LineId);writer.I64(rig.LineLengthMm);writer.Guid(rig.HookId);writer.Guid(rig.BobberId);writer.Guid(rig.SinkerId);
                writer.I64(rules.Version);writer.Text(rules.RigDefinitionKey);writer.Text(rules.BaitDefinitionKey);writer.I32(rules.CastStaminaCost);writer.I32(rules.MaximumStamina);writer.I32(rules.CreelCapacity);writer.I64(rules.MaximumWalletMinor);
                if(rules.Species.Count<1||rules.Species.Count>MaximumSpecies)throw Invalid("Species rule count is invalid.");
                writer.U16((ushort)rules.Species.Count);
                string previous=null;
                foreach(var row in rules.Species){
                    if(row==null||(previous!=null&&StringComparer.Ordinal.Compare(previous,row.SpeciesKey)>=0))throw Invalid("Species rules are not canonical.");
                    writer.Text(row.SpeciesKey);writer.I64(row.MinimumMassMg);writer.I64(row.MaximumMassMg);writer.I64(row.MinorPerKg);previous=row.SpeciesKey;
                }
            });
        }
        internal static FloatGameState DecodeGenesis(byte[] payload)
        {
            return Decode(payload,Genesis,reader=>{
                Guid profile=reader.Guid(),branch=reader.Guid();long sequence=reader.I64(),wallet=reader.I64();int stamina=reader.I32();
                if(sequence!=0)throw Invalid("Genesis sequence must be zero.");
                Guid bait=reader.Guid();string baitKey=reader.Text();int baitCount=reader.I32();
                Guid rigId=reader.Guid();long rigRevision=reader.I64();string rigKey=reader.Text();Guid rod=reader.Guid(),reel=reader.Guid(),line=reader.Guid();long lineMm=reader.I64();Guid hook=reader.Guid(),bobber=reader.Guid(),sinker=reader.Guid();
                long rulesVersion=reader.I64();string rulesRig=reader.Text(),rulesBait=reader.Text();int cost=reader.I32(),maximumStamina=reader.I32(),capacity=reader.I32();long maximumWallet=reader.I64();
                int count=reader.U16();if(count<1||count>MaximumSpecies)throw Invalid("Species rule count is invalid.");
                var species=new List<FloatSpeciesRule>(count);string previous=null;
                for(int i=0;i<count;i++){
                    string key=reader.Text();if(previous!=null&&StringComparer.Ordinal.Compare(previous,key)>=0)throw Invalid("Duplicate or noncanonical species rule.");
                    long minimum=reader.I64(),maximum=reader.I64(),price=reader.I64();species.Add(new FloatSpeciesRule(key,minimum,maximum,price));previous=key;
                }
                var rig=new FixedFloatRig(rigId,rigRevision,rigKey,rod,reel,line,lineMm,hook,bobber,sinker);
                var rules=new FloatGameRules(rulesVersion,rulesRig,rulesBait,cost,maximumStamina,capacity,maximumWallet,species);
                return FloatGameState.CreateNew(profile,branch,wallet,stamina,bait,baitKey,baitCount,rig,rules);
            });
        }
        static byte Type(FloatCommand command)
        {
            if(command is CastFloat)return Cast;if(command is RecallFloat)return Recall;if(command is ReleaseCurrentFloatFish)return Release;
            if(command is SellFloatFish)return Sale;if(command is HostFloatFish)return HostFish;if(command is RecoverFloatSession)return Recovery;throw Invalid("Unsupported journal command type.");
        }
        static void SaleIds(IList<Guid> ids)
        {
            if(ids.Count<1||ids.Count>MaximumSaleIds)throw Invalid("Sale identity count is invalid.");
            for(int i=0;i<ids.Count;i++)if(ids[i]==Guid.Empty||(i>0&&ids[i-1].CompareTo(ids[i])>=0))throw Invalid("Sale identities are not canonical and unique.");
        }
        internal static byte[] EncodeCommand(FloatCommand command)
        {
            return Encode(Command,writer=>{
                if(command==null)throw Invalid("Missing journal command.");byte type=Type(command);
                writer.Byte(type);writer.Guid(command.ProfileId);writer.Guid(command.BranchId);writer.Guid(command.RequestId);writer.Guid(command.EventId);writer.Guid(command.SessionId);
                writer.I64(command.ExpectedSequence);writer.I64(command.ExpectedRigRevision);writer.I64(command.ExpectedRulesVersion);
                if(type==Sale){
                    if(command.SessionId!=Guid.Empty)throw Invalid("Sale command contains a session.");var ids=((SellFloatFish)command).FishIds;SaleIds(ids);writer.U16((ushort)ids.Count);foreach(var id in ids)writer.Guid(id);
                }else if(type==Recovery){
                    writer.Byte((byte)((RecoverFloatSession)command).Reason);
                }else if(type==HostFish){
                    var host=(HostFloatFish)command;if(host.MassMg<=0)throw Invalid("Host fish mass is invalid.");writer.Guid(host.FishId);writer.Guid(host.EncounterId);writer.Text(host.SpeciesKey);writer.I64(host.MassMg);
                }
            });
        }
        internal static FloatCommand DecodeCommand(byte[] payload)
        {
            return Decode(payload,Command,reader=>{
                byte type=reader.Byte();if(type<Cast||type>Recovery)throw Invalid("Unknown journal command type.");
                Guid profile=reader.Guid(),branch=reader.Guid(),request=reader.Guid(),businessEvent=reader.Guid(),session=reader.Guid();
                long sequence=reader.I64(),rig=reader.I64(),rules=reader.I64();
                if(type==Cast)return (FloatCommand)new CastFloat(profile,branch,request,businessEvent,session,sequence,rig,rules);
                if(type==Recall)return new RecallFloat(profile,branch,request,businessEvent,session,sequence,rig,rules);
                if(type==Release)return new ReleaseCurrentFloatFish(profile,branch,request,businessEvent,session,sequence,rig,rules);
                if(type==Recovery)return new RecoverFloatSession(profile,branch,request,businessEvent,session,sequence,rig,rules,(FloatRecoveryReason)reader.Byte());
                if(type==Sale){
                    if(session!=Guid.Empty)throw Invalid("Sale command contains a session.");int count=reader.U16();if(count<1||count>MaximumSaleIds)throw Invalid("Sale identity count is invalid.");
                    var ids=new List<Guid>(count);for(int i=0;i<count;i++)ids.Add(reader.Guid());SaleIds(ids);return new SellFloatFish(profile,branch,request,businessEvent,ids,sequence,rig,rules);
                }
                Guid fish=reader.Guid(),encounter=reader.Guid();string species=reader.Text();long mass=reader.I64();if(mass<=0)throw Invalid("Host fish mass is invalid.");
                return new HostFloatFish(profile,branch,request,businessEvent,session,fish,encounter,species,mass,sequence,rig,rules);
            });
        }
    }
}
