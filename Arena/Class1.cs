// ============================================================================
// Arena1v1Plugin.cs - 1v1 PvP Arena Plugin for SCP:SL (LabAPI)
// Compatible with C# 7.3 (.NET Framework 4.8) out of the box.
// No compile-time dependency on ProjectMER.dll, XpSystem.dll, or Pooling.dll.
//
// Features:
// 1. Configurable Loadout Kits: 7 pre-configured weapon kits stored directly in
//    ArenaConfig.LoadoutKits (Shotgunner, Facility Guard, Pistol Duelist,
//    MTF Rifleman, Chaos Insurgent, SpecOps SMG, Heavy Gunner). Server owners
//    can edit, disable, or add new kits in the LabAPI YAML config file.
// 2. Dynamic 3+ Player Arena Pool: Spectators join via .arena. When 3 or more
//    players are in the pool, 2 fighters are selected each round (prioritizing
//    players who waited on the spectator bench in the previous round).
// 3. Remote Admin Subcommands: Client console (~) only exposes .arena. All admin
//    and testing subcommands (solo, add, win, set, setp1, setp2, stop) are
//    restricted to the Remote Admin console (M).
// 4. Optional Integrations via Reflection:
//    - ProjectMER (auto-spawns the Arena1v1 schematic at ArenaOrigin)
//    - MedveMarci/XpSystem (awards configurable XP on round win/loss)
//    - ArenaStats.cs (persists player stats & leaderboard to Arena1v1Stats.json)
// ============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using CommandSystem;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using MEC;
using PlayerRoles;
using PlayerStatsSystem;
using RemoteAdmin;
using UnityEngine;

namespace Arena1v1
{
    // ------------------------------------------------------------------------
    // 1. CONFIGURABLE LOADOUT KIT DEFINITION
    //    Serialized directly inside ArenaConfig.LoadoutKits so server owners
    //    can customize weapons, armor, items, ammo, and HP per kit in YAML.
    // ------------------------------------------------------------------------
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

    // ------------------------------------------------------------------------
    // 2. PLUGIN CONFIGURATION (LabAPI YAML Config)
    // ------------------------------------------------------------------------
    public sealed class ArenaConfig
    {
        [Description("Enable or disable the 1v1 Arena plugin.")]
        public bool IsEnabled { get; set; } = true;

        [Description("Enable verbose debug logging in the server console.")]
        public bool DebugLogs { get; set; } = false;

        // --- Schematic & Spawn Coordinates ---
        [Description("Automatically spawn the ProjectMER schematic when the first duel starts.")]
        public bool AutoSpawnSchematic { get; set; } = true;

        [Description("ProjectMER schematic name (place Arena1v1.json inside ProjectMER schematics folder).")]
        public string SchematicName { get; set; } = "Arena1v1";

        [Description("World coordinates where the arena schematic center is spawned.")]
        public Vector3 ArenaOrigin { get; set; } = new Vector3(180.0f, 1000.0f, -60.0f);

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

        // --- Matchmaking, Pool & Roles ---
        [Description("Role assigned to active arena fighters (Tutorial is recommended so duels do not block round end).")]
        public RoleTypeId DuelRole { get; set; } = RoleTypeId.Tutorial;

        [Description("Allow players to join the arena pool via .arena only when they are in Spectator role.")]
        public bool OnlySpectatorsCanJoin { get; set; } = true;

        [Description("Maximum number of players allowed in the arena rotation pool at the same time (0 = unlimited).")]
        public int MaxPoolSize { get; set; } = 16;

        [Description("When 3 or more players are in the pool, prioritize players who waited on the bench last round.")]
        public bool PrioritizeWaitingBenchPlayers { get; set; } = true;

        [Description("Swap fighter spawn sides (South <-> North) every even-numbered round.")]
        public bool SwapSidesEveryRound { get; set; } = true;

        [Description("Delay in seconds after a round ends before spawning the next pair of fighters.")]
        public float RematchDelaySeconds { get; set; } = 2.5f;

        [Description("Delay in seconds after changing player role before teleporting and equipping items.")]
        public float EquipDelaySeconds { get; set; } = 0.15f;

        [Description("Duration in seconds for in-game HUD hints sent by the arena plugin.")]
        public float HintDurationSeconds { get; set; } = 5.0f;

        // --- Combat, Health & Friendly Fire ---
        [Description("Default starting health (HP) for both fighters at the start of each round.")]
        public float DefaultHealth { get; set; } = 100f;

        [Description("Default starting artificial health (AHP) for both fighters at the start of each round.")]
        public float DefaultAhp { get; set; } = 0f;

        [Description("Automatically enable FriendlyFire for the two arena fighters while blocking team damage outside the arena.")]
        public bool IsolateFriendlyFireToArena { get; set; } = true;

        [Description("Damage multiplier applied to low-damage hits (<15 HP) between same-team arena fighters.")]
        public float LowDamageCompensationMultiplier { get; set; } = 2.5f;

        // --- XpSystem & ArenaStats Integration ---
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

        [Description("Record wins, losses, streaks, and XP into ArenaStats.cs (LabApi/Arena1v1Stats.json) if present.")]
        public bool EnableStatsRecording { get; set; } = true;

        // --- Loadout Kits & Fallback Equipment ---
        [Description("Select a random kit from LoadoutKits every round (if false, uses DefaultPrimaryWeapon/SecondaryWeapon below).")]
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

    // ------------------------------------------------------------------------
    // 3A. CLIENT CONSOLE COMMAND (.arena in ~ console)
    //     Exposes only joining/leaving the 1v1 arena pool for regular players.
    // ------------------------------------------------------------------------
    [CommandHandler(typeof(ClientCommandHandler))]
    public sealed class ArenaCommand : CommandSystem.ICommand
    {
        public string Command => "arena";
        public string[] Aliases => new[] { "1v1", "duel", "pvp" };
        public string Description => "Join or leave the 1v1 PvP Arena pool (.arena)";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (!Player.TryGet(sender, out Player player) || player == null)
            {
                response = "The .arena command can only be used by in-game players.";
                return false;
            }

            if (Arena1v1Plugin.Instance == null)
            {
                response = "Arena1v1 plugin is not initialized.";
                return false;
            }

            return Arena1v1Plugin.Instance.ToggleArenaParticipation(player, out response);
        }
    }

