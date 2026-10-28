using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    // Exact wire intents: kind/documentId/lease/transportId/action plus
    // replace-slot: slot,itemId(null or D-Guid),inputSequence,expectedSequence
    // move-line: lineId,reelId(null or D-Guid),inputSequence,expectedSequence
    // retry: operationId; cancel: no additional fields.
    // All versions are canonical Int64 decimal strings. Only the host creates
    // durable request/event IDs and captures current equipment revisions.
    sealed class GameEquipmentPending
    {
        internal string Id,Action,WireLease;
        internal FloatViewTicket View;
        internal FloatDocumentTicket Document;
        internal FloatControlLease Lease;
        internal FloatCommand Command;
        internal bool Retry;
    }
    bool gameEquipmentPrewriteFaultArmed,gameEquipmentCommitting;
    void ArmGameEquipmentPrewriteFailure()
    {
        if(!GameSelfTest)throw new InvalidOperationException("Equipment fault injection is self-test only.");
        if(gameEquipmentPrewriteFaultArmed)throw new InvalidOperationException("An equipment fault is already armed.");
        gameEquipmentPrewriteFaultArmed=true;
    }
    void GameEquipmentSaveFault(FloatSaveFaultPoint point)
    {
        if(GameSelfTest&&gameEquipmentCommitting&&gameEquipmentPrewriteFaultArmed&&point==FloatSaveFaultPoint.BeforeWrite){
            gameEquipmentPrewriteFaultArmed=false;throw new IOException("Scripted equipment failure before any frame write.");
        }
    }
    void ClearGameEquipment(ProbeWindow pane=null)
    {
        if(pane==null){foreach(var binding in gameViews.Values)binding.Equipment=null;return;}
        GameBinding current;if(gameViews.TryGetValue(pane,out current))current.Equipment=null;
    }
    static string GameEquipmentNumber(long value){return value.ToString(CultureInfo.InvariantCulture);}
    static string GameEquipmentId(Guid value){return value==Guid.Empty?null:value.ToString("D");}
    static string GameEquipmentSlotName(FloatEquipmentSlot slot)
    {
        switch(slot){case FloatEquipmentSlot.Rod:return "rod";case FloatEquipmentSlot.Reel:return "reel";case FloatEquipmentSlot.Hook:return "hook";case FloatEquipmentSlot.Bobber:return "bobber";case FloatEquipmentSlot.Sinker:return "sinker";default:throw new ArgumentOutOfRangeException("slot");}
    }
    static bool GameEquipmentSlot(string name,out FloatEquipmentSlot slot)
    {
        slot=FloatEquipmentSlot.Rod;if(name=="rod")return true;
        if(name=="reel"){slot=FloatEquipmentSlot.Reel;return true;}if(name=="hook"){slot=FloatEquipmentSlot.Hook;return true;}
        if(name=="bobber"){slot=FloatEquipmentSlot.Bobber;return true;}if(name=="sinker"){slot=FloatEquipmentSlot.Sinker;return true;}return false;
    }
    static string GameEquipmentKindName(FloatEquipmentKind kind)
    {
        switch(kind){case FloatEquipmentKind.Rod:return "rod";case FloatEquipmentKind.Reel:return "reel";case FloatEquipmentKind.Line:return "line";case FloatEquipmentKind.Hook:return "hook";case FloatEquipmentKind.Bobber:return "bobber";case FloatEquipmentKind.Sinker:return "sinker";default:throw new ArgumentOutOfRangeException("kind");}
    }
    static string GameEquipmentPlaceName(FloatEquipmentPlace place)
    {
        switch(place){case FloatEquipmentPlace.Inventory:return "inventory";case FloatEquipmentPlace.RigSlot:return "rig-slot";case FloatEquipmentPlace.ReelSpool:return "reel-spool";case FloatEquipmentPlace.Lost:return "lost";default:throw new ArgumentOutOfRangeException("place");}
    }
    static bool GameEquipmentExpectedSequence(Dictionary<string,object> message,out long value)
    {
        string text;value=0;
        return StringField(message,"expectedSequence",out text)&&text.Length>0&&text.Length<=19&&
            Int64.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out value)&&value>=0&&text==GameEquipmentNumber(value);
    }
    static bool GameEquipmentKeys(Dictionary<string,object> message,params string[] extra)
    {
        if(message.Count!=5+extra.Length)return false;
        var keys=new HashSet<string>(StringComparer.Ordinal){"kind","documentId","lease","transportId","action"};foreach(string key in extra)keys.Add(key);
        foreach(string key in message.Keys)if(!keys.Contains(key))return false;return true;
    }
    bool GameEquipmentAuthorized(ProbeWindow pane,GameBinding binding,string document,string lease)
    {
        GameBinding current;
        return GameAuthorized(pane,document,lease,out current)&&Object.ReferenceEquals(binding,current)&&game.CanControl(binding.View,binding.Document,binding.Lease);
    }
    static bool GameEquipmentBindingCurrent(GameBinding binding,GameEquipmentPending pending)
    {
        return pending!=null&&Object.ReferenceEquals(binding.View,pending.View)&&Object.ReferenceEquals(binding.Document,pending.Document)&&
            Object.ReferenceEquals(binding.Lease,pending.Lease)&&binding.WireLease==pending.WireLease;
    }
    static string GameEquipmentReason(FloatOwnerSnapshot snapshot,bool authorized)
    {
        if(snapshot.Faulted)return "save-outcome-unknown";if(snapshot.ShutdownConfirmed)return "owner-stopped";
        if(snapshot.State==null||snapshot.State.SchemaVersion!=3||snapshot.State.Equipment==null)return "equipment-schema-required";
        if(!authorized)return "control-required";if(!snapshot.MenuOpen)return "menu-required";
        if(snapshot.PendingResult)return "result-pending";if(snapshot.State.Session!=null)return "session-busy";return null;
    }
    void ReconcileGameEquipment(GameBinding binding,FloatOwnerSnapshot snapshot,bool authorized)
    {
        var pending=binding.Equipment;
        if(pending!=null&&(GameEquipmentReason(snapshot,authorized)!=null||!GameEquipmentBindingCurrent(binding,pending)||
            snapshot.State.Sequence!=pending.Command.ExpectedSequence))binding.Equipment=null;
    }
    void SendGameEquipment(ProbeWindow pane)
    {
        GameBinding binding;if(game==null||!pane.Ready||!gameViews.TryGetValue(pane,out binding)||binding.Document==null)return;
        var snapshot=game.ReadView(binding.View,binding.Document);
        bool authorized=GameEquipmentAuthorized(pane,binding,pane.Ticket.DocumentId,binding.WireLease);
        string reason=GameEquipmentReason(snapshot,authorized);ReconcileGameEquipment(binding,snapshot,authorized);
        var state=snapshot.State;var equipment=state!=null&&state.SchemaVersion==3?state.Equipment:null;
        var items=new List<object>();var issues=new List<object>();
        if(equipment!=null){
            foreach(var item in equipment.Instances)items.Add(new{
                id=item.Id.ToString("D"),definitionKey=item.DefinitionKey,kind=GameEquipmentKindName(item.Kind),revision=GameEquipmentNumber(item.Revision),
                location=GameEquipmentPlaceName(item.Location.Place),slot=item.Location.Slot.HasValue?GameEquipmentSlotName(item.Location.Slot.Value):null,
                reelId=item.Location.Place==FloatEquipmentPlace.ReelSpool?item.Location.OwnerId.ToString("D"):null,
                initialLineMm=item.InitialLineMm,remainingLineMm=item.RemainingLineMm});
            // The current development cast target explicitly requires 15 m;
            // this is the same flat-water target used by InitializeGameCast.
            var readiness=equipment.EvaluateCastReadiness(FloatEquipmentGate.Idle,15000,state.BaitCount,state.Stamina,state.Rules.CastStaminaCost);
            foreach(var issue in readiness.Issues)issues.Add(new{code=issue.Code,slot=issue.Slot.HasValue?GameEquipmentSlotName(issue.Slot.Value):null,
                itemId=GameEquipmentId(issue.ItemId),relatedId=GameEquipmentId(issue.RelatedId)});
        }
        object retry=null;var pending=binding.Equipment;
        if(reason==null&&pending!=null&&pending.Retry)retry=new{operationId=pending.Id,action=pending.Action};
        Send(pane,new{kind="game-equipment-state",documentId=pane.Ticket.DocumentId,revision=checked(++binding.EquipmentRevision),state=new{
            schemaVersion=state==null?0:state.SchemaVersion,available=reason==null,reason=reason,lease=authorized?binding.WireLease:null,
            stateSequence=equipment==null?null:GameEquipmentNumber(state.Sequence),rigRevision=equipment==null?null:GameEquipmentNumber(equipment.RigRevision),
            inventoryRevision=equipment==null?null:GameEquipmentNumber(equipment.InventoryRevision),equipmentVersion=equipment==null?null:GameEquipmentNumber(equipment.RulesVersion),
            items=items,issues=issues,retry=retry}});
    }
    void GameEquipmentReply(ProbeWindow pane,string transport,string code)
    {GameReply(pane,transport,code);Broadcast();}
    void ReceiveGameEquipment(ProbeWindow pane,GameBinding binding,Dictionary<string,object> message,string document,string lease,string transport)
    {
        // Keep this boundary independently safe even when invoked by an
        // internal probe rather than ReceiveGame's initial authorization gate.
        if(!GameEquipmentAuthorized(pane,binding,document,lease)){GameEquipmentReply(pane,transport,"control-required");return;}
        string action,slotName=null,operationId=null;object raw;long inputSequence=0,expectedSequence=0;Guid item=Guid.Empty,reel=Guid.Empty,opaque;
        FloatEquipmentSlot slot=FloatEquipmentSlot.Rod;bool valid=false,nullableItem=false,nullableReel=false;
        if(!StringField(message,"action",out action)){GameEquipmentReply(pane,transport,"invalid-intent");return;}
        if(action=="replace-slot"){
            valid=GameEquipmentKeys(message,"slot","itemId","inputSequence","expectedSequence")&&StringField(message,"slot",out slotName)&&GameEquipmentSlot(slotName,out slot)&&
                message.TryGetValue("itemId",out raw)&&GameGuid(raw,true,out item)&&GameInputSequence(message,out inputSequence)&&GameEquipmentExpectedSequence(message,out expectedSequence);
            nullableItem=item==Guid.Empty;
        }else if(action=="move-line"){
            valid=GameEquipmentKeys(message,"lineId","reelId","inputSequence","expectedSequence")&&message.TryGetValue("lineId",out raw)&&GameGuid(raw,false,out item)&&
                message.TryGetValue("reelId",out raw)&&GameGuid(raw,true,out reel)&&GameInputSequence(message,out inputSequence)&&GameEquipmentExpectedSequence(message,out expectedSequence);
            nullableReel=reel==Guid.Empty;
        }else if(action=="retry")valid=GameEquipmentKeys(message,"operationId")&&StringField(message,"operationId",out operationId)&&GameGuid(operationId,false,out opaque);
        else if(action=="cancel")valid=GameEquipmentKeys(message);
        if(!valid){GameEquipmentReply(pane,transport,"invalid-intent");return;}
        AdvanceActivity();
        if(!GameEquipmentAuthorized(pane,binding,document,lease)){GameEquipmentReply(pane,transport,"control-required");return;}
        // Consume a fresh authorized edge even when business state rejects it.
        // Replaying it after opening a menu cannot turn it into a future edit.
        if(action=="replace-slot"||action=="move-line"){
            if(inputSequence<=binding.EquipmentHighWater){GameEquipmentReply(pane,transport,"stale-input");return;}binding.EquipmentHighWater=inputSequence;
        }
        var snapshot=game.Snapshot;string reason=GameEquipmentReason(snapshot,true);ReconcileGameEquipment(binding,snapshot,true);
        if(reason!=null){GameEquipmentReply(pane,transport,reason);return;}
        if(action=="cancel"){binding.Equipment=null;GameEquipmentReply(pane,transport,"equipment-cancelled");return;}
        if(action=="retry"){
            var pending=binding.Equipment;
            if(pending==null||!pending.Retry||pending.Id!=operationId||!GameEquipmentBindingCurrent(binding,pending)){GameEquipmentReply(pane,transport,"equipment-intent-not-current");return;}
            CommitGameEquipment(pane,binding,pending,document,lease,transport);return;
        }
        if(binding.Equipment!=null&&binding.Equipment.Retry){GameEquipmentReply(pane,transport,"equipment-retry-required");return;}
        var state=snapshot.State;
        if(expectedSequence!=state.Sequence){GameEquipmentReply(pane,transport,"version-conflict");return;}
        FloatCommand command;
        if(action=="replace-slot")command=new ReplaceFloatSlot(state.ProfileId,state.BranchId,Guid.NewGuid(),Guid.NewGuid(),slot,nullableItem?(Guid?)null:item,
            state.Sequence,state.Rig.Revision,state.RulesVersion,state.Equipment.InventoryRevision,state.Equipment.RulesVersion);
        else command=new MoveFloatLine(state.ProfileId,state.BranchId,Guid.NewGuid(),Guid.NewGuid(),item,nullableReel?(Guid?)null:reel,
            state.Sequence,state.Rig.Revision,state.RulesVersion,state.Equipment.InventoryRevision,state.Equipment.RulesVersion);
        var next=new GameEquipmentPending{Id=Guid.NewGuid().ToString("D"),Action=action,View=binding.View,Document=binding.Document,Lease=binding.Lease,
            WireLease=binding.WireLease,Command=command};binding.Equipment=next;
        CommitGameEquipment(pane,binding,next,document,lease,transport);
    }
    void CommitGameEquipment(ProbeWindow pane,GameBinding binding,GameEquipmentPending pending,string document,string lease,string transport)
    {
        if(!GameEquipmentAuthorized(pane,binding,document,lease)||!GameEquipmentBindingCurrent(binding,pending)){
            GameEquipmentReply(pane,transport,"control-required");return;
        }
        var snapshot=game.Snapshot;string reason=GameEquipmentReason(snapshot,true);
        if(reason!=null){binding.Equipment=null;GameEquipmentReply(pane,transport,reason);return;}
        if(snapshot.State.Sequence!=pending.Command.ExpectedSequence){binding.Equipment=null;GameEquipmentReply(pane,transport,"version-conflict");return;}
        gameEquipmentCommitting=true;
        try{
            var result=game.ExecuteUser(pending.View,pending.Document,pending.Lease,pending.Command);
            binding.Equipment=null;GameReply(pane,transport,result.Code,result);Broadcast();
        }catch(FloatSaveUncertainException){
            ClearGameEquipment();ExitCode=4;GameReply(pane,transport,"save-outcome-unknown");Broadcast();
        }catch(IOException){
            pending.Retry=true;binding.Equipment=pending;GameReply(pane,transport,"equipment-not-written");Broadcast();
        }finally{gameEquipmentCommitting=false;}
    }
}
