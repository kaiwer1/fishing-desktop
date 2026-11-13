using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FishingDesktop.GameCore
{
    // A bounded selection of already-lost identities. The host derives the
    // receipt facts from current ownership; callers cannot supply a refund,
    // archived line quantity, loss owner or replacement item.
    internal sealed class ArchiveLostFloatEquipment : FloatCommand
    {
        internal readonly ReadOnlyCollection<Guid> ItemIds;
        internal readonly long ExpectedInventoryRevision,ExpectedEquipmentVersion;
        internal ArchiveLostFloatEquipment(Guid profile,Guid branch,Guid request,Guid businessEvent,IEnumerable<Guid> itemIds,
            long expectedSequence,long expectedRigRevision,long expectedRulesVersion,long expectedInventoryRevision,long expectedEquipmentVersion)
            :base("archive-lost-equipment",profile,branch,request,businessEvent,Guid.Empty,expectedSequence,expectedRigRevision,expectedRulesVersion)
        {
            FloatEquipmentGuard.Revision(expectedInventoryRevision);FloatEquipmentGuard.Revision(expectedEquipmentVersion);
            ItemIds=FloatEquipmentArchiveSummary.Identifiers(itemIds,"archive items",false);
            ExpectedInventoryRevision=expectedInventoryRevision;ExpectedEquipmentVersion=expectedEquipmentVersion;
        }
        internal override string Detail
        {get{var ids=new List<string>();foreach(var id in ItemIds)ids.Add(id.ToString("N"));return FloatGuard.Number(ExpectedInventoryRevision)+"|"+FloatGuard.Number(ExpectedEquipmentVersion)+"|"+String.Join(",",ids.ToArray());}}
    }

    // This summary is derived during reduction and rebuilt from the journal.
    // It is never a stand-alone genesis field or a repeated full archive blob.
    public sealed class FloatEquipmentArchiveSummary
    {
        public readonly ReadOnlyCollection<Guid> ItemIds,LossOwnerIds;
        public readonly long LineMm;
        internal FloatEquipmentArchiveSummary(IEnumerable<Guid> itemIds,IEnumerable<Guid> lossOwnerIds,long lineMm)
        {
            ItemIds=Identifiers(itemIds,"archive items",false);LossOwnerIds=Identifiers(lossOwnerIds,"archive loss owners",false);
            if(LossOwnerIds.Count>ItemIds.Count||lineMm<0||lineMm>checked((long)ItemIds.Count*FloatEquipmentGuard.MaximumLineMm))
                throw new ArgumentOutOfRangeException("archive summary");
            var itemSet=new HashSet<Guid>(ItemIds);foreach(var id in LossOwnerIds)if(itemSet.Contains(id))throw new ArgumentException("An archived loss owner cannot be an archived item.");
            LineMm=lineMm;
        }
        internal static ReadOnlyCollection<Guid> Identifiers(IEnumerable<Guid> values,string name,bool allowEmpty)
        {
            if(values==null)throw new ArgumentNullException(name);var copy=new List<Guid>();var unique=new HashSet<Guid>();
            foreach(var id in values){FloatGuard.Id(id,name);if(copy.Count>=FloatEquipmentGuard.MaximumInstances||!unique.Add(id))throw new ArgumentException("Duplicate or excessive "+name+".");copy.Add(id);}
            if(!allowEmpty&&copy.Count==0)throw new ArgumentException("Empty "+name+".");copy.Sort();return copy.AsReadOnly();
        }
    }
}
