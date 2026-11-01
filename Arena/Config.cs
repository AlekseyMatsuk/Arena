using System.Collections.Generic;
using System.ComponentModel;
using PlayerRoles;
using UnityEngine;

namespace Arena1v1
{
    public sealed class Config
    {
        [Description("Enable or disable the 1v1 Arena plugin.")]
        public bool IsEnabled { get; set; } = true;

        [Description("Enable verbose debug logging in the server console.")]
        public bool DebugLogs { get; set; } = false;
        [Description("Automatically spawn the ProjectMER schematic when the first duel starts.")]
        public bool AutoSpawnSchematic { get; set; } = true;

        [Description("ProjectMER schematic name (place Arena1v1.json inside ProjectMER schematics folder).")]
        public string SchematicName { get; set; } = "Arena1v1";

        [Description("World coordinates where the arena schematic center is spawned (Y=200 avoids height limit issues).")]
        public Vector3 ArenaOrigin { get; set; } = new Vector3(180.0f, 200.0f, -60.0f);

        [Description("Local spawn offset for Fighter 1 relative to ArenaOrigin (South Bunker Z = -13.5).")]
        public Vector3 Player1SpawnOffset { get; set; } = new Vector3(0.00f, 1.25f, -13.50f);

        [Description("Default yaw rotation (degrees) for Fighter 1 spawn point.")]
        public float Player1SpawnYaw { get; set; } = 0.0f;

        [Description("Local spawn offset for Fighter 2 relative to ArenaOrigin (North Bunker Z = +13.5).")]
        public Vector3 Player2SpawnOffset { get; set; } = new Vector3(0.00f, 1.25f, 13.50f);

        [Description("Default yaw rotation (degrees) for Fighter 2 spawn point.")]
        public float Player2SpawnYaw { get; set; } = 180.0f;

        [Description("Radius around ArenaOrigin within which dropped items/pickups are cleaned up between rounds.")]
        public float CleanupDroppedItemsRadius { get; set; } = 35.0f;
        [Description("Role assigned to active arena fighters (Tutorial is recommended so duels do not block round end).")]
        public RoleTypeId DuelRole { get; set; } = RoleTypeId.Tutorial;

        [Description("Allow players to join the arena pool via .arena only when they are in Spectator (or Tutorial/Overwatch).")]
        public bool OnlySpectatorsCanJoin { get; set; } = true;

        [Description("Automatically remove waiting players from the arena pool if a main facility respawn wave spawns them.")]
        public bool RemoveFromPoolOnMainRoundSpawn { get; set; } = false;

        [Description("Maximum number of players allowed in the arena rotation pool at the same time (0 = unlimited).")]
        public int MaxPoolSize { get; set; } = 16;

        [Description("When 3 or more players are in the pool, prioritize players who waited on the bench last round.")]
        public bool PrioritizeWaitingBenchPlayers { get; set; } = true;

        [Description("Swap fighter spawn sides (South <-> North) every even-numbered round.")]
        public bool SwapSidesEveryRound { get; set; } = true;

        [Description("Delay in seconds after a round ends before spawning the next pair of fighters.")]
        public float RematchDelaySeconds { get; set; } = 2.5f;

        [Description("Delay in seconds after changing player role before equipping items.")]
        public float EquipDelaySeconds { get; set; } = 0.15f;

        [Description("Duration in seconds for in-game HUD hints sent by the arena plugin.")]
        public float HintDurationSeconds { get; set; } = 5.0f;

        [Description("Default starting health (HP) for both fighters at the start of each round.")]
        public float DefaultHealth { get; set; } = 100f;

        [Description("Default starting artificial health (AHP) for both fighters at the start of each round.")]
        public float DefaultAhp { get; set; } = 0f;

        [Description("Automatically enable FriendlyFire for the two arena fighters while blocking team damage outside the arena.")]
        public bool IsolateFriendlyFireToArena { get; set; } = true;

        [Description("Damage multiplier applied to low-damage hits (<15 HP) between same-team arena fighters.")]
        public float LowDamageCompensationMultiplier { get; set; } = 2.5f;
        [Description("Enable automatic XP rewards via MedveMarci/XpSystem when XpSystem.dll is loaded on the server.")]
        public bool EnableXpRewards { get; set; } = true;

