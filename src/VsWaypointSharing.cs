using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using VsWaypointSharing.Models.Networking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
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
        private readonly string _waypointSharingChannel = "waypointsharing";

        private ICoreClientAPI ClientApi;
        private ICoreServerAPI ServerApi;

        private IServerNetworkChannel ServerChannel;
        private IClientNetworkChannel ClientChannel;

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api ?? throw new ArgumentException("Client API is null");

            ClientChannel = api.Network.RegisterChannel(_waypointSharingChannel)
                            .RegisterMessageType(typeof(WaypointShareMessage));
            //.RegisterMessageType(typeof(WaypointShareResponse));
            //.SetMessageHandler<WaypointShareResponse>(OnShareResponse);

            api.RegisterCommand("ws", "Functions for sharing waypoints", "[share|purgeothers]", OnCmdWs);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            ServerApi = api ?? throw new ArgumentException("Server API is null");

            ServerChannel = api.Network.RegisterChannel(_waypointSharingChannel)
                .RegisterMessageType(typeof(WaypointShareMessage))
                //.RegisterMessageType(typeof(WaypointShareResponse))
                .SetMessageHandler<WaypointShareMessage>(OnShareRequested);
        }

        private void OnCmdWs(int groupId, CmdArgs args)
        {
            string cmd = args.PopWord();

            switch (cmd)
            {
                case "share":
                    ClientApi.SendChatMessage($"Requesting waypoints from server");
                    ClientChannel.SendPacket(new WaypointShareMessage()
                    {
                        Request = "Request"
                    });
                    break;

                case "purgeothers":
                    // TODO: Implement purging other people's way points (I think we'll use a prefix here)
                    // PurgeNonNativeOwnWaypoints();
                    break;
            }
        }

        // private WaypointMapLayer GetWaypointMapLayerOnClient()
        // {
        //     var mapManager = ClientApi.ModLoader.GetModSystem<WorldMapManager>();
        //     return mapManager.MapLayers.FirstOrDefault(x => x.GetType() == typeof(WaypointMapLayer)) as WaypointMapLayer;
        // }

        // private void PurgeNonNativeOwnWaypoints()
        // {
        //     var waypointLayer = GetWaypointMapLayerOnClient();

        //     foreach (Waypoint w in waypointLayer.ownWaypoints)
        //     {
        //         ClientApi.Logger.Notification($"Purging local waypoint: {w.Icon} {w.Position.X} {w.Position.Y} {w.Position.Z} {w.Pinned.ToString().ToLower()} {w.Title}");
        //     }

        //     // TODO
        // }

        // private void PurgeAllOwnWaypoints()
        // {
        //     var waypointLayer = GetWaypointMapLayerOnClient();

        //     ClientApi.Logger.Notification($"Purging All");

        //     foreach (Waypoint w in waypointLayer.ownWaypoints)
        //     {
        //         ClientApi.Logger.Notification($"Purging (all) local waypoint: {w.Icon} {w.Position.X} {w.Position.Y} {w.Position.Z} {w.Pinned.ToString().ToLower()} {w.Title}");
        //     }

        //     waypointLayer.ownWaypoints.Clear();
        // }

        /*
            This gets called when the client requests waypoints. The ideal way to do this would be to send them the waypoints
            and let them update their local cache. Unfortunately, because the WaypointMapLayer is a server-side layer, it refreshes
            the waypoints every time the client opens the map, which will undo any changes to the local client state. After some experimentation,
            our only real option in the current architecture is to actually add all the waypoints from the other players onto the requesting
            player as their own. There are numerous downsides to this:
                1. The player cannot easily purge waypoints from other players without losing their own (TODO: Maybe prefix the added ones in some way?)
                2. It is hard to tell if we are duplicating a waypoint. Currently, we use location only, so if another player
                    changes the icon or color, it won't update when the player requests a new update.
                3. There is no clean way on the server-side to add a waypoint to a player because AddWp inside WaypointLayerMap
                    is private. So we either send another message back to the client and have them do a hack by sending a chat command
                    for each waypoint (yuck, plus we're rate limited). Or we reflect out the private function and call it anyway (double yuck, but ¯\_(ツ)_/¯).
        */
        private void OnShareRequested(IServerPlayer fromPlayer, WaypointShareMessage networkMessage)
        {
            // Client requested waypoints
            var mapManager = ServerApi.ModLoader.GetModSystem<WorldMapManager>();
            var waypointLayer = mapManager.MapLayers.FirstOrDefault(x => x.GetType() == typeof(WaypointMapLayer)) as WaypointMapLayer;
            List<Waypoint> waypoints = waypointLayer.Waypoints;

            // To make sure we don't modify the waypoint during enumeration, we save off the ones to add and then do so
            // afterwards. The object[] is the list of arguments to AddWp inside WaypointMapLayer
            List<object[]> waypointsToAdd = new List<object[]>();

            foreach (Waypoint w in waypoints)
            {
                // Ignore any waypoint that is from the requesting player
                if (w.OwningPlayerUid != fromPlayer.PlayerUID)
                {
                    bool cloneWaypoint = true;

                    // Check that this player does not already have this waypoint (only uses position)
                    foreach (Waypoint w2 in waypoints)
                    {
                        // If this waypoint is owned by the player
                        if (w2.OwningPlayerUid == fromPlayer.PlayerUID)
                        {
                            // And if this waypoint is in the same place as the one we want to clone
                            if (w.Position.X == w2.Position.X &&
                                w.Position.Y == w2.Position.Y &&
                                w.Position.Z == w2.Position.Z)
                            {
                                // Don't clone this waypoint to the player, they already have a waypoint there
                                ServerApi.Logger.Notification($"Skipping waypoint, player already has it");
                                cloneWaypoint = false;
                                break;
                            }
                        }
                    }

                    if (cloneWaypoint)
                    {
                        ServerApi.Logger.Notification($"Cloning waypoint {w.Icon} to player {fromPlayer.ClientId}");

                        // If we get here, the player doesn't already have the WP and it's not theirs to begin with
                        // Prepare the arguments for calling AddWp
                        string color = w.Color.ToString();
                        string title = w.Title;
                        CmdArgs cArgs = new CmdArgs();
                        cArgs.PushSingle(title);
                        cArgs.PushSingle(color);

                        object[] args = new object[] { w.Position,
                            cArgs,
                            fromPlayer,
                            0, // TODO: GroupId means what?
                            w.Icon,
                            w.Pinned };

                        waypointsToAdd.Add(args);
                    }
                }
                else
                {
                    ServerApi.Logger.Notification($"Skipping waypoint to player {fromPlayer.ClientId} as it's their own");
                }
            }

            // Add each waypoint to the user. Doing so will send them the updated information so they display on their map correctly.
            foreach (var args in waypointsToAdd)
            {
                typeof(WaypointMapLayer).GetMethod("AddWp", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(waypointLayer, args);
            }

            // Serialize to protobuf
            // byte[] waypointsBytes = SerializerUtil.Serialize<List<Waypoint>>(waypoints);

            // IServerPlayer[] players = new IServerPlayer[1] { fromPlayer };
            // var spawn = ServerApi.World.DefaultSpawnPosition.XYZ;
            // ServerChannel.SendPacket(new WaypointShareResponse()
            // {
            //     Message = waypointsBytes,
            //     WorldSpawnPos = spawn
            // }, players);
        }

        // private void OnShareResponse(WaypointShareResponse networkMessage)
        // {
        //     var waypointsBytes = networkMessage.Message;
        //     if (waypointsBytes == null)
        //     {
        //         return;
        //     }

        //     // PurgeAllOwnWaypoints();
        //     var waypointLayer = GetWaypointMapLayerOnClient();

        //     // Debug statements
        //     List<Waypoint> waypoints = SerializerUtil.Deserialize<List<Waypoint>>(waypointsBytes);
        //     foreach (Waypoint w in waypoints)
        //     {
        //         ClientApi.Logger.Notification($"Received waypoint from server: {networkMessage.WorldSpawnPos} {w.Icon} {w.Position.X} {w.Position.Y} {w.Position.Z} {w.Pinned.ToString().ToLower()} {w.Title}");
        //     }

        //     waypointLayer.OnDataFromServer(waypointsBytes);

        //     // waypoints.ToList().ForEach(w =>
        //     // {
        //     //     
        //     //     waypointLayer.ownWaypoints.Add(w);
        //     //     // ClientApi.SendChatMessage($"/waypoint addati {w.Icon} {w.Position.X} {w.Position.Y} {w.Position.Z} {w.Pinned.ToString().ToLower()} {ColorUtil.Int2Hex(w.Color):X} {w.Title}");
        //     // });

        //     // ClientChannel.SendPacket(new WaypointImportResponse()
        //     // {
        //     //     Response = $"{waypoints.Count} waypoints added to map."
        //     // });
        // }

        // private IList<Waypoint> GetWaypoints()
        // {
        //     // Copied from the Essentials mod lol
        //     var waypoints = new List<Waypoint>();
        //     if (ServerApi != null)
        //     {
        //         ClientApi

        //         try
        //         {
        //             byte[] data = ServerApi.WorldManager.SaveGame.GetData("playerMapMarkers_v2");
        //             if (data != null)
        //             {
        //                 waypoints = SerializerUtil.Deserialize<List<Waypoint>>(data);
        //                 ServerApi.World.Logger.Notification("Successfully loaded " + waypoints.Count + " waypoints");
        //             }
        //             else
        //             {
        //                 data = ServerApi.WorldManager.SaveGame.GetData("playerMapMarkers");
        //                 if (data != null) waypoints = JsonUtil.FromBytes<List<Waypoint>>(data);
        //             }

        //             for (int i = 0; i < waypoints.Count; i++)
        //             {
        //                 var wp = waypoints[i];
        //                 if (wp.Title == null) wp.Title = wp.Text; // Not sure how this happenes. For some reason the title moved into text
        //                 if (wp == null)
        //                 {
        //                     ServerApi.World.Logger.Error("Waypoint with no position loaded, will remove");
        //                     waypoints.RemoveAt(i);
        //                     i--;
        //                 }
        //             }
        //         }
        //         catch (Exception e)
        //         {
        //             ServerApi.World.Logger.Error("Failed deserializing player map markers. Won't load them, sorry! Exception thrown: ", e);
        //         }
        //         return waypoints;
        //     }
        //     return waypoints;
        // }
    }
}