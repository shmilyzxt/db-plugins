﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Trinity.Items;
using Trinity.Objects;
using Trinity.Reference;
using Trinity.Technicals;
using Zeta.Common;
using Zeta.Game;
using Zeta.Game.Internals.Actors;
using Zeta.TreeSharp;
using Logger = Trinity.Technicals.Logger;

namespace Trinity.Helpers
{
    class DebugUtil
    {
        private static DateTime _lastCacheClear = DateTime.MinValue;
        private static Dictionary<int, CachedBuff> _lastBuffs = new Dictionary<int, CachedBuff>();
        private static Dictionary<string, DateTime> _seenAnimationCache = new Dictionary<string, DateTime>();
        private static Dictionary<int, DateTime> _seenUnknownCache = new Dictionary<int, DateTime>();


        public static void LogAnimation(TrinityCacheObject cacheObject)
        {
            if (!LogCategoryEnabled(LogCategory.Animation) || cacheObject.CommonData == null || !cacheObject.CommonData.IsValid || !cacheObject.CommonData.AnimationInfo.IsValid)
                return;

            var state = cacheObject.CommonData.AnimationState.ToString();
            var name = cacheObject.CommonData.CurrentAnimation.ToString();

            // Log Animation
            if (!_seenAnimationCache.ContainsKey(name))
            {
                Logger.Log(LogCategory.Animation, "{0} State={1} By: {2} ({3})", name, state, cacheObject.InternalName, cacheObject.ActorSNO);
                _seenAnimationCache.Add(name, DateTime.UtcNow);
            }

            CacheMaintenance();
        }

        internal static void LogUnknown(DiaObject diaObject)
        {
            if (!LogCategoryEnabled(LogCategory.UnknownObjects) || !diaObject.IsValid || !diaObject.CommonData.IsValid)
                return;

            // Log Object
            if (!_seenUnknownCache.ContainsKey(diaObject.ActorSNO))
            {
                Logger.Log(LogCategory.UnknownObjects, "{0} ({1}) Type={2}", diaObject.Name, diaObject.ActorSNO, diaObject.ActorType);
                _seenUnknownCache.Add(diaObject.ActorSNO, DateTime.UtcNow);
            }

            CacheMaintenance();
        }

        internal static void LogInFile(string file, string msg, string filename = "")
        {
            FileStream logStream = null;

            string filePath = Path.Combine(FileManager.LoggingPath, file + ".log");
            logStream = File.Open(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);

            //TODO : Change File Log writing
            using (var logWriter = new StreamWriter(logStream))
            {
                logWriter.WriteLine(msg);
            }
        }

        private static void CacheMaintenance()
        {
            var age = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(15));
            if (DateTime.UtcNow.Subtract(_lastCacheClear) > TimeSpan.FromSeconds(15))
            {
                if (_seenAnimationCache.Any())
                    _seenAnimationCache = _seenAnimationCache.Where(p => p.Value < age).ToDictionary(p => p.Key, p => p.Value);

                if (_seenUnknownCache.Any())
                    _seenUnknownCache = _seenUnknownCache.Where(p => p.Value < age).ToDictionary(p => p.Key, p => p.Value);

            }
            _lastCacheClear = DateTime.UtcNow;
        }

        public static bool LogCategoryEnabled(LogCategory category)
        {
            return Trinity.Settings != null && Trinity.Settings.Advanced.LogCategories.HasFlag(category);
        }



        internal static void LogOnPulse()
        {
            LogBuffs();
        }

        public static void LogBuffs()
        {
            if (CacheData.Buffs != null && CacheData.Buffs.ActiveBuffs != null)
            {
                _lastBuffs.ForEach(b =>
                {
                    if (CacheData.Buffs.ActiveBuffs.Any(o => o.Id + o.VariantId == b.Key))
                        return;

                    Logger.Log(LogCategory.ActiveBuffs, "Buff lost '{0}' ({1}) Stacks={2} VariantId={3} VariantName={4}", b.Value.InternalName, b.Value.Id, b.Value.StackCount, b.Value.VariantId, b.Value.VariantName);
                });

                CacheData.Buffs.ActiveBuffs.ForEach(b =>
                {
                    CachedBuff lastBuff;
                    if (_lastBuffs.TryGetValue(b.Id + b.VariantId, out lastBuff))
                    {
                        if (b.StackCount != lastBuff.StackCount)
                        {
                            Logger.Log(LogCategory.ActiveBuffs, "Buff Stack Changed: '{0}' ({1}) Stacks={2}", b.InternalName, b.Id, b.StackCount);
                        }
                    }
                    else
                    {
                        Logger.Log(LogCategory.ActiveBuffs, "Buff Gained '{0}' ({1}) Stacks={2} VariantId={3} VariantName={4}", b.InternalName, b.Id, b.StackCount, b.VariantId, b.VariantName);
                    }
                });

                _lastBuffs = CacheData.Buffs.ActiveBuffs.DistinctBy(k => k.Id + k.VariantId).ToDictionary(k => k.Id + k.VariantId, v => v);
            }            
        }

