using System;
using System.Collections.Generic;
using ModernUO.CodeGeneratedEvents;
using Server.Accounting;
using Server.Collections;
using Server.ContextMenus;
using Server.Engines.BuffIcons;
using Server.Engines.Help;
using Server.Engines.PartySystem;
using Server.Engines.PlayerMurderSystem;
using Server.Engines.Quests;
using Server.Guilds;
using Server.Gumps;
using Server.Items;
using Server.Misc;
using Server.Movement;
using Server.Multis;
using Server.Network;
using Server.Regions;
using Server.SkillHandlers;
using Server.Spells;
using Server.Targeting;
using CalcMoves = Server.Movement.Movement;

namespace Server.Mobiles
{
    [Flags]
    public enum PlayerFlag // First 16 bits are reserved for default-distro use, start custom flags at 0x00010000
    {
        None = 0x00000000,
        Glassblowing = 0x00000001,
        Masonry = 0x00000002,
        SandMining = 0x00000004,
        StoneMining = 0x00000008,
        ToggleMiningStone = 0x00000010,
        KarmaLocked = 0x00000020,
        AutoRenewInsurance = 0x00000040,
        UseOwnFilter = 0x00000080,
        PagingSquelched = 0x00000200,
        Young = 0x00000400,
        AcceptGuildInvites = 0x00000800,
        DisplayChampionTitle = 0x00001000,
        HasStatReward = 0x00002000,
        RefuseTrades = 0x00004000
    }

    public enum NpcGuild
    {
        None,
        MagesGuild,
        WarriorsGuild,
        ThievesGuild,
        RangersGuild,
        HealersGuild,
        MinersGuild,
        MerchantsGuild,
        TinkersGuild,
        TailorsGuild,
        FishermensGuild,
        BardsGuild,
        BlacksmithsGuild
    }

    public enum SolenFriendship
    {
        None,
        Red,
        Black
    }

    public enum BlockMountType
    {
        None = -1,
        Dazed = 1040024, // You are still too dazed from being knocked off your mount to ride!
        BolaRecovery = 1062910, // You cannot mount while recovering from a bola throw.
        DismountRecovery = 1070859 // You cannot mount while recovering from a dismount special maneuver.
    }

    public partial class PlayerMobile : Mobile, IHasSteps
    {
        private static bool m_NoRecursion;

        private static readonly Point3D[] m_TrammelDeathDestinations =
        {
            new(1481, 1612, 20),
            new(2708, 2153, 0),
            new(2249, 1230, 0),
            new(5197, 3994, 37),
            new(1412, 3793, 0),
            new(3688, 2232, 20),
            new(2578, 604, 0),
            new(4397, 1089, 0),
            new(5741, 3218, -2),
            new(2996, 3441, 15),
            new(624, 2225, 0),
            new(1916, 2814, 0),
            new(2929, 854, 0),
            new(545, 967, 0),
            new(3665, 2587, 0)
        };

        private static readonly Point3D[] m_IlshenarDeathDestinations =
        {
            new(1216, 468, -13),
            new(723, 1367, -60),
            new(745, 725, -28),
            new(281, 1017, 0),
            new(986, 1011, -32),
            new(1175, 1287, -30),
            new(1533, 1341, -3),
            new(529, 217, -44),
            new(1722, 219, 96)
        };

        private static readonly Point3D[] m_MalasDeathDestinations =
        {
            new(2079, 1376, -70),
            new(944, 519, -71)
        };

        private static readonly Point3D[] m_TokunoDeathDestinations =
        {
            new(1166, 801, 27),
            new(782, 1228, 25),
            new(268, 624, 15)
        };

        private HashSet<int> _acquiredRecipes;

        private HashSet<Mobile> _allFollowers;
        private int m_BeardModID = -1, m_BeardModHue;

        // TODO: Pool BuffInfo objects
        private Dictionary<BuffIcon, BuffInfo> m_BuffTable;

        private Type m_EnemyOfOneType;
        private TimeSpan m_GameTime;

        /*
         * a value of zero means, that the mobile is not executing the spell. Otherwise,
         * the value should match the BaseMana required
        */

        private int m_HairModID = -1, m_HairModHue;

        public DateTime _honorTime;

        private Mobile m_InsuranceAward;
        private int m_InsuranceBonus;

        private int m_LastGlobalLight = -1, m_LastPersonalLight = -1;

        private bool m_LastProtectedMessage;

        private DateTime m_LastYoungHeal = DateTime.MinValue;

        private DateTime m_LastYoungMessage = DateTime.MinValue;

        private MountBlock _mountBlock;

        private int m_NextProtectionCheck = 10;
        private DateTime m_NextSmithBulkOrder;
        private DateTime m_NextTailorBulkOrder;

        private bool m_NoDeltaRecursion;

        // number of items that could not be automatically reinsured because gold in bank was not enough
        private int m_NonAutoreinsuredItems;

        private DateTime m_SavagePaintExpiration;

        private DateTime[] m_StuckMenuUses;

        private QuestArrow m_QuestArrow;

        public PlayerMobile()
        {
            VisibilityList = new List<Mobile>();
            PermaFlags = new List<Mobile>();

            m_GameTime = TimeSpan.Zero;
        }

        public PlayerMobile(Serial s) : base(s)
        {
            VisibilityList = new List<Mobile>();
        }

        public int StepsMax => 16;

        public int StepsGainedPerIdleTime => 1;

