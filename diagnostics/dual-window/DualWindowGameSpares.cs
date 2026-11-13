using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    // Exact game-spares intents have kind/documentId/lease/transportId/action,
    // plus quote:sku, buy:offerId, retry:operationId; cancel adds no fields.
    // Price, quantities, resulting item identities and persistent command IDs
    // are host-owned. A quote only previews a discarded reducer candidate.
    sealed class GameSparesPending
    {
        internal string Id,WireLease;
        internal FloatViewTicket View;
        internal FloatDocumentTicket Document;
        internal FloatControlLease Lease;
        internal FloatEquipmentPurchaseQuote Quote;
        internal BuyFloatEquipment Command;
        internal bool Retry;
    }
    bool gameSparesPrewriteFaultArmed,gameSparesCommitting;
    void ArmGameSparesPrewriteFailure()
    {
        if(!GameSelfTest)throw new InvalidOperationException("Spares fault injection is self-test only.");
        if(gameSparesPrewriteFaultArmed)throw new InvalidOperationException("A spares fault is already armed.");
        gameSparesPrewriteFaultArmed=true;
    }
    void GameSparesSaveFault(FloatSaveFaultPoint point)
    {
        if(GameSelfTest&&gameSparesCommitting&&gameSparesPrewriteFaultArmed&&point==FloatSaveFaultPoint.BeforeWrite){
            gameSparesPrewriteFaultArmed=false;throw new IOException("Scripted spares failure before any frame write.");
        }
    }
    void ClearGameSpares(ProbeWindow pane=null)
    {
        if(pane==null){foreach(var binding in gameViews.Values)binding.Spares=null;return;}
        GameBinding current;if(gameViews.TryGetValue(pane,out current))current.Spares=null;
    }
    static string GameSparesNumber(long value){return value.ToString(CultureInfo.InvariantCulture);}
    static string GameSparesKind(FloatEquipmentKind kind)
    {
        switch(kind){case FloatEquipmentKind.Hook:return "hook";case FloatEquipmentKind.Bobber:return "bobber";case FloatEquipmentKind.Sinker:return "sinker";case FloatEquipmentKind.Line:return "line";default:throw new ArgumentOutOfRangeException("kind");}
    }
    static bool GameSparesSku(string value)
    {
        if(String.IsNullOrEmpty(value)||value.Length>96)return false;
        foreach(char c in value)if(!((c>='a'&&c<='z')||(c>='0'&&c<='9')||c=='-'||c=='_'||c=='.'))return false;return true;
    }
    static bool GameSparesKeys(Dictionary<string,object> message,params string[] extra)
    {
        if(message.Count!=5+extra.Length)return false;
        var keys=new HashSet<string>(StringComparer.Ordinal){"kind","documentId","lease","transportId","action"};foreach(string key in extra)keys.Add(key);
        foreach(string key in message.Keys)if(!keys.Contains(key))return false;return true;
    }
    bool GameSparesAuthorized(ProbeWindow pane,GameBinding binding,string document,string lease)
    {
        GameBinding current;
        return GameAuthorized(pane,document,lease,out current)&&Object.ReferenceEquals(binding,current)&&game.CanControl(binding.View,binding.Document,binding.Lease);
    }
    static bool GameSparesBindingCurrent(GameBinding binding,GameSparesPending pending)
    {
        return pending!=null&&Object.ReferenceEquals(binding.View,pending.View)&&Object.ReferenceEquals(binding.Document,pending.Document)&&
            Object.ReferenceEquals(binding.Lease,pending.Lease)&&binding.WireLease==pending.WireLease;
    }
    static string GameSparesReason(FloatOwnerSnapshot snapshot,bool authorized)
    {
        if(snapshot.Faulted)return "save-outcome-unknown";if(snapshot.ShutdownConfirmed)return "owner-stopped";
        if(snapshot.State==null||snapshot.State.SchemaVersion!=3||snapshot.State.Equipment==null)return "spares-schema-required";
        if(snapshot.State.EquipmentOffers==null)return "spares-not-configured";
        if(!authorized)return "control-required";if(!snapshot.MenuOpen)return "menu-required";
        if(snapshot.PendingResult)return "result-pending";if(snapshot.State.Session!=null)return "session-busy";return null;
    }
    void ReconcileGameSpares(GameBinding binding,FloatOwnerSnapshot snapshot,bool authorized)
    {
        var pending=binding.Spares;
        if(pending!=null&&(GameSparesReason(snapshot,authorized)!=null||!GameSparesBindingCurrent(binding,pending)||
            snapshot.State.Sequence!=pending.Command.ExpectedSequence||snapshot.State.EquipmentOffers.Version!=pending.Quote.CatalogVersion||
            snapshot.State.EquipmentOffers.Signature!=pending.Quote.CatalogSignature))binding.Spares=null;
    }
    void SendGameSpares(ProbeWindow pane)
    {
        GameBinding binding;if(game==null||!pane.Ready||!gameViews.TryGetValue(pane,out binding)||binding.Document==null)return;
        var snapshot=game.ReadView(binding.View,binding.Document);
        bool authorized=GameSparesAuthorized(pane,binding,pane.Ticket.DocumentId,binding.WireLease);
        string reason=GameSparesReason(snapshot,authorized);ReconcileGameSpares(binding,snapshot,authorized);
        var state=snapshot.State;bool equipmentProfile=state!=null&&state.SchemaVersion==3&&state.Equipment!=null;
        var offers=equipmentProfile?state.EquipmentOffers:null;var items=new List<object>();
        if(offers!=null)foreach(var offer in offers.Offers)items.Add(new{sku=offer.Sku,definitionKey=offer.DefinitionKey,kind=GameSparesKind(offer.Kind),priceMinor=GameSparesNumber(offer.PriceMinor),lineLengthMm=offer.LineLengthMm});
        object quote=null,retry=null;var pending=binding.Spares;
        if(reason==null&&pending!=null){
            if(pending.Retry)retry=new{operationId=pending.Id};
            else{var offer=pending.Quote;quote=new{offerId=pending.Id,sku=offer.Sku,priceMinor=GameSparesNumber(offer.PriceMinor),definitionKey=offer.DefinitionKey,kind=GameSparesKind(offer.Kind),lineLengthMm=offer.LineLengthMm};}
        }
        Send(pane,new{kind="game-spares-state",documentId=pane.Ticket.DocumentId,revision=checked(++binding.SparesRevision),state=new{
            schemaVersion=state==null?0:state.SchemaVersion,available=reason==null,reason=reason,lease=authorized?binding.WireLease:null,
            stateSequence=equipmentProfile?GameSparesNumber(state.Sequence):null,catalogVersion=offers==null?null:GameSparesNumber(offers.Version),
            items=items,walletMinor=equipmentProfile?GameSparesNumber(state.WalletMinor):null,instancesCount=equipmentProfile?state.Equipment.Instances.Count:0,
            quote=quote,retry=retry}});
    }
    void GameSparesReply(ProbeWindow pane,string transport,string code)
    {GameReply(pane,transport,code);Broadcast();}
    void ReceiveGameSpares(ProbeWindow pane,GameBinding binding,Dictionary<string,object> message,string document,string lease,string transport)
    {
        // Old/foreign messages cannot clear a newer current binding's quote.
        if(!GameSparesAuthorized(pane,binding,document,lease)){GameSparesReply(pane,transport,"control-required");return;}
        string action,sku=null,id=null;Guid opaque;bool valid=false;
        if(!StringField(message,"action",out action)){GameSparesReply(pane,transport,"invalid-intent");return;}
        if(action=="quote")valid=GameSparesKeys(message,"sku")&&StringField(message,"sku",out sku)&&GameSparesSku(sku);
        else if(action=="buy"||action=="retry")valid=GameSparesKeys(message,action=="buy"?"offerId":"operationId")&&StringField(message,action=="buy"?"offerId":"operationId",out id)&&GameGuid(id,false,out opaque);
        else if(action=="cancel")valid=GameSparesKeys(message);
        if(!valid){GameSparesReply(pane,transport,"invalid-intent");return;}
        AdvanceActivity();
        if(!GameSparesAuthorized(pane,binding,document,lease)){GameSparesReply(pane,transport,"control-required");return;}
        var snapshot=game.Snapshot;string reason=GameSparesReason(snapshot,true);ReconcileGameSpares(binding,snapshot,true);
        if(reason!=null){GameSparesReply(pane,transport,reason);return;}
        if(action=="cancel"){binding.Spares=null;GameSparesReply(pane,transport,"spares-cancelled");return;}
        var pending=binding.Spares;
        if(action=="retry"){
            if(pending==null||!pending.Retry||pending.Id!=id||!GameSparesBindingCurrent(binding,pending)){GameSparesReply(pane,transport,"spares-intent-not-current");return;}
            CommitGameSpares(pane,binding,pending,document,lease,transport);return;
        }
        if(pending!=null&&pending.Retry){GameSparesReply(pane,transport,"spares-retry-required");return;}
        if(action=="buy"){
            if(pending==null||pending.Id!=id||!GameSparesBindingCurrent(binding,pending)){GameSparesReply(pane,transport,"spares-intent-not-current");return;}
            CommitGameSpares(pane,binding,pending,document,lease,transport);return;
        }
        var state=snapshot.State;
        if(state.EquipmentOffers.Find(sku)==null){GameSparesReply(pane,transport,"spares-sku-unavailable");return;}
        try{
            var quote=FloatEquipmentPurchaseQuote.Create(state,sku);
            var command=new BuyFloatEquipment(state.ProfileId,state.BranchId,Guid.NewGuid(),Guid.NewGuid(),quote,Guid.NewGuid());
            var possible=FloatGameReducer.Reduce(state,command,state.Rules);
            if(!possible.HasCandidate){binding.Spares=null;GameSparesReply(pane,transport,possible.Code);return;}
            binding.Spares=new GameSparesPending{Id=Guid.NewGuid().ToString("D"),View=binding.View,Document=binding.Document,Lease=binding.Lease,
                WireLease=binding.WireLease,Quote=quote,Command=command};
            GameSparesReply(pane,transport,"spares-quoted");
        }catch(ArgumentException){GameSparesReply(pane,transport,"invalid-intent");}
        catch(OverflowException){GameSparesReply(pane,transport,"arithmetic-overflow");}
    }
    void CommitGameSpares(ProbeWindow pane,GameBinding binding,GameSparesPending pending,string document,string lease,string transport)
    {
        if(!GameSparesAuthorized(pane,binding,document,lease)||!GameSparesBindingCurrent(binding,pending)){
            GameSparesReply(pane,transport,"control-required");return;
        }
        var snapshot=game.Snapshot;string reason=GameSparesReason(snapshot,true);
        if(reason!=null){binding.Spares=null;GameSparesReply(pane,transport,reason);return;}
        if(snapshot.State.Sequence!=pending.Command.ExpectedSequence){binding.Spares=null;GameSparesReply(pane,transport,"version-conflict");return;}
        gameSparesCommitting=true;
        try{
            var result=game.ExecuteUser(pending.View,pending.Document,pending.Lease,pending.Command);
            binding.Spares=null;GameReply(pane,transport,result.Code,result);Broadcast();
        }catch(FloatSaveUncertainException){
            ClearGameSpares();ExitCode=4;GameReply(pane,transport,"save-outcome-unknown");Broadcast();
        }catch(IOException){
            pending.Retry=true;binding.Spares=pending;GameReply(pane,transport,"spares-not-written");Broadcast();
        }finally{gameSparesCommitting=false;}
    }
}
