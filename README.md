# Arena1v1 - 1v1 PvP Arena Plugin for SCP:SL (LabAPI)

A modular, C# 7.3 compatible 1v1 PvP Arena plugin for SCP: Secret Laboratory servers running **Northwood LabAPI**. Spectators can enter a virtual duel pool via `.arena` without interrupting the main facility round, fight on a custom **ProjectMER** schematic with customizable weapon kits, earn XP via **MedveMarci/XpSystem**, and track persistent statistics in **ArenaStats.cs**.

---

## Key Features

- **Spectator Virtual Queue and 3+ Player Rotation Pool**:
  Players join and leave the arena pool using `.arena` in the client console (`~`). When 2 players are in the pool, they are immediately teleported to the arena. When a 3rd, 4th, or more spectators join via `.arena`, they enter the rotation pool and are automatically rotated into the next round (prioritizing players who waited on the spectator bench).
- **7 Fully Configurable Loadout Kits in YAML Config**:
  Includes pre-configured kits in `ArenaConfig.LoadoutKits` (`Shotgunner Kit`, `Facility Guard Kit`, `Pistol Duelist Kit`, `MTF Rifleman Kit`, `Chaos Insurgent Kit`, `SpecOps SMG Kit`, and `Heavy Gunner Kit`). Server owners can edit weapons, armor, healing items, ammo counts, HP/AHP overrides, or add new kits directly in `config.yml`.
- **Isolated Arena Friendly Fire**:
  Both duelists spawn as `RoleTypeId.Tutorial` so active duels do not block SCP:SL round completion conditions. Friendly fire is automatically enabled for the two active duelists while blocking team damage between players in the main facility.
