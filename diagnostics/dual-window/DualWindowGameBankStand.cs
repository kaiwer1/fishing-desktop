using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    BankRodStand gameBankStand;
    BankRodScheduler gameBankScheduler;
    FloatControlLease gameBankControl;
    bool GameBankMode {get{return gameBankStand!=null;}}
    FloatSession GameHeldBankSession {get{return !GameBankMode||game.Snapshot.State==null?null:game.Snapshot.State.BankSessionFor(gameBankStand.Held);}}
    BankRodState GameBankFacts(Guid rig)
    {
        var state=game.Snapshot.State;var kit=state==null?null:state.BottomKitFor(rig);
        if(kit==null)return null;
        var live=BankLiveFacts(rig);if(live!=null)return live;
        var s=state.BankSessionFor(rig);
        return new BankRodState(rig,kit.Equipment.RigRevision,s==null?Guid.Empty:s.Id,s==null?BankRodPhase.Idle:BankRodPhase.PendingResult,true);
    }
    static string BankGuid(Guid id){return id==Guid.Empty?null:id.ToString("D");}
    static string BankNumber(long v){return v.ToString(CultureInfo.InvariantCulture);}
    static bool BankCounter(Dictionary<string,object> m,string key,out long v)
    {
        string n;v=-1;return StringField(m,key,out n)&&Int64.TryParse(n,NumberStyles.None,CultureInfo.InvariantCulture,out v)&&v>=0&&BankNumber(v)==n;
    }
    bool ReachBankSlot(int slot)
    {
        long dx=gameWater.AnchorXmm-gameBankStand.SlotXmm(slot),dz=gameWater.AnchorZmm-gameBankStand.CenterZmm;
        return dx*dx+dz*dz<=(long)BankRodStand.ReachMm*BankRodStand.ReachMm;
    }
    BankStandPose BankPose()
    {
        int slot=0,min=int.MaxValue;
        for(int i=1;i<=3;i++){
            // The contextual key aims at a placeable support when holding a
            // rod; with empty hands it aims at the nearest occupied support.
            if((gameBankStand.Held==Guid.Empty)==(gameBankStand.At(i)==Guid.Empty))continue;
            int dx=Math.Abs(gameWater.AnchorXmm-gameBankStand.SlotXmm(i));
            if(ReachBankSlot(i)&&dx<min){min=dx;slot=i;}
        }
        return new BankStandPose(gameWater.WaterId,gameWater.SpotId,gameWater.AnchorXmm,gameWater.AnchorZmm,slot);
    }
    void BankLease(FloatControlLease lease)
    {
        if(!GameBankMode||Object.ReferenceEquals(gameBankControl,lease))return;
        gameBankControl=lease;gameBankStand.SetControl(checked(gameBankStand.ControlEpoch+1),lease!=null);
    }
    void ClearBankMode()
    {
        if(!GameBankMode)return;
        BankLease(null);gameBankStand=null;gameBankScheduler=null;gameBankControl=null;
        ActivateBankHand();
    }
    // Recovery already ended every unpaid simulation. A paid fish waiting
    // for explicit release must still be reachable after restarting Windows.
    void RestoreBankResultHand()
    {
        if(game==null||!GameBankStandSpot)return;
        var s=game.Snapshot.State;
        if(s==null||!s.HasPendingBankResult||s.OwnedBankRodStand==null)return;
        if(GameHeldBankSession!=null&&GameHeldBankSession.CapacityBlocked)return;
        foreach(var pending in s.BankSessions){
            if(!pending.CapacityBlocked)continue;
            var slots=new Guid[3];int n=0;
            foreach(var k in s.OwnedBottomKits){
                if(k.Equipment.RigId!=pending.BankRigId&&s.BankSessionFor(k.Equipment.RigId)==null)slots[n++]=k.Equipment.RigId;
            }
            var lease=gameBankControl;
            if(GameBankMode)BankLease(null);
            gameBankControl=null;
            gameBankStand=new BankRodStand(s.OwnedBankRodStand.Id,gameWater.WaterId,gameWater.SpotId,gameWater.AnchorXmm,gameWater.AnchorZmm+1200,
                pending.BankRigId,slots[0],slots[1],slots[2],GameBankFacts);
            gameBankScheduler=new BankRodScheduler(gameBankStand);BankLease(lease);ActivateBankHand();return;
        }
    }
    void ReceiveBankKit(ProbeWindow p,GameBinding b,Dictionary<string,object> m,string t)
    {
        string a;long seq;if(m.Count!=6||!StringField(m,"action",out a)||!GameEquipmentExpectedSequence(m,out seq)||
            (a!="buy-2"&&a!="buy-3"&&a!="buy-stand"&&a!="select"&&a!="leave")){GameReply(p,t,"invalid-intent");return;}
        AdvanceActivity();var v=game.Snapshot;var s=v.State;
        if(!GameAtlasMode||!game.CanControl(b.View,b.Document,b.Lease)||s==null||!BottomOwnedKit.Enabled(s)){GameReply(p,t,"bank-unavailable");return;}
        if(!v.MenuOpen||s.Session!=null||s.BankSessions.Count!=0||v.PendingResult||GameCampActive||b.Shop!=null||b.Equipment!=null||b.Spares!=null){GameReply(p,t,"bank-busy");return;}
        if(s.Sequence!=seq){GameReply(p,t,"bank-stale");Broadcast();return;}
        if(a=="select"||a=="leave"){
            if(a=="select"){
                if(!GameBankStandSpot||s.OwnedBankRodStand==null||s.OwnedBottomKits.Count==0){GameReply(p,t,"bank-kit-required");return;}
                if(!GameBankMode){
                    var ids=new Guid[3];for(int i=0;i<s.OwnedBottomKits.Count;i++)ids[i]=s.OwnedBottomKits[i].Equipment.RigId;
                    gameBottom=null;gameBottomAttempt=Guid.Empty;gameBottomSelected=false;gameBite=null;gameFight=null;
                    gameBankStand=new BankRodStand(s.OwnedBankRodStand.Id,gameWater.WaterId,gameWater.SpotId,gameWater.AnchorXmm,gameWater.AnchorZmm+1200,Guid.Empty,ids[0],ids[1],ids[2],GameBankFacts);
                    gameBankScheduler=new BankRodScheduler(gameBankStand);BankLease(b.Lease);ActivateBankHand();
                }
            }else if(GameBankMode){ClearBankMode();gameBottomSelected=false;InitializeGameCast();InitializeGameFlight();}
            GameReply(p,t,"bank-mode-selected");
        }else{
            if(GameBankMode){GameReply(p,t,"bank-leave-first");return;}
            FloatCommand command;
            if(a=="buy-stand")command=new BuyBankRodStand(s.ProfileId,s.BranchId,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),seq,s.Rig.Revision,s.RulesVersion);
            else{
                var kit=new BottomOwnedKit(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
                command=new BuyAdditionalBottomKit(s.ProfileId,s.BranchId,Guid.NewGuid(),Guid.NewGuid(),a=="buy-2"?2:3,kit,seq,s.Rig.Revision,s.RulesVersion);
            }
            try{
                var result=game.ExecuteUser(b.View,b.Document,b.Lease,command);GameReply(p,t,result.OperationSucceeded?"bank-purchased":result.Code,result);
            }catch(FloatSaveUncertainException){ExitCode=4;GameReply(p,t,"save-outcome-unknown");}
            catch(IOException){GameReply(p,t,"bank-not-written");}
        }
        Broadcast();
    }
    object BankRigRow(Guid id)
    {
        if(id==Guid.Empty)return null;
        var r=GameBankFacts(id);if(r==null)return null;int n=0;
        foreach(var k in game.Snapshot.State.OwnedBottomKits){n++;if(k.Equipment.RigId==id)break;}
        string phase=r.Phase==BankRodPhase.PendingResult?"pending-result":r.Phase.ToString().ToLowerInvariant();
        var tip=BankBottomFor(id);object end=tip==null?null:new {xMm=tip.Contact.Xmm,yMm=tip.SinkerYmm,zMm=tip.Contact.Zmm};
        return new {rigId=BankGuid(id),sessionId=BankGuid(r.SessionId),label="底钓竿 "+n,phase,tipPermille=tip==null?0:(int)Math.Round(tip.TipOffsetMm*1000.0/46),lineEnd=end};
    }
    void SendBankKit(ProbeWindow p)
    {
        GameBinding b;if(!GameAtlasMode||!p.Ready||!gameViews.TryGetValue(p,out b)||b.Document==null)return;
        var s=game.Snapshot.State;
        Send(p,new {kind="game-bank-kit-state",documentId=p.Ticket.DocumentId,revision=checked(++b.BankKitRevision),state=new {
            available=s!=null&&BottomOwnedKit.Enabled(s),allowed=GameBankStandSpot,selected=GameBankMode,standOwned=s!=null&&s.OwnedBankRodStand!=null,
            kitCount=s==null?0:s.OwnedBottomKits.Count,extraPriceMinor=BottomOwnedKit.AdditionalPriceMinor,standPriceMinor=OwnedBankRodStand.PriceMinor}});
    }
    void SendBankStand(ProbeWindow p)
    {
        GameBinding b;if(!GameAtlasMode||gameWater==null||!p.Ready||!gameViews.TryGetValue(p,out b)||b.Document==null)return;
        object model;
        if(!GameBankMode)model=new {waterId=gameWater.WaterId,spotId=gameWater.SpotId,standId=(string)null};
        else{
            var rows=new List<object>();for(int i=1;i<=3;i++){
                Guid id=gameBankStand.At(i);var tip=id==Guid.Empty?null:BankBottomFor(id);
                string alarm=tip==null?"none":tip.Phase==BottomPhase.Strikeable?"bite":tip.Phase==BottomPhase.Nibbling?"touch":"none";
                rows.Add(new {slot=i,reachable=ReachBankSlot(i),alarm,rig=BankRigRow(id)});
            }
            model=new {waterId=gameWater.WaterId,spotId=gameWater.SpotId,standId=BankGuid(gameBankStand.StandId),
                standRevision=BankNumber(gameBankStand.Revision),controlEpoch=BankNumber(gameBankStand.ControlEpoch),handGeneration=BankNumber(gameBankStand.HandGeneration),
                geometryVersion=gameWater.Version,origin=new {xMm=gameBankStand.CenterXmm,yMm=gameWater.SurfaceYmm,zMm=gameBankStand.CenterZmm},
                aimedSlot=BankPose().AimedSlot,pending=gameBankStand.Pending!=null,heldRig=BankRigRow(gameBankStand.Held),slots=rows.ToArray()};
        }
        Send(p,new {kind="game-stand-state",documentId=p.Ticket.DocumentId,revision=checked(++b.BankStandRevision),state=model});
    }
    void BroadcastBankStand(){foreach(var p in windows.Values)SendBankStand(p);}
    void ReceiveBankStand(ProbeWindow p,GameBinding b,Dictionary<string,object> m,string t)
    {
        string key;Guid stand,held,other;object x,h,o,k;long seq,epoch,rev;
        if(m.Count!=12||!StringField(m,"key",out key)||!GameInputSequence(m,out seq)||!BankCounter(m,"controlEpoch",out epoch)||!BankCounter(m,"expectedRevision",out rev)||
            !m.TryGetValue("standId",out x)||!GameGuid(x,false,out stand)||!m.TryGetValue("expectedHeld",out h)||!GameGuid(h,true,out held)||
            !m.TryGetValue("expectedOccupant",out o)||!GameGuid(o,true,out other)||!m.TryGetValue("slot",out k)||!(k is int)||(int)k<1||(int)k>3){GameReply(p,t,"invalid-intent");return;}
        BankStandKey action;
        switch(key){case "place":action=BankStandKey.Place;break;case "take":action=BankStandKey.Take;break;case "one":action=BankStandKey.One;break;
            case "two":action=BankStandKey.Two;break;case "three":action=BankStandKey.Three;break;default:GameReply(p,t,"invalid-intent");return;}
        AdvanceActivity();if(!GameBankMode||!game.CanControl(b.View,b.Document,b.Lease)||!game.Snapshot.CanSimulate||stand!=gameBankStand.StandId||GameWalkMoving){GameReply(p,t,"stand-unavailable");return;}
        var pose=BankPose();int target=action<=BankStandKey.Take?pose.AimedSlot:(int)action-(int)BankStandKey.One+1;
        if(target!=(int)k){GameReply(p,t,"ownership-stale");return;}
        BankTransfer ticket;string error=gameBankStand.Begin(action,pose,epoch,seq,rev,held,other,out ticket);
        while(error==null&&ticket!=null){
            BankTransfer next;error=gameBankStand.Commit(ticket,BankPose(),out next);ticket=next;if(error==null)ActivateBankHand();
        }
        GameReply(p,t,error??"stand-updated");Broadcast();
    }
}
