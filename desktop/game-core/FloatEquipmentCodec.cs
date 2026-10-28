using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FishingDesktop.GameCore
{
    // A bounded equipment payload for a future owning transaction frame.
    // No file, writer ownership, migration, checksum or commit is implied.
    internal static class FloatEquipmentCodec
    {
        internal const int FormatVersion=1,MaximumPayloadBytes=65536,MaximumCollectionCount=128;
        const int MaximumKeyBytes=96;
        const byte NoSlot=255;
        static readonly byte[] Magic={0x46,0x45,0x51,0x50}; // FEQP
        static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        static InvalidDataException Invalid(string message){return new InvalidDataException(message);}
        sealed class Writer : IDisposable
        {
            readonly MemoryStream stream=new MemoryStream();
            readonly BinaryWriter writer;
            internal Writer(){writer=new BinaryWriter(stream,Utf8);}
            void Space(int count){if(count<0||stream.Length>MaximumPayloadBytes-count)throw Invalid("Equipment payload exceeds the frame budget.");}
            internal void Bytes(byte[] value){Space(value.Length);writer.Write(value);}
            internal void Byte(byte value){Space(1);writer.Write(value);}
            internal void U16(int value){if(value<0||value>UInt16.MaxValue)throw Invalid("Invalid unsigned short.");Space(2);writer.Write((ushort)value);}
            internal void I32(int value){Space(4);writer.Write(value);}
            internal void I64(long value){Space(8);writer.Write(value);}
            internal void Id(Guid value){Bytes(value.ToByteArray());}
            internal void Key(string value)
            {
                FloatEquipmentGuard.Key(value);int length=Utf8.GetByteCount(value);if(length<1||length>MaximumKeyBytes)throw Invalid("Equipment key exceeds its bound.");
                U16(length);Bytes(Utf8.GetBytes(value));
            }
            internal void Count(int count){if(count<0||count>MaximumCollectionCount)throw Invalid("Equipment collection exceeds its bound.");U16(count);}
            internal byte[] Finish(){writer.Flush();return stream.ToArray();}
            public void Dispose(){writer.Dispose();stream.Dispose();}
        }
        sealed class Reader : IDisposable
        {
            readonly MemoryStream stream;
            readonly BinaryReader reader;
            internal Reader(byte[] payload)
            {
                if(payload==null||payload.Length<6||payload.Length>MaximumPayloadBytes)throw Invalid("Equipment payload length is invalid.");
                stream=new MemoryStream((byte[])payload.Clone(),false);reader=new BinaryReader(stream,Utf8);
            }
            void Need(int count){if(count<0||stream.Length-stream.Position<count)throw Invalid("Truncated equipment payload.");}
            internal byte[] Bytes(int count){Need(count);var value=reader.ReadBytes(count);if(value.Length!=count)throw Invalid("Truncated equipment bytes.");return value;}
            internal byte Byte(){Need(1);return reader.ReadByte();}
            internal int U16(){Need(2);return reader.ReadUInt16();}
            internal int I32(){Need(4);return reader.ReadInt32();}
            internal long I64(){Need(8);return reader.ReadInt64();}
            internal Guid Id(){return new Guid(Bytes(16));}
            internal string Key(){int length=U16();if(length<1||length>MaximumKeyBytes)throw Invalid("Invalid equipment key length.");return FloatEquipmentGuard.Key(Utf8.GetString(Bytes(length)));}
            internal int Count(){int count=U16();if(count>MaximumCollectionCount)throw Invalid("Equipment collection exceeds its bound.");return count;}
            internal bool Boolean(){byte value=Byte();if(value>1)throw Invalid("Invalid equipment boolean.");return value==1;}
            internal void End(){if(stream.Position!=stream.Length)throw Invalid("Trailing equipment payload bytes.");}
            public void Dispose(){reader.Dispose();stream.Dispose();}
        }
        static int PairOrder(string first,string second,string otherFirst,string otherSecond)
        {int result=StringComparer.Ordinal.Compare(first,otherFirst);return result!=0?result:StringComparer.Ordinal.Compare(second,otherSecond);}
        static void Ordered(string previous,string current)
        {if(previous!=null&&StringComparer.Ordinal.Compare(previous,current)>=0)throw Invalid("Equipment keys must be unique and in canonical order.");}
        static void OrderedPair(string first,string second,string nextFirst,string nextSecond)
        {if(first!=null&&PairOrder(first,second,nextFirst,nextSecond)>=0)throw Invalid("Equipment relations must be unique and in canonical order.");}
        static void WritePairs(Writer writer,IEnumerable<FloatEquipmentPairRule> source)
        {
            var rows=new List<FloatEquipmentPairRule>(source);rows.Sort((a,b)=>PairOrder(a.First,a.Second,b.First,b.Second));writer.Count(rows.Count);
            foreach(var row in rows){writer.Key(row.First);writer.Key(row.Second);writer.Byte(row.Allowed?(byte)1:(byte)0);}
        }
        static List<FloatEquipmentPairRule> ReadPairs(Reader reader)
        {
            int count=reader.Count();var rows=new List<FloatEquipmentPairRule>(count);string first=null,second=null;
            for(int i=0;i<count;i++){string nextFirst=reader.Key(),nextSecond=reader.Key();OrderedPair(first,second,nextFirst,nextSecond);
                rows.Add(new FloatEquipmentPairRule(nextFirst,nextSecond,reader.Boolean()));first=nextFirst;second=nextSecond;}return rows;
        }
        static void WriteCatalog(Writer writer,FloatEquipmentCatalog catalog)
        {
            writer.I64(catalog.Version);writer.Key(catalog.BaitDefinitionKey);
            var definitions=new List<FloatEquipmentDefinition>(catalog.Definitions);definitions.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Key,b.Key));writer.Count(definitions.Count);
            foreach(var row in definitions){writer.Key(row.Key);writer.Byte((byte)row.Kind);writer.I64(row.MassMg);writer.I32(row.LineDiameterUm);writer.I64(row.MinimumLoadMg);writer.I64(row.MaximumLoadMg);}
            WritePairs(writer,catalog.RodReelRules);
            var spools=new List<FloatEquipmentSpoolRule>(catalog.SpoolRules);spools.Sort((a,b)=>PairOrder(a.ReelKey,a.LineKey,b.ReelKey,b.LineKey));writer.Count(spools.Count);
            foreach(var row in spools){writer.Key(row.ReelKey);writer.Key(row.LineKey);writer.I32(row.DiameterUm);writer.I64(row.MaximumLengthMm);}
            WritePairs(writer,catalog.HookBaitRules);
            var losses=new List<FloatEquipmentLossRule>(catalog.LossRules);losses.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Key,b.Key));writer.Count(losses.Count);
            foreach(var row in losses){writer.Key(row.Key);writer.Byte((byte)row.LostSlots.Count);foreach(var slot in row.LostSlots)writer.Byte((byte)slot);}
        }
        static FloatEquipmentCatalog ReadCatalog(Reader reader)
        {
            long version=reader.I64();string bait=reader.Key();int count=reader.Count();var definitions=new List<FloatEquipmentDefinition>(count);string previous=null;
            for(int i=0;i<count;i++){string key=reader.Key();Ordered(previous,key);definitions.Add(new FloatEquipmentDefinition(key,(FloatEquipmentKind)reader.Byte(),reader.I64(),reader.I32(),reader.I64(),reader.I64()));previous=key;}
            var pairs=ReadPairs(reader);count=reader.Count();var spools=new List<FloatEquipmentSpoolRule>(count);string first=null,second=null;
            for(int i=0;i<count;i++){string reel=reader.Key(),line=reader.Key();OrderedPair(first,second,reel,line);spools.Add(new FloatEquipmentSpoolRule(reel,line,reader.I32(),reader.I64()));first=reel;second=line;}
            var hooks=ReadPairs(reader);count=reader.Count();var losses=new List<FloatEquipmentLossRule>(count);previous=null;
            for(int i=0;i<count;i++){string key=reader.Key();Ordered(previous,key);int slotCount=reader.Byte();if(slotCount<1||slotCount>3)throw Invalid("Invalid terminal slot count.");
                var slots=new List<FloatEquipmentSlot>(slotCount);int last=-1;for(int j=0;j<slotCount;j++){int slot=reader.Byte();if(slot<=last||slot<(int)FloatEquipmentSlot.Hook||slot>(int)FloatEquipmentSlot.Sinker)throw Invalid("Invalid or noncanonical terminal slots.");slots.Add((FloatEquipmentSlot)slot);last=slot;}
                losses.Add(new FloatEquipmentLossRule(key,slots));previous=key;}
            return new FloatEquipmentCatalog(version,bait,definitions,pairs,spools,hooks,losses);
        }
        static void WriteLocation(Writer writer,FloatEquipmentLocation location)
        {writer.Byte((byte)location.Place);writer.Id(location.OwnerId);writer.Byte(location.Slot.HasValue?(byte)location.Slot.Value:NoSlot);}
        static FloatEquipmentLocation ReadLocation(Reader reader)
        {
            var place=(FloatEquipmentPlace)reader.Byte();Guid owner=reader.Id();byte slot=reader.Byte();
            if(place==FloatEquipmentPlace.Inventory){if(owner!=Guid.Empty||slot!=NoSlot)throw Invalid("Inventory location has an owner or slot.");return FloatEquipmentLocation.Inventory();}
            if(place==FloatEquipmentPlace.RigSlot){if(slot>(byte)FloatEquipmentSlot.Sinker)throw Invalid("Invalid rig slot.");return FloatEquipmentLocation.RigSlot(owner,(FloatEquipmentSlot)slot);}
            if(slot!=NoSlot)throw Invalid("Non-rig location contains a slot.");
            if(place==FloatEquipmentPlace.ReelSpool)return FloatEquipmentLocation.ReelSpool(owner);
            if(place==FloatEquipmentPlace.Lost)return FloatEquipmentLocation.Lost(owner);
            throw Invalid("Unknown equipment location.");
        }
        internal static byte[] Encode(FloatEquipmentLedger ledger)
        {
            try{
                if(ledger==null)throw Invalid("Equipment ledger is missing.");var check=ledger.ValidateAssembly();
                if(!check.Valid)throw Invalid("Equipment assembly is invalid: "+check.Issues[0].Code);
                using(var writer=new Writer()){
                    writer.Bytes(Magic);writer.U16(FormatVersion);WriteCatalog(writer,ledger.Catalog);
                    writer.Id(ledger.RigId);writer.I64(ledger.RigRevision);writer.I64(ledger.InventoryRevision);writer.Count(ledger.Instances.Count);
                    foreach(var item in ledger.Instances){writer.Id(item.Id);writer.Key(item.DefinitionKey);writer.Byte((byte)item.Kind);writer.I64(item.Revision);WriteLocation(writer,item.Location);writer.I64(item.InitialLineMm);writer.I64(item.RemainingLineMm);}
                    return writer.Finish();
                }
            }catch(InvalidDataException){throw;}catch(Exception error){throw new InvalidDataException("Cannot encode equipment payload.",error);}
        }
        internal static FloatEquipmentLedger Decode(byte[] payload)
        {
            try{using(var reader=new Reader(payload)){
                    foreach(byte marker in Magic)if(reader.Byte()!=marker)throw Invalid("Unknown equipment payload marker.");
                    if(reader.U16()!=FormatVersion)throw Invalid("Unsupported equipment payload version.");
                    var catalog=ReadCatalog(reader);Guid rig=reader.Id();long rigRevision=reader.I64(),inventoryRevision=reader.I64();int count=reader.Count();
                    var items=new List<FloatEquipmentInstance>(count);Guid? previous=null;
                    for(int i=0;i<count;i++){Guid id=reader.Id();if(previous.HasValue&&previous.Value.CompareTo(id)>=0)throw Invalid("Equipment identities must be unique and in canonical order.");
                        string key=reader.Key();var kind=(FloatEquipmentKind)reader.Byte();long revision=reader.I64();var location=ReadLocation(reader);long initial=reader.I64(),remaining=reader.I64();
                        items.Add(new FloatEquipmentInstance(id,key,kind,revision,location,initial,remaining));previous=id;}
                    reader.End();var ledger=new FloatEquipmentLedger(rig,rigRevision,inventoryRevision,catalog,items);var check=ledger.ValidateAssembly();
                    if(!check.Valid)throw Invalid("Decoded equipment assembly is invalid: "+check.Issues[0].Code);return ledger;
                }
            }catch(InvalidDataException){throw;}catch(Exception error){throw new InvalidDataException("Invalid equipment payload.",error);}
        }
    }
}
