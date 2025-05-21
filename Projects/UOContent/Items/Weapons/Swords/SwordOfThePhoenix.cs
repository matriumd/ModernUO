using System;
using System.Collections.Generic;
using System.Linq;
using ModernUO.Serialization;

namespace Server.Items
{
    [SerializationGenerator(0, false)]
    public partial class SwordOfThePhoenix : BaseSword
    {
        private WeaponAbility? AssignedAbility;
        private List<WeaponAbility> AdditionalAbilities = new List<WeaponAbility>();

        private int Level;
        private int Experience;

        private static readonly WeaponAbility[] AllAbilities =
        {
            WeaponAbility.BleedAttack,
            WeaponAbility.ParalyzingBlow,
            WeaponAbility.MortalStrike,
            WeaponAbility.ArmorIgnore,
            WeaponAbility.DoubleStrike,
            WeaponAbility.InfectiousStrike,
            WeaponAbility.WhirlwindAttack,
            WeaponAbility.ArmorPierce
        };

        [Constructible]
        public SwordOfThePhoenix() : base(0x13FF)
        {
            Name = "Sword of the Phoenix";
            Hue = 1359;
            Weight = 7.0;

            Level = 0;
            Experience = 0;
        }

        public override WeaponAbility PrimaryAbility => AssignedAbility ?? WeaponAbility.DoubleStrike;
        public override WeaponAbility SecondaryAbility => WeaponAbility.ArmorIgnore;

        public override int AosStrengthReq => 25;
        public override int AosMinDamage => 11 + Level;
        public override int AosMaxDamage => 13 + Level;
        public override int AosSpeed => 46;
        public override float MlSpeed => 2.50f;

        public override int OldStrengthReq => 10;
        public override int OldMinDamage => 5;
        public override int OldMaxDamage => 26;
        public override int OldSpeed => 58;

        public override int DefHitSound => 0x23B;
        public override int DefMissSound => 0x23A;

        public override int InitMinHits => 31 + Level * 2;
        public override int InitMaxHits => 90 + Level * 3;

        public override bool OnEquip(Mobile from)
        {
            if (AssignedAbility == null)
                AssignedAbility = GetRandomAbility();

            return base.OnEquip(from);
        }

        public override void OnHit(Mobile attacker, Mobile defender, double damageBonus)
        {
            base.OnHit(attacker, defender, damageBonus);

            // 1% chance to gain 0.01 XP
            if (Utility.RandomDouble() <= 0.01)
                Experience += 1;

            if (Experience >= GetRequiredExperienceForLevel(Level + 1) && Level < 10)
            {
                Level++;
                Experience = 0;
                attacker.SendMessage($"Your Sword of the Phoenix has leveled up to {Level}!");

                if (Level % 2 == 0)
                {
                    AddNewAbility(attacker);
                }
            }

            // 5% chance to drain mana
            if (Utility.RandomDouble() <= 0.05)
            {
                int manaDrain = 1 + (Level / 4);

                if (defender.Mana >= manaDrain)
                {
                    defender.Mana -= manaDrain;
                    attacker.SendMessage($"The Sword of the Phoenix drains {manaDrain} mana from your foe.");
                }
                else if (defender.Mana > 0)
                {
                    attacker.SendMessage($"The Sword of the Phoenix drains the last of your foe's mana!");
                    defender.Mana = 0;
                }
            }
        }

        private static WeaponAbility GetRandomAbility()
        {
            return AllAbilities[Utility.Random(AllAbilities.Length)];
        }

        private int GetRequiredExperienceForLevel(int level)
        {
            int[] primeExp = { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29 };

            if (level < 1 || level > 10)
                return int.MaxValue;

            return primeExp[level - 1] * 1000;
        }

        private void AddNewAbility(Mobile from)
        {
            var currentAbilities = new HashSet<WeaponAbility> { PrimaryAbility, SecondaryAbility };
            currentAbilities.UnionWith(AdditionalAbilities);

            var newOptions = AllAbilities.Where(a => !currentAbilities.Contains(a)).ToList();

            if (newOptions.Count > 0)
            {
                var newAbility = newOptions[Utility.Random(newOptions.Count)];
                AdditionalAbilities.Add(newAbility);
                from.SendMessage($"Your Sword of the Phoenix has awakened a new ability: {newAbility}!");
            }
        }
    }
}
