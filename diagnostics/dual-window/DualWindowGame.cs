using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FishingDesktop.GameCore;

// Opt-in P1 data integration. The old diagnostic authority supplies window
// qualification only; its counter transaction is never used by the game page.
sealed partial class DualWindowContext
{
    readonly string gameMode;
    internal bool GameMode { get { return gameMode != null; } }
    bool GameSelfTest { get { return gameMode == "self-test" || GameFailureCase!=null; } }
    string GameFailureCase { get { return gameMode!=null && gameMode.StartsWith("failure-",StringComparison.Ordinal)?gameMode.Substring(8):null; } }
    FloatGameOwner game;
    bool gameShutdownAttempted;
    string gameShutdownCode;
    sealed class GameBinding
    {
        internal FloatViewTicket View;
        internal FloatDocumentTicket Document;
        internal FloatControlLease Lease;
        internal string WireLease;
        internal int Revision, Offset, CastRevision, FlightRevision, BiteRevision, FightRevision, ShopRevision, EquipmentRevision;
        internal long CastHighWater, StrikeHighWater, FightHighWater, ShopHighWater, EquipmentHighWater;
        internal GameShopPending Shop;
        internal GameEquipmentPending Equipment;
    }
    readonly Dictionary<ProbeWindow,GameBinding> gameViews = new Dictionary<ProbeWindow,GameBinding>();
    bool GameCanSimulate { get { return game == null || (game.Snapshot.CanSimulate && !GameFightTimeBlocked); } }

