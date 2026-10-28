using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FishingDesktop.GameCore
{
    // A frozen validated snapshot, not a writer or an activation/restore token.
    public sealed class FloatJournalInspection
    {
        public readonly string SourcePath,Sha256,TipSha256;
        public readonly long Length,Sequence;
        public readonly Guid ProfileId,BranchId;
        public readonly bool HasActiveSession,HasPendingFish;
        internal readonly FloatGameState State;
        readonly byte[] bytes;
        FloatJournalInspection(string sourcePath,byte[] frozen,FloatJournalReplay replay)
        {
            SourcePath=sourcePath;bytes=frozen;State=replay.State;Length=replay.Length;Sequence=State.Sequence;
            ProfileId=State.ProfileId;BranchId=State.BranchId;HasActiveSession=State.Session!=null;HasPendingFish=HasActiveSession&&State.Session.PendingFish!=null;
            Sha256=FloatJournalArchive.Hash(frozen);TipSha256=FloatJournalArchive.Hex(replay.CopyTipHash());
        }
        internal static FloatJournalInspection FromValidatedBytes(string sourcePath,byte[] captured)
        {
            if(captured==null)throw new ArgumentNullException("captured");
            if(captured.LongLength>FloatGameStore.MaximumFileBytes)throw new InvalidDataException("Journal exceeds the archive bound.");
            var frozen=(byte[])captured.Clone();
            using(var input=new MemoryStream(frozen,false)){
                var replay=FloatGameStore.ReadValidatedJournal(input);
                return new FloatJournalInspection(sourcePath,frozen,replay);
            }
        }
        internal byte[] CopyBytes(){return (byte[])bytes.Clone();}
    }
    public sealed class FloatJournalBackup
    {
        public readonly string DestinationPath,Sha256,TipSha256;
        public readonly long Length,Sequence;
        public readonly Guid ProfileId,BranchId;
        internal FloatJournalBackup(string destinationPath,FloatJournalInspection source)
        {DestinationPath=destinationPath;Sha256=source.Sha256;TipSha256=source.TipSha256;Length=source.Length;Sequence=source.Sequence;ProfileId=source.ProfileId;BranchId=source.BranchId;}
    }
    public sealed class FloatJournalBackupUnconfirmedException : IOException
    {
        public readonly string DestinationPath;
        internal FloatJournalBackupUnconfirmedException(string destinationPath,Exception cause)
            :base("The backup was not confirmed. Its new destination is retained and must be inspected explicitly; it will not be overwritten or selected automatically.",cause)
        {DestinationPath=destinationPath;}
    }
    // Internal fault/mutation seams for newly created private fixtures only.
    // Public callers cannot supply callbacks or obtain a live file handle.
    internal enum FloatArchiveFaultPoint { SourceOpened,SourceCopied,BeforeBackupWrite,AfterBackupPrefix,AfterBackupWrite,AfterBackupFlush,BeforeBackupVerify,AfterBackupVerified }
    public static class FloatJournalArchive
    {
        internal static string Hex(byte[] value)
        {var text=new StringBuilder(value.Length*2);foreach(byte b in value)text.Append(b.ToString("x2"));return text.ToString();}
        internal static string Hash(byte[] value)
        {using(var sha=SHA256.Create())return Hex(sha.ComputeHash(value));}
        static string FullPath(string path)
        {if(String.IsNullOrWhiteSpace(path))throw new ArgumentException("An explicit journal path is required.","path");return Path.GetFullPath(path);}
        static void Hook(Action<FloatArchiveFaultPoint,Stream> hook,FloatArchiveFaultPoint point,Stream stream)
        {if(hook!=null)hook(point,stream);}
        static byte[] Capture(Stream source)
        {
            long length=source.Length;
            if(length<1||length>FloatGameStore.MaximumFileBytes)throw new InvalidDataException("Journal size is outside the archive bound.");
            var bytes=new byte[checked((int)length)];int offset=0;
            while(offset<bytes.Length){int count=source.Read(bytes,offset,bytes.Length-offset);if(count==0)throw new InvalidDataException("Journal changed or ended during capture.");offset+=count;}
            if(source.Position!=length||source.Length!=length)throw new InvalidDataException("Journal length changed during capture.");
            return bytes;
        }
        public static FloatJournalInspection Inspect(string sourcePath){return Inspect(sourcePath,null);}
        internal static FloatJournalInspection Inspect(string sourcePath,Action<FloatArchiveFaultPoint,Stream> hook)
        {
            string source=FullPath(sourcePath);
            // This share mode rejects an already-open writer as well as a new
            // writer/delete attempt, and works when the source is read-only.
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,4096,FileOptions.SequentialScan)){
                Hook(hook,FloatArchiveFaultPoint.SourceOpened,input);
                byte[] bytes=Capture(input);
                Hook(hook,FloatArchiveFaultPoint.SourceCopied,input);
                return FloatJournalInspection.FromValidatedBytes(source,bytes);
            }
        }
        static void Verify(FileStream destination,byte[] frozen,FloatJournalInspection inspection)
        {
            if(destination.Length!=frozen.LongLength)throw new InvalidDataException("Backup length differs from the inspected snapshot.");
            destination.Position=0;var buffer=new byte[4096];int offset=0;
            using(var sha=SHA256.Create()){
                while(offset<frozen.Length){int wanted=Math.Min(buffer.Length,frozen.Length-offset),count=destination.Read(buffer,0,wanted);
                    if(count==0)throw new InvalidDataException("Backup ended during verification.");
                    for(int i=0;i<count;i++)if(buffer[i]!=frozen[offset+i])throw new InvalidDataException("Backup bytes differ from the inspected snapshot.");
                    sha.TransformBlock(buffer,0,count,buffer,0);offset+=count;
                }
                sha.TransformFinalBlock(new byte[0],0,0);
                if(Hex(sha.Hash)!=inspection.Sha256)throw new InvalidDataException("Backup hash differs from the inspected snapshot.");
            }
            destination.Position=0;var replay=FloatGameStore.ReadValidatedJournal(destination);
            if(replay.Length!=inspection.Length||replay.State.Sequence!=inspection.Sequence||replay.State.ProfileId!=inspection.ProfileId||replay.State.BranchId!=inspection.BranchId||Hex(replay.CopyTipHash())!=inspection.TipSha256)
                throw new InvalidDataException("Backup replay differs from the inspected snapshot.");
        }
        public static FloatJournalBackup Backup(FloatJournalInspection inspection,string destinationPath){return Backup(inspection,destinationPath,null);}
        internal static FloatJournalBackup Backup(FloatJournalInspection inspection,string destinationPath,Action<FloatArchiveFaultPoint,Stream> hook)
        {
            if(inspection==null)throw new ArgumentNullException("inspection");
            string destination=FullPath(destinationPath);
            if(String.Equals(destination,inspection.SourcePath,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Backup destination must differ from its inspected source.","destinationPath");
            byte[] frozen=inspection.CopyBytes();
            // CreateNew itself decides whether the destination exists. There
            // is no check-then-overwrite race or same-content overwrite path.
            var output=new FileStream(destination,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,4096,FileOptions.WriteThrough);
            try{
                using(output){
                    Hook(hook,FloatArchiveFaultPoint.BeforeBackupWrite,output);
                    int prefix=Math.Min(64,frozen.Length);output.Write(frozen,0,prefix);
                    Hook(hook,FloatArchiveFaultPoint.AfterBackupPrefix,output);
                    output.Write(frozen,prefix,frozen.Length-prefix);
                    Hook(hook,FloatArchiveFaultPoint.AfterBackupWrite,output);
                    output.Flush(true);
                    Hook(hook,FloatArchiveFaultPoint.AfterBackupFlush,output);
                    Hook(hook,FloatArchiveFaultPoint.BeforeBackupVerify,output);
                    Verify(output,frozen,inspection);
                    // The final test seam can lose confirmation, but is not a
                    // mutation seam after successful readback validation.
                    Hook(hook,FloatArchiveFaultPoint.AfterBackupVerified,null);
                }
                return new FloatJournalBackup(destination,inspection);
            }catch(Exception error){throw new FloatJournalBackupUnconfirmedException(destination,error);}
        }
    }
}
