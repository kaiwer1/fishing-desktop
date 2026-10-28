using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FishingDesktop.GameCore
{
    internal static class FloatEquipmentGuard
    {
        internal const int MaximumInstances=128;
        internal const long MaximumLineMm=100000000;
        internal static void Id(Guid id,string name){if(id==Guid.Empty)throw new ArgumentException("A nonempty identity is required.",name);}
        internal static string Key(string value)
        {
            if(String.IsNullOrEmpty(value)||value.Length>96)throw new ArgumentException("A bounded definition key is required.");
            foreach(char c in value)if(!((c>='a'&&c<='z')||(c>='0'&&c<='9')||c=='-'||c=='_'||c=='.'))throw new ArgumentException("Definition keys use lowercase ASCII identifiers.");
            return value;
        }
        internal static void Revision(long revision){if(revision<1)throw new ArgumentOutOfRangeException("revision");}
        internal static bool Kind(FloatEquipmentKind kind){return kind>=FloatEquipmentKind.Rod&&kind<=FloatEquipmentKind.Sinker;}
        internal static bool Slot(FloatEquipmentSlot slot){return slot>=FloatEquipmentSlot.Rod&&slot<=FloatEquipmentSlot.Sinker;}
        internal static FloatEquipmentKind SlotKind(FloatEquipmentSlot slot)
        {
            if(!Slot(slot))throw new ArgumentOutOfRangeException("slot");
            return slot==FloatEquipmentSlot.Rod?FloatEquipmentKind.Rod:slot==FloatEquipmentSlot.Reel?FloatEquipmentKind.Reel:
                slot==FloatEquipmentSlot.Hook?FloatEquipmentKind.Hook:slot==FloatEquipmentSlot.Bobber?FloatEquipmentKind.Bobber:FloatEquipmentKind.Sinker;
        }
    }
    internal enum FloatEquipmentKind { Rod,Reel,Line,Hook,Bobber,Sinker }
    internal enum FloatEquipmentSlot { Rod,Reel,Hook,Bobber,Sinker }
    internal enum FloatEquipmentPlace { Inventory,RigSlot,ReelSpool,Lost }
    internal enum FloatEquipmentGate { Idle,ActiveSession,PendingResult,PersistenceUnknown }

    internal sealed class FloatEquipmentLocation
    {
        internal readonly FloatEquipmentPlace Place;
        internal readonly Guid OwnerId;
        internal readonly FloatEquipmentSlot? Slot;
        FloatEquipmentLocation(FloatEquipmentPlace place,Guid owner,FloatEquipmentSlot? slot){Place=place;OwnerId=owner;Slot=slot;}
        internal static FloatEquipmentLocation Inventory(){return new FloatEquipmentLocation(FloatEquipmentPlace.Inventory,Guid.Empty,null);}
        internal static FloatEquipmentLocation RigSlot(Guid rig,FloatEquipmentSlot slot)
        {FloatEquipmentGuard.Id(rig,"rig");FloatEquipmentGuard.SlotKind(slot);return new FloatEquipmentLocation(FloatEquipmentPlace.RigSlot,rig,slot);}
        internal static FloatEquipmentLocation ReelSpool(Guid reel)
        {FloatEquipmentGuard.Id(reel,"reel");return new FloatEquipmentLocation(FloatEquipmentPlace.ReelSpool,reel,null);}
        internal static FloatEquipmentLocation Lost(Guid lossEvent)
        {FloatEquipmentGuard.Id(lossEvent,"lossEvent");return new FloatEquipmentLocation(FloatEquipmentPlace.Lost,lossEvent,null);}
    }
    internal sealed class FloatEquipmentInstance
    {
        internal readonly Guid Id;
        internal readonly string DefinitionKey;
        internal readonly FloatEquipmentKind Kind;
        internal readonly long Revision,InitialLineMm,RemainingLineMm;
        internal readonly FloatEquipmentLocation Location;
        internal long LostLineMm {get{return InitialLineMm-RemainingLineMm;}}
        internal FloatEquipmentInstance(Guid id,string definitionKey,FloatEquipmentKind kind,long revision,FloatEquipmentLocation location,long initialLineMm=0,long remainingLineMm=0)
        {
            FloatEquipmentGuard.Id(id,"instance");FloatEquipmentGuard.Revision(revision);
            if(!FloatEquipmentGuard.Kind(kind))throw new ArgumentOutOfRangeException("kind");if(location==null)throw new ArgumentNullException("location");
            if(kind==FloatEquipmentKind.Line){
                if(initialLineMm<1||initialLineMm>FloatEquipmentGuard.MaximumLineMm||remainingLineMm<0||remainingLineMm>initialLineMm)throw new ArgumentOutOfRangeException("line length");
                if((location.Place==FloatEquipmentPlace.Lost)!=(remainingLineMm==0))throw new ArgumentException("A depleted line retains its identity in the lost location.");
                if(location.Place==FloatEquipmentPlace.RigSlot)throw new ArgumentException("Line belongs to a reel spool, not a duplicated rig slot.");
            }else if(initialLineMm!=0||remainingLineMm!=0||location.Place==FloatEquipmentPlace.ReelSpool)throw new ArgumentException("Only line segments carry lengths and occupy a spool.");
            Id=id;DefinitionKey=FloatEquipmentGuard.Key(definitionKey);Kind=kind;Revision=revision;Location=location;InitialLineMm=initialLineMm;RemainingLineMm=remainingLineMm;
        }
        internal FloatEquipmentInstance Move(FloatEquipmentLocation location)
        {if(Location.Place==FloatEquipmentPlace.Lost)throw new InvalidOperationException("A lost identity cannot move again.");return new FloatEquipmentInstance(Id,DefinitionKey,Kind,checked(Revision+1),location,InitialLineMm,RemainingLineMm);}
        internal FloatEquipmentInstance Revise()
        {return new FloatEquipmentInstance(Id,DefinitionKey,Kind,checked(Revision+1),Location,InitialLineMm,RemainingLineMm);}
        internal FloatEquipmentInstance LoseLine(long lossMm,Guid lossEvent)
        {
            if(Kind!=FloatEquipmentKind.Line||lossMm<0||lossMm>RemainingLineMm)throw new ArgumentOutOfRangeException("lossMm");
            long remaining=RemainingLineMm-lossMm;
            return new FloatEquipmentInstance(Id,DefinitionKey,Kind,checked(Revision+1),remaining==0?FloatEquipmentLocation.Lost(lossEvent):Location,InitialLineMm,remaining);
        }
    }
    internal sealed class FloatEquipmentDefinition
    {
        internal readonly string Key;
        internal readonly FloatEquipmentKind Kind;
        internal readonly long MassMg,MinimumLoadMg,MaximumLoadMg;
        internal readonly int LineDiameterUm;
        // Load is casting mass for a rod and sinker mass for a bobber. Other
        // kinds must explicitly use zero; there is no inferred durability.
        internal FloatEquipmentDefinition(string key,FloatEquipmentKind kind,long massMg,int lineDiameterUm=0,long minimumLoadMg=0,long maximumLoadMg=0)
        {
            if(!FloatEquipmentGuard.Kind(kind))throw new ArgumentOutOfRangeException("kind");
            if(massMg<0||massMg>1000000000||(kind!=FloatEquipmentKind.Line&&massMg==0))throw new ArgumentOutOfRangeException("massMg");
            if(kind==FloatEquipmentKind.Line){if(lineDiameterUm<1||lineDiameterUm>10000)throw new ArgumentOutOfRangeException("lineDiameterUm");}
            else if(lineDiameterUm!=0)throw new ArgumentException("Only line has a diameter.");
            if(kind==FloatEquipmentKind.Rod||kind==FloatEquipmentKind.Bobber){if(minimumLoadMg<0||maximumLoadMg<1||minimumLoadMg>maximumLoadMg||maximumLoadMg>1000000000)throw new ArgumentOutOfRangeException("load range");}
            else if(minimumLoadMg!=0||maximumLoadMg!=0)throw new ArgumentException("This kind has no assembly load range.");
            Key=FloatEquipmentGuard.Key(key);Kind=kind;MassMg=massMg;LineDiameterUm=lineDiameterUm;MinimumLoadMg=minimumLoadMg;MaximumLoadMg=maximumLoadMg;
        }
    }
    internal sealed class FloatEquipmentPairRule
    {
        internal readonly string First,Second;
        internal readonly bool Allowed;
        internal FloatEquipmentPairRule(string first,string second,bool allowed)
        {First=FloatEquipmentGuard.Key(first);Second=FloatEquipmentGuard.Key(second);Allowed=allowed;}
    }
    internal sealed class FloatEquipmentSpoolRule
    {
        internal readonly string ReelKey,LineKey;
        internal readonly int DiameterUm;
        internal readonly long MaximumLengthMm;
        internal FloatEquipmentSpoolRule(string reelKey,string lineKey,int diameterUm,long maximumLengthMm)
        {
            if(diameterUm<1||diameterUm>10000||maximumLengthMm<1||maximumLengthMm>FloatEquipmentGuard.MaximumLineMm)throw new ArgumentOutOfRangeException("spool capacity");
            ReelKey=FloatEquipmentGuard.Key(reelKey);LineKey=FloatEquipmentGuard.Key(lineKey);DiameterUm=diameterUm;MaximumLengthMm=maximumLengthMm;
        }
    }
    internal sealed class FloatEquipmentLossRule
    {
        internal readonly string Key;
        internal readonly ReadOnlyCollection<FloatEquipmentSlot> LostSlots;
        internal FloatEquipmentLossRule(string key,IEnumerable<FloatEquipmentSlot> lostSlots)
        {
            if(lostSlots==null)throw new ArgumentNullException("lostSlots");var rows=new List<FloatEquipmentSlot>();var unique=new HashSet<FloatEquipmentSlot>();
            foreach(var slot in lostSlots){if((slot!=FloatEquipmentSlot.Hook&&slot!=FloatEquipmentSlot.Bobber&&slot!=FloatEquipmentSlot.Sinker)||!unique.Add(slot))throw new ArgumentException("Only distinct declared terminal slots may be lost.");rows.Add(slot);}
            if(!unique.Contains(FloatEquipmentSlot.Hook))throw new ArgumentException("A terminal line loss must explicitly disconnect the hook.");
            rows.Sort();Key=FloatEquipmentGuard.Key(key);LostSlots=rows.AsReadOnly();
        }
    }
    internal sealed class FloatEquipmentCatalog
    {
        internal readonly long Version;
        internal readonly string BaitDefinitionKey;
        internal readonly ReadOnlyCollection<FloatEquipmentDefinition> Definitions;
        internal readonly ReadOnlyCollection<FloatEquipmentPairRule> RodReelRules,HookBaitRules;
        internal readonly ReadOnlyCollection<FloatEquipmentSpoolRule> SpoolRules;
        internal readonly ReadOnlyCollection<FloatEquipmentLossRule> LossRules;
        internal FloatEquipmentCatalog(long version,string baitDefinitionKey,IEnumerable<FloatEquipmentDefinition> definitions,
            IEnumerable<FloatEquipmentPairRule> rodReelRules,IEnumerable<FloatEquipmentSpoolRule> spoolRules,IEnumerable<FloatEquipmentPairRule> hookBaitRules,IEnumerable<FloatEquipmentLossRule> lossRules)
        {
            FloatEquipmentGuard.Revision(version);Version=version;BaitDefinitionKey=FloatEquipmentGuard.Key(baitDefinitionKey);
            Definitions=Copy(definitions);RodReelRules=Copy(rodReelRules);SpoolRules=Copy(spoolRules);HookBaitRules=Copy(hookBaitRules);LossRules=Copy(lossRules);
            var keys=new HashSet<string>(StringComparer.Ordinal);foreach(var item in Definitions)if(!keys.Add(item.Key))throw new ArgumentException("Duplicate definition.");
            keys.Clear();foreach(var row in RodReelRules){DefinitionKind(row.First,FloatEquipmentKind.Rod);DefinitionKind(row.Second,FloatEquipmentKind.Reel);if(!keys.Add(row.First+"|"+row.Second))throw new ArgumentException("Duplicate rod/reel rule.");}
            keys.Clear();foreach(var row in HookBaitRules){DefinitionKind(row.First,FloatEquipmentKind.Hook);if(row.Second!=BaitDefinitionKey||!keys.Add(row.First+"|"+row.Second))throw new ArgumentException("Invalid hook/bait rule.");}
            keys.Clear();foreach(var row in SpoolRules){DefinitionKind(row.ReelKey,FloatEquipmentKind.Reel);DefinitionKind(row.LineKey,FloatEquipmentKind.Line);
                if(Find(row.LineKey).LineDiameterUm!=row.DiameterUm||!keys.Add(row.ReelKey+"|"+row.LineKey))throw new ArgumentException("Invalid or duplicate spool rule.");}
            keys.Clear();foreach(var row in LossRules)if(!keys.Add(row.Key))throw new ArgumentException("Duplicate loss rule.");
        }
        static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> input)where T:class
        {
            if(input==null)throw new ArgumentNullException("catalogue collection");var copy=new List<T>();
            foreach(var item in input){if(item==null||copy.Count>=FloatEquipmentGuard.MaximumInstances)throw new ArgumentException("Null or oversized catalogue collection.");copy.Add(item);}return copy.AsReadOnly();
        }
        void DefinitionKind(string key,FloatEquipmentKind kind){var found=Find(key);if(found==null||found.Kind!=kind)throw new ArgumentException("Catalogue rule references a missing or wrong definition kind.");}
        internal FloatEquipmentDefinition Find(string key){foreach(var row in Definitions)if(row.Key==key)return row;return null;}
        internal FloatEquipmentPairRule Pair(ReadOnlyCollection<FloatEquipmentPairRule> rows,string first,string second){foreach(var row in rows)if(row.First==first&&row.Second==second)return row;return null;}
        internal FloatEquipmentSpoolRule Capacity(string reel,string line){foreach(var row in SpoolRules)if(row.ReelKey==reel&&row.LineKey==line)return row;return null;}
        internal FloatEquipmentLossRule Loss(string key){foreach(var row in LossRules)if(row.Key==key)return row;return null;}
    }
    internal sealed class FloatEquipmentIssue
    {
        internal readonly string Code;
        internal readonly FloatEquipmentSlot? Slot;
        internal readonly Guid ItemId,RelatedId;
        internal FloatEquipmentIssue(string code,FloatEquipmentSlot? slot=null,Guid item=default(Guid),Guid related=default(Guid))
        {Code=code;Slot=slot;ItemId=item;RelatedId=related;}
    }
    internal sealed class FloatEquipmentCheck
    {
        internal readonly ReadOnlyCollection<FloatEquipmentIssue> Issues;
        internal bool Valid {get{return Issues.Count==0;}}
        internal FloatEquipmentCheck(IEnumerable<FloatEquipmentIssue> issues)
        {
            var copy=new List<FloatEquipmentIssue>(issues);copy.Sort((a,b)=>{int slot=(a.Slot.HasValue?(int)a.Slot.Value:99).CompareTo(b.Slot.HasValue?(int)b.Slot.Value:99);
                if(slot!=0)return slot;int code=StringComparer.Ordinal.Compare(a.Code,b.Code);return code!=0?code:a.ItemId.CompareTo(b.ItemId);});Issues=copy.AsReadOnly();
        }
    }
    internal sealed class FloatEquipmentChange
    {
        internal readonly bool Accepted;
        internal readonly string Code;
        internal readonly FloatEquipmentLedger Candidate;
        internal readonly FloatEquipmentCheck Check;
        internal readonly ReadOnlyCollection<Guid> LostItemIds;
        internal readonly long LostLineMm;
        internal FloatEquipmentChange(bool accepted,string code,FloatEquipmentLedger state,FloatEquipmentCheck check=null,IEnumerable<Guid> lost=null,long lostLine=0)
        {Accepted=accepted;Code=code;Candidate=state;Check=check??new FloatEquipmentCheck(new FloatEquipmentIssue[0]);LostItemIds=new List<Guid>(lost??new Guid[0]).AsReadOnly();LostLineMm=lostLine;}
    }
    internal sealed class FloatEquipmentSessionLock
    {
        internal readonly Guid SessionId,LineId;
        internal readonly long RigRevision,InventoryRevision,LineRevision;
        internal readonly FloatEquipmentLedger Source;
        internal readonly FloatEquipmentCatalog Catalog;
        internal FloatEquipmentSessionLock(Guid session,FloatEquipmentLedger source,FloatEquipmentInstance line)
        {
            FloatEquipmentGuard.Id(session,"session");if(source==null||line==null)throw new ArgumentNullException("session assembly");
            if(session==source.RigId||source.Find(session)!=null)throw new ArgumentException("Session identity collides with equipment.");
            var reel=source.Equipped(FloatEquipmentSlot.Reel);
            if(reel==null||!Object.ReferenceEquals(source.Find(line.Id),line)||!Object.ReferenceEquals(source.Spool(reel.Id),line))throw new ArgumentException("A session locks the exact equipped reel line.");
            if(!source.EvaluateCastReadiness(FloatEquipmentGate.Idle,1,1,1,1).Valid)throw new InvalidOperationException("Only a complete compatible assembly can be locked.");
            SessionId=session;Source=source;Catalog=source.Catalog;RigRevision=source.RigRevision;InventoryRevision=source.InventoryRevision;LineId=line.Id;LineRevision=line.Revision;
        }
    }
    internal sealed class FloatEquipmentLedger
    {
        internal readonly Guid RigId;
        internal readonly long RigRevision,InventoryRevision,RulesVersion;
        internal readonly FloatEquipmentCatalog Catalog;
        internal readonly ReadOnlyCollection<FloatEquipmentInstance> Instances;
        internal long InitialLineMm {get{long n=0;foreach(var item in Instances)n=checked(n+item.InitialLineMm);return n;}}
        internal long RemainingLineMm {get{long n=0;foreach(var item in Instances)n=checked(n+item.RemainingLineMm);return n;}}
        internal long LostLineMm {get{return InitialLineMm-RemainingLineMm;}}
        internal FloatEquipmentLedger(Guid rigId,long rigRevision,long inventoryRevision,FloatEquipmentCatalog catalog,IEnumerable<FloatEquipmentInstance> instances)
        {
            FloatEquipmentGuard.Id(rigId,"rig");FloatEquipmentGuard.Revision(rigRevision);FloatEquipmentGuard.Revision(inventoryRevision);
            if(catalog==null||instances==null)throw new ArgumentNullException("ledger dependency");
            RigId=rigId;RigRevision=rigRevision;InventoryRevision=inventoryRevision;Catalog=catalog;RulesVersion=catalog.Version;
            var copy=new List<FloatEquipmentInstance>();var ids=new HashSet<Guid>();ids.Add(rigId);
            foreach(var item in instances){if(item==null||copy.Count>=FloatEquipmentGuard.MaximumInstances||!ids.Add(item.Id))throw new ArgumentException("Null, duplicate or oversized instance collection.");copy.Add(item);}
            copy.Sort((a,b)=>a.Id.CompareTo(b.Id));Instances=copy.AsReadOnly();var slots=new HashSet<FloatEquipmentSlot>();var spools=new HashSet<Guid>();
            foreach(var item in Instances){
                var place=item.Location;
                if(place.Place==FloatEquipmentPlace.RigSlot){if(place.OwnerId!=RigId||!place.Slot.HasValue||FloatEquipmentGuard.SlotKind(place.Slot.Value)!=item.Kind||!slots.Add(place.Slot.Value))throw new ArgumentException("Invalid or duplicate rig slot ownership.");}
                else if(place.Place==FloatEquipmentPlace.ReelSpool){var reel=Find(place.OwnerId);if(item.Kind!=FloatEquipmentKind.Line||reel==null||reel.Kind!=FloatEquipmentKind.Reel||reel.Location.Place==FloatEquipmentPlace.Lost||!spools.Add(reel.Id))throw new ArgumentException("Line requires one live reel spool owner.");}
                else if(place.Place==FloatEquipmentPlace.Lost&&ids.Contains(place.OwnerId))throw new ArgumentException("A loss event cannot reuse an equipment identity.");
            }
        }
        internal FloatEquipmentInstance Find(Guid id){foreach(var item in Instances)if(item.Id==id)return item;return null;}
        internal FloatEquipmentInstance Equipped(FloatEquipmentSlot slot)
        {FloatEquipmentGuard.SlotKind(slot);foreach(var item in Instances)if(item.Location.Place==FloatEquipmentPlace.RigSlot&&item.Location.Slot==slot)return item;return null;}
        internal FloatEquipmentInstance Spool(Guid reel){foreach(var item in Instances)if(item.Location.Place==FloatEquipmentPlace.ReelSpool&&item.Location.OwnerId==reel)return item;return null;}
        internal FloatEquipmentCheck ValidateAssembly()
        {
            var issues=new List<FloatEquipmentIssue>();
            foreach(var item in Instances){if(item.Location.Place==FloatEquipmentPlace.Lost)continue;var definition=Catalog.Find(item.DefinitionKey);
                if(definition==null)issues.Add(new FloatEquipmentIssue("configuration-missing",item.Location.Slot,item.Id));
                else if(definition.Kind!=item.Kind)issues.Add(new FloatEquipmentIssue("definition-kind-mismatch",item.Location.Slot,item.Id));}
            var rod=Equipped(FloatEquipmentSlot.Rod);var reel=Equipped(FloatEquipmentSlot.Reel);var hook=Equipped(FloatEquipmentSlot.Hook);var bobber=Equipped(FloatEquipmentSlot.Bobber);var sinker=Equipped(FloatEquipmentSlot.Sinker);
            if(rod!=null&&reel!=null){var rule=Catalog.Pair(Catalog.RodReelRules,rod.DefinitionKey,reel.DefinitionKey);
                if(rule==null||!rule.Allowed)issues.Add(new FloatEquipmentIssue(rule==null?"configuration-missing":"incompatible-components",FloatEquipmentSlot.Reel,reel.Id,rod.Id));}
            foreach(var item in Instances)if(item.Kind==FloatEquipmentKind.Reel&&item.Location.Place!=FloatEquipmentPlace.Lost){var line=Spool(item.Id);if(line==null)continue;
                var rule=Catalog.Capacity(item.DefinitionKey,line.DefinitionKey);if(rule==null)issues.Add(new FloatEquipmentIssue("configuration-missing",FloatEquipmentSlot.Reel,item.Id,line.Id));
                else if(line.RemainingLineMm>rule.MaximumLengthMm)issues.Add(new FloatEquipmentIssue("line-capacity-exceeded",FloatEquipmentSlot.Reel,item.Id,line.Id));}
            if(hook!=null){var rule=Catalog.Pair(Catalog.HookBaitRules,hook.DefinitionKey,Catalog.BaitDefinitionKey);
                if(rule==null||!rule.Allowed)issues.Add(new FloatEquipmentIssue(rule==null?"configuration-missing":"incompatible-bait",FloatEquipmentSlot.Hook,hook.Id));}
            var bobberDefinition=bobber==null?null:Catalog.Find(bobber.DefinitionKey);var sinkerDefinition=sinker==null?null:Catalog.Find(sinker.DefinitionKey);
            if(bobberDefinition!=null&&sinkerDefinition!=null&&bobberDefinition.Kind==FloatEquipmentKind.Bobber&&sinkerDefinition.Kind==FloatEquipmentKind.Sinker&&
                (sinkerDefinition.MassMg<bobberDefinition.MinimumLoadMg||sinkerDefinition.MassMg>bobberDefinition.MaximumLoadMg))issues.Add(new FloatEquipmentIssue("bobber-load-out-of-range",FloatEquipmentSlot.Sinker,sinker.Id,bobber.Id));
            var rodDefinition=rod==null?null:Catalog.Find(rod.DefinitionKey);var hookDefinition=hook==null?null:Catalog.Find(hook.DefinitionKey);
            if(rodDefinition!=null&&rodDefinition.Kind==FloatEquipmentKind.Rod&&hookDefinition!=null&&bobberDefinition!=null&&sinkerDefinition!=null){
                long castMass=checked(hookDefinition.MassMg+bobberDefinition.MassMg+sinkerDefinition.MassMg);
                if(castMass<rodDefinition.MinimumLoadMg||castMass>rodDefinition.MaximumLoadMg)issues.Add(new FloatEquipmentIssue("cast-mass-out-of-range",FloatEquipmentSlot.Rod,rod.Id));}
            return new FloatEquipmentCheck(issues);
        }
        static string GateCode(FloatEquipmentGate gate)
        {
            if(gate==FloatEquipmentGate.Idle)return null;if(gate==FloatEquipmentGate.ActiveSession)return "rig-busy";
            if(gate==FloatEquipmentGate.PendingResult)return "result-pending";if(gate==FloatEquipmentGate.PersistenceUnknown)return "persistence-uncertain";
            throw new ArgumentOutOfRangeException("gate");
        }
        List<FloatEquipmentIssue> RequiredIssues()
        {
            var issues=new List<FloatEquipmentIssue>(ValidateAssembly().Issues);
            foreach(FloatEquipmentSlot slot in Enum.GetValues(typeof(FloatEquipmentSlot)))if(Equipped(slot)==null)issues.Add(new FloatEquipmentIssue("missing-component",slot));
            var reel=Equipped(FloatEquipmentSlot.Reel);if(reel!=null&&Spool(reel.Id)==null)issues.Add(new FloatEquipmentIssue("missing-line",FloatEquipmentSlot.Reel,reel.Id));return issues;
        }
        internal FloatEquipmentCheck EvaluateCastReadiness(FloatEquipmentGate gate,long requiredLineMm,int baitCount,int stamina,int castCost)
        {
            if(requiredLineMm<1||requiredLineMm>FloatEquipmentGuard.MaximumLineMm||baitCount<0||stamina<0||castCost<1)throw new ArgumentOutOfRangeException("readiness resources");
            var issues=RequiredIssues();string blocked=GateCode(gate);if(blocked!=null)issues.Add(new FloatEquipmentIssue(blocked));
            if(baitCount==0)issues.Add(new FloatEquipmentIssue("bait-empty"));if(stamina<castCost)issues.Add(new FloatEquipmentIssue("stamina-insufficient"));
            var reel=Equipped(FloatEquipmentSlot.Reel);var line=reel==null?null:Spool(reel.Id);if(line!=null&&line.RemainingLineMm<requiredLineMm)issues.Add(new FloatEquipmentIssue("insufficient-line",FloatEquipmentSlot.Reel,line.Id));
            return new FloatEquipmentCheck(issues);
        }
        FloatEquipmentChange Reject(string code,FloatEquipmentCheck check=null){return new FloatEquipmentChange(false,code,this,check);}
        FloatEquipmentChange Precheck(FloatEquipmentGate gate,long rigRevision,long inventoryRevision)
        {string code=GateCode(gate);if(code!=null)return Reject(code);if(rigRevision!=RigRevision)return Reject("stale-rig");if(inventoryRevision!=InventoryRevision)return Reject("stale-inventory");return null;}
        FloatEquipmentChange Candidate(List<FloatEquipmentInstance> items,long rigRevision,string code,IEnumerable<Guid> lost=null,long lostLine=0)
        {
            var candidate=new FloatEquipmentLedger(RigId,rigRevision,checked(InventoryRevision+1),Catalog,items);var check=candidate.ValidateAssembly();
            if(!check.Valid)return Reject(check.Issues[0].Code,check);
            if(candidate.InitialLineMm!=InitialLineMm||candidate.RemainingLineMm+lostLine!=RemainingLineMm||candidate.LostLineMm!=LostLineMm+lostLine)throw new InvalidOperationException("Equipment candidate violated line conservation.");
            return new FloatEquipmentChange(true,code,candidate,check,lost,lostLine);
        }
        internal FloatEquipmentChange ReplaceSlot(FloatEquipmentGate gate,long expectedRigRevision,long expectedInventoryRevision,FloatEquipmentSlot slot,Guid? itemId)
        {
            var blocked=Precheck(gate,expectedRigRevision,expectedInventoryRevision);if(blocked!=null)return blocked;
            if(!FloatEquipmentGuard.Slot(slot))return Reject("slot-type-mismatch");if(itemId.HasValue&&itemId.Value==Guid.Empty)return Reject("item-identity-invalid");
            var old=Equipped(slot);if((old==null&&!itemId.HasValue)||(old!=null&&itemId==old.Id))return Reject("assembly-unchanged");
            var next=itemId.HasValue?Find(itemId.Value):null;if(itemId.HasValue&&next==null)return Reject("item-not-found");
            if(next!=null){if(next.Kind!=FloatEquipmentGuard.SlotKind(slot))return Reject("slot-type-mismatch");if(next.Location.Place==FloatEquipmentPlace.Lost)return Reject("item-unavailable");if(next.Location.Place!=FloatEquipmentPlace.Inventory)return Reject("item-in-use");}
            try{var items=new List<FloatEquipmentInstance>();foreach(var item in Instances)items.Add(old!=null&&item.Id==old.Id?item.Move(FloatEquipmentLocation.Inventory()):next!=null&&item.Id==next.Id?item.Move(FloatEquipmentLocation.RigSlot(RigId,slot)):item);
                return Candidate(items,checked(RigRevision+1),"assembly-candidate");}catch(OverflowException){return Reject("revision-overflow");}
        }
        internal FloatEquipmentChange MoveLine(FloatEquipmentGate gate,long expectedRigRevision,long expectedInventoryRevision,Guid lineId,Guid? reelId)
        {
            var blocked=Precheck(gate,expectedRigRevision,expectedInventoryRevision);if(blocked!=null)return blocked;
            var line=Find(lineId);if(line==null)return Reject("item-not-found");if(line.Kind!=FloatEquipmentKind.Line)return Reject("slot-type-mismatch");if(line.Location.Place==FloatEquipmentPlace.Lost)return Reject("item-unavailable");
            if(reelId.HasValue&&reelId.Value==Guid.Empty)return Reject("item-identity-invalid");var next=reelId.HasValue?Find(reelId.Value):null;
            if(reelId.HasValue&&next==null)return Reject("item-not-found");if(next!=null&&(next.Kind!=FloatEquipmentKind.Reel||next.Location.Place==FloatEquipmentPlace.Lost))return Reject("reel-unavailable");
            Guid oldReel=line.Location.Place==FloatEquipmentPlace.ReelSpool?line.Location.OwnerId:Guid.Empty;
            if((next==null&&oldReel==Guid.Empty)||(next!=null&&next.Id==oldReel))return Reject("line-unchanged");
            if(next!=null&&Spool(next.Id)!=null)return Reject("spool-occupied");
            try{var equipped=Equipped(FloatEquipmentSlot.Reel);bool rigChanged=equipped!=null&&(equipped.Id==oldReel||(next!=null&&equipped.Id==next.Id));var items=new List<FloatEquipmentInstance>();
                foreach(var item in Instances)items.Add(item.Id==line.Id?item.Move(next==null?FloatEquipmentLocation.Inventory():FloatEquipmentLocation.ReelSpool(next.Id)):
                    item.Id==oldReel||(next!=null&&item.Id==next.Id)?item.Revise():item);
                return Candidate(items,rigChanged?checked(RigRevision+1):RigRevision,"line-move-candidate");}catch(OverflowException){return Reject("revision-overflow");}
        }
        internal FloatEquipmentSessionLock LockForSession(Guid sessionId)
        {
            FloatEquipmentGuard.Id(sessionId,"session");if(sessionId==RigId||Find(sessionId)!=null)throw new ArgumentException("Session identity collides with equipment.");
            var check=new FloatEquipmentCheck(RequiredIssues());if(!check.Valid)throw new InvalidOperationException("Only a complete compatible assembly can be locked.");
            return new FloatEquipmentSessionLock(sessionId,this,Spool(Equipped(FloatEquipmentSlot.Reel).Id));
        }
        internal FloatEquipmentChange PlanTerminalLoss(FloatEquipmentGate gate,FloatEquipmentSessionLock session,Guid lossEventId,string lossRuleKey,long outLineMm,long cutDistanceFromReelMm)
        {
            GateCode(gate);if(gate!=FloatEquipmentGate.ActiveSession)return Reject(gate==FloatEquipmentGate.Idle?"session-required":GateCode(gate));
            if(session==null||!Object.ReferenceEquals(session.Source,this)||!Object.ReferenceEquals(session.Catalog,Catalog)||session.RigRevision!=RigRevision||session.InventoryRevision!=InventoryRevision)return Reject("session-rig-not-current");
            if(lossEventId==Guid.Empty||lossEventId==RigId||lossEventId==session.SessionId||Find(lossEventId)!=null)return Reject("loss-identity-invalid");
            foreach(var item in Instances)if(item.Location.Place==FloatEquipmentPlace.Lost&&item.Location.OwnerId==lossEventId)return Reject("loss-event-used");
            var rule=Catalog.Loss(lossRuleKey);if(rule==null)return Reject("configuration-missing");var line=Find(session.LineId);
            var equippedReel=Equipped(FloatEquipmentSlot.Reel);
            if(line==null||line.Kind!=FloatEquipmentKind.Line||line.Revision!=session.LineRevision||line.Location.Place!=FloatEquipmentPlace.ReelSpool||equippedReel==null||!Object.ReferenceEquals(Spool(equippedReel.Id),line))return Reject("session-rig-not-current");
            if(outLineMm<1||outLineMm>line.RemainingLineMm||cutDistanceFromReelMm<0||cutDistanceFromReelMm>outLineMm)return Reject("line-loss-out-of-range");
            long loss=outLineMm-cutDistanceFromReelMm;var lostIds=new List<Guid>();
            foreach(var slot in rule.LostSlots){var item=Equipped(slot);if(item==null)return Reject("loss-component-missing");lostIds.Add(item.Id);}
            if(loss==line.RemainingLineMm)lostIds.Add(line.Id);lostIds.Sort();
            try{var items=new List<FloatEquipmentInstance>();foreach(var item in Instances){
                    if(item.Id==line.Id)items.Add(loss>0?item.LoseLine(loss,lossEventId):item);
                    else if(lostIds.Contains(item.Id))items.Add(item.Move(FloatEquipmentLocation.Lost(lossEventId)));
                    else if(item.Id==line.Location.OwnerId&&loss>0)items.Add(item.Revise());else items.Add(item);}
                return Candidate(items,checked(RigRevision+1),"terminal-loss-candidate",lostIds,loss);
            }catch(OverflowException){return Reject("revision-overflow");}
        }
    }
}
