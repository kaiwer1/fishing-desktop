using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FishingDesktop.GameCore
{
    [Flags]
    internal enum FishingPresentation { Float=1, Bottom=2, Spoon=4 }

    internal sealed class FloatFishingSpecies
    {
        internal readonly string Id,Name,AssetId;
        internal readonly int CatalogSize;
        internal readonly long EncounterMinimumMg,EncounterMaximumMg;

        internal FloatFishingSpecies(string id,string name,string assetId,long encounterMinimumMg,long encounterMaximumMg,int catalogSize=7)
        {
            var rule=FloatDomesticSpeciesCatalog.Eleven.Find(id);
            if(rule==null || encounterMinimumMg<rule.MinimumMassMg || encounterMaximumMg>rule.MaximumMassMg || encounterMaximumMg<encounterMinimumMg)
                throw new ArgumentOutOfRangeException("encounterMinimumMg");
            Id=id;Name=name;AssetId=assetId;CatalogSize=catalogSize;
            EncounterMinimumMg=encounterMinimumMg;EncounterMaximumMg=encounterMaximumMg;
        }
    }

    internal sealed class FloatFishingPool
    {
        internal readonly string Id,Name;
        internal readonly FloatBiteEnvironment Environment;
        internal FloatFishingPool(string id,string name,FloatBiteEnvironment environment)
        {Id=id;Name=name;Environment=environment;}
    }

    internal sealed class FloatShoreStation
    {
        internal readonly string Id,Name;
        internal readonly int Xmm;
        internal const int SoundingZmm=19000;
        internal FloatShoreStation(string id,string name,int xMm){Id=id;Name=name;Xmm=xMm;}
    }

    // Relative weights and depth bands are original game settings for one
    // shoreline station, not real population surveys or sale prices.
    internal sealed class FloatFishingWater
    {
        internal sealed class FishWeight
        {
            internal readonly FloatFishingSpecies Species;
            internal readonly int Shallow,Deep;
            internal readonly FishingPresentation Methods;
            internal FishWeight(FloatFishingSpecies species,int shallow,int deep,FishingPresentation methods=FishingPresentation.Float)
            {Species=species;Shallow=shallow;Deep=deep;Methods=methods;}
        }

        internal readonly string Id,Name,Kind,Description,ShallowPoolName,DeepPoolName;
        internal readonly FloatStillWaterGeometry Geometry;
        internal readonly int DeepThresholdMm;
        internal readonly ReadOnlyCollection<FloatShoreStation> ShoreStations;
        readonly ReadOnlyCollection<FishWeight> weights;

        internal FloatFishingWater(string name,string kind,string description,FloatStillWaterGeometry geometry,
            int thresholdMm,string shallowName,string deepName,FloatShoreStation[] stations,params FishWeight[] species)
        {
            Id=geometry.WaterId;Name=name;Kind=kind;Description=description;Geometry=geometry;
            DeepThresholdMm=thresholdMm;ShallowPoolName=shallowName;DeepPoolName=deepName;
            weights=Array.AsReadOnly((FishWeight[])species.Clone());
            ShoreStations=Array.AsReadOnly((FloatShoreStation[])stations.Clone());
        }

        static bool HasEncounterRange(FloatGameRules rules,FloatFishingSpecies species)
        {
            if(rules==null || (rules.Version!=1 && rules.Version!=FloatFishingAtlas.NewRulesVersion))return false;
            if(species.CatalogSize>=9 && rules.Signature!=FloatDomesticSpeciesCatalog.Eleven.Signature &&
                (species.CatalogSize>9 || rules.Signature!=FloatDomesticSpeciesCatalog.Nine.Signature))return false;
            var rule=rules.Find(species.Id);
            return rule!=null && rule.MaximumMassMg>=species.EncounterMinimumMg && rule.MinimumMassMg<=species.EncounterMaximumMg;
        }

        internal ReadOnlyCollection<FloatFishingSpecies> SpeciesFor(FloatGameRules rules)
        {
            var result=new List<FloatFishingSpecies>();
            foreach(var row in weights)if(HasEncounterRange(rules,row.Species))result.Add(row.Species);
            return result.AsReadOnly();
        }

        internal string[] StationFish(FloatGameRules rules,bool deep)
        {
            var result=new List<string>();FishWeight strongest=null;
            foreach(var row in weights){
                if(!HasEncounterRange(rules,row.Species))continue;
                if(strongest==null||(deep?row.Deep>strongest.Deep:row.Shallow>strongest.Shallow))strongest=row;
                if(deep?row.Deep>row.Shallow:row.Shallow>row.Deep)result.Add(row.Species.Name);
            }
            // An older regional catalog may only have a generalist in this water.
            if(result.Count==0&&strongest!=null)result.Add(strongest.Species.Name);
            return result.ToArray();
        }

        // World identity, coordinates, bed and casting depth must agree with
        // the selected immutable station before generating any encounter.
        internal FloatFishingPool CreatePool(FloatWaterSample landing,FloatGameRules rules,FishingPresentation method=FishingPresentation.Float)
        {
            if(landing==null || landing.WaterId!=Id || landing.SpotId!=Geometry.SpotId ||
                landing.GeometryVersion!=Geometry.Version)return null;
            var sample=Geometry.Sample(landing.Xmm,landing.Zmm,landing.GeometryVersion);
            if(!sample.Accepted || sample.Sample.DepthMm!=landing.DepthMm || sample.Sample.SurfaceYmm!=landing.SurfaceYmm ||
                sample.Sample.HookYmm!=landing.HookYmm || sample.Sample.BottomClearanceMm!=landing.BottomClearanceMm)return null;
            bool deep=landing.DepthMm>=DeepThresholdMm;
            var pool=new List<FloatBiteCandidate>();
            foreach(var row in weights){
                var species=row.Species;
                if((row.Methods&method)==0||!HasEncounterRange(rules,species))continue;
                var saved=rules.Find(species.Id);
                pool.Add(new FloatBiteCandidate(species.Id,deep?row.Deep:row.Shallow,
                    Math.Max(saved.MinimumMassMg,species.EncounterMinimumMg),Math.Min(saved.MaximumMassMg,species.EncounterMaximumMg)));
            }
            if(pool.Count==0)return null;
            return new FloatFishingPool(deep?"deep":"shallow",deep?DeepPoolName:ShallowPoolName,
                new FloatBiteEnvironment(.18,pool.ToArray()));
        }
    }

    internal static class FloatFishingAtlas
    {
        internal const long NewRulesVersion=2;
        static readonly FloatFishingSpecies Carp=new FloatFishingSpecies("carp","鲤鱼","fw_carp",300000,1800000);
        static readonly FloatFishingSpecies Crucian=new FloatFishingSpecies("crucian","鲫鱼","fw_crucian",100000,650000);
        static readonly FloatFishingSpecies Grass=new FloatFishingSpecies("grass-carp","草鱼","fw_grass_carp",550000,2400000);
        static readonly FloatFishingSpecies Bream=new FloatFishingSpecies("blunt-snout-bream","鳊鱼","fw_blunt_snout_bream",220000,1050000);
        static readonly FloatFishingSpecies Black=new FloatFishingSpecies("black-seabream","黑鲷","sw_black_seabream",330000,1900000);
        internal static readonly FloatFishingSpecies Bass=new FloatFishingSpecies("spotted-seabass","花鲈","sw_spotted_seabass",480000,2600000);
        static readonly FloatFishingSpecies Yellow=new FloatFishingSpecies("yellowfin-seabream","黄鳍鲷","sw_yellowfin_seabream",180000,1300000);
        static readonly FloatFishingSpecies Catfish=new FloatFishingSpecies("yellow-catfish","黄颡鱼","fw_yellow_catfish",110000,480000,9);
        static readonly FloatFishingSpecies Rockfish=new FloatFishingSpecies("black-rockfish","许氏平鲉","sw_black_rockfish",300000,1350000,9);
        internal static readonly FloatFishingSpecies Culter=new FloatFishingSpecies("topmouth-culter","翘嘴鲌","fw_topmouth_culter",400000,1700000,11);
        internal static readonly FloatFishingSpecies Croaker=new FloatFishingSpecies("large-yellow-croaker","大黄鱼","sw_large_yellow_croaker",320000,1400000,11);
        internal static readonly ReadOnlyCollection<FloatSpeciesRule> SpeciesRules=FloatDomesticSpeciesCatalog.Eleven.Species;
        internal static readonly ReadOnlyCollection<FloatFishingWater> Waters=Array.AsReadOnly(new[]{
            new FloatFishingWater("静水湾","freshwater","近岸芦苇间可寻鲫鱼，塘心深水更常遇到鲤鱼。",
                FloatStillWaterGeometry.CreateDevelopment(true),1800,"芦苇浅滩","塘心深窝",
                new[]{new FloatShoreStation("reed-edge","芦苇口",-6000),new FloatShoreStation("pond-shelf","塘坎",6000)},
                new FloatFishingWater.FishWeight(Carp,3,8,FishingPresentation.Float|FishingPresentation.Bottom),new FloatFishingWater.FishWeight(Crucian,7,2,FishingPresentation.Float|FishingPresentation.Bottom)),
            new FloatFishingWater("江湾","freshwater","浅水草滩聚着草鱼和鳊鱼，回湾深槽里鲤鱼较多。",
                new FloatStillWaterGeometry("river-bend","reed-bank",5,0,1500,0,0,-46000,46000,3000,80000,
                    -13000,13000,12000,41000,new[]{new FloatWaterBedKnot(3000,520),new FloatWaterBedKnot(12000,950),
                        new FloatWaterBedKnot(28000,2750),new FloatWaterBedKnot(80000,4650)},-500),2100,"湾口草滩","回湾深槽",
                new[]{new FloatShoreStation("river-channel","回流口",-6000),new FloatShoreStation("grass-bank","草滩尾",6000)},
                new FloatFishingWater.FishWeight(Grass,5,2),new FloatFishingWater.FishWeight(Bream,4,3),new FloatFishingWater.FishWeight(Carp,1,5),new FloatFishingWater.FishWeight(Catfish,1,3),
                new FloatFishingWater.FishWeight(Culter,5,2,FishingPresentation.Spoon)),
            new FloatFishingWater("海湾","marine","黄鳍鲷常在近岸沙槽活动，外湾礁坡更容易遇到黑鲷和花鲈。",
                new FloatStillWaterGeometry("coastal-bay","rock-bank",6,0,1600,0,0,-48000,48000,3000,85000,
                    -14000,14000,13000,42000,new[]{new FloatWaterBedKnot(3000,750),new FloatWaterBedKnot(13000,1500),
                        new FloatWaterBedKnot(30000,3500),new FloatWaterBedKnot(85000,6400)},620),2700,"近岸沙槽","外湾礁坡",
                new[]{new FloatShoreStation("sand-spit","沙嘴",-6000),new FloatShoreStation("reef-ledge","礁台",6000)},
                new FloatFishingWater.FishWeight(Black,3,5,FishingPresentation.Float|FishingPresentation.Bottom),new FloatFishingWater.FishWeight(Bass,1,4,FishingPresentation.Float|FishingPresentation.Spoon),new FloatFishingWater.FishWeight(Yellow,6,1,FishingPresentation.Float|FishingPresentation.Bottom),new FloatFishingWater.FishWeight(Rockfish,1,4),
                new FloatFishingWater.FishWeight(Croaker,2,8,FishingPresentation.Bottom))
        });

        internal static FloatFishingWater Find(string waterId)
        {
            foreach(var water in Waters)if(water.Id==waterId)return water;
            return null;
        }

        internal static ReadOnlyCollection<FloatFishingWater> Available(FloatGameRules rules)
        {
            var result=new List<FloatFishingWater>();
            foreach(var water in Waters)if(water.SpeciesFor(rules).Count!=0)result.Add(water);
            return result.AsReadOnly();
        }
    }
}
