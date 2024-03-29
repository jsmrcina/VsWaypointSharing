using System;
using System.Linq;
using System.Collections.Generic;
using VsWaypointSharing.Models.Networking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;
using Vintagestory.GameContent;
using System.Reflection;

[assembly: ModInfo("VsWaypointSharing",
    Description = "Allows sharing waypoints to other users",
    Authors = new[] { "jsmrcina" })]

namespace VsWaypointSharing
{
    public class VsWaypointSharing : ModSystem
    {
        private class SharingState
        {
            public bool isAutoSyncEnabled = false;
        }

        private class AddWpArgs
        {
            public Vec3d pos;
            public CmdArgs args; // Contains color and title
            public IServerPlayer player;
            public int groupId;
            public string icon;
            public bool pinned;
        }

        private readonly string _sharedWaypointPrefix = "<sync from:";
        private readonly string _waypointSharingChannel = "waypointsharing";

        private ICoreClientAPI ClientApi;
        private ICoreServerAPI ServerApi;

        private IServerNetworkChannel ServerChannel;
        private IClientNetworkChannel ClientChannel;

        // Server side
        // TODO: Persist to save game?
        private Dictionary<IServerPlayer, SharingState> clientStates = new Dictionary<IServerPlayer, SharingState>();
        private readonly int autoSyncThreadDelay = 15;

        private bool debug = false;
        private bool conflictingModErrorLogged = false;
        

        public override void StartClientSide(ICoreClientAPI api)
        {            
            ClientApi = api ?? throw new ArgumentException("Client API is null");
            DetectDebugBuild(false);

            ClientChannel = ClientApi.Network.RegisterChannel(_waypointSharingChannel)
                            .RegisterMessageType(typeof(WaypointShareMessage))
                            .RegisterMessageType(typeof(WaypointRevertMessage))
                            .RegisterMessageType(typeof(WaypointToggleAutoSyncMessage));

            ClientApi.ModLoader.GetModSystem<WorldMapManager>().RegisterMapLayer<WaypointMapLayerExtension>("waypoints");
            ClientApi.ChatCommands.GetOrCreate("ws").RequiresPrivilege(Privilege.chat)
                .BeginSubCommand("sync").HandleWith(OnShare).EndSubCommand()
                .BeginSubCommand("revert").HandleWith(OnRevert).EndSubCommand()
                .BeginSubCommand("autosync").HandleWith(OnAutoSync).EndSubCommand();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            ServerApi = api ?? throw new ArgumentException("Server API is null");
            DetectDebugBuild(true);

            ServerChannel = ServerApi.Network.RegisterChannel(_waypointSharingChannel)
                .RegisterMessageType(typeof(WaypointShareMessage))
                .RegisterMessageType(typeof(WaypointRevertMessage))
                .RegisterMessageType(typeof(WaypointToggleAutoSyncMessage))
                .SetMessageHandler<WaypointShareMessage>(OnShareRequested)
                .SetMessageHandler<WaypointRevertMessage>(OnRevertRequested)
                .SetMessageHandler<WaypointToggleAutoSyncMessage>(OnToggleAutoSyncRequested);

            ServerApi.ModLoader.GetModSystem<WorldMapManager>().RegisterMapLayer<WaypointMapLayerExtension>("waypoints");
            ServerApi.Event.Timer(AutoSyncThreadFunction, autoSyncThreadDelay);
            ServerApi.Event.PlayerLeave += OnPlayerLeaveDisconnect;
            ServerApi.Event.PlayerDisconnect += OnPlayerLeaveDisconnect;
        }

        public void DetectDebugBuild(bool isServer)
        {
            var assemblyConfigurationAttribute = typeof(VsWaypointSharing).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
            var buildConfigurationName = assemblyConfigurationAttribute?.Configuration;
            if(isServer)
            {
                ServerApi.Logger.Notification($"Build of VsWaypointSharing is of type: {buildConfigurationName}");
            }
            else
            {
                ClientApi.Logger.Notification($"Build of VsWaypointSharing is of type: {buildConfigurationName}");
            }

            if (buildConfigurationName.Equals("Debug"))
            {
                debug = true;
            }
        }

        public void OnPlayerLeaveDisconnect(IServerPlayer player)
        {
            clientStates.Remove(player);
        }

