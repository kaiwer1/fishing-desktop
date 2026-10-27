using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace FishingDesktop.GameCore
{
    // Narrow page-intent conversion only. The owner must still authorize this
    // command and the reducer must validate versions, resources and ownership.
    public static class FloatPageCommands
    {
        static bool Id(object value,out Guid id)
        {
            id=Guid.Empty;var text=value as string;
            return text!=null&&Guid.TryParseExact(text,"D",out id)&&id!=Guid.Empty&&String.Equals(text,id.ToString("D"),StringComparison.Ordinal);
        }
        static bool Number(object value,bool positive,out long number)
        {
            number=0;var text=value as string;
            if(String.IsNullOrEmpty(text)||text.Length>19||(text.Length>1&&text[0]=='0'))return false;
            foreach(char item in text)if(item<'0'||item>'9')return false;
            return Int64.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out number)&&(!positive||number>0);
        }
        static bool Key(string key,bool sale)
        {
            return key=="action"||key=="requestId"||key=="eventId"||key=="sessionId"||key=="expectedSequence"||key=="rigRevision"||key=="rulesVersion"||(sale&&key=="fishIds");
        }
        public static bool TryParse(Dictionary<string,object> intent,FloatGameState context,out FloatCommand command)
        {
            command=null;if(intent==null||context==null)return false;
            // Enumerate exact spellings even if a caller supplied a dictionary
            // using a case-insensitive comparer.
            object raw;string action=null;
            foreach(var row in intent)if(row.Key=="action")action=row.Value as string;
            bool sale=action=="sell";
            if(action!="cast"&&action!="recall"&&action!="release-current"&&!sale)return false;
            if(intent.Count!=(sale?8:7))return false;
            foreach(var row in intent)if(!Key(row.Key,sale))return false;
            Guid request,businessEvent,session;
            long sequence,rig,rules;
            if(!intent.TryGetValue("requestId",out raw)||!Id(raw,out request)||!intent.TryGetValue("eventId",out raw)||!Id(raw,out businessEvent))return false;
            if(!intent.TryGetValue("expectedSequence",out raw)||!Number(raw,false,out sequence)||!intent.TryGetValue("rigRevision",out raw)||!Number(raw,true,out rig)||!intent.TryGetValue("rulesVersion",out raw)||!Number(raw,true,out rules))return false;
            if(!intent.TryGetValue("sessionId",out raw))return false;
            if(sale){
                if(!(raw is string)||(string)raw!="")return false;
                object items;if(!intent.TryGetValue("fishIds",out items))return false;
                IList list=items as object[];if(list==null)list=items as ArrayList;
                if(list==null||list.Count<1||list.Count>50)return false;
                var ids=new List<Guid>(list.Count);var unique=new HashSet<Guid>();
                foreach(object item in list){Guid id;if(!Id(item,out id)||!unique.Add(id))return false;ids.Add(id);}
                command=new SellFloatFish(context.ProfileId,context.BranchId,request,businessEvent,ids,sequence,rig,rules);return true;
            }
            if(!Id(raw,out session))return false;
            if(action=="cast")command=new CastFloat(context.ProfileId,context.BranchId,request,businessEvent,session,sequence,rig,rules);
            else if(action=="recall")command=new RecallFloat(context.ProfileId,context.BranchId,request,businessEvent,session,sequence,rig,rules);
            else command=new ReleaseCurrentFloatFish(context.ProfileId,context.BranchId,request,businessEvent,session,sequence,rig,rules);
            return true;
        }
    }
}