        [Description("Amount of XP awarded to the round winner via MedveMarci/XpSystem (0 to disable).")]
        public int WinXpReward { get; set; } = 10;

        [Description("Amount of XP awarded to the round loser via MedveMarci/XpSystem (0 to disable).")]
        public int LossXpReward { get; set; } = 0;

        [Description("Reason string passed to XpSystem.XpManager.AddXp for winning a duel.")]
        public string WinXpReason { get; set; } = "1v1 Arena Victory";

        [Description("Reason string passed to XpSystem.XpManager.AddXp for participating in a duel.")]
        public string LossXpReason { get; set; } = "1v1 Arena Participation";

        [Description("Record wins, losses, streaks, and XP into ArenaStatsManager (LabApi/Arena1v1Stats.json) if present.")]
        public bool EnableStatsRecording { get; set; } = true;
        [Description("Select a random kit from LoadoutKits every round (if false, uses PrimaryWeapon/SecondaryWeapon below).")]
        public bool UseRandomLoadoutSets { get; set; } = true;

        [Description("Prevent the same loadout kit from being randomly selected twice in a row.")]
        public bool PreventConsecutiveDuplicateKits { get; set; } = true;

        [Description("Fallback primary weapon used when UseRandomLoadoutSets is false.")]
        public ItemType PrimaryWeapon { get; set; } = ItemType.GunE11SR;

        [Description("Fallback secondary weapon used when UseRandomLoadoutSets is false.")]
        public ItemType SecondaryWeapon { get; set; } = ItemType.GunCOM18;

        [Description("Fallback armor item used when UseRandomLoadoutSets is false.")]
        public ItemType ArmorItem { get; set; } = ItemType.ArmorCombat;

        [Description("Fallback healing item used when UseRandomLoadoutSets is false.")]
        public ItemType HealingItem { get; set; } = ItemType.Medkit;

        [Description("Fallback primary ammo type used when UseRandomLoadoutSets is false.")]
        public ItemType FallbackPrimaryAmmoType { get; set; } = ItemType.Ammo556x45;

        [Description("Fallback primary ammo amount used when UseRandomLoadoutSets is false.")]
        public ushort FallbackPrimaryAmmoAmount { get; set; } = 180;

        [Description("Fallback secondary ammo type used when UseRandomLoadoutSets is false.")]
        public ItemType FallbackSecondaryAmmoType { get; set; } = ItemType.Ammo9x19;

        [Description("Fallback secondary ammo amount used when UseRandomLoadoutSets is false.")]
        public ushort FallbackSecondaryAmmoAmount { get; set; } = 90;

        [Description("Configurable list of weapon loadout kits. You can edit existing kits or add new ones in config.yml.")]
        public List<ArenaLoadoutSet> LoadoutKits { get; set; } = CreateDefaultLoadoutKits();

