using System;
using System.Linq;
using VsWaypointSharing.Models.Networking;
using VsWaypointSharing.Sync;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.GameContent;
using System.Reflection;

[assembly: ModInfo("VsWaypointSharing",
    Description = "Allows sharing waypoints to other users",
    Authors = new[] { "jsmrcina" })]

namespace VsWaypointSharing
{
    public class VsWaypointSharing : ModSystem
    {
        private readonly string _waypointSharingChannel = "waypointsharing";

        // Same position as the vanilla "waypoints" layer we replace (see WorldMapManager.RegisterDefaultMapLayers)
        internal const double WaypointLayerPosition = 1;

        private ICoreClientAPI ClientApi;
        private ICoreServerAPI ServerApi;

        private IClientNetworkChannel ClientChannel;

        private WaypointSharingServer server;
        private readonly int autoSyncThreadDelay = 15;

        private bool debug = false;

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api ?? throw new ArgumentException("Client API is null");
            DetectDebugBuild(false);

            ClientChannel = ClientApi.Network.RegisterChannel(_waypointSharingChannel)
                            .RegisterMessageType(typeof(WaypointShareMessage))
                            .RegisterMessageType(typeof(WaypointRevertMessage))
                            .RegisterMessageType(typeof(WaypointToggleAutoSyncMessage))
                            .RegisterMessageType(typeof(WaypointAutoSyncStatusMessage));

            ClientApi.ModLoader.GetModSystem<WorldMapManager>().RegisterMapLayer<WaypointMapLayerExtension>("waypoints", WaypointLayerPosition);
            ClientApi.ChatCommands.GetOrCreate("ws").RequiresPrivilege(Privilege.chat)
                .BeginSubCommand("sync").HandleWith(OnShare).EndSubCommand()
                .BeginSubCommand("revert").HandleWith(OnRevert).EndSubCommand()
                .BeginSubCommand("autosync").HandleWith(OnAutoSync).EndSubCommand()
                .BeginSubCommand("status").HandleWith(OnStatus).EndSubCommand();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            ServerApi = api ?? throw new ArgumentException("Server API is null");
            DetectDebugBuild(true);

            var mapManager = ServerApi.ModLoader.GetModSystem<WorldMapManager>();
            server = new WaypointSharingServer(ServerApi,
                () => mapManager.MapLayers.FirstOrDefault(x => x is WaypointMapLayerExtension) as WaypointMapLayerExtension,
                debug);

            ServerApi.Network.RegisterChannel(_waypointSharingChannel)
                .RegisterMessageType(typeof(WaypointShareMessage))
                .RegisterMessageType(typeof(WaypointRevertMessage))
                .RegisterMessageType(typeof(WaypointToggleAutoSyncMessage))
                .RegisterMessageType(typeof(WaypointAutoSyncStatusMessage))
                .SetMessageHandler<WaypointShareMessage>(server.OnShareRequested)
                .SetMessageHandler<WaypointRevertMessage>(server.OnRevertRequested)
                .SetMessageHandler<WaypointToggleAutoSyncMessage>(server.OnToggleAutoSyncRequested)
                .SetMessageHandler<WaypointAutoSyncStatusMessage>(server.OnAutoSyncStatusRequested);

            mapManager.RegisterMapLayer<WaypointMapLayerExtension>("waypoints", WaypointLayerPosition);
            // WorldMapManager instantiates the layers on RunGame, and its handler is registered before ours
            ServerApi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => server.CheckWaypointLayer());
            ServerApi.Event.Timer(server.AutoSyncTick, autoSyncThreadDelay);
            ServerApi.Event.PlayerLeave += server.OnPlayerLeaveDisconnect;
            ServerApi.Event.PlayerDisconnect += server.OnPlayerLeaveDisconnect;
        }

        public void DetectDebugBuild(bool isServer)
        {
            var assemblyConfigurationAttribute = typeof(VsWaypointSharing).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
            var buildConfigurationName = assemblyConfigurationAttribute?.Configuration;
            if(isServer)
            {
                ServerApi.Logger.Notification($"Build of VsWaypointSharing is of type: {buildConfigurationName}");
            }
            else
            {
                ClientApi.Logger.Notification($"Build of VsWaypointSharing is of type: {buildConfigurationName}");
            }

            if ("Debug".Equals(buildConfigurationName))
            {
                debug = true;
            }
        }

        private TextCommandResult OnShare(TextCommandCallingArgs args)
        {
                ClientApi.SendChatMessage($"Requesting waypoints from server");
                ClientChannel.SendPacket(new WaypointShareMessage());
                return TextCommandResult.Success();
        }

        private TextCommandResult OnRevert(TextCommandCallingArgs args)
        {
                ClientApi.SendChatMessage($"Reverting to only local waypoints");
                ClientChannel.SendPacket(new WaypointRevertMessage());
                return TextCommandResult.Success();
        }

        private TextCommandResult OnAutoSync(TextCommandCallingArgs args)
        {
                ClientApi.SendChatMessage($"Toggling auto-sync");
                ClientChannel.SendPacket(new WaypointToggleAutoSyncMessage());
                return TextCommandResult.Success();
        }

        private TextCommandResult OnStatus(TextCommandCallingArgs args)
        {
                ClientChannel.SendPacket(new WaypointAutoSyncStatusMessage());
                return TextCommandResult.Success();
        }
    }
}
