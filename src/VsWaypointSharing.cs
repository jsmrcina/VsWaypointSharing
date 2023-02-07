using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using VsWaypointSharing.Models.Networking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using System.Reflection;

[assembly: ModInfo("VsWaypointSharing",
    Description = "Allows sharing waypoints to other users",
    Website = "",
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
        // TODO: Persist to save game
        private Dictionary<string, SharingState> clientStates = new Dictionary<string, SharingState>();

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api ?? throw new ArgumentException("Client API is null");

            ClientChannel = api.Network.RegisterChannel(_waypointSharingChannel)
                            .RegisterMessageType(typeof(WaypointShareMessage))
                            .RegisterMessageType(typeof(WaypointRevertMessage))
                            .RegisterMessageType(typeof(WaypointToggleAutoSyncMessage))
                            .RegisterMessageType(typeof(WaypointResponseMessage))
                            .SetMessageHandler<WaypointResponseMessage>(OnResponseReceived);

            api.RegisterCommand("ws", "Functions for sharing waypoints", "[sync|revert|autosync]", OnCmdWs);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            ServerApi = api ?? throw new ArgumentException("Server API is null");

            ServerChannel = api.Network.RegisterChannel(_waypointSharingChannel)
                .RegisterMessageType(typeof(WaypointShareMessage))
                .RegisterMessageType(typeof(WaypointRevertMessage))
                .RegisterMessageType(typeof(WaypointToggleAutoSyncMessage))
                .RegisterMessageType(typeof(WaypointResponseMessage))
                .SetMessageHandler<WaypointShareMessage>(OnShareRequested)
                .SetMessageHandler<WaypointRevertMessage>(OnRevertRequested)
                .SetMessageHandler<WaypointToggleAutoSyncMessage>(OnToggleAutoSyncRequested);
        }

        private void OnCmdWs(int groupId, CmdArgs args)
        {
            string cmd = args.PopWord();

            switch (cmd)
            {
                case "sync":
                    ClientApi.SendChatMessage($"Requesting waypoints from server");
                    ClientChannel.SendPacket(new WaypointShareMessage());
                    break;

                case "revert":
                    ClientApi.SendChatMessage($"Reverting to only local waypoints");
                    ClientChannel.SendPacket(new WaypointRevertMessage());
                    break;
                case "autosync":
                    ClientApi.SendChatMessage($"Toggling auto-sync");
                    ClientChannel.SendPacket(new WaypointShareMessage());
                    break;
            }
        }

        private void OnResponseReceived(WaypointResponseMessage msg)
        {
            ClientApi.SendChatMessage($"Auto-sync state toggled to {msg.isAutoSyncEnabled}");
        }

        private void OnRevertRequested(IServerPlayer fromPlayer, WaypointRevertMessage msg)
        {
            // Get waypoint layer
            var mapManager = ServerApi.ModLoader.GetModSystem<WorldMapManager>();
            var waypointLayer = mapManager.MapLayers.FirstOrDefault(x => x.GetType() == typeof(WaypointMapLayer)) as WaypointMapLayer;
            List<Waypoint> waypoints = waypointLayer.Waypoints;

            // To make sure we don't modify the waypoint during enumeration,
            // we save off the ones to remove and then do so afterwards.
            List<Waypoint> waypointsToRemove = new List<Waypoint>();

            // Dictionary<Vec3d, bool> waypointsToRemove = new Dictionary<Vec3d, bool>();
            foreach (Waypoint w in waypoints)
            {
                if (w.OwningPlayerUid == fromPlayer.PlayerUID &&
                    w.Title.StartsWith(_sharedWaypointPrefix))
                {
                    waypointsToRemove.Add(w);
                }
            }

            waypoints.RemoveAll(x => x.OwningPlayerUid == fromPlayer.PlayerUID && x.Title.StartsWith(_sharedWaypointPrefix));
            // TODO: This won't sync to the user until they reopen the map
        }

        private void OnToggleAutoSyncRequested(IServerPlayer fromPlayer, WaypointToggleAutoSyncMessage msg)
        {
            if (!clientStates.ContainsKey(fromPlayer.PlayerUID))
            {
                clientStates.Add(fromPlayer.PlayerUID, new SharingState { isAutoSyncEnabled = true });
            }
            else
            {
                SharingState state = clientStates[fromPlayer.PlayerUID];
                if (state.isAutoSyncEnabled)
                {
                    state.isAutoSyncEnabled = false;
                }
                else
                {
                    state.isAutoSyncEnabled = true;
                }
            }

            // TODO: Sync on timer for enabled clients
            // TODO: Deal with disconnects
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
                3. There is no clean way on the server-side to add a waypoint to a player because AddWp inside WaypointLayerMap
                    is private. So we either send another message back to the client and have them do a hack by sending a chat command
                    for each waypoint (yuck, plus we're rate limited). Or we reflect out the private function and call it anyway (double yuck, but ¯\_(ツ)_/¯).
        */
        private void OnShareRequested(IServerPlayer fromPlayer, WaypointShareMessage msg)
        {
            // Get waypoint layer
            var mapManager = ServerApi.ModLoader.GetModSystem<WorldMapManager>();
            var waypointLayer = mapManager.MapLayers.FirstOrDefault(x => x.GetType() == typeof(WaypointMapLayer)) as WaypointMapLayer;
            List<Waypoint> waypoints = waypointLayer.Waypoints;

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
                    // Check that this player does not already have this waypoint (only uses position)
                    if (fromPlayerWaypoints.ContainsKey(w.Position))
                    {
                        // Don't clone this waypoint to the player, they already have a waypoint there
                        ServerApi.Logger.Notification($"Skipping waypoint, player already has it");
                    }
                    else
                    {
                        ServerApi.Logger.Notification($"Cloning waypoint {w.Icon} to player {fromPlayer.ClientId}");

                        // If we get here, the player doesn't already have the WP and it's not theirs to begin with
                        // Prepare the arguments for calling AddWp
                        string color = w.Color.ToString();
                        string title = $"{_sharedWaypointPrefix} {w.OwningPlayerUid}>: {w.Title}";
                        CmdArgs cArgs = new CmdArgs();
                        cArgs.PushSingle(title);
                        cArgs.PushSingle(color);

                        waypointsToAdd.Add(
                            new AddWpArgs
                            {
                                pos = w.Position,
                                args = cArgs,
                                player = fromPlayer,
                                groupId = 0, // TODO: GroupId means what?
                                icon = w.Icon,
                                pinned = w.Pinned
                            });
                    }
                }
                else
                {
                    ServerApi.Logger.Notification($"Skipping waypoint to player {fromPlayer.ClientId} as it's their own");
                }
            }

            // Add each waypoint to the user. Doing so will send them the updated information so they display on their map correctly.
            foreach (var addWpArgs in waypointsToAdd)
            {
                object[] argsAsObjectArray = new object[]
                {
                    addWpArgs.pos,
                    addWpArgs.args,
                    addWpArgs.player,
                    addWpArgs.groupId,
                    addWpArgs.icon,
                    addWpArgs.pinned
                };

                typeof(WaypointMapLayer).GetMethod("AddWp", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(waypointLayer, argsAsObjectArray);
            }
        }
    }
}