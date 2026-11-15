using System;
using System.Collections.Generic;
using System.Globalization;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    FloatGameState gameAnglerLogSource;
    object gameAnglerLogData;

    static object GameAnglerLogData(FloatGameState state)
    {
        var log=FloatAnglerLog.Build(state);
        var methods=new List<object>();var species=new List<object>();
        foreach(var row in log.Methods)methods.Add(new {id=row.Id,casts=row.Casts,caught=row.Caught,
            xp=row.Xp.ToString(CultureInfo.InvariantCulture)});
        foreach(var row in log.Species)species.Add(new {id=row.Id,caught=row.Caught,
            maxMassMg=row.MaxMassMg.ToString(CultureInfo.InvariantCulture)});
        return new {rulesVersion=FloatAnglerLog.RulesVersion,sequence=log.Sequence,totalCasts=log.TotalCasts,totalCaught=log.TotalCaught,
            totalSold=log.TotalSold,saleMinor=log.SaleMinor.ToString(CultureInfo.InvariantCulture),
            totalXp=log.TotalXp.ToString(CultureInfo.InvariantCulture),methods=methods.ToArray(),species=species.ToArray()};
    }
    void SendGameAnglerLog(ProbeWindow pane)
    {
        GameBinding b;
        if(game==null||!pane.Ready||!gameViews.TryGetValue(pane,out b)||b.Document==null)return;
        var view=game.Snapshot;
        if(!view.MenuOpen||view.State==null)return;
        if(!Object.ReferenceEquals(view.State,gameAnglerLogSource)){
            gameAnglerLogData=GameAnglerLogData(view.State);gameAnglerLogSource=view.State;
        }
        Send(pane,new {kind="game-angler-log",documentId=pane.Ticket.DocumentId,
            revision=checked(++b.AnglerLogRevision),state=gameAnglerLogData});
    }
}
