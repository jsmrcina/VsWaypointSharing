using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VsWaypointSharing.Models.Networking
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WaypointShareMessage
    {
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WaypointRevertMessage
    {
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WaypointToggleAutoSyncMessage
    {
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WaypointResponseMessage
    {
        public bool isAutoSyncEnabled;
    }
}