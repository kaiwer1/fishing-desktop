using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;
using FishingDesktop.GameCore;

// Explicit local maintenance tool. No default source, restore, activation or
// startup lifecycle: inspecting a cast must never secretly retrieve it.
static class FloatJournalTool
{
    static object EquipmentSummary(FloatGameState state)
    {
        if(state.Equipment==null)return null;
        var equipment=state.Equipment;var missing=new List<string>();
        foreach(FloatEquipmentSlot slot in Enum.GetValues(typeof(FloatEquipmentSlot)))
            if(equipment.Equipped(slot)==null)missing.Add(slot.ToString().ToLowerInvariant());
        if(!state.Rig.LineId.HasValue)missing.Add("line");
        int lost=0,confirmedLosses=0;
        foreach(var item in equipment.Instances)if(item.Location.Place==FloatEquipmentPlace.Lost)lost++;
        foreach(var receipt in state.Receipts)if(receipt.Outcome==FloatOutcome.TerminalLost)confirmedLosses++;
        return new {instances=equipment.Instances.Count,lostInstances=lost,confirmedLosses=confirmedLosses,
            initialLineMm=equipment.InitialLineMm.ToString(CultureInfo.InvariantCulture),
            remainingLineMm=equipment.RemainingLineMm.ToString(CultureInfo.InvariantCulture),
            lostLineMm=equipment.LostLineMm.ToString(CultureInfo.InvariantCulture),
            installedLineMm=state.Rig.LineLengthMm.HasValue?state.Rig.LineLengthMm.Value.ToString(CultureInfo.InvariantCulture):null,
            missingSlots=missing.ToArray()};
    }
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
                    hasPendingFish=source.HasPendingFish,schemaVersion=source.State.SchemaVersion,equipment=EquipmentSummary(source.State),
                    sourceModified=false,backupConfirmed=false,restored=false}));
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
