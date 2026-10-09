using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using VsWaypointSharing.Sync;

namespace Vintagestory.GameContent
{
    //
    // Note that this only works as long as no one else tries to do the same thing, which isn't an ideal situation for
    // a mod to assume. TODO: Maybe I can do this with Harmony? Though I imagine that would run into a similar issue
    // if I patch the WaypointMapLayer class.
    //
    public class WaypointMapLayerExtension : WaypointMapLayer
    {
        // To get the waypoints to update immediately, we have to call two private methods in the base class, so we use reflection here.
        // These are looked up once; the unit tests check they still exist after a game update.
        internal static readonly MethodInfo RebuildMapComponentsMethod =
            typeof(WaypointMapLayer).GetMethod("RebuildMapComponents", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
        internal static readonly MethodInfo ResendWaypointsMethod =
            typeof(WaypointMapLayer).GetMethod("ResendWaypoints", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(IServerPlayer) }, null);

        public WaypointMapLayerExtension(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
        {

        }

        // Updates the player's synced copies in place (see WaypointSync.ApplySharedCopies) and sends them the result if anything changed
        public bool SyncSharedCopies(IServerPlayer player, IEnumerable<Waypoint> copies)
        {
            bool changed = WaypointSync.ApplySharedCopies(Waypoints, player.PlayerUID, copies);
            if (changed)
            {
                Refresh(player);
            }
            return changed;
        }

        public void RemoveSharedCopies(IServerPlayer player)
        {
            Waypoints.RemoveAll(x => WaypointSync.IsSharedCopyOwnedBy(x, player.PlayerUID));
            Refresh(player);
        }

        private void Refresh(IServerPlayer player)
        {
            if (RebuildMapComponentsMethod == null || ResendWaypointsMethod == null)
            {
                throw new MissingMethodException("WaypointMapLayer.RebuildMapComponents/ResendWaypoints not found; the game API has changed");
            }

            RebuildMapComponentsMethod.Invoke(this, null);
            ResendWaypointsMethod.Invoke(this, new object[] { player });
        }
    }
}
