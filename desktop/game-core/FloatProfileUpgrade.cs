using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FishingDesktop.GameCore
{
    public sealed class FloatProfileUpgradeResult
    {
        public readonly string BackupPath,CandidatePath,SourceSha256,CandidateSha256;
        public readonly Guid ProfileId,SourceBranchId,CandidateBranchId;
        public readonly long SourceSequence,CandidateSequence;
        public readonly bool ExistingCandidateVerified;
        internal FloatProfileUpgradeResult(string backup,string candidate,FloatJournalInspection source,FloatJournalInspection upgraded,bool existing)
        {BackupPath=backup;CandidatePath=candidate;SourceSha256=source.Sha256;CandidateSha256=upgraded.Sha256;ProfileId=source.ProfileId;
            SourceBranchId=source.BranchId;CandidateBranchId=upgraded.BranchId;SourceSequence=source.Sequence;CandidateSequence=upgraded.Sequence;ExistingCandidateVerified=existing;}
    }
    // Candidate creation only. This service never replaces an active file,
    // opens a GameOwner, retrieves an unfinished cast or activates a branch.
    public static class FloatProfileUpgrade
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
                throw new IOException("Cannot establish independent upgrade file identities.");
            if(a.Volume==b.Volume&&a.IndexHigh==b.IndexHigh&&a.IndexLow==b.IndexLow)
                throw new IOException("Upgrade paths alias the same file; an independent backup and candidate are required.");
        }
        static string FullPath(string value)
        {if(String.IsNullOrWhiteSpace(value))throw new ArgumentException("An explicit upgrade path is required.");return Path.GetFullPath(value);}
        static Guid Identity(string source,string policy,string role)
        {
            byte[] bytes;
            using(var hash=SHA256.Create())bytes=hash.ComputeHash(Encoding.UTF8.GetBytes("fishing-profile-upgrade-v2|"+source+"|"+policy+"|"+role));
            var id=new byte[16];Array.Copy(bytes,id,16);id[7]=(byte)((id[7]&15)|128);id[8]=(byte)((id[8]&63)|128);return new Guid(id);
        }
        static void SameBytes(FloatJournalInspection expected,FloatJournalInspection actual)
        {
            if(expected.Length!=actual.Length || expected.Sha256!=actual.Sha256 || expected.TipSha256!=actual.TipSha256 ||
                expected.ProfileId!=actual.ProfileId || expected.BranchId!=actual.BranchId || expected.Sequence!=actual.Sequence)
                throw new InvalidDataException("Existing backup does not match the frozen source.");
            byte[] left=expected.CopyBytes(),right=actual.CopyBytes();
            for(int i=0;i<left.Length;i++)if(left[i]!=right[i])throw new InvalidDataException("Existing backup bytes differ from the frozen source.");
        }
        static void ValidateCandidate(FloatJournalInspection source,FloatJournalInspection candidate,UpgradeFloatProfile expected)
        {
            var state=candidate.State;var origin=state.UpgradeOrigin;
            if(state.SchemaVersion!=2 || origin==null || state.CommerceCatalog==null || state.CommerceCatalog.Signature!=expected.Catalog.Signature ||
                state.FoodStackId!=expected.FoodStackId || candidate.ProfileId!=source.ProfileId || candidate.BranchId!=expected.NewBranchId || candidate.Sequence!=source.Sequence+1 ||
                origin.SourceBranchId!=source.BranchId || origin.SourceSha256!=source.Sha256 || origin.SourceTipSha256!=source.TipSha256 || origin.SourceLength!=source.Length || origin.SourceSequence!=source.Sequence)
                throw new InvalidDataException("Candidate is not the untouched upgrade of this source and policy; it is retained without changes.");
            var receipt=state.Receipts[state.Receipts.Count-1];
            if(receipt.RequestId!=expected.RequestId || receipt.EventId!=expected.EventId || receipt.Fingerprint!=expected.Fingerprint)throw new InvalidDataException("Candidate migration identities do not match.");
            byte[] prefix=source.CopyBytes(),full=candidate.CopyBytes();
            if(full.Length<=prefix.Length)throw new InvalidDataException("Candidate does not append a migration event.");
            for(int i=0;i<prefix.Length;i++)if(prefix[i]!=full[i])throw new InvalidDataException("Candidate changed source history bytes.");
        }
        public static FloatProfileUpgradeResult CreateCandidate(string sourcePath,string backupPath,string candidatePath,FloatCommerceCatalog catalog)
        {
            if(catalog==null)throw new ArgumentNullException("catalog");
            string sourceFile=FullPath(sourcePath),backupFile=FullPath(backupPath),candidateFile=FullPath(candidatePath);
            if(String.Equals(sourceFile,backupFile,StringComparison.OrdinalIgnoreCase) || String.Equals(sourceFile,candidateFile,StringComparison.OrdinalIgnoreCase) ||
                String.Equals(backupFile,candidateFile,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Source, backup and candidate must use three different explicit paths.");
            // Hold the original read-only and deny writers/deletion until all
            // candidate work ends, even if another path aliases that file.
            using(var sourceLock=new FileStream(sourceFile,FileMode.Open,FileAccess.Read,FileShare.Read)){
                var source=FloatJournalArchive.Inspect(sourceFile);
                if(source.State.SchemaVersion!=1)throw new InvalidOperationException("This upgrade accepts only an unmodified schema-1 source.");
                if(source.Sequence>=FloatGameStore.MaximumTransactions)throw new InvalidOperationException("Source is at the transaction limit; migration does not reset history.");
                string policy=catalog.Signature;
                Guid branch=Identity(source.Sha256,policy,"branch"),request=Identity(source.Sha256,policy,"request"),terminal=Identity(source.Sha256,policy,"event"),food=Identity(source.Sha256,policy,"food-stack");
                var command=new UpgradeFloatProfile(source.ProfileId,source.BranchId,request,terminal,branch,food,source.Sha256,source.TipSha256,source.Length,
                    source.Sequence,source.State.Rig.Revision,source.State.RulesVersion,catalog);
                var preview=FloatGameReducer.Reduce(source.State,command,source.State.Rules);
                if(!preview.HasCandidate)throw new InvalidOperationException("Upgrade policy was rejected before file creation: "+preview.Code);
                long frameBytes=4+8+32+32+(long)FloatJournalCodec.EncodeCommand(command,2).Length;
                if(source.Length>FloatGameStore.MaximumFileBytes-frameBytes)throw new InvalidOperationException("Source has insufficient journal space; history is not reset.");
                if(!File.Exists(backupFile))FloatJournalArchive.Backup(source,backupFile);
                using(var backupLock=new FileStream(backupFile,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    DistinctFiles(sourceLock,backupLock);
                    SameBytes(source,FloatJournalArchive.Inspect(backupFile));
                    if(File.Exists(candidateFile)){
                        using(var candidateLock=new FileStream(candidateFile,FileMode.Open,FileAccess.Read,FileShare.Read)){
                            DistinctFiles(sourceLock,candidateLock);DistinctFiles(backupLock,candidateLock);
                            var previous=FloatJournalArchive.Inspect(candidateFile);ValidateCandidate(source,previous,command);
                            return new FloatProfileUpgradeResult(backupFile,candidateFile,source,previous,true);
                        }
                    }
                    FloatJournalArchive.Backup(source,candidateFile);
                    using(var store=FloatGameStore.OpenExisting(candidateFile)){
                        var result=store.AppendUpgrade(command);
                        if(!result.Committed || !result.Written || result.IsReplay)throw new InvalidOperationException("The new candidate upgrade was not confirmed; keep the file for inspection.");
                    }
                    using(var candidateLock=new FileStream(candidateFile,FileMode.Open,FileAccess.Read,FileShare.Read)){
                        DistinctFiles(sourceLock,candidateLock);DistinctFiles(backupLock,candidateLock);
                        var upgraded=FloatJournalArchive.Inspect(candidateFile);ValidateCandidate(source,upgraded,command);
                        return new FloatProfileUpgradeResult(backupFile,candidateFile,source,upgraded,false);
                    }
                }
            }
        }
    }
}