        public static void LogBuildAndItems(TrinityLogLevel level = TrinityLogLevel.Info)
        {
            try
            {
                using (new MemoryHelper())
                {
                    Action<Item, TrinityLogLevel> logItem = (i, l) =>
                    {
                        Logger.Log(l, LogCategory.UserInformation, string.Format("Item: {0}: {1} ({2}) is Equipped", i.ItemType, i.Name, i.Id));
                    };

                    if (ZetaDia.Me == null || !ZetaDia.Me.IsValid)
                    {
                        Logger.Log("Error: Not in game");
                        return;
                    }

                    if (!ZetaDia.Me.Inventory.Equipped.Any())
                    {
                        Logger.Log("Error: No equipped items detected");
                        return;
                    }

                    LogNewItems();

                    var equippedItems = Legendary.Equipped.Where(c => (!c.IsSetItem || !c.Set.IsEquipped) && !c.IsEquippedInCube).ToList();
                    Logger.Log(level, LogCategory.UserInformation, "------ Equipped Non-Set Legendaries: Items={0}, Sets={1} ------", equippedItems.Count, Sets.Equipped.Count);
                    equippedItems.ForEach(i => logItem(i, level));

                    var cubeItems = Legendary.Equipped.Where(c => c.IsEquippedInCube).ToList();
                    Logger.Log(level, LogCategory.UserInformation, "------ Equipped in Kanai's Cube: Items={0} ------", cubeItems.Count, Sets.Equipped.Count);
                    cubeItems.ForEach(i => logItem(i, level));

                    Sets.Equipped.ForEach(s =>
                    {
                        Logger.Log(level, LogCategory.UserInformation, "------ Set: {0} {1}: {2}/{3} Equipped. ActiveBonuses={4}/{5} ------",
                            s.Name,
                            s.IsClassRestricted ? "(" + s.ClassRestriction + ")" : string.Empty,
                            s.EquippedItems.Count,
                            s.Items.Count,
                            s.CurrentBonuses,
                            s.MaxBonuses);

                        s.Items.Where(i => i.IsEquipped).ForEach(i => logItem(i, level));
                    });

                    Logger.Log(level, LogCategory.UserInformation, "------ Active Skills / Runes ------", SkillUtils.Active.Count, SkillUtils.Active.Count);

                    Action<Skill> logSkill = s =>
                    {
                        Logger.Log(level, LogCategory.UserInformation, "Skill: {0} Rune={1} Type={2}",
                            s.Name,
                            s.CurrentRune.Name,
                            (s.IsAttackSpender) ? "Spender" : (s.IsGeneratorOrPrimary) ? "Generator" : "Other"
                            );
                    };

                    SkillUtils.Active.ForEach(logSkill);

                    Logger.Log(level, LogCategory.UserInformation, "------ Passives ------", SkillUtils.Active.Count, SkillUtils.Active.Count);

                    Action<Passive> logPassive = p => Logger.Log(level, LogCategory.UserInformation, "Passive: {0}", p.Name);
                    
                    PassiveUtils.Active.ForEach(logPassive);

                }
            }
            catch (Exception ex)
            {
                Logger.Log("Exception in DebugUtil > LogBuildAndItems: {0} {1}", ex.Message, ex.InnerException);
            }
        }


        public static void LogSystemInformation(TrinityLogLevel level = TrinityLogLevel.Debug)
        {
            Logger.Log(level, LogCategory.UserInformation, "------ System Information ------");
            Logger.Log(level, LogCategory.UserInformation, "Processor: " + SystemInformation.Processor);
            Logger.Log(level, LogCategory.UserInformation, "Current Speed: " + SystemInformation.ActualProcessorSpeed);
            Logger.Log(level, LogCategory.UserInformation, "Operating System: " + SystemInformation.OperatingSystem);
            Logger.Log(level, LogCategory.UserInformation, "Motherboard: " + SystemInformation.MotherBoard);
            Logger.Log(level, LogCategory.UserInformation, "System Type: " + SystemInformation.SystemType);
            Logger.Log(level, LogCategory.UserInformation, "Free Physical Memory: " + SystemInformation.FreeMemory);
            Logger.Log(level, LogCategory.UserInformation, "Hard Drive: " + SystemInformation.HardDisk);
            Logger.Log(level, LogCategory.UserInformation, "Video Card: " + SystemInformation.VideoCard);
            Logger.Log(level, LogCategory.UserInformation, "Resolution: " + SystemInformation.Resolution);
        }


        internal static void DumpReferenceItems(TrinityLogLevel level = TrinityLogLevel.Debug)
        {

            var path = Path.Combine(FileManager.DemonBuddyPath, "Resources\\JS Class Generator\\ItemReference.js");

            if(File.Exists(path))
                File.Delete(path);

            using (StreamWriter w = File.AppendText(path))
            {
                w.WriteLine("var itemLookup = {");

                foreach (var item in Legendary.ToList())
                {
                    if(item.Id!=0)
                        w.WriteLine(string.Format("     \"{0}\": [\"{1}\", {2}, \"{3}\"],",item.Slug, item.Name, item.Id, item.InternalName));
                }

                w.WriteLine("}");                
            }

            Logger.Log("Dumped Reference Items to: {0}", path);
        }

