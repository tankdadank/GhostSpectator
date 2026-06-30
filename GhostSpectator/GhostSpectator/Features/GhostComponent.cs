using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Log = LabApi.Features.Console.Logger;

using AdminToys;
using CustomPlayerEffects;
using GhostSpectator.Commands.ClientConsole;
using GhostSpectator.Commands.ClientConsole.Duelling;
using GhostSpectator.Commands.ClientConsole.Toys;
using GhostSpectator.Commands.ClientConsole.Voicechat;
using GhostSpectator.Features.Extensions;
using InventorySystem.Items;
using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;
using MEC;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps.Scp049;
using PlayerStatsSystem;
using Respawning.Waves;
using UnityEngine;
using Utils.NonAllocLINQ;

namespace GhostSpectator.Features
{
    [DisallowMultipleComponent]
    public class GhostComponent : MonoBehaviour, IInteractionBlocker
    {
        public void Awake()
        {
            player = Player.Get(base.transform.root.gameObject);
            Log.Debug($"Created a {this.GetType().Name} for player {player.Nickname}.", Config.Debug);
        }

        public void OnEnable()
        {
            State = GhostState.Spawning;
            int reviveNum = Scp049ResurrectAbility.GetResurrectionsNumber(player.ReferenceHub);
            if (player.Role == RoleTypeId.Spectator)
            {
                DeadTime += player.RoleBase.ActiveTime;
            }
            else
            {
                PreviousTeam = player.ReferenceHub.GetFaction().GetSpawnableTeam();
            }
            player.Position = new(0f, 500f, 0f);
            player.SetRole(RoleType, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.AssignInventory);
            if (reviveNum > 0)
            {
                Scp049ResurrectAbility.RegisterPlayerResurrection(player.ReferenceHub, reviveNum);
                Log.Debug($"Re-registered resurrection number ({reviveNum}) for player {player.Nickname}.", Config.Debug);
            }
            player.InfoArea &= ~PlayerInfoArea.Role;
            player.CustomInfo = $"<color={Config.GhostColor}>{Translation.GhostNickname ?? "GHOST"}</color>";
            player.Health = player.MaxHealth = Config.GhostHealth;
            Timing.CallDelayed(0.1f, delegate ()
            {
                player.EnableEffect<Ghostly>();
                player.EnableEffect<NightVision>();
            });
            player.ReferenceHub.interCoordinator.AddBlocker(this);
            ghostItem = player.AddItem(GhostItemType);
            Other.GhostItemList.Add(ghostItem);
            if (player.HasPermissions("gs.noclip"))
            {
                FpcNoclip.PermitPlayer(player.ReferenceHub);
                Log.Debug($"Granted noclip permit to player {player.Nickname}.", Config.Debug);
            }
            foreach (string permission in Other.voiceChats)
            {
                if (player.HasPermissions($"gs.autolisten.{permission}"))
                {
                    VoiceChats.Add(permission);
                    Log.Debug($"Enabled autolistening to {permission} for {player.Nickname}.", Config.Debug);
                }
            }
            if (SSGhostSpectator.Singleton != null && Config.SendSettingsOnSpawn)
            {
                SSGhostSpectator.Singleton.ActivateForHub(player.ReferenceHub);
            }
            foreach (Player ply in Ghost.List)
            {
                ply.GetGhostComponent().Toys.ForEach(toy =>
                {
                    toy.netIdentity.AddObserver(player.ConnectionToClient);
                });
            }
            if (!string.IsNullOrWhiteSpace(Translation.SpawnMessage))
            {
                string message = Translation.SpawnMessage.Replace("%colour%", Config.GhostColor);
                player.SendBroadcast(message, Config.SpawnmessageDuration, Broadcast.BroadcastFlags.Normal, true);
            }
            if (!string.IsNullOrWhiteSpace(Translation.SpawnConsoleMessage))
            {
                player.SendConsoleMessage(Translation.SpawnConsoleMessage.Replace("%ghostset%", Translation.GhostsettingsCommand ?? GhostSettings._command)
                                                                         .Replace("%wavetimer%", Translation.CheckwaveinfoCommand ?? CheckWaveInfo._command)
                                                                         .Replace("%createtoy%", Translation.CreateCommand ?? Create._command)
                                                                         .Replace("%destroytoy%", Translation.DestroyCommand ?? Commands.ClientConsole.Toys.Destroy._command)
                                                                         .Replace("%enablevc%", Translation.EnablevoicechatCommand ?? EnableVoicechat._command)
                                                                         .Replace("%disablevc%", Translation.DisablevoicechatCommand ?? DisableVoicechat._command)
                                                                         .Replace("%duel%", Translation.DuelParentCommand ?? DuelParent._command)
                                                                         .Replace("%givegun%", Translation.GivefirearmCommand ?? GiveFirearm._command), "gray");
            }
            State = GhostState.Spawned;
            Log.Debug($"Enabled {this.GetType().Name} for player {player.Nickname}.", Config.Debug);
        }