        private void AutoSyncThreadFunction()
        {
            if (debug)
            {
                ServerApi.Logger.Notification($"Auto-sync thread running");
            }

            foreach (var sharingState in clientStates)
            {
                if (sharingState.Value.isAutoSyncEnabled == true)
                {
                    if (debug)
                    {
                        ServerApi.Logger.Notification($"Auto-syncing client {sharingState.Key.PlayerUID}");
                    }
                    WaypointShareMessage wsm = new WaypointShareMessage();
                    wsm.LogSuccess = false;
                    OnShareRequested(sharingState.Key, wsm);
                }
            }
        }

        private TextCommandResult OnShare(TextCommandCallingArgs args)
        {
                ClientApi.SendChatMessage($"Requesting waypoints from server");
                ClientChannel.SendPacket(new WaypointShareMessage());
                return TextCommandResult.Success();
        }
        
        private TextCommandResult OnRevert(TextCommandCallingArgs args)
        {
                ClientApi.SendChatMessage($"Reverting to only local waypoints");
                ClientChannel.SendPacket(new WaypointRevertMessage());
                return TextCommandResult.Success();
        }

        private TextCommandResult OnAutoSync(TextCommandCallingArgs args)
        {
                ClientApi.SendChatMessage($"Toggling auto-sync");
                ClientChannel.SendPacket(new WaypointToggleAutoSyncMessage());
                return TextCommandResult.Success();
        }

        private void OnRevertRequested(IServerPlayer fromPlayer, WaypointRevertMessage msg)
        {
            // Get waypoint layer
            var mapManager = ServerApi.ModLoader.GetModSystem<WorldMapManager>();
            var waypointLayer = mapManager.MapLayers.FirstOrDefault(x => x is WaypointMapLayerExtension) as WaypointMapLayerExtension;

            if (waypointLayer == null)
            {
                // Something has gone wrong
                if (!conflictingModErrorLogged)
                {
                    ServerApi.Logger.Error($"VsWaypointSharing cannot function as the WaypointMapLayer is not WaypointMapLayerExtension class");
                    conflictingModErrorLogged = true;
                }
                return;
            }

            List<Waypoint> waypoints = waypointLayer.Waypoints;

            // To make sure we don't modify the waypoint during enumeration,
            // we save off the ones to remove and then do so afterwards.
            List<Waypoint> waypointsToRemove = new List<Waypoint>();

            foreach (Waypoint w in waypoints)
            {
                if (w.OwningPlayerUid == fromPlayer.PlayerUID &&
                    w.Title.StartsWith(_sharedWaypointPrefix))
                {
                    waypointsToRemove.Add(w);
                }
            }

            waypointLayer.NoLogRemoveWp(fromPlayer, _sharedWaypointPrefix);
        }

        private void OnToggleAutoSyncRequested(IServerPlayer fromPlayer, WaypointToggleAutoSyncMessage msg)
        {
            bool isAutoSyncEnabled = true;
            if (!clientStates.ContainsKey(fromPlayer))
            {
                clientStates.Add(fromPlayer, new SharingState { isAutoSyncEnabled = isAutoSyncEnabled });
            }
            else
            {
                SharingState state = clientStates[fromPlayer];
                state.isAutoSyncEnabled = !state.isAutoSyncEnabled;
                isAutoSyncEnabled = state.isAutoSyncEnabled;
            }

            fromPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, $"Auto-sync state toggled to {isAutoSyncEnabled}", EnumChatType.CommandSuccess);
        }

