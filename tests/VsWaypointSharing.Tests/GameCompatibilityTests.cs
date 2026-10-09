using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using VsWaypointSharing.Models.Networking;
using Xunit;

namespace VsWaypointSharing.Tests
{
    //
    // Canaries for game updates: these check the parts of the game the mod depends on beyond what the compiler sees
    // (reflection on private methods, layer registration, serialization, modinfo.json).
    //
    public class GameCompatibilityTests
    {
        [Fact]
        public void PrivateWaypointMapLayerMethodsUsedViaReflectionStillExist()
        {
            Assert.NotNull(WaypointMapLayerExtension.RebuildMapComponentsMethod);
            Assert.NotNull(WaypointMapLayerExtension.ResendWaypointsMethod);
        }

        [Fact]
        public void VanillaWaypointLayerIsRegisteredUnderTheCodeWeReplace()
        {
            var manager = new WorldMapManager();
            manager.RegisterDefaultMapLayers();

            Assert.Equal(typeof(WaypointMapLayer), manager.MapLayerRegistry["waypoints"]);
            Assert.Equal(VsWaypointSharing.WaypointLayerPosition, manager.LayerGroupPositions["waypoints"]);

            manager.RegisterMapLayer<WaypointMapLayerExtension>("waypoints", VsWaypointSharing.WaypointLayerPosition);
            Assert.Equal(typeof(WaypointMapLayerExtension), manager.MapLayerRegistry["waypoints"]);
            Assert.Equal(1, manager.MapLayerRegistry.Values.Count(t => typeof(WaypointMapLayer).IsAssignableFrom(t)));
        }

        [Fact]
        public void ExtensionIsAWaypointMapLayer()
        {
            Assert.True(typeof(WaypointMapLayer).IsAssignableFrom(typeof(WaypointMapLayerExtension)));
        }

        [Fact]
        public void CopiedWaypointsSurviveTheSaveGameSerializer()
        {
            // The layer stores Waypoints with SerializerUtil (protobuf) in the save and when sending to clients
            var copy = Waypoints.Make("a", "<sync from: Bob>Copper", 1.5, 2, -3, color: unchecked((int)0xFFABCDEF), icon: "pick", pinned: false);

            var roundTripped = Assert.Single(SerializerUtil.Deserialize<List<Waypoint>>(SerializerUtil.Serialize(new List<Waypoint> { copy })));

            Assert.Equal(copy.OwningPlayerUid, roundTripped.OwningPlayerUid);
            Assert.Equal(copy.Title, roundTripped.Title);
            Assert.Equal(copy.Color, roundTripped.Color);
            Assert.Equal(copy.Icon, roundTripped.Icon);
            Assert.Equal(copy.Pinned, roundTripped.Pinned);
            Assert.Equal(copy.Guid, roundTripped.Guid);
            Assert.Equal(new Vec3d(1.5, 2, -3), roundTripped.Position);
        }

        [Fact]
        public void NetworkMessagesRoundTripThroughProtobuf()
        {
            Assert.True(SerializerUtil.Deserialize<WaypointShareMessage>(SerializerUtil.Serialize(new WaypointShareMessage())).LogSuccess);
            Assert.NotNull(SerializerUtil.Deserialize<WaypointRevertMessage>(SerializerUtil.Serialize(new WaypointRevertMessage())));
            Assert.NotNull(SerializerUtil.Deserialize<WaypointToggleAutoSyncMessage>(SerializerUtil.Serialize(new WaypointToggleAutoSyncMessage())));
            Assert.NotNull(SerializerUtil.Deserialize<WaypointAutoSyncStatusMessage>(SerializerUtil.Serialize(new WaypointAutoSyncStatusMessage())));
        }

        [Fact]
        public void ModInfoTargetsTheGameVersionWeBuiltAgainst()
        {
            var modinfo = JObject.Parse(File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "modinfo.json")));

            Assert.Equal("code", (string)modinfo["type"]);
            Assert.Equal("VsWaypointSharing", (string)modinfo["modid"]);
            Assert.Matches(@"^\d+\.\d+\.\d+$", (string)modinfo["version"]);
            Assert.Equal(GameVersion.OverallVersion, (string)modinfo["dependencies"]["game"]);
        }
    }
}
