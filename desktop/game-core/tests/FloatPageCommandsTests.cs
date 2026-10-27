using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;
using FishingDesktop.GameCore;

static class FloatPageCommandsTests
{
    static int passed,failed,nextId;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,5});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    static FloatGameState New()
    {
        var rules=new FloatGameRules(1,"fixture-float","fixture-worm",4,100,2,Int64.MaxValue,new[]{new FloatSpeciesRule("carp",100000,2000000,1000)});
        var rig=new FixedFloatRig(G(),3,"fixture-float",G(),G(),G(),100000,G(),G(),G());
        return FloatGameState.CreateNew(G(),G(),100,100,G(),"fixture-worm",20,rig,rules);
    }
    static Dictionary<string,object> Intent(FloatGameState s,string action="cast")
    {
        var intent=new Dictionary<string,object>{{"action",action},{"requestId",G().ToString("D")},{"eventId",G().ToString("D")},{"sessionId",action=="sell"?"":G().ToString("D")},{"expectedSequence",s.Sequence.ToString(CultureInfo.InvariantCulture)},{"rigRevision",s.Rig.Revision.ToString(CultureInfo.InvariantCulture)},{"rulesVersion",s.RulesVersion.ToString(CultureInfo.InvariantCulture)}};
        if(action=="sell")intent.Add("fishIds",new object[]{G().ToString("D"),G().ToString("D")});return intent;
    }
    static Dictionary<string,object> JsonRoundTrip(Dictionary<string,object> source){return (Dictionary<string,object>)Json.DeserializeObject(Json.Serialize(source));}
    static FloatCommand Parse(Dictionary<string,object> intent,FloatGameState s){FloatCommand command;Need(FloatPageCommands.TryParse(intent,s,out command)&&command!=null,"Valid page intent rejected.");return command;}
    static void Invalid(Dictionary<string,object> intent,FloatGameState s)
    {FloatCommand command=new CastFloat(G(),G(),G(),G(),G(),0,1,1);Need(!FloatPageCommands.TryParse(intent,s,out command)&&command==null,"Malformed intent returned a command.");}
    static Guid Id(Dictionary<string,object> d,string key){return Guid.Parse((string)d[key]);}
    static FloatCommand Expected(Dictionary<string,object> d,FloatGameState s)
    {
        Guid request=Id(d,"requestId"),businessEvent=Id(d,"eventId");string action=(string)d["action"];
        if(action=="sell"){var ids=new List<Guid>();foreach(object item in (IEnumerable)d["fishIds"])ids.Add(Guid.Parse((string)item));return new SellFloatFish(s.ProfileId,s.BranchId,request,businessEvent,ids,s.Sequence,s.Rig.Revision,s.RulesVersion);}
        Guid session=Id(d,"sessionId");if(action=="cast")return new CastFloat(s.ProfileId,s.BranchId,request,businessEvent,session,s.Sequence,s.Rig.Revision,s.RulesVersion);
        if(action=="recall")return new RecallFloat(s.ProfileId,s.BranchId,request,businessEvent,session,s.Sequence,s.Rig.Revision,s.RulesVersion);
        return new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,request,businessEvent,session,s.Sequence,s.Rig.Revision,s.RulesVersion);
    }
    static int Main()
    {
        foreach(string action in new[]{"cast","recall","release-current","sell"}){
            string name=action;Test("real JSON "+name+" converts to its exact typed command",()=>{var state=New();var intent=JsonRoundTrip(Intent(state,name));var command=Parse(intent,state);var expected=Expected(intent,state);Need(command.GetType()==expected.GetType()&&command.Fingerprint==expected.Fingerprint&&command.ProfileId==state.ProfileId&&command.BranchId==state.BranchId,"Conversion changed typed facts or context identities.");});
        }
        Test("all four stable JSON retries preserve request event session and fingerprint",()=>{
            var s=New();foreach(string action in new[]{"cast","recall","release-current","sell"}){var intent=Intent(s,action);string json=Json.Serialize(intent);var first=Parse((Dictionary<string,object>)Json.DeserializeObject(json),s);var retry=Parse((Dictionary<string,object>)Json.DeserializeObject(json),s);Need(first.RequestId==retry.RequestId&&first.EventId==retry.EventId&&first.SessionId==retry.SessionId&&first.Fingerprint==retry.Fingerprint,"Parser generated a replacement identity during retry.");}
        });
        Test("null intent or unavailable context always clears the output command",()=>{var s=New();Invalid(null,s);Invalid(Intent(s),null);Invalid(null,null);});
        Test("extra profile branch host facts and misspelled keys cannot cross the page boundary",()=>{
            var s=New();foreach(string key in new[]{"profileId","branchId","species","speciesKey","mass","massMg","price","saleValueMinor","host","recover","reason","Action"}){var intent=Intent(s);intent.Add(key,"forbidden");Invalid(JsonRoundTrip(intent),s);}
            var caseInsensitive=new Dictionary<string,object>(Intent(s),StringComparer.OrdinalIgnoreCase);caseInsensitive.Remove("requestId");caseInsensitive.Add("RequestId",G().ToString("D"));Invalid(caseInsensitive,s);
        });
        Test("unknown internal case-variant and nonstring actions are rejected",()=>{
            var s=New();foreach(object action in new object[]{"host-fish","recover-session","startup","Cast","SELL","",null,1,true}){var intent=Intent(s);intent["action"]=action;Invalid(JsonRoundTrip(intent),s);}
        });
        Test("every required property rejects omission and explicit null",()=>{
            var s=New();foreach(string action in new[]{"cast","sell"}){var valid=Intent(s,action);foreach(string key in new List<string>(valid.Keys)){var missing=new Dictionary<string,object>(valid);missing.Remove(key);Invalid(missing,s);var nil=new Dictionary<string,object>(valid);nil[key]=null;Invalid(JsonRoundTrip(nil),s);}}
        });
        Test("JSON numeric tokens booleans and objects never convert to integer strings",()=>{
            var s=New();foreach(string key in new[]{"expectedSequence","rigRevision","rulesVersion"})foreach(object value in new object[]{0,1L,1.0,1.25,true,new object[0],new Dictionary<string,object>(),9007199254740993L}){var intent=Intent(s);intent[key]=value;Invalid(JsonRoundTrip(intent),s);}
            var actual=(Dictionary<string,object>)Json.DeserializeObject("{\"action\":\"cast\",\"requestId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"eventId\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"sessionId\":\"cccccccc-cccc-cccc-cccc-cccccccccccc\",\"expectedSequence\":9007199254740993,\"rigRevision\":\"3\",\"rulesVersion\":\"1\"}");Invalid(actual,s);
        });
        Test("canonical decimal grammar rejects rounding overflow symbols whitespace and zero versions",()=>{
            var s=New();foreach(string key in new[]{"expectedSequence","rigRevision","rulesVersion"})foreach(string value in new[]{"00","01","+1","-1"," 1","1 ","1.0","1e3","١","","9223372036854775808","18446744073709551615"}){var intent=Intent(s);intent[key]=value;Invalid(intent,s);}
            foreach(string key in new[]{"rigRevision","rulesVersion"}){var intent=Intent(s);intent[key]="0";Invalid(intent,s);}
            var maximum=Intent(s);maximum["expectedSequence"]=Int64.MaxValue.ToString(CultureInfo.InvariantCulture);Need(Parse(maximum,s).ExpectedSequence==Int64.MaxValue,"Lossless max long string was rounded.");Need(Parse(Intent(s),s).ExpectedSequence==0,"Canonical sequence zero was rejected.");
        });
        Test("all identity fields require nonempty lowercase canonical Guid D strings",()=>{
            var s=New();foreach(string key in new[]{"requestId","eventId","sessionId"})foreach(object value in new object[]{"AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA","aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}"," aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",Guid.Empty.ToString("D"),"",1,Guid.NewGuid()}){var intent=Intent(s);intent[key]=value;Invalid(intent,s);}
        });
        Test("sale alone requires an empty session and other actions require a nonempty session",()=>{
            var s=New();foreach(object session in new object[]{G().ToString("D"),Guid.Empty.ToString("D")," ",1,null}){var intent=Intent(s,"sell");intent["sessionId"]=session;Invalid(intent,s);}
            foreach(string action in new[]{"cast","recall","release-current"}){var intent=Intent(s,action);intent["sessionId"]="";Invalid(intent,s);intent=Intent(s,action);intent.Add("fishIds",new object[]{G().ToString("D")});Invalid(intent,s);}
        });
        Test("fish selection rejects duplicates empty identities noncanonical IDs and nonarray shapes",()=>{
            var s=New();string id=G().ToString("D");foreach(object items in new object[]{new object[0],new object[]{id,id},new object[]{Guid.Empty.ToString("D")},new object[]{"AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"},new object[]{id,1},new object[]{null},id,new List<object>{id},new Dictionary<string,object>{{"0",id}}}){var intent=Intent(s,"sell");intent["fishIds"]=items;Invalid(intent,s);}
        });
        Test("object array and ArrayList selections are copied sorted and bounded at fifty fish",()=>{
            var s=New();var items=new object[50];for(int i=0;i<items.Length;i++)items[i]=G().ToString("D");Array.Reverse(items);var intent=Intent(s,"sell");intent["fishIds"]=items;var command=(SellFloatFish)Parse(intent,s);Need(command.FishIds.Count==50,"Fifty frozen fish IDs rejected.");string fingerprint=command.Fingerprint;items[0]=G().ToString("D");Need(command.Fingerprint==fingerprint,"Caller mutation changed selected fish.");for(int i=1;i<command.FishIds.Count;i++)Need(command.FishIds[i-1].CompareTo(command.FishIds[i])<0,"Sale selection was not canonicalized.");
            intent=Intent(s,"sell");intent["fishIds"]=new ArrayList{G().ToString("D")};Need(((SellFloatFish)Parse(intent,s)).FishIds.Count==1,"ArrayList JSON array rejected.");intent["fishIds"]=new string[]{G().ToString("D")};Need(((SellFloatFish)Parse(intent,s)).FishIds.Count==1,"Covariant string array of valid JSON IDs rejected.");var tooMany=new object[51];for(int i=0;i<tooMany.Length;i++)tooMany[i]=G().ToString("D");intent["fishIds"]=tooMany;Invalid(intent,s);
        });
        Test("parsing preserves stale versions and foreign fish for reducer rejection without executing",()=>{
            var s=New();var stale=Intent(s);stale["expectedSequence"]="1";var parsed=Parse(stale,s);var rejected=FloatGameReducer.Reduce(s,parsed,s.Rules);Need(!rejected.HasCandidate&&rejected.Code=="version-conflict"&&s.Sequence==0&&s.BaitCount==20&&s.Stamina==100,"Parser authorized or executed stale cast.");var sale=Parse(Intent(s,"sell"),s);rejected=FloatGameReducer.Reduce(s,sale,s.Rules);Need(!rejected.HasCandidate&&rejected.Code=="fish-not-owned"&&s.WalletMinor==100&&s.Creel.Count==0,"Page selection granted fish or money.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
