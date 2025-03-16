using System.Collections.Generic;
using Server.Engines.PartySystem;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;
using Server.Multis;
using Server.SkillHandlers;

namespace Server.Misc
{
    public static class NotorietyHandlers
    {
        public static void Initialize()
        {
            Notoriety.Hues[Notoriety.Innocent] = 0x59;
            Notoriety.Hues[Notoriety.Ally] = 0x3F;
            Notoriety.Hues[Notoriety.CanBeAttacked] = 0x3B2;
            Notoriety.Hues[Notoriety.Criminal] = 0x3B2;
            Notoriety.Hues[Notoriety.Enemy] = 0x90;
            Notoriety.Hues[Notoriety.Murderer] = 0x22;
            Notoriety.Hues[Notoriety.Invulnerable] = 0x35;

            Notoriety.Handler = MobileNotoriety;

            Mobile.AllowBeneficialHandler = Mobile_AllowBeneficial;
            Mobile.AllowHarmfulHandler = Mobile_AllowHarmful;
        }

        public static bool Mobile_AllowBeneficial(Mobile from, Mobile target)
        {
            if (from == null || target == null || from.AccessLevel > AccessLevel.Player ||
                target.AccessLevel > AccessLevel.Player)
            {
                return true;
            }

            var bcFrom = from as BaseCreature;
            var bcTarg = target as BaseCreature;

            var pmFrom = (bcFrom?.GetMaster() ?? from) as PlayerMobile;
            var pmTarg = (bcTarg?.GetMaster() ?? target) as PlayerMobile;

            var map = from.Map;

            if ((map?.Rules & MapRules.BeneficialRestrictions) == 0)
            {
                return true; // In felucca, anything goes
            }

            if (!from.Player && pmFrom?.AccessLevel != AccessLevel.Player)
            {
                return true; // NPCs have no restrictions
            }

            if (bcTarg?.Controlled == false)
            {
                return false; // Players cannot heal uncontrolled mobiles
            }

            if (pmFrom?.Young == true && pmTarg?.Young == false)
            {
                return false; // Young players cannot perform beneficial actions towards non-young players or pets
            }

            return true;
        }

        public static bool Mobile_AllowHarmful(Mobile from, Mobile target)
        {
            if (from == null || target == null || from.AccessLevel > AccessLevel.Player ||
                target.AccessLevel > AccessLevel.Player)
            {
                return true;
            }

            var bcFrom = from as BaseCreature;
            var bcTarg = target as BaseCreature;

            var pmFrom = (bcFrom?.GetMaster() ?? from) as PlayerMobile;

            var map = from.Map;

            if ((map?.Rules & MapRules.HarmfulRestrictions) == 0)
            {
                return true; // In felucca, anything goes
            }

            if (!from.Player && pmFrom?.AccessLevel != AccessLevel.Player)
            {
                // Uncontrolled NPCs are only restricted by the young system
                return CheckAggressor(from.Aggressors, target) || CheckAggressed(from.Aggressed, target) ||
                       (target as PlayerMobile)?.CheckYoungProtection(from) != true;
            }

            if (bcTarg?.Controlled == true
                || bcTarg?.Summoned == true && bcTarg.SummonMaster != from && bcTarg.SummonMaster.Player)
            {
                return false; // Cannot harm other controlled mobiles from players
            }

            if (pmFrom == null && bcFrom != null && bcFrom.Summoned && target.Player)
            {
                return true; // Summons from monsters can attack players
            }

            if (target.Player)
            {
                return false; // Cannot harm other players
            }

            return bcTarg?.InitialInnocent == true || Notoriety.Compute(from, target) != Notoriety.Innocent;
        }

