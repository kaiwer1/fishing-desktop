using System;
using System.Collections.Generic;
using FishingDesktop.GameCore;

static class BankRodStandTests
{
    static Guid Id(int n){return new Guid(n,0,0,new byte[8]);}
    static Guid G(int n){return n==0?Guid.Empty:Id(n);}
    static readonly Guid A=Id(1),B=Id(2),C=Id(3),D=Id(4);
    static BankStandPose Pose(int slot=0,int x=0,int z=0,string water="lake",string spot="bank")
    {return new BankStandPose(water,spot,x,z,slot);}
    sealed class Fixture
    {
        internal readonly Dictionary<Guid,BankRodState> Rods=new Dictionary<Guid,BankRodState>();
        internal readonly BankRodStand Stand;internal long Seq;
        internal Fixture(int held=1,int first=0,int second=2,int third=3)
        {
            for(int i=1;i<=4;i++)Set(Id(i),BankRodPhase.Waiting);
            Stand=new BankRodStand(Id(99),"lake","bank",0,0,G(held),G(first),G(second),G(third),
                id=>Rods.ContainsKey(id)?Rods[id]:null);Stand.SetControl(1,true);
        }
        internal void Set(Guid rig,BankRodPhase phase,long revision=4,bool supports=true,int session=0)
        {
            Guid active=phase==BankRodPhase.Idle||phase==BankRodPhase.Charging?Guid.Empty:Id(session==0?100+rig.ToByteArray()[0]:session);
            Rods[rig]=new BankRodState(rig,revision,active,phase,supports);
        }
        internal string Begin(BankStandKey key,BankStandPose pose,out BankTransfer t)
        {
            int slot=key==BankStandKey.Place||key==BankStandKey.Take?pose.AimedSlot:(int)key-(int)BankStandKey.One+1;
            return Stand.Begin(key,pose,Stand.ControlEpoch,++Seq,Stand.Revision,Stand.Held,slot==0?Guid.Empty:Stand.At(slot),out t);
        }
    }
    static int passed,failed;
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception x){failed++;Console.WriteLine("FAIL "+name+": "+x.Message);}}
    static void Need(bool value){if(!value)throw new Exception("Unexpected stand state");}
    static void Throws<T>(Action body)where T:Exception{try{body();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    public static int Main(){
        Test("slot numbers have fixed physical locations and duplicate ownership is refused",()=>{
            var f=new Fixture();Need(f.Stand.At(2)==B && f.Stand.SlotXmm(1)==-750 && f.Stand.SlotXmm(2)==0 && f.Stand.SlotXmm(3)==750);
            Throws<ArgumentException>(()=>new Fixture(1,1,0,0));Throws<ArgumentException>(()=>new Fixture(1,5,0,0));
            Throws<ArgumentException>(()=>new BankRodStand(Id(99),"lake","bank",0,0,A,B,C,Guid.Empty,id=>new BankRodState(id,1,Id(100),BankRodPhase.Waiting,true)));
        });
        Test("X reserves the held rig without moving it until commit and consumes the old hold",()=>{
            var f=new Fixture();long held=f.Stand.HandGeneration;BankTransfer t,n;
            Need(f.Stand.CanOperateHeld(A,Id(101),1,held));Need(f.Begin(BankStandKey.Place,Pose(1),out t)==null);
            Need(f.Stand.Held==A && f.Stand.At(1)==Guid.Empty && f.Stand.Revision==1 && !f.Stand.CanOperateHeld(A,Id(101),1,held));
            Need(f.Stand.Commit(t,Pose(1),out n)==null && n==null && f.Stand.Held==Guid.Empty && f.Stand.At(1)==A && f.Stand.SlotRevision(1)==2);
        });
        Test("E takes only the aimed reachable rig and preserves authoritative session identity",()=>{
            var f=new Fixture(0);BankTransfer t,n;
            Need(f.Begin(BankStandKey.Take,Pose(2),out t)==null && t.Rod==f.Rods[B]);
            Need(f.Stand.Commit(t,Pose(3),out n)=="slot-unreachable" && f.Stand.At(2)==B);
            Need(f.Begin(BankStandKey.Take,Pose(2),out t)==null && f.Stand.Commit(t,Pose(2),out n)==null);
            Need(f.Stand.Held==B && f.Stand.At(2)==Guid.Empty && f.Stand.CanOperateHeld(B,Id(102),1,f.Stand.HandGeneration));
        });
        Test("keys one two three refer to stable slots independent of aim and switching uses two commits",()=>{
            var f=new Fixture();BankTransfer t,n,unused;
            foreach(var k in new[]{BankStandKey.Two,BankStandKey.Three,BankStandKey.One}){
                Need(f.Begin(k,Pose(),out t)==null);
                var old=f.Stand.Held;var desired=t.NextRod;Need(desired!=null);
                Need(f.Stand.Commit(t,Pose(),out n)==null && n!=null && f.Stand.Held==Guid.Empty && f.Stand.At(t.Slot)==old);
                Need(f.Stand.Committed(t.Id)==t && f.Stand.Commit(n,Pose(),out unused)==null && unused==null && f.Stand.Held==desired.RigId && f.Stand.Committed(n.Id)==n);
            }
        });
        Test("one occupied hand and three occupied slots cannot silently put an active rod in inventory",()=>{
            var f=new Fixture(1,4,2,3);BankTransfer t;Need(f.Begin(BankStandKey.Two,Pose(),out t)=="no-reachable-empty-slot" && t==null && f.Stand.Held==A && f.Stand.At(1)==D);
        });
        Test("empty hands, empty slot, full slot and no aimed post have distinct refusals",()=>{
            var f=new Fixture();BankTransfer t;Need(f.Begin(BankStandKey.Place,Pose(2),out t)=="slot-occupied" &&
                f.Begin(BankStandKey.Take,Pose(1),out t)=="slot-empty" && f.Begin(BankStandKey.Place,Pose(),out t)=="slot-unreachable");
            Need(new Fixture(0).Begin(BankStandKey.Place,Pose(1),out t)=="hands-empty");
        });
        Test("selection never reaches through a different water or beyond the actual post radius",()=>{
            var f=new Fixture(0);BankTransfer t,n;
            Need(f.Begin(BankStandKey.Two,Pose(2,0,1801),out t)=="slot-unreachable");
            Need(f.Begin(BankStandKey.Two,Pose(2,0,1800),out t)==null && f.Stand.Commit(t,Pose(2,0,0,"coast"),out n)=="slot-unreachable");
            Need(f.Stand.At(2)==B && f.Stand.Revision==1);
        });
        Test("charging needs pre-cast cancellation; flying, sinking, fighting and pending rods cannot be placed",()=>{
            var f=new Fixture();BankTransfer t;
            f.Set(A,BankRodPhase.Charging);Need(f.Begin(BankStandKey.Place,Pose(1),out t)=="cast-cancel-required");
            foreach(var p in new[]{BankRodPhase.Flying,BankRodPhase.Settling,BankRodPhase.Fighting,BankRodPhase.PendingResult}){
                f.Set(A,p);Need(f.Begin(BankStandKey.Place,Pose(1),out t)=="rig-busy");}
            f.Set(A,BankRodPhase.Waiting,4,false);Need(f.Begin(BankStandKey.Place,Pose(1),out t)=="stand-incompatible");
            Need(f.Stand.Held==A && f.Stand.Revision==1);
        });
        Test("stale rig revision or session and newly started flight invalidate the exact pending transfer",()=>{
            var f=new Fixture();BankTransfer t,n;
            Need(f.Begin(BankStandKey.Place,Pose(1),out t)==null);f.Set(A,BankRodPhase.Waiting,5);Need(f.Stand.Commit(t,Pose(1),out n)=="rig-stale");
            Need(f.Begin(BankStandKey.Place,Pose(1),out t)==null);f.Set(A,BankRodPhase.Waiting,5,true,500);Need(f.Stand.Commit(t,Pose(1),out n)=="rig-stale");
            Need(f.Begin(BankStandKey.Place,Pose(1),out t)==null);f.Set(A,BankRodPhase.Flying,5,true,500);Need(f.Stand.Commit(t,Pose(1),out n)=="rig-busy");
            Need(f.Stand.Held==A && f.Stand.At(1)==Guid.Empty);
        });
        Test("X and E cannot race, older input sequence and ownership revisions are rejected",()=>{
            var f=new Fixture();BankTransfer t,n;Need(f.Begin(BankStandKey.Place,Pose(1),out t)==null && f.Begin(BankStandKey.Take,Pose(2),out n)=="handoff-busy");
            Need(f.Stand.Cancel(t));Need(f.Stand.Begin(BankStandKey.Place,Pose(1),1,1,1,A,Guid.Empty,out n)=="stale-input");
            Need(f.Stand.Begin(BankStandKey.Place,Pose(1),1,3,2,A,Guid.Empty,out n)=="ownership-stale");
            Need(f.Stand.Begin(BankStandKey.Take,Pose(2),1,4,1,A,C,out n)=="ownership-stale");
        });
        Test("focus loss or menu pause before commit cancels without later restoration or late input",()=>{
            var f=new Fixture();BankTransfer t,n;Need(f.Begin(BankStandKey.Place,Pose(1),out t)==null);
            f.Stand.SetControl(2,false);Need(f.Stand.Commit(t,Pose(1),out n)=="handoff-not-current" && f.Stand.Held==A);
            Need(f.Begin(BankStandKey.Place,Pose(1),out n)=="control-required");f.Stand.SetControl(3,true);
            Need(f.Stand.Begin(BankStandKey.Place,Pose(1),1,4,1,A,Guid.Empty,out n)=="control-required" && f.Stand.Pending==null);
            Throws<ArgumentOutOfRangeException>(()=>f.Stand.SetControl(2,true));
        });
        Test("a failed second leg cannot roll back a confirmed placement or copy rod B",()=>{
            var f=new Fixture();BankTransfer t,n,retry;Need(f.Begin(BankStandKey.Two,Pose(),out t)==null && f.Stand.Revision==1);
            Need(f.Stand.Commit(t,Pose(),out n)==null && n!=null);f.Set(B,BankRodPhase.Fighting);
            Need(f.Stand.Commit(n,Pose(),out retry)=="rig-busy" && f.Stand.Held==Guid.Empty && f.Stand.At(1)==A && f.Stand.At(2)==B);
            Need(f.Stand.Commit(t,Pose(),out retry)=="handoff-replayed" && retry==null && f.Stand.Revision==2);
        });
        Test("E switches still require aiming at the target on the separate take commit",()=>{
            var f=new Fixture();BankTransfer t,n,z;Need(f.Begin(BankStandKey.Take,Pose(2),out t)==null);Need(f.Stand.Commit(t,Pose(2),out n)==null);
            Need(f.Stand.Commit(n,Pose(1),out z)=="slot-unreachable" && f.Stand.Held==Guid.Empty && f.Stand.At(2)==B);
        });
        Test("a confirmed callback replay exposes the pending next leg once without applying either move twice",()=>{
            var f=new Fixture();BankTransfer t,n,duplicate;Need(f.Begin(BankStandKey.Three,Pose(),out t)==null);Need(f.Stand.Commit(t,Pose(),out n)==null);
            Need(f.Stand.Commit(t,Pose(),out duplicate)=="handoff-replayed" && duplicate==n && f.Stand.Revision==2);
            Need(f.Stand.Commit(n,Pose(),out duplicate)==null);Need(f.Stand.Commit(n,Pose(),out duplicate)=="handoff-replayed" && f.Stand.Revision==3 && f.Stand.Held==C);
        });
        Test("stored owned idle rod can enter empty hands, but a rack slot cannot act as duplicate inventory",()=>{
            var f=new Fixture(0,1,2,3);BankTransfer t,n;
            Need(f.Stand.DrawStored(B,Pose(),1,1,1,out t)=="rig-not-stored");Need(f.Stand.DrawStored(D,Pose(),1,2,1,out t)=="rig-not-stored");
            f.Set(D,BankRodPhase.Idle);Need(f.Stand.DrawStored(D,Pose(),1,3,1,out t)==null && f.Stand.Held==Guid.Empty);
            Need(f.Stand.Commit(t,Pose(),out n)==null && f.Stand.Held==D && f.Stand.At(1)==A && f.Stand.At(2)==B && f.Stand.At(3)==C);
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