        public static List<ArenaLoadoutSet> CreateDefaultLoadoutKits()
        {
            return new List<ArenaLoadoutSet>
            {
                new ArenaLoadoutSet
                {
                    Id = "shotgun",
                    Name = "Shotgunner Kit",
                    Enabled = true,
                    PrimaryWeapon = ItemType.GunShotgun,
                    SecondaryWeapon = ItemType.GunRevolver,
                    Armor = ItemType.ArmorHeavy,
                    HealingAndUtility = new List<ItemType> { ItemType.Adrenaline, ItemType.Medkit },
                    PrimaryAmmoType = ItemType.Ammo12gauge,
                    PrimaryAmmoAmount = 72,
                    SecondaryAmmoType = ItemType.Ammo44cal,
                    SecondaryAmmoAmount = 48,
                    HealthOverride = 0f,
                    AhpOverride = 0f
                },
                new ArenaLoadoutSet
                {
                    Id = "guard",
                    Name = "Facility Guard Kit",
                    Enabled = true,
                    PrimaryWeapon = ItemType.GunFSP9,
                    SecondaryWeapon = ItemType.GunCOM15,
                    Armor = ItemType.ArmorLight,
                    HealingAndUtility = new List<ItemType> { ItemType.Medkit, ItemType.Painkillers },
                    PrimaryAmmoType = ItemType.Ammo9x19,
                    PrimaryAmmoAmount = 180,
                    SecondaryAmmoType = ItemType.Ammo9x19,
                    SecondaryAmmoAmount = 180,
                    HealthOverride = 0f,
                    AhpOverride = 0f
                },
                new ArenaLoadoutSet
                {
                    Id = "pistol",
                    Name = "Pistol Duelist Kit",
                    Enabled = true,
                    PrimaryWeapon = ItemType.GunCOM18,
                    SecondaryWeapon = ItemType.GunRevolver,
                    Armor = ItemType.ArmorLight,
                    HealingAndUtility = new List<ItemType> { ItemType.Painkillers, ItemType.Medkit },
                    PrimaryAmmoType = ItemType.Ammo9x19,
                    PrimaryAmmoAmount = 120,
                    SecondaryAmmoType = ItemType.Ammo44cal,
                    SecondaryAmmoAmount = 48,
                    HealthOverride = 0f,
                    AhpOverride = 0f
                },
                new ArenaLoadoutSet
                {
                    Id = "mtf",
                    Name = "MTF Rifleman Kit",
                    Enabled = true,
                    PrimaryWeapon = ItemType.GunE11SR,
                    SecondaryWeapon = ItemType.GunCOM18,
                    Armor = ItemType.ArmorCombat,
                    HealingAndUtility = new List<ItemType> { ItemType.Medkit, ItemType.Adrenaline },
                    PrimaryAmmoType = ItemType.Ammo556x45,
                    PrimaryAmmoAmount = 180,
                    SecondaryAmmoType = ItemType.Ammo9x19,
                    SecondaryAmmoAmount = 90,
                    HealthOverride = 0f,
                    AhpOverride = 0f
                },
                new ArenaLoadoutSet
                {
                    Id = "chaos",
                    Name = "Chaos Insurgent Kit",
                    Enabled = true,
                    PrimaryWeapon = ItemType.GunAK,
                    SecondaryWeapon = ItemType.GunRevolver,
                    Armor = ItemType.ArmorCombat,
                    HealingAndUtility = new List<ItemType> { ItemType.Medkit, ItemType.Painkillers },
                    PrimaryAmmoType = ItemType.Ammo762x39,
                    PrimaryAmmoAmount = 180,
                    SecondaryAmmoType = ItemType.Ammo44cal,
                    SecondaryAmmoAmount = 48,
                    HealthOverride = 0f,
                    AhpOverride = 0f
                },
                new ArenaLoadoutSet
                {
                    Id = "specops",
                    Name = "SpecOps SMG Kit",
                    Enabled = true,
                    PrimaryWeapon = ItemType.GunCrossvec,
                    SecondaryWeapon = ItemType.GunCOM18,
                    Armor = ItemType.ArmorCombat,
                    HealingAndUtility = new List<ItemType> { ItemType.Medkit },
                    PrimaryAmmoType = ItemType.Ammo9x19,
                    PrimaryAmmoAmount = 210,
                    SecondaryAmmoType = ItemType.Ammo9x19,
                    SecondaryAmmoAmount = 210,
                    HealthOverride = 0f,
                    AhpOverride = 0f
                },
                new ArenaLoadoutSet
                {
                    Id = "heavy",
                    Name = "Heavy Gunner Kit",
                    Enabled = true,
                    PrimaryWeapon = ItemType.GunLogicer,
                    SecondaryWeapon = ItemType.GunCOM18,
                    Armor = ItemType.ArmorHeavy,
                    HealingAndUtility = new List<ItemType> { ItemType.Adrenaline },
                    PrimaryAmmoType = ItemType.Ammo762x39,
                    PrimaryAmmoAmount = 240,
                    SecondaryAmmoType = ItemType.Ammo9x19,
                    SecondaryAmmoAmount = 60,
                    HealthOverride = 0f,
                    AhpOverride = 0f
                }
            };
        }
    }
}
