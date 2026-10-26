using System;
using System.Collections.Generic;

static class SceneFaultRecordTests
{
    const string Document="a1111111-1111-1111-1111-111111111111";
    const string Transport="b2222222-2222-2222-2222-222222222222";
    static Dictionary<string,object> Fixture()
    {
        return new Dictionary<string,object>{{"kind","scene-fault"},{"documentId",Document},{"lease",""},
            {"transportId",Transport},{"scene","webgl3d"},{"reason","webgl-context-lost"}};
    }
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static bool Rejected(Dictionary<string,object> data,string expected=Document)
    {
        string reason="previous-result";
        return !SceneFaultRecord.TryRead(data,expected,out reason) && reason==null;
    }
    static bool Replaced(string key,object value)
    {
        var data=Fixture();data[key]=value;return Rejected(data);
    }
    static int Main()
    {
        try{
            string reason;var data=Fixture();
            Check(SceneFaultRecord.TryRead(data,Document,out reason) && reason=="webgl-context-lost","current document context loss is accepted");
            data["reason"]="webgl-render-failed";
            Check(SceneFaultRecord.TryRead(data,Document,out reason) && reason=="webgl-render-failed","fixed render failure is accepted");
            Check(Rejected(null),"null envelope has no output reason");
            data=Fixture();data["stack"]="arbitrary-private-text";
            Check(Rejected(data),"extra diagnostics or arbitrary payload fields are rejected");
            bool all=true;
            foreach(string key in new[]{"kind","documentId","lease","transportId","scene","reason"}){data=Fixture();data.Remove(key);all &= Rejected(data);}
            Check(all,"every required field is checked");
            Check(Rejected(Fixture(),"c3333333-3333-3333-3333-333333333333"),"old document cannot report for the current document");
            Check(Rejected(Fixture(),null) && Rejected(Fixture(),"") && Rejected(Fixture(),"not-a-document"),"invalid expected document is rejected");
            Check(Rejected(Fixture(),Document.ToUpperInvariant()),"document identity uses exact ordinal text");
            Check(Replaced("documentId",Document.Replace("-","")) && Replaced("documentId","{"+Document+"}") &&
                Replaced("documentId","g1111111-1111-1111-1111-111111111111"),"incoming document requires a valid D-format GUID");
            Check(Replaced("transportId",Transport.Replace("-","")) && Replaced("transportId","{"+Transport+"}") &&
                Replaced("transportId","not-a-transport"),"transport requires a valid D-format GUID");
            all=true;
            foreach(string key in new[]{"kind","documentId","lease","transportId","scene","reason"}){
                all &= Replaced(key,null) && Replaced(key,1) && Replaced(key,true) && Replaced(key,new Dictionary<string,object>());
            }
            Check(all,"all envelope values must be strings");
            Check(Replaced("kind","command") && Replaced("kind","SCENE-FAULT"),"command or differently cased kind is rejected");
            Check(Replaced("scene","canvas2d") && Replaced("scene","WebGL3D"),"scene name is restricted to the fixed three dimensional probe");
            Check(Replaced("lease",Transport) && Replaced("lease"," "),"failure envelope cannot carry an input lease");
            Check(Replaced("reason","Error: arbitrary-private-text") && Replaced("reason","") &&
                Replaced("reason","webgl-context-lost\nprivate-text") && Replaced("reason",new String('x',16384)),"unknown and arbitrary reason contents are rejected without output");
            data=new Dictionary<string,object>(Fixture(),StringComparer.OrdinalIgnoreCase);data.Remove("kind");data.Add("KIND","scene-fault");
            Check(Rejected(data),"case insensitive dictionaries cannot alias fixed envelope keys");
            data=Fixture();data["transportId"]=Transport.ToUpperInvariant();
            Check(SceneFaultRecord.TryRead(data,Document,out reason) && reason=="webgl-context-lost","standard D GUID hex case is accepted without normalizing document identity");
            data=Fixture();var before=new Dictionary<string,object>(data);SceneFaultRecord.TryRead(data,Document,out reason);
            all=data.Count==before.Count;foreach(var pair in before)all &= Object.ReferenceEquals(data[pair.Key],pair.Value);
            Check(all,"validation does not mutate or retain arbitrary envelope fields");
            return 0;
        }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
    }
}
