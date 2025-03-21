﻿using Fika.Core.Coop.Players;
using LiteNetLib.Utils;
using static Fika.Core.Networking.SubPacket;

namespace Fika.Core.Networking
{
    public struct WeaponPacket : INetSerializable
    {
        public int NetId;
        public EFirearmSubPacketType Type;
        public ISubPacket SubPacket;

        public void Execute(CoopPlayer player)
        {
            SubPacket.Execute(player);
        }

        public void Deserialize(NetDataReader reader)
        {
            NetId = reader.GetInt();
            Type = (EFirearmSubPacketType)reader.GetByte();
            SubPacket = reader.GetFirearmSubPacket(Type);
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(NetId);
            writer.Put((byte)Type);
            writer.PutFirearmSubPacket(SubPacket, Type);
        }
    }
}
