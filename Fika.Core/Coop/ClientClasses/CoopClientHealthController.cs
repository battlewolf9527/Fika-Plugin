﻿// © 2025 Lacyway All Rights Reserved

using EFT;
using EFT.InventoryLogic;
using Fika.Core.Coop.Players;
using Fika.Core.Networking;

namespace Fika.Core.Coop.ClientClasses
{
    public sealed class CoopClientHealthController(Profile.ProfileHealthClass healthInfo, Player player, InventoryController inventoryController, SkillManager skillManager, bool aiHealth)
        : GControl4(healthInfo, player, inventoryController, skillManager, aiHealth)
    {
        private readonly CoopPlayer coopPlayer = (CoopPlayer)player;
        public override bool _sendNetworkSyncPackets
        {
            get
            {
                return true;
            }
        }

        public override void SendNetworkSyncPacket(NetworkHealthSyncPacketStruct packet)
        {
            if (packet.SyncType == NetworkHealthSyncPacketStruct.ESyncType.IsAlive && !packet.Data.IsAlive.IsAlive)
            {
                HealthSyncPacket deathPacket = coopPlayer.SetupCorpseSyncPacket(packet);
                coopPlayer.PacketSender.SendPacket(ref deathPacket);
                return;
            }

            HealthSyncPacket netPacket = new()
            {
                NetId = coopPlayer.NetId,
                Packet = packet
            };
            coopPlayer.PacketSender.SendPacket(ref netPacket);
        }
    }
}