- **Zero Hard Plugin Dependencies (Reflection Integration)**:
  Compiles cleanly against standard SCP:SL / LabAPI assemblies without requiring `ProjectMER.dll`, `XpSystem.dll`, or `Pooling.dll` at compile time.
  - Automatically calls `ProjectMER.Features.ObjectSpawner.SpawnSchematic` if ProjectMER is installed.
  - Automatically calls `XpSystem.XpManager.AddXp` (`+10 XP` per win by default) if [MedveMarci/XpSystem](https://github.com/MedveMarci/XpSystem) is installed.
- **Persistent Statistics Module (`ArenaStats.cs`)**:
  Tracks wins, losses, win rate percentage, K/D ratio, current and best win streaks, total XP earned, and last winning kit. Saves data automatically to `LabApi/Arena1v1Stats.json` and `LabApi/Arena1v1Stats.db`.
- **Strict Separation of Player and Admin Commands**:
  Regular players only have access to `.arena`, `.stats`, and `.top` in the client console (`~`). All testing and management subcommands (`solo`, `add`, `win`, `set`, `kits`, `setp1`, `setp2`, `stop`) are restricted to the Remote Admin console (`M`).

---

## Repository Structure

| File | Description |
| :--- | :--- |
| `Arena1v1Plugin.cs` | Main LabAPI plugin containing `ArenaLoadoutSet`, `ArenaConfig`, `.arena` client command, Remote Admin `arena` command, pool rotation, and combat handlers. |
| `ArenaStats.cs` | Optional persistent statistics and leaderboard module (`.stats`, `.top`, Remote Admin `arenastats`, and `Arena1v1Stats.json` writer). |
| `Arena1v1.json` | Dedicated 1v1 tactical arena schematic for ProjectMER (high-visibility light-grey geometry, 16 lights, 6 physics colliders, symmetrical North/South spawn bunkers). |
| `Arena1v1Plugin.csproj` | .NET Framework 4.8 project file for building the plugin with Visual Studio or the .NET SDK. |

---

## Building from Source

### Prerequisites

- .NET Framework 4.8 Developer Pack (or .NET SDK)
- Visual Studio 2022 or JetBrains Rider
- Reference assemblies from your SCP:SL Dedicated Server (`SCPSL_Data/Managed/`):
  - `Assembly-CSharp.dll`
  - `Assembly-CSharp-firstpass.dll` (for MEC coroutines)
  - `CommandSystem.Core.dll`
  - `LabApi.dll` (or via NuGet package `Northwood.LabAPI`)
  - `Mirror.dll`
  - `UnityEngine.CoreModule.dll`

### Build Steps

1. Clone the repository and place the required server DLLs into a `References/` folder next to `Arena1v1Plugin.csproj` (or update the `HintPath` entries).
2. Keep both `Arena1v1Plugin.cs` and `ArenaStats.cs` in the project to compile a single unified `Arena1v1Plugin.dll` (or compile `Arena1v1Plugin.cs` standalone).
3. Run:

```bash
dotnet build -c Release
```

---

## Installation on Server

1. Copy the compiled `Arena1v1Plugin.dll` into your server's LabAPI plugins directory:
   `LabApi/plugins/global/` (or your server port folder).
2. If you use **ProjectMER**, copy `Arena1v1.json` into your ProjectMER schematics folder:
   `ProjectMER/Schematics/Arena1v1/Arena1v1.json` (or `AppData/Roaming/SCP Secret Laboratory/LabAPI/configs/ProjectMER/Schematics/` depending on your ProjectMER version).
3. Restart the SCP:SL server. The plugin will generate its configuration file automatically.

---

## Commands Reference

### Client Console Commands (`~`)

| Command | Aliases | Description |
| :--- | :--- | :--- |
| `.arena` | `.1v1`, `.duel`, `.pvp` | Join the 1v1 arena pool (must be Spectator by default) or leave the arena and return to Spectator. |
| `.stats` | `.arenastats`, `.1v1stats`, `.mystats` | View your personal 1v1 arena statistics (rank, wins, losses, win rate, K/D, streak, XP). Requires `ArenaStats.cs`. |
| `.top` | `.arenatop`, `.1v1top`, `.leaderboard` | Display the Top-10 1v1 Arena leaderboard. Requires `ArenaStats.cs`. |

### Remote Admin Console Commands (`M`)

| Command | Description |
| :--- | :--- |
| `arena` | Display all available Remote Admin arena subcommands. |
| `arena solo [ID/Name]` | Spawn yourself (or the specified player) on the arena in solo test mode. |
| `arena add <ID/Name>` | Force-add a player into the active duel or into the arena rotation pool. |
| `arena win [ID/Name]` | Award a round win, grant XP via XpSystem, record stats, and advance to the next round. |
| `arena set <id\|num\|random>` | Force a specific kit by index (`1..7`), kit ID (`shotgun`, `guard`, `pistol`, `mtf`, `chaos`, `specops`, `heavy`), or `random`. |
| `arena kits` | List all configured kits and their status from `ArenaConfig.LoadoutKits`. |
| `arena setp1` | Save the Fighter 1 spawn position and rotation at your current in-game transform. |
| `arena setp2` | Save the Fighter 2 spawn position and rotation at your current in-game transform. |
| `arena stop` | Stop the active duel, return all participants to Spectator, and clear the pool. |
| `arenastats top` | Display the Top-10 arena leaderboard in Remote Admin. |
| `arenastats info <ID/Name>` | Inspect detailed arena statistics for a player. |
| `arenastats reset <UserId\|all>` | Reset statistics for a specific player UserId or wipe the entire leaderboard. |
| `arenastats path` | Print the exact file paths to `Arena1v1Stats.json` and `Arena1v1Stats.db`. |

---

## Configuration Reference (`ArenaConfig`)

The plugin generates a comprehensive configuration file where you can tune arena coordinates, matchmaking rules, XP rewards, and custom loadout kits:

```yaml
is_enabled: true
debug_logs: false
auto_spawn_schematic: true
schematic_name: "Arena1v1"
arena_origin:
  x: 180.0
  y: 200.0
  z: -60.0
player1_spawn_offset:
  x: 0.00
  y: 1.25
  z: -13.50
player1_spawn_yaw: 0.0
player2_spawn_offset:
  x: 0.00
  y: 1.25
  z: 13.50
player2_spawn_yaw: 180.0
cleanup_dropped_items_radius: 35.0
duel_role: Tutorial
only_spectators_can_join: true
max_pool_size: 16
prioritize_waiting_bench_players: true
swap_sides_every_round: true
rematch_delay_seconds: 2.5
equip_delay_seconds: 0.15
hint_duration_seconds: 5.0
default_health: 100.0
default_ahp: 0.0
isolate_friendly_fire_to_arena: true
low_damage_compensation_multiplier: 2.5
enable_xp_rewards: true
win_xp_reward: 10
loss_xp_reward: 0
win_xp_reason: "1v1 Arena Victory"
loss_xp_reason: "1v1 Arena Participation"
enable_stats_recording: true
use_random_loadout_sets: true
prevent_consecutive_duplicate_kits: true
loadout_kits:
  - id: "shotgun"
    name: "Shotgunner Kit"
    enabled: true
    primary_weapon: GunShotgun
    secondary_weapon: GunRevolver
    armor: ArmorHeavy
    healing_and_utility:
      - Adrenaline
      - Medkit
    primary_ammo_type: Ammo12gauge
    primary_ammo_amount: 72
    secondary_ammo_type: Ammo44cal
    secondary_ammo_amount: 48
    health_override: 0
    ahp_override: 0
  - id: "guard"
    name: "Facility Guard Kit"
    enabled: true
    primary_weapon: GunFSP9
    secondary_weapon: GunCOM15
    armor: ArmorLight
    healing_and_utility:
      - Medkit
      - Painkillers
    primary_ammo_type: Ammo9x19
    primary_ammo_amount: 180
    secondary_ammo_type: Ammo9x19
    secondary_ammo_amount: 180
    health_override: 0
    ahp_override: 0
  - id: "pistol"
    name: "Pistol Duelist Kit"
    enabled: true
    primary_weapon: GunCOM18
    secondary_weapon: GunRevolver
    armor: ArmorLight
    healing_and_utility:
      - Painkillers
      - Medkit
    primary_ammo_type: Ammo9x19
    primary_ammo_amount: 120
    secondary_ammo_type: Ammo44cal
    secondary_ammo_amount: 48
    health_override: 0
    ahp_override: 0
```

---

## License

Released under the MIT License.
