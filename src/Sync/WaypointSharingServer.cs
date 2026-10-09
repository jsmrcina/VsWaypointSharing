using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VsWaypointSharing.Models.Networking;

namespace VsWaypointSharing.Sync
{
    //
    // Server side of the mod: handles the client requests and the auto-sync timer.
    // Kept apart from the ModSystem so it can be driven directly by tests.
    //
    public class WaypointSharingServer
    {
        private class SharingState
        {
            public bool isAutoSyncEnabled = false;
        }

        private readonly ICoreServerAPI ServerApi;
        private readonly Func<WaypointMapLayerExtension> findWaypointLayer;
        private readonly Func<string> deathWaypointTitle;
        private readonly bool debug;
        private bool conflictingModErrorLogged = false;

        // TODO: Persist to save game?
        private readonly Dictionary<IServerPlayer, SharingState> clientStates = new Dictionary<IServerPlayer, SharingState>();

        // deathWaypointTitle defaults to the server's translation of the vanilla death waypoint title (looked up lazily, once Lang is loaded)
        public WaypointSharingServer(ICoreServerAPI api, Func<WaypointMapLayerExtension> findWaypointLayer, bool debug, Func<string> deathWaypointTitle = null)
        {
            ServerApi = api ?? throw new ArgumentException("Server API is null");
            this.findWaypointLayer = findWaypointLayer;
            this.debug = debug;
            this.deathWaypointTitle = deathWaypointTitle ?? (() => Lang.Get("You died here"));
        }

        // Called once the map layers exist, so a conflicting mod shows up in the log at startup rather than on first use
        public bool CheckWaypointLayer()
        {
            if (GetWaypointLayer() == null)
            {
                return false;
            }

            ServerApi.Logger.Notification("VsWaypointSharing: waypoint layer hooked, sharing is available");
            return true;
        }

        public bool IsAutoSyncEnabled(IServerPlayer player)
        {
            return clientStates.TryGetValue(player, out var state) && state.isAutoSyncEnabled;
        }

        public void OnPlayerLeaveDisconnect(IServerPlayer player)
        {
            clientStates.Remove(player);
        }

        public void AutoSyncTick()
        {
            if (debug)
            {
                ServerApi.Logger.Notification($"Auto-sync thread running");
            }

            // Snapshot, since a failure below turns auto-sync off for that player
            foreach (var sharingState in clientStates.ToList())
            {
                if (sharingState.Value.isAutoSyncEnabled)
                {
                    if (debug)
                    {
                        ServerApi.Logger.Notification($"Auto-syncing client {sharingState.Key.PlayerUID}");
                    }

                    // This must never throw: the server's Timer only consumes an interval after the handler returns, so an
                    // exception here re-runs it on every server tick (1.0.4 filled a server's disk with 23 GB of logs that way)
                    try
                    {
                        OnShareRequested(sharingState.Key, new WaypointShareMessage { LogSuccess = false });
                    }
                    catch (Exception e)
                    {
                        sharingState.Value.isAutoSyncEnabled = false;
                        ServerApi.Logger.Error($"VsWaypointSharing: auto-sync for {sharingState.Key.PlayerName} failed and was turned off");
                        ServerApi.Logger.Error(e);
                        sharingState.Key.SendMessage(GlobalConstants.InfoLogChatGroup, "Auto-sync hit an error and was turned off (details in the server log)", EnumChatType.CommandError);
                    }
                }
            }
        }

        public void OnToggleAutoSyncRequested(IServerPlayer fromPlayer, WaypointToggleAutoSyncMessage msg)
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

            fromPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, isAutoSyncEnabled ? "Auto-sync is now on" : "Auto-sync is now off", EnumChatType.CommandSuccess);
        }

        public void OnAutoSyncStatusRequested(IServerPlayer fromPlayer, WaypointAutoSyncStatusMessage msg)
        {
            string state = IsAutoSyncEnabled(fromPlayer) ? "on" : "off";
            fromPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, $"Auto-sync is {state}", EnumChatType.CommandSuccess);
        }

        public void OnRevertRequested(IServerPlayer fromPlayer, WaypointRevertMessage msg)
        {
            var waypointLayer = GetWaypointLayer();
            if (waypointLayer == null)
            {
                return;
            }

            waypointLayer.RemoveSharedCopies(fromPlayer);
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
        public void OnShareRequested(IServerPlayer fromPlayer, WaypointShareMessage wsm)
        {
            var waypointLayer = GetWaypointLayer();
            if (waypointLayer == null)
            {
                return;
            }

            List<Waypoint> copies = WaypointSync.PlanSharedCopies(waypointLayer.Waypoints, fromPlayer.PlayerUID, PlayerNameForUid, deathWaypointTitle());
            if (debug)
            {
                ServerApi.Logger.Notification($"Copying {copies.Count} waypoints to player {fromPlayer.PlayerUID}");
            }

            // Copies of waypoints another player deleted go away for fromPlayer too. If anything changed, this
            // also sends the updated list to the player so they display on their map correctly.
            waypointLayer.SyncSharedCopies(fromPlayer, copies);

            if (wsm.LogSuccess)
            {
                fromPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, "Finished Sync", EnumChatType.CommandSuccess);
            }
        }

        private string PlayerNameForUid(string uid)
        {
            return ServerApi.PlayerData.GetPlayerDataByUid(uid)?.LastKnownPlayername ?? "unknown player";
        }

        private WaypointMapLayerExtension GetWaypointLayer()
        {
            var waypointLayer = findWaypointLayer();
            if (waypointLayer == null && !conflictingModErrorLogged)
            {
                // Something has gone wrong
                ServerApi.Logger.Error($"VsWaypointSharing cannot function as the WaypointMapLayer is not WaypointMapLayerExtension class");
                conflictingModErrorLogged = true;
            }
            return waypointLayer;
        }
    }
}
