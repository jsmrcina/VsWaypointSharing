using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VsWaypointSharing.Sync
{
    //
    // Pure waypoint-sharing rules, kept free of game API calls so they can be unit tested.
    //
    public static class WaypointSync
    {
        // Every copied waypoint's title starts with this, which is how we tell copies apart from a player's own waypoints
        public const string SharedWaypointPrefix = "<sync from:";

        public static bool IsSharedCopy(Waypoint w)
        {
            return w.Title != null && w.Title.StartsWith(SharedWaypointPrefix, StringComparison.Ordinal);
        }

        public static bool IsSharedCopyOwnedBy(Waypoint w, string playerUid)
        {
            return w.OwningPlayerUid == playerUid && IsSharedCopy(w);
        }

        public static string SharedTitle(string playerName, string title)
        {
            return $"{SharedWaypointPrefix} {playerName}>{title}";
        }

        // Vanilla death waypoints are titled Lang.Get("You died here") with this icon (see WaypointMapLayer.Event_PlayerDeath)
        public const string DeathWaypointIcon = "gravestone";

        // The title a copy gets: "<sync from: Bob>Iron mine", or "<sync from: Bob>Bob died here" for Bob's death waypoint
        public static string CopyTitle(Waypoint source, string playerName, string deathWaypointTitle)
        {
            bool isDeathWaypoint = deathWaypointTitle != null && source.Icon == DeathWaypointIcon && source.Title == deathWaypointTitle;
            return SharedTitle(playerName, isDeathWaypoint ? $"{playerName} died here" : source.Title);
        }

        /*
            A copy's Guid is derived from the requesting player and the source waypoint, so the same source always maps to
            the same copy. That lets a sync update copies in place instead of recreating them, which keeps their order
            (vanilla /waypoint remove and modify, and mods like Auto Map Markers, address waypoints by index) and any pin
            the player set on them. Death waypoints have no Guid in vanilla, so those fall back to owner + position.
        */
        public static string CopyGuid(string requestingPlayerUid, Waypoint source)
        {
            string sourceKey = !string.IsNullOrEmpty(source.Guid)
                ? source.Guid
                : $"{source.OwningPlayerUid}@{source.Position.X:R},{source.Position.Y:R},{source.Position.Z:R}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"vswaypointsharing|{requestingPlayerUid}|{sourceKey}"));
            return new Guid(hash.AsSpan(0, 16)).ToString();
        }

        // Copies are always fully opaque, matching what /waypoint add produces
        public static int OpaqueColor(int argb)
        {
            return (argb & 0xFFFFFF) | unchecked((int)0xFF000000);
        }

        /*
            Works out which waypoints from other players should be copied onto the requesting player. A waypoint is copied when:
                1. It belongs to someone else
                2. It is not itself a copy (otherwise copies would multiply on every sync)
                3. The requesting player has no waypoint of their own at that exact position
            Previous copies owned by the requesting player are ignored here; the caller replaces them wholesale, so that
            waypoints another player deleted also disappear for the requesting player.
        */
        public static List<Waypoint> PlanSharedCopies(IEnumerable<Waypoint> waypoints, string requestingPlayerUid, Func<string, string> playerNameForUid, string deathWaypointTitle = null)
        {
            var all = new List<Waypoint>(waypoints);

            // For now, we only compare on location, so things like icon and name are ignored.
            var ownPositions = new HashSet<Vec3d>();
            foreach (Waypoint w in all)
            {
                if (w.OwningPlayerUid == requestingPlayerUid && !IsSharedCopy(w))
                {
                    ownPositions.Add(w.Position);
                }
            }

            var copies = new List<Waypoint>();
            foreach (Waypoint w in all)
            {
                if (w.OwningPlayerUid == requestingPlayerUid || IsSharedCopy(w) || ownPositions.Contains(w.Position))
                {
                    continue;
                }

                copies.Add(new Waypoint()
                {
                    Color = OpaqueColor(w.Color),
                    OwningPlayerUid = requestingPlayerUid,
                    Position = w.Position.Clone(),
                    Title = CopyTitle(w, playerNameForUid(w.OwningPlayerUid), deathWaypointTitle),
                    Icon = w.Icon,
                    Pinned = false, // Do not pin other players' waypoints by default
                    Guid = CopyGuid(requestingPlayerUid, w)
                });
            }

            return copies;
        }

        /*
            Brings the requesting player's copies in `waypoints` in line with `planned` (from PlanSharedCopies), in place:
                - a copy whose source still exists is updated where it is (title, color, icon, position; the player's pin is kept)
                - a copy whose source is gone (or a copy from 1.0.x, which used random Guids) is removed
                - new copies are appended
            Returns whether anything changed, so callers only resend the player's waypoints when needed.
        */
        public static bool ApplySharedCopies(List<Waypoint> waypoints, string requestingPlayerUid, IEnumerable<Waypoint> planned)
        {
            var pending = new Dictionary<string, Waypoint>();
            var order = new List<string>();
            foreach (Waypoint p in planned)
            {
                if (pending.TryAdd(p.Guid, p))
                {
                    order.Add(p.Guid);
                }
            }

            bool changed = false;
            for (int i = 0; i < waypoints.Count; i++)
            {
                Waypoint existing = waypoints[i];
                if (!IsSharedCopyOwnedBy(existing, requestingPlayerUid))
                {
                    continue;
                }

                if (existing.Guid != null && pending.Remove(existing.Guid, out Waypoint update))
                {
                    if (existing.Title != update.Title || existing.Color != update.Color || existing.Icon != update.Icon || !update.Position.Equals(existing.Position))
                    {
                        existing.Title = update.Title;
                        existing.Color = update.Color;
                        existing.Icon = update.Icon;
                        existing.Position = update.Position;
                        changed = true;
                    }
                }
                else
                {
                    waypoints.RemoveAt(i);
                    i--;
                    changed = true;
                }
            }

            foreach (string guid in order)
            {
                if (pending.TryGetValue(guid, out Waypoint added))
                {
                    waypoints.Add(added);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
