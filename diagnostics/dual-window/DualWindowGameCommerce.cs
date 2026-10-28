using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    // Wire contract: every intent has kind="game-shop", documentId, lease,
    // transportId plus the EXACT fields below (extra pricing/asset fields reject):
    // quote: action, product("net"|"bait"|"food"), packs(int 1..100)
    // buy: action, quoteId(host D-Guid string)
    // equip: action, equipped(bool), inputSequence(canonical positive decimal string)
    // eat: action, inputSequence(same monotone stream as equip, per lease)
    // retry: action, operationId(host D-Guid after known-not-written failure only)
    // cancel: action (drops only a never-written/known-not-written pending intent).
    // Reply: normal game-reply. Independent game-shop-state carries documentId,
    // revision, state {schemaVersion:int, available:bool, reason:string|null,
    // walletMinor:string|null,baitCount:int,foodCount:int,netOwned:bool,netEquipped:bool,
    // catalogVersion:string|null,netPriceMinor:string|null,baitPackPriceMinor:string|null,
    // foodPriceMinor:string|null,baitPackUnits:int,foodStaminaRestore:int,maxPacks:int,
    // quote:null|{id,product,packs,units,totalPriceMinor:string},
    // retry:null|{operationId,action}}. No private identities or history are sent.
    // Only authorized current binding sees quote/retry. Losing focus/grant clears
    // them; confirmed-unknown save disables the Owner and cannot become a new retry.
    // One host-owned quote or known-not-written intent per binding. No price,
    // asset identity or durable request identity is accepted from the page.
    sealed class GameShopPending
    {
        internal string Id,Action;
        internal FloatViewTicket View;
        internal FloatDocumentTicket Document;
        internal FloatControlLease Lease;
        internal string WireLease;
        internal FloatPurchaseQuote Quote;
        internal FloatCommand Command;
        internal bool Retry;
    }
    bool gameShopPrewriteFaultArmed,gameShopCommitting;
    // Private deterministic probe seam; only the self-test Owner installs the
    // existing store hook. No page message, normal new/open mode or public API
    // can arm this. Other game transactions do not consume the one-shot fault.
    void ArmGameShopPrewriteFailure()
    {
        if(!GameSelfTest)throw new InvalidOperationException("Shop fault injection is self-test only.");
        if(gameShopPrewriteFaultArmed)throw new InvalidOperationException("A shop fault is already armed.");
        gameShopPrewriteFaultArmed=true;
    }
    void GameShopSaveFault(FloatSaveFaultPoint point)
    {
        GameEquipmentSaveFault(point);
        if(GameSelfTest&&GameFailureCase!=null&&gameFailureCommitInProgress&&gameFailureBoundaryFault==point){
            gameFailureBoundaryFault=null;throw new IOException("Controlled private failure boundary: "+GameFailureCase);
        }
        if(GameSelfTest&&gameFailureCommitInProgress&&gameFailurePrewriteArmed&&point==FloatSaveFaultPoint.BeforeWrite){
            gameFailurePrewriteArmed=false;throw new IOException("Controlled unhook write-before-start failure.");
        }
        if(GameSelfTest&&gameShopCommitting&&gameShopPrewriteFaultArmed&&point==FloatSaveFaultPoint.BeforeWrite){
            gameShopPrewriteFaultArmed=false;throw new IOException("Scripted self-test shop failure before any frame write.");
        }
    }
    void ClearGameShop(ProbeWindow pane=null)
    {
        if(pane==null){foreach(var b in gameViews.Values)b.Shop=null;return;}
        GameBinding binding;if(gameViews.TryGetValue(pane,out binding))binding.Shop=null;
    }
    static string SupplyName(FloatSupplyProduct product)
    {return product==FloatSupplyProduct.Net?"net":product==FloatSupplyProduct.Bait?"bait":"food";}
    static bool SupplyProduct(string name,out FloatSupplyProduct product)
    {
        product=FloatSupplyProduct.Net;if(name=="net")return true;
        if(name=="bait"){product=FloatSupplyProduct.Bait;return true;}
        if(name=="food"){product=FloatSupplyProduct.Food;return true;}return false;
    }
    static string ShopNumber(long value){return value.ToString(CultureInfo.InvariantCulture);}
    bool ShopBindingCurrent(GameBinding binding,GameShopPending pending)
    {
        return pending!=null&&Object.ReferenceEquals(binding.View,pending.View)&&Object.ReferenceEquals(binding.Document,pending.Document)&&
            Object.ReferenceEquals(binding.Lease,pending.Lease)&&binding.WireLease==pending.WireLease;
    }
    void ReconcileGameShop(GameBinding binding,FloatGameState state)
    {
        if(binding.Shop==null)return;
        if(state==null||!ShopBindingCurrent(binding,binding.Shop)||state.Sequence!=binding.Shop.Command.ExpectedSequence||
            (state.SchemaVersion!=2&&state.SchemaVersion!=3)||state.CommerceCatalog==null||
            (binding.Shop.Quote!=null&&binding.Shop.Quote.CatalogVersion!=state.CommerceCatalog.Version))binding.Shop=null;
    }
    GameShopPending PrepareGameShop(GameBinding binding,FloatCommand command,string action,FloatPurchaseQuote quote=null)
    {
        return new GameShopPending{Id=Guid.NewGuid().ToString("D"),Action=action,View=binding.View,Document=binding.Document,
            Lease=binding.Lease,WireLease=binding.WireLease,Quote=quote,Command=command};
    }
    void SendGameShop(ProbeWindow pane)
    {
        GameBinding binding;if(game==null||!pane.Ready||!gameViews.TryGetValue(pane,out binding)||binding.Document==null)return;
        var snapshot=game.ReadView(binding.View,binding.Document);var state=snapshot.State;var catalog=state==null?null:state.CommerceCatalog;
        GameBinding authorizedBinding;
        bool authorized=GameAuthorized(pane,pane.Ticket.DocumentId,binding.WireLease,out authorizedBinding)&&Object.ReferenceEquals(binding,authorizedBinding)&&game.CanControl(binding.View,binding.Document,binding.Lease);
        // A lost grant is a hard cancellation, including known-not-written
        // intents. Only current authorization may see an opaque pending ID.
        if(!authorized)binding.Shop=null;else ReconcileGameShop(binding,state);
        var pending=authorized?binding.Shop:null;
        string reason=snapshot.Faulted?"save-outcome-unknown":snapshot.ShutdownConfirmed?"owner-stopped":state==null||(state.SchemaVersion!=2&&state.SchemaVersion!=3)?"commerce-upgrade-required":!authorized?"control-required":state.Session!=null?"session-busy":null;
        object quote=null,retry=null;
        if(pending!=null){
            if(pending.Retry)retry=new{operationId=pending.Id,action=pending.Action};
            else if(pending.Quote!=null)quote=new{id=pending.Id,product=SupplyName(pending.Quote.Product),packs=pending.Quote.PackCount,units=pending.Quote.Units,totalPriceMinor=ShopNumber(pending.Quote.TotalPriceMinor)};
        }
        Send(pane,new{kind="game-shop-state",documentId=pane.Ticket.DocumentId,revision=checked(++binding.ShopRevision),state=new{
            schemaVersion=state==null?0:state.SchemaVersion,available=reason==null,reason=reason,
            walletMinor=state==null?null:ShopNumber(state.WalletMinor),baitCount=state==null?0:state.BaitCount,foodCount=state==null?0:state.FoodCount,
            netOwned=state!=null&&state.OwnedNet!=null,netEquipped=state!=null&&state.OwnedNet!=null&&state.EquippedNetId==state.OwnedNet.Id,
            catalogVersion=catalog==null?null:ShopNumber(catalog.Version),netPriceMinor=catalog==null?null:ShopNumber(catalog.NetPriceMinor),
            baitPackPriceMinor=catalog==null?null:ShopNumber(catalog.BaitPackPriceMinor),foodPriceMinor=catalog==null?null:ShopNumber(catalog.FoodPriceMinor),
            baitPackUnits=catalog==null?0:catalog.BaitPackUnits,foodStaminaRestore=catalog==null?0:catalog.FoodStaminaRestore,maxPacks=catalog==null?0:catalog.MaximumPurchasePacks,
            quote=quote,retry=retry}});
    }
    void ShopReply(ProbeWindow pane,string transport,string code)
    {GameReply(pane,transport,code);SendGameShop(pane);}
    void ReceiveGameShop(ProbeWindow pane,GameBinding binding,Dictionary<string,object> message,string document,string lease,string transport)
    {
        string action,productName=null,id=null;object raw;bool equipped=false;int packs=0;long sequence=0;FloatSupplyProduct product=FloatSupplyProduct.Net;Guid opaque;
        if(!StringField(message,"action",out action)){ShopReply(pane,transport,"invalid-intent");return;}
        bool valid=false;
        if(action=="quote")valid=message.Count==7&&StringField(message,"product",out productName)&&SupplyProduct(productName,out product)&&message.TryGetValue("packs",out raw)&&raw is int&&(packs=(int)raw)>0&&packs<=100;
        else if(action=="buy"||action=="retry")valid=message.Count==6&&StringField(message,action=="buy"?"quoteId":"operationId",out id)&&GameGuid(id,false,out opaque);
        else if(action=="equip"){
            valid=message.Count==7&&message.TryGetValue("equipped",out raw)&&raw is bool&&GameInputSequence(message,out sequence);
            if(valid)equipped=(bool)message["equipped"];
        }
        else if(action=="eat")valid=message.Count==6&&GameInputSequence(message,out sequence);
        else if(action=="cancel")valid=message.Count==5;
        if(!valid){ShopReply(pane,transport,"invalid-intent");return;}
        // Settle elapsed simulation first. Then recheck BOTH host authority
        // systems before looking up a pending quote or its durable receipt.
        AdvanceActivity();GameBinding current;
        if(!GameAuthorized(pane,document,lease,out current)||!Object.ReferenceEquals(current,binding)||!game.CanControl(binding.View,binding.Document,binding.Lease)){
            binding.Shop=null;ShopReply(pane,transport,"control-required");return;
        }
        var state=game.Snapshot.State;ReconcileGameShop(binding,state);
        if(state==null||(state.SchemaVersion!=2&&state.SchemaVersion!=3)||state.CommerceCatalog==null){ShopReply(pane,transport,"commerce-upgrade-required");return;}
        if(action=="cancel"){binding.Shop=null;ShopReply(pane,transport,"shop-cancelled");return;}
        if(state.Session!=null){ShopReply(pane,transport,"session-busy");return;}
        if(action=="buy"||action=="retry"){
            var pending=binding.Shop;
            if(pending==null||pending.Id!=id||!ShopBindingCurrent(binding,pending)||(action=="buy"?(pending.Quote==null||pending.Retry):!pending.Retry)){
                ShopReply(pane,transport,"shop-intent-not-current");return;
            }
            CommitGameShop(pane,binding,pending,transport);return;
        }
        if(binding.Shop!=null&&binding.Shop.Retry){ShopReply(pane,transport,"shop-retry-required");return;}
        if(action=="quote"){
            try{
                var quote=FloatPurchaseQuote.Create(state,product,packs);
                var command=new BuyFloatSupply(state.ProfileId,state.BranchId,Guid.NewGuid(),Guid.NewGuid(),quote,product==FloatSupplyProduct.Net?Guid.NewGuid():Guid.Empty,state.Sequence,state.Rig.Revision,state.RulesVersion);
                // Validate affordability/ownership before offering a quote;
                // candidate state is discarded and never published here.
                var possible=FloatGameReducer.Reduce(state,command,state.Rules);
                if(!possible.HasCandidate){binding.Shop=null;ShopReply(pane,transport,possible.Code);return;}
                binding.Shop=PrepareGameShop(binding,command,"buy",quote);ShopReply(pane,transport,"shop-quoted");
            }catch(ArgumentException){ShopReply(pane,transport,"invalid-intent");}
            catch(OverflowException){ShopReply(pane,transport,"arithmetic-overflow");}
            return;
        }
        if(sequence<=binding.ShopHighWater){ShopReply(pane,transport,"stale-input");return;}binding.ShopHighWater=sequence;
        FloatCommand intent;
        if(action=="equip"){
            if(equipped&&state.OwnedNet==null){ShopReply(pane,transport,"net-not-owned");return;}
            intent=new EquipFloatNet(state.ProfileId,state.BranchId,Guid.NewGuid(),Guid.NewGuid(),equipped?state.OwnedNet.Id:Guid.Empty,state.Sequence,state.Rig.Revision,state.RulesVersion);
        }else intent=new UseFloatFood(state.ProfileId,state.BranchId,Guid.NewGuid(),Guid.NewGuid(),state.FoodStackId,state.Sequence,state.Rig.Revision,state.RulesVersion);
        binding.Shop=PrepareGameShop(binding,intent,action);CommitGameShop(pane,binding,binding.Shop,transport);
    }
    void CommitGameShop(ProbeWindow pane,GameBinding binding,GameShopPending pending,string transport)
    {
        gameShopCommitting=true;
        try{
            var result=game.ExecuteUser(binding.View,binding.Document,binding.Lease,pending.Command);
            // Success, replay or determinate domain refusal ends this handle.
            // A subsequent same-ID request cannot manufacture another charge.
            binding.Shop=null;GameReply(pane,transport,result.Code,result);Broadcast();
        }catch(FloatSaveUncertainException){
            ClearGameShop();ExitCode=4;GameReply(pane,transport,"save-outcome-unknown");Broadcast();
        }catch(IOException){
            pending.Retry=true;binding.Shop=pending;GameReply(pane,transport,"shop-not-written");Broadcast();
        }finally{gameShopCommitting=false;}
    }
}
