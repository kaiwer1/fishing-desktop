using System;
using System.Collections.Generic;

// Fixed diagnostic failure envelope only. This does not grant authority or
// clear host policy; the caller must validate current view, role and source.
static class SceneFaultRecord
{
    static readonly HashSet<string> Fields=new HashSet<string>(new[]{"kind","documentId","lease","transportId","scene","reason"},StringComparer.Ordinal);
    static bool DocumentId(string value)
    {
        Guid parsed;
        return value!=null && value.Length==36 && Guid.TryParseExact(value,"D",out parsed);
    }
    internal static bool TryRead(Dictionary<string,object> message,string expectedDocument,out string reason)
    {
        reason=null;
        if(message==null || message.Count!=Fields.Count || !DocumentId(expectedDocument))return false;
        foreach(var pair in message)if(!Fields.Contains(pair.Key) || !(pair.Value is string))return false;
        string document=(string)message["documentId"],transport=(string)message["transportId"];
        if(!DocumentId(document) || !String.Equals(document,expectedDocument,StringComparison.Ordinal) || !DocumentId(transport) ||
            (string)message["kind"]!="scene-fault" || (string)message["scene"]!="webgl3d" || (string)message["lease"]!=String.Empty)return false;
        string candidate=(string)message["reason"];
        if(candidate!="webgl-context-lost" && candidate!="webgl-render-failed")return false;
        reason=candidate;return true;
    }
}
