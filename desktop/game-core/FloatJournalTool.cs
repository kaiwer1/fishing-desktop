using System;
using System.IO;
using System.Web.Script.Serialization;
using FishingDesktop.GameCore;

// Explicit local maintenance tool. No default source, restore, activation or
// startup lifecycle: inspecting a cast must never secretly retrieve it.
static class FloatJournalTool
{
    static int Main(string[] args)
    {
        var json=new JavaScriptSerializer();
        try{
            if(args.Length<2 || (args[0]!="inspect" && args[0]!="backup") || args.Length!=(args[0]=="backup"?3:2))
                throw new ArgumentException("Use inspect <source> or backup <source> <new destination>.");
            var source=FloatJournalArchive.Inspect(args[1]);
            if(args[0]=="inspect"){
                Console.WriteLine(json.Serialize(new {success=true,action="inspect",source=source.SourcePath,length=source.Length,
                    sha256=source.Sha256,tipSha256=source.TipSha256,sequence=source.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    profileId=source.ProfileId.ToString("D"),branchId=source.BranchId.ToString("D"),hasActiveSession=source.HasActiveSession,
                    hasPendingFish=source.HasPendingFish,sourceModified=false,backupConfirmed=false,restored=false}));
            }else{
                var backup=FloatJournalArchive.Backup(source,args[2]);
                Console.WriteLine(json.Serialize(new {success=true,action="backup",source=source.SourcePath,destination=backup.DestinationPath,
                    length=backup.Length,sha256=backup.Sha256,tipSha256=backup.TipSha256,sequence=backup.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    profileId=backup.ProfileId.ToString("D"),branchId=backup.BranchId.ToString("D"),hasActiveSession=source.HasActiveSession,
                    hasPendingFish=source.HasPendingFish,sourceModified=false,backupConfirmed=true,restored=false}));
            }
            return 0;
        }catch(Exception error){
            Console.WriteLine(json.Serialize(new {success=false,error=error.GetType().Name,message=error.Message,backupConfirmed=false,restored=false,
                instruction="Source is unchanged. Any created destination is unconfirmed; do not activate or overwrite it."}));
            return 1;
        }
    }
}