        public void Update()
        {
        player.GetStatModule<StaminaStat>().AddAmount(1f);

            if (!Config.ConstantlyReinforceName)
            {
              return;
            }

            string ghostInfo = $"<color={Config.GhostColor}>{Translation.GhostNickname ?? "GHOST"}</color>";

            player.InfoArea &= ~PlayerInfoArea.Role;

            if (player.CustomInfo != ghostInfo)
            {
            player.CustomInfo = ghostInfo;
            }
        }

        public void OnDisable()
        {
            State = GhostState.Despawning;
            player.InfoArea |= PlayerInfoArea.Role;
            player.CustomInfo = string.Empty;
            player.DisableAllEffects();
            Other.GhostItemList.Remove(ghostItem);
            ghostItem = null;
            if (player.HasPermissions("gs.noclip"))
            {
                FpcNoclip.UnpermitPlayer(player.ReferenceHub);
                Log.Debug($"Revoked noclip permit of player {player.Nickname}.", Config.Debug);
            }
            foreach (string permission in Other.voiceChats)
            {
                VoiceChats.Remove(permission);
                Log.Debug($"Disabled listening to {permission} for player {player.Nickname}.", Config.Debug);
            }
            Toys.ForEach(toy => Toy.Destroy(player, toy));
            foreach (Player ply in Ghost.List)
            {
                ply.GetGhostComponent().Toys.ForEach(toy =>
                {
                    toy.netIdentity.RemoveObserver(ply.ConnectionToClient);
                    player.ConnectionToClient.RemoveFromObserving(toy.netIdentity, false);
                });
            }
            Duel.Abort(player, DuelPartner);
            Duel.TryAbortPrepare(player, out _);
            Duel.TryRemoveRequest(player, out _, false);
            SSGhostSpectator.Singleton?.DeactivateForHub(player.ReferenceHub);
            Log.Debug($"Disabled {this.GetType().Name} for player {player.Nickname}.", Config.Debug);
        }

        private Player player;
        private Item ghostItem;

        public BlockedInteraction BlockedInteractions => BlockedInteraction.GeneralInteractions | BlockedInteraction.BeDisarmed | BlockedInteraction.GrabItems;
        public bool CanBeCleared => !base.enabled;
        internal float DeadTime { get; set; }
        public Player DuelPartner { get; internal set; }
        private ItemType GhostItemType { get; } = ItemType.Lantern;
        internal GhostState State { get; set; }
        internal Team PreviousTeam { get; set; }
        private RoleTypeId RoleType { get; } = RoleTypeId.Tutorial;
        public HashSet<AdminToyBase> Toys { get; } = new();
        internal HashSet<string> VoiceChats { get; } = new();
        private Config Config => MainClass.Instance.pluginConfig;
        private Translation Translation => MainClass.Instance.pluginTranslation;
    }

    internal enum GhostState
    {
        Spawning,
        Spawned,
        Despawning,
        Despawned
    }
}