    // ------------------------------------------------------------------------
    // 3B. REMOTE ADMIN COMMAND (arena in Remote Admin console M)
    //     All administrative and testing subcommands:
    //     - arena solo [ID/Name]       -> spawn in solo test mode on the arena
    //     - arena add <ID/Name>        -> force-add a player to the arena/pool
    //     - arena win [ID/Name]        -> award a round win (+XP and stats record)
    //     - arena set <index|id|random>-> force a specific kit or restore random rotation
    //     - arena kits                 -> list all configured loadout kits
    //     - arena setp1 / arena setp2  -> calibrate Fighter 1 / Fighter 2 spawn points
    //     - arena stop                 -> stop active duel and clear the participant pool
    // ------------------------------------------------------------------------
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public sealed class ArenaAdminCommand : CommandSystem.ICommand
    {
        public string Command => "arena";
        public string[] Aliases => new[] { "arenaadmin", "1v1admin" };
        public string Description => "Manage the 1v1 Arena (solo, add, win, set, kits, setp1, setp2, stop)";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (Arena1v1Plugin.Instance == null)
            {
                response = "Arena1v1 plugin is not initialized.";
                return false;
            }

            Player.TryGet(sender, out Player adminPlayer);

            if (arguments.Count == 0)
            {
                response =
                    "[RemoteAdmin - Arena1v1] Available subcommands:\n" +
                    "- arena solo [ID/Name] : Spawn in solo arena test mode\n" +
                    "- arena add <ID/Name>  : Force-add a player to the arena or rotation pool\n" +
                    "- arena win [ID/Name]  : Trigger round victory (+XP and stats record)\n" +
                    "- arena set <id|num|random> : Force a specific loadout kit or reset to random\n" +
                    "- arena kits           : List all configured loadout kits\n" +
                    "- arena setp1 / setp2  : Save Fighter 1 or Fighter 2 spawn at your current position\n" +
                    "- arena stop           : Stop active duel and clear the arena pool";
                return true;
            }

            string sub = arguments.At(0).ToLowerInvariant();

            if (sub == "kits" || sub == "list")
            {
                return Arena1v1Plugin.Instance.ListConfiguredKits(out response);
            }

            if (sub == "solo" || sub == "test" || sub == "1")
            {
                Player target = adminPlayer;
                if (arguments.Count >= 2)
                {
                    target = Arena1v1Plugin.Instance.FindTargetPlayer(arguments.At(1), adminPlayer);
                }

                if (target == null)
                {
                    response = "[RA - Arena1v1] Target player not found. Usage: arena solo <ID/Name>";
                    return false;
                }

                return Arena1v1Plugin.Instance.StartOrJoinSoloArena(target, out response);
            }

            if (sub == "add" || sub == "force" || sub == "tp" || sub == "put" || sub == "spawn")
            {
                if (arguments.Count < 2)
                {
                    response = "[RA - Arena1v1] Please specify a player ID or nickname: arena add <ID/Name>";
                    return false;
                }

                string targetQuery = arguments.At(1);
                Player target = Arena1v1Plugin.Instance.FindTargetPlayer(targetQuery, adminPlayer);
                if (target == null)
                {
                    response = $"[RA - Arena1v1] Player '{targetQuery}' was not found.";
                    return false;
                }

                return Arena1v1Plugin.Instance.ForceAddPlayerToArena(target, out response);
            }

            if (sub == "win" || sub == "xp")
            {
                Player target = adminPlayer;
                if (arguments.Count >= 2)
                {
                    target = Arena1v1Plugin.Instance.FindTargetPlayer(arguments.At(1), adminPlayer);
                }

                if (target == null)
                {
                    response = "[RA - Arena1v1] Target player not found. Usage: arena win <ID/Name>";
                    return false;
                }

                return Arena1v1Plugin.Instance.SimulateRoundWinForTesting(target, out response);
            }

            if (sub == "set" && arguments.Count >= 2)
            {
                return Arena1v1Plugin.Instance.ForceSelectLoadoutSet(adminPlayer, arguments.At(1), out response);
            }

            if (sub == "stop" || sub == "clear" || sub == "reset")
            {
                return Arena1v1Plugin.Instance.ForceStopAllDuelsFromAdmin(out response);
            }

            if (sub == "setp1")
            {
                if (adminPlayer == null)
                {
                    response = "[RA - Arena1v1] You must be in-game to run setp1.";
                    return false;
                }
                Arena1v1Plugin.Instance.SetCustomSpawnPoint(1, adminPlayer.Position, adminPlayer.Rotation.eulerAngles.y);
                response = $"[RA - Arena1v1] Fighter 1 spawn point saved at {adminPlayer.Position}";
                return true;
            }

            if (sub == "setp2")
            {
                if (adminPlayer == null)
                {
                    response = "[RA - Arena1v1] You must be in-game to run setp2.";
                    return false;
                }
                Arena1v1Plugin.Instance.SetCustomSpawnPoint(2, adminPlayer.Position, adminPlayer.Rotation.eulerAngles.y);
                response = $"[RA - Arena1v1] Fighter 2 spawn point saved at {adminPlayer.Position}";
                return true;
            }

            if (adminPlayer != null)
            {
                return Arena1v1Plugin.Instance.ToggleArenaParticipation(adminPlayer, out response);
            }

            response = "[RA - Arena1v1] Unknown subcommand. Type 'arena' with no arguments for help.";
            return false;
        }
    }

    // ------------------------------------------------------------------------
    // 4. MAIN LABAPI PLUGIN CLASS
    // ------------------------------------------------------------------------
    public sealed class Arena1v1Plugin : Plugin<ArenaConfig>
    {
        public static Arena1v1Plugin Instance { get; private set; }

        public override string Name => "Arena1v1";
        public override string Description => "1v1 PvP Arena with configurable kits, 3+ player pool rotation, XpSystem and stats integration";
        public override string Author => "Arena1v1 Studio";
        public override Version Version => new Version(3, 0, 0);
        public override Version RequiredApiVersion => new Version(1, 0, 0);

        private sealed class ActiveDuelSession
        {
            public string Player1UserId { get; set; } = string.Empty;
            public string Player2UserId { get; set; } = string.Empty;
            public int CurrentRound { get; set; } = 1;
            public bool IsRespawningBetweenRounds { get; set; }
            public CoroutineHandle RematchCoroutine { get; set; }
            public ArenaLoadoutSet CurrentLoadout { get; set; }

            public bool IsSoloSession => string.IsNullOrEmpty(Player2UserId);
        }

        // Participant pool (supports 2, 3, 4+ players with automatic bench rotation)
        private readonly List<string> _arenaParticipants = new List<string>();
        private readonly Dictionary<string, int> _playerWins = new Dictionary<string, int>();
        private readonly System.Random _rng = new System.Random();

        private ActiveDuelSession _activeDuel;
        private string _lastLoadoutId = string.Empty;
        private int _forcedLoadoutIndex = -1;
        private UnityEngine.Component _spawnedArenaSchematic;
        private Vector3? _customP1WorldPos;
        private Vector3? _customP2WorldPos;
        private float _customP1Yaw = 0f;
        private float _customP2Yaw = 180f;

        private bool _wasServerFriendlyFireEnabled;
        private bool _friendlyFireOverrideActive;

        private MethodInfo _xpSystemAddXpMethod;
        private bool _xpSystemSearched;

        private MethodInfo _arenaStatsRecordMethod;
        private bool _arenaStatsSearched;

        public override void Enable()
        {
            Instance = this;
            if (Config.LoadoutKits == null || Config.LoadoutKits.Count == 0)
            {
                Config.LoadoutKits = ArenaConfig.CreateDefaultLoadoutKits();
            }

            PlayerEvents.Hurting += OnPlayerHurting;
            PlayerEvents.Dying += OnPlayerDying;
            PlayerEvents.ChangedRole += OnPlayerChangedRole;
            PlayerEvents.Left += OnPlayerLeft;
            ServerEvents.WaitingForPlayers += ResetOnRoundRestart;
        }

        public override void Disable()
        {
            PlayerEvents.Hurting -= OnPlayerHurting;
            PlayerEvents.Dying -= OnPlayerDying;
            PlayerEvents.ChangedRole -= OnPlayerChangedRole;
            PlayerEvents.Left -= OnPlayerLeft;
            ServerEvents.WaitingForPlayers -= ResetOnRoundRestart;
            ResetOnRoundRestart();
            Instance = null;
        }

        public void SetCustomSpawnPoint(int playerNumber, Vector3 worldPos, float yaw)
        {
            if (playerNumber == 1)
            {
                _customP1WorldPos = worldPos;
                _customP1Yaw = yaw;
            }
            else
            {
                _customP2WorldPos = worldPos;
                _customP2Yaw = yaw;
            }
        }

        private static bool IsSpectatorRole(RoleTypeId role)
        {
            return role == RoleTypeId.Spectator || role == RoleTypeId.Overwatch;
        }

        private int GetPlayerWins(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return 0;
            return _playerWins.TryGetValue(userId, out int w) ? w : 0;
        }

        private int IncrementPlayerWins(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return 0;
            int next = GetPlayerWins(userId) + 1;
            _playerWins[userId] = next;
            return next;
        }

        /// <summary>
        /// Records the duel outcome in ArenaStats.cs (Arena1v1.ArenaStatsManager) via Reflection if loaded.
        /// </summary>
        private int TryRecordMatchInArenaStats(Player winner, Player loser, string loadoutSetName, int xpAwarded)
        {
            string winnerId = winner != null ? winner.UserId : string.Empty;
            string winnerName = winner != null ? winner.Nickname : "Player";
            string loserId = loser != null ? loser.UserId : string.Empty;
            string loserName = loser != null ? loser.Nickname : "Opponent";

            int sessionWins = IncrementPlayerWins(winnerId);
            if (!Config.EnableStatsRecording)
                return sessionWins;

            try
            {
                if (!_arenaStatsSearched)
                {
                    _arenaStatsSearched = true;
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type statsType = asm.GetType("Arena1v1.ArenaStatsManager");
                        if (statsType == null)
                            continue;

                        _arenaStatsRecordMethod = statsType.GetMethod(
                            "RecordMatch",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new Type[]
                            {
                                typeof(string),
                                typeof(string),
                                typeof(string),
                                typeof(string),
                                typeof(string),
                                typeof(int)
                            },
                            null
                        );

                        if (_arenaStatsRecordMethod != null)
                            break;
                    }
                }

                if (_arenaStatsRecordMethod != null)
                {
                    object result = _arenaStatsRecordMethod.Invoke(
                        null,
                        new object[] { winnerId, winnerName, loserId, loserName, loadoutSetName ?? "Standard Kit", xpAwarded }
                    );
                    if (result is int totalSavedWins && totalSavedWins > 0)
                    {
                        _playerWins[winnerId] = totalSavedWins;
                        return totalSavedWins;
                    }
                }
            }
            catch (Exception ex)
            {
                LabApi.Features.Console.Logger.Error($"[Arena1v1] Failed to record match in ArenaStats: {ex.Message}");
            }

            return sessionWins;
        }

        private List<ArenaLoadoutSet> GetActiveKitPool()
        {
            ArenaConfig cfg = Config;
            List<ArenaLoadoutSet> list = new List<ArenaLoadoutSet>();
            if (cfg.LoadoutKits != null)
            {
                for (int i = 0; i < cfg.LoadoutKits.Count; i++)
                {
                    ArenaLoadoutSet kit = cfg.LoadoutKits[i];
                    if (kit != null && kit.Enabled)
                        list.Add(kit);
                }
            }

            if (list.Count == 0)
            {
                list = ArenaConfig.CreateDefaultLoadoutKits();
            }

            return list;
        }

        /// <summary>
        /// Selects a random loadout kit from ArenaConfig.LoadoutKits or builds the fallback kit.
        /// </summary>
        private ArenaLoadoutSet PickRoundLoadoutSet()
        {
            ArenaConfig cfg = Config;
            List<ArenaLoadoutSet> allKits = cfg.LoadoutKits != null && cfg.LoadoutKits.Count > 0
                ? cfg.LoadoutKits
                : ArenaConfig.CreateDefaultLoadoutKits();

            if (_forcedLoadoutIndex >= 0 && _forcedLoadoutIndex < allKits.Count)
            {
                return allKits[_forcedLoadoutIndex];
            }

            if (!cfg.UseRandomLoadoutSets)
            {
                List<ItemType> items = new List<ItemType>();
                if (cfg.HealingItem != ItemType.None)
                    items.Add(cfg.HealingItem);

                return new ArenaLoadoutSet
                {
                    Id = "fallback",
                    Name = "Standard Kit",
                    Enabled = true,
                    PrimaryWeapon = cfg.PrimaryWeapon,
                    SecondaryWeapon = cfg.SecondaryWeapon,
                    Armor = cfg.ArmorItem,
                    HealingAndUtility = items,
                    PrimaryAmmoType = cfg.FallbackPrimaryAmmoType,
                    PrimaryAmmoAmount = cfg.FallbackPrimaryAmmoAmount,
                    SecondaryAmmoType = cfg.FallbackSecondaryAmmoType,
                    SecondaryAmmoAmount = cfg.FallbackSecondaryAmmoAmount
                };
            }

            List<ArenaLoadoutSet> enabledKits = GetActiveKitPool();
            int idx = _rng.Next(enabledKits.Count);
            if (cfg.PreventConsecutiveDuplicateKits && enabledKits.Count > 1 && enabledKits[idx].Id == _lastLoadoutId)
            {
                idx = (idx + 1 + _rng.Next(enabledKits.Count - 1)) % enabledKits.Count;
            }

            ArenaLoadoutSet chosen = enabledKits[idx];
            _lastLoadoutId = chosen.Id ?? chosen.Name;
            return chosen;
        }

        public bool ListConfiguredKits(out string response)
        {
            List<ArenaLoadoutSet> kits = Config.LoadoutKits != null && Config.LoadoutKits.Count > 0
                ? Config.LoadoutKits
                : ArenaConfig.CreateDefaultLoadoutKits();

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("[RA - Arena1v1] Configured Loadout Kits:");
            for (int i = 0; i < kits.Count; i++)
            {
                ArenaLoadoutSet k = kits[i];
                string status = k.Enabled ? "ENABLED" : "DISABLED";
                sb.AppendLine($"#{i + 1} [{k.Id}] {k.Name} ({status}) - {k.PrimaryWeapon} + {k.SecondaryWeapon} + {k.Armor}");
            }
            response = sb.ToString();
            return true;
        }

        public bool ForceSelectLoadoutSet(Player player, string query, out string response)
        {
            if (string.Equals(query, "random", StringComparison.OrdinalIgnoreCase) || query == "0")
            {
                _forcedLoadoutIndex = -1;
                response = "[RA - Arena1v1] Random kit rotation enabled for every round.";
                return true;
            }

            List<ArenaLoadoutSet> kits = Config.LoadoutKits != null && Config.LoadoutKits.Count > 0
                ? Config.LoadoutKits
                : ArenaConfig.CreateDefaultLoadoutKits();

            int foundIndex = -1;
            if (int.TryParse(query, out int num) && num >= 1 && num <= kits.Count)
            {
                foundIndex = num - 1;
            }
            else
            {
                for (int i = 0; i < kits.Count; i++)
                {
                    if (string.Equals(kits[i].Id, query, StringComparison.OrdinalIgnoreCase) ||
                        kits[i].Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        foundIndex = i;
                        break;
                    }
                }
            }

            if (foundIndex < 0 || foundIndex >= kits.Count)
            {
                response = $"[RA - Arena1v1] Kit '{query}' not found. Use 1..{kits.Count}, kit ID, or 'random'.";
                return false;
            }

            _forcedLoadoutIndex = foundIndex;
            ArenaLoadoutSet chosen = kits[_forcedLoadoutIndex];
            if (_activeDuel != null)
            {
                _activeDuel.CurrentLoadout = chosen;
                if (_activeDuel.IsSoloSession && player != null && _activeDuel.Player1UserId == player.UserId)
                {
                    SpawnSingleFighterOnArena(player, 1, _activeDuel.CurrentRound, chosen);
                }
            }

            response = $"[RA - Arena1v1] Forced loadout kit set to: {chosen.Name} ({chosen.Id})";
            return true;
        }

        public bool ForceStopAllDuelsFromAdmin(out string response)
        {
            if (_activeDuel != null)
            {
                Timing.KillCoroutines(_activeDuel.RematchCoroutine);
                Player p1 = Player.Get(_activeDuel.Player1UserId);
                Player p2 = Player.Get(_activeDuel.Player2UserId);
                _activeDuel = null;
                if (p1 != null && !p1.IsDestroyed) ReturnPlayerToSpectator(p1);
                if (p2 != null && !p2.IsDestroyed) ReturnPlayerToSpectator(p2);
            }

            foreach (string uid in _arenaParticipants)
            {
                Player p = Player.Get(uid);
                if (p != null && !p.IsDestroyed)
                    ReturnPlayerToSpectator(p);
            }

            _arenaParticipants.Clear();
            CleanupDroppedArenaPickups();
            RestoreServerFriendlyFire();

            response = "[RA - Arena1v1] Active duel stopped, all participants moved to Spectator, and pool cleared.";
            return true;
        }

        /// <summary>
        /// Integration with MedveMarci/XpSystem (https://github.com/MedveMarci/XpSystem) via Reflection.
        /// </summary>
        private bool TryAwardXpSystemReward(Player target, int xpAmount, string reason)
        {
            if (!Config.EnableXpRewards || target == null || target.IsDestroyed || xpAmount <= 0)
                return false;

            try
            {
                if (!_xpSystemSearched)
                {
                    _xpSystemSearched = true;
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type xpManagerType = asm.GetType("XpSystem.XpManager");
                        if (xpManagerType == null)
                            continue;

                        _xpSystemAddXpMethod = xpManagerType.GetMethod(
                            "AddXp",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new Type[] { typeof(Player), typeof(int), typeof(string) },
                            null
                        );

                        if (_xpSystemAddXpMethod != null)
                            break;
                    }
                }

                if (_xpSystemAddXpMethod != null)
                {
                    _xpSystemAddXpMethod.Invoke(null, new object[] { target, xpAmount, reason });
                    return true;
                }
            }
            catch (Exception ex)
            {
                LabApi.Features.Console.Logger.Error($"[Arena1v1] Failed to award XP via XpSystem: {ex.Message}");
            }

            return false;
        }

        public Player FindTargetPlayer(string query, Player caller)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            string trimmed = query.Trim();
            if (string.Equals(trimmed, "me", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "self", StringComparison.OrdinalIgnoreCase))
            {
                return caller;
            }

            if (int.TryParse(trimmed, out int pid) && Player.TryGet(pid, out Player byId) && byId != null && !byId.IsDestroyed)
            {
                return byId;
            }

            if (Player.TryGet(trimmed, out Player byUserId) && byUserId != null && !byUserId.IsDestroyed)
            {
                return byUserId;
            }

            Player exactNick = Player.GetByNickname(trimmed, false);
            if (exactNick != null && !exactNick.IsDestroyed)
            {
                return exactNick;
            }

            foreach (Player candidate in Player.List)
            {
                if (candidate != null && !candidate.IsDestroyed &&
                    !string.IsNullOrEmpty(candidate.Nickname) &&
                    candidate.Nickname.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return candidate;
                }
            }

            return null;
        }

        public bool StartOrJoinSoloArena(Player player, out string response)
        {
            if (!_arenaParticipants.Contains(player.UserId))
                _arenaParticipants.Add(player.UserId);

            EnsureArenaSchematicSpawned();
            EnableArenaFriendlyFire();

            ArenaLoadoutSet loadout = PickRoundLoadoutSet();

            if (_activeDuel == null)
            {
                _activeDuel = new ActiveDuelSession
                {
                    Player1UserId = player.UserId,
                    Player2UserId = string.Empty,
                    CurrentRound = 1,
                    IsRespawningBetweenRounds = false,
                    CurrentLoadout = loadout
                };

                SpawnSingleFighterOnArena(player, 1, _activeDuel.CurrentRound, loadout);
                response = $"[RA - Arena1v1] {player.Nickname} spawned on the arena in solo mode ({loadout.Name}).";
                return true;
            }

            if (_activeDuel.IsSoloSession && _activeDuel.Player1UserId == player.UserId)
            {
                _activeDuel.CurrentRound++;
                _activeDuel.CurrentLoadout = loadout;
                CleanupDroppedArenaPickups();
                SpawnSingleFighterOnArena(player, 1, _activeDuel.CurrentRound, loadout);
                response = $"[RA - Arena1v1] Solo round #{_activeDuel.CurrentRound} started ({loadout.Name}).";
                return true;
            }

            if (_activeDuel.IsSoloSession && _activeDuel.Player1UserId != player.UserId)
            {
                Player p1 = Player.Get(_activeDuel.Player1UserId);
                if (p1 == null || p1.IsDestroyed)
                {
                    _activeDuel.Player1UserId = player.UserId;
                    _activeDuel.CurrentLoadout = loadout;
                    SpawnSingleFighterOnArena(player, 1, 1, loadout);
                    response = $"[RA - Arena1v1] {player.Nickname} spawned on the arena ({loadout.Name}).";
                    return true;
                }

                _activeDuel.Player2UserId = player.UserId;
                _activeDuel.CurrentRound = 1;
                _activeDuel.CurrentLoadout = loadout;
                SpawnBothFightersOnArena(p1, player, _activeDuel);
                response = $"[RA - Arena1v1] Duel started: {p1.Nickname} vs {player.Nickname} ({loadout.Name})!";
                return true;
            }

            response = $"[RA - Arena1v1] {player.Nickname} added to the arena pool (Pool size: {_arenaParticipants.Count}).";
            return true;
        }

        public bool ForceAddPlayerToArena(Player target, out string response)
        {
            if (!_arenaParticipants.Contains(target.UserId))
                _arenaParticipants.Add(target.UserId);

            EnsureArenaSchematicSpawned();
            EnableArenaFriendlyFire();

            ArenaLoadoutSet loadout = PickRoundLoadoutSet();

            if (_activeDuel == null)
            {
                _activeDuel = new ActiveDuelSession
                {
                    Player1UserId = target.UserId,
                    Player2UserId = string.Empty,
                    CurrentRound = 1,
                    IsRespawningBetweenRounds = false,
                    CurrentLoadout = loadout
                };

                SpawnSingleFighterOnArena(target, 1, 1, loadout);
                response = $"[RA - Arena1v1] Player {target.Nickname} teleported to the arena ({loadout.Name}).";
                return true;
            }

            if (_activeDuel.IsSoloSession && _activeDuel.Player1UserId != target.UserId)
            {
                Player p1 = Player.Get(_activeDuel.Player1UserId);
                if (p1 == null || p1.IsDestroyed)
                {
                    _activeDuel.Player1UserId = target.UserId;
                    _activeDuel.CurrentLoadout = loadout;
                    SpawnSingleFighterOnArena(target, 1, 1, loadout);
                    response = $"[RA - Arena1v1] Player {target.Nickname} teleported to the arena ({loadout.Name}).";
                    return true;
                }

                _activeDuel.Player2UserId = target.UserId;
                _activeDuel.CurrentRound = 1;
                _activeDuel.CurrentLoadout = loadout;
                SpawnBothFightersOnArena(p1, target, _activeDuel);
                response = $"[RA - Arena1v1] Player {target.Nickname} matched against {p1.Nickname}! 1v1 duel started.";
                return true;
            }

            response = $"[RA - Arena1v1] Player {target.Nickname} added to the arena pool ({_arenaParticipants.Count} players). They will rotate in next round.";
            target.SendHint(
                $"<color=#00F0FF><b>[1v1 Arena]</b></color> You have been added to the arena pool (<b>{_arenaParticipants.Count}</b> players).\n" +
                "Two fighters are selected randomly each round.",
                Config.HintDurationSeconds
            );
            return true;
        }

        public bool SimulateRoundWinForTesting(Player player, out string response)
        {
            ArenaConfig cfg = Config;
            string currentSetName = _activeDuel != null && _activeDuel.CurrentLoadout != null
                ? _activeDuel.CurrentLoadout.Name
                : "Standard Kit";

            int totalWins = TryRecordMatchInArenaStats(player, null, currentSetName, cfg.WinXpReward);
            string xpReason = $"{cfg.WinXpReason} (Wins: {totalWins})";
            bool xpAwarded = TryAwardXpSystemReward(player, cfg.WinXpReward, xpReason);

            if (!xpAwarded)
            {
                player.SendHint(
                    $"<color=#FACC15><b>[1v1 Arena - Victory]</b></color> Awarded <b>+{cfg.WinXpReward} XP</b> (Total Wins: {totalWins})!",
                    cfg.HintDurationSeconds
                );
            }

            if (_activeDuel != null)
            {
                _activeDuel.CurrentRound++;
                CleanupDroppedArenaPickups();
                if (_activeDuel.IsSoloSession && _arenaParticipants.Count < 2)
                {
                    ArenaLoadoutSet nextSet = PickRoundLoadoutSet();
                    _activeDuel.CurrentLoadout = nextSet;
                    SpawnSingleFighterOnArena(player, 1, _activeDuel.CurrentRound, nextSet);
                }
                else
                {
                    StartNextRandomRoundFromPool(_activeDuel.CurrentRound);
                }
            }

            response = $"[RA - Arena1v1] Round victory awarded to {player.Nickname} (+{cfg.WinXpReward} XP, Total Wins: {totalWins}).";
            return true;
        }

        private void EnableArenaFriendlyFire()
        {
            if (!Config.IsolateFriendlyFireToArena)
                return;

            if (!_friendlyFireOverrideActive)
            {
                _wasServerFriendlyFireEnabled = Server.FriendlyFire;
                _friendlyFireOverrideActive = true;
            }
            Server.FriendlyFire = true;
        }

        private void RestoreServerFriendlyFire()
        {
            if (_friendlyFireOverrideActive)
            {
                Server.FriendlyFire = _wasServerFriendlyFireEnabled;
                _friendlyFireOverrideActive = false;
            }
        }

        private void ResolveArenaSpawnPoints(out Vector3 p1WorldPos, out float p1Yaw, out Vector3 p2WorldPos, out float p2Yaw)
        {
            ArenaConfig cfg = Config;

            p1WorldPos = _customP1WorldPos.HasValue ? _customP1WorldPos.Value : (cfg.ArenaOrigin + cfg.Player1SpawnOffset);
            p2WorldPos = _customP2WorldPos.HasValue ? _customP2WorldPos.Value : (cfg.ArenaOrigin + cfg.Player2SpawnOffset);
            p1Yaw = _customP1WorldPos.HasValue ? _customP1Yaw : cfg.Player1SpawnYaw;
            p2Yaw = _customP2WorldPos.HasValue ? _customP2Yaw : cfg.Player2SpawnYaw;

            if (_spawnedArenaSchematic != null && !_customP1WorldPos.HasValue && !_customP2WorldPos.HasValue)
            {
                foreach (Transform child in _spawnedArenaSchematic.transform.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "Arena_Spawn_P1_BaseRing")
                        p1WorldPos = child.position + Vector3.up * 1.15f;
                    else if (child.name == "Arena_Spawn_P2_BaseRing")
                        p2WorldPos = child.position + Vector3.up * 1.15f;
                }
            }
        }

        /// <summary>
        /// Handles player .arena toggle:
        /// - If already in the pool or fighting -> removes player and sets role to Spectator.
        /// - If joining as Spectator:
        ///   * Starts duel immediately when 2 players are ready and no duel is active.
        ///   * If 2 players are already fighting and a 3rd+ player types .arena, adds them to the rotation pool.
        /// </summary>
        public bool ToggleArenaParticipation(Player player, out string response)
        {
            ArenaConfig cfg = Config;
            string userId = player.UserId;

            bool isCurrentlyFighting = _activeDuel != null &&
                                       (_activeDuel.Player1UserId == userId || _activeDuel.Player2UserId == userId);

            if (_arenaParticipants.Contains(userId) || isCurrentlyFighting)
            {
                _arenaParticipants.Remove(userId);

                if (isCurrentlyFighting)
                {
                    StopDuelAndOptOutPlayer(player);
                    response = "[1v1 Arena] You have left the arena and returned to Spectator.";
                    return true;
                }

                player.SendHint("<color=#F87171>[1v1 Arena]</color> You have left the arena pool.", cfg.HintDurationSeconds);
                response = "[1v1 Arena] You have left the arena pool (.arena).";
                return true;
            }

            if (cfg.OnlySpectatorsCanJoin && !IsSpectatorRole(player.Role))
            {
                player.SendHint(
                    "<color=#F87171><b>[1v1 Arena]</b></color> You can only join <b>.arena</b> while in the <b>Spectator</b> role!",
                    cfg.HintDurationSeconds
                );
                response = "[1v1 Arena] You can only join .arena while you are a Spectator.";
                return false;
            }

            CleanupInvalidParticipants();
            if (cfg.MaxPoolSize > 0 && _arenaParticipants.Count >= cfg.MaxPoolSize)
            {
                response = $"[1v1 Arena] The arena pool is currently full ({cfg.MaxPoolSize}/{cfg.MaxPoolSize}).";
                return false;
            }

            _arenaParticipants.Add(userId);

            if (_activeDuel != null && _activeDuel.IsSoloSession && _activeDuel.Player1UserId != userId)
            {
                StartNextRandomRoundFromPool(1);
                response = "[1v1 Arena] Opponent found - teleporting to the arena!";
                return true;
            }

            if (_activeDuel == null && _arenaParticipants.Count >= 2)
            {
                StartNextRandomRoundFromPool(1);
                response = "[1v1 Arena] Match found - teleporting to the arena!";
                return true;
            }

            if (_activeDuel != null && !_activeDuel.IsSoloSession)
            {
                int totalInPool = _arenaParticipants.Count;
                player.SendHint(
                    $"<color=#00F0FF><b>[1v1 Arena]</b></color> Added to the arena rotation pool (<b>{totalInPool}</b> players)!\n" +
                    "Two fighters are chosen randomly after the current round. Type <b>.arena</b> again to leave.",
                    cfg.HintDurationSeconds
                );

                Player f1 = Player.Get(_activeDuel.Player1UserId);
                Player f2 = Player.Get(_activeDuel.Player2UserId);
                string joinNotice = $"<color=#34D399>[1v1 Arena]</color> <b>{player.Nickname}</b> joined the arena pool (<b>{totalInPool}</b> players).";
                if (f1 != null && !f1.IsDestroyed) f1.SendHint(joinNotice, 3.5f);
                if (f2 != null && !f2.IsDestroyed) f2.SendHint(joinNotice, 3.5f);

                response = $"[1v1 Arena] Added to the arena pool ({totalInPool} players). You will rotate in next round!";
                return true;
            }

            player.SendHint(
                "<color=#00F0FF>[1v1 Arena]</color> You are queued in the arena pool (1/2).\n" +
                "Match starts as soon as a 2nd Spectator joins. Type <b>.arena</b> again to cancel.",
                cfg.HintDurationSeconds
            );
            response = "[1v1 Arena] Added to queue (1/2). Type .arena again to leave.";
            return true;
        }

        private void CleanupInvalidParticipants()
        {
            ArenaConfig cfg = Config;
            _arenaParticipants.RemoveAll(id =>
            {
                Player p = Player.Get(id);
                if (p == null || p.IsDestroyed)
                    return true;

                bool isFightingNow = _activeDuel != null &&
                                     (_activeDuel.Player1UserId == id || _activeDuel.Player2UserId == id);

                if (!isFightingNow && cfg.OnlySpectatorsCanJoin && !IsSpectatorRole(p.Role))
                    return true;

                return false;
            });
        }

        /// <summary>
        /// Selects 2 fighters from _arenaParticipants (prioritizing bench players when 3+ players are in the pool),
        /// moves non-selected participants to Spectator, and spawns the chosen pair with a random loadout kit.
        /// </summary>
        private void StartNextRandomRoundFromPool(int nextRoundNumber)
        {
            CleanupInvalidParticipants();

            if (_arenaParticipants.Count < 2)
            {
                string prevP1 = _activeDuel != null ? _activeDuel.Player1UserId : string.Empty;
                string prevP2 = _activeDuel != null ? _activeDuel.Player2UserId : string.Empty;
                _activeDuel = null;
                RestoreServerFriendlyFire();

                foreach (string uid in _arenaParticipants)
                {
                    Player waitPlayer = Player.Get(uid);
                    if (waitPlayer != null && !waitPlayer.IsDestroyed)
                    {
                        ReturnPlayerToSpectator(waitPlayer);
                        waitPlayer.SendHint(
                            "<color=#FACC15>[1v1 Arena]</color> Waiting for a second opponent (1/2).\n" +
                            "Type <b>.arena</b> again to leave.",
                            Config.HintDurationSeconds
                        );
                    }
                }

                if (!string.IsNullOrEmpty(prevP1) && !_arenaParticipants.Contains(prevP1))
                {
                    Player p1Old = Player.Get(prevP1);
                    if (p1Old != null && !p1Old.IsDestroyed) ReturnPlayerToSpectator(p1Old);
                }
                if (!string.IsNullOrEmpty(prevP2) && !_arenaParticipants.Contains(prevP2))
                {
                    Player p2Old = Player.Get(prevP2);
                    if (p2Old != null && !p2Old.IsDestroyed) ReturnPlayerToSpectator(p2Old);
                }
                return;
            }

            string lastP1 = _activeDuel != null ? _activeDuel.Player1UserId : string.Empty;
            string lastP2 = _activeDuel != null ? _activeDuel.Player2UserId : string.Empty;

            List<string> waitingBench = new List<string>();
            List<string> previousFighters = new List<string>();

            foreach (string uid in _arenaParticipants)
            {
                if (uid == lastP1 || uid == lastP2)
                    previousFighters.Add(uid);
                else
                    waitingBench.Add(uid);
            }

            string chosenP1Id;
            string chosenP2Id;

            if (Config.PrioritizeWaitingBenchPlayers && waitingBench.Count >= 2)
            {
                int idx1 = _rng.Next(waitingBench.Count);
                chosenP1Id = waitingBench[idx1];
                waitingBench.RemoveAt(idx1);
                int idx2 = _rng.Next(waitingBench.Count);
                chosenP2Id = waitingBench[idx2];
            }
            else if (Config.PrioritizeWaitingBenchPlayers && waitingBench.Count == 1)
            {
                chosenP1Id = waitingBench[0];
                chosenP2Id = previousFighters[_rng.Next(previousFighters.Count)];
                if (_rng.Next(2) == 1)
                {
                    string tmp = chosenP1Id;
                    chosenP1Id = chosenP2Id;
                    chosenP2Id = tmp;
                }
            }
            else
            {
                List<string> poolCopy = new List<string>(_arenaParticipants);
                int idx1 = _rng.Next(poolCopy.Count);
                chosenP1Id = poolCopy[idx1];
                poolCopy.RemoveAt(idx1);
                int idx2 = _rng.Next(poolCopy.Count);
                chosenP2Id = poolCopy[idx2];
            }

            Player p1 = Player.Get(chosenP1Id);
            Player p2 = Player.Get(chosenP2Id);

            if (p1 == null || p1.IsDestroyed || p2 == null || p2.IsDestroyed)
            {
                CleanupInvalidParticipants();
                return;
            }

            EnsureArenaSchematicSpawned();
            EnableArenaFriendlyFire();

            ArenaLoadoutSet roundLoadout = PickRoundLoadoutSet();

            foreach (string uid in _arenaParticipants)
            {
                if (uid == chosenP1Id || uid == chosenP2Id)
                    continue;

                Player benchPlayer = Player.Get(uid);
                if (benchPlayer != null && !benchPlayer.IsDestroyed)
                {
                    ReturnPlayerToSpectator(benchPlayer);
                    benchPlayer.SendHint(
                        $"<color=#00F0FF><b>[1v1 Arena - Round #{nextRoundNumber}]</b></color>\n" +
                        $"Fighting now: <color=#38BDF8>{p1.Nickname}</color> vs <color=#FB7185>{p2.Nickname}</color> (<b>{roundLoadout.Name}</b>)\n" +
                        $"<size=75%>You are in the pool ({_arenaParticipants.Count} players) and will rotate in next round.</size>",
                        Config.HintDurationSeconds
                    );
                }
            }

            _activeDuel = new ActiveDuelSession
            {
                Player1UserId = p1.UserId,
                Player2UserId = p2.UserId,
                CurrentRound = nextRoundNumber,
                IsRespawningBetweenRounds = false,
                CurrentLoadout = roundLoadout
            };

            SpawnBothFightersOnArena(p1, p2, _activeDuel);
        }

        private void EnsureArenaSchematicSpawned()
        {
            if (!Config.AutoSpawnSchematic || _spawnedArenaSchematic != null)
                return;

            ArenaConfig cfg = Config;
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type spawnerType = asm.GetType("ProjectMER.Features.ObjectSpawner");
                    if (spawnerType == null)
                        continue;

                    foreach (MethodInfo method in spawnerType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (method.Name != "SpawnSchematic")
                            continue;

                        ParameterInfo[] pars = method.GetParameters();
                        if (pars.Length >= 4 && pars[0].ParameterType == typeof(string) && pars[1].ParameterType == typeof(Vector3))
                        {
                            object[] invokeArgs = new object[pars.Length];
                            invokeArgs[0] = cfg.SchematicName;
                            invokeArgs[1] = cfg.ArenaOrigin;
                            invokeArgs[2] = Quaternion.identity;
                            invokeArgs[3] = Vector3.one;
                            for (int i = 4; i < pars.Length; i++)
                            {
                                invokeArgs[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue : null;
                            }

                            _spawnedArenaSchematic = method.Invoke(null, invokeArgs) as UnityEngine.Component;
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LabApi.Features.Console.Logger.Error($"[Arena1v1] Failed to spawn ProjectMER schematic {cfg.SchematicName}: {ex.Message}");
            }
        }

        private void SpawnSingleFighterOnArena(Player fighter, int slotNumber, int roundNumber, ArenaLoadoutSet loadout)
        {
            ArenaConfig cfg = Config;
            bool swap = cfg.SwapSidesEveryRound && (roundNumber % 2 == 0);

            ResolveArenaSpawnPoints(
                out Vector3 baseP1Pos,
                out float baseP1Yaw,
                out Vector3 baseP2Pos,
                out float baseP2Yaw
            );

            bool useP2Side = (slotNumber == 2) ? !swap : swap;
            Vector3 pos = useP2Side ? baseP2Pos : baseP1Pos;
            float yaw = useP2Side ? baseP2Yaw : baseP1Yaw;

            TeleportAndEquipFighter(fighter, cfg.DuelRole, pos, yaw, loadout);

            fighter.SendHint(
                $"<color=#00F0FF><b>[1v1 Arena - Round #{roundNumber}]</b></color>\n" +
                $"Kit: <color=#FACC15><b>{loadout.Name}</b></color>\n" +
                "<size=75%>Type <b>.arena</b> again to return to Spectator</size>",
                cfg.HintDurationSeconds
            );
        }

        private void SpawnBothFightersOnArena(Player p1, Player p2, ActiveDuelSession session)
        {
            ArenaConfig cfg = Config;
            bool swap = cfg.SwapSidesEveryRound && (session.CurrentRound % 2 == 0);

            ResolveArenaSpawnPoints(
                out Vector3 baseP1Pos,
                out float baseP1Yaw,
                out Vector3 baseP2Pos,
                out float baseP2Yaw
            );

            Vector3 p1Pos = swap ? baseP2Pos : baseP1Pos;
            Vector3 p2Pos = swap ? baseP1Pos : baseP2Pos;

            float p1Yaw = swap ? baseP2Yaw : baseP1Yaw;
            float p2Yaw = swap ? baseP1Yaw : baseP2Yaw;

            ArenaLoadoutSet loadout = session.CurrentLoadout ?? PickRoundLoadoutSet();
            session.CurrentLoadout = loadout;

            TeleportAndEquipFighter(p1, cfg.DuelRole, p1Pos, p1Yaw, loadout);
            TeleportAndEquipFighter(p2, cfg.DuelRole, p2Pos, p2Yaw, loadout);

            int p1Wins = GetPlayerWins(p1.UserId);
            int p2Wins = GetPlayerWins(p2.UserId);

            string banner =
                $"<color=#00F0FF><b>[1v1 Arena - Round #{session.CurrentRound}]</b></color> - Kit: <color=#FACC15><b>{loadout.Name}</b></color>\n" +
                $"<color=#38BDF8>{p1.Nickname}</color> <b>({p1Wins})</b> vs <b>({p2Wins})</b> <color=#FB7185>{p2.Nickname}</color> " +
                $"<color=#34D399>(+{cfg.WinXpReward} XP)</color>\n" +
                $"<size=75%>Pool Size: {_arenaParticipants.Count} - Leave Arena: <b>.arena</b></size>";

            p1.SendHint(banner, cfg.HintDurationSeconds);
            p2.SendHint(banner, cfg.HintDurationSeconds);
        }

        private void TeleportAndEquipFighter(
            Player fighter,
            RoleTypeId targetRole,
            Vector3 worldPos,
            float yawDegrees,
            ArenaLoadoutSet loadout
        )
        {
            ArenaConfig cfg = Config;
            if (fighter.Role != targetRole)
            {
                fighter.SetRole(targetRole, RoleChangeReason.Respawn, RoleSpawnFlags.None);
            }

            float delay = cfg.EquipDelaySeconds > 0f ? cfg.EquipDelaySeconds : 0.15f;
            Timing.CallDelayed(delay, () =>
            {
                if (fighter == null || fighter.IsDestroyed)
                    return;

                fighter.ClearInventory();
                fighter.Position = worldPos;
                fighter.Rotation = Quaternion.Euler(0f, yawDegrees, 0f);

                float targetHp = loadout.HealthOverride > 0f ? loadout.HealthOverride : cfg.DefaultHealth;
                fighter.Health = targetHp > 0f ? targetHp : fighter.MaxHealth;

                float targetAhp = loadout.AhpOverride > 0f ? loadout.AhpOverride : cfg.DefaultAhp;
                if (targetAhp > 0f)
                {
                    fighter.ArtificialHealth = targetAhp;
                }

                if (loadout.Armor != ItemType.None)
                    fighter.AddItem(loadout.Armor);

                if (loadout.PrimaryWeapon != ItemType.None)
                    fighter.AddItem(loadout.PrimaryWeapon);

                if (loadout.SecondaryWeapon != ItemType.None)
                    fighter.AddItem(loadout.SecondaryWeapon);

                if (loadout.HealingAndUtility != null)
                {
                    for (int i = 0; i < loadout.HealingAndUtility.Count; i++)
                    {
                        if (loadout.HealingAndUtility[i] != ItemType.None)
                            fighter.AddItem(loadout.HealingAndUtility[i]);
                    }
                }

                if (loadout.PrimaryAmmoType != ItemType.None)
                    fighter.SetAmmo(loadout.PrimaryAmmoType, loadout.PrimaryAmmoAmount);

                if (loadout.SecondaryAmmoType != ItemType.None && loadout.SecondaryAmmoType != loadout.PrimaryAmmoType)
                    fighter.SetAmmo(loadout.SecondaryAmmoType, loadout.SecondaryAmmoAmount);
            });
        }

        private void OnPlayerHurting(PlayerHurtingEventArgs ev)
        {
            if (ev.Player == null || ev.Attacker == null || ev.Player == ev.Attacker)
                return;

            if (_activeDuel != null)
            {
                string victimId = ev.Player.UserId;
                string attackerId = ev.Attacker.UserId;

                bool victimInDuel = victimId == _activeDuel.Player1UserId || victimId == _activeDuel.Player2UserId;
                bool attackerInDuel = attackerId == _activeDuel.Player1UserId || attackerId == _activeDuel.Player2UserId;

                if (victimInDuel && attackerInDuel && victimId != attackerId)
                {
                    ev.IsAllowed = true;
                    StandardDamageHandler stdDamage = ev.DamageHandler as StandardDamageHandler;
                    if (stdDamage != null && stdDamage.Damage > 0f && stdDamage.Damage < 15f)
                    {
                        stdDamage.Damage *= Config.LowDamageCompensationMultiplier;
                    }
                    return;
                }
            }

            if (_friendlyFireOverrideActive && !_wasServerFriendlyFireEnabled)
            {
                if (ev.Attacker.Team == ev.Player.Team ||
                    (ev.Attacker.Faction == ev.Player.Faction && ev.Attacker.Faction != Faction.Unclassified))
                {
                    ev.IsAllowed = false;
                }
            }
        }

        private void OnPlayerChangedRole(PlayerChangedRoleEventArgs ev)
        {
            Player changedPlayer = ev.Player;
            if (changedPlayer == null || changedPlayer.IsDestroyed)
                return;

            Timing.CallDelayed(0.05f, () =>
            {
                if (changedPlayer == null || changedPlayer.IsDestroyed)
                    return;

                string userId = changedPlayer.UserId;
                if (_activeDuel != null && (userId == _activeDuel.Player1UserId || userId == _activeDuel.Player2UserId))
                    return;

                if (!IsSpectatorRole(changedPlayer.Role) && _arenaParticipants.Remove(userId))
                {
                    changedPlayer.SendHint("<color=#FACC15>[1v1 Arena]</color> You spawned in the main round and were removed from the arena pool.", 4f);
                }
            });
        }

        private void OnPlayerDying(PlayerDyingEventArgs ev)
        {
            if (_activeDuel == null || ev.Player == null)
                return;

            string victimId = ev.Player.UserId;
            if (victimId != _activeDuel.Player1UserId && victimId != _activeDuel.Player2UserId)
                return;

            ev.IsAllowed = false;

            if (_activeDuel.IsRespawningBetweenRounds)
                return;

            _activeDuel.IsRespawningBetweenRounds = true;
            ev.Player.ClearInventory();
            ev.Player.Health = ev.Player.MaxHealth;

            ArenaConfig cfg = Config;

            if (_activeDuel.IsSoloSession)
            {
                _activeDuel.CurrentRound++;
                _activeDuel.RematchCoroutine = Timing.RunCoroutine(RematchRespawnCoroutine(_activeDuel));
                return;
            }

            bool victimIsP1 = victimId == _activeDuel.Player1UserId;
            Player p1 = Player.Get(_activeDuel.Player1UserId);
            Player p2 = Player.Get(_activeDuel.Player2UserId);
            Player winner = victimIsP1 ? p2 : p1;
            Player loser = victimIsP1 ? p1 : p2;
            string winnerName = winner != null ? winner.Nickname : "Player";
            string loadoutName = _activeDuel.CurrentLoadout != null ? _activeDuel.CurrentLoadout.Name : "Standard Kit";

            int winnerWins = TryRecordMatchInArenaStats(winner, loser, loadoutName, cfg.WinXpReward);

            string winMsg =
                $"<color=#FACC15><b>[1v1 Arena]</b></color> Round #{_activeDuel.CurrentRound} Winner: " +
                $"<color=#34D399><b>{winnerName} (+{cfg.WinXpReward} XP | Wins: {winnerWins})</b></color>!\n" +
                $"Next random match and kit in {cfg.RematchDelaySeconds:0.0}s...";

            if (loser != null && !loser.IsDestroyed)
            {
                if (cfg.LossXpReward > 0)
                {
                    TryAwardXpSystemReward(loser, cfg.LossXpReward, cfg.LossXpReason);
                }
                loser.SendHint(winMsg, cfg.RematchDelaySeconds);
            }

            bool xpSent = false;
            if (winner != null && !winner.IsDestroyed && cfg.WinXpReward > 0)
            {
                string reasonWithWins = $"{cfg.WinXpReason} (Wins: {winnerWins})";
                xpSent = TryAwardXpSystemReward(winner, cfg.WinXpReward, reasonWithWins);
            }

            if (!xpSent && winner != null && !winner.IsDestroyed)
            {
                winner.SendHint(winMsg, cfg.RematchDelaySeconds);
            }

            _activeDuel.CurrentRound++;
            _activeDuel.RematchCoroutine = Timing.RunCoroutine(RematchRespawnCoroutine(_activeDuel));
        }

        private IEnumerator<float> RematchRespawnCoroutine(ActiveDuelSession session)
        {
            ArenaConfig cfg = Config;
            yield return Timing.WaitForSeconds(cfg.RematchDelaySeconds);

            if (_activeDuel != session)
                yield break;

            CleanupDroppedArenaPickups();
            session.IsRespawningBetweenRounds = false;

            if (session.IsSoloSession && _arenaParticipants.Count < 2)
            {
                Player solo = Player.Get(session.Player1UserId);
                if (solo == null || solo.IsDestroyed)
                {
                    HandleUnexpectedDisconnect();
                    yield break;
                }
                ArenaLoadoutSet nextLoadout = PickRoundLoadoutSet();
                session.CurrentLoadout = nextLoadout;
                SpawnSingleFighterOnArena(solo, 1, session.CurrentRound, nextLoadout);
                yield break;
            }

            StartNextRandomRoundFromPool(session.CurrentRound);
        }

        private void StopDuelAndOptOutPlayer(Player quittingPlayer)
        {
            if (_activeDuel == null)
                return;

            Timing.KillCoroutines(_activeDuel.RematchCoroutine);

            int nextRound = _activeDuel.CurrentRound + 1;
            _activeDuel = null;
            CleanupDroppedArenaPickups();

            ReturnPlayerToSpectator(quittingPlayer);
            quittingPlayer.SendHint("<color=#F87171>[1v1 Arena]</color> You left the arena and returned to Spectator.", Config.HintDurationSeconds);

            StartNextRandomRoundFromPool(nextRound);
        }

        private void ReturnPlayerToSpectator(Player player)
        {
            if (player == null || player.IsDestroyed)
                return;

            player.ClearInventory();
            if (player.Role != RoleTypeId.Spectator)
            {
                player.SetRole(RoleTypeId.Spectator, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
            }
        }

        private void OnPlayerLeft(PlayerLeftEventArgs ev)
        {
            if (ev.Player == null)
                return;

            _arenaParticipants.Remove(ev.Player.UserId);

            if (_activeDuel != null &&
                (ev.Player.UserId == _activeDuel.Player1UserId || ev.Player.UserId == _activeDuel.Player2UserId))
            {
                HandleUnexpectedDisconnect();
            }
        }

        private void HandleUnexpectedDisconnect()
        {
            if (_activeDuel == null)
                return;

            Timing.KillCoroutines(_activeDuel.RematchCoroutine);

            int nextRound = _activeDuel.CurrentRound + 1;
            _activeDuel = null;
            CleanupDroppedArenaPickups();

            StartNextRandomRoundFromPool(nextRound);
        }

        private void CleanupDroppedArenaPickups()
        {
            ArenaConfig cfg = Config;
            float radius = cfg.CleanupDroppedItemsRadius > 0f ? cfg.CleanupDroppedItemsRadius : 35f;
            foreach (Pickup pickup in Pickup.List)
            {
                if (pickup != null && Vector3.Distance(pickup.Position, cfg.ArenaOrigin) <= radius)
                {
                    pickup.Destroy();
                }
            }
        }

        private void ResetOnRoundRestart()
        {
            _arenaParticipants.Clear();
            _playerWins.Clear();
            if (_activeDuel != null)
            {
                Timing.KillCoroutines(_activeDuel.RematchCoroutine);
                _activeDuel = null;
            }
            RestoreServerFriendlyFire();
            _spawnedArenaSchematic = null;
        }
    }
}
