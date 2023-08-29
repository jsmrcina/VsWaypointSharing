using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using System.Reflection;

namespace Vintagestory.GameContent
{
    //
    // Note that this only works as long as no one else tries to do the same thing, which isn't an ideal situation for
    // a mod to assume. TODO: Maybe I can do this with Harmony? Though I imagine that would run into a similar issue
    // if I patch the WaypointMapLayer class.
    //
    public class WaypointMapLayerExtension : WaypointMapLayer
    {
        public WaypointMapLayerExtension(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
        {

        }

        // This is just AddWp from WaypointMapLayer.cs in the essentials mod but without logging on add
        public void NoLogAddWp(Vec3d pos, CmdArgs args, IServerPlayer player, int groupId, string icon, bool pinned)
        {
            if (args.Length == 0)
            {
                player.SendMessage(groupId, Lang.Get("command-waypoint-syntax"), EnumChatType.CommandError);
                return;
            }

            string colorstring = args.PopWord();
            string title = args.PopAll();

            System.Drawing.Color parsedColor;

            if (colorstring.StartsWith("#"))
            {
                try
                {
                    int argb = int.Parse(colorstring.Replace("#", ""), NumberStyles.HexNumber);
                    parsedColor = System.Drawing.Color.FromArgb(argb);
                }
                catch (FormatException)
                {
                    player.SendMessage(groupId, Lang.Get("command-waypoint-invalidcolor"), EnumChatType.CommandError);
                    return;
                }
            }
            else
            {
                parsedColor = System.Drawing.Color.FromName(colorstring);
            }

            if (title == null || title.Length == 0)
            {
                player.SendMessage(groupId, Lang.Get("command-waypoint-notext"), EnumChatType.CommandError);
                return;
            }

            Waypoint waypoint = new Waypoint()
            {
                Color = parsedColor.ToArgb() | (255 << 24),
                OwningPlayerUid = player.PlayerUID,
                Position = pos,
                Title = title,
                Icon = icon,
                Pinned = pinned,
                Guid = Guid.NewGuid().ToString()
            };

            AddWaypoint(waypoint, player);
        }

        public void NoLogRemoveWp(IServerPlayer player, string sharedWaypointPrefix)
        {
            Waypoints.RemoveAll(x => x.OwningPlayerUid == player.PlayerUID && x.Title.StartsWith(sharedWaypointPrefix));

            // To get the waypoints to update immediately, we have to call two private methods in the base class, so we use reflection here
            typeof(WaypointMapLayer).GetMethod("RebuildMapComponents", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(this, null);
            object[] argsAsObjectArray = new object[] { player };
            typeof(WaypointMapLayer).GetMethod("ResendWaypoints", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(this, argsAsObjectArray);
        }
    }
}