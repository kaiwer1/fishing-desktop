using System;
using FishingDesktop.GameCore;

sealed partial class DualWindowContext
{
    readonly ChinaWaterConditions gameClimate=new ChinaWaterConditions(0x1557u);
    long gameEcologyTimeMs,gameFishingPoolTimeMs;
    double gameEcologyRemainder;
    bool GameEcologyEnabled {get{return gameFishingWater!=null&&(PlayerMode||GameEcologySelfTest||GameCatchSelfTest||
        GameMultiRodSelfTest||GameBottomSelfTest||GameLureSelfTest||GameWallpaperSelfTest||GamePlayerSelfTest);}}

    ChinaWaterConditionSnapshot SampleGameConditions(){return gameClimate.Sample(gameFishingWater,gameEcologyTimeMs);}
    FloatFishingPool BuildGameFishingPool(FloatWaterSample sample)
    {
        if(gameFishingWater==null||game==null||game.Snapshot.State==null)return null;
        gameFishingPoolTimeMs=gameEcologyTimeMs;
        var state=game.Snapshot.State;
        var pool=GameEcologyEnabled?gameClimate.CreatePool(gameFishingWater,sample,state.Rules,gameFishingPoolTimeMs):
            gameFishingWater.CreatePool(sample,state.Rules);
        var session=state.Session;
        if(pool!=null&&gameFishingWater.Kind=="freshwater"&&session!=null&&!session.IsBottom&&!session.IsLure&&state.BreadBaitSelected)
            return new FloatFishingPool(pool.Id,pool.Name,pool.Environment.WithBreadPaste());
        return pool;
    }
    void AdvanceGameEcology(double activeSeconds)
    {
        // The owner supplies the same bounded, active delta to every rod. No
        // tab, display, map or Windows wall clock can independently add time.
        if(!GameEcologyEnabled||activeSeconds<=0||!GameCanSimulate)return;
        double amount=activeSeconds*1000+gameEcologyRemainder;
        long ms=(long)Math.Floor(amount);gameEcologyRemainder=amount-ms;
        long before=gameEcologyTimeMs;gameEcologyTimeMs=checked(before+ms);
        if(before/1000==gameEcologyTimeMs/1000)return;
        if(GameBankMode){
            foreach(var pair in bankLive){
                var rod=pair.Value;
                if(rod.Bottom==null||rod.Landing==null||!rod.Landing.Accepted||
                    rod.Bottom.Snapshot.Phase>BottomPhase.Searching)continue;
                var pool=BuildGameFishingPool(rod.Landing.Sample);
                if(pool!=null&&gameBankScheduler.SetEnvironment(pair.Key,rod.Session,pool.Environment)==null){
                    rod.Pool=pool;if(gameBankStand.Held==pair.Key)gameFishingPool=pool;
                }
            }
            return;
        }
        if(gameWaterLanding==null||!gameWaterLanding.Accepted||game.Snapshot.State.Session==null)return;
        var session=game.Snapshot.State.Session;
        if(session.IsBottom&&gameBottom!=null&&gameBottom.Snapshot.Phase<=BottomPhase.Searching){
            var pool=BuildGameFishingPool(gameWaterLanding.Sample);
            if(pool!=null&&gameBottom.SetEnvironment(session.Id,pool.Environment).Accepted)gameFishingPool=pool;
        }else if(!session.IsBottom&&!session.IsLure&&gameBite!=null&&gameBite.Snapshot.Phase<=FloatBitePhase.Searching){
            var pool=BuildGameFishingPool(gameWaterLanding.Sample);
            if(pool!=null&&gameBite.SetEnvironment(session.Id,pool.Environment).Accepted)gameFishingPool=pool;
        }
    }
    void SendGameConditions(ProbeWindow pane)
    {
        GameBinding b;
        if(!GameEcologyEnabled||!pane.Ready||!gameViews.TryGetValue(pane,out b)||b.Document==null)return;
        long minute=gameEcologyTimeMs/1000;
        if(b.ConditionsDocument==b.Document&&b.ConditionsWater==gameFishingWater.Id&&b.ConditionsMinute==minute)return;
        var s=SampleGameConditions();
        Send(pane,new {kind="game-conditions-state",documentId=pane.Ticket.DocumentId,revision=checked(++b.ConditionsRevision),
            state=new {waterId=s.WaterId,clockMinute=s.MinuteOfDay,weatherCode=s.WeatherKey,cloudPercent=(s.CloudPermille+5)/10,
                windDeciMs=s.WindTenthsMps,tempDeciC=s.TemperatureTenthsC,lightPermille=s.LightPermille,rainPermille=s.RainPermille}});
        b.ConditionsDocument=b.Document;b.ConditionsWater=s.WaterId;b.ConditionsMinute=minute;
    }
}