        /* Must be thread-safe */
        public static int MobileNotoriety(Mobile source, Mobile target)
        {
            var bcTarg = target as BaseCreature;

            if (Core.AOS && (target.Blessed || bcTarg?.IsInvulnerable == true || target is PlayerVendor or TownCrier))
            {
                return Notoriety.Invulnerable;
            }

            // Moved above AccessLevel check so staff summons are red
            if (bcTarg?.AlwaysMurderer == true)
            {
                return Notoriety.Murderer;
            }

            var pmFrom = source as PlayerMobile;
            var pmTarg = target as PlayerMobile;

            if (target.AccessLevel > AccessLevel.Player)
            {
                return Notoriety.CanBeAttacked;
            }

            if (source.Player && !target.Player && pmFrom != null && bcTarg != null)
            {
                var master = bcTarg.GetMaster();

                if (master?.AccessLevel > AccessLevel.Player)
                {
                    return Notoriety.CanBeAttacked;
                }

                master = bcTarg.ControlMaster;

                if (Core.ML && master != null)
                {
                    if (source == master && CheckAggressor(bcTarg.Aggressors, source) ||
                        CheckAggressor(source.Aggressors, bcTarg))
                    {
                        return Notoriety.CanBeAttacked;
                    }

                    return MobileNotoriety(source, master);
                }

                if (!bcTarg.Summoned && !bcTarg.Controlled && pmFrom.EnemyOfOneType == bcTarg.GetType())
                {
                    return Notoriety.Enemy;
                }
            }

            if (target.Kills >= 5 ||
                target.Body.IsMonster && IsSummoned(bcTarg) && target is not BaseFamiliar &&
                target is not Golem || bcTarg?.IsAnimatedDead == true)
            {
                return Notoriety.Murderer;
            }

            if (target.Criminal)
            {
                return Notoriety.Criminal;
            }

            if (Stealing.ClassicMode && pmTarg?.PermaFlags.Contains(source) == true)
            {
                return Notoriety.CanBeAttacked;
            }

            if (bcTarg?.AlwaysAttackable == true)
            {
                return Notoriety.CanBeAttacked;
            }

            if (CheckHouseFlag(source, target, target.Location, target.Map))
            {
                return Notoriety.CanBeAttacked;
            }

            if (bcTarg?.InitialInnocent != true)
            {
                if (!target.Body.IsHuman && !target.Body.IsGhost && !IsPet(bcTarg) && pmTarg == null || !Core.ML)
                {
                    return Notoriety.CanBeAttacked;
                }
            }

            if (CheckAggressor(source.Aggressors, target))
            {
                return Notoriety.CanBeAttacked;
            }

            if (CheckAggressed(source.Aggressed, target))
            {
                return Notoriety.CanBeAttacked;
            }

            if (bcTarg?.Controlled == true && bcTarg.ControlOrder == OrderType.Guard &&
                bcTarg.ControlTarget == source)
            {
                return Notoriety.CanBeAttacked;
            }

            if (source is BaseCreature bc)
            {
                var master = bc.GetMaster();

                if (master != null && (CheckAggressor(master.Aggressors, target) ||
                                       MobileNotoriety(master, target) == Notoriety.CanBeAttacked || bcTarg != null))
                {
                    return Notoriety.CanBeAttacked;
                }
            }

            return Notoriety.Innocent;
        }

        public static bool CheckHouseFlag(Mobile from, Mobile m, Point3D p, Map map)
        {
            var house = BaseHouse.FindHouseAt(p, map, 16);

            if (house?.Public != false || !house.IsFriend(from))
            {
                return false;
            }

            if (m != null && house.IsFriend(m))
            {
                return false;
            }

            return m is not BaseCreature c || c.Deleted || !c.Controlled || c.ControlMaster == null ||
                   !house.IsFriend(c.ControlMaster);
        }

        public static bool IsPet(BaseCreature c) => c?.Controlled == true;

        public static bool IsSummoned(BaseCreature c) => c?.Summoned == true;

        public static bool CheckAggressor(List<AggressorInfo> list, Mobile target)
        {
            for (var i = 0; i < list.Count; ++i)
            {
                if (list[i].Attacker == target)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool CheckAggressed(List<AggressorInfo> list, Mobile target)
        {
            for (var i = 0; i < list.Count; ++i)
            {
                var info = list[i];

                if (!info.CriminalAggression && info.Defender == target)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
