﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Trinity.Framework.Grid;
using Trinity.Reference;
using Zeta.Bot;
using Zeta.Common;
using Zeta.Common.Helpers;
using Zeta.Game;
using Zeta.Game.Internals.Actors;
using Logger = Trinity.Technicals.Logger;

namespace Trinity.Combat.Abilities
{
    public class WitchDoctorCombat : CombatBase
    {
        private static WaitTimer _bastianGeneratorWaitTimer = new WaitTimer(TimeSpan.FromSeconds(3));

        static WitchDoctorCombat()
        {
            _bastianGeneratorWaitTimer.Reset();
        }

        private static readonly HashSet<SNOPower> HarvesterDebuffs = new HashSet<SNOPower>
        {
            SNOPower.Witchdoctor_Haunt,
            SNOPower.Witchdoctor_Locust_Swarm,
            SNOPower.Witchdoctor_Piranhas,
            SNOPower.Witchdoctor_AcidCloud
        };

        private static readonly HashSet<SNOPower> HarvesterCoreDebuffs = new HashSet<SNOPower>
        {
            SNOPower.Witchdoctor_Haunt,
            SNOPower.Witchdoctor_Locust_Swarm,
        };

        public static Stopwatch VisionQuestRefreshTimer = new Stopwatch();
        public static long GetTimeSinceLastVisionQuestRefresh()
        {
            if (!VisionQuestRefreshTimer.IsRunning)
                VisionQuestRefreshTimer.Start();

            return VisionQuestRefreshTimer.ElapsedMilliseconds;
        }

        public static TrinityPower GetPower()
        {
            TrinityPower power = null;

            // Spirit Walk, always!
            if (CanCast(SNOPower.Witchdoctor_SpiritWalk))
            {
                return new TrinityPower(SNOPower.Witchdoctor_SpiritWalk);
            }

            var hasAngryChicken = (Skills.WitchDoctor.Hex.IsActive && Runes.WitchDoctor.AngryChicken.IsActive) || CacheData.Hotbar.ActivePowers.Contains(SNOPower.Witchdoctor_Hex_ChickenWalk);

            if (hasAngryChicken && Sets.ManajumasWay.IsEquipped && CanCast(SNOPower.Witchdoctor_Hex))
            {
                return new TrinityPower(SNOPower.Witchdoctor_Hex);
            }

            // Combat Avoidance Spells
            if (!UseOOCBuff && IsCurrentlyAvoiding)
            {
                // Spirit Walk out of AoE
                if (CanCast(SNOPower.Witchdoctor_SpiritWalk))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SpiritWalk);
                }

                // Soul harvest at current location while avoiding
                if (Sets.RaimentOfTheJadeHarvester.IsMaxBonusActive && MinimumSoulHarvestCriteria(Enemies.BestCluster))
                {
                    Skills.WitchDoctor.SoulHarvest.Cast();
                }
            }

