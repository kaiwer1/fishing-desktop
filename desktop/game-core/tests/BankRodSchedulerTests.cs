using System;
using System.Collections.Generic;
using System.Linq;
using FishingDesktop.GameCore;

static class BankRodSchedulerTests
{
    static Guid G(int n){return new Guid(n,0,0,new byte[8]);}
    static Guid RigId(int n){return G(100*n);}
    static Guid Session(int n){return G(100*n+50);}
    static void Need(bool b,string why){if(!b)throw new Exception(why);}
    static int failures;
    static void Test(string name,Action test)
    {try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+" "+e.Message);}}
    static BottomFightHandoff NewOwner(int n,int z,uint seed,double rate=0,Guid? session=null)
    {
        var keys=new[]{"bottom-bank-rod","bottom-bank-reel","bottom-022-line","bottom-016-leader","bottom-single-hook","bottom-18g-sinker","worm"};
        var p=keys.Select((key,i)=>new BottomRigPart(G(100*n+1+i),(BottomPartKind)i,key)).ToArray();
        var assembly=new BottomSimpleAssembly(RigId(n),1,"bottom_simple",50000,p[0],p[1],p[2],p[3],p[4],p[5],p[6]);
        var water=FloatStillWaterGeometry.CreateDevelopment();
        var patch=new BottomSurveyPatch(water,-40000,40000,3000,80000,BottomSubstrate.FirmGravel,true);
        var config=new BottomFightConfig(new FloatFightRigRules(80,1,40,200,1,20,.2,1.6,40,3,3,5,5),
            new FloatLandingRules(true,5,1,1,.5),new Dictionary<string,FloatFightFishRules>{
            {"carp",new FloatFightFishRules(3,2,20,.05,.005,10)}});
        BottomFightHandoff owner;string error=BottomFightHandoff.AtWaterContact(session??Session(n),assembly,patch,1,0,z,seed,Pool(rate),config,out owner);
        Need(error==null,error);return owner;
    }
    static FloatBiteEnvironment Pool(double rate)
    {return new FloatBiteEnvironment(rate,new[]{new FloatBiteCandidate("carp",1,1000000,1400000)});}

    sealed class Bank
    {
        internal readonly BankRodStand Stand;
        internal readonly BankRodScheduler Clock;
        internal readonly Dictionary<int,BottomFightHandoff> Owners=new Dictionary<int,BottomFightHandoff>();
        internal readonly List<BankTipEvent> Events=new List<BankTipEvent>();
        internal long Frame,ControlInput;
        internal Bank()
        {
            Stand=new BankRodStand(G(9000),"development-still-water","bank-01",0,0,RigId(1),Guid.Empty,Guid.Empty,Guid.Empty,
                id=>Clock!=null&&Clock.ReadRig(id)!=null?Clock.ReadRig(id):new BankRodState(id,1,Guid.Empty,BankRodPhase.Idle,true));
            Clock=new BankRodScheduler(Stand);Stand.SetControl(1,true);
        }
        internal BankRodClockResult Tick(int ms=50,bool paused=false)
        {
            var result=Clock.Advance(++Frame,ms,paused);
            Need(result.Code==(paused?"clock-paused":"clock-advanced"),result.Code);
            Events.AddRange(result.Signals);return result;
        }
        internal void Begin(int n,int z=12000,uint seed=1)
        {
            if(Stand.Held==Guid.Empty){
                BankTransfer transfer,next;var pose=Pose(0);
                Need(Stand.DrawStored(RigId(n),pose,Stand.ControlEpoch,++ControlInput,Stand.Revision,out transfer)==null,"draw");
                Need(Stand.Commit(transfer,pose,out next)==null,"draw commit");
            }
            var owner=NewOwner(n,z,seed);Need(Clock.AttachHeld(owner)==null,"attach");Owners.Add(n,owner);
        }
        internal void Until(int n,BottomPhase phase)
        {for(int i=0;i<900&&Owners[n].Snapshot.Phase!=phase;i++)Tick();Need(Owners[n].Snapshot.Phase==phase,"phase "+phase);}
        static BankStandPose Pose(int slot){return new BankStandPose("development-still-water","bank-01",0,0,slot);}
        internal string Transfer(bool put,int slot)
        {
            var pose=Pose(slot);BankTransfer ticket,next;
            string error=Stand.Begin(put?BankStandKey.Place:BankStandKey.Take,pose,Stand.ControlEpoch,++ControlInput,
                Stand.Revision,Stand.Held,Stand.At(slot),out ticket);
            if(error!=null)return error;
            error=Stand.Commit(ticket,pose,out next);
            if(error==null&&next!=null)error=Stand.Commit(next,pose,out ticket);
            return error;
        }
        internal void Rack(int n,int slot,int z=12000,uint seed=1)
        {Begin(n,z,seed);Until(n,BottomPhase.Searching);Need(Transfer(true,slot)==null,"place");}
        internal void Stock()
        {Rack(1,3,9000,9);Rack(2,1,15000,19);Rack(3,2,21000,29);}
        internal void Feed(int n,double rate)
        {Need(Clock.SetEnvironment(RigId(n),Session(n),Pool(rate))==null,"update");}
        internal string Strike(int n,long seq,out FloatFightSimulation fight,long? generation=null)
        {
            var s=Owners[n].Snapshot;return Clock.StrikeHeld(s.RigId,s.SessionId,s.RigRevision,s.EncounterId,
                Stand.ControlEpoch,generation??Stand.HandGeneration,seq,out fight);
        }
    }

    static int Main()
    {
        Test("three distinct casts sink to their own bed, stable handoffs retain each live session",delegate{
            var bank=new Bank();
            bank.Begin(1,9000);Need(bank.Transfer(true,1)=="rig-busy","sinking transferred");
            bank.Until(1,BottomPhase.Searching);var identity=bank.Owners[1].Snapshot;
            Need(bank.Transfer(true,3)==null,"place");bank.Rack(2,1,15000,19);bank.Rack(3,2,21000,29);
            var summary=bank.Clock.Summary();Need(summary.Count==3&&summary.Select(x=>x.Slot).SequenceEqual(new[]{1,2,3})&&
                summary.Select(x=>x.RigId).SequenceEqual(new[]{RigId(2),RigId(3),RigId(1)}),"stable posts");
            foreach(var rod in bank.Owners.Values){var s=rod.Snapshot;Need(s.SinkerOnBed&&s.BaitOnBed&&s.OutLineMm+s.OnSpoolMm==50000,"real settled rod");}
            Need(bank.Owners.Values.Select(r=>r.Snapshot.Contact.BaitYmm).Distinct().Count()==3&&
                bank.Owners[1].Snapshot.SessionId==identity.SessionId&&bank.Owners[1].Snapshot.StepIndex>identity.StepIndex,"individual depths/clocks");
        });
        Test("frames must be consecutive, duplicate and skipped frames cannot advance any rod",delegate{
            var bank=new Bank();bank.Begin(1);bank.Tick(17);
            Need(bank.Clock.Advance(bank.Frame,200).Code=="stale-clock-frame"&&bank.Clock.Advance(bank.Frame+2,250).Code=="clock-frame-gap","frame identity");
            Need(bank.Clock.StepIndex==0&&bank.Clock.RemainderMilliseconds==17,"unearned time");
            bank.Tick(33);Need(bank.Clock.StepIndex==1&&bank.Owners[1].Snapshot.StepIndex==1&&bank.Clock.RemainderMilliseconds==0,"recovery");
        });
        Test("a rod joined during a partial step never receives time already accumulated by a different rod",delegate{
            var bank=new Bank();bank.Rack(1,1);long previous=bank.Owners[1].Snapshot.StepIndex;
            bank.Tick(30);bank.Begin(2);bank.Tick(20);
            Need(bank.Owners[1].Snapshot.StepIndex==previous+1&&bank.Owners[2].Snapshot.StepIndex==0&&
                bank.Owners[2].Snapshot.RemainderMilliseconds==20,"wrong partial active time for new rod");
            bank.Tick();Need(bank.Owners[1].Snapshot.StepIndex==previous+2&&bank.Owners[2].Snapshot.StepIndex==1&&
                bank.Owners[2].Snapshot.RemainderMilliseconds==20,"next shared interval");
        });
        Test("three simultaneous true touches and bites emit once in post order independent of registration order",delegate{
            var bank=new Bank();bank.Stock();for(int n=1;n<=3;n++)bank.Feed(n,1000);
            bank.Until(1,BottomPhase.Strikeable);
            Need(bank.Events.Count==6&&bank.Events.Take(3).Select(e=>e.Slot).SequenceEqual(new[]{1,2,3})&&
                bank.Events.Skip(3).Select(e=>e.Slot).SequenceEqual(new[]{1,2,3}),"event ordering");
            foreach(var group in bank.Events.GroupBy(e=>e.Kind)){
                Need(group.Select(e=>e.Step).Distinct().Count()==1&&group.Select(e=>e.SessionId).Distinct().Count()==3&&
                    group.All(e=>e.SignalSequence==(e.Kind==BankTipKind.Touch?1:2)),"cross-rod signals");
            }
            Need(bank.Owners.Values.Select(x=>x.Snapshot.EncounterId).Distinct().Count()==3&&bank.Stand.Held==Guid.Empty,"wrong identity or automatic pick");
            bank.Clock.Summary();bank.Clock.Summary();
            Need(bank.Clock.Advance(bank.Frame,50).Signals.Count==0&&bank.Tick(0).Signals.Count==0,"replayed old sounds");
        });
        Test("staggered windows, one expired and three missed baits cannot award any fish",delegate{
            var bank=new Bank();bank.Stock();bank.Feed(1,1000);for(int j=0;j<12;j++)bank.Tick();
            bank.Feed(2,1000);for(int j=0;j<12;j++)bank.Tick();bank.Feed(3,1000);
            bank.Until(1,BottomPhase.RetrieveRequired);
            Need(bank.Owners[2].Snapshot.Phase==BottomPhase.Strikeable&&bank.Owners[3].Snapshot.Phase==BottomPhase.Nibbling,"independent windows");
            var open=bank.Owners[2].Snapshot;FloatFightSimulation denied;
            Need(bank.Strike(2,1,out denied)=="not-current-hand"&&denied==null&&bank.Stand.Held==Guid.Empty,"racked fish hooked");
            Need(open.StepIndex==bank.Owners[2].Snapshot.StepIndex,"rejected action ticked");
            bank.Until(3,BottomPhase.RetrieveRequired);
            Need(bank.Clock.Summary().All(s=>s.NeedsRetrieve)&&bank.Owners.Values.All(x=>!x.Snapshot.BaitAttached),"missed baits");
        });
        Test("taking an active bite preserves window; only a fresh hand input can start one physical fight",delegate{
            var bank=new Bank();bank.Stock();bank.Feed(2,1000);bank.Until(2,BottomPhase.Strikeable);var before=bank.Owners[2].Snapshot;
            long stale=bank.Stand.HandGeneration;Need(bank.Transfer(false,1)==null,"take");var after=bank.Owners[2].Snapshot;
            Need(before.EncounterId==after.EncounterId&&before.OpenStep==after.OpenStep&&before.CloseStep==after.CloseStep&&
                before.StepIndex==after.StepIndex&&after.Phase==BottomPhase.Strikeable,"pickup changed bite");
            bank.Tick(30);FloatFightSimulation f;Need(bank.Strike(2,1,out f,stale)=="not-current-hand"&&f==null,"stale hand");
            Need(bank.Strike(2,1,out f)==null&&f!=null&&!f.Snapshot.Reeling&&f.Snapshot.EncounterId==before.EncounterId,"manual strike");
            FloatFightSimulation duplicate;Need(bank.Strike(2,1,out duplicate)=="stale-rod-input"&&duplicate==null,"duplicate");
            Need(bank.Transfer(true,1)=="rig-busy","placed fighting rod");
            long otherStep=bank.Owners[1].Snapshot.StepIndex;bank.Tick(20);
            Need(f.Snapshot.StepIndex==0&&f.Snapshot.RemainderMilliseconds==20,"pre-strike time leaked into fight");
            bank.Tick(100);
            Need(f.Snapshot.StepIndex==2&&!f.Snapshot.Reeling&&bank.Owners[1].Snapshot.StepIndex==otherStep+3,"single shared clock");
        });
        Test("pause freezes all three and drops held reel input without a wall-clock catch-up",delegate{
            var bank=new Bank();bank.Stock();bank.Feed(3,1000);bank.Until(3,BottomPhase.Strikeable);Need(bank.Transfer(false,2)==null,"take");
            FloatFightSimulation f;Need(bank.Strike(3,1,out f)==null,"hook");var s=f.Snapshot;
            Need(bank.Clock.ReelHeld(RigId(3),Session(3),s.EncounterId,1,bank.Stand.HandGeneration,2,true)==null&&f.Snapshot.Reeling,"reel");
            long step=bank.Clock.StepIndex,other=bank.Owners[1].Snapshot.StepIndex;
            for(int i=0;i<100;i++){var events=bank.Tick(250,true);Need(events.Signals.Count==0,"paused event");}
            Need(!f.Snapshot.Reeling&&bank.Clock.StepIndex==step&&bank.Owners[1].Snapshot.StepIndex==other,"pause");
            Need(bank.Clock.ReelHeld(RigId(3),Session(3),s.EncounterId,1,bank.Stand.HandGeneration,3,true)=="clock-paused","paused command");
            bank.Tick(0);bank.Tick(50);Need(bank.Clock.StepIndex==step+1&&!f.Snapshot.Reeling&&f.Snapshot.StepIndex==1,"catch-up/input replay");
        });
        Test("independent search seeds match separately run real encounters under different host frame partitions",delegate{
            var bank=new Bank();bank.Stock();var reference=new Dictionary<int,BottomFightHandoff>();
            foreach(var n in new[]{1,2,3}){
                var s=bank.Owners[n].Snapshot;var single=NewOwner(n,s.Contact.Zmm,(uint)(n*10-1));reference.Add(n,single);
                while(single.Snapshot.StepIndex<s.StepIndex)single.Advance(Session(n),50);
                single.SetEnvironment(Session(n),Pool(.8));bank.Feed(n,.8);
            }
            for(int j=0;j<200;j++){
                bank.Tick(17);bank.Tick(33);
                for(int n=3;n>=1;n--){var control=reference[n];
                    control.Advance(Session(n),50);var a=bank.Owners[n].Snapshot;var b=control.Snapshot;
                    Need(a.Phase==b.Phase&&a.EncounterId==b.EncounterId&&a.StepIndex==b.StepIndex&&
                        a.SignalSequence==b.SignalSequence&&a.OpenStep==b.OpenStep&&a.CloseStep==b.CloseStep,"random or timing cross-talk");}
            }
            Need(bank.Events.Where(e=>e.Kind==BankTipKind.Bite).Select(e=>e.Step).Distinct().Count()>1,"seeded variation not observed");
        });
        Test("no fourth live rod and no duplicate identity, history or pre-advanced owner can register",delegate{
            var bank=new Bank();bank.Stock();
            Need(bank.Clock.AttachHeld(bank.Owners[1])=="rod-already-attached"&&bank.Clock.AttachHeld(NewOwner(4,12000,3))=="rod-capacity","capacity");
            Need(bank.Clock.DetachConfirmed(RigId(1),Session(1))==null,"confirmed end");
            Need(bank.Clock.AttachHeld(NewOwner(1,12000,8))=="rod-already-attached"&&
                bank.Clock.AttachHeld(NewOwner(4,12000,8,session:RigId(1)))=="rod-identity-conflict","history collision");
            var other=new Bank();var advanced=NewOwner(1,12000,1);advanced.Advance(Session(1),1);
            Need(other.Clock.AttachHeld(advanced)=="bottom-clock-already-started","parallel clock");
        });
        return failures==0?0:1;
    }
}
