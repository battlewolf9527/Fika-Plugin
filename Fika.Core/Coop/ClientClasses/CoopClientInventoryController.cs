﻿using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using EFT.UI;
using Fika.Core.Coop.Players;
using Fika.Core.Coop.Utils;
using Fika.Core.Networking;
using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fika.Core.Coop.ClientClasses
{
    public sealed class CoopClientInventoryController : Player.PlayerOwnerInventoryController
    {
        public override bool HasDiscardLimits
        {
            get
            {
                return false;
            }
        }
        private readonly ManualLogSource logger;
        private readonly Player player;
        private readonly CoopPlayer coopPlayer;
        private readonly IPlayerSearchController searchController;

        public CoopClientInventoryController(Player player, Profile profile, bool examined) : base(player, profile, examined)
        {
            this.player = player;
            coopPlayer = (CoopPlayer)player;
            mongoID_0 = MongoID.Generate(true);
            searchController = new PlayerSearchControllerClass(profile, this);
            logger = BepInEx.Logging.Logger.CreateLogSource(nameof(CoopClientInventoryController));
        }

        public override IPlayerSearchController PlayerSearchController
        {
            get
            {
                return searchController;
            }
        }

        public override void GetTraderServicesDataFromServer(string traderId)
        {
            if (FikaBackendUtils.IsClient)
            {
                RequestPacket request = new()
                {
                    PacketType = SubPacket.ERequestSubPacketType.TraderServices,
                    RequestSubPacket = new RequestSubPackets.TraderServicesRequest()
                    {
                        NetId = coopPlayer.NetId,
                        TraderId = traderId
                    }
                };

                Singleton<FikaClient>.Instance.SendData(ref request, DeliveryMethod.ReliableOrdered);
                return;
            }

            coopPlayer.UpdateTradersServiceData(traderId).HandleExceptions();
        }

        public override void CallMalfunctionRepaired(Weapon weapon)
        {
            if (Singleton<SharedGameSettingsClass>.Instance.Game.Settings.MalfunctionVisability)
            {
                MonoBehaviourSingleton<PreloaderUI>.Instance.MalfunctionGlow.ShowGlow(BattleUIMalfunctionGlow.EGlowType.Repaired, true, method_41());
            }
        }

        public override void vmethod_1(BaseInventoryOperationClass operation, Callback callback)
        {
            HandleOperation(operation, callback).HandleExceptions();
        }

        private async Task HandleOperation(BaseInventoryOperationClass operation, Callback callback)
        {
            if (player.HealthController.IsAlive)
            {
                await Task.Yield();
            }
            RunClientOperation(operation, callback);
        }

        private void RunClientOperation(BaseInventoryOperationClass operation, Callback callback)
        {
            if (!vmethod_0(operation))
            {
                operation.Dispose();
                callback.Fail("LOCAL: hands controller can't perform this operation");
                return;
            }

            // Do not replicate picking up quest items, throws an error on the other clients            
            if (operation is GClass3266 moveOperation)
            {
                Item lootedItem = moveOperation.Item;
                if (lootedItem.QuestItem)
                {
                    if (coopPlayer.AbstractQuestControllerClass is CoopClientSharedQuestController sharedQuestController && sharedQuestController.ContainsAcceptedType("PlaceBeacon"))
                    {
                        if (!sharedQuestController.CheckForTemplateId(lootedItem.TemplateId))
                        {
                            sharedQuestController.AddLootedTemplateId(lootedItem.TemplateId);

                            // We use templateId because each client gets a unique itemId
                            QuestItemPacket questPacket = new()
                            {
                                Nickname = coopPlayer.Profile.Info.MainProfileNickname,
                                ItemId = lootedItem.TemplateId
                            };
                            coopPlayer.PacketSender.SendPacket(ref questPacket);
                        }
                    }
                    base.vmethod_1(operation, callback);
                    return;
                }
            }

            // Do not replicate stashing quest items
            if (operation is RemoveOperationClass discardOperation)
            {
                if (discardOperation.Item.QuestItem)
                {
                    base.vmethod_1(operation, callback);
                    return;
                }
            }

            // Do not replicate quest operations / search operations
            // Check for GClass increments, ReadPolymorph
            if (operation is GClass3303)// or GClass3307 or GClass3308 or GClass3309)
            {
                base.vmethod_1(operation, callback);
                return;
            }

            EFTWriterClass eftWriter = new();
            ClientInventoryOperationHandler handler = new()
            {
                Operation = operation,
                Callback = callback,
                InventoryController = this
            };

            uint operationNum = AddOperationCallback(operation, handler.ReceiveStatusFromServer);
            eftWriter.WritePolymorph(operation.ToDescriptor());
            InventoryPacket packet = new()
            {
                NetId = coopPlayer.NetId,
                CallbackId = operationNum,
                OperationBytes = eftWriter.ToArray()
            };

#if DEBUG
            ConsoleScreen.Log($"InvOperation: {operation.GetType().Name}, Id: {operation.Id}");
#endif

            coopPlayer.PacketSender.SendPacket(ref packet);
        }

        public override bool HasCultistAmulet(out CultistAmuletItemClass amulet)
        {
            amulet = null;
            using IEnumerator<Item> enumerator = Inventory.GetItemsInSlots([EquipmentSlot.Pockets]).GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Current is CultistAmuletItemClass cultistAmuletClass)
                {
                    amulet = cultistAmuletClass;
                    return true;
                }
            }
            return false;
        }

        private uint AddOperationCallback(BaseInventoryOperationClass operation, Action<ServerOperationStatus> callback)
        {
            ushort id = operation.Id;
            coopPlayer.OperationCallbacks.Add(id, callback);
            return id;
        }

        public override SearchContentOperation vmethod_2(SearchableItemItemClass item)
        {
            return new GClass3303(method_12(), this, PlayerSearchController, Profile, item);
        }

        private class ClientInventoryOperationHandler
        {
            public BaseInventoryOperationClass Operation;
            public Callback Callback;
            public CoopClientInventoryController InventoryController;
            public IResult OperationResult = null;
            public ServerOperationStatus ServerStatus = default;

            public void ReceiveStatusFromServer(ServerOperationStatus serverStatus)
            {
                ServerStatus = serverStatus;
                switch (serverStatus.Status)
                {
                    case EOperationStatus.Started:
                        Operation.method_0(ExecuteResult);
                        return;
                    case EOperationStatus.Succeeded:
                        HandleFinalResult(SuccessfulResult.New);
                        return;
                    case EOperationStatus.Failed:
                        InventoryController.logger.LogError($"{InventoryController.ID} - Client operation rejected by server: {Operation.Id} - {Operation}\r\nReason: {serverStatus.Error}");
                        HandleFinalResult(new FailedResult(serverStatus.Error));
                        break;
                    default:
                        InventoryController.logger.LogError("ReceiveStatusFromServer: Status was missing?");
                        break;
                }
            }

            private void ExecuteResult(IResult executeResult)
            {
                if (!executeResult.Succeed)
                {
                    InventoryController.logger.LogError($"{InventoryController.ID} - Client operation critical failure: {Operation.Id} server status: {"SERVERRESULT"} - {Operation}\r\nError: {executeResult.Error}");
                }
                HandleFinalResult(executeResult);
            }

            private void HandleFinalResult(IResult result)
            {
                IResult result2 = OperationResult;
                if (result2 == null || !result2.Failed)
                {
                    OperationResult = result;
                }
                EOperationStatus serverStatus = ServerStatus.Status;
                if (!serverStatus.Finished())
                {
                    return;
                }
                EOperationStatus localStatus = Operation.Status;
                if (localStatus.InProgress())
                {
                    if (Operation is GInterface415 ginterface)
                    {
                        ginterface.Terminate();
                    }
                    return;
                }
                Operation.Dispose();
                if (serverStatus != localStatus)
                {
                    if (localStatus.Finished())
                    {
                        InventoryController.logger.LogError($"{InventoryController.ID} - Operation critical failure - status mismatch: {Operation.Id} server status: {serverStatus} client status: {localStatus} - {Operation}");
                    }
                }
                Callback?.Invoke(OperationResult);
            }
        }

        public readonly struct ServerOperationStatus(EOperationStatus status, string error)
        {
            public readonly EOperationStatus Status = status;
            public readonly string Error = error;
        }
    }
}