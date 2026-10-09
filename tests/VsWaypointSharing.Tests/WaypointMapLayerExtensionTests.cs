using System.Linq;
using VsWaypointSharing.Sync;
using Xunit;

namespace VsWaypointSharing.Tests
{
    public class WaypointMapLayerExtensionTests
    {
        [Fact]
        public void SyncSharedCopies_AddsCopiesAndSendsThemToThePlayerOnce()
        {
            var world = new TestWorld();
            var alice = world.AddPlayer("a", "Alice");
            world.AddWaypoint("a", "Mine", 1, 1, 1);
            var copies = new[] { Waypoints.Make("a", WaypointSync.SharedTitle("Bob", "Farm"), 2, 2, 2) };

            Assert.True(world.Layer.SyncSharedCopies(alice, copies));

            Assert.Equal(2, world.Layer.Waypoints.Count);
            Assert.Equal(1, world.SendCount("a"));
            Assert.Equal(new[] { "Mine", "<sync from: Bob>Farm" }, world.LastSentTo("a").Select(w => w.Title));
        }

        [Fact]
        public void SyncSharedCopies_DropsThePlayersStaleCopiesOnly()
        {
            var world = new TestWorld();
            var alice = world.AddPlayer("a", "Alice");
            world.AddWaypoint("a", "Mine", 1, 1, 1);
            world.AddWaypoint("a", WaypointSync.SharedTitle("Bob", "Deleted by Bob"), 2, 2, 2);
            world.AddWaypoint("c", WaypointSync.SharedTitle("Bob", "Carol's copy"), 2, 2, 2);
            world.AddWaypoint("b", "Bob's own", 3, 3, 3);

            world.Layer.SyncSharedCopies(alice, new[] { Waypoints.Make("a", WaypointSync.SharedTitle("Bob", "Bob's own"), 3, 3, 3) });

            Assert.Equal(new[] { "Mine", "<sync from: Bob>Carol's copy", "Bob's own", "<sync from: Bob>Bob's own" },
                world.Layer.Waypoints.Select(w => w.Title));
        }

        [Fact]
        public void SyncSharedCopies_ResendsWhenTheLastCopyIsRemoved()
        {
            var world = new TestWorld();
            var alice = world.AddPlayer("a", "Alice");
            world.AddWaypoint("a", WaypointSync.SharedTitle("Bob", "Gone"), 2, 2, 2);

            Assert.True(world.Layer.SyncSharedCopies(alice, new Vintagestory.GameContent.Waypoint[0]));

            Assert.Empty(world.Layer.Waypoints);
            Assert.Empty(world.LastSentTo("a"));
        }

        [Fact]
        public void SyncSharedCopies_DoesNotResendWhenNothingChanged()
        {
            var world = new TestWorld();
            var alice = world.AddPlayer("a", "Alice");
            var copy = Waypoints.Make("a", WaypointSync.SharedTitle("Bob", "Farm"), 2, 2, 2);
            world.Layer.SyncSharedCopies(alice, new[] { copy });

            var sameCopy = Waypoints.Make("a", copy.Title, 2, 2, 2, color: copy.Color, icon: copy.Icon);
            sameCopy.Guid = copy.Guid;
            Assert.False(world.Layer.SyncSharedCopies(alice, new[] { sameCopy }));

            Assert.Equal(1, world.SendCount("a"));
        }

        [Fact]
        public void RemoveSharedCopies_KeepsOwnWaypointsAndOtherPlayersCopies()
        {
            var world = new TestWorld();
            var alice = world.AddPlayer("a", "Alice");
            world.AddWaypoint("a", "Mine", 1, 1, 1);
            world.AddWaypoint("a", WaypointSync.SharedTitle("Bob", "Farm"), 2, 2, 2);
            world.AddWaypoint("b", WaypointSync.SharedTitle("Alice", "Mine"), 1, 1, 1);

            world.Layer.RemoveSharedCopies(alice);

            Assert.Equal(new[] { "Mine", "<sync from: Alice>Mine" }, world.Layer.Waypoints.Select(w => w.Title));
            Assert.Equal(new[] { "Mine" }, world.LastSentTo("a").Select(w => w.Title));
        }

        [Fact]
        public void PlayerOnlyReceivesTheirOwnWaypoints()
        {
            var world = new TestWorld();
            var alice = world.AddPlayer("a", "Alice");
            world.AddWaypoint("a", "Mine", 1, 1, 1);
            world.AddWaypoint("b", "Bob's", 2, 2, 2);

            world.Layer.RemoveSharedCopies(alice);

            Assert.Equal(new[] { "Mine" }, world.LastSentTo("a").Select(w => w.Title));
            Assert.Equal(0, world.SendCount("b"));
        }
    }
}
