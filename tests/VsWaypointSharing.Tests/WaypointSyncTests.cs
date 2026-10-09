using System.Collections.Generic;
using System.Linq;
using Vintagestory.GameContent;
using VsWaypointSharing.Sync;
using Xunit;

namespace VsWaypointSharing.Tests
{
    public class WaypointSyncTests
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string> { ["a"] = "Alice", ["b"] = "Bob", ["c"] = "Carol" };

        private static List<Waypoint> Plan(IEnumerable<Waypoint> waypoints, string forUid)
        {
            return WaypointSync.PlanSharedCopies(waypoints, forUid, uid => Names[uid]);
        }

        [Theory]
        [InlineData("<sync from: Bob>Home", true)]
        [InlineData("<sync from:", true)]
        [InlineData("Home", false)]
        [InlineData(" <sync from: Bob>Home", false)]
        [InlineData("<SYNC FROM: Bob>Home", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsSharedCopy_MatchesOnlyTheExactPrefix(string title, bool expected)
        {
            Assert.Equal(expected, WaypointSync.IsSharedCopy(Waypoints.Make("a", title, 0, 0, 0)));
        }

        [Fact]
        public void IsSharedCopyOwnedBy_RequiresBothOwnerAndPrefix()
        {
            var copy = Waypoints.Make("a", WaypointSync.SharedTitle("Bob", "Home"), 0, 0, 0);
            Assert.True(WaypointSync.IsSharedCopyOwnedBy(copy, "a"));
            Assert.False(WaypointSync.IsSharedCopyOwnedBy(copy, "b"));
            Assert.False(WaypointSync.IsSharedCopyOwnedBy(Waypoints.Make("a", "Home", 0, 0, 0), "a"));
        }

        [Fact]
        public void SharedTitle_HasTheDocumentedFormat()
        {
            Assert.Equal("<sync from: Bob>Iron mine", WaypointSync.SharedTitle("Bob", "Iron mine"));
        }

        [Theory]
        [InlineData(0x00112233, unchecked((int)0xFF112233))]
        [InlineData(0x7F112233, unchecked((int)0xFF112233))]
        [InlineData(unchecked((int)0xFFABCDEF), unchecked((int)0xFFABCDEF))]
        [InlineData(0, unchecked((int)0xFF000000))]
        [InlineData(-1, -1)]
        public void OpaqueColor_KeepsRgbAndForcesFullAlpha(int input, int expected)
        {
            Assert.Equal(expected, WaypointSync.OpaqueColor(input));
        }

        [Fact]
        public void Plan_CopiesOtherPlayersWaypointsWithAllFieldsMapped()
        {
            var bobs = Waypoints.Make("b", "Iron mine", 10, 20, 30, color: 0x00ABCDEF, icon: "pick", pinned: true);

            var copy = Assert.Single(Plan(new[] { bobs }, "a"));

            Assert.Equal("a", copy.OwningPlayerUid);
            Assert.Equal("<sync from: Bob>Iron mine", copy.Title);
            Assert.Equal(unchecked((int)0xFFABCDEF), copy.Color);
            Assert.Equal("pick", copy.Icon);
            Assert.False(copy.Pinned); // other players' waypoints are never pinned
            Assert.Equal(bobs.Position, copy.Position);
            Assert.NotSame(bobs.Position, copy.Position);
            Assert.False(string.IsNullOrEmpty(copy.Guid));
            Assert.NotEqual(bobs.Guid, copy.Guid);
            Assert.Equal(-1, copy.OwningPlayerGroupId);
        }

        [Fact]
        public void Plan_DoesNotModifyTheSourceWaypoints()
        {
            var bobs = Waypoints.Make("b", "Iron mine", 10, 20, 30, color: 0x00ABCDEF);
            var input = new List<Waypoint> { bobs };

            Plan(input, "a");

            Assert.Single(input);
            Assert.Equal("b", bobs.OwningPlayerUid);
            Assert.Equal("Iron mine", bobs.Title);
            Assert.Equal(0x00ABCDEF, bobs.Color);
        }

        [Fact]
        public void Plan_SkipsTheRequestersOwnWaypoints()
        {
            Assert.Empty(Plan(new[] { Waypoints.Make("a", "Mine", 1, 2, 3) }, "a"));
        }

        [Fact]
        public void Plan_SkipsCopiesSoTheyDoNotMultiply()
        {
            var bobsCopyOfCarol = Waypoints.Make("b", WaypointSync.SharedTitle("Carol", "Farm"), 1, 2, 3);
            Assert.Empty(Plan(new[] { bobsCopyOfCarol }, "a"));
        }

        [Fact]
        public void Plan_SkipsPositionsTheRequesterAlreadyHas()
        {
            var mine = Waypoints.Make("a", "My base", 5, 5, 5);
            var bobsSameSpot = Waypoints.Make("b", "Bob's name for it", 5, 5, 5, icon: "home");

            Assert.Empty(Plan(new[] { mine, bobsSameSpot }, "a"));
        }

        [Fact]
        public void Plan_PositionMatchIsExact()
        {
            var mine = Waypoints.Make("a", "My base", 5, 5, 5);
            var bobsNearby = Waypoints.Make("b", "Nearby", 5, 5, 5.5);

            Assert.Single(Plan(new[] { mine, bobsNearby }, "a"));
        }

        [Fact]
        public void Plan_OldCopiesDoNotBlockFreshOnes()
        {
            // a's stale copy of Bob's waypoint is replaced, not treated as "a already has a waypoint here"
            var staleCopy = Waypoints.Make("a", WaypointSync.SharedTitle("Bob", "Old name"), 5, 5, 5);
            var bobs = Waypoints.Make("b", "New name", 5, 5, 5);

            var copy = Assert.Single(Plan(new[] { staleCopy, bobs }, "a"));
            Assert.Equal("<sync from: Bob>New name", copy.Title);
        }

        [Fact]
        public void Plan_RequesterWithTwoWaypointsAtTheSameSpotDoesNotThrow()
        {
            // 1.0.x used Dictionary.Add here, which threw and aborted the whole sync
            var input = new[]
            {
                Waypoints.Make("a", "One", 5, 5, 5),
                Waypoints.Make("a", "Two", 5, 5, 5),
                Waypoints.Make("b", "Elsewhere", 9, 9, 9),
            };

            Assert.Single(Plan(input, "a"));
        }

        [Fact]
        public void Plan_CopiesFromSeveralPlayers()
        {
            var input = new[]
            {
                Waypoints.Make("b", "Bob 1", 1, 0, 0),
                Waypoints.Make("b", "Bob 2", 2, 0, 0),
                Waypoints.Make("c", "Carol 1", 3, 0, 0),
                Waypoints.Make("a", "Alice 1", 4, 0, 0),
            };

            var titles = Plan(input, "a").Select(w => w.Title).ToList();
            Assert.Equal(new[] { "<sync from: Bob>Bob 1", "<sync from: Bob>Bob 2", "<sync from: Carol>Carol 1" }, titles);
        }

        [Fact]
        public void Plan_TwoOtherPlayersAtTheSameSpotBothGetCopied()
        {
            var input = new[] { Waypoints.Make("b", "Bob's", 1, 0, 0), Waypoints.Make("c", "Carol's", 1, 0, 0) };
            Assert.Equal(2, Plan(input, "a").Count);
        }

        [Fact]
        public void Plan_IgnoresWaypointsWithoutTitlesFromBeingTreatedAsCopies()
        {
            var untitled = Waypoints.Make("b", null, 1, 0, 0);
            var copy = Assert.Single(Plan(new[] { untitled }, "a"));
            Assert.Equal("<sync from: Bob>", copy.Title);
        }

        [Fact]
        public void CopyGuid_IsStablePerRequesterAndSource()
        {
            var bobs = Waypoints.Make("b", "Copper", 1, 2, 3);
            var other = Waypoints.Make("b", "Tin", 1, 2, 3);

            Assert.Equal(WaypointSync.CopyGuid("a", bobs), WaypointSync.CopyGuid("a", bobs));
            Assert.NotEqual(WaypointSync.CopyGuid("a", bobs), WaypointSync.CopyGuid("c", bobs));
            Assert.NotEqual(WaypointSync.CopyGuid("a", bobs), WaypointSync.CopyGuid("a", other));
            Assert.True(System.Guid.TryParse(WaypointSync.CopyGuid("a", bobs), out _));
        }

        [Fact]
        public void CopyGuid_FallsBackToOwnerAndPositionWithoutASourceGuid()
        {
            // Vanilla death waypoints are created without a Guid
            var death1 = Waypoints.Make("b", "You died here", 1, 2, 3); death1.Guid = null;
            var death2 = Waypoints.Make("b", "You died here", 1, 2, 3); death2.Guid = null;
            var elsewhere = Waypoints.Make("b", "You died here", 1, 2, 4); elsewhere.Guid = null;

            Assert.Equal(WaypointSync.CopyGuid("a", death1), WaypointSync.CopyGuid("a", death2));
            Assert.NotEqual(WaypointSync.CopyGuid("a", death1), WaypointSync.CopyGuid("a", elsewhere));
        }

        [Theory]
        [InlineData("You died here", "gravestone", "<sync from: Bob>Bob died here")]
        [InlineData("You died here", "circle", "<sync from: Bob>You died here")]
        [InlineData("Graveyard", "gravestone", "<sync from: Bob>Graveyard")]
        public void CopyTitle_RenamesOnlyVanillaDeathWaypoints(string title, string icon, string expected)
        {
            Assert.Equal(expected, WaypointSync.CopyTitle(Waypoints.Make("b", title, 0, 0, 0, icon: icon), "Bob", "You died here"));
        }

        [Fact]
        public void CopyTitle_WithoutAKnownDeathTitleLeavesTitlesAlone()
        {
            Assert.Equal("<sync from: Bob>You died here", WaypointSync.CopyTitle(Waypoints.Make("b", "You died here", 0, 0, 0, icon: "gravestone"), "Bob", null));
        }

        private static Waypoint CopyOf(string requester, Waypoint source, string playerName = "Bob")
        {
            return Assert.Single(WaypointSync.PlanSharedCopies(new[] { source }, requester, uid => playerName));
        }

        [Fact]
        public void Apply_UpdatesInPlaceAndKeepsThePin()
        {
            var bobs = Waypoints.Make("b", "Copper", 10, 0, 0);
            var list = new List<Waypoint> { Waypoints.Make("a", "Home", 0, 0, 0) };
            WaypointSync.ApplySharedCopies(list, "a", new[] { CopyOf("a", bobs) });
            list.Add(Waypoints.Make("a", "Later", 20, 0, 0));
            var copy = list[1];
            copy.Pinned = true;

            bobs.Title = "Copper (rich)";
            bobs.Color = unchecked((int)0xFF00FF00);
            bobs.Icon = "pick";
            Assert.True(WaypointSync.ApplySharedCopies(list, "a", new[] { CopyOf("a", bobs) }));

            Assert.Same(copy, list[1]);
            Assert.Equal("<sync from: Bob>Copper (rich)", copy.Title);
            Assert.Equal(unchecked((int)0xFF00FF00), copy.Color);
            Assert.Equal("pick", copy.Icon);
            Assert.True(copy.Pinned);
            Assert.Equal(new[] { "Home", "<sync from: Bob>Copper (rich)", "Later" }, list.Select(w => w.Title));
        }

        [Fact]
        public void Apply_ReportsNoChangeWhenUpToDate()
        {
            var bobs = Waypoints.Make("b", "Copper", 10, 0, 0);
            var list = new List<Waypoint>();
            Assert.True(WaypointSync.ApplySharedCopies(list, "a", new[] { CopyOf("a", bobs) }));
            Assert.False(WaypointSync.ApplySharedCopies(list, "a", new[] { CopyOf("a", bobs) }));
            Assert.Single(list);
        }

        [Fact]
        public void Apply_RemovesCopiesWhoseSourceIsGoneAndAppendsNewOnes()
        {
            var copper = Waypoints.Make("b", "Copper", 10, 0, 0);
            var tin = Waypoints.Make("b", "Tin", 20, 0, 0);
            var list = new List<Waypoint> { Waypoints.Make("a", "Home", 0, 0, 0) };
            WaypointSync.ApplySharedCopies(list, "a", new[] { CopyOf("a", copper) });

            Assert.True(WaypointSync.ApplySharedCopies(list, "a", new[] { CopyOf("a", tin) }));

            Assert.Equal(new[] { "Home", "<sync from: Bob>Tin" }, list.Select(w => w.Title));
        }

        [Fact]
        public void Apply_ReplacesCopiesMadeBy1_0WhichHaveRandomGuids()
        {
            var bobs = Waypoints.Make("b", "Copper", 10, 0, 0);
            var oldCopy = Waypoints.Make("a", WaypointSync.SharedTitle("Bob", "Copper"), 10, 0, 0); // random Guid
            var list = new List<Waypoint> { oldCopy };

            WaypointSync.ApplySharedCopies(list, "a", new[] { CopyOf("a", bobs) });

            var copy = Assert.Single(list);
            Assert.NotSame(oldCopy, copy);
            Assert.Equal(WaypointSync.CopyGuid("a", bobs), copy.Guid);
        }

        [Fact]
        public void Apply_LeavesOwnWaypointsAndOtherPlayersCopiesAlone()
        {
            var own = Waypoints.Make("a", "Home", 0, 0, 0);
            var carolsCopy = Waypoints.Make("c", WaypointSync.SharedTitle("Bob", "Copper"), 10, 0, 0);
            var list = new List<Waypoint> { own, carolsCopy };

            Assert.False(WaypointSync.ApplySharedCopies(list, "a", new Waypoint[0]));
            Assert.Equal(new[] { own, carolsCopy }, list);
        }

        [Fact]
        public void Apply_IgnoresDuplicatePlannedGuids()
        {
            var bobs = Waypoints.Make("b", "Copper", 10, 0, 0);
            var list = new List<Waypoint>();

            WaypointSync.ApplySharedCopies(list, "a", new[] { CopyOf("a", bobs), CopyOf("a", bobs) });

            Assert.Single(list);
        }

        [Fact]
        public void Plan_EmptyInputGivesNoCopies()
        {
            Assert.Empty(Plan(new Waypoint[0], "a"));
        }
    }
}