            // Incapacitated or Rooted
            if (!UseOOCBuff && (Player.IsIncapacitated || Player.IsRooted))
            {
                // Spirit Walk
                if (CanCast(SNOPower.Witchdoctor_SpiritWalk))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SpiritWalk);
                }
            }

            // Combat Spells with a Target
            if (!UseOOCBuff && !IsCurrentlyAvoiding && CurrentTarget != null)
            {

                if (_bastianGeneratorWaitTimer.IsFinished && ShouldRefreshBastiansGeneratorBuff)
                {
                    if (Hotbar.Contains(SNOPower.Witchdoctor_CorpseSpider))
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_CorpseSpider, 50f, CurrentTarget.ACDGuid);
                    }
                    if (Hotbar.Contains(SNOPower.Witchdoctor_PoisonDart))
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_PoisonDart, 50f, CurrentTarget.ACDGuid);
                    }
                    if (Hotbar.Contains(SNOPower.Witchdoctor_PlagueOfToads))
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_PlagueOfToads, 50f, CurrentTarget.ACDGuid);
                    }
                    if (Hotbar.Contains(SNOPower.Witchdoctor_Firebomb))
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_Firebomb, 50f, CurrentTarget.ACDGuid);
                    }
                    _bastianGeneratorWaitTimer.Reset();
                }


                bool hasGraveInjustice = CacheData.Hotbar.PassiveSkills.Contains(SNOPower.Witchdoctor_Passive_GraveInjustice);



                //                Debug.Print(CacheData.Hotbar.GetSkill(SNOPower.Witchdoctor_Hex).RuneIndex.ToString());
                var isChicken = CacheData.Hotbar.ActivePowers.Contains(SNOPower.Witchdoctor_Hex_ChickenWalk);

                bool hasVisionQuest = CacheData.Hotbar.PassiveSkills.Any(s => s == SNOPower.Witchdoctor_Passive_VisionQuest);

                // Set max ranged attack range, based on Grave Injustice, and current target NOT standing in avoidance, and health > 25%
                float rangedAttackMaxRange = 30f;
                if (hasGraveInjustice && !CurrentTarget.IsStandingInAvoidance && Player.CurrentHealthPct > 0.25)
                    rangedAttackMaxRange = Math.Min(Player.GoldPickupRadius + 8f, 30f);

                // Set basic attack range, depending on whether or not we have Bears and whether or not we are a tik tank
                float basicAttackRange = 35f;
                if (hasGraveInjustice)
                    basicAttackRange = rangedAttackMaxRange;
                else if (Hotbar.Contains(SNOPower.Witchdoctor_ZombieCharger) && Player.PrimaryResource >= 150)
                    basicAttackRange = 30f;
                else if (Legendary.TiklandianVisage.IsEquipped && !TikHorrifyCriteria(Enemies.BestLargeCluster))
                    basicAttackRange = 25f;
                else if (Legendary.TiklandianVisage.IsEquipped)
                    basicAttackRange = 1f;




                // Summon Pets  -----------------------------------------------------------------------

                // Hex with angry chicken, is chicken, explode!
                if (isChicken && (TargetUtil.AnyMobsInRange(12f, 1, false) || CurrentTarget.RadiusDistance <= 10f || UseDestructiblePower)) // && CanCast(SNOPower.Witchdoctor_Hex_Explode)
                {
                    //Debug.Print("Attempting to cast HEx Explosion {0}", ZetaDia.Me.UsePower(SNOPower.Witchdoctor_Hex_Explode, ZetaDia.Me.Position,
                    //    ZetaDia.CurrentWorldDynamicId));

                    return new TrinityPower(SNOPower.Witchdoctor_Hex_Explode);
                }

                bool hasJaunt = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_SpiritWalk && s.RuneIndex == 1);
                bool hasHonoredGuest = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_SpiritWalk && s.RuneIndex == 3);
                bool hasUmbralShock = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_SpiritWalk && s.RuneIndex == 2);
                bool hasSeverance = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_SpiritWalk && s.RuneIndex == 0);
                bool hasHealingJourney = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_SpiritWalk && s.RuneIndex == 4);

                // Spirit Walk for Goblins chasing
                if (CanCast(SNOPower.Witchdoctor_SpiritWalk) &&
                    CurrentTarget.IsTreasureGoblin && CurrentTarget.HitPointsPct < 0.90 && CurrentTarget.RadiusDistance <= 40f)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SpiritWalk);
                }

                // Spirit Walk < 65% Health: Healing Journey
                if (CanCast(SNOPower.Witchdoctor_SpiritWalk) && hasHealingJourney &&
                    Player.CurrentHealthPct <= V.F("WitchDoctor.SpiritWalk.HealingJourneyHealth"))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SpiritWalk);
                }

                // Spirit Walk < 50% Mana: Honored Guest
                if (CanCast(SNOPower.Witchdoctor_SpiritWalk) && hasHonoredGuest &&
                    Player.PrimaryResourcePct <= V.F("WitchDoctor.SpiritWalk.HonoredGuestMana"))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SpiritWalk);
                }

                //bool shouldRefreshVisionQuest = GetTimeSinceLastVisionQuestRefresh() > 4000;
                bool shouldRefreshVisionQuest = !GetHasBuff(SNOPower.Witchdoctor_Passive_VisionQuest) || GetTimeSinceLastVisionQuestRefresh() > 3800;

                // Vision Quest Passive
                if (hasVisionQuest && shouldRefreshVisionQuest)
                {
                    // Poison Darts 
                    if (CanCast(SNOPower.Witchdoctor_PoisonDart))
                    {
                        VisionQuestRefreshTimer.Restart();
                        return new TrinityPower(SNOPower.Witchdoctor_PoisonDart, basicAttackRange, CurrentTarget.ACDGuid);
                    }
                    // Corpse Spiders
                    if (CanCast(SNOPower.Witchdoctor_CorpseSpider))
                    {
                        VisionQuestRefreshTimer.Restart();
                        return new TrinityPower(SNOPower.Witchdoctor_CorpseSpider, basicAttackRange, CurrentTarget.ACDGuid);
                    }
                    // Plague Of Toads 
                    if (CanCast(SNOPower.Witchdoctor_PlagueOfToads))
                    {
                        VisionQuestRefreshTimer.Restart();
                        return new TrinityPower(SNOPower.Witchdoctor_PlagueOfToads, basicAttackRange, CurrentTarget.ACDGuid);
                    }
                    // Fire Bomb 
                    if (CanCast(SNOPower.Witchdoctor_Firebomb))
                    {
                        VisionQuestRefreshTimer.Restart();
                        return new TrinityPower(SNOPower.Witchdoctor_Firebomb, basicAttackRange, CurrentTarget.ACDGuid); ;
                    }
                }

                bool hasVengefulSpirit = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_SoulHarvest && s.RuneIndex == 4);
                bool hasSwallowYourSoul = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_SoulHarvest && s.RuneIndex == 3);

                // START Jade Harvester -----------------------------------------------------------------------

                if (Sets.RaimentOfTheJadeHarvester.IsMaxBonusActive)
                {
                    //LogTargetArea("BestLargeCluster", Enemies.BestLargeCluster);
                    //LogTargetArea("BestCluster", Enemies.BestCluster);
                    //LogTargetArea("Nearby", Enemies.Nearby);
                    //LogTargetArea("CloseNearby", Enemies.CloseNearby);

                    // Piranhas
                    if (CanCast(SNOPower.Witchdoctor_Piranhas) && Player.PrimaryResource >= 250 &&
                        (TargetUtil.ClusterExists(15f, 45f) || TargetUtil.AnyElitesInRange(45f)) &&
                        LastPowerUsed != SNOPower.Witchdoctor_Piranhas &&
                        Player.PrimaryResource >= 250)
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_Piranhas, 25f, Enemies.BestCluster.Position);
                    }

                    // Should we move to cluster for harvest
                    if (IdealSoulHarvestCriteria(Enemies.BestLargeCluster))
                    {
                        //LogTargetArea("--- Found a good harvest location...", Enemies.BestLargeCluster);
                        MoveToSoulHarvestPoint(Enemies.BestLargeCluster);
                    }

                    // Is there a slightly better position than right here
                    if (MinimumSoulHarvestCriteria(Enemies.BestCluster) && (Enemies.BestCluster.EliteCount >= 2 || Enemies.BestCluster.UnitCount > 4))
                    {
                        //LogTargetArea("--- Found an average harvest location...", Enemies.BestCluster);
                        MoveToSoulHarvestPoint(Enemies.BestCluster);
                    }

                    // Should we harvest right here?
                    if (MinimumSoulHarvestCriteria(Enemies.CloseNearby))
                    {
                        //LogTargetArea("--- Harvesting (CurrentPosition)", Enemies.CloseNearby);
                        return new TrinityPower(SNOPower.Witchdoctor_SoulHarvest);
                    }

                    // Locust Swarm
                    if (CanCast(SNOPower.Witchdoctor_Locust_Swarm) && Player.PrimaryResource >= 300 &&
                        !CurrentTarget.HasDebuff(SNOPower.Witchdoctor_Locust_Swarm))
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_Locust_Swarm, 20f, CurrentTarget.ACDGuid);
                    }

                    // Haunt 
                    if (Skills.WitchDoctor.Haunt.CanCast() && Player.PrimaryResource >= 50 &&
                        !CurrentTarget.HasDebuff(SNOPower.Witchdoctor_Haunt))
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_Haunt, 45f, CurrentTarget.ACDGuid);
                    }

                    // Acid Cloud
                    if (Skills.WitchDoctor.AcidCloud.CanCast() && Player.PrimaryResource >= 325 &&
                        LastPowerUsed != SNOPower.Witchdoctor_AcidCloud)
                    {
                        Vector3 bestClusterPoint;
                        if (Passives.WitchDoctor.GraveInjustice.IsActive)
                            bestClusterPoint = TargetUtil.GetBestClusterPoint(15f, Math.Min(Player.GoldPickupRadius + 8f, 30f));
                        else
                            bestClusterPoint = TargetUtil.GetBestClusterPoint(15f, 30f);

                        return new TrinityPower(SNOPower.Witchdoctor_AcidCloud, rangedAttackMaxRange, bestClusterPoint);
                    }

                    // Spread the love around
                    if (!CurrentTarget.IsTreasureGoblin && CurrentTarget.HasDebuff(SNOPower.Witchdoctor_Locust_Swarm) &&
                        CurrentTarget.HasDebuff(SNOPower.Witchdoctor_Haunt) && Enemies.Nearby.UnitCount > 3 &&
                        Enemies.Nearby.DebuffedPercent(HarvesterCoreDebuffs) < 0.5)
                    {
                        //var oldTarget = Trinity.CurrentTarget;
                        Trinity.Blacklist3Seconds.Add(CurrentTarget.RActorGuid);
                        Trinity.CurrentTarget = Enemies.BestCluster.GetTargetWithoutDebuffs(HarvesterCoreDebuffs);
                        //Logger.LogNormal("{0} {1} is fully debuffed, switched to {2} {3}", oldTarget.InternalName, oldTarget.ACDGuid, CurrentTarget.InternalName, CurrentTarget.ACDGuid);
                    }

                    // Save mana for locust swarm || piranhas
                    if (!CurrentTarget.HasDebuff(SNOPower.Witchdoctor_Locust_Swarm) && Player.PrimaryResource < 300)
                    {
                        //Logger.LogNormal("Saving mana");
                        return DefaultPower;
                    }

                }

                // END Jade Harvester -----------------------------------------------------------------------

                // Tiklandian Visage ----------------------------------------------------------------------
                // Constantly casts Horrify and moves the middle of clusters

                if (Legendary.TiklandianVisage.IsEquipped)
                {
                    // Piranhas
                    if (CanCast(SNOPower.Witchdoctor_Piranhas) && Player.PrimaryResource >= 250 &&
                        (TargetUtil.ClusterExists(15f, 45f) || TargetUtil.AnyElitesInRange(45f)) &&
                        LastPowerUsed != SNOPower.Witchdoctor_Piranhas &&
                        Player.PrimaryResource >= 250)
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_Piranhas, 25f, Enemies.BestCluster.Position);
                    }

                    //Cast Horrify before we go into the fray
                    if (CanCast(SNOPower.Witchdoctor_Horrify))
                        return new TrinityPower(SNOPower.Witchdoctor_Horrify);

                    // Should we move to a better position to fear people
                    if (TikHorrifyCriteria(Enemies.BestLargeCluster))
                        MoveToHorrifyPoint(Enemies.BestLargeCluster);


                }

                // END Tiklandian Visage ----------------------------------------------------------------------   

                // Sacrifice
                if (CanCast(SNOPower.Witchdoctor_Sacrifice) && Trinity.PlayerOwnedZombieDogCount > 0 &&
                    (TargetUtil.AnyElitesInRange(15, 1) || (CurrentTarget.IsBossOrEliteRareUnique && CurrentTarget.RadiusDistance <= 9f)))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Sacrifice);
                }

                // Sacrifice for Circle of Life
                bool hasCircleofLife = CacheData.Hotbar.PassiveSkills.Any(s => s == SNOPower.Witchdoctor_Passive_CircleOfLife);
                if (CanCast(SNOPower.Witchdoctor_Sacrifice) && Trinity.PlayerOwnedZombieDogCount > 0 && hasCircleofLife && TargetUtil.AnyMobsInRange(15f))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Sacrifice);
                }

                // Wall of Zombies
                if (CanCast(SNOPower.Witchdoctor_WallOfZombies) &&
                    (TargetUtil.AnyElitesInRange(15, 1) || TargetUtil.AnyMobsInRange(15, 1) ||
                    ((CurrentTarget.IsEliteRareUnique || CurrentTarget.IsTreasureGoblin || CurrentTarget.IsBoss) && CurrentTarget.RadiusDistance <= 25f)))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_WallOfZombies, 25f, CurrentTarget.Position);
                }

                bool hasRestlessGiant = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Gargantuan && s.RuneIndex == 0);
                bool hasWrathfulProtector = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Gargantuan && s.RuneIndex == 3);

                if (CanCast(SNOPower.Witchdoctor_Gargantuan))
                {
                    // Gargantuan, Recast on Elites or Bosses to trigger Restless Giant
                    if (hasRestlessGiant && (TargetUtil.IsEliteTargetInRange(30f) || Trinity.PlayerOwnedGargantuanCount == 0))
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_Gargantuan);
                    }

                    // Gargantuan Wrathful Protector, 15 seconds of smash, use sparingly!
                    if (hasWrathfulProtector && TargetUtil.IsEliteTargetInRange(30f))
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_Gargantuan);
                    }

                    // Gargantuan regular
                    if (!hasRestlessGiant && !hasWrathfulProtector && Trinity.PlayerOwnedGargantuanCount == 0)
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_Gargantuan);
                    }
                }

                bool hasSacrifice = Hotbar.Contains(SNOPower.Witchdoctor_Sacrifice);

                // Zombie Dogs for Sacrifice
                if (hasSacrifice && CanCast(SNOPower.Witchdoctor_SummonZombieDog) &&
                    (LastPowerUsed == SNOPower.Witchdoctor_Sacrifice || Trinity.PlayerOwnedZombieDogCount <= 2) &&
                    LastPowerUsed != SNOPower.Witchdoctor_SummonZombieDog)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SummonZombieDog);
                }

                // Hex with angry chicken, check if we want to shape shift and explode
                if (CanCast(SNOPower.Witchdoctor_Hex) && (TargetUtil.AnyMobsInRange(12f, 1, false) || CurrentTarget.RadiusDistance <= 10f) &&
                    hasAngryChicken)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Hex);
                }

                // Hex Spam Cast without angry chicken
                if (CanCast(SNOPower.Witchdoctor_Hex) && !hasAngryChicken &&
                   (TargetUtil.AnyElitesInRange(12) || TargetUtil.AnyMobsInRange(12, 2) || TargetUtil.IsEliteTargetInRange(18f)))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Hex);
                }

                if (CanCast(SNOPower.Witchdoctor_SoulHarvest) && (TargetUtil.AnyElitesInRange(16) || TargetUtil.AnyMobsInRange(16, 2) || TargetUtil.IsEliteTargetInRange(16f)))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SoulHarvest);
                }

                // Mass Confuse, elites only or big mobs or to escape on low health
                if (CanCast(SNOPower.Witchdoctor_MassConfusion) &&
                    (TargetUtil.AnyElitesInRange(12, 1) || TargetUtil.AnyMobsInRange(12, 6) || Player.CurrentHealthPct <= 0.25 || (CurrentTarget.IsBossOrEliteRareUnique && CurrentTarget.RadiusDistance <= 12f)) &&
                    !CurrentTarget.IsTreasureGoblin)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_MassConfusion, 0f, CurrentTarget.ACDGuid);
                }

                if (!Settings.Combat.WitchDoctor.UseBigBadVoodooOffCooldown)
                {
                    // Big Bad Voodoo, elites and bosses only
                    if (CanCast(SNOPower.Witchdoctor_BigBadVoodoo) &&
                        (TargetUtil.EliteOrTrashInRange(25f) || (CurrentTarget.IsBoss && CurrentTarget.Distance <= 30f)))
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_BigBadVoodoo);
                    }
                }
                else
                {
                    // Big Bad Voodo, cast whenever available
                    if (!UseOOCBuff && !Player.IsIncapacitated && CanCast(SNOPower.Witchdoctor_BigBadVoodoo))
                    {
                        return new TrinityPower(SNOPower.Witchdoctor_BigBadVoodoo);
                    }
                }
                // Grasp of the Dead
                if (CanCast(SNOPower.Witchdoctor_GraspOfTheDead) &&
                    (TargetUtil.AnyMobsInRange(30, 2) || TargetUtil.EliteOrTrashInRange(30f)) &&
                    Player.PrimaryResource >= 150)
                {
                    var bestClusterPoint = TargetUtil.GetBestClusterPoint();

                    return new TrinityPower(SNOPower.Witchdoctor_GraspOfTheDead, 25f, bestClusterPoint);
                }

                // Piranhas
                if (CanCast(SNOPower.Witchdoctor_Piranhas) && Player.PrimaryResource >= 250 &&
                    (TargetUtil.ClusterExists(15f, 45f) || TargetUtil.AnyElitesInRange(45f)) &&
                    Player.PrimaryResource >= 250)
                {
                    var bestClusterPoint = TargetUtil.GetBestClusterPoint();

                    return new TrinityPower(SNOPower.Witchdoctor_Piranhas, 25f, bestClusterPoint);
                }

                bool hasPhobia = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Horrify && s.RuneIndex == 2);
                bool hasStalker = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Horrify && s.RuneIndex == 4);
                bool hasFaceOfDeath = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Horrify && s.RuneIndex == 1);
                bool hasFrighteningAspect = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Horrify && s.RuneIndex == 0);
                bool hasRuthlessTerror = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Horrify && s.RuneIndex == 3);

                float horrifyRadius = hasFaceOfDeath ? 24f : 12f;

                // Horrify when low on health
                if (CanCast(SNOPower.Witchdoctor_Horrify) && Player.CurrentHealthPct <= EmergencyHealthPotionLimit && TargetUtil.AnyMobsInRange(horrifyRadius, 3))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Horrify);
                }

                // Horrify Buff at 35% health -- Freightening Aspect
                if (CanCast(SNOPower.Witchdoctor_Horrify) && Player.CurrentHealthPct <= 0.35 && hasFrighteningAspect)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Horrify);
                }

                // Spam Horrify
                if (CanCast(SNOPower.Witchdoctor_Horrify) && Settings.Combat.WitchDoctor.SpamHorrify)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Horrify);
                }

                // Fetish Army, elites only
                if (CanCast(SNOPower.Witchdoctor_FetishArmy) &&
                    (TargetUtil.EliteOrTrashInRange(30f) || TargetUtil.IsEliteTargetInRange(30f) || Settings.Combat.WitchDoctor.UseFetishArmyOffCooldown))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_FetishArmy);
                }

                bool hasManitou = Runes.WitchDoctor.Manitou.IsActive;

                // Spirit Barrage Manitou
                if (CanCast(SNOPower.Witchdoctor_SpiritBarrage) && Player.PrimaryResource >= 100 &&
                    TimeSincePowerUse(SNOPower.Witchdoctor_SpiritBarrage) > 18000 && hasManitou)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SpiritBarrage);
                }

                bool hasResentfulSpirit = Runes.WitchDoctor.ResentfulSpirits.IsActive;
                // Haunt 
                if (CanCast(SNOPower.Witchdoctor_Haunt) &&
                    Player.PrimaryResource >= 50 &&
                    !SpellTracker.IsUnitTracked(CurrentTarget, SNOPower.Witchdoctor_Haunt) &&
                    LastPowerUsed != SNOPower.Witchdoctor_Haunt)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Haunt, 21f, CurrentTarget.ACDGuid);
                }

                //skillDict.Add("LocustSwarm", SNOPower.Witchdoctor_Locust_Swarm);

                // Locust Swarm
                if (CanCast(SNOPower.Witchdoctor_Locust_Swarm) && Player.PrimaryResource >= 300 &&
                    !SpellTracker.IsUnitTracked(CurrentTarget, SNOPower.Witchdoctor_Locust_Swarm) && LastPowerUsed != SNOPower.Witchdoctor_Locust_Swarm)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Locust_Swarm, 12f, CurrentTarget.ACDGuid);
                }

                // Sacrifice for 0 Dogs
                if (CanCast(SNOPower.Witchdoctor_Sacrifice) &&
                    (Settings.Combat.WitchDoctor.ZeroDogs || !WitchDoctorHasPrimaryAttack))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Sacrifice, 9f);
                }

                var zombieChargerRange = hasGraveInjustice ? Math.Min(Player.GoldPickupRadius + 8f, 11f) : 11f;

                // Zombie Charger aka Zombie bears Spams Bears @ Everything from 11feet away
                if (CanCast(SNOPower.Witchdoctor_ZombieCharger) && Player.PrimaryResource >= 150)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_ZombieCharger, zombieChargerRange, CurrentTarget.Position);
                }

                // Soul Harvest Any Elites or to increase buff stacks
                if (!Sets.RaimentOfTheJadeHarvester.IsMaxBonusActive && CanCast(SNOPower.Witchdoctor_SoulHarvest) &&
                    (TargetUtil.AnyMobsInRange(16f, GetBuffStacks(SNOPower.Witchdoctor_SoulHarvest) + 1, false) || (hasSwallowYourSoul && Player.PrimaryResourcePct <= 0.50) || TargetUtil.IsEliteTargetInRange(16f)))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SoulHarvest);
                }

                // Soul Harvest with VengefulSpirit
                if (!Sets.RaimentOfTheJadeHarvester.IsMaxBonusActive && CanCast(SNOPower.Witchdoctor_SoulHarvest) && hasVengefulSpirit &&
                    TargetUtil.AnyMobsInRange(16, 3))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SoulHarvest);
                }

                bool hasDireBats = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Firebats && s.RuneIndex == 0);
                bool hasVampireBats = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Firebats && s.RuneIndex == 3);
                bool hasPlagueBats = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Firebats && s.RuneIndex == 2);
                bool hasHungryBats = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Firebats && s.RuneIndex == 1);
                bool hasCloudOfBats = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Firebats && s.RuneIndex == 4);

                int fireBatsChannelCost = hasVampireBats ? 0 : 75;
                int fireBatsMana = TimeSincePowerUse(SNOPower.Witchdoctor_Firebats) < 125 ? fireBatsChannelCost : 225;

                bool firebatsMaintain =
                  Trinity.ObjectCache.Any(u => u.IsUnit &&
                      u.IsPlayerFacing(70f) && u.Weight > 0 &&
                      u.Distance <= V.F("WitchDoctor.Firebats.MaintainRange") &&
                      SpellHistory.TimeSinceUse(SNOPower.Witchdoctor_Firebats) <= TimeSpan.FromMilliseconds(250d));

                // Fire Bats:Cloud of bats 
                if (hasCloudOfBats && (TargetUtil.AnyMobsInRange(8f) || firebatsMaintain) &&
                    CanCast(SNOPower.Witchdoctor_Firebats) && Player.PrimaryResource >= fireBatsMana)
                {
                    var range = Settings.Combat.WitchDoctor.FirebatsRange > 12f ? 12f : Settings.Combat.WitchDoctor.FirebatsRange;

                    return new TrinityPower(SNOPower.Witchdoctor_Firebats, range, CurrentTarget.ACDGuid);
                }

                // Fire Bats fast-attack
                if (CanCast(SNOPower.Witchdoctor_Firebats) && Player.PrimaryResource >= fireBatsMana &&
                     (TargetUtil.AnyMobsInRange(Settings.Combat.WitchDoctor.FirebatsRange) || firebatsMaintain) && !hasCloudOfBats)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Firebats, Settings.Combat.WitchDoctor.FirebatsRange, CurrentTarget.Position);
                }

                // Acid Cloud
                if (CanCast(SNOPower.Witchdoctor_AcidCloud) && Player.PrimaryResource >= 175)
                {
                    Vector3 bestClusterPoint;
                    if (hasGraveInjustice)
                        bestClusterPoint = TargetUtil.GetBestClusterPoint(15f, Math.Min(Player.GoldPickupRadius + 8f, 30f));
                    else
                        bestClusterPoint = TargetUtil.GetBestClusterPoint(15f, 30f);

                    return new TrinityPower(SNOPower.Witchdoctor_AcidCloud, rangedAttackMaxRange, bestClusterPoint);
                }

                bool hasWellOfSouls = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_SpiritBarrage && s.RuneIndex == 1);
                bool hasRushOfEssence = CacheData.Hotbar.PassiveSkills.Any(s => s == SNOPower.Witchdoctor_Passive_RushOfEssence);

                // Spirit Barrage + Rush of Essence
                if (CanCast(SNOPower.Witchdoctor_SpiritBarrage) && Player.PrimaryResource >= 100 &&
                    hasRushOfEssence && !hasManitou)
                {
                    if (hasWellOfSouls)
                        return new TrinityPower(SNOPower.Witchdoctor_SpiritBarrage, 21f, CurrentTarget.ACDGuid);

                    return new TrinityPower(SNOPower.Witchdoctor_SpiritBarrage, 21f, CurrentTarget.ACDGuid);
                }

                // Zombie Charger backup
                if (CanCast(SNOPower.Witchdoctor_ZombieCharger) && Player.PrimaryResource >= 150)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_ZombieCharger, zombieChargerRange, CurrentTarget.Position);
                }

                // Regular spirit barage
                if (CanCast(SNOPower.Witchdoctor_SpiritBarrage) && Player.PrimaryResource >= 100 && !hasManitou)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SpiritBarrage, basicAttackRange, CurrentTarget.ACDGuid);
                }

                // Poison Darts fast-attack Spams Darts when mana is too low (to cast bears) @12yds or @10yds if Bears avialable
                if (CanCast(SNOPower.Witchdoctor_PoisonDart))
                {
                    VisionQuestRefreshTimer.Restart();
                    return new TrinityPower(SNOPower.Witchdoctor_PoisonDart, basicAttackRange, CurrentTarget.ACDGuid);
                }
                // Corpse Spiders fast-attacks Spams Spiders when mana is too low (to cast bears) @12yds or @10yds if Bears avialable
                if (CanCast(SNOPower.Witchdoctor_CorpseSpider))
                {
                    VisionQuestRefreshTimer.Restart();
                    return new TrinityPower(SNOPower.Witchdoctor_CorpseSpider, basicAttackRange, CurrentTarget.ACDGuid);
                }
                // Toads fast-attacks Spams Toads when mana is too low (to cast bears) @12yds or @10yds if Bears avialable
                if (CanCast(SNOPower.Witchdoctor_PlagueOfToads))
                {
                    VisionQuestRefreshTimer.Restart();
                    return new TrinityPower(SNOPower.Witchdoctor_PlagueOfToads, basicAttackRange, CurrentTarget.ACDGuid);
                }
                // Fire Bomb fast-attacks Spams Bomb when mana is too low (to cast bears) @12yds or @10yds if Bears avialable
                if (CanCast(SNOPower.Witchdoctor_Firebomb))
                {
                    VisionQuestRefreshTimer.Restart();
                    return new TrinityPower(SNOPower.Witchdoctor_Firebomb, basicAttackRange, CurrentTarget.ACDGuid);
                }

                //Hexing Pants Mod
                if (Legendary.HexingPantsOfMrYan.IsEquipped && CurrentTarget.IsUnit &&
                //!CanCast(SNOPower.Witchdoctor_Piranhas) && 
                CurrentTarget.RadiusDistance > 10f)
                {
                    return new TrinityPower(SNOPower.Walk, 10f, CurrentTarget.Position);
                }

                if (Legendary.HexingPantsOfMrYan.IsEquipped && CurrentTarget.IsUnit &&
                //!CanCast(SNOPower.Witchdoctor_Piranhas) && 
                CurrentTarget.RadiusDistance < 10f)
                {
                    Vector3 vNewTarget = MathEx.CalculatePointFrom(CurrentTarget.Position, Player.Position, -10f);
                    return new TrinityPower(SNOPower.Walk, 10f, vNewTarget);
                }

            }

            // Buffs
            if (UseOOCBuff)
            {
                // Spirit Walk OOC 
                if (CanCast(SNOPower.Witchdoctor_SpiritWalk) && Settings.Combat.Misc.AllowOOCMovement)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SpiritWalk);
                }


                //Spam fear at all times if Tiklandian Visage is ewquipped and fear spam is selected to keep fear buff active
                if (CanCast(SNOPower.Witchdoctor_Horrify) && Settings.Combat.WitchDoctor.SpamHorrify && Legendary.TiklandianVisage.IsEquipped)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Horrify);
                }

                bool hasStalker = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Horrify && s.RuneIndex == 4);
                // Horrify Buff When not in combat for movement speed -- Stalker
                if (CanCast(SNOPower.Witchdoctor_Horrify) && hasStalker)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Horrify);
                }

                // Zombie Dogs non-sacrifice build
                if (CanCast(SNOPower.Witchdoctor_SummonZombieDog) &&
                ((Legendary.TheTallMansFinger.IsEquipped && Trinity.PlayerOwnedZombieDogCount < 1) ||
                (!Legendary.TheTallMansFinger.IsEquipped && Trinity.PlayerOwnedZombieDogCount <= 2)))
                {
                    return new TrinityPower(SNOPower.Witchdoctor_SummonZombieDog);
                }

                bool hasRestlessGiant = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Gargantuan && s.RuneIndex == 0);
                bool hasWrathfulProtector = CacheData.Hotbar.ActiveSkills.Any(s => s.Power == SNOPower.Witchdoctor_Gargantuan && s.RuneIndex == 3);

                if (CanCast(SNOPower.Witchdoctor_Gargantuan) && !hasRestlessGiant && !hasWrathfulProtector && Trinity.PlayerOwnedGargantuanCount == 0)
                {
                    return new TrinityPower(SNOPower.Witchdoctor_Gargantuan);
                }
            }

            // Default Attacks
            if (IsNull(power))
                power = DefaultPower;

            return power;
        }

        private static readonly Func<TargetArea, bool> MinimumSoulHarvestCriteria = area =>

             //Harvest is off cooldown AND at least 2 debuffs exists && at least 40% of the units have a havestable debuff
             Skills.WitchDoctor.SoulHarvest.CanCast() && area.TotalDebuffCount(HarvesterCoreDebuffs) >= 2 &&
             area.DebuffedCount(HarvesterCoreDebuffs) >= area.UnitCount * 0.4 &&

             // AND there's an elite, boss or more than 3 units or greater 35% of the units within sight are within this cluster
             (area.EliteCount > 0 || area.BossCount > 0 || area.UnitCount >= 3 || area.UnitCount >= (float)Enemies.Nearby.UnitCount * 0.35);


        private static readonly Func<TargetArea, bool> IdealSoulHarvestCriteria = area =>

            // Harvest is off cooldown AND at least 7 debuffs are present (can be more than 1 per unit)
            Skills.WitchDoctor.SoulHarvest.CanCast() && area.TotalDebuffCount(HarvesterDebuffs) > 7 &&

            // AND average health accross units in area is more than 30%
            area.AverageHealthPct > 0.3f &&

            // AND at least 2 Elites, a boss or more than 5 units or 80% of the nearby units are within this area
            (area.EliteCount >= 2 || area.BossCount > 0 || area.UnitCount >= 5 || area.UnitCount >= (float)Enemies.Nearby.UnitCount * 0.80);

        private static readonly Func<TargetArea, bool> TikHorrifyCriteria = area =>

            //at least 2 Elites, a boss or more than 5 units or 80% of the nearby units are within this area
            (area.EliteCount >= 2 || area.UnitCount >= 5 || area.UnitCount >= (float)Enemies.Nearby.UnitCount * 0.80);



        private static readonly Action<string, TargetArea> LogTargetArea = (message, area) =>
        {
            Logger.LogDebug(message + " Units={0} Elites={1} DebuffedUnits={2} TotalDebuffs={4} AvgHealth={3:#.#} ---",
                area.UnitCount,
                area.EliteCount,
                area.DebuffedCount(HarvesterDebuffs),
                area.AverageHealthPct * 100,
                area.TotalDebuffCount(HarvesterDebuffs));
        };

        private static void MoveToSoulHarvestPoint(TargetArea area)
        {
            CombatMovement.Queue(new CombatMovement
            {
                Name = "Jade Harvest Position",
                Destination = area.Position,
                OnUpdate = m =>
                {
                    // Only change destination if the new target is way better
                    if (IdealSoulHarvestCriteria(Enemies.BestLargeCluster) &&
                        Enemies.BestLargeCluster.Position.Distance(m.Destination) > 10f)
                        m.Destination = Enemies.BestLargeCluster.Position;
                },
                OnFinished = m =>
                {
                    if (MinimumSoulHarvestCriteria(Enemies.CloseNearby))
                    {
                        //LogTargetArea("--- Harvesting (CombatMovement)", area);
                        Skills.WitchDoctor.SoulHarvest.Cast();
                    }
                },
                Options = new CombatMovementOptions
                {
                    Logging = LogLevel.Verbose,
                }
            });
        }

        private static void MoveToHorrifyPoint(TargetArea area)
        {
            CombatMovement.Queue(new CombatMovement
            {
                Name = "Horrify Position",
                Destination = area.Position,
                OnUpdate = m =>
                {
                    // Only change destination if the new target is way better
                    if (TikHorrifyCriteria(Enemies.BestLargeCluster) &&
                        Enemies.BestLargeCluster.Position.Distance(m.Destination) > 15f)
                        m.Destination = Enemies.BestLargeCluster.Position;
                },
                Options = new CombatMovementOptions
                {
                    AcceptableDistance = 12f,
                    Logging = LogLevel.Verbose,
                    ChangeInDistanceLimit = 2f,
                    SuccessBlacklistSeconds = 3,
                    FailureBlacklistSeconds = 7,
                    TimeBeforeBlocked = 500
                }

            });
        }

        private static bool WitchDoctorHasPrimaryAttack
        {
            get
            {
                return
                    Hotbar.Contains(SNOPower.Witchdoctor_WallOfZombies) ||
                    Hotbar.Contains(SNOPower.Witchdoctor_Firebats) ||
                    Hotbar.Contains(SNOPower.Witchdoctor_AcidCloud) ||
                    Hotbar.Contains(SNOPower.Witchdoctor_ZombieCharger) ||
                    Hotbar.Contains(SNOPower.Witchdoctor_PoisonDart) ||
                    Hotbar.Contains(SNOPower.Witchdoctor_CorpseSpider) ||
                    Hotbar.Contains(SNOPower.Witchdoctor_PlagueOfToads) ||
                    Hotbar.Contains(SNOPower.Witchdoctor_Firebomb);
            }
        }


        private static TrinityPower DestroyObjectPower
        {
            get
            {

                if (Hotbar.Contains(SNOPower.Witchdoctor_Firebomb))
                    return new TrinityPower(SNOPower.Witchdoctor_Firebomb, 12f, CurrentTarget.Position);
                if (Hotbar.Contains(SNOPower.Witchdoctor_PoisonDart))
                    return new TrinityPower(SNOPower.Witchdoctor_PoisonDart, 15f, CurrentTarget.Position);
                if (Hotbar.Contains(SNOPower.Witchdoctor_ZombieCharger) && Player.PrimaryResource >= 150)
                    return new TrinityPower(SNOPower.Witchdoctor_ZombieCharger, 12f, CurrentTarget.Position);
                if (Hotbar.Contains(SNOPower.Witchdoctor_CorpseSpider))
                    return new TrinityPower(SNOPower.Witchdoctor_CorpseSpider, 12f, CurrentTarget.Position);
                if (Hotbar.Contains(SNOPower.Witchdoctor_PlagueOfToads))
                    return new TrinityPower(SNOPower.Witchdoctor_PlagueOfToads, 12f, CurrentTarget.Position);
                if (Hotbar.Contains(SNOPower.Witchdoctor_AcidCloud) && Player.PrimaryResource >= 175)
                    return new TrinityPower(SNOPower.Witchdoctor_AcidCloud, 12f, CurrentTarget.Position);

                if (Hotbar.Contains(SNOPower.Witchdoctor_Sacrifice) && Hotbar.Contains(SNOPower.Witchdoctor_SummonZombieDog) &&
                    Trinity.PlayerOwnedZombieDogCount > 0 && Settings.Combat.WitchDoctor.ZeroDogs)
                    return new TrinityPower(SNOPower.Witchdoctor_Sacrifice, 12f, CurrentTarget.Position);

                if (Hotbar.Contains(SNOPower.Witchdoctor_SpiritBarrage) && Player.PrimaryResource > 100)
                    return new TrinityPower(SNOPower.Witchdoctor_SpiritBarrage, 12f, CurrentTarget.ACDGuid);

                return DefaultPower;
            }
        }

    }
}
