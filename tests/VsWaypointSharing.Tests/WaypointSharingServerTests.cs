using System.Linq;
using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VsWaypointSharing.Models.Networking;
using VsWaypointSharing.Sync;
using Xunit;

namespace VsWaypointSharing.Tests
{
    public class WaypointSharingServerTests
    {
        private readonly TestWorld world = new TestWorld();
        private readonly FakeServerPlayer alice;
        private readonly FakeServerPlayer bob;
        private readonly WaypointSharingServer server;

        public WaypointSharingServerTests()
        {
            alice = world.AddPlayer("a", "Alice");
            bob = world.AddPlayer("b", "Bob");
            server = new WaypointSharingServer(world.Api.Object, () => world.Layer, debug: false, deathWaypointTitle: () => "You died here");
        }

        private string[] AliceTitles()
        {
            return world.Layer.Waypoints.Where(w => w.OwningPlayerUid == "a").Select(w => w.Title).ToArray();
        }

        private static int ChatCount(FakeServerPlayer player, string message)
        {
            return player.Messages.Count(m => m == new FakeServerPlayer.ChatMessage(GlobalConstants.InfoLogChatGroup, message, EnumChatType.CommandSuccess));
        }

        [Fact]
        public void Share_CopiesOtherPlayersWaypointsAndConfirms()
        {
            world.AddWaypoint("a", "Home", 0, 0, 0);
            world.AddWaypoint("b", "Copper", 10, 0, 0);

            server.OnShareRequested(alice, new WaypointShareMessage());

            Assert.Equal(new[] { "Home", "<sync from: Bob>Copper" }, AliceTitles());
            Assert.Equal(new[] { "Home", "<sync from: Bob>Copper" }, world.LastSentTo("a").Select(w => w.Title));
            Assert.Equal(1, ChatCount(alice, "Finished Sync"));
        }

        [Fact]
        public void Share_WithLogSuccessFalseIsSilent()
        {
            world.AddWaypoint("b", "Copper", 10, 0, 0);

            server.OnShareRequested(alice, new WaypointShareMessage { LogSuccess = false });

            Assert.Single(AliceTitles());
            Assert.Empty(alice.Messages);
        }

        [Fact]
        public void Share_IsIdempotent()
        {
            world.AddWaypoint("b", "Copper", 10, 0, 0);

            server.OnShareRequested(alice, new WaypointShareMessage());
            server.OnShareRequested(alice, new WaypointShareMessage());
            server.OnShareRequested(alice, new WaypointShareMessage());

            Assert.Equal(new[] { "<sync from: Bob>Copper" }, AliceTitles());
        }

        [Fact]
        public void Share_ReflectsWaypointsTheOtherPlayerDeletedOrRenamed()
        {
            var copper = world.AddWaypoint("b", "Copper", 10, 0, 0);
            var tin = world.AddWaypoint("b", "Tin", 20, 0, 0);
            server.OnShareRequested(alice, new WaypointShareMessage());

            world.Layer.Waypoints.Remove(copper);
            tin.Title = "Tin (depleted)";
            server.OnShareRequested(alice, new WaypointShareMessage());

            Assert.Equal(new[] { "<sync from: Bob>Tin (depleted)" }, AliceTitles());
        }

        [Fact]
        public void Share_DoesNotTouchTheOtherPlayersWaypoints()
        {
            world.AddWaypoint("b", "Copper", 10, 0, 0);

            server.OnShareRequested(alice, new WaypointShareMessage());

            var bobs = Assert.Single(world.Layer.Waypoints, w => w.OwningPlayerUid == "b");
            Assert.Equal("Copper", bobs.Title);
        }

        [Fact]
        public void Share_BothWaysDoesNotEchoCopiesBack()
        {
            world.AddWaypoint("a", "Home", 0, 0, 0);
            world.AddWaypoint("b", "Copper", 10, 0, 0);

            server.OnShareRequested(alice, new WaypointShareMessage());
            server.OnShareRequested(bob, new WaypointShareMessage());
            server.OnShareRequested(alice, new WaypointShareMessage());

            Assert.Equal(new[] { "Home", "<sync from: Bob>Copper" }, AliceTitles());
            Assert.Equal(new[] { "Copper", "<sync from: Alice>Home" },
                world.Layer.Waypoints.Where(w => w.OwningPlayerUid == "b").Select(w => w.Title));
        }