        public TimeSpan IdleTimePerStepsGain => TimeSpan.FromSeconds(1);

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime AnkhNextUse { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public TimeSpan DisguiseTimeLeft => DisguisePersistence.TimeRemaining(this);

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime PeacedUntil { get; set; }

        public DesignContext DesignContext { get; set; }

        public BlockMountType MountBlockReason => _mountBlock?.MountBlockReason ?? BlockMountType.None;

        public override int MaxWeight => (Core.ML && Race == Race.Human ? 100 : 40) + (int)(3.5 * Str);

        public override double ArmorRating
        {
            get
            {
                // BaseArmor ar;
                var rating = 0.0;

                AddArmorRating(ref rating, NeckArmor);
                AddArmorRating(ref rating, HandArmor);
                AddArmorRating(ref rating, HeadArmor);
                AddArmorRating(ref rating, ArmsArmor);
                AddArmorRating(ref rating, LegsArmor);
                AddArmorRating(ref rating, ChestArmor);
                AddArmorRating(ref rating, ShieldArmor);

                return VirtualArmor + VirtualArmorMod + rating;
            }
        }

        public SkillName[] AnimalFormRestrictedSkills { get; } =
        {
            SkillName.ArmsLore, SkillName.Begging, SkillName.Discordance, SkillName.Forensics,
            SkillName.Inscribe, SkillName.ItemID, SkillName.Meditation, SkillName.Peacemaking,
            SkillName.Provocation, SkillName.RemoveTrap, SkillName.SpiritSpeak, SkillName.Stealing,
            SkillName.TasteID
        };

        public override double RacialSkillBonus
        {
            get
            {
                if (Core.ML && Race == Race.Human)
                {
                    return 20.0;
                }

                return 0;
            }
        }

        public List<Item> EquipSnapshot { get; private set; }

        public SkillName Learning { get; set; } = (SkillName)(-1);

        [CommandProperty(AccessLevel.GameMaster)]
        public TimeSpan SavagePaintExpiration
        {
            get => Utility.Max(m_SavagePaintExpiration - Core.Now, TimeSpan.Zero);
            set => m_SavagePaintExpiration = Core.Now + value;
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public TimeSpan NextSmithBulkOrder
        {
            get => Utility.Max(m_NextSmithBulkOrder - Core.Now, TimeSpan.Zero);
            set
            {
                try
                {
                    m_NextSmithBulkOrder = Core.Now + value;
                }
                catch
                {
                    // ignored
                }
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public TimeSpan NextTailorBulkOrder
        {
            get => Utility.Max(m_NextTailorBulkOrder - Core.Now, TimeSpan.Zero);
            set
            {
                try
                {
                    m_NextTailorBulkOrder = Core.Now + value;
                }
                catch
                {
                    // ignored
                }
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime LastEscortTime { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime LastPetBallTime { get; set; }

        public List<Mobile> VisibilityList { get; }

        public List<Mobile> PermaFlags { get; private set; }

        public override int Luck => AosAttributes.GetValue(this, AosAttribute.Luck);

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime SessionStart { get; private set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public TimeSpan GameTime
        {
            get
            {
                if (NetState != null)
                {
                    return m_GameTime + (Core.Now - SessionStart);
                }

                return m_GameTime;
            }
        }

        public bool BedrollLogout { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public override bool Paralyzed
        {
            get => base.Paralyzed;
            set
            {
                base.Paralyzed = value;

                if (value)
                {
                    AddBuff(new BuffInfo(BuffIcon.Paralyze, 1075827)); // Paralyze/You are frozen and can not move
                }
                else
                {
                    RemoveBuff(BuffIcon.Paralyze);
                }
            }
        }

        // WARNING - This can be null!!
        public HashSet<Mobile> Stabled { get; private set; }

        // WARNING - This can be null!!
        public HashSet<Mobile> AutoStabled { get; private set; }

        public bool NinjaWepCooldown { get; set; }

        // WARNING - This can be null!!
        public HashSet<Mobile> AllFollowers => _allFollowers;

        [CommandProperty(AccessLevel.GameMaster)]
        public int GuildMessageHue { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public int AllianceMessageHue { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public int Profession { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool IsStealthing // IsStealthing should be moved to Server.Mobiles
        {
            get;
            set;
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public NpcGuild NpcGuild { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime NpcGuildJoinTime { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime NextBODTurnInTime { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime LastOnline { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public long LastMoved => LastMoveTime;

        [CommandProperty(AccessLevel.GameMaster)]
        public TimeSpan NpcGuildGameTime { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public int ToTItemsTurnedIn { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public int ToTTotalMonsterFame { get; set; }

        public int ExecutesLightningStrike { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public int ToothAche
        {
            get => CandyCane.GetToothAche(this);
            set => CandyCane.SetToothAche(this, value);
        }

        public PlayerFlag Flags { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool PagingSquelched
        {
            get => GetFlag(PlayerFlag.PagingSquelched);
            set => SetFlag(PlayerFlag.PagingSquelched, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool Glassblowing
        {
            get => GetFlag(PlayerFlag.Glassblowing);
            set => SetFlag(PlayerFlag.Glassblowing, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool Masonry
        {
            get => GetFlag(PlayerFlag.Masonry);
            set => SetFlag(PlayerFlag.Masonry, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool SandMining
        {
            get => GetFlag(PlayerFlag.SandMining);
            set => SetFlag(PlayerFlag.SandMining, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool StoneMining
        {
            get => GetFlag(PlayerFlag.StoneMining);
            set => SetFlag(PlayerFlag.StoneMining, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool ToggleMiningStone
        {
            get => GetFlag(PlayerFlag.ToggleMiningStone);
            set => SetFlag(PlayerFlag.ToggleMiningStone, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool KarmaLocked
        {
            get => GetFlag(PlayerFlag.KarmaLocked);
            set => SetFlag(PlayerFlag.KarmaLocked, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool AutoRenewInsurance
        {
            get => GetFlag(PlayerFlag.AutoRenewInsurance);
            set => SetFlag(PlayerFlag.AutoRenewInsurance, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool UseOwnFilter
        {
            get => GetFlag(PlayerFlag.UseOwnFilter);
            set => SetFlag(PlayerFlag.UseOwnFilter, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool AcceptGuildInvites
        {
            get => GetFlag(PlayerFlag.AcceptGuildInvites);
            set => SetFlag(PlayerFlag.AcceptGuildInvites, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool HasStatReward
        {
            get => GetFlag(PlayerFlag.HasStatReward);
            set => SetFlag(PlayerFlag.HasStatReward, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool RefuseTrades
        {
            get => GetFlag(PlayerFlag.RefuseTrades);
            set => SetFlag(PlayerFlag.RefuseTrades, value);
        }

        public Dictionary<Type, int> RecoverableAmmo { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime AcceleratedStart { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public SkillName AcceleratedSkill { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public override int HitsMax
        {
            get
            {
                int strBase;
                var strOffs = GetStatOffset(StatType.Str);

                if (Core.AOS)
                {
                    strBase = Str; // this.Str already includes GetStatOffset/str
                    strOffs = AosAttributes.GetValue(this, AosAttribute.BonusHits);

                    if (Core.ML && strOffs > 25 && AccessLevel <= AccessLevel.Player)
                    {
                        strOffs = 25;
                    }
                }
                else
                {
                    strBase = RawStr;
                }

                return strBase / 2 + 50 + strOffs;
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public override int StamMax => base.StamMax + AosAttributes.GetValue(this, AosAttribute.BonusStam);

        [CommandProperty(AccessLevel.GameMaster)]
        public override int ManaMax => base.ManaMax + AosAttributes.GetValue(this, AosAttribute.BonusMana) +
                                       (Core.ML && Race == Race.Elf ? 20 : 0);

        [CommandProperty(AccessLevel.GameMaster)]
        public override int Str
        {
            get
            {
                if (Core.ML && AccessLevel == AccessLevel.Player)
                {
                    return Math.Min(base.Str, 150);
                }

                return base.Str;
            }
            set => base.Str = value;
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public override int Int
        {
            get
            {
                if (Core.ML && AccessLevel == AccessLevel.Player)
                {
                    return Math.Min(base.Int, 150);
                }

                return base.Int;
            }
            set => base.Int = value;
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public override int Dex
        {
            get
            {
                if (Core.ML && AccessLevel == AccessLevel.Player)
                {
                    return Math.Min(base.Dex, 150);
                }

                return base.Dex;
            }
            set => base.Dex = value;
        }

        public QuestSystem Quest { get; set; }

        public List<QuestRestartInfo> DoneQuests { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public SolenFriendship SolenFriendship { get; set; }

        public Type EnemyOfOneType
        {
            get => m_EnemyOfOneType;
            set
            {
                var oldType = m_EnemyOfOneType;
                var newType = value;

                if (oldType == newType)
                {
                    return;
                }

                m_EnemyOfOneType = value;

                DeltaEnemies(oldType, newType);
            }
        }

        public bool WaitingForEnemy { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool Young
        {
            get => GetFlag(PlayerFlag.Young);
            set
            {
                SetFlag(PlayerFlag.Young, value);
                InvalidateProperties();
            }
        }

        public SpeechLog SpeechLog { get; private set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool DisplayChampionTitle
        {
            get => GetFlag(PlayerFlag.DisplayChampionTitle);
            set => SetFlag(PlayerFlag.DisplayChampionTitle, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int ShortTermMurders
        {
            get => PlayerMurderSystem.GetMurderContext(this, out var context) ? context.ShortTermMurders : 0;
            set => PlayerMurderSystem.ManuallySetShortTermMurders(this, value);
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime ShortTermMurderExpiration
            => PlayerMurderSystem.GetMurderContext(this, out var context) && context.ShortTermMurders > 0
                ? Core.Now + (context.ShortTermElapse - GameTime)
                : DateTime.MinValue;

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime LongTermMurderExpiration
            => Kills > 0 && PlayerMurderSystem.GetMurderContext(this, out var context)
                ? Core.Now + (context.LongTermElapse - GameTime)
                : DateTime.MinValue;

        [CommandProperty(AccessLevel.GameMaster)]
        public int KnownRecipes => _acquiredRecipes?.Count ?? 0;

        public QuestArrow QuestArrow
        {
            get => m_QuestArrow;
            set
            {
                if (m_QuestArrow != value)
                {
                    m_QuestArrow?.Stop();

                    m_QuestArrow = value;
                }
            }
        }

        public void ClearQuestArrow() => m_QuestArrow = null;

        public static Direction GetDirection4(Point3D from, Point3D to)
        {
            var dx = from.X - to.X;
            var dy = from.Y - to.Y;

            var rx = dx - dy;
            var ry = dx + dy;

            Direction ret;

            if (rx >= 0 && ry >= 0)
            {
                ret = Direction.West;
            }
            else if (rx >= 0 && ry < 0)
            {
                ret = Direction.South;
            }
            else if (rx < 0 && ry < 0)
            {
                ret = Direction.East;
            }
            else
            {
                ret = Direction.North;
            }

            return ret;
        }

        public override bool OnDroppedItemToWorld(Item item, Point3D location)
        {
            if (!base.OnDroppedItemToWorld(item, location))
            {
                return false;
            }

            if (Core.AOS)
            {
                foreach (Mobile m in Map.GetMobilesAt(location))
                {
                    if (m.Z >= location.Z && m.Z < location.Z + 16 && (!m.Hidden || m.AccessLevel == AccessLevel.Player))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public bool GetFlag(PlayerFlag flag) => (Flags & flag) != 0;

        public void SetFlag(PlayerFlag flag, bool value)
        {
            if (value)
            {
                Flags |= flag;
            }
            else
            {
                Flags &= ~flag;
            }
        }

        public static void Initialize()
        {
            EventSink.Logout += OnLogout;
            EventSink.Connected += EventSink_Connected;
            EventSink.Disconnected += EventSink_Disconnected;

            if (Core.SE)
            {
                Timer.StartTimer(CheckPets);
            }

            var stableMigrations = StableMigrations;
            if (stableMigrations?.Count > 0)
            {
                foreach (var (player, stabled) in stableMigrations)
                {
                    if (player is PlayerMobile pm)
                    {
                        pm.Stabled = stabled;
                    }
                }
            }
        }

        public static void TargetedSkillUse(Mobile from, IEntity target, int skillId)
        {
            if (from == null || target == null)
            {
                return;
            }

            from.TargetLocked = true;

            if (skillId == 35)
            {
                AnimalTaming.DisableMessage = true;
            }
            // AnimalTaming.DeferredTarget = false;

            if (from.UseSkill(skillId))
            {
                from.Target?.Invoke(from, target);
            }

            if (skillId == 35)
            // AnimalTaming.DeferredTarget = true;
            {
                AnimalTaming.DisableMessage = false;
            }

            from.TargetLocked = false;
        }

        public static void EquipMacro(Mobile m, List<Serial> list)
        {
            if (m is PlayerMobile { Alive: true } pm && pm.Backpack != null)
            {
                var pack = pm.Backpack;

                foreach (var serial in list)
                {
                    Item item = null;
                    foreach (var i in pack.Items)
                    {
                        if (i.Serial == serial)
                        {
                            item = i;
                            break;
                        }
                    }

                    if (item == null)
                    {
                        continue;
                    }

                    var toMove = pm.FindItemOnLayer(item.Layer);

                    if (toMove != null)
                    {
                        // pack.DropItem(toMove);
                        toMove.Internalize();

                        if (!pm.EquipItem(item))
                        {
                            pm.EquipItem(toMove);
                        }
                        else
                        {
                            pack.DropItem(toMove);
                        }
                    }
                    else
                    {
                        pm.EquipItem(item);
                    }
                }
            }
        }

        public static void UnequipMacro(Mobile m, List<Layer> layers)
        {
            if (m is PlayerMobile { Alive: true } pm && pm.Backpack != null)
            {
                var pack = pm.Backpack;
                var eq = m.Items;

                for (var i = eq.Count - 1; i >= 0; i--)
                {
                    var item = eq[i];
                    if (layers.Contains(item.Layer))
                    {
                        pack.TryDropItem(pm, item, false);
                    }
                }
            }
        }

        private static void CheckPets()
        {
            foreach (var m in World.Mobiles.Values)
            {
                if (m is not PlayerMobile pm || pm.AllFollowers == null || pm.AllFollowers.Count == 0)
                {
                    continue;
                }

                var autoStabledCount = pm.AutoStabled?.Count ?? 0;
                if (pm.Mounted && pm.Mount is not EtherealMount)
                {
                    autoStabledCount++;
                }

                if (pm.AllFollowers.Count > autoStabledCount)
                {
                    pm.AutoStablePets();
                }
            }
        }

        public void SetMountBlock(BlockMountType type, bool dismount) =>
            SetMountBlock(type, TimeSpan.MaxValue, dismount);

        public void SetMountBlock(BlockMountType type, TimeSpan duration, bool dismount)
        {
            if (dismount && Mount != null)
            {
                Mount.Rider = null;
            }

            if (_mountBlock == null || !_mountBlock.CheckBlock() || _mountBlock.Expiration < Core.Now + duration)
            {
                _mountBlock?.RemoveBlock(this);
                _mountBlock = new MountBlock(duration, type, this);
            }
        }

        public override void OnSkillInvalidated(Skill skill)
        {
            if (Core.AOS && skill.SkillName == SkillName.MagicResist)
            {
                UpdateResistances();
            }
        }

        public override int GetMaxResistance(ResistanceType type)
        {
            if (AccessLevel > AccessLevel.Player)
            {
                return 100;
            }

            var max = base.GetMaxResistance(type);

            if (type != ResistanceType.Physical)
            {
                max -= 10;
            }

            if (Core.ML && Race == Race.Elf && type == ResistanceType.Energy)
            {
                max += 5; // Intended to go after the 60 max from curse
            }

            return max;
        }

        protected override void OnRaceChange(Race oldRace)
        {
            ValidateEquipment();
            UpdateResistances();
        }

        public override void OnNetStateChanged()
        {
            m_LastGlobalLight = -1;
            m_LastPersonalLight = -1;
        }

        public override void ComputeBaseLightLevels(out int global, out int personal)
        {
            global = LightCycle.ComputeLevelFor(this);

            var racialNightSight = Core.ML && Race == Race.Elf;

            if (LightLevel < 21 && (AosAttributes.GetValue(this, AosAttribute.NightSight) > 0 || racialNightSight))
            {
                personal = 21;
            }
            else
            {
                personal = LightLevel;
            }
        }

        public override void CheckLightLevels(bool forceResend)
        {
            var ns = NetState;

            if (ns == null)
            {
                return;
            }

            ComputeLightLevels(out var global, out var personal);

            if (!forceResend)
            {
                forceResend = global != m_LastGlobalLight || personal != m_LastPersonalLight;
            }

            if (!forceResend)
            {
                return;
            }

            m_LastGlobalLight = global;
            m_LastPersonalLight = personal;

            ns.SendGlobalLightLevel(global);
            ns.SendPersonalLightLevel(Serial, personal);
        }

        public override int GetMinResistance(ResistanceType type)
        {
            var magicResist = (int)(Skills.MagicResist.Value * 10);
            int min;

            if (magicResist >= 1000)
            {
                min = 40 + (magicResist - 1000) / 50;
            }
            else if (magicResist >= 400)
            {
                min = (magicResist - 400) / 15;
            }
            else
            {
                min = int.MinValue;
            }

            return Math.Clamp(min, base.GetMinResistance(type), MaxPlayerResistance);
        }

        public override void OnManaChange(int oldValue)
        {
            base.OnManaChange(oldValue);
            if (ExecutesLightningStrike > 0)
            {
                if (Mana < ExecutesLightningStrike)
                {
                    SpecialMove.ClearCurrentMove(this);
                }
            }

            if (!Meditating)
            {
                RemoveBuff(BuffIcon.ActiveMeditation);
            }
        }

        [GeneratedEvent(nameof(PlayerLoginEvent))]
        public static partial void PlayerLoginEvent(PlayerMobile pm);

        [OnEvent(nameof(PlayerLoginEvent))]
        public static void OnLogin(PlayerMobile from)
        {
            if (AccountHandler.LockdownLevel > AccessLevel.Player)
            {
                string notice;

                if (from.Account is not Account acct || !acct.HasAccess(from.NetState))
                {
                    if (from.AccessLevel == AccessLevel.Player)
                    {
                        notice = "The server is currently under lockdown. No players are allowed to log in at this time.";
                    }
                    else
                    {
                        notice =
                            "The server is currently under lockdown. You do not have sufficient access level to connect.";
                    }

                    if (from.NetState != null)
                    {
                        Timer.StartTimer(TimeSpan.FromSeconds(1.0), () => from.NetState.Disconnect("Server is locked down"));
                    }
                }
                else if (from.AccessLevel >= AccessLevel.Administrator)
                {
                    notice =
                        "The server is currently under lockdown. As you are an administrator, you may change this from the [Admin gump.";
                }
                else
                {
                    notice = "The server is currently under lockdown. You have sufficient access level to connect.";
                }

                from.SendGump(new ServerLockdownNoticeGump(notice));
                return;
            }

            from.ClaimAutoStabledPets();
            from.ResendBuffs();
        }

        private class ServerLockdownNoticeGump : StaticNoticeGump<ServerLockdownNoticeGump>
        {
            public override int Width => 300;
            public override int Height => 140;
            public override string Content { get; }

            public ServerLockdownNoticeGump(string content) => Content = content;
        }

        public void ValidateEquipment()
        {
            if (m_NoDeltaRecursion || Map == null || Map == Map.Internal)
            {
                return;
            }

            if (Items == null)
            {
                return;
            }

            m_NoDeltaRecursion = true;
            Timer.StartTimer(ValidateEquipment_Sandbox);
        }

        private void ValidateEquipment_Sandbox()
        {
            try
            {
                if (Map == null || Map == Map.Internal)
                {
                    return;
                }

                var items = Items;

                if (items == null)
                {
                    return;
                }

                var moved = false;

                var str = Str;
                var dex = Dex;
                var intel = Int;

                Mobile from = this;

                for (var i = items.Count - 1; i >= 0; --i)
                {
                    if (i >= items.Count)
                    {
                        continue;
                    }

                    var item = items[i];

                    if (item is BaseWeapon weapon)
                    {
                        var drop = false;

                        if (dex < weapon.DexRequirement)
                        {
                            drop = true;
                        }
                        else if (str < AOS.Scale(weapon.StrRequirement, 100 - weapon.GetLowerStatReq()))
                        {
                            drop = true;
                        }
                        else if (intel < weapon.IntRequirement)
                        {
                            drop = true;
                        }
                        else if (!weapon.CheckRace(Race))
                        {
                            drop = true;
                        }

                        if (drop)
                        {
                            // You can no longer wield your ~1_WEAPON~
                            from.SendLocalizedMessage(1062001, weapon.Name ?? $"#{weapon.LabelNumber}");
                            from.AddToBackpack(weapon);
                            moved = true;
                        }
                    }
                    else if (item is BaseArmor armor)
                    {
                        var drop = false;

                        if (!armor.AllowMaleWearer && !from.Female && from.AccessLevel < AccessLevel.GameMaster)
                        {
                            drop = true;
                        }
                        else if (!armor.AllowFemaleWearer && from.Female && from.AccessLevel < AccessLevel.GameMaster)
                        {
                            drop = true;
                        }
                        else if (!armor.CheckRace(Race))
                        {
                            drop = true;
                        }
                        else
                        {
                            int strBonus = armor.ComputeStatBonus(StatType.Str), strReq = armor.ComputeStatReq(StatType.Str);
                            int dexBonus = armor.ComputeStatBonus(StatType.Dex), dexReq = armor.ComputeStatReq(StatType.Dex);
                            int intBonus = armor.ComputeStatBonus(StatType.Int), intReq = armor.ComputeStatReq(StatType.Int);

                            if (dex < dexReq || dex + dexBonus < 1)
                            {
                                drop = true;
                            }
                            else if (str < strReq || str + strBonus < 1)
                            {
                                drop = true;
                            }
                            else if (intel < intReq || intel + intBonus < 1)
                            {
                                drop = true;
                            }
                        }

                        if (drop)
                        {
                            var name = armor.Name ?? $"#{armor.LabelNumber}";

                            if (armor is BaseShield)
                            {
                                from.SendLocalizedMessage(1062003, name); // You can no longer equip your ~1_SHIELD~
                            }
                            else
                            {
                                from.SendLocalizedMessage(1062002, name); // You can no longer wear your ~1_ARMOR~
                            }

                            from.AddToBackpack(armor);
                            moved = true;
                        }
                    }
                    else if (item is BaseClothing clothing)
                    {
                        var drop = false;

                        if (!clothing.AllowMaleWearer && !from.Female && from.AccessLevel < AccessLevel.GameMaster)
                        {
                            drop = true;
                        }
                        else if (!clothing.AllowFemaleWearer && from.Female && from.AccessLevel < AccessLevel.GameMaster)
                        {
                            drop = true;
                        }
                        else if (clothing.RequiredRace != null && clothing.RequiredRace != Race)
                        {
                            drop = true;
                        }
                        else
                        {
                            var strBonus = clothing.ComputeStatBonus(StatType.Str);
                            var strReq = clothing.ComputeStatReq(StatType.Str);

                            if (str < strReq || str + strBonus < 1)
                            {
                                drop = true;
                            }
                        }

                        if (drop)
                        {
                            // You can no longer wear your ~1_ARMOR~
                            from.SendLocalizedMessage(1062002, clothing.Name ?? $"#{clothing.LabelNumber}");

                            from.AddToBackpack(clothing);
                            moved = true;
                        }
                    }
                }

                if (moved)
                {
                    from.SendLocalizedMessage(500647); // Some equipment has been moved to your backpack.
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                m_NoDeltaRecursion = false;
            }
        }

        public override void Delta(MobileDelta flag)
        {
            base.Delta(flag);

            if ((flag & MobileDelta.Stat) != 0)
            {
                ValidateEquipment();
            }
        }

        private static void OnLogout(Mobile m)
        {
            (m as PlayerMobile)?.AutoStablePets();
        }

        private static void EventSink_Connected(Mobile m)
        {
            if (m is PlayerMobile pm)
            {
                pm.SessionStart = Core.Now;

                pm.Quest?.StartTimer();

                pm.BedrollLogout = false;
                pm.LastOnline = Core.Now;
            }

            DisguisePersistence.StartTimer(m);

            Timer.StartTimer(() => SpecialMove.ClearAllMoves(m));
        }

        private static void EventSink_Disconnected(Mobile from)
        {
            var context = DesignContext.Find(from);

            if (context != null)
            {
                /* Client disconnected
                 *  - Remove design context
                 *  - Eject all from house
                 *  - Restore relocated entities
                 */

                // Remove design context
                DesignContext.Remove(from);

                // Eject all from house
                from.RevealingAction();

                foreach (var item in context.Foundation.GetItems())
                {
                    item.Location = context.Foundation.BanLocation;
                }

                foreach (var mobile in context.Foundation.GetMobiles())
                {
                    mobile.Location = context.Foundation.BanLocation;
                }

                // Restore relocated entities
                context.Foundation.RestoreRelocatedEntities();
            }

            if (from is PlayerMobile pm)
            {
                pm.m_GameTime += Core.Now - pm.SessionStart;

                pm.Quest?.StopTimer();

                pm.SpeechLog = null;
                pm.ClearQuestArrow();
                pm.LastOnline = Core.Now;
            }

            DisguisePersistence.StopTimer(from);
        }

        public override void RevealingAction()
        {
            if (DesignContext != null)
            {
                return;
            }

            base.RevealingAction();

            IsStealthing = false; // IsStealthing should be moved to Server.Mobiles
        }

        public override void OnHiddenChanged()
        {
            base.OnHiddenChanged();

            // Always remove, default to the hiding icon EXCEPT in the invis spell where it's explicitly set
            RemoveBuff(BuffIcon.Invisibility);

            if (!Hidden)
            {
                RemoveBuff(BuffIcon.HidingAndOrStealth);
            }
            else // if (!InvisibilitySpell.HasTimer( this ))
            {
                // Hidden/Stealthing & You Are Hidden
                AddBuff(new BuffInfo(BuffIcon.HidingAndOrStealth, 1075655));
            }
        }

        public override void OnSubItemAdded(Item item)
        {
            if (AccessLevel < AccessLevel.GameMaster && item.IsChildOf(Backpack))
            {
                var maxWeight = StaminaSystem.GetMaxWeight(this);
                var curWeight = BodyWeight + TotalWeight;

                if (curWeight > maxWeight)
                {
                    SendLocalizedMessage(1019035, true, $" : {curWeight} / {maxWeight}");
                }
            }

            base.OnSubItemAdded(item);
        }

        public override bool CanBeHarmful(Mobile target, bool message, bool ignoreOurBlessedness)
        {
            if (DesignContext != null || target is PlayerMobile mobile && mobile.DesignContext != null)
            {
                return false;
            }

            if (target is BaseCreature creature && creature.IsInvulnerable || target is PlayerVendor or TownCrier)
            {
                if (message)
                {
                    if (target.Title == null)
                    {
                        SendMessage($"{target.Name} cannot be harmed.");
                    }
                    else
                    {
                        SendMessage($"{target.Name} {target.Title} cannot be harmed.");
                    }
                }

                return false;
            }

            return base.CanBeHarmful(target, message, ignoreOurBlessedness);
        }

        public override bool CanBeBeneficial(Mobile target, bool message, bool allowDead)
        {
            if (DesignContext != null || target is PlayerMobile mobile && mobile.DesignContext != null)
            {
                return false;
            }

            return base.CanBeBeneficial(target, message, allowDead);
        }

        public override bool CheckContextMenuDisplay(IEntity target) => DesignContext == null;

        public override void OnItemAdded(Item item)
        {
            base.OnItemAdded(item);

            if (item is BaseArmor or BaseWeapon)
            {
                CheckStatTimers();
            }

            if (NetState != null)
            {
                CheckLightLevels(false);
            }
        }

        public override void OnItemRemoved(Item item)
        {
            base.OnItemRemoved(item);

            if (item is BaseArmor or BaseWeapon)
            {
                CheckStatTimers();
            }

            if (NetState != null)
            {
                CheckLightLevels(false);
            }
        }

        private void AddArmorRating(ref double rating, Item armor)
        {
            if (armor is BaseArmor ar && (!Core.AOS || ar.ArmorAttributes.MageArmor == 0))
            {
                rating += ar.ArmorRatingScaled;
            }
        }

        public override bool Move(Direction d)
        {
            if (NetState != null)
            {
                var gumps = NetState.GetGumps();

                if (Alive)
                {
                    gumps.Close<ResurrectGump>();
                }
                else if (gumps.Has<ResurrectGump>())
                {
                    SendLocalizedMessage(500111); // You are frozen and cannot move.
                    return false;
                }
            }

            // var speed = ComputeMovementSpeed(d);

            if (!Alive)
            {
                MovementImpl.IgnoreMovableImpassables = true;
            }

            var res = base.Move(d);
            MovementImpl.IgnoreMovableImpassables = false;
            return res;
        }

        public override bool CheckMovement(Direction d, out int newZ)
        {
            var context = DesignContext;

            if (context == null)
            {
                return base.CheckMovement(d, out newZ);
            }

            var foundation = context.Foundation;

            newZ = foundation.Z + HouseFoundation.GetLevelZ(context.Level, context.Foundation);

            var newX = X;
            var newY = Y;
            CalcMoves.Offset(d, ref newX, ref newY);

            var startX = foundation.X + foundation.Components.Min.X + 1;
            var startY = foundation.Y + foundation.Components.Min.Y + 1;
            var endX = startX + foundation.Components.Width - 1;
            var endY = startY + foundation.Components.Height - 2;

            return newX >= startX && newY >= startY && newX < endX && newY < endY && Map == foundation.Map;
        }

        public virtual void RecheckTownProtection()
        {
            m_NextProtectionCheck = 10;

            var reg = Region.GetRegion<GuardedRegion>();
            var isProtected = reg?.IsDisabled() == false;

            if (isProtected != m_LastProtectedMessage)
            {
                if (isProtected)
                {
                    SendLocalizedMessage(500112); // You are now under the protection of the town guards.
                }
                else
                {
                    SendLocalizedMessage(500113); // You have left the protection of the town guards.
                }

                m_LastProtectedMessage = isProtected;
            }
        }

        public override void MoveToWorld(Point3D loc, Map map)
        {
            base.MoveToWorld(loc, map);

            RecheckTownProtection();
        }

        public override void SetLocation(Point3D loc, bool isTeleport)
        {
            if (!isTeleport && AccessLevel == AccessLevel.Player)
            {
                // moving, not teleporting
                var zDrop = Location.Z - loc.Z;

                if (zDrop > 20)                  // we fell more than one story
                {
                    Hits -= zDrop / 20 * 10 - 5; // deal some damage; does not kill, disrupt, etc
                }
            }

            base.SetLocation(loc, isTeleport);

            if (isTeleport || --m_NextProtectionCheck == 0)
            {
                RecheckTownProtection();
            }
        }

        public override void GetContextMenuEntries(Mobile from, ref PooledRefList<ContextMenuEntry> list)
        {
            base.GetContextMenuEntries(from, ref list);

            if (from == this)
            {
                if (Alive && Backpack != null && CanSee(Backpack))
                {
                    list.Add(new OpenBackpackEntry());
                }

                Quest?.GetContextMenuEntries(ref list);

                if (Alive)
                {
                    if (InsuranceEnabled)
                    {
                        if (Core.SA)
                        {
                            list.Add(new CallbackEntry(1114299, OpenItemInsuranceMenu)); // Open Item Insurance Menu
                        }

                        list.Add(new CallbackEntry(6201, ToggleItemInsurance)); // Toggle Item Insurance

                        if (!Core.SA)
                        {
                            if (AutoRenewInsurance)
                            {
                                // Cancel Renewing Inventory Insurance
                                list.Add(new CallbackEntry(6202, CancelRenewInventoryInsurance));
                            }
                            else
                            {
                                // Auto Renew Inventory Insurance
                                list.Add(new CallbackEntry(6200, AutoRenewInventoryInsurance));
                            }
                        }
                    }
                }

                var house = BaseHouse.FindHouseAt(this);

                if (house != null)
                {
                    if (Alive && house.InternalizedVendors.Count > 0 && house.IsOwner(this))
                    {
                        list.Add(new CallbackEntry(6204, GetVendor));
                    }
                }

                if (Alive)
                {
                    list.Add(new CallbackEntry(6210, ToggleChampionTitleDisplay));
                }

                if (Core.HS)
                {
                    var ns = from.NetState;

                    if (ns?.ExtendedStatus == true)
                    {
                        // Allow Trades / Refuse Trades
                        list.Add(new CallbackEntry(RefuseTrades ? 1154112 : 1154113, ToggleTrades));
                    }
                }
            }
            else
            {
                if (Core.TOL && from.InRange(this, 2))
                {
                    list.Add(new CallbackEntry(1077728, () => OpenTrade(from))); // Trade
                }

                if (Alive && Core.Expansion >= Expansion.AOS)
                {
                    var theirParty = from.Party as Party;
                    var ourParty = Party as Party;

                    if (theirParty == null && ourParty == null)
                    {
                        list.Add(new AddToPartyEntry());
                    }
                    else if (theirParty != null && theirParty.Leader == from)
                    {
                        if (ourParty == null)
                        {
                            list.Add(new AddToPartyEntry());
                        }
                        else if (ourParty == theirParty)
                        {
                            list.Add(new RemoveFromPartyEntry());
                        }
                    }
                }

                var curhouse = BaseHouse.FindHouseAt(this);

                if (curhouse != null && Alive && Core.Expansion >= Expansion.AOS && curhouse.IsAosRules &&
                    curhouse.IsFriend(from))
                {
                    list.Add(new EjectPlayerEntry());
                }
            }
        }

        private void ToggleTrades()
        {
            RefuseTrades = !RefuseTrades;
        }

        private void GetVendor()
        {
            var house = BaseHouse.FindHouseAt(this);

            if (CheckAlive() && house?.IsOwner(this) == true && house.InternalizedVendors.Count > 0 && NetState is { } ns)
            {
                ns.SendGump(new ReclaimVendorGump(house));
            }
        }

        private void LeaveHouse()
        {
            var house = BaseHouse.FindHouseAt(this);

            if (house != null)
            {
                Location = house.BanLocation;
            }
        }

        public override void DisruptiveAction()
        {
            if (Meditating)
            {
                RemoveBuff(BuffIcon.ActiveMeditation);
            }

            base.DisruptiveAction();
        }

        public override void OnDoubleClick(Mobile from)
        {
            if (this == from && !Warmode)
            {
                var mount = Mount;

                if (mount != null && !DesignContext.Check(this))
                {
                    return;
                }
            }

            base.OnDoubleClick(from);
        }

        public override void DisplayPaperdollTo(Mobile to)
        {
            if (DesignContext.Check(this))
            {
                base.DisplayPaperdollTo(to);
            }
        }

        public override bool CheckEquip(Item item)
        {
            if (!base.CheckEquip(item))
            {
                return false;
            }

            if (AccessLevel < AccessLevel.GameMaster && item.Layer != Layer.Mount && HasTrade)
            {
                var bounce = item.GetBounce();

                if (bounce != null)
                {
                    if (bounce.Parent is Item parent)
                    {
                        if (parent == Backpack || parent.IsChildOf(Backpack))
                        {
                            return true;
                        }
                    }
                    else if (bounce.Parent == this)
                    {
                        return true;
                    }
                }

                SendLocalizedMessage(
                    1004042
                ); // You can only equip what you are already carrying while you have a trade pending.
                return false;
            }

            return true;
        }

        public override bool CheckTrade(
            Mobile to, Item item, SecureTradeContainer cont, bool message, bool checkItems,
            int plusItems, int plusWeight
        )
        {
            var msgNum = 0;

            if (cont == null)
            {
                if (to.Holding != null)
                {
                    msgNum = 1062727; // You cannot trade with someone who is dragging something.
                }
                else if (HasTrade)
                {
                    msgNum = 1062781; // You are already trading with someone else!
                }
                else if (to.HasTrade)
                {
                    msgNum = 1062779; // That person is already involved in a trade
                }
                else if (to is PlayerMobile mobile && mobile.RefuseTrades)
                {
                    msgNum = 1154111; // ~1_NAME~ is refusing all trades.
                }
            }

            if (msgNum == 0 && item != null)
            {
                if (cont != null)
                {
                    plusItems += cont.TotalItems;
                    plusWeight += cont.TotalWeight;
                }

                if (Backpack?.CheckHold(this, item, false, checkItems, plusItems, plusWeight) != true)
                {
                    msgNum = 1004040; // You would not be able to hold this if the trade failed.
                }
                else if (to.Backpack?.CheckHold(to, item, false, checkItems, plusItems, plusWeight) != true)
                {
                    msgNum = 1004039; // The recipient of this trade would not be able to carry this.
                }
                else
                {
                    msgNum = CheckContentForTrade(item);
                }
            }

            if (msgNum != 0)
            {
                if (message)
                {
                    if (msgNum == 1154111)
                    {
                        SendLocalizedMessage(msgNum, to.Name);
                    }
                    else
                    {
                        SendLocalizedMessage(msgNum);
                    }
                }

                return false;
            }

            return true;
        }

        private static int CheckContentForTrade(Item item)
        {
            if (item is TrappableContainer container && container.TrapType != TrapType.None)
            {
                return 1004044; // You may not trade trapped items.
            }

            if (StolenItem.IsStolen(item))
            {
                return 1004043; // You may not trade recently stolen items.
            }

            if (item is Container)
            {
                foreach (var subItem in item.Items)
                {
                    var msg = CheckContentForTrade(subItem);

                    if (msg != 0)
                    {
                        return msg;
                    }
                }
            }

            return 0;
        }

        public override bool CheckNonlocalDrop(Mobile from, Item item, Item target)
        {
            if (!base.CheckNonlocalDrop(from, item, target))
            {
                return false;
            }

            if (from.AccessLevel >= AccessLevel.GameMaster)
            {
                return true;
            }

            var pack = Backpack;
            if (from == this && HasTrade && (target == pack || target.IsChildOf(pack)))
            {
                var bounce = item.GetBounce();

                if (bounce?.Parent is Item parent && (parent == pack || parent.IsChildOf(pack)))
                {
                    return true;
                }

                SendLocalizedMessage(1004041); // You can't do that while you have a trade pending.
                return false;
            }

            return true;
        }

        protected override void OnLocationChange(Point3D oldLocation)
        {
            CheckLightLevels(false);

            var context = DesignContext;

            if (context == null || m_NoRecursion)
            {
                return;
            }

            m_NoRecursion = true;

            var foundation = context.Foundation;

            int newX = X, newY = Y;
            var newZ = foundation.Z + HouseFoundation.GetLevelZ(context.Level, context.Foundation);

            var startX = foundation.X + foundation.Components.Min.X + 1;
            var startY = foundation.Y + foundation.Components.Min.Y + 1;
            var endX = startX + foundation.Components.Width - 1;
            var endY = startY + foundation.Components.Height - 2;

            if (newX >= startX && newY >= startY && newX < endX && newY < endY && Map == foundation.Map)
            {
                if (Z != newZ)
                {
                    Location = new Point3D(X, Y, newZ);
                }

                m_NoRecursion = false;
                return;
            }

            Location = new Point3D(foundation.X, foundation.Y, newZ);
            Map = foundation.Map;

            m_NoRecursion = false;
        }

        public override bool OnMoveOver(Mobile m) =>
            m is BaseCreature creature && !creature.Controlled
                ? !Alive || !creature.Alive || IsDeadBondedPet || creature.IsDeadBondedPet ||
                  Hidden && AccessLevel > AccessLevel.Player
                : base.OnMoveOver(m);

        public override bool CheckShove(Mobile shoved) =>
            IgnoreMobiles || shoved.IgnoreMobiles || base.CheckShove(shoved);

        protected override void OnMapChange(Map oldMap)
        {
            var context = DesignContext;

            if (context == null || m_NoRecursion)
            {
                return;
            }

            m_NoRecursion = true;

            var foundation = context.Foundation;

            if (Map != foundation.Map)
            {
                Map = foundation.Map;
            }

            m_NoRecursion = false;
        }

        public override void OnDamage(int amount, Mobile from, bool willKill)
        {
            int disruptThreshold;

            if (!Core.AOS)
            {
                disruptThreshold = 0;
            }
            else if (from?.Player == true)
            {
                disruptThreshold = 18;
            }
            else
            {
                disruptThreshold = 25;
            }

            if (amount > disruptThreshold)
            {
                var c = BandageContext.GetContext(this);

                c?.Slip();
            }

            StaminaSystem.FatigueOnDamage(this, amount);

            if (willKill && from is PlayerMobile mobile)
            {
                Timer.StartTimer(TimeSpan.FromSeconds(10), mobile.RecoverAmmo);
            }

            base.OnDamage(amount, from, willKill);
        }

        public override void Resurrect()
        {
            var wasAlive = Alive;

            base.Resurrect();

            if (Alive && !wasAlive)
            {
                Item deathRobe = new DeathRobe();

                if (!EquipItem(deathRobe))
                {
                    deathRobe.Delete();
                }
            }
        }

        public override void OnWarmodeChanged()
        {
            if (!Warmode)
            {
                Timer.StartTimer(TimeSpan.FromSeconds(10), RecoverAmmo);
            }
        }

        private bool FindItems_Callback(Item item) =>
            !item.Deleted && (item.LootType == LootType.Blessed || item.Insured) &&
            Backpack != item.Parent;

        public override bool OnBeforeDeath()
        {
            var state = NetState;

            state?.CancelAllTrades();

            DropHolding();

            // During AOS+, insured/blessed items are moved out of their child containers and put directly into the backpack.
            // This fixes a "bug" where players put blessed items in nested bags and they were dropped on death
            if (Core.AOS && Backpack?.Deleted == false)
            {
                foreach (var item in Backpack.EnumerateItems(true, FindItems_Callback))
                {
                    Backpack.AddItem(item);
                }
            }

            EquipSnapshot = new List<Item>(Items);

            m_NonAutoreinsuredItems = 0;
            m_InsuranceAward = FindMostRecentDamager(false);

            if (m_InsuranceAward is BaseCreature creature)
            {
                var master = creature.GetMaster();

                if (master != null)
                {
                    m_InsuranceAward = master;
                }
            }

            if (m_InsuranceAward != null && (!m_InsuranceAward.Player || m_InsuranceAward == this))
            {
                m_InsuranceAward = null;
            }

            if (m_InsuranceAward is PlayerMobile mobile)
            {
                mobile.m_InsuranceBonus = 0;
            }

            RecoverAmmo();

            return base.OnBeforeDeath();
        }

        private bool CheckInsuranceOnDeath(Item item)
        {
            if (!InsuranceEnabled || !item.Insured)
            {
                return false;
            }

            if (AutoRenewInsurance)
            {
                var cost = GetInsuranceCost(item);

                if (m_InsuranceAward != null)
                {
                    cost /= 2;
                }

                if (Banker.Withdraw(this, cost))
                {
                    item.PaidInsurance = true;
                    // ~1_AMOUNT~ gold has been withdrawn from your bank box.
                    SendLocalizedMessage(1060398, cost.ToString());
                }
                else
                {
                    SendLocalizedMessage(1061079, "", 0x23); // You lack the funds to purchase the insurance
                    item.PaidInsurance = false;
                    item.Insured = false;
                    m_NonAutoreinsuredItems++;
                }
            }
            else
            {
                item.PaidInsurance = false;
                item.Insured = false;
            }

            if (m_InsuranceAward is PlayerMobile insurancePm && Banker.Deposit(m_InsuranceAward, 300))
            {
                insurancePm.m_InsuranceBonus += 300;
            }

            return true;
        }

        public override DeathMoveResult GetParentMoveResultFor(Item item)
        {
            // It seems all items are unmarked on death, even blessed/insured ones
            if (item.QuestItem)
            {
                item.QuestItem = false;
            }

            if (CheckInsuranceOnDeath(item))
            {
                return DeathMoveResult.MoveToBackpack;
            }

            var res = base.GetParentMoveResultFor(item);

            if (res == DeathMoveResult.MoveToCorpse && item.Movable && Young)
            {
                res = DeathMoveResult.MoveToBackpack;
            }

            return res;
        }

        public override DeathMoveResult GetInventoryMoveResultFor(Item item)
        {
            // It seems all items are unmarked on death, even blessed/insured ones
            if (item.QuestItem)
            {
                item.QuestItem = false;
            }

            if (CheckInsuranceOnDeath(item))
            {
                return DeathMoveResult.MoveToBackpack;
            }

            var res = base.GetInventoryMoveResultFor(item);

            if (res == DeathMoveResult.MoveToCorpse && item.Movable && Young)
            {
                res = DeathMoveResult.MoveToBackpack;
            }

            return res;
        }

        [GeneratedEvent(nameof(PlayerDeathEvent))]
        public static partial void PlayerDeathEvent(PlayerMobile m);

        public override void OnDeath(Container c)
        {
            if (m_NonAutoreinsuredItems > 0)
            {
                SendLocalizedMessage(1061115);
            }

            base.OnDeath(c);

            EquipSnapshot = null;

            HueMod = -1;
            NameMod = null;
            SavagePaintExpiration = TimeSpan.Zero;
            SetHairMods(-1, -1);

            if (Flying)
            {
                Flying = false;
                RemoveBuff(BuffIcon.Fly);
            }

            if (PermaFlags.Count > 0)
            {
                PermaFlags.Clear();

                if (c is Corpse corpse)
                {
                    corpse.Criminal = true;
                }

                if (Stealing.ClassicMode)
                {
                    Criminal = true;
                }
            }

            if (m_InsuranceAward is PlayerMobile insurancePm && insurancePm.m_InsuranceBonus > 0)
            {
                // ~1_AMOUNT~ gold has been deposited into your bank box.
                insurancePm.SendLocalizedMessage(1060397, insurancePm.m_InsuranceBonus.ToString());
            }

            var killer = FindMostRecentDamager(true);

            if (killer is BaseCreature bcKiller)
            {
                var master = bcKiller.GetMaster();
                if (master != null)
                {
                    killer = master;
                }
            }

            if (m_BuffTable != null)
            {
                using var queue = PooledRefQueue<BuffIcon>.Create();

                foreach (var buff in m_BuffTable.Values)
                {
                    if (!buff.RetainThroughDeath)
                    {
                        queue.Enqueue(buff.ID);
                    }
                }

                while (queue.Count > 0)
                {
                    RemoveBuff(queue.Dequeue());
                }
            }

            PlayerDeathEvent(this);
        }

        public override bool MutateSpeech(List<Mobile> hears, ref string text, ref object context)
        {
            if (Alive)
            {
                return false;
            }

            if (Core.ML && Skills.SpiritSpeak.Value >= 100.0)
            {
                return false;
            }

            if (Core.AOS)
            {
                for (var i = 0; i < hears.Count; ++i)
                {
                    var m = hears[i];

                    if (m != this && m.Skills.SpiritSpeak.Value >= 100.0)
                    {
                        return false;
                    }
                }
            }

            return base.MutateSpeech(hears, ref text, ref context);
        }

        private static void SendToStaffMessage(Mobile from, string text)
        {
            Span<byte> buffer = stackalloc byte[OutgoingMessagePackets.GetMaxMessageLength(text)].InitializePacket();

            foreach (var ns in from.GetClientsInRange(8))
            {
                var mob = ns.Mobile;

                if (mob?.AccessLevel >= AccessLevel.GameMaster && mob.AccessLevel > from.AccessLevel)
                {
                    var length = OutgoingMessagePackets.CreateMessage(
                        buffer,
                        from.Serial,
                        from.Body,
                        MessageType.Regular,
                        from.SpeechHue,
                        3,
                        false,
                        from.Language,
                        from.Name,
                        text
                    );

                    if (length != buffer.Length)
                    {
                        buffer = buffer[..length]; // Adjust to the actual size
                    }

                    ns.Send(buffer);
                }
            }
        }

        public override bool IsHarmfulCriminal(Mobile target)
        {
            if (Stealing.ClassicMode && target is PlayerMobile mobile && mobile.PermaFlags.Count > 0)
            {
                if (Notoriety.Compute(this, mobile) == Notoriety.Innocent)
                {
                    mobile.Delta(MobileDelta.Noto);
                }

                return false;
            }

            var bc = target as BaseCreature;

            if (bc?.InitialInnocent == true && !bc.Controlled)
            {
                return false;
            }

            if (Core.ML && bc?.Controlled == true && this == bc.ControlMaster)
            {
                return false;
            }

            return base.IsHarmfulCriminal(target);
        }

        private void RevertHair()
        {
            SetHairMods(-1, -1);
        }

        public override void Deserialize(IGenericReader reader)
        {
            base.Deserialize(reader);
            var version = reader.ReadInt();

            switch (version)
            {
                case 34: // Acquired Recipes is now a Set
                case 33: // Removes champion title
                case 32: // Removes virtue properties
                case 31: // Removed Short/Long Term Elapse
                case 30:
                    {
                        Stabled = reader.ReadEntitySet<Mobile>(true);
                        goto case 29;
                    }
                case 29:
                    {
                        if (reader.ReadBool())
                        {
                            m_StuckMenuUses = new DateTime[reader.ReadInt()];

                            for (var i = 0; i < m_StuckMenuUses.Length; ++i)
                            {
                                m_StuckMenuUses[i] = reader.ReadDateTime();
                            }
                        }
                        else
                        {
                            m_StuckMenuUses = null;
                        }

                        goto case 28;
                    }
                case 28:
                    {
                        PeacedUntil = reader.ReadDateTime();

                        goto case 27;
                    }
                case 27:
                    {
                        AnkhNextUse = reader.ReadDateTime();

                        goto case 26;
                    }
                case 26:
                    {
                        AutoStabled = reader.ReadEntitySet<Mobile>(true);

                        goto case 25;
                    }
                case 25:
                    {
                        var recipeCount = reader.ReadInt();

                        if (recipeCount > 0)
                        {
                            _acquiredRecipes = new HashSet<int>();

                            for (var i = 0; i < recipeCount; i++)
                            {
                                var r = reader.ReadInt();
                                if (version > 33 || reader.ReadBool()) // Don't add in recipes which we haven't gotten or have been removed
                                {
                                    _acquiredRecipes.Add(r);
                                }
                            }
                        }

                        goto case 24;
                    }
                case 24:
                    {
                        if (version < 32)
                        {
                            reader.ReadDeltaTime(); // LastHonorLoss - Not even used
                        }
                        goto case 23;
                    }
                case 23:
                    {
                        goto case 22;
                    }
                case 22:
                    {
                        goto case 21;
                    }
                case 21:
                    {
                        ToTItemsTurnedIn = reader.ReadEncodedInt();
                        ToTTotalMonsterFame = reader.ReadInt();
                        goto case 20;
                    }
                case 20:
                    {
                        AllianceMessageHue = reader.ReadEncodedInt();
                        GuildMessageHue = reader.ReadEncodedInt();

                        goto case 19;
                    }
                case 19:
                    {
                        LastOnline = reader.ReadDateTime();
                        goto case 18;
                    }
                case 18:
                    {
                        SolenFriendship = (SolenFriendship)reader.ReadEncodedInt();

                        goto case 17;
                    }
                case 17: // changed how DoneQuests is serialized
                case 16:
                    {
                        Quest = QuestSerializer.DeserializeQuest(reader);

                        if (Quest != null)
                        {
                            Quest.From = this;
                        }

                        var count = reader.ReadEncodedInt();

                        if (count > 0)
                        {
                            DoneQuests = new List<QuestRestartInfo>();

                            for (var i = 0; i < count; ++i)
                            {
                                var questType = QuestSerializer.ReadType(QuestSystem.QuestTypes, reader);
                                DateTime restartTime;

                                if (version < 17)
                                {
                                    restartTime = DateTime.MaxValue;
                                }
                                else
                                {
                                    restartTime = reader.ReadDateTime();
                                }

                                DoneQuests.Add(new QuestRestartInfo(questType, restartTime));
                            }
                        }

                        Profession = reader.ReadEncodedInt();
                        goto case 15;
                    }
                case 15:
                    {
                        goto case 14;
                    }
                case 14:
                    {
                        goto case 13;
                    }
                case 13: // just removed m_PaidInsurance list
                case 12:
                    {
                        goto case 11;
                    }
                case 11:
                    {
                        if (version < 13)
                        {
                            var paid = reader.ReadEntityList<Item>();

                            for (var i = 0; i < paid.Count; ++i)
                            {
                                paid[i].PaidInsurance = true;
                            }
                        }

                        goto case 10;
                    }
                case 10:
                    {
                        if (reader.ReadBool())
                        {
                            m_HairModID = reader.ReadInt();
                            m_HairModHue = reader.ReadInt();
                            m_BeardModID = reader.ReadInt();
                            m_BeardModHue = reader.ReadInt();
                        }

                        goto case 9;
                    }
                case 9:
                    {
                        SavagePaintExpiration = reader.ReadTimeSpan();

                        if (SavagePaintExpiration > TimeSpan.Zero)
                        {
                            BodyMod = Female ? 184 : 183;
                            HueMod = 0;
                        }

                        goto case 8;
                    }
                case 8:
                    {
                        NpcGuild = (NpcGuild)reader.ReadInt();
                        NpcGuildJoinTime = reader.ReadDateTime();
                        NpcGuildGameTime = reader.ReadTimeSpan();
                        goto case 7;
                    }
                case 7:
                    {
                        PermaFlags = reader.ReadEntityList<Mobile>();
                        goto case 6;
                    }
                case 6:
                    {
                        NextTailorBulkOrder = reader.ReadTimeSpan();
                        goto case 5;
                    }
                case 5:
                    {
                        NextSmithBulkOrder = reader.ReadTimeSpan();
                        goto case 4;
                    }
                case 4:
                    {
                        goto case 3;
                    }
                case 3:
                    {
                        goto case 2;
                    }
                case 2:
                    {
                        Flags = (PlayerFlag)reader.ReadInt();
                        goto case 1;
                    }
                case 1:
                    {
                        if (version < 31)
                        {
                            var longTermElapse = reader.ReadTimeSpan();
                            var shortTermElapse = reader.ReadTimeSpan();

                            PlayerMurderSystem.MigrateContext(this, shortTermElapse, longTermElapse);
                        }

                        m_GameTime = reader.ReadTimeSpan();
                        goto case 0;
                    }
                case 0:
                    {
                        break;
                    }
            }

            if (!ProfessionInfo.VerifyProfession(Profession))
            {
                Profession = 0;
            }

            PermaFlags ??= new List<Mobile>();

            if (LastOnline == DateTime.MinValue && Account != null)
            {
                LastOnline = ((Account)Account).LastLogin;
            }

            if (AccessLevel > AccessLevel.Player)
            {
                IgnoreMobiles = true;
            }

            if (Stabled != null)
            {
                foreach (var stabled in Stabled)
                {
                    if (stabled is BaseCreature bc)
                    {
                        bc.IsStabled = true;
                        bc.StabledBy = this;
                    }
                }
            }

            if (Hidden) // Hiding is the only buff where it has an effect that's serialized.
            {
                AddBuff(new BuffInfo(BuffIcon.HidingAndOrStealth, 1075655));
            }
        }

        public override void Serialize(IGenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write(34); // version

            if (Stabled == null)
            {
                writer.Write(0);
            }
            else
            {
                Stabled.Tidy();
                writer.Write(Stabled);
            }

            if (m_StuckMenuUses != null)
            {
                writer.Write(true);

                writer.Write(m_StuckMenuUses.Length);

                for (var i = 0; i < m_StuckMenuUses.Length; ++i)
                {
                    writer.Write(m_StuckMenuUses[i]);
                }
            }
            else
            {
                writer.Write(false);
            }

            writer.Write(PeacedUntil);
            writer.Write(AnkhNextUse);
            if (AutoStabled == null)
            {
                writer.Write(0);
            }
            else
            {
                AutoStabled.Tidy();
                writer.Write(AutoStabled);
            }

            if (_acquiredRecipes == null)
            {
                writer.Write(0);
            }
            else
            {
                writer.Write(_acquiredRecipes.Count);

                foreach (var recipeId in _acquiredRecipes)
                {
                    writer.Write(recipeId);
                }
            }

            writer.WriteEncodedInt(ToTItemsTurnedIn);
            writer.Write(ToTTotalMonsterFame); // This ain't going to be a small #.

            writer.WriteEncodedInt(AllianceMessageHue);
            writer.WriteEncodedInt(GuildMessageHue);

            writer.Write(LastOnline);

            writer.WriteEncodedInt((int)SolenFriendship);

            QuestSerializer.Serialize(Quest, writer);

            if (DoneQuests == null)
            {
                writer.WriteEncodedInt(0);
            }
            else
            {
                writer.WriteEncodedInt(DoneQuests.Count);

                for (var i = 0; i < DoneQuests.Count; ++i)
                {
                    var restartInfo = DoneQuests[i];

                    QuestSerializer.Write(restartInfo.QuestType, QuestSystem.QuestTypes, writer);
                    writer.Write(restartInfo.RestartTime);
                }
            }

            writer.WriteEncodedInt(Profession);

            var useMods = m_HairModID != -1 || m_BeardModID != -1;

            writer.Write(useMods);

            if (useMods)
            {
                writer.Write(m_HairModID);
                writer.Write(m_HairModHue);
                writer.Write(m_BeardModID);
                writer.Write(m_BeardModHue);
            }

            writer.Write(SavagePaintExpiration);

            writer.Write((int)NpcGuild);
            writer.Write(NpcGuildJoinTime);
            writer.Write(NpcGuildGameTime);

            PermaFlags.Tidy();
            writer.Write(PermaFlags);

            writer.Write(NextTailorBulkOrder);

            writer.Write(NextSmithBulkOrder);

            writer.Write((int)Flags);

            writer.Write(GameTime);
        }

        public override bool CanSee(Mobile m)
        {
            return m is PlayerMobile mobile && mobile.VisibilityList.Contains(this) || base.CanSee(m);
        }

        public virtual void CheckedAnimate(int action, int frameCount, int repeatCount, bool forward, bool repeat, int delay)
        {
            if (!Mounted)
            {
                Animate(action, frameCount, repeatCount, forward, repeat, delay);
            }
        }

        public override bool CanSee(Item item) =>
            DesignContext?.Foundation.IsHiddenToCustomizer(item) != true && base.CanSee(item);

        public override void OnAfterDelete()
        {
            base.OnAfterDelete();

            PlayerDeletedEvent(this);
        }

        public override void GetProperties(IPropertyList list)
        {
            base.GetProperties(list);

            if (!Core.ML || AllFollowers == null)
            {
                return;
            }

            foreach (var follower in AllFollowers)
            {
                if (follower is BaseCreature { ControlOrder: OrderType.Guard })
                {
                    list.Add(501129); // guarded
                    break;
                }
            }
        }

        protected override bool OnMove(Direction d)
        {
            if (!Core.SE)
            {
                return base.OnMove(d);
            }

            if (AccessLevel != AccessLevel.Player)
            {
                return true;
            }

            if (Hidden && DesignContext.Find(this) == null) // Hidden & NOT customizing a house
            {
                if (!Mounted && Skills.Stealth.Value >= 25.0)
                {
                    var running = (d & Direction.Running) != 0;

                    if (running)
                    {
                        if ((AllowedStealthSteps -= 2) <= 0)
                        {
                            RevealingAction();
                        }
                    }
                    else if (AllowedStealthSteps-- <= 0)
                    {
                        Stealth.OnUse(this);
                    }
                }
                else
                {
                    RevealingAction();
                }
            }

            return true;
        }

        public void AddFollower(Mobile m)
        {
            _allFollowers ??= new HashSet<Mobile>();
            _allFollowers.Add(m);
        }

        public void AddStabled(Mobile m)
        {
            Stabled ??= new HashSet<Mobile>();
            Stabled.Add(m);
        }

        public bool RemoveStabled(Mobile m)
        {
            if (Stabled?.Remove(m) == true)
            {
                if (Stabled.Count == 0)
                {
                    Stabled = null;
                }

                return true;
            }

            return false;
        }

        public bool RemoveFollower(Mobile m)
        {
            if (_allFollowers?.Remove(m) == true)
            {
                if (_allFollowers.Count == 0)
                {
                    _allFollowers = null;
                }

                return true;
            }

            return false;
        }

        public void AutoStablePets()
        {
            var allFollowers = _allFollowers;

            if (!Core.SE || !(allFollowers?.Count > 0))
            {
                return;
            }

            foreach (var follower in allFollowers)
            {
                if (follower is not BaseCreature pet || pet.ControlMaster == null)
                {
                    continue;
                }

                if (pet.Summoned)
                {
                    if (pet.Map != Map)
                    {
                        pet.PlaySound(pet.GetAngerSound());
                        Timer.StartTimer(pet.Delete);
                    }

                    continue;
                }

                if ((pet as IMount)?.Rider != null)
                {
                    continue;
                }

                if (pet is PackLlama or PackHorse or Beetle && pet.Backpack?.Items.Count > 0)
                {
                    continue;
                }

                if (pet is BaseEscortable)
                {
                    continue;
                }

                pet.ControlTarget = null;
                pet.ControlOrder = OrderType.Stay;
                pet.Internalize();

                pet.SetControlMaster(null);
                pet.SummonMaster = null;

                pet.IsStabled = true;
                pet.StabledBy = this;

                pet.Loyalty = BaseCreature.MaxLoyalty; // Wonderfully happy

                Stabled ??= new HashSet<Mobile>();
                Stabled.Add(pet);

                AutoStabled ??= new HashSet<Mobile>();
                AutoStabled.Add(pet);
            }
        }

        public void ClaimAutoStabledPets()
        {
            if (!Core.SE || !(AutoStabled?.Count > 0))
            {
                return;
            }

            if (!Alive)
            {
                // Your pet was unable to join you while you are a ghost.  Please re-login once you have ressurected to claim your pets.
                SendLocalizedMessage(1076251);
                return;
            }

            foreach (var stabled in AutoStabled)
            {
                if (stabled is not BaseCreature pet)
                {
                    continue;
                }

                if (pet.Deleted)
                {
                    pet.IsStabled = false;
                    pet.StabledBy = null;

                    Stabled?.Remove(pet);
                    continue;
                }

                if (Followers + pet.ControlSlots <= FollowersMax)
                {
                    pet.SetControlMaster(this);

                    if (pet.Summoned)
                    {
                        pet.SummonMaster = this;
                    }

                    pet.ControlTarget = this;
                    pet.ControlOrder = OrderType.Follow;

                    pet.MoveToWorld(Location, Map);

                    pet.IsStabled = false;
                    pet.StabledBy = null;

                    pet.Loyalty = BaseCreature.MaxLoyalty; // Wonderfully Happy

                    Stabled?.Remove(pet);
                }
                else
                {
                    // ~1_NAME~ remained in the stables because you have too many followers.
                    SendLocalizedMessage(1049612, pet.Name);
                }
            }

            AutoStabled = null;
        }

        public void RecoverAmmo()
        {
            if (!Core.SE || !Alive || RecoverableAmmo == null)
            {
                return;
            }

            foreach (var kvp in RecoverableAmmo)
            {
                if (kvp.Value > 0)
                {
                    Item ammo = null;

                    try
                    {
                        ammo = kvp.Key.CreateInstance<Item>();
                    }
                    catch
                    {
                        // ignored
                    }

                    if (ammo == null)
                    {
                        continue;
                    }

                    ammo.Amount = kvp.Value;

                    var name = ammo.Name ?? ammo switch
                    {
                        Arrow _ => $"arrow{(ammo.Amount != 1 ? "s" : "")}",
                        Bolt _ => $"bolt{(ammo.Amount != 1 ? "s" : "")}",
                        _ => $"#{ammo.LabelNumber}"
                    };

                    PlaceInBackpack(ammo);
                    SendLocalizedMessage(1073504, $"{ammo.Amount}\t{name}"); // You recover ~1_NUM~ ~2_AMMO~.
                }
            }

            RecoverableAmmo.Clear();
        }

        private static int GetInsuranceCost(Item item) => 600;

        private void ToggleItemInsurance()
        {
            if (!CheckAlive())
            {
                return;
            }

            BeginTarget(-1, false, TargetFlags.None, ToggleItemInsurance_Callback);
            SendLocalizedMessage(1060868); // Target the item you wish to toggle insurance status on <ESC> to cancel
        }

        private bool CanInsure(Item item)
        {
            if (item is Container && item is not BaseQuiver || item is BagOfSending or KeyRing or PotionKeg)
            {
                return false;
            }

            if (item.Stackable)
            {
                return false;
            }

            if (item.LootType == LootType.Cursed)
            {
                return false;
            }

            if (item.ItemID == 0x204E) // death shroud
            {
                return false;
            }

            if (item.Layer == Layer.Mount)
            {
                return false;
            }

            return item.LootType != LootType.Blessed && item.LootType != LootType.Newbied && item.BlessedFor != this;
        }

        private void ToggleItemInsurance_Callback(Mobile from, object obj)
        {
            if (!CheckAlive())
            {
                return;
            }

            ToggleItemInsurance_Callback(from, obj as Item, true);
        }

        private void ToggleItemInsurance_Callback(Mobile from, Item item, bool target)
        {
            if (item?.IsChildOf(this) != true)
            {
                if (target)
                {
                    BeginTarget(-1, false, TargetFlags.None, ToggleItemInsurance_Callback);
                }

                SendLocalizedMessage(
                    1060871,
                    "",
                    0x23
                ); // You can only insure items that you have equipped or that are in your backpack
            }
            else if (item.Insured)
            {
                item.Insured = false;

                SendLocalizedMessage(1060874, "", 0x35); // You cancel the insurance on the item

                if (target)
                {
                    BeginTarget(-1, false, TargetFlags.None, ToggleItemInsurance_Callback);
                    SendLocalizedMessage(
                        1060868,
                        "",
                        0x23
                    ); // Target the item you wish to toggle insurance status on <ESC> to cancel
                }
            }
            else if (!CanInsure(item))
            {
                if (target)
                {
                    BeginTarget(-1, false, TargetFlags.None, ToggleItemInsurance_Callback);
                }

                SendLocalizedMessage(1060869, "", 0x23); // You cannot insure that
            }
            else
            {
                if (!item.PaidInsurance)
                {
                    var cost = GetInsuranceCost(item);

                    if (Banker.Withdraw(from, cost))
                    {
                        SendLocalizedMessage(
                            1060398,
                            cost.ToString()
                        ); // ~1_AMOUNT~ gold has been withdrawn from your bank box.
                        item.PaidInsurance = true;
                    }
                    else
                    {
                        SendLocalizedMessage(1061079, "", 0x23); // You lack the funds to purchase the insurance
                        return;
                    }
                }

                item.Insured = true;

                SendLocalizedMessage(1060873, "", 0x23); // You have insured the item

                if (target)
                {
                    BeginTarget(-1, false, TargetFlags.None, ToggleItemInsurance_Callback);
                    SendLocalizedMessage(
                        1060868,
                        "",
                        0x23
                    ); // Target the item you wish to toggle insurance status on <ESC> to cancel
                }
            }
        }

        private void AutoRenewInventoryInsurance()
        {
            if (!CheckAlive())
            {
                return;
            }

            // You have selected to automatically reinsure all insured items upon death
            SendLocalizedMessage(1060881, "", 0x23);
            AutoRenewInsurance = true;
        }

        private void CancelRenewInventoryInsurance()
        {
            if (!CheckAlive())
            {
                return;
            }

            if (Core.SE)
            {
                NetState?.SendGump(new CancelRenewInventoryInsuranceGump(null));
            }
            else
            {
                // You have cancelled automatically reinsuring all insured items upon death
                SendLocalizedMessage(1061075, "", 0x23);
                AutoRenewInsurance = false;
            }
        }

        private void OpenItemInsuranceMenu()
        {
            if (!CheckAlive())
            {
                return;
            }

            using var queue = PooledRefQueue<Item>.Create(128);

            foreach (var item in Items)
            {
                if (DisplayInItemInsuranceGump(item))
                {
                    queue.Enqueue(item);
                }
            }

            var pack = Backpack;

            if (pack != null)
            {
                foreach (var item in pack.FindItems())
                {
                    if (DisplayInItemInsuranceGump(item))
                    {
                        queue.Enqueue(item);
                    }
                }
            }

            // TODO: Investigate item sorting
            if (NetState != null)
            {
                if (queue.Count == 0)
                {
                    SendLocalizedMessage(1114915, "", 0x35); // None of your current items meet the requirements for insurance.
                }
                else
                {
                    NetState.SendGump(new ItemInsuranceMenuGump(this, queue.ToArray()));
                }
            }
        }

        private bool DisplayInItemInsuranceGump(Item item) => (item.Visible || AccessLevel >= AccessLevel.GameMaster) &&
                                                              (item.Insured || CanInsure(item));

        private void ToggleQuestItem()
        {
            if (!CheckAlive())
            {
                return;
            }

            ToggleQuestItemTarget();
        }

        private void ToggleQuestItemTarget()
        {
            if (NetState != null)
            {
                var gumps = this.GetGumps();
                gumps.Close<QuestOfferGump>();
            }

            BeginTarget(-1, false, TargetFlags.None, ToggleQuestItem_Callback);
            SendLocalizedMessage(1072352); // Target the item you wish to toggle Quest Item status on <ESC> to cancel
        }

        private void ToggleQuestItem_Callback(Mobile from, object obj)
        {
            if (!CheckAlive())
            {
                return;
            }

            if (obj is not Item item)
            {
                return;
            }

            if (from.Backpack == null || item.Parent != from.Backpack)
            {
                // An item must be in your backpack (and not in a container within) to be toggled as a quest item.
                SendLocalizedMessage(1074769);
            }
            else if (item.QuestItem)
            {
                item.QuestItem = false;
                SendLocalizedMessage(1072354); // You remove Quest Item status from the item
            }
            else
            {
                // That item does not match any of your quest criteria
                SendLocalizedMessage(1072355, "", 0x23);
            }

            ToggleQuestItemTarget();
        }

        public bool CanUseStuckMenu()
        {
            if (m_StuckMenuUses == null)
            {
                return true;
            }

            for (var i = 0; i < m_StuckMenuUses.Length; ++i)
            {
                if (Core.Now - m_StuckMenuUses[i] > TimeSpan.FromDays(1.0))
                {
                    return true;
                }
            }

            return false;
        }

        public void UsedStuckMenu()
        {
            if (m_StuckMenuUses == null)
            {
                m_StuckMenuUses = new DateTime[2];
            }

            for (var i = 0; i < m_StuckMenuUses.Length; ++i)
            {
                if (Core.Now - m_StuckMenuUses[i] > TimeSpan.FromDays(1.0))
                {
                    m_StuckMenuUses[i] = Core.Now;
                    return;
                }
            }
        }

        public override ApplyPoisonResult ApplyPoison(Mobile from, Poison poison)
        {
            if (!Alive)
            {
                return ApplyPoisonResult.Immune;
            }

            var result = base.ApplyPoison(from, poison);

            if (from != null && result == ApplyPoisonResult.Poisoned && PoisonTimer is PoisonImpl.PoisonTimer timer)
            {
                timer.From = from;
            }

            return result;
        }

        public override bool CheckPoisonImmunity(Mobile from, Poison poison) =>
            Young || base.CheckPoisonImmunity(from, poison);

        public override void OnPoisonImmunity(Mobile from, Poison poison)
        {
            if (Young)
            {
                // You would have been poisoned, were you not new to the land of Britannia.
                // Be careful in the future.
                SendLocalizedMessage(502808);
            }
            else
            {
                base.OnPoisonImmunity(from, poison);
            }
        }

        public override void OnKillsChange(int oldValue)
        {
            if (Young && Kills > oldValue)
            {
                ((Account)Account)?.RemoveYoungStatus(0);
            }
        }

        public override void OnGenderChanged(bool oldFemale)
        {
        }

        public override void OnGuildChange(BaseGuild oldGuild)
        {
        }

        public override void OnGuildTitleChange(string oldTitle)
        {
        }

        public override void OnKarmaChange(int oldValue)
        {
        }

        public override void OnFameChange(int oldValue)
        {
        }

        public override void OnSkillChange(SkillName skill, double oldBase)
        {
            if (Young && SkillsTotal >= 4500)
            {
                // You have successfully obtained a respectable skill level, and have outgrown your status as a young player!
                ((Account)Account)?.RemoveYoungStatus(1019036);
            }
        }

        public override void OnAccessLevelChanged(AccessLevel oldLevel)
        {
            IgnoreMobiles = AccessLevel != AccessLevel.Player;
        }

        public override void OnRawStatChange(StatType stat, int oldValue)
        {
        }

        [GeneratedEvent(nameof(PlayerDeletedEvent))]
        public static partial void PlayerDeletedEvent(PlayerMobile pm);

        public override void OnDelete()
        {
            if (Stabled == null)
            {
                return;
            }

            foreach (var stabled in Stabled)
            {
                stabled.Delete();
            }

            Stabled = null;
        }

        public override int ComputeMovementSpeed(Direction dir, bool checkTurning = true)
        {
            if (checkTurning && (dir & Direction.Mask) != (Direction & Direction.Mask))
            {
                return CalcMoves.TurnDelay; // We are NOT actually moving (just a direction change)
            }

            var running = (dir & Direction.Running) != 0;

            var onHorse = Mount != null;

            if (onHorse)
            {
                return running ? CalcMoves.RunMountDelay : CalcMoves.WalkMountDelay;
            }

            return running ? CalcMoves.RunFootDelay : CalcMoves.WalkFootDelay;
        }

        private void DeltaEnemies(Type oldType, Type newType)
        {
            foreach (var m in GetMobilesInRange(18))
            {
                var t = m.GetType();

                if (t == oldType || t == newType)
                {
                    m.NetState.SendMobileMoving(this, m);
                }
            }
        }

        public void SetHairMods(int hairID, int beardID)
        {
            if (hairID == -1)
            {
                InternalRestoreHair(true, ref m_HairModID, ref m_HairModHue);
            }
            else if (hairID != -2)
            {
                InternalChangeHair(true, hairID, ref m_HairModID, ref m_HairModHue);
            }

            if (beardID == -1)
            {
                InternalRestoreHair(false, ref m_BeardModID, ref m_BeardModHue);
            }
            else if (beardID != -2)
            {
                InternalChangeHair(false, beardID, ref m_BeardModID, ref m_BeardModHue);
            }
        }

        private void CreateHair(bool hair, int id, int hue)
        {
            if (hair)
            {
                // TODO Verification?
                HairItemID = id;
                HairHue = hue;
            }
            else
            {
                FacialHairItemID = id;
                FacialHairHue = hue;
            }
        }

        private void InternalRestoreHair(bool hair, ref int id, ref int hue)
        {
            if (id == -1)
            {
                return;
            }

            if (hair)
            {
                HairItemID = 0;
            }
            else
            {
                FacialHairItemID = 0;
            }

            // if (id != 0)
            CreateHair(hair, id, hue);

            id = -1;
            hue = 0;
        }

        private void InternalChangeHair(bool hair, int id, ref int storeID, ref int storeHue)
        {
            if (storeID == -1)
            {
                storeID = hair ? HairItemID : FacialHairItemID;
                storeHue = hair ? HairHue : FacialHairHue;
            }

            CreateHair(hair, id, 0);
        }

        public override string ApplyNameSuffix(string suffix)
        {
            if (Young)
            {
                suffix = suffix.Length == 0 ? "(Young)" : $"{suffix} (Young)";
            }

            return base.ApplyNameSuffix(suffix);
        }

        public override TimeSpan GetLogoutDelay()
        {
            if (Young || BedrollLogout || TestCenter.Enabled)
            {
                return TimeSpan.Zero;
            }

            return base.GetLogoutDelay();
        }

        public bool CheckYoungProtection(Mobile from)
        {
            if (!Young)
            {
                return false;
            }

            if (Region is BaseRegion region && !region.YoungProtected)
            {
                return false;
            }

            if (from is BaseCreature creature && creature.IgnoreYoungProtection)
            {
                return false;
            }

            if (Quest?.IgnoreYoungProtection(from) == true)
            {
                return false;
            }

            if (Core.Now - m_LastYoungMessage > TimeSpan.FromMinutes(1.0))
            {
                m_LastYoungMessage = Core.Now;
                // A monster looks at you menacingly but does not attack.
                // You would be under attack now if not for your status as a new citizen of Britannia.
                SendLocalizedMessage(1019067);
            }

            return true;
        }

        public bool CheckYoungHealTime()
        {
            if (Core.Now - m_LastYoungHeal > TimeSpan.FromMinutes(5.0))
            {
                m_LastYoungHeal = Core.Now;
                return true;
            }

            return false;
        }

        public bool YoungDeathTeleport()
        {
            if (Region.IsPartOf<JailRegion>()
                || Region.IsPartOf("Samurai start location")
                || Region.IsPartOf("Ninja start location")
                || Region.IsPartOf("Ninja cave"))
            {
                return false;
            }

            Point3D loc;
            Map map;

            var dungeon = Region.GetRegion<DungeonRegion>();
            if (dungeon != null && dungeon.Entrance != Point3D.Zero)
            {
                loc = dungeon.Entrance;
                map = dungeon.Map;
            }
            else
            {
                loc = Location;
                map = Map;
            }

            Point3D[] list;

            if (map == Map.Trammel)
            {
                list = m_TrammelDeathDestinations;
            }
            else if (map == Map.Ilshenar)
            {
                list = m_IlshenarDeathDestinations;
            }
            else if (map == Map.Malas)
            {
                list = m_MalasDeathDestinations;
            }
            else if (map == Map.Tokuno)
            {
                list = m_TokunoDeathDestinations;
            }
            else
            {
                return false;
            }

            var dest = Point3D.Zero;
            var sqDistance = int.MaxValue;

            for (var i = 0; i < list.Length; i++)
            {
                var curDest = list[i];

                var width = loc.X - curDest.X;
                var height = loc.Y - curDest.Y;
                var curSqDistance = width * width + height * height;

                if (curSqDistance < sqDistance)
                {
                    dest = curDest;
                    sqDistance = curSqDistance;
                }
            }

            MoveToWorld(dest, map);
            return true;
        }

        private void SendYoungDeathNotice()
        {
            if (NetState is { } ns)
            {
                ns.SendGump(new YoungDeathNoticeGump());
            }
        }

        public override void OnSpeech(SpeechEventArgs e)
        {
            if (SpeechLog.Enabled && NetState != null)
            {
                if (SpeechLog == null)
                {
                    SpeechLog = new SpeechLog();
                }

                SpeechLog.Add(e.Mobile, e.Speech);
            }
        }

        private void ToggleChampionTitleDisplay()
        {
            if (!CheckAlive())
            {
                return;
            }

            if (DisplayChampionTitle)
            {
                // You have chosen to hide your monster kill title.
                SendLocalizedMessage(1062419, "", 0x23);
            }
            else
            {
                // You have chosen to display your monster kill title.
                SendLocalizedMessage(1062418, "", 0x23);
            }

            DisplayChampionTitle = !DisplayChampionTitle;
            InvalidateProperties();
        }

        public void SendAddBuffPacket(BuffInfo buffInfo)
        {
            if (buffInfo == null || NetState?.BuffIcon != true)
            {
                return;
            }

            var duration = Utility.Max(buffInfo.Duration - (Core.Now - buffInfo.StartTime), TimeSpan.Zero);
            if (duration == TimeSpan.Zero)
            {
                SendAddBuffPacket(buffInfo, 0);
                return;
            }

            var roundedSeconds = Math.Round(duration.TotalSeconds);
            var offset = duration.TotalMilliseconds - roundedSeconds * TimeSpan.MillisecondsPerSecond;
            if (offset > 0)
            {
                Timer.DelayCall(TimeSpan.FromMilliseconds(offset), () =>
                    {
                        // They are still online, we still have the buff icon in the table, and it is the same buff icon
                        if (NetState != null && m_BuffTable?.GetValueOrDefault(buffInfo.ID) == buffInfo)
                        {
                            SendAddBuffPacket(buffInfo, (long)roundedSeconds);
                        }
                    }
                );
            }
            else // Round up, will be removed a little bit early by the server
            {
                SendAddBuffPacket(buffInfo, (long)roundedSeconds);
            }
        }

        private void SendAddBuffPacket(BuffInfo buffInfo, long seconds)
        {
            NetState.SendAddBuffPacket(
                Serial,
                buffInfo.ID,
                buffInfo.TitleCliloc,
                buffInfo.SecondaryCliloc,
                buffInfo.Args,
                seconds
            );
        }

        public void ResendBuffs()
        {
            if (BuffInfo.Enabled && m_BuffTable != null && NetState?.BuffIcon == true)
            {
                foreach (var info in m_BuffTable.Values)
                {
                    SendAddBuffPacket(info);
                }
            }
        }

        public void AddBuff(BuffInfo b)
        {
            if (!BuffInfo.Enabled || b == null)
            {
                return;
            }

            RemoveBuff(b.ID); // Check, stop old timer, & subsequently remove the old one.
            b.StartTimer(this);

            m_BuffTable ??= new Dictionary<BuffIcon, BuffInfo>();
            m_BuffTable.Add(b.ID, b);

            SendAddBuffPacket(b);
        }

        public void RemoveBuff(BuffIcon b)
        {
            if (m_BuffTable?.Remove(b, out var buffInfo) != true)
            {
                return;
            }

            buffInfo.StopTimer();

            if (NetState?.BuffIcon == true)
            {
                NetState.SendRemoveBuffPacket(Serial, b);
            }

            if (m_BuffTable.Count <= 0)
            {
                m_BuffTable = null;
            }
        }

        private class MountBlock
        {
            private TimerExecutionToken _timerToken;
            private BlockMountType _type;

            public MountBlock(TimeSpan duration, BlockMountType type, Mobile mobile)
            {
                _type = type;

                if (duration < TimeSpan.MaxValue)
                {
                    Timer.StartTimer(duration, () => RemoveBlock(mobile), out _timerToken);
                }
            }

            public DateTime Expiration => _timerToken.Next;

            public BlockMountType MountBlockReason => CheckBlock() ? _type : BlockMountType.None;

            public bool CheckBlock() => _timerToken.Next == DateTime.MinValue || _timerToken.Running;

            public void RemoveBlock(Mobile mobile)
            {
                if (mobile is PlayerMobile pm)
                {
                    pm._mountBlock = null;
                }

                _timerToken.Cancel();
            }
        }

        private delegate void ContextCallback();

        private class CallbackEntry : ContextMenuEntry
        {
            private readonly ContextCallback m_Callback;

            public CallbackEntry(int number, ContextCallback callback) : this(number, -1, callback)
            {
            }

            public CallbackEntry(int number, int range, ContextCallback callback) : base(number, range) =>
                m_Callback = callback;

            public override void OnClick(Mobile from, IEntity target)
            {
                m_Callback?.Invoke();
            }
        }

        private class CancelRenewInventoryInsuranceGump : StaticGump<CancelRenewInventoryInsuranceGump>
        {
            private readonly ItemInsuranceMenuGump _insuranceGump;

            public override bool Singleton => true;

            public CancelRenewInventoryInsuranceGump(ItemInsuranceMenuGump insuranceGump) : base(250, 200) =>
                _insuranceGump = insuranceGump;

            protected override void BuildLayout(ref StaticGumpBuilder builder)
            {
                builder.AddBackground(0, 0, 240, 142, 0x13BE);
                builder.AddImageTiled(6, 6, 228, 100, 0xA40);
                builder.AddImageTiled(6, 116, 228, 20, 0xA40);
                builder.AddAlphaRegion(6, 6, 228, 142);

                // You are about to disable inventory insurance auto-renewal.
                builder.AddHtmlLocalized(8, 8, 228, 100, 1071021, 0x7FFF);

                builder.AddButton(6, 116, 0xFB1, 0xFB2, 0);
                builder.AddHtmlLocalized(40, 118, 450, 20, 1060051, 0x7FFF); // CANCEL

                builder.AddButton(114, 116, 0xFA5, 0xFA7, 1);
                builder.AddHtmlLocalized(148, 118, 450, 20, 1071022, 0x7FFF); // DISABLE IT!
            }

            public override void OnResponse(NetState sender, in RelayInfo info)
            {
                if (sender.Mobile is not PlayerMobile pm || !pm.CheckAlive())
                {
                    return;
                }

                if (info.ButtonID == 1)
                {
                    // You have cancelled automatically reinsuring all insured items upon death
                    pm.SendLocalizedMessage(1061075, "", 0x23);
                    pm.AutoRenewInsurance = false;
                }
                else
                {
                    pm.SendLocalizedMessage(1042021); // Cancelled.
                }

                if (_insuranceGump != null)
                {
                    pm.SendGump(_insuranceGump);
                }
            }
        }

        private class ItemInsuranceMenuGump : DynamicGump
        {
            private readonly PlayerMobile _from;
            private readonly bool[] _insure;
            private readonly Item[] _items;
            private int _page;

            public override bool Singleton => true;

            public ItemInsuranceMenuGump(PlayerMobile from, Item[] items) : base(25, 50)
            {
                _from = from;
                _items = items;
                _insure = new bool[items.Length];

                for (var i = 0; i < items.Length; ++i)
                {
                    _insure[i] = items[i].Insured;
                }
            }

            protected override void BuildLayout(ref DynamicGumpBuilder builder)
            {
                builder.AddPage();

                builder.AddBackground(0, 0, 520, 510, 0x13BE);
                builder.AddImageTiled(10, 10, 500, 30, 0xA40);
                builder.AddImageTiled(10, 50, 500, 355, 0xA40);
                builder.AddImageTiled(10, 415, 500, 80, 0xA40);
                builder.AddAlphaRegion(10, 10, 500, 485);

                builder.AddButton(15, 470, 0xFB1, 0xFB2, 0);
                builder.AddHtmlLocalized(50, 472, 80, 20, 1011012, 0x7FFF); // CANCEL

                if (_from.AutoRenewInsurance)
                {
                    builder.AddButton(360, 10, 9723, 9724, 1);
                }
                else
                {
                    builder.AddButton(360, 10, 9720, 9722, 1);
                }

                builder.AddHtmlLocalized(395, 14, 105, 20, 1114122, 0x7FFF); // AUTO REINSURE

                builder.AddButton(395, 470, 0xFA5, 0xFA6, 2);
                builder.AddHtmlLocalized(430, 472, 50, 20, 1006044, 0x7FFF); // OK

                builder.AddHtmlLocalized(10, 14, 150, 20, 1114121, 0x7FFF); // <CENTER>ITEM INSURANCE MENU</CENTER>

                builder.AddHtmlLocalized(45, 54, 70, 20, 1062214, 0x7FFF);  // Item
                builder.AddHtmlLocalized(250, 54, 70, 20, 1061038, 0x7FFF); // Cost
                builder.AddHtmlLocalized(400, 54, 70, 20, 1114311, 0x7FFF); // Insured

                var balance = Banker.GetBalance(_from);
                var cost = 0;

                for (var i = 0; i < _items.Length; ++i)
                {
                    if (_insure[i])
                    {
                        cost += GetInsuranceCost(_items[i]);
                    }
                }

                builder.AddHtmlLocalized(15, 420, 300, 20, 1114310, 0x7FFF); // GOLD AVAILABLE:
                builder.AddLabel(215, 420, 0x481, balance.ToString());
                builder.AddHtmlLocalized(15, 435, 300, 20, 1114123, 0x7FFF); // TOTAL COST OF INSURANCE:
                builder.AddLabel(215, 435, 0x481, cost.ToString());

                if (cost != 0)
                {
                    builder.AddHtmlLocalized(15, 450, 300, 20, 1114125, 0x7FFF); // NUMBER OF DEATHS PAYABLE:
                    builder.AddLabel(215, 450, 0x481, (balance / cost).ToString());
                }

                for (int i = _page * 4, y = 72; i < (_page + 1) * 4 && i < _items.Length; ++i, y += 75)
                {
                    var item = _items[i];
                    var b = ItemBounds.Bounds[item.ItemID];

                    builder.AddImageTiledButton(
                        40,
                        y,
                        0x918,
                        0x918,
                        0,
                        GumpButtonType.Page,
                        0,
                        item.ItemID,
                        item.Hue,
                        40 - b.Width / 2 - b.X,
                        30 - b.Height / 2 - b.Y
                    );
                    builder.AddItemProperty(item.Serial);

                    if (_insure[i])
                    {
                        builder.AddButton(400, y, 9723, 9724, 100 + i);
                        builder.AddLabel(250, y, 0x481, GetInsuranceCost(item).ToString());
                    }
                    else
                    {
                        builder.AddButton(400, y, 9720, 9722, 100 + i);
                        builder.AddLabel(250, y, 0x66C, GetInsuranceCost(item).ToString());
                    }
                }

                if (_page >= 1)
                {
                    builder.AddButton(15, 380, 0xFAE, 0xFAF, 3);
                    builder.AddHtmlLocalized(50, 380, 450, 20, 1044044, 0x7FFF); // PREV PAGE
                }

                if ((_page + 1) * 4 < _items.Length)
                {
                    builder.AddButton(400, 380, 0xFA5, 0xFA7, 4);
                    builder.AddHtmlLocalized(435, 380, 70, 20, 1044045, 0x7FFF); // NEXT PAGE
                }
            }

            public override void OnResponse(NetState sender, in RelayInfo info)
            {
                if (info.ButtonID == 0 || !_from.CheckAlive())
                {
                    return;
                }

                switch (info.ButtonID)
                {
                    case 1: // Auto Reinsure
                        {
                            if (_from.AutoRenewInsurance)
                            {
                                _from.SendGump(new CancelRenewInventoryInsuranceGump(this));
                            }
                            else
                            {
                                _from.AutoRenewInventoryInsurance();
                                _from.SendGump(this);
                            }

                            break;
                        }
                    case 2: // OK
                        {
                            _from.SendGump(new ItemInsuranceMenuConfirmGump(this));

                            break;
                        }
                    case 3: // Prev
                        {
                            if (_page >= 1)
                            {
                                _page--;
                                _from.SendGump(this);
                            }

                            break;
                        }
                    case 4: // Next
                        {
                            if ((_page + 1) * 4 < _items.Length)
                            {
                                _page++;
                                _from.SendGump(this);
                            }

                            break;
                        }
                    default:
                        {
                            var idx = info.ButtonID - 100;

                            if (idx >= 0 && idx < _items.Length)
                            {
                                _insure[idx] = !_insure[idx];
                            }

                            _from.SendGump(this);

                            break;
                        }
                }
            }

            private class ItemInsuranceMenuConfirmGump : StaticGump<ItemInsuranceMenuConfirmGump>
            {
                private readonly ItemInsuranceMenuGump _parentGump;

                public ItemInsuranceMenuConfirmGump(ItemInsuranceMenuGump parentGump) : base(250, 200) =>
                    _parentGump = parentGump;

                protected override void BuildLayout(ref StaticGumpBuilder builder)
                {
                    builder.AddBackground(0, 0, 240, 142, 0x13BE);
                    builder.AddImageTiled(6, 6, 228, 100, 0xA40);
                    builder.AddImageTiled(6, 116, 228, 20, 0xA40);
                    builder.AddAlphaRegion(6, 6, 228, 142);

                    builder.AddHtmlLocalized(8, 8, 228, 100, 1114300, 0x7FFF); // Do you wish to insure all newly selected items?

                    builder.AddButton(6, 116, 0xFB1, 0xFB2, 0);
                    builder.AddHtmlLocalized(40, 118, 450, 20, 1060051, 0x7FFF); // CANCEL

                    builder.AddButton(114, 116, 0xFA5, 0xFA7, 1);
                    builder.AddHtmlLocalized(148, 118, 450, 20, 1073996, 0x7FFF); // ACCEPT
                }

                public override void OnResponse(NetState sender, in RelayInfo info)
                {
                    if (sender.Mobile is not PlayerMobile pm || !pm.CheckAlive())
                    {
                        return;
                    }

                    if (info.ButtonID == 1)
                    {
                        var items = _parentGump._items;
                        var insure = _parentGump._insure;
                        for (var i = 0; i < items.Length; ++i)
                        {
                            var item = items[i];

                            if (item.Insured != insure[i])
                            {
                                pm.ToggleItemInsurance_Callback(pm, item, false);
                            }
                        }
                    }
                    else
                    {
                        pm.SendLocalizedMessage(1042021); // Cancelled.
                        pm.SendGump(_parentGump);
                    }
                }
            }
        }
    }
}
