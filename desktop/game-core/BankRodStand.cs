using System;
using System.Collections.Generic;
using System.Threading;

namespace FishingDesktop.GameCore
{
    internal enum BankRodPhase { Idle, Charging, Flying, Settling, Waiting, Biting, Fighting, PendingResult }
    internal enum BankStandKey { Place, Take, One, Two, Three }
    internal enum BankTransferKind { Place, Take }

    // Read-only host facts. The owner retains all gear instances and fishing
    // sessions. A stand never issues, copies, advances or settles them.
    internal sealed class BankRodState
    {
        internal readonly Guid RigId,SessionId;
        internal readonly long RigRevision;
        internal readonly BankRodPhase Phase;
        internal readonly bool StandCompatible;
        internal BankRodState(Guid rig,long rigRevision,Guid session,BankRodPhase phase,bool standCompatible)
        {
            if(rig==Guid.Empty || rigRevision<1 || !Enum.IsDefined(typeof(BankRodPhase),phase) ||
                ((phase==BankRodPhase.Idle || phase==BankRodPhase.Charging)!=(session==Guid.Empty)))
                throw new ArgumentException("Invalid authoritative rig facts.");
            RigId=rig;RigRevision=rigRevision;SessionId=session;Phase=phase;StandCompatible=standCompatible;
        }
    }
    internal sealed class BankStandPose
    {
        internal readonly string Water,Spot;
        internal readonly int Xmm,Zmm,AimedSlot;
        internal BankStandPose(string water,string spot,int xMm,int zMm,int aimedSlot)
        {
            if(String.IsNullOrEmpty(water)||String.IsNullOrEmpty(spot)||Math.Abs((long)xMm)>1000000 ||
                Math.Abs((long)zMm)>1000000 || aimedSlot<0 || aimedSlot>3)throw new ArgumentException("Invalid bank pose.");
            Water=water;Spot=spot;Xmm=xMm;Zmm=zMm;AimedSlot=aimedSlot;
        }
    }
    internal sealed class BankTransfer
    {
        internal readonly Guid Id;
        internal readonly int Slot;
        internal readonly long Epoch,Revision;
        internal readonly BankTransferKind Kind;
        internal readonly BankRodState Rod;
        internal readonly bool RequiresAim,NextRequiresAim;
        internal readonly int NextSlot;
        internal readonly BankRodState NextRod;
        internal BankTransfer(Guid id,int slot,long epoch,long revision,BankTransferKind kind,BankRodState rod,bool requiresAim,
            int nextSlot=0,BankRodState next=null,bool nextRequiresAim=false)
        {Id=id;Slot=slot;Epoch=epoch;Revision=revision;Kind=kind;Rod=rod;RequiresAim=requiresAim;NextSlot=nextSlot;NextRod=next;NextRequiresAim=nextRequiresAim;}
    }
    internal sealed class BankRodStand
    {
        internal const int ReachMm=1800,SpacingMm=750;
        internal readonly Guid StandId;
        internal readonly string Water,Spot;
        internal readonly int CenterXmm,CenterZmm;
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        readonly Func<Guid,BankRodState> readRig;
        readonly Guid[] slots;
        readonly long[] slotRevisions={1,1,1};
        internal long Revision {get;private set;}
        internal long ControlEpoch {get;private set;}
        internal long HandGeneration {get;private set;}
        internal Guid Held {get;private set;}
        internal BankTransfer Pending {get;private set;}
        bool allowed;
        long lastInput;
        bool checking;
        sealed class Receipt
        {
            internal readonly BankTransfer Transfer,Next;
            internal Receipt(BankTransfer transfer,BankTransfer next){Transfer=transfer;Next=next;}
        }
        readonly Dictionary<Guid,Receipt> receipts=new Dictionary<Guid,Receipt>();

