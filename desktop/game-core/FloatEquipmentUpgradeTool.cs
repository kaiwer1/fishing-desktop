using System;
using System.Globalization;
using System.Web.Script.Serialization;
using FishingDesktop.GameCore;

// Explicit development mapping, never an inferred mapping or profile activation.
static class FloatEquipmentUpgradeTool
{
    static FloatEquipmentCatalog Catalogue()
    {
        return new FloatEquipmentCatalog(1,"worm",new[]{
            new FloatEquipmentDefinition("starter-rod",FloatEquipmentKind.Rod,150000,0,1000,25000),
            new FloatEquipmentDefinition("starter-reel",FloatEquipmentKind.Reel,200000),
            new FloatEquipmentDefinition("nylon-018",FloatEquipmentKind.Line,0,180),
            new FloatEquipmentDefinition("single-hook",FloatEquipmentKind.Hook,200),
            new FloatEquipmentDefinition("float-3g",FloatEquipmentKind.Bobber,3000,0,2000,3000),
            new FloatEquipmentDefinition("sinker-2_5g",FloatEquipmentKind.Sinker,2500)},
            new[]{new FloatEquipmentPairRule("starter-rod","starter-reel",true)},
            new[]{new FloatEquipmentSpoolRule("starter-reel","nylon-018",180,50000)},
            new[]{new FloatEquipmentPairRule("single-hook","worm",true)},
            new[]{new FloatEquipmentLossRule("mainline-terminal",new[]{FloatEquipmentSlot.Hook,FloatEquipmentSlot.Bobber,FloatEquipmentSlot.Sinker})});
    }
    static int Main(string[] args)
    {
        var json=new JavaScriptSerializer();
        try{
            if(args.Length!=4||args[3]!="starter-float-v1")
                throw new ArgumentException("Use <closed schema2 source> <independent backup> <new candidate> starter-float-v1. No mapping is selected by default.");
            var catalog=Catalogue();
            var policy=new FloatEquipmentMigrationPolicy(1,"float-starter",catalog,"starter-rod","starter-reel","nylon-018","single-hook","float-3g","sinker-2_5g");
            var offers=new FloatEquipmentOffers(1,catalog,new[]{
                new FloatEquipmentOffer("hook-single","single-hook",FloatEquipmentKind.Hook,35),
                new FloatEquipmentOffer("float-3g","float-3g",FloatEquipmentKind.Bobber,120),
                new FloatEquipmentOffer("sinker-2_5g","sinker-2_5g",FloatEquipmentKind.Sinker,25),
                new FloatEquipmentOffer("nylon-018-50m","nylon-018",FloatEquipmentKind.Line,400,50000)},1000000);
            var result=FloatEquipmentProfileUpgrade.CreateCandidate(args[0],args[1],args[2],policy,offers);
            Console.WriteLine(json.Serialize(new {success=true,action="prepare-equipment-candidate",schemaVersion=3,mapping=args[3],
                policyVersion=policy.Version.ToString(CultureInfo.InvariantCulture),policySignature=policy.Signature,offersSignature=offers.Signature,
                backup=result.BackupPath,candidate=result.CandidatePath,sourceSha256=result.SourceSha256,candidateSha256=result.CandidateSha256,
                profileId=result.ProfileId.ToString("D"),sourceBranchId=result.SourceBranchId.ToString("D"),candidateBranchId=result.CandidateBranchId.ToString("D"),
                sourceSequence=result.SourceSequence.ToString(CultureInfo.InvariantCulture),candidateSequence=result.CandidateSequence.ToString(CultureInfo.InvariantCulture),
                existingCandidateVerified=result.ExistingCandidateVerified,backupConfirmed=true,candidateConfirmed=true,sourceModified=false,activated=false,
                instruction="Equipment candidate prepared only. Original item identities and recorded line length are preserved. No profile was activated."}));
            return 0;
        }catch(Exception error){
            Console.WriteLine(json.Serialize(new {success=false,error=error.GetType().Name,message=error.Message,candidateConfirmed=false,activated=false,
                instruction="Source was not changed. Retain created files for inspection; do not overwrite or activate an unconfirmed candidate."}));
            return 1;
        }
    }
}
