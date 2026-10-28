using System;
using System.Globalization;
using System.Web.Script.Serialization;
using FishingDesktop.GameCore;

// Explicit private-file candidate preparation; never activates the result.
static class FloatProfileUpgradeTool
{
    static int Main(string[] args)
    {
        var json=new JavaScriptSerializer();
        try{
            if(args.Length!=3)throw new ArgumentException("Use <source> <independent backup> <new candidate>.");
            // Original development catalogue v1, independent of fishing rules.
            var catalogue=new FloatCommerceCatalog(1,"worm","landing-net","bread",2400,5,20,20,20,10000,1000,100);
            var result=FloatProfileUpgrade.CreateCandidate(args[0],args[1],args[2],catalogue);
            Console.WriteLine(json.Serialize(new {success=true,action="prepare-commerce-candidate",schemaVersion=2,catalogVersion=1,
                backup=result.BackupPath,candidate=result.CandidatePath,sourceSha256=result.SourceSha256,candidateSha256=result.CandidateSha256,
                profileId=result.ProfileId.ToString("D"),sourceBranchId=result.SourceBranchId.ToString("D"),candidateBranchId=result.CandidateBranchId.ToString("D"),
                sourceSequence=result.SourceSequence.ToString(CultureInfo.InvariantCulture),candidateSequence=result.CandidateSequence.ToString(CultureInfo.InvariantCulture),
                existingCandidateVerified=result.ExistingCandidateVerified,backupConfirmed=true,candidateConfirmed=true,sourceModified=false,activated=false,
                instruction="Candidate prepared only. Source and backup are retained; no running profile was replaced."}));
            return 0;
        }catch(Exception error){
            Console.WriteLine(json.Serialize(new {success=false,error=error.GetType().Name,message=error.Message,
                candidateConfirmed=false,activated=false,instruction="Source was not changed. Retain any created files; do not overwrite or activate an unconfirmed candidate."}));
            return 1;
        }
    }
}
