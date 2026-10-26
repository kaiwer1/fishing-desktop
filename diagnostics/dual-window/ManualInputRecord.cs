using System;
using System.Collections;
using System.Collections.Generic;

// Whitelist private telemetry; never dispatch it as a state command.
static class ManualInputRecord
{
    static readonly string[] Counters={"revision","cancellationEpoch","held","presses","releases","keys","wheels","cancellations"};
    static readonly HashSet<string> Kinds=new HashSet<string>{"ready","mode","cancel","focus","down","up","wheel","key"};
    static readonly HashSet<string> Reasons=new HashSet<string>{"startup","owner-lease","host-revoke","host-deactivate","blur","hidden","pagehide","escape","pointercancel","lostcapture","capture-failed"};
    internal static Dictionary<string,object> Normalize(Dictionary<string,object> data,string document,bool eligible)
    {
        if(data==null)return null;
        object value;Guid id;
        if(!Guid.TryParseExact(document,"D",out id) || !data.TryGetValue("documentId",out value) || !Object.Equals(value,document))return null;
        if(!data.TryGetValue("kind",out value) || !(value is string) || !Kinds.Contains((string)value))return null;
        string kind=(string)value;
        var clean=new Dictionary<string,object>{{"kind",kind},{"documentId",document},{"hostEligible",eligible}};
        if(!data.TryGetValue("enabled",out value) || !(value is bool))return null;clean.Add("enabled",value);
        if(!data.TryGetValue("lastReason",out value) || !(value is string) || !Reasons.Contains((string)value))return null;clean.Add("lastReason",value);
        foreach(string name in Counters){if(!data.TryGetValue(name,out value) || !(value is int) || (int)value<0)return null;clean.Add(name,value);}
        if(!data.TryGetValue("buttonTotals",out value) || !(value is IList))return null;
        var buttons=(IList)value;if(buttons.Count!=3)return null;
        var totals=new List<object>();long down=0,up=0;
        foreach(var item in buttons){
            var button=item as Dictionary<string,object>;object d,u;
            if(button==null || !button.TryGetValue("down",out d) || !button.TryGetValue("up",out u) || !(d is int) || !(u is int) || (int)u<0 || (int)d<(int)u)return null;
            down+=(int)d;up+=(int)u;totals.Add(new{down=(int)d,up=(int)u});
        }
        if(down!=(int)clean["presses"] || up!=(int)clean["releases"])return null;clean.Add("buttonTotals",totals);
        if(kind=="down" || kind=="up"){
            if(!data.TryGetValue("button",out value) || !(value is int) || (int)value<0 || (int)value>2)return null;clean.Add("button",value);
            if(!data.TryGetValue("sourceEvent",out value) || !(value is string) || ((string)value!="pointerdown" && (string)value!="pointerup" && (string)value!="pointermove"))return null;clean.Add("sourceEvent",value);
        }
        bool input=kind=="down" || kind=="up" || kind=="wheel" || kind=="key";
        if(input || kind=="focus"){
            if(!data.TryGetValue("trusted",out value) || !(value is bool))return null;
            clean.Add("trusted",(bool)value && (!input || eligible));
        }
        return clean;
    }
}