        // This fixed three-post rack faces +Z. caller supplies an approved
        // bank placement and existing rig IDs; omitted rigs remain in storage.
        internal BankRodStand(Guid standId,string water,string spot,int centerXmm,int centerZmm,Guid held,
            Guid first,Guid second,Guid third,Func<Guid,BankRodState> readRig)
        {
            if(standId==Guid.Empty||String.IsNullOrEmpty(water)||String.IsNullOrEmpty(spot)||readRig==null ||
                Math.Abs((long)centerXmm)>1000000-SpacingMm||Math.Abs((long)centerZmm)>1000000)throw new ArgumentException("Invalid stand.");
            StandId=standId;Water=water;Spot=spot;CenterXmm=centerXmm;CenterZmm=centerZmm;Held=held;this.readRig=readRig;
            slots=new[]{first,second,third};var ids=new HashSet<Guid>{standId};var sessions=new HashSet<Guid>();
            foreach(var id in new[]{held,first,second,third}){
                if(id==Guid.Empty)continue;
                var state=readRig(id);
                if(!ids.Add(id)||state==null||state.RigId!=id || (state.SessionId!=Guid.Empty&&!sessions.Add(state.SessionId)))
                    throw new ArgumentException("Unknown or multiply placed rig/session.");
                if(id!=held && Eligible(state)!=null)throw new ArgumentException("Ineligible rig in stand slot.");
            }
            if(ids.Overlaps(sessions))throw new ArgumentException("Session identity conflicts with its physical owner.");
            Revision=1;HandGeneration=1;
        }
        void Entry(){
            if(thread!=Thread.CurrentThread.ManagedThreadId || checking)throw new InvalidOperationException("Stand ownership is single-threaded.");
        }
        BankRodState Read(Guid rig){
            checking=true;try{var s=readRig(rig);return s!=null && s.RigId==rig?s:null;}finally{checking=false;}
        }
        static int Index(int slot){if(slot<1||slot>3)throw new ArgumentOutOfRangeException("slot");return slot-1;}
        internal Guid At(int slot){return slots[Index(slot)];}
        internal long SlotRevision(int slot){return slotRevisions[Index(slot)];}
        internal int SlotXmm(int slot){return CenterXmm+(Index(slot)-1)*SpacingMm;}
        internal BankTransfer Committed(Guid id){Receipt r;return receipts.TryGetValue(id,out r)?r.Transfer:null;}

        internal string DrawStored(Guid rig,BankStandPose pose,long epoch,long inputSequence,long expectedRevision,out BankTransfer transfer)
        {
            Entry();transfer=null;
            if(!allowed||epoch!=ControlEpoch)return "control-required";
            if(inputSequence<=lastInput)return "stale-input";
            lastInput=inputSequence;
            if(Pending!=null)return "handoff-busy";
            if(expectedRevision!=Revision)return "ownership-stale";
            if(Held!=Guid.Empty)return "hands-full";
            if(pose==null||pose.Water!=Water||pose.Spot!=Spot)return "location-stale";
            if(rig==StandId||rig==Guid.Empty||Array.IndexOf(slots,rig)>=0)return "rig-not-stored";
            var rod=Read(rig);if(rod==null||rod.Phase!=BankRodPhase.Idle)return "rig-not-stored";
            transfer=new BankTransfer(Guid.NewGuid(),0,epoch,Revision,BankTransferKind.Take,rod,false);
            HandGeneration=checked(HandGeneration+1);Pending=transfer;return null;
        }

