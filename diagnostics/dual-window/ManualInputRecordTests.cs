using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

static class ManualInputRecordTests
{
    const string Document="11111111-1111-1111-1111-111111111111";
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static Dictionary<string,object> Fixture()
    {
        return Json.Deserialize<Dictionary<string,object>>("{\"kind\":\"down\",\"documentId\":\""+Document+"\",\"enabled\":true,\"lastReason\":\"owner-lease\",\"revision\":1,\"cancellationEpoch\":0,\"held\":1,\"presses\":1,\"releases\":0,\"keys\":0,\"wheels\":0,\"cancellations\":0,\"buttonTotals\":[{\"down\":1,\"up\":0},{\"down\":0,\"up\":0},{\"down\":0,\"up\":0}],\"button\":0,\"sourceEvent\":\"pointerdown\",\"trusted\":true}");
    }
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static int Main()
    {
        try{
            var data=Fixture();var result=ManualInputRecord.Normalize(data,Document,true);
            Check(result!=null && (bool)result["trusted"] && (bool)result["hostEligible"],"eligible trusted pointer retains evidence");
            result=ManualInputRecord.Normalize(data,Document,false);
            Check(result!=null && !(bool)result["trusted"] && !(bool)result["hostEligible"],"background or revoked host cannot contribute trusted input");
            Check(ManualInputRecord.Normalize(data,Guid.NewGuid().ToString("D"),true)==null,"old document evidence rejected");
            data["kind"]="focus";result=ManualInputRecord.Normalize(data,Document,false);
            Check(result!=null && (bool)result["trusted"],"disabled trusted focus preserved for return checks");
            data=Fixture();data["kind"]="key";data["keyContents"]="never-log-this";data["coordinates"]="never-log-this";
            string text=Json.Serialize(ManualInputRecord.Normalize(data,Document,true));
            Check(!text.Contains("never-log-this") && !text.Contains("sourceEvent") && !text.Contains("button\":"),"arbitrary contents and irrelevant pointer fields omitted");
            data=Fixture();data["presses"]=2;Check(ManualInputRecord.Normalize(data,Document,true)==null,"disagreeing cumulative totals rejected");
            data=Fixture();data["held"]=-1;Check(ManualInputRecord.Normalize(data,Document,true)==null,"negative counter rejected");
            data=Fixture();data["buttonTotals"]=new object[0];Check(ManualInputRecord.Normalize(data,Document,true)==null,"malformed button totals rejected");
            data=Fixture();data["sourceEvent"]="click";Check(ManualInputRecord.Normalize(data,Document,true)==null,"unrecognized pointer source rejected");
            data=Fixture();data["kind"]="command";Check(ManualInputRecord.Normalize(data,Document,true)==null,"state command is not telemetry");
            data=Fixture();data["lastReason"]="arbitrary-private-text";Check(ManualInputRecord.Normalize(data,Document,true)==null,"reason restricted to fixed labels");
            return 0;
        }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
    }
}
