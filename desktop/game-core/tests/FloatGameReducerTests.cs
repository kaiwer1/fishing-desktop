using System;
using System.Collections.Generic;
using System.Reflection;
using FishingDesktop.GameCore;

static class FloatGameReducerTests
{
    static int passed,failed,nextId;
    static Guid G(){return new Guid(++nextId,0,0,new byte[]{0,0,0,0,0,0,0,1});}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    // All numbers below are test fixtures, NOT formal fish/economy parameters.
    static FloatGameRules Rules(int capacity=2,long walletLimit=Int64.MaxValue,long price=1000)
    {return new FloatGameRules(1,"fixture-float","fixture-worm",4,1000,capacity,walletLimit,new[]{new FloatSpeciesRule("carp",100000,2000000,price),new FloatSpeciesRule("crucian",100000,1000000,price)});}
    static FixedFloatRig Rig(){return new FixedFloatRig(G(),1,"fixture-float",G(),G(),G(),100000,G(),G(),G());}
    static FloatGameState New(FloatGameRules rules=null,long wallet=100,int stamina=1000,int bait=50)
    {return FloatGameState.CreateNew(G(),G(),wallet,stamina,G(),"fixture-worm",bait,Rig(),rules??Rules());}
    static CastFloat Cast(FloatGameState s){return new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static RecallFloat Recall(FloatGameState s){return new RecallFloat(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static ReleaseCurrentFloatFish Release(FloatGameState s){return new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static HostFloatFish Host(FloatGameState s,Guid? fish=null,Guid? encounter=null,string species="carp",long mass=500000)
    {return new HostFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Session.Id,fish??G(),encounter??G(),species,mass,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static SellFloatFish Sale(FloatGameState s,IEnumerable<Guid> ids){return new SellFloatFish(s.ProfileId,s.BranchId,G(),G(),ids,s.Sequence,s.Rig.Revision,s.RulesVersion);}
    static FloatReduction Run(FloatGameState s,FloatCommand c){return FloatGameReducer.Reduce(s,c,s.Rules);}
    // Explicit test adoption only. This does NOT simulate disk confirmation.
    static FloatGameState AcceptForNextInput(FloatReduction r){Need(r.HasCandidate&&r.RequiresConfirmation&&r.Candidate!=null&&r.Receipt!=null&&!r.IsReplay,"No candidate to adopt.");return r.Candidate;}
    static FloatGameState LandOne(FloatGameState state)
    {var cast=AcceptForNextInput(Run(state,Cast(state)));return AcceptForNextInput(Run(cast,Host(cast)));}
    static void Refused(FloatReduction r,string code){Need(r.Code==code&&!r.HasCandidate&&!r.RequiresConfirmation&&r.Receipt==null&&!r.IsReplay&&!r.OperationSucceeded,"Expected atomic rejection: "+code+", got "+r.Code);}
    static int Main()
    {
        Test("new state requires explicit assets and never grants default resources",()=>{
            var s=New(wallet:0,stamina:0,bait:0);Need(s.Sequence==0&&s.WalletMinor==0&&s.Stamina==0&&s.BaitCount==0&&s.Creel.Count==0&&s.Receipts.Count==0&&s.Session==null,"Creation replaced explicit zero values.");
        });
        Test("invalid new state and malformed configuration reject instead of falling back",()=>{
            Reject<ArgumentOutOfRangeException>(()=>New(wallet:-1));Reject<ArgumentOutOfRangeException>(()=>New(stamina:1001));Reject<ArgumentOutOfRangeException>(()=>New(bait:-1));
            Reject<ArgumentException>(()=>FloatGameState.CreateNew(Guid.Empty,G(),0,0,G(),"fixture-worm",0,Rig(),Rules()));
            Reject<ArgumentNullException>(()=>FloatGameState.CreateNew(G(),G(),0,0,G(),"fixture-worm",0,Rig(),null));
            Reject<ArgumentOutOfRangeException>(()=>new FloatGameRules(1,"fixture-float","fixture-worm",0,1000,1,1000,new FloatSpeciesRule[0]));
            Reject<ArgumentException>(()=>new FloatGameRules(1,"fixture-float","fixture-worm",4,1000,1,1000,new FloatSpeciesRule[0]));
        });
        Test("fixed complete rig rejects missing and multiply owned component references",()=>{
            var shared=G();Reject<ArgumentException>(()=>new FixedFloatRig(G(),1,"fixture-float",shared,shared,G(),1000,G(),G(),G()));
            Reject<ArgumentException>(()=>new FixedFloatRig(G(),1,"fixture-float",G(),G(),Guid.Empty,1000,G(),G(),G()));
            var rig=Rig();Reject<ArgumentException>(()=>FloatGameState.CreateNew(G(),G(),0,0,rig.RodId,"fixture-worm",0,rig,Rules()));
        });
        Test("state and configuration collections are defensively copied and read only",()=>{
            var rows=new List<FloatSpeciesRule>{new FloatSpeciesRule("carp",100000,2000000,1000)};var rules=new FloatGameRules(1,"fixture-float","fixture-worm",4,1000,2,10000,rows);var s=New(rules);rows.Clear();
            Need(rules.Species.Count==1,"Caller cleared configuration.");Reject<NotSupportedException>(()=>((IList<FloatFish>)s.Creel).Add(null));Reject<NotSupportedException>(()=>((IList<FloatReceipt>)s.Receipts).Clear());
        });
        Test("cast produces one candidate debit without changing its confirmed input",()=>{
            var s=New();var c=Cast(s);var r=Run(s,c);Need(r.HasCandidate&&r.RequiresConfirmation&&r.OperationSucceeded&&r.Receipt.Outcome==FloatOutcome.CastCommitted,"Cast not a confirmable candidate.");
            Need(s.Sequence==0&&s.BaitCount==50&&s.Stamina==1000&&s.Session==null&&s.Receipts.Count==0,"Reducer changed its input.");
            Need(r.Candidate.Sequence==1&&r.Candidate.BaitCount==49&&r.Candidate.Stamina==996&&r.Candidate.Session.Id==c.SessionId&&r.Candidate.Rig==s.Rig,"Cast duplicated equipment or mischarged.");
        });
        Test("discarded candidate does not consume assets and retry is deterministic",()=>{
            var s=New();var c=Cast(s);var first=Run(s,c);var retry=Run(s,c);
            Need(!retry.IsReplay&&s.Receipts.Count==0&&s.BaitCount==50&&retry.Candidate.BaitCount==first.Candidate.BaitCount&&retry.Candidate.Session.Id==first.Candidate.Session.Id&&retry.Receipt.RequestId==first.Receipt.RequestId,"Unconfirmed candidate was treated as saved history.");
        });
        Test("same confirmed cast request replays without another debit",()=>{
            var s=New();var c=Cast(s);var confirmed=AcceptForNextInput(Run(s,c));var replay=Run(confirmed,c);
            Need(replay.IsReplay&&!replay.HasCandidate&&!replay.RequiresConfirmation&&Object.ReferenceEquals(replay.Receipt,confirmed.Receipts[0])&&confirmed.BaitCount==49&&confirmed.Sequence==1,"Replay recomputed cast.");
        });
        Test("changing request ID cannot cast the same session again even after recall",()=>{
            var s=New();var c=Cast(s);var active=AcceptForNextInput(Run(s,c));
            var again=new CastFloat(s.ProfileId,s.BranchId,G(),G(),c.SessionId,active.Sequence,1,1);Refused(Run(active,again),"session-already-used");
            var ended=AcceptForNextInput(Run(active,Recall(active)));var late=new CastFloat(s.ProfileId,s.BranchId,G(),G(),c.SessionId,ended.Sequence,1,1);Refused(Run(ended,late),"session-already-used");Need(ended.BaitCount==49&&ended.Stamina==996,"Recall refunded cast.");
        });
        Test("same request with changed payload conflicts before version or configuration checks",()=>{
            var s=New();var c=Cast(s);var active=AcceptForNextInput(Run(s,c));var different=new CastFloat(s.ProfileId,s.BranchId,c.RequestId,G(),G(),active.Sequence,1,1);
            Refused(FloatGameReducer.Reduce(active,different,null),"request-conflict");Need(active.Sequence==1&&active.BaitCount==49,"Conflict changed state.");
        });
        Test("old receipts replay after later transitions without current rules or stale-version rejection",()=>{
            var s=New();var c=Cast(s);var active=AcceptForNextInput(Run(s,c));var ended=AcceptForNextInput(Run(active,Recall(active)));var replay=FloatGameReducer.Reduce(ended,c,null);
            Need(replay.IsReplay&&!replay.HasCandidate&&replay.Receipt.Sequence==1&&ended.Sequence==2&&ended.Session==null,"Old receipt overwrote current state or required new configuration.");
        });
        Test("a wrong profile or branch cannot query an old request receipt",()=>{
            var s=New();var c=Cast(s);var active=AcceptForNextInput(Run(s,c));var wrong=new CastFloat(s.ProfileId,G(),c.RequestId,c.EventId,c.SessionId,0,1,1);Refused(Run(active,wrong),"branch-mismatch");
        });
        Test("stale sequence rig and rule versions reject without partial changes",()=>{
            var s=New();foreach(int which in new[]{0,1,2}){
                var c=new CastFloat(s.ProfileId,s.BranchId,G(),G(),G(),which==0?1:0,which==1?2:1,which==2?2:1);Refused(Run(s,c),"version-conflict");
            }Need(s.Sequence==0&&s.BaitCount==50&&s.Stamina==1000,"Version rejection partially consumed resources.");
        });
        Test("empty bait insufficient stamina and missing rules do not create a session",()=>{
            var empty=New(bait:0);Refused(Run(empty,Cast(empty)),"bait-empty");var tired=New(stamina:3);Refused(Run(tired,Cast(tired)),"stamina-insufficient");var s=New();Refused(FloatGameReducer.Reduce(s,Cast(s),null),"configuration-missing");
            Need(empty.Session==null&&tired.Session==null&&tired.BaitCount==50,"Failed cast changed resources.");
        });
        Test("configuration contents cannot change under the same version",()=>{
            var s=New();var changed=Rules(price:2000);Refused(FloatGameReducer.Reduce(s,Cast(s),changed),"configuration-mismatch");
        });
        Test("recall ends only the current session and does not refund bait or stamina",()=>{
            var initial=New();var s=AcceptForNextInput(Run(initial,Cast(initial)));var c=Recall(s);var ended=AcceptForNextInput(Run(s,c));
            Need(ended.Session==null&&ended.Stamina==s.Stamina&&ended.BaitCount==s.BaitCount&&ended.Creel.Count==0&&ended.Rig.LineLengthMm==s.Rig.LineLengthMm,"Recall refunded or created assets.");Need(Run(ended,c).IsReplay,"Recall reply did not replay.");
            Refused(Run(ended,new RecallFloat(ended.ProfileId,ended.BranchId,G(),G(),s.Session.Id,ended.Sequence,1,1)),"session-not-current");
        });
        Test("reusing a business event with a different request or session rejects",()=>{
            var s=New();var c=Cast(s);var active=AcceptForNextInput(Run(s,c));Refused(Run(active,new RecallFloat(s.ProfileId,s.BranchId,G(),c.EventId,c.SessionId,active.Sequence,1,1)),"event-already-used");
        });
        Test("host fish needs an explicit configured species and bounded integer mass",()=>{
            var initial=New();var s=AcceptForNextInput(Run(initial,Cast(initial)));Refused(Run(s,Host(s,species:"unknown")),"configuration-missing");Refused(Run(s,Host(s,mass:99999)),"fish-mass-invalid");Refused(Run(s,Host(s,mass:2000001)),"fish-mass-invalid");
            Need(s.Creel.Count==0&&s.Session.PendingFish==null&&s.Stamina==996,"Invalid host outcome refunded or partially landed.");
        });
        Test("host terminal fish locks original facts and value without public arbitrary-price fields",()=>{
            var initial=New();var active=AcceptForNextInput(Run(initial,Cast(initial)));var host=Host(active);var s=AcceptForNextInput(Run(active,host));var fish=s.Creel[0];
            Need(s.Session==null&&fish.Id==host.FishId&&fish.EncounterId==host.EncounterId&&fish.MassMg==500000&&fish.SaleValueMinor==500&&s.WalletMinor==100&&s.Rig==initial.Rig,"Fish facts/value or equipment changed incorrectly.");
            Need(Run(s,host).IsReplay,"Host terminal repeated instead of replaying.");
        });
        Test("host fish cannot reuse an owned equipment identity",()=>{
            var initial=New();var s=AcceptForNextInput(Run(initial,Cast(initial)));Refused(Run(s,Host(s,fish:s.Rig.HookId)),"fish-identity-used");
        });
        Test("fish and encounter identities cannot be recreated after selling",()=>{
            var first=LandOne(New());var old=first.Creel[0];var sold=AcceptForNextInput(Run(first,Sale(first,new[]{old.Id})));var active=AcceptForNextInput(Run(sold,Cast(sold)));
            Refused(Run(active,Host(active,fish:old.Id)),"fish-identity-used");Refused(Run(active,Host(active,encounter:old.EncounterId)),"encounter-identity-used");
        });
        Test("capacity refusal has a required candidate preserving the same current fish",()=>{
            var full=LandOne(New(Rules(capacity:1)));var old=full.Creel[0];var active=AcceptForNextInput(Run(full,Cast(full)));var host=Host(active);var result=Run(active,host);
            Need(!result.OperationSucceeded&&result.Code=="creel-capacity-exceeded"&&result.HasCandidate&&result.RequiresConfirmation&&!result.IsReplay,"Capacity refusal could be mistaken for a no-save error.");
            Need(active.Session.PendingFish==null&&active.Creel.Count==1&&active.Sequence==3,"Capacity outcome mutated confirmed input.");
            var s=AcceptForNextInput(result);Need(s.Session.Id==active.Session.Id&&s.Session.PendingFish.Id==host.FishId&&s.Session.PendingFish.EncounterId==host.EncounterId&&s.Creel[0]==old&&s.Creel.Count==1&&s.Sequence==4&&s.WalletMinor==100,"Capacity outcome lost current fish or replaced old creel.");
        });
        Test("capacity-blocked state cannot sell old fish recall cast or replace the pending fish",()=>{
            var full=LandOne(New(Rules(capacity:1)));var active=AcceptForNextInput(Run(full,Cast(full)));var s=AcceptForNextInput(Run(active,Host(active)));var pending=s.Session.PendingFish;
            Refused(Run(s,Sale(s,new[]{s.Creel[0].Id})),"session-busy");Refused(Run(s,Recall(s)),"release-current-required");Refused(Run(s,Cast(s)),"session-busy");Refused(Run(s,Host(s)),"pending-fish-already-set");
            Need(s.Session.PendingFish==pending&&s.Creel.Count==1&&s.Sequence==4,"Blocked operations changed pending ownership.");
        });
        Test("explicit release settles pending fish once without changing old creel or refunding",()=>{
            var full=LandOne(New(Rules(capacity:1)));var active=AcceptForNextInput(Run(full,Cast(full)));var pending=AcceptForNextInput(Run(active,Host(active)));var c=Release(pending);var ended=AcceptForNextInput(Run(pending,c));
            Need(ended.Session==null&&ended.Creel.Count==1&&ended.Creel[0]==full.Creel[0]&&ended.BaitCount==48&&ended.Stamina==992&&ended.WalletMinor==100,"Release changed old assets or refunded.");
            Need(Run(ended,c).IsReplay,"Repeated release did not replay.");Refused(Run(ended,new ReleaseCurrentFloatFish(ended.ProfileId,ended.BranchId,G(),G(),pending.Session.Id,ended.Sequence,1,1)),"pending-fish-not-current");
        });
        Test("replaying a historical capacity refusal never restores its old pending state",()=>{
            var full=LandOne(New(Rules(capacity:1)));var active=AcceptForNextInput(Run(full,Cast(full)));var host=Host(active);var pending=AcceptForNextInput(Run(active,host));var ended=AcceptForNextInput(Run(pending,Release(pending)));var replay=Run(ended,host);
            Need(replay.IsReplay&&!replay.OperationSucceeded&&!replay.HasCandidate&&!replay.RequiresConfirmation&&ended.Session==null&&ended.Sequence==5,"Old capacity receipt resurrected fish/session.");
        });
        Test("release without a pending fish cannot release an owned old creel fish",()=>{
            var s=LandOne(New());var c=new ReleaseCurrentFloatFish(s.ProfileId,s.BranchId,G(),G(),s.Creel[0].SessionId,s.Sequence,1,1);Refused(Run(s,c),"pending-fish-not-current");Need(s.Creel.Count==1,"Old creel was released.");
        });
        Test("sale freezes copied fish IDs and adds only stored integer values atomically",()=>{
            var s=LandOne(LandOne(New()));var selected=new List<Guid>{s.Creel[0].Id};var c=Sale(s,selected);selected.Clear();selected.Add(G());var r=Run(s,c);var next=AcceptForNextInput(r);
            Need(s.Creel.Count==2&&s.WalletMinor==100&&next.Creel.Count==1&&next.Creel[0].Id==s.Creel[1].Id&&next.WalletMinor==600&&r.Receipt.FishIds[0]==s.Creel[0].Id,"Sale used mutable selection or altered the source.");
            Reject<NotSupportedException>(()=>((IList<Guid>)c.FishIds).Clear());Reject<NotSupportedException>(()=>((IList<Guid>)r.Receipt.FishIds).Clear());
        });
        Test("batch containing one missing fish or duplicate IDs rejects every fish and balance change",()=>{
            var s=LandOne(LandOne(New()));Refused(Run(s,Sale(s,new[]{s.Creel[0].Id,G()})),"fish-not-owned");Refused(Run(s,Sale(s,new[]{s.Creel[0].Id,s.Creel[0].Id})),"fish-selection-invalid");Refused(Run(s,Sale(s,new Guid[0])),"fish-selection-invalid");Need(s.Creel.Count==2&&s.WalletMinor==100&&s.Sequence==4,"Batch rejection partially sold.");
        });
        Test("normalized reordered batch selection replays without a second sale",()=>{
            var s=LandOne(LandOne(New()));var c=Sale(s,new[]{s.Creel[0].Id,s.Creel[1].Id});var sold=AcceptForNextInput(Run(s,c));var reordered=new SellFloatFish(s.ProfileId,s.BranchId,c.RequestId,c.EventId,new[]{s.Creel[1].Id,s.Creel[0].Id},s.Sequence,1,1);
            Need(Run(sold,reordered).IsReplay&&sold.Creel.Count==0&&sold.WalletMinor==1100,"Order changed semantics or replay paid twice.");Refused(Run(sold,Sale(sold,new[]{s.Creel[0].Id})),"fish-not-owned");
        });
        Test("wallet integer overflow and configured wallet limit reject before removing fish",()=>{
            var rich=LandOne(New(wallet:Int64.MaxValue-100));Refused(Run(rich,Sale(rich,new[]{rich.Creel[0].Id})),"arithmetic-overflow");Need(rich.Creel.Count==1&&rich.WalletMinor==Int64.MaxValue-100,"Overflow removed fish.");
            var limited=LandOne(New(Rules(walletLimit:1000),wallet:900));Refused(Run(limited,Sale(limited,new[]{limited.Creel[0].Id})),"wallet-limit-exceeded");Need(limited.Creel.Count==1&&limited.WalletMinor==900,"Wallet limit partially changed assets.");
        });
        Test("price multiplication overflow does not create fish or lose the active cast",()=>{
            var initial=New(Rules(price:Int64.MaxValue));var s=AcceptForNextInput(Run(initial,Cast(initial)));Refused(Run(s,Host(s)),"arithmetic-overflow");Need(s.Session!=null&&!s.Session.CapacityBlocked&&s.Creel.Count==0&&s.Sequence==1&&s.BaitCount==49,"Pricing overflow partially settled or refunded cast.");
        });
        Test("host weight and fish construction are not public user command APIs",()=>{
            Need(typeof(HostFloatFish).IsNotPublic&&typeof(HostFloatFish).GetConstructors().Length==0&&typeof(FloatFish).GetConstructors().Length==0,"Arbitrary host facts became public.");
            foreach(var field in typeof(SellFloatFish).GetFields(BindingFlags.Public|BindingFlags.Instance))Need(field.Name!="MassMg"&&field.Name!="SaleValueMinor"&&field.Name!="Price","User sale carries host-controlled value.");
        });
        Test("a complete discard retry recall land sell and receipt replay flow conserves resources",()=>{
            var original=New();var c=Cast(original);Run(original,c);var active=AcceptForNextInput(Run(original,c));var ended=AcceptForNextInput(Run(active,Recall(active)));var caught=LandOne(ended);var sale=Sale(caught,new[]{caught.Creel[0].Id});var candidate=Run(caught,sale);
            Need(caught.WalletMinor==100&&caught.Creel.Count==1,"Unconfirmed sale was published.");var sold=AcceptForNextInput(candidate);Need(sold.BaitCount==48&&sold.Stamina==992&&sold.WalletMinor==600&&sold.Creel.Count==0&&sold.Session==null&&Run(sold,sale).IsReplay,"End-to-end transaction conservation failed.");
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