        [Fact]
        public void Share_UsesAPlaceholderWhenTheOwnerHasNoPlayerData()
        {
            world.AddWaypoint("ghost", "Ruins", 10, 0, 0);

            server.OnShareRequested(alice, new WaypointShareMessage());

            Assert.Equal(new[] { "<sync from: unknown player>Ruins" }, AliceTitles());
        }

        [Fact]
        public void Revert_RemovesOnlyCopies()
        {
            world.AddWaypoint("a", "Home", 0, 0, 0);
            world.AddWaypoint("b", "Copper", 10, 0, 0);
            server.OnShareRequested(alice, new WaypointShareMessage());

            server.OnRevertRequested(alice, new WaypointRevertMessage());

            Assert.Equal(new[] { "Home" }, AliceTitles());
            Assert.Equal(new[] { "Home" }, world.LastSentTo("a").Select(w => w.Title));
        }

        [Fact]
        public void MissingLayer_LogsOneErrorAndDoesNotThrow()
        {
            var broken = new WaypointSharingServer(world.Api.Object, () => null, debug: false);

            broken.OnShareRequested(alice, new WaypointShareMessage());
            broken.OnRevertRequested(alice, new WaypointRevertMessage());
            broken.OnShareRequested(bob, new WaypointShareMessage());

            world.Logger.Verify(l => l.Error(It.Is<string>(s => s.Contains("not WaypointMapLayerExtension"))), Times.Once());
            Assert.Equal(0, ChatCount(alice, "Finished Sync"));
        }

        [Fact]
        public void CheckWaypointLayer_ReportsWhetherTheLayerIsHooked()
        {
            Assert.True(server.CheckWaypointLayer());
            world.Logger.Verify(l => l.Notification(It.Is<string>(s => s.Contains("waypoint layer hooked"))), Times.Once());

            var broken = new WaypointSharingServer(world.Api.Object, () => null, debug: false);
            Assert.False(broken.CheckWaypointLayer());
            world.Logger.Verify(l => l.Error(It.Is<string>(s => s.Contains("not WaypointMapLayerExtension"))), Times.Once());
        }

        [Fact]
        public void ToggleAutoSync_FlipsStateAndTellsThePlayer()
        {
            Assert.False(server.IsAutoSyncEnabled(alice));

            server.OnToggleAutoSyncRequested(alice, new WaypointToggleAutoSyncMessage());
            Assert.True(server.IsAutoSyncEnabled(alice));
            Assert.Equal(1, ChatCount(alice, "Auto-sync is now on"));

            server.OnToggleAutoSyncRequested(alice, new WaypointToggleAutoSyncMessage());
            Assert.False(server.IsAutoSyncEnabled(alice));
            Assert.Equal(1, ChatCount(alice, "Auto-sync is now off"));

            Assert.False(server.IsAutoSyncEnabled(bob));
        }

        [Fact]
        public void AutoSyncTick_SyncsOnlyEnabledPlayersAndSilently()
        {
            world.AddWaypoint("a", "Home", 0, 0, 0);
            world.AddWaypoint("b", "Copper", 10, 0, 0);
            server.OnToggleAutoSyncRequested(alice, new WaypointToggleAutoSyncMessage());

            server.AutoSyncTick();

            Assert.Contains("<sync from: Bob>Copper", AliceTitles());
            Assert.DoesNotContain(world.Layer.Waypoints, w => w.OwningPlayerUid == "b" && WaypointSync.IsSharedCopy(w));
            Assert.Equal(0, ChatCount(alice, "Finished Sync"));
        }

        [Fact]
        public void AutoSyncTick_StopsAfterTheToggleIsTurnedOff()
        {
            server.OnToggleAutoSyncRequested(alice, new WaypointToggleAutoSyncMessage());
            server.OnToggleAutoSyncRequested(alice, new WaypointToggleAutoSyncMessage());
            world.AddWaypoint("b", "Copper", 10, 0, 0);

            server.AutoSyncTick();

            Assert.Empty(AliceTitles());
        }

        [Fact]
        public void PlayerLeaving_ForgetsTheirAutoSyncState()
        {
            server.OnToggleAutoSyncRequested(alice, new WaypointToggleAutoSyncMessage());

            server.OnPlayerLeaveDisconnect(alice);
            world.AddWaypoint("b", "Copper", 10, 0, 0);
            server.AutoSyncTick();

            Assert.False(server.IsAutoSyncEnabled(alice));
            Assert.Empty(AliceTitles());
        }