        internal static void LogInvalidItems(TrinityLogLevel level = TrinityLogLevel.Debug)
        {
            var p = Logger.Prefix;
            Logger.Prefix = "";

            var dropItems = Legendary.ToList().Where(i => !i.IsCrafted && i.Id==0).OrderBy(i => i.TrinityItemType).ToList();
            var craftedItems = Legendary.ToList().Where(i => i.IsCrafted && i.Id==0).OrderBy(i => i.TrinityItemType).ToList();

            Logger.Log("Dropped Items: {0}", dropItems.Count);
            foreach (var item in dropItems)
            {
                    Logger.Log("{0} - {1} = 0", item.TrinityItemType, item.Name);                    
            }

            Logger.Log(" ");
            Logger.Log("Crafted Items: {0}", craftedItems.Count);
            foreach (var item in craftedItems)
            {
                    Logger.Log("{0} - {1} = 0", item.TrinityItemType, item.Name);
            }

            Logger.Prefix = p;
        }

        internal static void LogNewItems()
        {
            var knownIds = Legendary.ItemIds;

            using (new MemoryHelper())
            {
                if (ZetaDia.Me == null || !ZetaDia.Me.IsValid)
                {
                    Logger.Log("Not in game");
                    return;
                }

                var allItems = new List<ACDItem>();
                allItems.AddRange(ZetaDia.Me.Inventory.StashItems);
                allItems.AddRange(ZetaDia.Me.Inventory.Equipped);
                allItems.AddRange(ZetaDia.Me.Inventory.Backpack);

                if (!allItems.Any())
                    return;
       
                var newItems = allItems.Where(i => i != null && i.IsValid && i.ItemQualityLevel == ItemQuality.Legendary && (i.ItemBaseType == ItemBaseType.Jewelry || i.ItemBaseType == ItemBaseType.Armor || i.ItemBaseType == ItemBaseType.Weapon) && !knownIds.Contains(i.ActorSNO)).DistinctBy(p => p.ActorSNO).OrderBy(i => i.ItemType).ToList();

                if (!newItems.Any())
                    return;

                Logger.Log(TrinityLogLevel.Info, LogCategory.UserInformation, "------ New/Unknown Items {0} ------", newItems.Count);

                newItems.ForEach(i =>
                {
                    Logger.Log(string.Format("Item: {0}: {1} ({2})", i.ItemType, i.Name, i.ActorSNO));
                });                
            }        
        }

        internal static void DumpItemSNOReference()
        {
            string[] names = Enum.GetNames(typeof(SNOActor));
            int[] values = (int[])Enum.GetValues(typeof(SNOActor));
            var toLog = new List<string>();
            for( int i = 0; i < names.Length; i++ )
            {
                var sno = values[i];
                var name = names[i];
                var type = TrinityItemManager.DetermineItemType(name, ItemType.Unknown);
                if (type != TrinityItemType.Unknown || DataDictionary.GoldSNO.Contains(sno) ||
                    DataDictionary.ForceToItemOverrideIds.Contains(sno) || DataDictionary.HealthGlobeSNO.Contains(sno) || Legendary.ItemIds.Contains(sno))
                {
                    toLog.Add(string.Format("{{ {0}, TrinityItemType.{1} }}, // {2}", sno, type, name));
                }
            }

            var path = WriteLinesToLog("ItemSNOReference.log", toLog, true);
            Logger.Log("Finished Dumping Item SNO Reference to {0}", path);
        }

        /// <summary>
        /// Writes an ActorMeta record to the log file in the format for a Dictionary collection initializer
        /// </summary>
        public static string WriteLinesToLog(string logFileName, IEnumerable<string> msgs, bool deleteFirst = false)
        {
            var fullFilePath = Path.Combine(FileManager.LoggingPath, logFileName);

            if (deleteFirst && File.Exists(fullFilePath))
            {
                File.Delete(fullFilePath);
            }               

            var logStream = File.Open(fullFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);

            using (var logWriter = new StreamWriter(logStream))
            {
                foreach (var msg in msgs)
                {
                    logWriter.WriteLine(msg);
                }                
            }

            logStream.Close();

            return fullFilePath;
        }

        public static void ItemListTest()
        {
            Logger.Log("Starting ItemList Backpack Test");
           
            var backpackItems = ZetaDia.Me.Inventory.Backpack.ToList();
            var total = backpackItems.Count;
            var stashCount = 0;
         
            foreach (var acdItem in backpackItems)
            {
                Logger.Log("{0} ActorSNO={1} GameBalanceId={2}", acdItem.Name, acdItem.ActorSNO, acdItem.GameBalanceId);
                var cItem = CachedACDItem.GetCachedItem(acdItem);
                if (ItemList.ShouldStashItem(cItem, true))
                    stashCount++;
            }

            Logger.Log("Finished ItemList Backpack Test");

            Logger.Log("Finished - Stash {0} / {1}", stashCount, total);
        }
    }
}