        internal void SetControl(long epoch,bool active)
        {
            Entry();if(epoch<=ControlEpoch)throw new ArgumentOutOfRangeException("epoch","Every grant or revocation uses a fresh generation.");
            ControlEpoch=epoch;allowed=active;Pending=null;lastInput=0;HandGeneration=checked(HandGeneration+1);
        }
        internal bool Cancel(BankTransfer ticket)
        {
            Entry();if(ticket==null||ticket!=Pending)return false;
            Pending=null;return true;
        }
        internal bool CanOperateHeld(Guid rig,Guid session,long epoch,long generation)
        {
            Entry();if(!allowed||Pending!=null||rig==Guid.Empty||rig!=Held||epoch!=ControlEpoch||generation!=HandGeneration)return false;
            var current=Read(rig);return current!=null && current.SessionId==session;
        }
        bool Reach(BankStandPose pose,int slot,bool aim)
        {
            if(pose==null||pose.Water!=Water||pose.Spot!=Spot||(aim&&pose.AimedSlot!=slot))return false;
            long x=(long)pose.Xmm-SlotXmm(slot),z=(long)pose.Zmm-CenterZmm;
            return x*x+z*z<=(long)ReachMm*ReachMm;
        }
        static string Eligible(BankRodState rod)
        {
            if(rod==null)return "rig-unavailable";
            if(rod.Phase==BankRodPhase.Charging)return "cast-cancel-required";
            if(rod.Phase==BankRodPhase.Flying||rod.Phase==BankRodPhase.Settling||rod.Phase==BankRodPhase.Fighting||rod.Phase==BankRodPhase.PendingResult)return "rig-busy";
            return rod.StandCompatible?null:"stand-incompatible";
        }
        static bool Same(BankRodState frozen,BankRodState current)
        {return current!=null && frozen.RigId==current.RigId && frozen.RigRevision==current.RigRevision && frozen.SessionId==current.SessionId;}
        internal string Begin(BankStandKey key,BankStandPose pose,long epoch,long inputSequence,long expectedRevision,
            Guid expectedHeld,Guid expectedOccupant,out BankTransfer transfer)
        {
            Entry();transfer=null;
            if(!allowed||epoch!=ControlEpoch)return "control-required";
            if(inputSequence<=lastInput)return "stale-input";
            if(!Enum.IsDefined(typeof(BankStandKey),key)||pose==null)return "invalid-intent";
            lastInput=inputSequence;
            if(Pending!=null)return "handoff-busy";
            if(expectedRevision!=Revision || expectedHeld!=Held)return "ownership-stale";
            bool aimed=key==BankStandKey.Place || key==BankStandKey.Take;
            int slot=aimed?pose.AimedSlot:(int)key-(int)BankStandKey.One+1;
            if(slot<1 || slot>3 || !Reach(pose,slot,aimed))return "slot-unreachable";
            Guid occupied=At(slot);if(occupied!=expectedOccupant)return "ownership-stale";
            if(key==BankStandKey.Place){
                if(Held==Guid.Empty)return "hands-empty";
                if(occupied!=Guid.Empty)return "slot-occupied";
                var rod=Read(Held);string reason=Eligible(rod);if(reason!=null)return reason;
                transfer=new BankTransfer(Guid.NewGuid(),slot,epoch,Revision,BankTransferKind.Place,rod,true);
            }else{
                if(occupied==Guid.Empty)return "slot-empty";
                var rod=Read(occupied);string reason=Eligible(rod);if(reason!=null)return reason;
                if(Held==Guid.Empty)transfer=new BankTransfer(Guid.NewGuid(),slot,epoch,Revision,BankTransferKind.Take,rod,aimed);
                else{
                    var held=Read(Held);reason=Eligible(held);if(reason!=null)return reason;
                    int spare=1;while(spare<=3 && (At(spare)!=Guid.Empty || !Reach(pose,spare,false)))spare++;
                    if(spare>3)return "no-reachable-empty-slot";
                    transfer=new BankTransfer(Guid.NewGuid(),spare,epoch,Revision,BankTransferKind.Place,held,false,slot,rod,aimed);
                }
            }
            HandGeneration=checked(HandGeneration+1);Pending=transfer;return null;
        }
        internal string Commit(BankTransfer ticket,BankStandPose pose,out BankTransfer next)
        {
            Entry();next=null;
            if(ticket==null)return "handoff-not-current";
            Receipt previous;
            if(receipts.TryGetValue(ticket.Id,out previous) && previous.Transfer==ticket){
                if(Pending==previous.Next)next=previous.Next;
                return "handoff-replayed";
            }
            if(ticket!=Pending)return "handoff-not-current";
            Pending=null;
            if(!allowed || ticket.Epoch!=ControlEpoch)return "control-required";
            if(ticket.Revision!=Revision)return "ownership-stale";
            if(ticket.Slot!=0 && !Reach(pose,ticket.Slot,ticket.RequiresAim))return "slot-unreachable";
            if(ticket.Slot==0 && (pose==null||pose.Water!=Water||pose.Spot!=Spot))return "location-stale";
            var current=Read(ticket.Rod.RigId);if(!Same(ticket.Rod,current))return "rig-stale";
            if(ticket.Slot==0){
                if(Held!=Guid.Empty || current.Phase!=BankRodPhase.Idle || Array.IndexOf(slots,current.RigId)>=0)return "rig-not-stored";
            }else{string reason=Eligible(current);if(reason!=null)return reason;}
            if(ticket.Kind==BankTransferKind.Place){
                if(Held!=ticket.Rod.RigId || At(ticket.Slot)!=Guid.Empty)return "ownership-stale";
                slots[ticket.Slot-1]=Held;Held=Guid.Empty;
            }else{
                if(Held!=Guid.Empty || (ticket.Slot!=0 && At(ticket.Slot)!=ticket.Rod.RigId))return "ownership-stale";
                Held=ticket.Rod.RigId;if(ticket.Slot!=0)slots[ticket.Slot-1]=Guid.Empty;
            }
            Revision=checked(Revision+1);
            if(ticket.Slot!=0)slotRevisions[ticket.Slot-1]=checked(slotRevisions[ticket.Slot-1]+1);
            if(ticket.NextRod!=null){
                // A switch commits placement first, then prepares a distinct
                // take. Failure or cancellation later leaves that placement.
                next=new BankTransfer(Guid.NewGuid(),ticket.NextSlot,ControlEpoch,Revision,BankTransferKind.Take,ticket.NextRod,ticket.NextRequiresAim);
                Pending=next;
            }
            receipts.Add(ticket.Id,new Receipt(ticket,next));
            return null;
        }
    }
}