        /*
            This gets called when the client requests waypoints. The ideal way to do this would be to send them the waypoints
            and let them update their local cache. Unfortunately, because the WaypointMapLayer is a server-side layer, it refreshes
            the waypoints every time the client opens the map, which will undo any changes to the local client state. After some experimentation,
            our only real option in the current architecture is to actually add all the waypoints from the other players onto the requesting
            player as their own. There are numerous downsides to this:
                1. The player cannot easily purge waypoints from other players without losing their own
                2. It is hard to tell if we are duplicating a waypoint. Currently, we use location only, so if another player
                    changes the icon or color, it won't update when the player requests a new update.
        */
        private void OnShareRequested(IServerPlayer fromPlayer, WaypointShareMessage wsm)
        {
            // Get waypoint layer
            var mapManager = ServerApi.ModLoader.GetModSystem<WorldMapManager>();
            var waypointLayer = mapManager.MapLayers.FirstOrDefault(x => x is WaypointMapLayerExtension) as WaypointMapLayerExtension;

            if (waypointLayer == null)
            {
                // Something has gone wrong
                if (!conflictingModErrorLogged)
                {
                    ServerApi.Logger.Error($"VsWaypointSharing cannot function as the WaypointMapLayer is not WaypointMapLayerExtension class");
                    conflictingModErrorLogged = true;
                }
                return;
            }

            List<Waypoint> waypoints = waypointLayer.Waypoints;

            // First clear out any previously synced waypoints so that if another player deletes a waypoint,
            // it goes away for fromPlayer too
            waypoints.RemoveAll(x => x.OwningPlayerUid == fromPlayer.PlayerUID && x.Title.StartsWith(_sharedWaypointPrefix));

            // To make sure we don't modify the waypoint during enumeration,
            // we save off the ones to add and then do so afterwards.
            List<AddWpArgs> waypointsToAdd = new List<AddWpArgs>();

            // Get a dictionary of waypoints for this player (where we hash on the attributes we wish to compare on)
            // For now, we only compare on location, so things like icon and name are ignored.
            Dictionary<Vec3d, bool> fromPlayerWaypoints = new Dictionary<Vec3d, bool>();
            foreach (Waypoint w in waypoints)
            {
                if (w.OwningPlayerUid == fromPlayer.PlayerUID)
                {
                    fromPlayerWaypoints.Add(w.Position, true);
                }
            }

            foreach (Waypoint w in waypoints)
            {
                // Ignore any waypoint that is from the requesting player
                if (w.OwningPlayerUid != fromPlayer.PlayerUID)
                {
                    if (!w.Title.StartsWith(_sharedWaypointPrefix))
                    {
                        // Check that this player does not already have this waypoint (only uses position)
                        if (fromPlayerWaypoints.ContainsKey(w.Position))
                        {
                            // Don't clone this waypoint to the player, they already have a waypoint there
                            if (debug)
                            {
                                ServerApi.Logger.Notification($"Skipping waypoint, player already has it");
                            }
                        }
                        else
                        {
                            if (debug)
                            {
                                ServerApi.Logger.Notification($"Cloning waypoint {w.Icon} to player {fromPlayer.ClientId}");
                            }

                            // TODO: When cloning another user's tombstone, use their name for the name

                            // If we get here, the player doesn't already have the WP and it's not theirs to begin with
                            // Prepare the arguments for calling AddWp
                            string playerName = ServerApi.PlayerData.GetPlayerDataByUid(w.OwningPlayerUid).LastKnownPlayername;
                            string color = "#" + (w.Color & 0xFFFFFF).ToString("X");
                            string title = $"{_sharedWaypointPrefix} {playerName}>{w.Title}";
                            CmdArgs cArgs = new CmdArgs();
                            cArgs.PushSingle(title);
                            cArgs.PushSingle(color);

                            waypointsToAdd.Add(
                                new AddWpArgs
                                {
                                    pos = w.Position,
                                    args = cArgs,
                                    player = fromPlayer,
                                    groupId = GlobalConstants.InfoLogChatGroup,
                                    icon = w.Icon,
                                    pinned = false // Do not pin other players' waypoints by default
                                });
                        }
                    }
                    else
                    {
                        if (debug)
                        {
                            ServerApi.Logger.Notification($"Skipping waypoint to player {fromPlayer.ClientId} as it is a copy (a synced waypoint)");
                        }
                    }
                }
                else
                {
                    if (debug)
                    {
                        ServerApi.Logger.Notification($"Skipping waypoint to player {fromPlayer.ClientId} as it's their own");
                    }
                }
            }

            // Add each waypoint to the user. Doing so will send them the updated information so they display on their map correctly.
            foreach (var addWpArgs in waypointsToAdd)
            {
                waypointLayer.NoLogAddWp(addWpArgs.pos,
                            addWpArgs.args,
                            addWpArgs.player,
                            addWpArgs.groupId,
                            addWpArgs.icon,
                            addWpArgs.pinned);
            }

            if (wsm.LogSuccess)
            {
                fromPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, "Finished Sync", EnumChatType.CommandSuccess);
            }
        }
    }
}