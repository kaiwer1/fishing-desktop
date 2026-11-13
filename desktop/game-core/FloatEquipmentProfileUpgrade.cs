using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FishingDesktop.GameCore
{
    // Creates an independent candidate only. Never opens a GameOwner, settles
    // a legacy cast, replaces the source or selects the new branch for play.
    internal static class FloatEquipmentProfileUpgrade
    {
        [StructLayout(LayoutKind.Sequential)]
        struct FileIdentity
        {
            internal uint Attributes,CreatedLow,CreatedHigh,AccessLow,AccessHigh,WrittenLow,WrittenHigh,
                Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
        }
        [DllImport("kernel32.dll",SetLastError=true)]
        static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileIdentity information);
        static void DistinctFiles(FileStream first,FileStream second)
        {
            FileIdentity a,b;
            if(!GetFileInformationByHandle(first.SafeFileHandle,out a)||!GetFileInformationByHandle(second.SafeFileHandle,out b))
                throw new IOException("Cannot establish independent equipment upgrade file identities.");
            if(a.Volume==b.Volume&&a.IndexHigh==b.IndexHigh&&a.IndexLow==b.IndexLow)
                throw new IOException("Equipment upgrade paths alias the same file; independent files are required.");
        }
        static string FullPath(string value)
        {if(String.IsNullOrWhiteSpace(value))throw new ArgumentException("An explicit equipment upgrade path is required.");return Path.GetFullPath(value);}
        static Guid Identity(string source,string policy,string offers,string role)
        {
            byte[] bytes;
            using(var hash=SHA256.Create())bytes=hash.ComputeHash(Encoding.UTF8.GetBytes("fishing-equipment-profile-upgrade-v1|"+role+"|"+source+"|"+policy+"|"+offers));
            var id=new byte[16];Array.Copy(bytes,id,16);id[7]=(byte)((id[7]&15)|128);id[8]=(byte)((id[8]&63)|128);return new Guid(id);
        }
        static bool EqualBytes(byte[] left,byte[] right)
        {if(left.Length!=right.Length)return false;for(int i=0;i<left.Length;i++)if(left[i]!=right[i])return false;return true;}
        static bool SameOrigin(FloatUpgradeOrigin left,FloatUpgradeOrigin right)
        {
            if(left==null||right==null)return left==null&&right==null;
            return left.SourceBranchId==right.SourceBranchId&&left.SourceSha256==right.SourceSha256&&left.SourceTipSha256==right.SourceTipSha256&&
                left.SourceLength==right.SourceLength&&left.SourceSequence==right.SourceSequence;
        }
        static void ValidateBackup(FloatJournalInspection source,FloatJournalInspection backup)
        {
            if(source.Length!=backup.Length||source.Sha256!=backup.Sha256||source.TipSha256!=backup.TipSha256||
                source.ProfileId!=backup.ProfileId||source.BranchId!=backup.BranchId||source.Sequence!=backup.Sequence||!EqualBytes(source.CopyBytes(),backup.CopyBytes()))
                throw new InvalidDataException("Existing backup does not match the frozen equipment upgrade source.");
        }
        static void ValidateCandidate(FloatJournalInspection source,FloatJournalInspection candidate,UpgradeFloatEquipment command,FloatEquipmentOffers offers)
        {
            var state=candidate.State;
            if(state.SchemaVersion!=3||state.Session!=null||state.Equipment==null||state.EquipmentOffers==null||
                state.EquipmentOffers.Signature!=offers.Signature||candidate.ProfileId!=source.ProfileId||candidate.BranchId!=command.NewBranchId||candidate.Sequence!=source.Sequence+1||
                !SameOrigin(state.EquipmentUpgradeOrigin,command.Origin)||!SameOrigin(state.UpgradeOrigin,source.State.UpgradeOrigin)||
                !EqualBytes(FloatEquipmentCodec.Encode(state.Equipment),command.CopyEquipmentPayload())||state.Receipts.Count!=source.State.Receipts.Count+1)
                throw new InvalidDataException("Candidate is not the untouched equipment upgrade of this source and policy; it is retained without changes.");
            var receipt=state.Receipts[state.Receipts.Count-1];
            if(receipt.RequestId!=command.RequestId||receipt.EventId!=command.EventId||receipt.Fingerprint!=command.Fingerprint)
                throw new InvalidDataException("Candidate equipment migration identities do not match.");
            byte[] prefix=source.CopyBytes(),full=candidate.CopyBytes();
            if(full.Length<=prefix.Length)throw new InvalidDataException("Candidate does not append an equipment migration event.");
            for(int i=0;i<prefix.Length;i++)if(prefix[i]!=full[i])throw new InvalidDataException("Candidate changed source history bytes.");
        }
        internal static FloatProfileUpgradeResult CreateCandidate(string sourcePath,string backupPath,string candidatePath,FloatEquipmentMigrationPolicy policy,FloatEquipmentOffers offers)
        {
            if(policy==null)throw new ArgumentNullException("policy");if(offers==null)throw new ArgumentNullException("offers");
            string sourceFile=FullPath(sourcePath),backupFile=FullPath(backupPath),candidateFile=FullPath(candidatePath);
            if(String.Equals(sourceFile,backupFile,StringComparison.OrdinalIgnoreCase)||String.Equals(sourceFile,candidateFile,StringComparison.OrdinalIgnoreCase)||
                String.Equals(backupFile,candidateFile,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Source, backup and candidate must use three different explicit paths.");
            // Keep the original read-only and deny writers/deletion throughout
            // preflight, backup and candidate creation, including alias paths.
            using(var sourceLock=new FileStream(sourceFile,FileMode.Open,FileAccess.Read,FileShare.Read)){
                var source=FloatJournalArchive.Inspect(sourceFile);
                if(source.State.SchemaVersion!=2)throw new InvalidOperationException("Equipment upgrade requires a schema2 legacy source.");
                if(source.State.Session!=null)throw new InvalidOperationException("Finish the legacy session before equipment upgrade; it will not be settled automatically.");
                if(source.Sequence>=FloatGameStore.MaximumTransactions)throw new InvalidOperationException("Source is at the transaction limit; equipment migration does not reset history.");
                // All mapping, catalogue, codec and capacity checks precede
                // creation of either output. No starter items or refills apply.
                var equipment=policy.Map(source.State);offers.ValidateAgainst(equipment.Catalog,source.State.Rules.MaximumWalletMinor);
                byte[] equipmentPayload=FloatEquipmentCodec.Encode(equipment),offersPayload=FloatEquipmentOffersCodec.Encode(offers);
                Guid branch=Identity(source.Sha256,policy.Signature,offers.Signature,"branch"),request=Identity(source.Sha256,policy.Signature,offers.Signature,"request"),
                    terminal=Identity(source.Sha256,policy.Signature,offers.Signature,"event");
                var command=new UpgradeFloatEquipment(source.ProfileId,source.BranchId,request,terminal,branch,source.Sha256,source.TipSha256,source.Length,
                    source.Sequence,source.State.Rig.Revision,source.State.RulesVersion,policy.Version,policy.SourceRigDefinitionKey,equipmentPayload,offersPayload);
                var preview=FloatGameReducer.Reduce(source.State,command,source.State.Rules);
                if(!preview.HasCandidate)throw new InvalidOperationException("Equipment upgrade was rejected before file creation: "+preview.Code);
                long frameBytes=4+8+32+32+(long)FloatJournalCodec.EncodeCommand(command,3).Length;
                if(source.Length>FloatGameStore.MaximumFileBytes-frameBytes)throw new InvalidOperationException("Source has insufficient journal space; equipment migration does not reset history.");
                if(!File.Exists(backupFile))FloatJournalArchive.Backup(source,backupFile);
                using(var backupLock=new FileStream(backupFile,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    DistinctFiles(sourceLock,backupLock);ValidateBackup(source,FloatJournalArchive.Inspect(backupFile));
                    if(File.Exists(candidateFile)){
                        using(var candidateLock=new FileStream(candidateFile,FileMode.Open,FileAccess.Read,FileShare.Read)){
                            DistinctFiles(sourceLock,candidateLock);DistinctFiles(backupLock,candidateLock);
                            var previous=FloatJournalArchive.Inspect(candidateFile);ValidateCandidate(source,previous,command,offers);
                            return new FloatProfileUpgradeResult(backupFile,candidateFile,source,previous,true);
                        }
                    }
                    // CreateNew and the store's prefix proof also protect
                    // against a competing writer or path replacement here.
                    FloatJournalArchive.Backup(source,candidateFile);
                    using(var store=FloatGameStore.OpenExisting(candidateFile)){
                        var result=store.AppendEquipmentUpgrade(command);
                        if(!result.Committed||!result.Written||result.IsReplay)throw new InvalidOperationException("Equipment candidate upgrade was not confirmed; keep the file for inspection.");
                    }
                    using(var candidateLock=new FileStream(candidateFile,FileMode.Open,FileAccess.Read,FileShare.Read)){
                        DistinctFiles(sourceLock,candidateLock);DistinctFiles(backupLock,candidateLock);
                        var upgraded=FloatJournalArchive.Inspect(candidateFile);ValidateCandidate(source,upgraded,command,offers);
                        return new FloatProfileUpgradeResult(backupFile,candidateFile,source,upgraded,false);
                    }
                }
            }
        }
    }
}