        [Fact]
        public void Status_ReportsTheCurrentAutoSyncState()
        {
            server.OnAutoSyncStatusRequested(alice, new WaypointAutoSyncStatusMessage());
            Assert.Equal(1, ChatCount(alice, "Auto-sync is off"));

            server.OnToggleAutoSyncRequested(alice, new WaypointToggleAutoSyncMessage());
            server.OnAutoSyncStatusRequested(alice, new WaypointAutoSyncStatusMessage());
            Assert.Equal(1, ChatCount(alice, "Auto-sync is on"));
            Assert.True(server.IsAutoSyncEnabled(alice)); // asking doesn't toggle
        }

        [Fact]
        public void AutoSyncTick_FailureTurnsAutoSyncOffInsteadOfThrowing()
        {
            int calls = 0;
            var failing = new WaypointSharingServer(world.Api.Object, () => { calls++; throw new System.InvalidOperationException("boom"); }, debug: false);
            failing.OnToggleAutoSyncRequested(alice, new WaypointToggleAutoSyncMessage());
            failing.OnToggleAutoSyncRequested(bob, new WaypointToggleAutoSyncMessage());

            failing.AutoSyncTick(); // must not throw: the game's Timer would re-run it every tick
            failing.AutoSyncTick();

            Assert.Equal(2, calls); // once per player, then never again
            Assert.False(failing.IsAutoSyncEnabled(alice));
            Assert.False(failing.IsAutoSyncEnabled(bob));
            Assert.Contains(alice.Messages, m => m.ChatType == EnumChatType.CommandError && m.Message.Contains("turned off"));
            world.Logger.Verify(l => l.Error(It.IsAny<System.Exception>()), Times.Exactly(2));
        }

        [Fact]
        public void Share_TwiceWithoutChangesSendsNothingTheSecondTime()
        {
            world.AddWaypoint("b", "Copper", 10, 0, 0);

            server.OnShareRequested(alice, new WaypointShareMessage { LogSuccess = false });
            server.OnShareRequested(alice, new WaypointShareMessage { LogSuccess = false });

            Assert.Equal(1, world.SendCount("a"));
        }

        [Fact]
        public void Share_KeepsAPinThePlayerSetOnACopy()
        {
            world.AddWaypoint("b", "Copper", 10, 0, 0);
            server.OnShareRequested(alice, new WaypointShareMessage());

            world.Layer.Waypoints.Single(w => w.OwningPlayerUid == "a").Pinned = true;
            server.OnShareRequested(alice, new WaypointShareMessage());

            Assert.True(world.Layer.Waypoints.Single(w => w.OwningPlayerUid == "a").Pinned);
        }

        [Fact]
        public void Share_UpdatesCopiesInPlaceSoWaypointIndicesStayStable()
        {
            // Vanilla /waypoint remove|modify <index> (and Auto Map Markers) index into the player's own list,
            // so repeated syncs must not shuffle it
            world.AddWaypoint("a", "Home", 0, 0, 0);
            var copper = world.AddWaypoint("b", "Copper", 10, 0, 0);
            server.OnShareRequested(alice, new WaypointShareMessage());
            world.AddWaypoint("a", "Added after sync", 20, 0, 0);
            var before = AliceTitles();

            copper.Title = "Copper (rich)";
            server.OnShareRequested(alice, new WaypointShareMessage());

            Assert.Equal(new[] { "Home", "<sync from: Bob>Copper (rich)", "Added after sync" }, AliceTitles());
            Assert.Equal(System.Array.IndexOf(before, "Added after sync"), System.Array.IndexOf(AliceTitles(), "Added after sync"));
        }

        [Fact]
        public void Share_NamesCopiedDeathWaypointsAfterTheirOwner()
        {
            world.AddWaypoint("b", "You died here", 10, 0, 0, icon: "gravestone");

            server.OnShareRequested(alice, new WaypointShareMessage());

            Assert.Equal(new[] { "<sync from: Bob>Bob died here" }, AliceTitles());
        }

        [Fact]
        public void PlayerLeaving_WithoutStateIsHarmless()
        {
            server.OnPlayerLeaveDisconnect(bob);
            server.AutoSyncTick();
        }
    }
}
