using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FishingDesktop.GameCore
{
    // Independent FEOS v1 payload; never appended to a game journal by this
    // module. Decoding requires the caller's already-known equipment catalogue
    // and economic limit. It cannot install definitions or choose a wallet cap.
    internal static class FloatEquipmentOffersCodec
    {
        internal const int FormatVersion=1,MaximumPayloadBytes=65536;
        static readonly byte[] Magic={70,69,79,83};
        static readonly byte[] EquipmentIdentityDomain={70,69,67,83};
        static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        static InvalidDataException Invalid(string text){return new InvalidDataException(text);}
        sealed class Writer : IDisposable
        {
            readonly MemoryStream stream=new MemoryStream();readonly BinaryWriter writer;
            internal Writer(){writer=new BinaryWriter(stream,Utf8);}
            void Space(int n){if(n<0||stream.Length>MaximumPayloadBytes-n)throw Invalid("Offer payload exceeds its total byte budget.");}
            internal void Bytes(byte[] bytes){Space(bytes.Length);writer.Write(bytes);}
            internal void Byte(byte n){Space(1);writer.Write(n);}
            internal void U16(int n){if(n<0||n>UInt16.MaxValue)throw Invalid("Invalid unsigned short.");Space(2);writer.Write((ushort)n);}
            internal void I32(int n){Space(4);writer.Write(n);}
            internal void I64(long n){Space(8);writer.Write(n);}
            internal void Key(string key){FloatEquipmentGuard.Key(key);var bytes=Utf8.GetBytes(key);if(bytes.Length>96)throw Invalid("Key exceeds its byte bound.");U16(bytes.Length);Bytes(bytes);}
            internal void Count(int n){if(n<0||n>FloatEquipmentGuard.MaximumInstances)throw Invalid("Equipment identity collection exceeds its bound.");U16(n);}
            internal byte[] Finish(){writer.Flush();return stream.ToArray();}
            public void Dispose(){writer.Dispose();stream.Dispose();}
        }
        sealed class Reader : IDisposable
        {
            readonly MemoryStream stream;readonly BinaryReader reader;
            internal Reader(byte[] payload){
                if(payload==null||payload.Length<6||payload.Length>MaximumPayloadBytes)throw Invalid("Offer payload length is invalid.");
                stream=new MemoryStream((byte[])payload.Clone(),false);reader=new BinaryReader(stream,Utf8);
            }
            void Need(int n){if(n<0||stream.Length-stream.Position<n)throw Invalid("Truncated offer payload.");}
            internal byte[] Bytes(int n){Need(n);var b=reader.ReadBytes(n);if(b.Length!=n)throw Invalid("Truncated offer payload.");return b;}
            internal byte Byte(){Need(1);return reader.ReadByte();}
            internal int U16(){Need(2);return reader.ReadUInt16();}
            internal long I64(){Need(8);return reader.ReadInt64();}
            internal string Key(){int length=U16();if(length<1||length>96)throw Invalid("Invalid offer key length.");return FloatEquipmentGuard.Key(Utf8.GetString(Bytes(length)));}
            internal void End(){if(stream.Position!=stream.Length)throw Invalid("Trailing offer payload bytes.");}
            public void Dispose(){reader.Dispose();stream.Dispose();}
        }
        static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
        static byte[] Hash(byte[] bytes){using(var sha=SHA256.Create())return sha.ComputeHash(bytes);}
        static byte[] HashBytes(string hex)
        {
            if(hex==null||hex.Length!=64)throw Invalid("Invalid catalogue signature.");var bytes=new byte[32];
            for(int i=0;i<bytes.Length;i++){
                int a=HexDigit(hex[2*i]),b=HexDigit(hex[2*i+1]);if(a<0||b<0)throw Invalid("Catalogue signature must be lowercase hexadecimal.");bytes[i]=(byte)(a*16+b);
            }return bytes;
        }
        static int HexDigit(char c){return c>='0'&&c<='9'?c-'0':c>='a'&&c<='f'?c-'a'+10:-1;}
        static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        static int PairOrder(string a,string b,string c,string d){int n=StringComparer.Ordinal.Compare(a,c);return n!=0?n:StringComparer.Ordinal.Compare(b,d);}
        static void Pairs(Writer writer,IEnumerable<FloatEquipmentPairRule> source)
        {
            var rows=new List<FloatEquipmentPairRule>(source);rows.Sort((a,b)=>PairOrder(a.First,a.Second,b.First,b.Second));writer.Count(rows.Count);
            foreach(var row in rows){writer.Key(row.First);writer.Key(row.Second);writer.Byte(row.Allowed?(byte)1:(byte)0);}
        }
        // Domain-separated canonical signature of ALL existing equipment
        // definitions and rules, including rules not used by the four offers.
        // The bounded manifest is identity data only, not an import format.
        internal static string EquipmentSignature(FloatEquipmentCatalog catalog)
        {
            if(catalog==null)throw new ArgumentNullException("catalog");
            using(var writer=new Writer()){
                writer.Bytes(EquipmentIdentityDomain);writer.U16(1);writer.I64(catalog.Version);writer.Key(catalog.BaitDefinitionKey);
                var defs=new List<FloatEquipmentDefinition>(catalog.Definitions);defs.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Key,b.Key));writer.Count(defs.Count);
                foreach(var row in defs){writer.Key(row.Key);writer.Byte((byte)row.Kind);writer.I64(row.MassMg);writer.I32(row.LineDiameterUm);writer.I64(row.MinimumLoadMg);writer.I64(row.MaximumLoadMg);}
                Pairs(writer,catalog.RodReelRules);
                var spools=new List<FloatEquipmentSpoolRule>(catalog.SpoolRules);spools.Sort((a,b)=>PairOrder(a.ReelKey,a.LineKey,b.ReelKey,b.LineKey));writer.Count(spools.Count);
                foreach(var row in spools){writer.Key(row.ReelKey);writer.Key(row.LineKey);writer.I32(row.DiameterUm);writer.I64(row.MaximumLengthMm);}
                Pairs(writer,catalog.HookBaitRules);
                var losses=new List<FloatEquipmentLossRule>(catalog.LossRules);losses.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Key,b.Key));writer.Count(losses.Count);
                foreach(var row in losses){writer.Key(row.Key);writer.Byte((byte)row.LostSlots.Count);foreach(var slot in row.LostSlots)writer.Byte((byte)slot);}
                return Hex(Hash(writer.Finish()));
            }
        }
        static byte[] Content(FloatEquipmentOffers catalog)
        {
            using(var writer=new Writer()){
                writer.Bytes(Magic);writer.U16(FormatVersion);writer.I64(catalog.Version);writer.I64(catalog.EquipmentCatalogVersion);writer.Bytes(HashBytes(catalog.EquipmentCatalogSignature));
                if(catalog.Offers.Count<1||catalog.Offers.Count>FloatEquipmentOffers.MaximumOffers)throw Invalid("Invalid offer count.");writer.U16(catalog.Offers.Count);
                string previous=null;
                foreach(var offer in catalog.Offers){
                    if(previous!=null&&StringComparer.Ordinal.Compare(previous,offer.Sku)>=0)throw Invalid("Offers are not in canonical unique SKU order.");
                    writer.Key(offer.Sku);writer.Key(offer.DefinitionKey);writer.Byte((byte)offer.Kind);writer.I64(offer.PriceMinor);writer.I64(offer.LineLengthMm);previous=offer.Sku;
                }return writer.Finish();
            }
        }
        internal static string ContentSignature(FloatEquipmentOffers catalog)
        {if(catalog==null)throw new ArgumentNullException("catalog");return Hex(Hash(Content(catalog)));}
        internal static byte[] Encode(FloatEquipmentOffers catalog)
        {
            if(catalog==null)throw Invalid("Missing offer catalogue.");
            catalog.ValidateAgainst(catalog.BoundEquipmentCatalog,catalog.ValidatedMaximumWalletMinor);return Content(catalog);
        }
        internal static FloatEquipmentOffers Decode(byte[] payload,FloatEquipmentCatalog equipmentCatalog,long maximumWalletMinor)
        {
            if(equipmentCatalog==null)throw new ArgumentNullException("equipmentCatalog");
            if(maximumWalletMinor<=0)throw new ArgumentOutOfRangeException("maximumWalletMinor");
            try{using(var reader=new Reader(payload)){
                if(!Equal(reader.Bytes(4),Magic)||reader.U16()!=FormatVersion)throw Invalid("Unknown offer payload format.");
                long version=reader.I64(),equipmentVersion=reader.I64();string signature=Hex(reader.Bytes(32));
                if(equipmentVersion!=equipmentCatalog.Version||signature!=EquipmentSignature(equipmentCatalog))throw Invalid("Offer payload does not match the complete existing equipment catalogue.");
                int count=reader.U16();if(count<1||count>FloatEquipmentOffers.MaximumOffers)throw Invalid("Invalid offer count.");
                var rows=new List<FloatEquipmentOffer>(count);string previous=null;
                for(int i=0;i<count;i++){
                    string sku=reader.Key();if(previous!=null&&StringComparer.Ordinal.Compare(previous,sku)>=0)throw Invalid("Noncanonical or repeated offer SKU.");
                    rows.Add(new FloatEquipmentOffer(sku,reader.Key(),(FloatEquipmentKind)reader.Byte(),reader.I64(),reader.I64()));previous=sku;
                }
                reader.End();return new FloatEquipmentOffers(version,equipmentCatalog,rows,maximumWalletMinor);
            }}catch(InvalidDataException){throw;}catch(ArgumentException error){throw new InvalidDataException("Invalid offer payload data.",error);}
            catch(OverflowException error){throw new InvalidDataException("Offer payload arithmetic overflow.",error);}
        }
    }
}
