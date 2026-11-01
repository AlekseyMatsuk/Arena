using System;
using System.Collections.Generic;
using System.Reflection;
using LabApi.Events.Handlers;
using LabApi.Loader.Features.Plugins;
using MEC;
using UnityEngine;

namespace Arena1v1
{
    public sealed partial class Arena1v1Plugin : Plugin<Config>
    {
        public static Arena1v1Plugin Instance { get; private set; }

        public override string Name => "Arena1v1";
        public override string Description => "1v1 PvP Arena with configurable kits, 3+ player/bot pool rotation, XpSystem and stats integration";
        public override string Author => "Arena1v1 Studio";
        public override Version Version => new Version(3, 2, 0);
        public override Version RequiredApiVersion => new Version(1, 0, 0);

        private readonly List<int> _arenaParticipants = new List<int>();
        private readonly HashSet<int> _forceAddedParticipants = new HashSet<int>();
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
                Config.LoadoutKits = Config.CreateDefaultLoadoutKits();
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

        private void ResetOnRoundRestart()
        {
            _arenaParticipants.Clear();
            _forceAddedParticipants.Clear();
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
