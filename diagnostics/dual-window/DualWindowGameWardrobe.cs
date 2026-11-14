using System;
using System.IO;
using System.Collections.Generic;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    void SendGameWardrobe(ProbeWindow pane)
    {
        GameBinding b;
        if(!GameAtlasMode||!pane.Ready||!gameViews.TryGetValue(pane,out b)||b.Document==null)return;
        var s=game.Snapshot.State;var items=new List<object>();bool enabled=s!=null&&FloatClothingCatalog.Enabled(s);
        if(enabled)foreach(var item in FloatClothingCatalog.Items){
            bool moss=item.Id=="moss-jacket";
            items.Add(new {id=item.Id,name=item.Name,description=moss?"适合草滩的苔绿色钓鱼外套。":"适合海岸的蓝色防风外套。",priceMinor=item.PriceMinor,
                owned=s.OwnedClothingIds.Contains(item.Id),equipped=s.WornClothingId==item.Id,color=moss?"#586849":"#355c79"});
        }
        Send(pane,new {kind="game-wardrobe-state",documentId=pane.Ticket.DocumentId,revision=checked(++b.WardrobeRevision),
            state=new {available=enabled,items=items.ToArray(),activeId=enabled?s.WornClothingId:null}});
    }
    void ReceiveGameWardrobe(ProbeWindow pane,GameBinding b,Dictionary<string,object> m,string transport)
    {
        string action,id;long sequence;
        if(m.Count!=7||!StringField(m,"action",out action)||!StringField(m,"itemId",out id)||id.Length>64||
            (action!="buy"&&action!="wear"&&action!="remove")||!GameEquipmentExpectedSequence(m,out sequence)){
            GameReply(pane,transport,"invalid-intent");return;
        }
        AdvanceActivity();var view=game.Snapshot;var s=view.State;
        if(!game.CanControl(b.View,b.Document,b.Lease)){GameReply(pane,transport,"control-required");return;}
        if(!GameAtlasMode||s==null||!FloatClothingCatalog.Enabled(s)){GameReply(pane,transport,"wardrobe-unavailable");return;}
        if(!view.MenuOpen||s.Session!=null||GameCampActive||b.Shop!=null||b.Spares!=null||b.Equipment!=null){
            GameReply(pane,transport,"wardrobe-busy");return;
        }
        if(sequence!=s.Sequence){GameReply(pane,transport,"wardrobe-stale");Broadcast();return;}
        if(FloatClothingCatalog.Find(id)==null){GameReply(pane,transport,"wardrobe-unavailable");return;}
        var command=new ChangeFloatClothing(s.ProfileId,s.BranchId,Guid.NewGuid(),Guid.NewGuid(),action=="buy"?FloatClothingAction.Buy:action=="wear"?FloatClothingAction.Wear:FloatClothingAction.Remove,id,sequence,s.Rig.Revision,s.RulesVersion);
        try{
            var result=game.ExecuteUser(b.View,b.Document,b.Lease,command);
            string code=result.OperationSucceeded?"wardrobe-updated":result.Code=="wallet-insufficient"?"wardrobe-insufficient-funds":
                result.Code=="clothing-already-owned"?"wardrobe-already-owned":result.Code=="clothing-not-owned"?"wardrobe-not-owned":
                result.Code=="clothing-unchanged"||result.Code=="clothing-not-worn"?"wardrobe-unchanged":"wardrobe-unavailable";
            GameReply(pane,transport,code,result);
        }catch(FloatSaveUncertainException){ExitCode=4;GameReply(pane,transport,"save-outcome-unknown");}
        catch(IOException){GameReply(pane,transport,"wardrobe-not-written");}
        Broadcast();
    }
}
