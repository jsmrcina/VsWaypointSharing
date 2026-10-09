using System.Collections.Generic;
using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace VsWaypointSharing.Tests
{
    //
    // A real (vanilla-derived) WaypointMapLayerExtension running on a mocked server API.
    // Everything the layer sends to a client is captured in SentToClient so tests can assert on what the player would see.
    //
    public class TestWorld
    {
        public Mock<ICoreServerAPI> Api { get; }
        public Mock<IWorldMapManager> MapSink { get; }
        public Mock<ILogger> Logger { get; }
        public WaypointMapLayerExtension Layer { get; }
        public Dictionary<string, List<List<Waypoint>>> SentToClient { get; } = new Dictionary<string, List<List<Waypoint>>>();

        private readonly Dictionary<string, string> playerNames = new Dictionary<string, string>();

        public TestWorld()
        {
            Api = new Mock<ICoreServerAPI> { DefaultValue = DefaultValue.Mock };
            Api.Setup(a => a.Side).Returns(EnumAppSide.Server);
            Api.Setup(a => a.Assets.GetMany(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(new List<IAsset>());
            Api.Setup(a => a.ChatCommands.Parsers).Returns(() => new CommandArgumentParsers(Api.Object));
            Api.Setup(a => a.PlayerData.GetPlayerDataByUid(It.IsAny<string>()))
                .Returns((string uid) =>
                {
                    if (!playerNames.TryGetValue(uid, out var name)) return null;
                    var data = new Mock<IServerPlayerData>();
                    data.Setup(d => d.LastKnownPlayername).Returns(name);
                    return data.Object;
                });

            Logger = new Mock<ILogger>();
            Api.Setup(a => a.Logger).Returns(Logger.Object);

            MapSink = new Mock<IWorldMapManager>();
            MapSink.Setup(m => m.SendMapDataToClient(It.IsAny<MapLayer>(), It.IsAny<IServerPlayer>(), It.IsAny<byte[]>()))
                .Callback((MapLayer layer, IServerPlayer player, byte[] data) =>
                {
                    if (!SentToClient.TryGetValue(player.PlayerUID, out var sends))
                    {
                        SentToClient[player.PlayerUID] = sends = new List<List<Waypoint>>();
                    }
                    sends.Add(SerializerUtil.Deserialize<List<Waypoint>>(data));
                });

            Layer = new WaypointMapLayerExtension(Api.Object, MapSink.Object);
        }

        public FakeServerPlayer AddPlayer(string uid, string name)
        {
            playerNames[uid] = name;
            return new FakeServerPlayer(uid, name);
        }

        public Waypoint AddWaypoint(string ownerUid, string title, double x, double y, double z, int color = unchecked((int)0xFF112233), string icon = "circle", bool pinned = true)
        {
            var wp = Waypoints.Make(ownerUid, title, x, y, z, color, icon, pinned);
            Layer.Waypoints.Add(wp);
            return wp;
        }

        // The last waypoint list the player's client received
        public List<Waypoint> LastSentTo(string uid)
        {
            return SentToClient.TryGetValue(uid, out var sends) && sends.Count > 0 ? sends[sends.Count - 1] : null;
        }

        public int SendCount(string uid)
        {
            return SentToClient.TryGetValue(uid, out var sends) ? sends.Count : 0;
        }
    }

    public static class Waypoints
    {
        public static Waypoint Make(string ownerUid, string title, double x, double y, double z, int color = unchecked((int)0xFF112233), string icon = "circle", bool pinned = true)
        {
            return new Waypoint
            {
                OwningPlayerUid = ownerUid,
                Title = title,
                Position = new Vintagestory.API.MathTools.Vec3d(x, y, z),
                Color = color,
                Icon = icon,
                Pinned = pinned,
                Guid = System.Guid.NewGuid().ToString()
            };
        }
    }
}
