using System;
using System.Collections.Generic;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    FloatFishingWater gameFishingWater;
    FloatFishingPool gameFishingPool;

    void SendGameSpots(ProbeWindow pane)
    {
        GameBinding binding;
        if(gameFishingWater==null || game.Snapshot.State==null || !pane.Ready || !gameViews.TryGetValue(pane,out binding) || binding.Document==null)return;
        var rows=new List<object>();var rules=game.Snapshot.State.Rules;
        foreach(var water in FloatFishingAtlas.Available(rules)){
            var names=new List<string>();foreach(var species in water.SpeciesFor(rules))names.Add(species.Name);
            rows.Add(new {id=water.Id,name=water.Name,region=water.Kind=="marine"?"中国近海":"中国内陆",description=water.Description,species=names.ToArray()});
        }
        Send(pane,new {kind="game-spot-state",documentId=pane.Ticket.DocumentId,revision=checked(++binding.SpotRevision),state=new {activeId=gameFishingWater.Id,spots=rows.ToArray()}});
    }
    void ReceiveGameSpot(ProbeWindow pane,GameBinding binding,Dictionary<string,object> m,string transport)
    {
        string id;object version;
        if(m.Count!=6||!StringField(m,"spotId",out id)||id.Length==0||id.Length>64||!m.TryGetValue("expectedVersion",out version)||!(version is int)||(int)version<=0){
            GameReply(pane,transport,"invalid-intent");return;
        }
        AdvanceActivity();
        if(!game.CanControl(binding.View,binding.Document,binding.Lease)){GameReply(pane,transport,"control-required");return;}
        var s=game.Snapshot;
        if(gameFishingWater==null){GameReply(pane,transport,"spot-unavailable");return;}
        if(!s.MenuOpen||s.State.Session!=null||s.PendingResult||GameCampActive||GameFailureUnconfirmed||binding.Shop!=null||binding.Spares!=null||binding.Equipment!=null){
            GameReply(pane,transport,"spot-switch-busy");return;
        }
        if((int)version!=gameWater.Version){GameReply(pane,transport,"spot-stale");return;}
        var next=FloatFishingAtlas.Find(id);
        if(next==null||next.SpeciesFor(s.State.Rules).Count==0){GameReply(pane,transport,"spot-unavailable");return;}
        if(Object.ReferenceEquals(next,gameFishingWater)){GameReply(pane,transport,"spot-unchanged");return;}
        // Travel is only a session preference: no journal edit, no elapsed
        // fishing time, and no transfer of an old float or encounter.
        CancelGameCast();ClearGameFightInput();
        gameCastInput=null;gameWaterPlan=null;gameWaterTarget=null;gameWaterLanding=null;gameWaterLandingSession=Guid.Empty;
        gameBite=null;gameFight=null;gameFishingPool=null;gameFlight=null;gameFlightTrail.Clear();
        gameWater=next.Geometry;gameFishingWater=next;ConfigureGameWaterBudget();InitializeGameCast();InitializeGameFlight();
        GameReply(pane,transport,"spot-selected");Broadcast();
    }
}
