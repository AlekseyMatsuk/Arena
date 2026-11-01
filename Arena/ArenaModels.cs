using System;
using System.Collections.Generic;
using System.ComponentModel;
using MEC;

namespace Arena1v1
{
    public sealed class ArenaLoadoutSet
    {
        [Description("Unique identifier for this kit (used in Remote Admin: arena set <id>).")]
        public string Id { get; set; } = "default";

        [Description("Display name shown to players on round start.")]
        public string Name { get; set; } = "Default Kit";

        [Description("Whether this kit can be selected during random round rotation.")]
        public bool Enabled { get; set; } = true;

        [Description("Primary firearm given to both fighters.")]
        public ItemType PrimaryWeapon { get; set; } = ItemType.GunE11SR;

        [Description("Secondary firearm or sidearm given to both fighters.")]
        public ItemType SecondaryWeapon { get; set; } = ItemType.GunCOM18;

        [Description("Body armor given to both fighters (ItemType.None to disable).")]
        public ItemType Armor { get; set; } = ItemType.ArmorCombat;

        [Description("Medical and utility items given to both fighters.")]
        public List<ItemType> HealingAndUtility { get; set; } = new List<ItemType>();

        [Description("Primary ammunition type added to inventory.")]
        public ItemType PrimaryAmmoType { get; set; } = ItemType.Ammo556x45;

        [Description("Amount of primary ammunition granted.")]
        public ushort PrimaryAmmoAmount { get; set; } = 180;

        [Description("Secondary ammunition type added to inventory.")]
        public ItemType SecondaryAmmoType { get; set; } = ItemType.Ammo9x19;

        [Description("Amount of secondary ammunition granted.")]
        public ushort SecondaryAmmoAmount { get; set; } = 90;

        [Description("Custom starting health for this kit (0 = use ArenaConfig.DefaultHealth).")]
        public float HealthOverride { get; set; } = 0f;

        [Description("Custom starting AHP / artificial health for this kit (0 = use ArenaConfig.DefaultAhp).")]
        public float AhpOverride { get; set; } = 0f;
    }
    public sealed class ActiveDuelSession
    {
        public int Player1Id { get; set; } = -1;
        public int Player2Id { get; set; } = -1;
        public string Player1UserId { get; set; } = string.Empty;
        public string Player2UserId { get; set; } = string.Empty;
        public int LastWinnerId { get; set; } = -1;
        public int LastLoserId { get; set; } = -1;
        public int CurrentRound { get; set; } = 1;
        public float RoundStartTime { get; set; }
        public bool IsRespawningBetweenRounds { get; set; }
        public CoroutineHandle RematchCoroutine { get; set; }
        public ArenaLoadoutSet CurrentLoadout { get; set; }

        public bool IsSoloSession => Player2Id <= 0;
    }
    public sealed class ArenaPlayerStatsRecord
    {
        public string UserId { get; set; } = string.Empty;
        public string Nickname { get; set; } = "Player";
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int CurrentStreak { get; set; }
        public int BestStreak { get; set; }
        public int TotalXpEarned { get; set; }
        public string LastLoadoutSet { get; set; } = "None";
        public string LastPlayedUtc { get; set; } = string.Empty;

        public int TotalMatches => Wins + Losses;

        public double WinRatePercent
        {
            get
            {
                int total = TotalMatches;
                if (total <= 0) return 0.0;
                return Math.Round((double)Wins * 100.0 / total, 1);
            }
        }

        public double KdRatio
        {
            get
            {
                if (Losses <= 0) return Wins;
                return Math.Round((double)Wins / Losses, 2);
            }
        }
    }
}
