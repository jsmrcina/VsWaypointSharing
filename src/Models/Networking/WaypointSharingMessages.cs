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
        // TODO: Needed?
        public string Request;
    }

    // [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    // public class WaypointShareResponse
    // {
    //     public byte[] Message;
    //     public Vec3d WorldSpawnPos;
    // }
}