    void InitializeGame()
    {
        if (!GameMode) return;
        string path = Path.Combine(root, "game.fltj");
        if (gameMode == "open") {
            var inspection=FloatJournalArchive.Inspect(path);
            if(inspection.State.SchemaVersion!=2&&inspection.State.SchemaVersion!=3)throw new InvalidOperationException("This legacy profile requires an explicit backed-up candidate upgrade; opening did not change it.");
            game=FloatGameOwner.OpenExisting(path,null,inspection.State.SchemaVersion);
        }
        else if(GameSelfTest) game=FloatGameOwner.CreateNew(path,CreateGameFixture(),GameShopSaveFault);
        else if(gameMode=="new") game=FloatGameOwner.CreateNew(path,CreateGameFixture());
        else throw new ArgumentException("Unknown game mode.");
        InitializeGameCast();
        InitializeGameFlight();
        // This opt-in build uses only its private fixture save. Opening never
        // falls back to creating new funds, bait or equipment.
        desktopAllowForegroundChanges = true;
        if (!GameSelfTest) owner.SetPause(FishingDesktop.Diagnostics.HostPolicy.PauseReason.EnvironmentUnknown,true);
    }
    static FloatGameState CreateGameFixture()
    {
        var rules = new FloatGameRules(1,"float-starter","worm",2,100,2,1000000,
            new[]{new FloatSpeciesRule("carp",100000,10000000,120),new FloatSpeciesRule("crucian",50000,3000000,80)});
        var rig = new FixedFloatRig(Guid.NewGuid(),1,"float-starter",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),50000,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
        var catalog=new FloatCommerceCatalog(1,"worm","landing-net","bread",2400,5,20,20,20,1000,1000,100);
        // Explicit original development definitions for a NEW private profile.
        // This is not a migration policy or inferred old equipment durability.
        var equipmentCatalog=new FloatEquipmentCatalog(1,"worm",new[]{
            new FloatEquipmentDefinition("starter-rod",FloatEquipmentKind.Rod,150000,0,1000,25000),
            new FloatEquipmentDefinition("starter-reel",FloatEquipmentKind.Reel,200000),
            new FloatEquipmentDefinition("nylon-018",FloatEquipmentKind.Line,0,180),
            new FloatEquipmentDefinition("single-hook",FloatEquipmentKind.Hook,200),
            new FloatEquipmentDefinition("float-3g",FloatEquipmentKind.Bobber,3000,0,2000,3000),
            new FloatEquipmentDefinition("sinker-2_5g",FloatEquipmentKind.Sinker,2500)},
            new[]{new FloatEquipmentPairRule("starter-rod","starter-reel",true)},
            new[]{new FloatEquipmentSpoolRule("starter-reel","nylon-018",180,50000)},
            new[]{new FloatEquipmentPairRule("single-hook","worm",true)},
            new[]{new FloatEquipmentLossRule("mainline-terminal",new[]{FloatEquipmentSlot.Hook,FloatEquipmentSlot.Bobber,FloatEquipmentSlot.Sinker})});
        var equipment=new FloatEquipmentLedger(rig.Id,1,1,equipmentCatalog,new[]{
            new FloatEquipmentInstance(rig.RodId,"starter-rod",FloatEquipmentKind.Rod,1,FloatEquipmentLocation.RigSlot(rig.Id,FloatEquipmentSlot.Rod)),
            new FloatEquipmentInstance(rig.ReelId,"starter-reel",FloatEquipmentKind.Reel,1,FloatEquipmentLocation.RigSlot(rig.Id,FloatEquipmentSlot.Reel)),
            new FloatEquipmentInstance(rig.LineId,"nylon-018",FloatEquipmentKind.Line,1,FloatEquipmentLocation.ReelSpool(rig.ReelId),50000,50000),
            new FloatEquipmentInstance(rig.HookId,"single-hook",FloatEquipmentKind.Hook,1,FloatEquipmentLocation.RigSlot(rig.Id,FloatEquipmentSlot.Hook)),
            new FloatEquipmentInstance(rig.BobberId,"float-3g",FloatEquipmentKind.Bobber,1,FloatEquipmentLocation.RigSlot(rig.Id,FloatEquipmentSlot.Bobber)),
            new FloatEquipmentInstance(rig.SinkerId,"sinker-2_5g",FloatEquipmentKind.Sinker,1,FloatEquipmentLocation.RigSlot(rig.Id,FloatEquipmentSlot.Sinker))});
        return FloatGameState.CreateNewWithEquipment(Guid.NewGuid(),Guid.NewGuid(),12000,100,Guid.NewGuid(),"worm",12,equipment,rules,catalog,Guid.NewGuid());
    }
    static long GameInstalledLineMm(FloatGameState state)
    {
        if(state==null||state.Rig==null||!state.Rig.LineLengthMm.HasValue||state.Rig.LineLengthMm.Value<=0)
            throw new InvalidOperationException("An active simulation requires an actual installed line segment.");
        return state.Rig.LineLengthMm.Value;
    }
    void GameOpen(ProbeWindow pane)
    {
        if (game == null) return;
        gameViews.Add(pane,new GameBinding { View=game.OpenView(pane.Ticket.CanControl?FloatViewRole.Interaction:FloatViewRole.Observer) });
    }
    void GameNavigating(ProbeWindow pane)
    {
        GameBinding b;
        if (game == null || !gameViews.TryGetValue(pane,out b)) return;
        CancelGameCast(pane);
        ClearGameFightInput(pane);
        ClearGameShop(pane); ClearGameEquipment(pane);
        game.RevokeControl(b.Lease); b.Lease=null; b.WireLease=null; b.Document=null;
    }
    void GameAttached(ProbeWindow pane)
    {
        GameBinding b;
        if (game == null || !gameViews.TryGetValue(pane,out b)) return;
        ClearGameShop(pane); ClearGameEquipment(pane);
        b.Document=game.AttachDocument(b.View); b.Offset=0; b.Revision=0; b.CastRevision=0; b.FlightRevision=0; b.BiteRevision=0; b.FightRevision=0; b.ShopRevision=0;
        if (pane.DesktopReadOnly && !desktopShown.Contains(pane)) {
            desktopShown.Add(pane); GuardPresentation(()=>pane.Show()); CheckDesktop(pane);
        }
    }
    void GameRetire(ProbeWindow pane)
    {
        GameBinding b;
        if (game == null || !gameViews.TryGetValue(pane,out b)) return;
        CancelGameCast(pane);
        ClearGameFightInput(pane);
        ClearGameShop(pane); ClearGameEquipment(pane);
        game.CloseView(b.View); gameViews.Remove(pane);
    }
    void GameLease(ProbeWindow pane,string wireLease)
    {
        GameBinding b;
        if (game == null || !gameViews.TryGetValue(pane,out b)) return;
        CancelGameCast(pane);
        ClearGameFightInput(pane);
        ClearGameShop(pane); ClearGameEquipment(pane);
        game.RevokeControl(b.Lease); b.Lease=null; b.WireLease=null; b.CastHighWater=0; b.StrikeHighWater=0; b.FightHighWater=0; b.ShopHighWater=0; b.EquipmentHighWater=0;
        var state=game.Snapshot;
        if (wireLease == null || b.Document == null || state.Faulted || state.HostPaused || state.ShutdownConfirmed) return;
        b.Lease=game.GrantControl(b.View,b.Document); b.WireLease=wireLease;
    }
    void SyncGamePolicy()
    {
        if (game == null) return;
        var snapshot=game.Snapshot;
        if (!snapshot.Faulted && !snapshot.ShutdownConfirmed) game.SetHostPaused(!owner.ActivityState().CanRun);
        if(game.Snapshot.HostPaused||game.Snapshot.Faulted||game.Snapshot.ShutdownConfirmed){ClearGameShop(); ClearGameEquipment();}
        if (!game.Snapshot.CanSimulate) { CancelGameCast(); ClearGameFightInput(); }
    }
    async Task StartGamePresentation()
    {
        if (!GameSelfTest) StartNativePolicy();
        SyncGamePolicy();
        var read=windows[false];
        if (read.DesktopReadOnly && !desktopShown.Contains(read)) { desktopShown.Add(read); GuardPresentation(()=>read.Show()); CheckDesktop(read); }
        rendererEnabled=true; RendererPageReady(read);
        // Original fixed-water development gameplay runs on the host. A net
        // must be purchased and equipped; its visual remains a placeholder.
        await Task.FromResult(0);
        Broadcast();
    }
    void SendGameSnapshot(ProbeWindow pane)
    {
        GameBinding b;
        if (game == null || !pane.Ready || !gameViews.TryGetValue(pane,out b) || b.Document == null) return;
        ReconcileGameFlight();
        ReconcileGameBite();
        ReconcileGameFight();
        var snapshot=game.ReadView(b.View,b.Document);
        int count=snapshot.State==null?0:snapshot.State.Creel.Count;
        if (b.Offset>count) b.Offset=0;
        var projected=FloatPageProjection.Create(snapshot,b.Offset,50);
        Send(pane,new {kind="game-snapshot",documentId=pane.Ticket.DocumentId,revision=checked(++b.Revision),snapshot=projected});
        SendGameCast(pane);
        SendGameFlight(pane);
        SendGameBite(pane);
        SendGameFight(pane);
        SendGameShop(pane); SendGameEquipment(pane);
    }
    bool GameAuthorized(ProbeWindow pane,string document,string lease,out GameBinding b)
    {
        b=null;
        return game != null && gameViews.TryGetValue(pane,out b) && b.Document != null && b.Lease != null &&
            b.WireLease==lease && Foreground(pane) && pane.View.ClientSize.Width>pane.View.ClientSize.Height &&
            owner.IsAuthorized(pane.Ticket,document,lease);
    }
    void GameReply(ProbeWindow pane,string transport,string code,FloatCommitResult result=null)
    {
        // Never serialize FloatCommitResult.State or its full receipt/history.
        Send(pane,new {kind="game-reply",documentId=pane.Ticket.DocumentId,transportId=transport,code=code,
            committed=result!=null && result.Committed,written=result!=null && result.Written,
            replay=result!=null && result.IsReplay,operationSucceeded=result!=null && result.OperationSucceeded});
    }
    void ReceiveGame(ProbeWindow pane,Dictionary<string,object> m,string document,string lease,string transport)
    {
        string kind=(string)m["kind"]; GameBinding b;
        if (kind=="game-page") {
            object offset;
            if (document!=pane.Ticket.DocumentId || !gameViews.TryGetValue(pane,out b) || b.Document==null ||
                m.Count!=5 || !m.TryGetValue("offset",out offset) || !(offset is int) || (int)offset<0) return;
            var state=game.Snapshot;
            if (state.State==null || (int)offset>state.State.Creel.Count) return;
            b.Offset=(int)offset; SendGameSnapshot(pane); return;
        }
        if (!GameAuthorized(pane,document,lease,out b)) { GameReply(pane,transport,"control-required"); return; }
        if (kind=="game-cast") { ReceiveGameCast(pane,b,m,transport); return; }
        if (kind=="game-strike") { ReceiveGameStrike(pane,b,m,transport); return; }
        if (kind=="game-fight") { ReceiveGameFight(pane,b,m,transport); return; }
        if (kind=="game-shop") { ReceiveGameShop(pane,b,m,document,lease,transport); return; }
        if (kind=="game-equipment") { ReceiveGameEquipment(pane,b,m,document,lease,transport); return; }
        if (kind=="game-menu") {
            object open;
            if (m.Count!=5 || !m.TryGetValue("open",out open) || !(open is bool)) { GameReply(pane,transport,"invalid-intent"); return; }
            AdvanceActivity();
            if ((bool)open) { CancelGameCast(); ClearGameFightInput(); }
            game.SetMenuOpen((bool)open);
            AdvanceActivity();
            GameReply(pane,transport,"menu-updated"); Broadcast(); return;
        }
        object raw; FloatCommand command;
        if (kind!="game-intent" || m.Count!=5 || !m.TryGetValue("intent",out raw) ||
            !FloatPageCommands.TryParse(raw as Dictionary<string,object>,game.Snapshot.State,out command)) {
            GameReply(pane,transport,"invalid-intent"); return;
        }
        if (command is CastFloat) { GameReply(pane,transport,"cast-preparation-required"); return; }
        AdvanceActivity();
        if(GameFailureUnconfirmed){GameReply(pane,transport,"failure-result-required");Broadcast();return;}
        var reply=game.ExecuteUser(b.View,b.Document,b.Lease,command);
        GameReply(pane,transport,reply.Code,reply); Broadcast();
    }
    void PrepareGameShutdown()
    {
        if (game == null || gameShutdownAttempted) return;
        CancelGameCast();
        ClearGameFightInput();
        ClearGameShop(); ClearGameEquipment();
        gameShutdownAttempted=true;
        try {
            var state=game.Snapshot;
            if(GameFailureUnconfirmed){ExitCode=4;gameShutdownCode="unconfirmed-failure-no-settlement";return;}
            if (ExitCode!=0 || state.Faulted) { gameShutdownCode="fault-exit-no-settlement"; return; }
            var result=game.Shutdown(); gameShutdownCode=result.Code;
            ReconcileGameFlight();
            ReconcileGameBite();
            ReconcileGameFight();
            if (!result.Confirmed) ExitCode=4;
        } catch(Exception error) {
            ExitCode=4; gameShutdownCode="settlement-unconfirmed";
            try { File.WriteAllText(Path.Combine(root,"game-exit-error.txt"),error.ToString(),Encoding.UTF8); } catch(Exception) { }
        }
    }
    void DisposeGame()
    {
        if (game == null) return;
        try {
            File.WriteAllText(Path.Combine(root,"game-shutdown.json"),json.Serialize(new {code=gameShutdownCode,normalExit=ExitCode==0}),Encoding.UTF8);
        } finally { game.Dispose(); game=null; gameViews.Clear(); }
    }
}
