﻿// © 2024 Lacyway All Rights Reserved

using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.Communications;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.SynchronizableObjects;
using EFT.UI;
using EFT.Vehicle;
using EFT.WeaponMounting;
using Fika.Core.Coop.ClientClasses;
using Fika.Core.Coop.ClientClasses.HandsControllers;
using Fika.Core.Coop.HostClasses;
using Fika.Core.Coop.ObservedClasses;
using Fika.Core.Coop.ObservedClasses.Snapshotting;
using Fika.Core.Coop.PacketHandlers;
using Fika.Core.Coop.Utils;
using Fika.Core.Networking;
using Fika.Core.Networking.Http;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using static Fika.Core.Coop.ClientClasses.CoopClientInventoryController;
using static Fika.Core.Networking.Packets.SubPackets;

namespace Fika.Core.Coop.Players
{
    /// <summary>
    /// <see cref="CoopPlayer"/> is the <see cref="LocalPlayer"/>, there can only be one <see cref="CoopPlayer"/> in every game and that is always yourself.
    /// </summary>
    public class CoopPlayer : LocalPlayer
	{
		#region Fields and Properties
		public PacketReceiver PacketReceiver;
		public IPacketSender PacketSender;
		public bool HasSkilledScav = false;
		public float ObservedOverlap = 0f;
		public bool LeftStanceDisabled = false;
		public CorpseSyncPacket CorpseSyncPacket = default;
		public bool HasGround = false;
		public int NetId;
		public bool IsObservedAI = false;
		public Dictionary<uint, Action<ServerOperationStatus>> OperationCallbacks = [];
		public FikaSnapshotter Snapshotter;
		public ClientMovementContext ClientMovementContext
		{
			get
			{
				return MovementContext as ClientMovementContext;
			}
		}
		public Transform SpectateTransform
		{
			get
			{
				return PlayerBones.LootRaycastOrigin;
			}
		}
		#endregion

		public static async Task<CoopPlayer> Create(GameWorld gameWorld, int playerId, Vector3 position, Quaternion rotation,
			string layerName, string prefix, EPointOfView pointOfView, Profile profile, bool aiControl,
			EUpdateQueue updateQueue, EUpdateMode armsUpdateMode, EUpdateMode bodyUpdateMode,
			CharacterControllerSpawner.Mode characterControllerMode, Func<float> getSensitivity,
			Func<float> getAimingSensitivity, IStatisticsManager statisticsManager, IViewFilter filter, ISession session,
			ELocalMode localMode, int netId)
		{
			CoopPlayer player = Create<CoopPlayer>(gameWorld, ResourceKeyManagerAbstractClass.PLAYER_BUNDLE_NAME, playerId, position, updateQueue, armsUpdateMode,
						bodyUpdateMode, characterControllerMode, getSensitivity, getAimingSensitivity, prefix, false);

			player.IsYourPlayer = true;
			player.NetId = netId;

			PlayerOwnerInventoryController inventoryController = FikaBackendUtils.IsServer ? new CoopHostInventoryController(player, profile, false)
				: new CoopClientInventoryController(player, profile, false);

			LocalQuestControllerClass questController;
			if (FikaPlugin.Instance.SharedQuestProgression)
			{
				questController = new CoopClientSharedQuestController(profile, inventoryController, session, player);
			}
			else
			{
				questController = new LocalQuestControllerClass(profile, inventoryController, session, true);
			}
			questController.Init();
			questController.Run();

			GClass3519 achievementsController = new(profile, inventoryController, session, true);
			achievementsController.Init();
			achievementsController.Run();

			if (FikaBackendUtils.IsServer)
			{
				if (FikaBackendUtils.IsDedicated)
				{
					player.PacketSender = player.gameObject.AddComponent<DedicatedPacketSender>();
				}
				else
				{
					player.PacketSender = player.gameObject.AddComponent<ServerPacketSender>();
				}
			}
			else if (FikaBackendUtils.IsClient)
			{
				player.PacketSender = player.gameObject.AddComponent<ClientPacketSender>();
			}

			player.PacketReceiver = player.gameObject.AddComponent<PacketReceiver>();

			await player.Init(rotation, layerName, pointOfView, profile, inventoryController,
				new CoopClientHealthController(profile.Health, player, inventoryController, profile.Skills, aiControl),
				statisticsManager, questController, achievementsController, filter,
				EVoipState.NotAvailable, false, false);

			foreach (MagazineClass magazineClass in player.Inventory.GetPlayerItems(EPlayerItems.NonQuestItems).OfType<MagazineClass>())
			{
				player.InventoryController.StrictCheckMagazine(magazineClass, true, player.Profile.MagDrillsMastering, false, false);
			}

			HashSet<ETraderServiceType> services = Traverse.Create(player).Field<HashSet<ETraderServiceType>>("hashSet_0").Value;
			foreach (ETraderServiceType etraderServiceType in Singleton<BackendConfigSettingsClass>.Instance.ServicesData.Keys)
			{
				services.Add(etraderServiceType);
			}

			player._handsController = EmptyHandsController.smethod_6<EmptyHandsController>(player);
			player._handsController.Spawn(1f, Class1608.class1608_0.method_0);

			player.AIData = new GClass540(null, player);

			player.AggressorFound = false;

			player._animators[0].enabled = true;

			RadioTransmitterRecodableComponent radioTransmitterRecodableComponent = player.FindRadioTransmitter();
			if (radioTransmitterRecodableComponent != null)
			{
				//Todo: (Archangel) method_131 refers to 'singlePlayerInventoryController_0' which is null in our case
				//radioTransmitterRecodableComponent.OnRadioTransmitterStatusChanged += player.method_131;

				if (player.Profile.GetTraderStanding("638f541a29ffd1183d187f57").IsZero())
				{
					radioTransmitterRecodableComponent.SetEncoded(false);
				}
			}

			player.Profile.Info.MainProfileNickname = FikaBackendUtils.PMCName;

			return player;
		}

		public override void CreateMovementContext()
		{
			LayerMask movement_MASK = EFTHardSettings.Instance.MOVEMENT_MASK;
			if (FikaPlugin.Instance.UseInertia)
			{
				MovementContext = ClientMovementContext.Create(this, GetBodyAnimatorCommon,
					GetCharacterControllerCommon, movement_MASK);
				return;
			}
			MovementContext = NoInertiaMovementContext.Create(this, GetBodyAnimatorCommon,
					GetCharacterControllerCommon, movement_MASK);
		}

		public override void OnSkillLevelChanged(AbstractSkillClass skill)
		{
			NotificationManagerClass.DisplayNotification(new GClass2198(skill));
		}

		public override bool CheckSurface()
		{
			HasGround = base.CheckSurface();
			return HasGround;
		}

		public override void OnWeaponMastered(MasterSkillClass masterSkill)
		{
			NotificationManagerClass.DisplayMessageNotification(string.Format("MasteringLevelUpMessage".Localized(null),
				masterSkill.MasteringGroup.Id.Localized(null),
				masterSkill.Level.ToString()), ENotificationDurationType.Default, ENotificationIconType.Default, null);
		}

		public override void ApplyDamageInfo(DamageInfo damageInfo, EBodyPart bodyPartType, EBodyPartColliderType colliderType, float absorbed)
		{
			if (IsYourPlayer)
			{
				if (damageInfo.Player != null)
				{
					if (!FikaPlugin.Instance.FriendlyFire && damageInfo.Player.iPlayer.GroupId == GroupId)
					{
						return;
					}
				}
				if (colliderType == EBodyPartColliderType.HeadCommon)
				{
					damageInfo.Damage *= FikaPlugin.HeadDamageMultiplier.Value;
				}

				if (colliderType is EBodyPartColliderType.RightSideChestUp or EBodyPartColliderType.LeftSideChestUp)
				{
					damageInfo.Damage *= FikaPlugin.ArmpitDamageMultiplier.Value;
				}

				if (bodyPartType is EBodyPart.Stomach)
				{
					damageInfo.Damage *= FikaPlugin.StomachDamageMultiplier.Value;
				}
			}

			base.ApplyDamageInfo(damageInfo, bodyPartType, colliderType, absorbed);
		}

		public override ShotInfoClass ApplyShot(DamageInfo damageInfo, EBodyPart bodyPartType, EBodyPartColliderType colliderType, EArmorPlateCollider armorPlateCollider, GStruct400 shotId)
		{
			if (damageInfo.DamageType is EDamageType.Sniper or EDamageType.Landmine)
			{
				return SimulatedApplyShot(damageInfo, bodyPartType, colliderType, armorPlateCollider);
			}

			if (damageInfo.Player != null && damageInfo.Player.IsAI)
			{
				return SimulatedApplyShot(damageInfo, bodyPartType, colliderType, armorPlateCollider);
			}

			return null;
		}

		private ShotInfoClass SimulatedApplyShot(DamageInfo damageInfo, EBodyPart bodyPartType, EBodyPartColliderType colliderType, EArmorPlateCollider armorPlateCollider)
		{
			ActiveHealthController activeHealthController = ActiveHealthController;
			if (activeHealthController != null && !activeHealthController.IsAlive)
			{
				return null;
			}
			bool flag = !string.IsNullOrEmpty(damageInfo.DeflectedBy);
			float damage = damageInfo.Damage;
			List<ArmorComponent> list = ProceedDamageThroughArmor(ref damageInfo, colliderType, armorPlateCollider, true);
			MaterialType materialType = flag ? MaterialType.HelmetRicochet : ((list == null || list.Count < 1) ? MaterialType.Body : list[0].Material);
			ShotInfoClass hitInfo = new()
			{
				PoV = PointOfView,
				Penetrated = string.IsNullOrEmpty(damageInfo.BlockedBy) || string.IsNullOrEmpty(damageInfo.DeflectedBy),
				Material = materialType
			};
			float num = damage - damageInfo.Damage;
			if (num > 0)
			{
				damageInfo.DidArmorDamage = num;
			}
			ApplyDamageInfo(damageInfo, bodyPartType, colliderType, 0f);
			ShotReactions(damageInfo, bodyPartType);
			ReceiveDamage(damageInfo.Damage, bodyPartType, damageInfo.DamageType, num, hitInfo.Material);

			if (list != null)
			{
				QueueArmorDamagePackets([.. list]);
			}

			return hitInfo;
		}

		#region Proceed
		public override void Proceed(bool withNetwork, Callback<GInterface152> callback, bool scheduled = true)
		{
			base.Proceed(withNetwork, callback, scheduled);
			PacketSender.CommonPlayerPackets.Enqueue(new()
			{
				HasProceedPacket = true,
				ProceedPacket = new()
				{
					ProceedType = EProceedType.EmptyHands,
					Scheduled = scheduled
				}
			});
		}

		public override void Proceed(FoodClass foodDrink, float amount, Callback<GInterface157> callback, int animationVariant, bool scheduled = true)
		{
			FoodControllerHandler handler = new(this, foodDrink, amount, EBodyPart.Head, animationVariant);

			Func<MedsController> func = new(handler.ReturnController);
			handler.process = new(this, func, foodDrink, false);
			handler.confirmCallback = new(handler.SendPacket);
			handler.process.method_0(new(handler.HandleResult), callback, scheduled);
		}

		public override void Proceed<T>(Item item, Callback<GInterface156> callback, bool scheduled = true)
		{
			UsableItemControllerHandler handler = new(this, item);

			Func<UsableItemController> func = new(handler.ReturnController);
			handler.process = new(this, func, item, false);
			handler.confirmCallback = new(handler.SendPacket);
			handler.process.method_0(new(handler.HandleResult), callback, scheduled);
		}

		public override void Proceed(Item item, Callback<IOnHandsUseCallback> callback, bool scheduled = true)
		{
			QuickUseItemControllerHandler handler = new(this, item);

			Func<QuickUseItemController> func = new(handler.ReturnController);
			handler.process = new(this, func, item, true);
			handler.confirmCallback = new(handler.SendPacket);
			handler.process.method_0(new(handler.HandleResult), callback, scheduled);
		}

		public override void Proceed(KnifeComponent knife, Callback<IKnifeController> callback, bool scheduled = true)
		{
			KnifeControllerHandler handler = new(this, knife);

			Func<KnifeController> func = new(handler.ReturnController);
			handler.process = new(this, func, handler.knife.Item, false);
			handler.confirmCallback = new(handler.SendPacket);
			handler.process.method_0(new(handler.HandleResult), callback, scheduled);
		}

		public override void Proceed(KnifeComponent knife, Callback<GInterface161> callback, bool scheduled = true)
		{
			QuickKnifeControllerHandler handler = new(this, knife);

			Func<QuickKnifeKickController> func = new(handler.ReturnController);
			handler.process = new(this, func, handler.knife.Item, true);
			handler.confirmCallback = new(handler.SendPacket);
			handler.process.method_0(new(handler.HandleResult), callback, scheduled);
		}

		public override void Proceed(MedsClass meds, EBodyPart bodyPart, Callback<GInterface157> callback, int animationVariant, bool scheduled = true)
		{
			MedsControllerHandler handler = new(this, meds, bodyPart, animationVariant);

			Func<MedsController> func = new(handler.ReturnController);
			handler.process = new(this, func, meds, false);
			handler.confirmCallback = new(handler.SendPacket);
			handler.process.method_0(new(handler.HandleResult), callback, scheduled);
		}

		public override void Proceed(GrenadeClass throwWeap, Callback<GInterface160> callback, bool scheduled = true)
		{
			QuickGrenadeControllerHandler handler = new(this, throwWeap);

			Func<QuickGrenadeThrowHandsController> func = new(handler.ReturnController);
			handler.process = new(this, func, throwWeap, false);
			handler.confirmCallback = new(handler.SendPacket);
			handler.process.method_0(new(handler.HandleResult), callback, scheduled);
		}

		public override void Proceed(GrenadeClass throwWeap, Callback<IHandsThrowController> callback, bool scheduled = true)
		{
			GrenadeControllerHandler handler = new(this, throwWeap);

			Func<GrenadeHandsController> func = new(handler.ReturnController);
			handler.process = new(this, func, throwWeap, false);
			handler.confirmCallback = new(handler.SendPacket);
			handler.process.method_0(new(handler.HandleResult), callback, scheduled);
		}

		public override void Proceed(Weapon weapon, Callback<IFirearmHandsController> callback, bool scheduled = true)
		{
			FirearmControllerHandler handler = new(this, weapon);
			bool flag = false;
			FirearmController firearmController;
			if ((firearmController = _handsController as FirearmController) != null)
			{
				flag = firearmController.CheckForFastWeaponSwitch(handler.weapon);
			}
			Func<FirearmController> func = new(handler.ReturnController);
			handler.process = new Process<FirearmController, IFirearmHandsController>(this, func, handler.weapon, flag);
			handler.confirmCallback = new(handler.SendPacket);
			handler.process.method_0(new(handler.HandleResult), callback, scheduled);
		}
		#endregion

		public override void DropCurrentController(Action callback, bool fastDrop, Item nextControllerItem = null)
		{
			PacketSender.CommonPlayerPackets.Enqueue(new()
			{
				HasDrop = true,
				DropPacket = new()
				{
					FastDrop = fastDrop
				}
			});
			base.DropCurrentController(callback, fastDrop, nextControllerItem);
		}

		public override void OnBeenKilledByAggressor(IPlayer aggressor, DamageInfo damageInfo, EBodyPart bodyPart, EDamageType lethalDamageType)
		{
			base.OnBeenKilledByAggressor(aggressor, damageInfo, bodyPart, lethalDamageType);

			// Handle 'Help Scav' rep gains
			if (aggressor is CoopPlayer coopPlayer)
			{
				if (coopPlayer.Side == EPlayerSide.Savage)
				{
					coopPlayer.Loyalty.method_1(this);
				}

				if (Side == EPlayerSide.Savage && coopPlayer.Side != EPlayerSide.Savage && !coopPlayer.HasSkilledScav)
				{
					coopPlayer.HasSkilledScav = true;
					return;
				}
				else if (Side != EPlayerSide.Savage && HasSkilledScav && aggressor.Side == EPlayerSide.Savage)
				{
					coopPlayer.Profile?.FenceInfo?.AddStanding(Profile.Info.Settings.StandingForKill, EFT.Counters.EFenceStandingSource.ScavHelp);
				}
			}
		}

		public void HandleTeammateKill(DamageInfo damage, EBodyPart bodyPart,
			EPlayerSide playerSide, WildSpawnType role, string playerProfileId,
			float distance, int hour, List<string> targetEquipment,
			HealthEffects enemyEffects, List<string> zoneIds, CoopPlayer killer, int experience)
		{
			if (!HealthController.IsAlive)
			{
				return;
			}

			if (role != WildSpawnType.pmcBEAR)
			{
				if (role == WildSpawnType.pmcUSEC)
				{
					playerSide = EPlayerSide.Usec;
				}
			}
			else
			{
				playerSide = EPlayerSide.Bear;
			}

			List<string> list = ["Any"];

			switch (playerSide)
			{
				case EPlayerSide.Usec:
					list.Add("Usec");
					list.Add("AnyPmc");
					list.Add("Enemy");
					break;
				case EPlayerSide.Bear:
					list.Add("Bear");
					list.Add("AnyPmc");
					list.Add("Enemy");
					break;
				case EPlayerSide.Savage:
					list.Add("Savage");
					list.Add("Bot");
					break;
			}

			foreach (string value in list)
			{
				AbstractQuestControllerClass.CheckKillConditionCounter(value, playerProfileId, targetEquipment, damage.Weapon,
								bodyPart, Location, distance, role.ToStringNoBox(), hour, enemyEffects,
								killer.HealthController.BodyPartEffects, zoneIds, killer.HealthController.ActiveBuffsNames());

				/*AbstractAchievementControllerClass.CheckKillConditionCounter(value, playerProfileId, targetEquipment, damage.Weapon,
                    bodyPart, Location, distance, role.ToStringNoBox(), hour, enemyEffects,
                    killer.HealthController.BodyPartEffects, zoneIds, killer.HealthController.ActiveBuffsNames());*/
			}

			if (FikaPlugin.SharedBossExperience.Value && !(role is WildSpawnType.pmcUSEC or WildSpawnType.pmcBEAR) && role.IsBoss())
			{
				int toReceive = experience / 2;
				Profile.EftStats.SessionCounters.AddInt(toReceive, SessionCounterTypesAbstractClass.KilledBoss);
			}
		}

#if DEBUG
		public override void ShowStringNotification(string message)
		{
			if (IsYourPlayer)
			{
				ConsoleScreen.Log(message);
				FikaPlugin.Instance.FikaLogger.LogInfo(message);
			}
		}
#endif

		public override void SetInventoryOpened(bool opened)
		{
			if (this is ObservedCoopPlayer)
			{
				base.SetInventoryOpened(opened);
				return;
			}

			base.SetInventoryOpened(opened);
			PacketSender.CommonPlayerPackets.Enqueue(new()
			{
				HasInventoryChanged = true,
				SetInventoryOpen = opened
			});
		}

		public override void SetCompassState(bool value)
		{
			base.SetCompassState(value);
			PacketSender.FirearmPackets.Enqueue(new()
			{
				HasCompassChange = true,
				CompassState = value
			});
		}

		public override void SendHeadlightsPacket(bool isSilent)
		{
			FirearmLightStateStruct[] lightStates = _helmetLightControllers.Select(ClientPlayer.Class1535.class1535_0.method_0).ToArray();

			if (PacketSender != null)
			{
				PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					HasHeadLightsPacket = true,
					HeadLightsPacket = new()
					{
						Amount = lightStates.Count(),
						IsSilent = isSilent,
						LightStates = lightStates
					}
				});
			}
		}

		public override void OnItemAddedOrRemoved(Item item, ItemAddress location, bool added)
		{
			base.OnItemAddedOrRemoved(item, location, added);
		}

		public override void OnPhraseTold(EPhraseTrigger @event, TaggedClip clip, TagBank bank, PhraseSpeakerClass speaker)
		{
			base.OnPhraseTold(@event, clip, bank, speaker);

			if (ActiveHealthController.IsAlive)
			{
				PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					Phrase = @event,
					PhraseIndex = clip.NetId
				});
			}
		}

		public override void OperateStationaryWeapon(StationaryWeapon stationaryWeapon, GStruct177.EStationaryCommand command)
		{
			base.OperateStationaryWeapon(stationaryWeapon, command);
			PacketSender.CommonPlayerPackets.Enqueue(new()
			{
				HasStationaryPacket = true,
				StationaryPacket = new()
				{
					Command = (EStationaryCommand)command,
					Id = stationaryWeapon.Id
				}
			});
		}

		protected virtual void ReceiveSay(EPhraseTrigger trigger, int index)
		{
			if (HealthController.IsAlive)
			{
				Speaker.PlayDirect(trigger, index);
			}
		}

		// Start
		public override void vmethod_0(WorldInteractiveObject interactiveObject, InteractionResult interactionResult, Action callback)
		{
			if (this is ObservedCoopPlayer)
			{
				base.vmethod_0(interactiveObject, interactionResult, callback);
				return;
			}

			base.vmethod_0(interactiveObject, interactionResult, callback);

			CommonPlayerPacket packet = new()
			{
				HasWorldInteractionPacket = true,
				WorldInteractionPacket = new()
				{
					InteractiveId = interactiveObject.Id,
					InteractionType = interactionResult.InteractionType,
					InteractionStage = EInteractionStage.Start,
					ItemId = (interactionResult is GClass3263 keyInteractionResult) ? keyInteractionResult.Key.Item.Id : string.Empty
				}
			};
			PacketSender.CommonPlayerPackets.Enqueue(packet);
		}

		// Execute
		public override void vmethod_1(WorldInteractiveObject door, InteractionResult interactionResult)
		{
			if (this is ObservedCoopPlayer)
			{
				base.vmethod_1(door, interactionResult);
				return;
			}

			base.vmethod_1(door, interactionResult);

			if (!door.ForceLocalInteraction)
			{
				CommonPlayerPacket packet = new()
				{
					HasWorldInteractionPacket = true,
					WorldInteractionPacket = new()
					{
						InteractiveId = door.Id,
						InteractionType = interactionResult.InteractionType,
						InteractionStage = EInteractionStage.Execute,
						ItemId = (interactionResult is GClass3263 keyInteractionResult) ? keyInteractionResult.Key.Item.Id : string.Empty
					}
				};
				PacketSender.CommonPlayerPackets.Enqueue(packet);
			}

			UpdateInteractionCast();
		}

		public override void OnAnimatedInteraction(EInteraction interaction)
		{
			if (!FikaGlobals.BlockedInteractions.Contains(interaction))
			{
				PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					Interaction = interaction
				});
			}
		}

		public override void HealthControllerUpdate(float deltaTime)
		{
			_healthController.ManualUpdate(deltaTime);
		}

		public override void OnMounting(GStruct179.EMountingCommand command)
		{
			MountingPacket packet = new(command)
			{
				IsMounted = MovementContext.IsInMountedState,
				MountDirection = MovementContext.IsInMountedState ? MovementContext.PlayerMountingPointData.MountPointData.MountDirection : default,
				MountingPoint = MovementContext.IsInMountedState ? MovementContext.PlayerMountingPointData.MountPointData.MountPoint : default,
				CurrentMountingPointVerticalOffset = MovementContext.IsInMountedState ? MovementContext.PlayerMountingPointData.CurrentMountingPointVerticalOffset : 0f,
				MountingDirection = MovementContext.IsInMountedState ? (short)MovementContext.PlayerMountingPointData.MountPointData.MountSideDirection : (short)0
			};
			if (command == GStruct179.EMountingCommand.Enter)
			{
				packet.TransitionTime = MovementContext.PlayerMountingPointData.CurrentApproachTime;
				packet.TargetPos = MovementContext.PlayerMountingPointData.PlayerTargetPos;
				packet.TargetPoseLevel = MovementContext.PlayerMountingPointData.TargetPoseLevel;
				packet.TargetHandsRotation = MovementContext.PlayerMountingPointData.TargetHandsRotation;
				packet.TargetBodyRotation = MovementContext.PlayerMountingPointData.TargetBodyRotation;
				packet.PoseLimit = MovementContext.PlayerMountingPointData.PoseLimit;
				packet.PitchLimit = MovementContext.PlayerMountingPointData.PitchLimit;
				packet.YawLimit = MovementContext.PlayerMountingPointData.YawLimit;
			}

			PacketSender.CommonPlayerPackets.Enqueue(new()
			{
				HasMountingPacket = true,
				MountingPacket = packet
			});
		}

		public override void vmethod_4(TripwireSynchronizableObject tripwire)
		{
			base.vmethod_4(tripwire);
			SyncObjectPacket packet = new(tripwire.ObjectId)
			{
				ObjectType = SynchronizableObjectType.Tripwire,
				Data = new()
				{
					PacketData = new()
					{
						TripwireDataPacket = new()
						{
							State = ETripwireState.Inert
						}
					},
					Position = tripwire.transform.position,
					Rotation = tripwire.transform.rotation.eulerAngles,
					IsActive = true
				}
			};
			PacketSender.SendPacket(ref packet);
			UpdateInteractionCast();
		}

		public override void ApplyCorpseImpulse()
		{
			Corpse.Ragdoll.ApplyImpulse(LastDamageInfo.HitCollider, LastDamageInfo.Direction, LastDamageInfo.HitPoint, _corpseAppliedForce);
		}

		public HealthSyncPacket SetupCorpseSyncPacket(GStruct358 packet)
		{
			float num = EFTHardSettings.Instance.HIT_FORCE;
			num *= 0.3f + 0.7f * Mathf.InverseLerp(50f, 20f, LastDamageInfo.PenetrationPower);
			_corpseAppliedForce = num;

			HealthSyncPacket syncPacket = new(NetId)
			{
				Packet = packet,
				KillerId = !string.IsNullOrEmpty(KillerId) ? KillerId : null,
				CorpseSyncPacket = new()
				{
					BodyPartColliderType = LastDamageInfo.BodyPartColliderType,
					Direction = LastDamageInfo.Direction,
					Point = LastDamageInfo.HitPoint,
					Force = _corpseAppliedForce,
					OverallVelocity = Velocity,
					Equipment = Inventory.Equipment,
					ItemSlot = EquipmentSlot.ArmBand
				},
				TriggerZones = TriggerZones.Count > 0 ? [.. TriggerZones] : null,
			};

			if (HandsController.Item != null)
			{
				Item heldItem = HandsController.Item;
				EquipmentSlot[] weaponSlots = [EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster, EquipmentSlot.Scabbard];
				foreach (EquipmentSlot weaponSlot in weaponSlots)
				{
					if (heldItem == Equipment.GetSlot(weaponSlot).ContainedItem)
					{
						syncPacket.CorpseSyncPacket.ItemSlot = weaponSlot;
						break;
					}
				}
			}

			return syncPacket;
		}

		public override void OnDead(EDamageType damageType)
		{
			base.OnDead(damageType);
			PacketSender.Enabled = false;
			if (IsYourPlayer)
			{
				StartCoroutine(LocalPlayerDied());
			}
			if (FikaBackendUtils.IsServer || IsYourPlayer)
			{
				if (Side is not EPlayerSide.Savage)
				{
					GenerateDogtagDetails();
				}
			}
		}

		/// <summary>
		/// TODO: Refactor... BSG code makes this difficult
		/// </summary>
		/// <returns></returns>
		private void GenerateDogtagDetails()
		{
			string accountId = AccountId;
			string profileId = ProfileId;
			string nickname = Profile.Nickname;
			bool hasAggressor = LastAggressor != null;
			string killerAccountId = hasAggressor ? LastAggressor.AccountId : string.Empty;
			string killerProfileId = hasAggressor ? LastAggressor.ProfileId : string.Empty;
			string killerNickname = (hasAggressor && !string.IsNullOrEmpty(LastAggressor.Profile.Nickname)) ? LastAggressor.Profile.Nickname : "Unknown";
			EPlayerSide side = Side;
			int level = Profile.Info.Level;
			DateTime time = EFTDateTimeClass.UtcNow;
			string weaponName = LastAggressor != null ? (LastDamageInfo.Weapon != null ? LastDamageInfo.Weapon.ShortName : string.Empty) : "-";
			string groupId = GroupId;

			Item item = Equipment.GetSlot(EquipmentSlot.Dogtag).ContainedItem;
			if (item != null)
			{
				DogtagComponent dogtagComponent = item.GetItemComponent<DogtagComponent>();
				if (dogtagComponent != null)
				{
					dogtagComponent.Item.SpawnedInSession = true;
					dogtagComponent.AccountId = accountId;
					dogtagComponent.ProfileId = profileId;
					dogtagComponent.Nickname = nickname;
					dogtagComponent.KillerAccountId = killerAccountId;
					dogtagComponent.KillerProfileId = killerProfileId;
					dogtagComponent.KillerName = killerNickname;
					dogtagComponent.Side = side;
					dogtagComponent.Level = level;
					dogtagComponent.Time = time;
					dogtagComponent.Status = LastAggressor != null ? "Killed by" : "Died";
					dogtagComponent.WeaponName = weaponName;
					dogtagComponent.GroupId = groupId;
					return;
				}
			}

			FikaPlugin.Instance.FikaLogger.LogError($"GenerateAndSendDogTagPacket: Item or Dogtagcomponent was null on player {Profile.Nickname}, id {NetId}");
		}

		private IEnumerator LocalPlayerDied()
		{
			AddPlayerRequest request = new(FikaBackendUtils.GroupId, ProfileId);
			Task diedTask = FikaRequestHandler.PlayerDied(request);
			while (!diedTask.IsCompleted)
			{
				yield return new WaitForEndOfFrame();
			}
		}

		private void HandleInteractPacket(WorldInteractionPacket packet)
		{
			WorldInteractiveObject worldInteractiveObject = Singleton<GameWorld>.Instance.FindDoor(packet.InteractiveId);
			if (worldInteractiveObject != null)
			{
				if (worldInteractiveObject.isActiveAndEnabled && !worldInteractiveObject.ForceLocalInteraction)
				{
					InteractionResult interactionResult;
					Action action;
					if (packet.InteractionType == EInteractionType.Unlock)
					{
						KeyHandler keyHandler = new(this);

						if (string.IsNullOrEmpty(packet.ItemId))
						{
							FikaPlugin.Instance.FikaLogger.LogWarning("HandleInteractPacket: ItemID was null!");
							return;
						}

						GStruct428<Item> result = FindItemById(packet.ItemId, false, false);
						if (!result.Succeeded)
						{
							FikaPlugin.Instance.FikaLogger.LogWarning("HandleInteractPacket: Could not find item: " + packet.ItemId);
							return;
						}

						KeyComponent keyComponent = result.Value.GetItemComponent<KeyComponent>();
						if (keyComponent == null)
						{
							FikaPlugin.Instance.FikaLogger.LogWarning("HandleInteractPacket: keyComponent was null!");
							return;
						}

						keyHandler.unlockResult = worldInteractiveObject.UnlockOperation(keyComponent, this);
						if (keyHandler.unlockResult.Error != null)
						{
							FikaPlugin.Instance.FikaLogger.LogWarning("HandleInteractPacket: Error when processing unlockResult: " + keyHandler.unlockResult.Error);
							return;
						}

						interactionResult = keyHandler.unlockResult.Value;
						keyHandler.unlockResult.Value.RaiseEvents(_inventoryController, CommandStatus.Begin);
						action = new(keyHandler.HandleKeyEvent);
					}
					else
					{
						interactionResult = new InteractionResult(packet.InteractionType);
						action = null;
					}

					if (packet.InteractionStage == EInteractionStage.Start)
					{
						vmethod_0(worldInteractiveObject, interactionResult, action);
						return;
					}

					if (packet.InteractionStage != EInteractionStage.Execute)
					{
						worldInteractiveObject.Interact(interactionResult);
						return;
					}

					vmethod_1(worldInteractiveObject, interactionResult);
				}

			}
			else
			{
				FikaPlugin.Instance.FikaLogger.LogError("HandleInteractPacket: WorldInteractiveObject was null or disabled!");
			}
		}

		public override void TryInteractionCallback(LootableContainer container)
		{
			LootableContainerInteractionHandler handler = new(this, container);
			if (handler.container != null && _openAction != null)
			{
				_openAction(handler.Handle);
			}
			_openAction = null;
		}

		public override void vmethod_2(BTRSide btr, byte placeId, EInteractionType interaction)
		{
			if (FikaBackendUtils.IsServer)
			{
				base.vmethod_2(btr, placeId, interaction);
				return;
			}

			FikaClient client = Singleton<FikaClient>.Instance;
			BTRInteractionPacket packet = new(NetId)
			{
				Data = btr.GetInteractWithBtrPacket(placeId, interaction)
			};
			client.SendData(ref packet, LiteNetLib.DeliveryMethod.ReliableOrdered);
		}

		public void SetupMainPlayer()
		{
			// Set own group id, ignore if dedicated
			if (!Profile.Info.Nickname.Contains("dedicated_"))
			{
				Profile.Info.GroupId = "Fika";
			}

			// Setup own dog tag
			if (Side != EPlayerSide.Savage)
			{
				FikaPlugin.Instance.FikaLogger.LogInfo("Setting up DogTag");
				if (Equipment.GetSlot(EquipmentSlot.Dogtag).ContainedItem != null)
				{
					GStruct426<GClass3058> result = InteractionsHandlerClass.Remove(Equipment.GetSlot(EquipmentSlot.Dogtag).ContainedItem, _inventoryController, false);
					if (result.Error != null)
					{
						FikaPlugin.Instance.FikaLogger.LogWarning("CoopPlayer::SetupMainPlayer: Error removing dog tag!");
					}
				}

				string templateId = GetDogtagTemplateId();

				if (!string.IsNullOrEmpty(templateId))
				{
					Item item = Singleton<ItemFactoryClass>.Instance.CreateItem(MongoID.Generate(), templateId, null);

					Slot dogtagSlot = Equipment.GetSlot(EquipmentSlot.Dogtag);
					GStruct426<GClass3062> addResult = dogtagSlot.AddWithoutRestrictions(item);

					if (addResult.Error != null)
					{
						FikaPlugin.Instance.FikaLogger.LogError("CoopPlayer::SetupMainPlayer: Error adding dog tag to slot: " + addResult.Error);
					}

					DogtagComponent dogtagComponent = item.GetItemComponent<DogtagComponent>();
					if (dogtagComponent != null)
					{
						dogtagComponent.ProfileId = ProfileId;
						dogtagComponent.GroupId = Profile.Info.GroupId;
					}
					else
					{
						FikaPlugin.Instance.FikaLogger.LogWarning("Unable to find DogTagComponent");
					}
				}
				else
				{
					FikaPlugin.Instance.FikaLogger.LogError("Could not get templateId for DogTag!");
				}
			}
		}

		private string GetDogtagTemplateId()
		{
			if (Side is EPlayerSide.Usec)
			{
				switch (Profile.Info.SelectedMemberCategory)
				{
					case EMemberCategory.Default:
						return "59f32c3b86f77472a31742f0";
					case EMemberCategory.UniqueId:
						return "6662e9f37fa79a6d83730fa0";
					case EMemberCategory.Unheard:
						return "6662ea05f6259762c56f3189";
				}
			}
			else if (Side is EPlayerSide.Bear)
			{
				switch (Profile.Info.SelectedMemberCategory)
				{
					case EMemberCategory.Default:
						return "59f32bb586f774757e1e8442";
					case EMemberCategory.UniqueId:
						return "6662e9aca7e0b43baa3d5f74";
					case EMemberCategory.Unheard:
						return "6662e9cda7e0b43baa3d5f76";
				}
			}

			return string.Empty;
		}

		public virtual void HandleCommonPacket(in CommonPlayerPacket packet)
		{
			if (packet.Phrase != EPhraseTrigger.PhraseNone)
			{
				ReceiveSay(packet.Phrase, packet.PhraseIndex);
			}

			if (packet.HasWorldInteractionPacket)
			{
				HandleInteractPacket(packet.WorldInteractionPacket);
			}

			if (packet.HasContainerInteractionPacket)
			{
				WorldInteractiveObject lootableContainer = Singleton<GameWorld>.Instance.FindDoor(packet.ContainerInteractionPacket.InteractiveId);
				if (lootableContainer != null)
				{
					if (lootableContainer.isActiveAndEnabled)
					{
						InteractionResult result = new(packet.ContainerInteractionPacket.InteractionType);
						lootableContainer.Interact(result);
					}
				}
				else
				{
					FikaPlugin.Instance.FikaLogger.LogError("CommonPlayerPacket::ContainerInteractionPacket: LootableContainer was null!");
				}
			}

			if (packet.HasProceedPacket)
			{
				if (this is ObservedCoopPlayer observedCoopPlayer)
				{
					observedCoopPlayer.HandleProceedPacket(packet.ProceedPacket);
				}
			}

			if (packet.HasHeadLightsPacket)
			{
				try
				{
					if (_helmetLightControllers != null)
					{
						for (int i = 0; i < _helmetLightControllers.Count(); i++)
						{
							_helmetLightControllers.ElementAt(i)?.LightMod?.SetLightState(packet.HeadLightsPacket.LightStates[i]);
						}
						if (!packet.HeadLightsPacket.IsSilent)
						{
							SwitchHeadLightsAnimation();
						}
					}
				}
				catch (Exception)
				{
					// Do nothing
				}
			}

			if (packet.HasInventoryChanged)
			{
				base.SetInventoryOpened(packet.SetInventoryOpen);
			}

			if (packet.HasDrop)
			{
				DropHandler handler = new(this);
				base.DropCurrentController(handler.HandleResult, packet.DropPacket.FastDrop, null);
			}

			if (packet.HasStationaryPacket)
			{
				StationaryWeapon stationaryWeapon = (packet.StationaryPacket.Command == EStationaryCommand.Occupy)
					? Singleton<GameWorld>.Instance.FindStationaryWeapon(packet.StationaryPacket.Id) : null;
				ObservedStationaryInteract(stationaryWeapon, (GStruct177.EStationaryCommand)packet.StationaryPacket.Command);
			}

			if (packet.Interaction != EInteraction.None)
			{
				MovementContext.SetInteractInHands(packet.Interaction);
			}

			if (packet.HasVaultPacket)
			{
				DoObservedVault(packet.VaultPacket);
			}

			if (packet.HasMountingPacket)
			{
				MountingPacket mountPacket = packet.MountingPacket;

				switch (mountPacket.Command)
				{
					case GStruct179.EMountingCommand.Enter:
						{
							MovementContext.PlayerMountingPointData.SetData(new MountPointData(mountPacket.MountingPoint, mountPacket.MountDirection,
								(EMountSideDirection)mountPacket.MountingDirection), mountPacket.TargetPos, mountPacket.TargetPoseLevel, mountPacket.TargetHandsRotation,
								mountPacket.TransitionTime, mountPacket.TargetBodyRotation, mountPacket.PoseLimit, mountPacket.PitchLimit, mountPacket.YawLimit);
							MovementContext.PlayerMountingPointData.CurrentMountingPointVerticalOffset = mountPacket.CurrentMountingPointVerticalOffset;
							MovementContext.EnterMountedState();
						}
						break;
					case GStruct179.EMountingCommand.Exit:
						{
							MovementContext.ExitMountedState();
						}
						break;
					case GStruct179.EMountingCommand.Update:
						{
							MovementContext.PlayerMountingPointData.CurrentMountingPointVerticalOffset = mountPacket.CurrentMountingPointVerticalOffset;
						}
						break;
					case GStruct179.EMountingCommand.StartLeaving:
						{
							if (MovementContext is ObservedMovementContext observedMovementContext)
							{
								observedMovementContext.ObservedStartExitingMountedState();
							}
						}
						break;
					default:
						break;
				}

			}
		}

		public void HandleUsableItemPacket(in UsableItemPacket packet)
		{
			if (HandsController is UsableItemController usableItemController)
			{
				if (packet.ExamineWeapon)
				{
					usableItemController.ExamineWeapon();
				}

				if (packet.HasCompassState)
				{
					usableItemController.CompassState.Value = packet.CompassState;
				}

				if (packet.HasAim)
				{
					usableItemController.IsAiming = packet.AimState;
				}
			}
		}

		private void ObservedStationaryInteract(StationaryWeapon stationaryWeapon, GStruct177.EStationaryCommand command)
		{
			if (command == GStruct177.EStationaryCommand.Occupy)
			{
				stationaryWeapon.SetOperator(ProfileId, false);
				MovementContext.StationaryWeapon = stationaryWeapon;
				MovementContext.InteractionParameters = stationaryWeapon.GetInteractionParameters();
				MovementContext.PlayerAnimatorSetApproached(false);
				MovementContext.PlayerAnimatorSetStationary(true);
				MovementContext.PlayerAnimatorSetStationaryAnimation((int)stationaryWeapon.Animation);
				return;
			}
			if (command == GStruct177.EStationaryCommand.Leave)
			{
				return;
			}
			MovementContext.PlayerAnimatorSetStationary(false);
			if (MovementContext.StationaryWeapon != null)
			{
				MovementContext.StationaryWeapon.Unlock(ProfileId);
			}
		}

		public virtual void DoObservedVault(VaultPacket vaultPacket)
		{

		}

		public override void PauseAllEffectsOnPlayer()
		{
			ActiveHealthController.PauseAllEffects();
		}

		public override void UnpauseAllEffectsOnPlayer()
		{
			ActiveHealthController.UnpauseAllEffects();
		}

		public void HandleCallbackFromServer(in OperationCallbackPacket operationCallbackPacket)
		{
			if (OperationCallbacks.TryGetValue(operationCallbackPacket.CallbackId, out Action<ServerOperationStatus> callback))
			{
				if (operationCallbackPacket.OperationStatus != EOperationStatus.Started)
				{
					OperationCallbacks.Remove(operationCallbackPacket.CallbackId);
				}
				ServerOperationStatus status = new(operationCallbackPacket.OperationStatus, operationCallbackPacket.Error);
				callback(status);
			}
			else
			{
				FikaPlugin.Instance.FikaLogger.LogError($"Could not find CallbackId: {operationCallbackPacket.CallbackId}!");
			}
		}

		public virtual void HandleWeaponPacket(in WeaponPacket packet)
		{
			if (HandsController is CoopObservedFirearmController firearmController)
			{
				firearmController.HandleFirearmPacket(packet, _inventoryController);
			}

			if (packet.Loot)
			{
				HandsController.Loot(packet.Loot);
			}

			if (packet.HasGrenadePacket)
			{
				if (HandsController is CoopObservedGrenadeController grenadeController)
				{
					switch (packet.GrenadePacket.PacketType)
					{
						case GrenadePacketType.ExamineWeapon:
							{
								grenadeController.ExamineWeapon();
								break;
							}
						case GrenadePacketType.HighThrow:
							{
								grenadeController.HighThrow();
								break;
							}
						case GrenadePacketType.LowThrow:
							{
								grenadeController.LowThrow();
								break;
							}
						case GrenadePacketType.PullRingForHighThrow:
							{
								grenadeController.PullRingForHighThrow();
								break;
							}
						case GrenadePacketType.PullRingForLowThrow:
							{
								grenadeController.PullRingForLowThrow();
								break;
							}
					}
					if (packet.GrenadePacket.HasGrenade)
					{
						grenadeController.SpawnGrenade(0f, packet.GrenadePacket.GrenadePosition, packet.GrenadePacket.GrenadeRotation, packet.GrenadePacket.ThrowForce, packet.GrenadePacket.LowThrow);
					}

					if (packet.GrenadePacket.PlantTripwire)
					{
						grenadeController.PlantTripwire();
					}

					if (packet.GrenadePacket.ChangeToIdle)
					{
						grenadeController.ChangeFireMode(Weapon.EFireMode.grenadeThrowing);
					}

					if (packet.GrenadePacket.ChangeToPlant)
					{
						grenadeController.ChangeFireMode(Weapon.EFireMode.greanadePlanting);
					}
				}
				else if (HandsController is CoopObservedQuickGrenadeController quickGrenadeController)
				{
					if (packet.GrenadePacket.HasGrenade)
					{
						quickGrenadeController.SpawnGrenade(0f, packet.GrenadePacket.GrenadePosition, packet.GrenadePacket.GrenadeRotation, packet.GrenadePacket.ThrowForce, packet.GrenadePacket.LowThrow);
					}
				}
				else
				{
					FikaPlugin.Instance.FikaLogger.LogError($"HandleFirearmPacket::GrenadePacket: HandsController was not of type CoopObservedGrenadeController! Was {HandsController.GetType().Name}");
				}
			}

			if (packet.CancelGrenade)
			{
				if (HandsController is CoopObservedGrenadeController grenadeController)
				{
					grenadeController.vmethod_3();
				}
			}

			if (packet.HasCompassChange)
			{
				if (HandsController is ItemHandsController handsController)
				{
					handsController.CompassState.Value = packet.CompassState;
				}
			}

			if (packet.HasKnifePacket)
			{
				if (HandsController is CoopObservedKnifeController knifeController)
				{
					if (packet.KnifePacket.Examine)
					{
						knifeController.ExamineWeapon();
					}

					if (packet.KnifePacket.Kick)
					{
						knifeController.MakeKnifeKick();
					}

					if (packet.KnifePacket.AltKick)
					{
						knifeController.MakeAlternativeKick();
					}

					if (packet.KnifePacket.BreakCombo)
					{
						knifeController.BrakeCombo();
					}
				}
				else
				{
					FikaPlugin.Instance.FikaLogger.LogError($"HandleFirearmPacket::KnifePacket: HandsController was not of type CoopObservedKnifeController! Was {HandsController.GetType().Name}");
				}
			}
		}

		public override void ApplyExplosionDamageToArmor(Dictionary<GStruct209, float> armorDamage, DamageInfo damageInfo)
		{
			if (IsYourPlayer)
			{
				_preAllocatedArmorComponents.Clear();
				Inventory.GetPutOnArmorsNonAlloc(_preAllocatedArmorComponents);
				List<ArmorComponent> armorComponents = [];
				foreach (ArmorComponent armorComponent in _preAllocatedArmorComponents)
				{					
					float num = 0f;
					foreach (KeyValuePair<GStruct209, float> keyValuePair in armorDamage)
					{
						if (armorComponent.ShotMatches(keyValuePair.Key.BodyPartColliderType, keyValuePair.Key.ArmorPlateCollider))
						{
							num += keyValuePair.Value;
							armorComponents.Add(armorComponent);
						}
					}
					if (num > 0f)
					{
						num = armorComponent.ApplyExplosionDurabilityDamage(num, damageInfo, _preAllocatedArmorComponents);
						method_95(num, armorComponent);
					}
				}

				if (armorComponents.Count > 0)
				{
					QueueArmorDamagePackets([.. armorComponents]);
				}
			}
		}

		public void QueueArmorDamagePackets(ArmorComponent[] armorComponents)
		{
			int amount = armorComponents.Length;
			if (amount > 0)
			{
				string[] ids = new string[amount];
				float[] durabilities = new float[amount];

				for (int i = 0; i < amount; i++)
				{
					ids[i] = armorComponents[i].Item.Id;
					durabilities[i] = armorComponents[i].Repairable.Durability;
				}

				PacketSender.ArmorDamagePackets.Enqueue(new()
				{
					ItemIds = ids,
					Durabilities = durabilities,
				});
			}
		}

		public virtual void HandleDamagePacket(ref DamagePacket packet)
		{
			DamageInfo damageInfo = new()
			{
				Damage = packet.Damage,
				DamageType = packet.DamageType,
				BodyPartColliderType = packet.ColliderType,
				HitPoint = packet.Point,
				HitNormal = packet.HitNormal,
				Direction = packet.Direction,
				PenetrationPower = packet.PenetrationPower,
				BlockedBy = packet.BlockedBy,
				DeflectedBy = packet.DeflectedBy,
				SourceId = packet.SourceId,
				ArmorDamage = packet.ArmorDamage
			};

			if (!string.IsNullOrEmpty(packet.ProfileId))
			{
				IPlayerOwner player = Singleton<GameWorld>.Instance.GetAlivePlayerBridgeByProfileID(packet.ProfileId);

				if (player != null)
				{
					damageInfo.Player = player;
					if (IsYourPlayer)
					{
						if (!FikaPlugin.Instance.FriendlyFire && damageInfo.Player.iPlayer.GroupId == GroupId)
						{
							return;
						}
					}
				}

				// TODO: Fix this and consistently get the correct data...
				if (Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(packet.ProfileId).HandsController.Item is Weapon weapon)
				{
					damageInfo.Weapon = weapon;
				}
			}

			ShotReactions(damageInfo, packet.BodyPartType);
			ReceiveDamage(damageInfo.Damage, packet.BodyPartType, damageInfo.DamageType, packet.Absorbed, packet.Material);
			base.ApplyDamageInfo(damageInfo, packet.BodyPartType, packet.ColliderType, packet.Absorbed);
		}

		public void HandleArmorDamagePacket(ref ArmorDamagePacket packet)
		{
			for (int i = 0; i < packet.ItemIds.Length; i++)
			{
				_preAllocatedArmorComponents.Clear();
				Inventory.GetPutOnArmorsNonAlloc(_preAllocatedArmorComponents);
				foreach (ArmorComponent armorComponent in _preAllocatedArmorComponents)
				{
					if (armorComponent.Item.Id == packet.ItemIds[i])
					{
						armorComponent.Repairable.Durability = packet.Durabilities[i];
						armorComponent.Buff.TryDisableComponent(armorComponent.Repairable.Durability);
						armorComponent.Item.RaiseRefreshEvent(false, false);
						return;
					}
				}
				GStruct428<Item> gstruct = Singleton<GameWorld>.Instance.FindItemById(packet.ItemIds[i]);
				if (gstruct.Failed)
				{
					FikaPlugin.Instance.FikaLogger.LogError("HandleArmorDamagePacket: " + gstruct.Error);
					return;
				}
				ArmorComponent itemComponent = gstruct.Value.GetItemComponent<ArmorComponent>();
				if (itemComponent != null)
				{
					itemComponent.Repairable.Durability = packet.Durabilities[i];
					itemComponent.Buff.TryDisableComponent(itemComponent.Repairable.Durability);
					itemComponent.Item.RaiseRefreshEvent(false, false);
				}
			}
		}

		public void CheckAndResetControllers(ExitStatus exitStatus, float pastTime, string locationId, string exitName)
		{
			_questController?.CheckExitConditionCounters(exitStatus, pastTime, locationId, exitName, HealthController.BodyPartEffects, TriggerZones);
			_questController?.ResetCurrentNullableCounters();

			_achievementsController?.CheckExitConditionCounters(exitStatus, pastTime, locationId, exitName, HealthController.BodyPartEffects, TriggerZones);
			_achievementsController?.ResetCurrentNullableCounters();
		}

		public override void Dispose()
		{
			base.Dispose();
			if (PacketSender != null)
			{
				PacketSender.DestroyThis();
			}
		}

		public override void OnVaulting()
		{
			PacketSender.CommonPlayerPackets.Enqueue(new()
			{
				HasVaultPacket = true,
				VaultPacket = new()
				{
					VaultingStrategy = VaultingParameters.GetVaultingStrategy(),
					VaultingPoint = VaultingParameters.MaxWeightPointPosition,
					VaultingHeight = VaultingParameters.VaultingHeight,
					VaultingLength = VaultingParameters.VaultingLength,
					VaultingSpeed = MovementContext.VaultingSpeed,
					BehindObstacleHeight = VaultingParameters.BehindObstacleRatio,
					AbsoluteForwardVelocity = VaultingParameters.AbsoluteForwardVelocity
				}
			});
		}

		public void ReceiveTraderServicesData(List<TraderServicesClass> services)
		{
			if (!IsYourPlayer)
			{
				return;
			}

			Dictionary<ETraderServiceType, BackendConfigSettingsClass.ServiceData> servicesData = Singleton<BackendConfigSettingsClass>.Instance.ServicesData;

			foreach (TraderServicesClass service in services)
			{
				BackendConfigSettingsClass.ServiceData serviceData = new(service, null);
				if (servicesData.ContainsKey(serviceData.ServiceType))
				{
					servicesData[serviceData.ServiceType] = serviceData;
				}
				else
				{
					servicesData.Add(serviceData.ServiceType, serviceData);
				}
				if (!Profile.TradersInfo.TryGetValue(serviceData.TraderId, out Profile.TraderInfo traderInfo))
				{
					FikaPlugin.Instance.FikaLogger.LogWarning($"Can't find trader with id: {serviceData.TraderId}!");
				}
				else
				{
					traderInfo.SetServiceAvailability(serviceData.ServiceType, service.CanAfford, service.WasPurchasedInThisRaid);
				}
			}
		}

		public Item FindQuestItem(string itemId)
		{
			foreach (IKillableLootItem lootItem in Singleton<GameWorld>.Instance.LootList)
			{
				if (lootItem is LootItem observedLootItem)
				{
					if (observedLootItem.Item.TemplateId == itemId && observedLootItem.isActiveAndEnabled)
					{
						return observedLootItem.Item;
					}
				}
			}
#if DEBUG
			FikaPlugin.Instance.FikaLogger.LogInfo($"CoopPlayer::FindItem: Could not find questItem with id '{itemId}' in the current session, either the quest is not active or something else occured."); 
#endif
			return null;
		}

		#region handlers
		private class KeyHandler(CoopPlayer player)
		{
			private readonly CoopPlayer player = player;
			public GStruct428<GClass3263> unlockResult;

			internal void HandleKeyEvent()
			{
				unlockResult.Value.RaiseEvents(player._inventoryController, CommandStatus.Succeed);
			}
		}

		private class LootableContainerInteractionHandler(CoopPlayer player, LootableContainer container)
		{
			private readonly CoopPlayer player = player;
			public readonly LootableContainer container = container;

			public void Handle()
			{
				player.PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					HasContainerInteractionPacket = true,
					ContainerInteractionPacket = new()
					{
						InteractiveId = container.Id,
						InteractionType = EInteractionType.Close
					}
				});

				container.Interact(new InteractionResult(EInteractionType.Close));
				if (player.MovementContext.LevelOnApproachStart > 0f)
				{
					player.MovementContext.SetPoseLevel(player.MovementContext.LevelOnApproachStart, false);
					player.MovementContext.LevelOnApproachStart = -1f;
				}
			}
		}

		private class FirearmControllerHandler(CoopPlayer coopPlayer, Weapon weapon)
		{
			private readonly CoopPlayer coopPlayer = coopPlayer;
			public readonly Weapon weapon = weapon;
			public Process<FirearmController, IFirearmHandsController> process;
			public Action confirmCallback;

			internal CoopClientFirearmController ReturnController()
			{
				return CoopClientFirearmController.Create(coopPlayer, weapon);
			}

			internal void SendPacket()
			{
				coopPlayer.PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					HasProceedPacket = true,
					ProceedPacket = new()
					{
						ProceedType = weapon.IsStationaryWeapon ? EProceedType.Stationary : EProceedType.Weapon,
						ItemId = weapon.Id
					}
				});
			}

			internal void HandleResult(IResult result)
			{
				if (result.Succeed)
				{
					confirmCallback();
				}
			}
		}

		private class UsableItemControllerHandler(CoopPlayer coopPlayer, Item item)
		{
			private readonly CoopPlayer coopPlayer = coopPlayer;
			private readonly Item item = item;
			public Process<UsableItemController, GInterface156> process;
			public Action confirmCallback;

			internal CoopClientUsableItemController ReturnController()
			{
				return CoopClientUsableItemController.Create(coopPlayer, item);
			}

			internal void SendPacket()
			{
				coopPlayer.PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					HasProceedPacket = true,
					ProceedPacket = new()
					{
						ProceedType = EProceedType.UsableItem,
						ItemId = item.Id
					}
				});
			}

			internal void HandleResult(IResult result)
			{
				if (result.Succeed)
				{
					confirmCallback();
				}
			}
		}

		private class QuickUseItemControllerHandler(CoopPlayer coopPlayer, Item item)
		{
			private readonly CoopPlayer coopPlayer = coopPlayer;
			private readonly Item item = item;
			public Process<QuickUseItemController, IOnHandsUseCallback> process;
			public Action confirmCallback;

			internal QuickUseItemController ReturnController()
			{
				return QuickUseItemController.smethod_6<QuickUseItemController>(coopPlayer, item);
			}

			internal void SendPacket()
			{
				coopPlayer.PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					HasProceedPacket = true,
					ProceedPacket = new()
					{
						ProceedType = EProceedType.QuickUse,
						ItemId = item.Id
					}
				});
			}

			internal void HandleResult(IResult result)
			{
				if (result.Succeed)
				{
					confirmCallback();
				}
			}
		}

		private class MedsControllerHandler(CoopPlayer coopPlayer, MedsClass meds, EBodyPart bodyPart, int animationVariant)
		{
			private readonly CoopPlayer coopPlayer = coopPlayer;
			private readonly MedsClass meds = meds;
			private readonly EBodyPart bodyPart = bodyPart;
			private readonly int animationVariant = animationVariant;
			public Process<MedsController, GInterface157> process;
			public Action confirmCallback;

			internal MedsController ReturnController()
			{
				return MedsController.smethod_6<MedsController>(coopPlayer, meds, bodyPart, 1f, animationVariant);
			}

			internal void SendPacket()
			{
				coopPlayer.PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					HasProceedPacket = true,
					ProceedPacket = new()
					{
						ProceedType = EProceedType.MedsClass,
						ItemId = meds.Id,
						AnimationVariant = animationVariant,
						BodyPart = bodyPart
					}
				});
			}

			internal void HandleResult(IResult result)
			{
				if (result.Succeed)
				{
					confirmCallback();
				}
			}
		}

		private class FoodControllerHandler(CoopPlayer coopPlayer, FoodClass foodDrink, float amount, EBodyPart bodyPart, int animationVariant)
		{
			private readonly CoopPlayer coopPlayer = coopPlayer;
			private readonly FoodClass foodDrink = foodDrink;
			private readonly float amount = amount;
			private readonly EBodyPart bodyPart = bodyPart;
			private readonly int animationVariant = animationVariant;
			public Process<MedsController, GInterface157> process;
			public Action confirmCallback;

			internal MedsController ReturnController()
			{
				return MedsController.smethod_6<MedsController>(coopPlayer, foodDrink, EBodyPart.Head, amount, animationVariant);
			}

			internal void SendPacket()
			{
				coopPlayer.PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					HasProceedPacket = true,
					ProceedPacket = new()
					{
						ProceedType = EProceedType.MedsClass,
						ItemId = foodDrink.Id,
						Amount = amount,
						AnimationVariant = animationVariant,
						BodyPart = bodyPart
					}
				});
			}

			internal void HandleResult(IResult result)
			{
				if (result.Succeed)
				{
					confirmCallback();
				}
			}
		}

		private class KnifeControllerHandler(CoopPlayer coopPlayer, KnifeComponent knife)
		{
			private readonly CoopPlayer coopPlayer = coopPlayer;
			public readonly KnifeComponent knife = knife;
			public Process<KnifeController, IKnifeController> process;
			public Action confirmCallback;

			internal CoopClientKnifeController ReturnController()
			{
				return CoopClientKnifeController.Create(coopPlayer, knife);
			}

			internal void SendPacket()
			{
				coopPlayer.PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					HasProceedPacket = true,
					ProceedPacket = new()
					{
						ProceedType = EProceedType.Knife,
						ItemId = knife.Item.Id
					}
				});
			}

			internal void HandleResult(IResult result)
			{
				if (result.Succeed)
				{
					confirmCallback();
				}
			}
		}

		private class QuickKnifeControllerHandler(CoopPlayer coopPlayer, KnifeComponent knife)
		{
			private readonly CoopPlayer coopPlayer = coopPlayer;
			public readonly KnifeComponent knife = knife;
			public Process<QuickKnifeKickController, GInterface161> process;
			public Action confirmCallback;

			internal QuickKnifeKickController ReturnController()
			{
				return QuickKnifeKickController.smethod_9<QuickKnifeKickController>(coopPlayer, knife);
			}

			internal void SendPacket()
			{
				coopPlayer.PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					HasProceedPacket = true,
					ProceedPacket = new()
					{
						ProceedType = EProceedType.QuickKnifeKick,
						ItemId = knife.Item.Id
					}
				});
			}

			internal void HandleResult(IResult result)
			{
				if (result.Succeed)
				{
					confirmCallback();
				}
			}
		}

		private class GrenadeControllerHandler(CoopPlayer coopPlayer, GrenadeClass throwWeap)
		{
			private readonly CoopPlayer coopPlayer = coopPlayer;
			private readonly GrenadeClass throwWeap = throwWeap;
			public Process<GrenadeHandsController, IHandsThrowController> process;
			public Action confirmCallback;

			internal CoopClientGrenadeController ReturnController()
			{
				return CoopClientGrenadeController.Create(coopPlayer, throwWeap);
			}

			internal void SendPacket()
			{
				coopPlayer.PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					HasProceedPacket = true,
					ProceedPacket = new()
					{
						ProceedType = EProceedType.GrenadeClass,
						ItemId = throwWeap.Id
					}
				});
			}

			internal void HandleResult(IResult result)
			{
				if (result.Succeed)
				{
					confirmCallback();
				}
			}
		}

		private class QuickGrenadeControllerHandler(CoopPlayer coopPlayer, GrenadeClass throwWeap)
		{
			private readonly CoopPlayer coopPlayer = coopPlayer;
			private readonly GrenadeClass throwWeap = throwWeap;
			public Process<QuickGrenadeThrowHandsController, GInterface160> process;
			public Action confirmCallback;

			internal CoopClientQuickGrenadeController ReturnController()
			{
				return CoopClientQuickGrenadeController.Create(coopPlayer, throwWeap);
			}

			internal void SendPacket()
			{
				coopPlayer.PacketSender.CommonPlayerPackets.Enqueue(new()
				{
					HasProceedPacket = true,
					ProceedPacket = new()
					{
						ProceedType = EProceedType.QuickGrenadeThrow,
						ItemId = throwWeap.Id
					}
				});
			}

			internal void HandleResult(IResult result)
			{
				if (result.Succeed)
				{
					confirmCallback();
				}
			}
		}

		private class DropHandler(CoopPlayer coopPlayer)
		{
			private readonly CoopPlayer coopPlayer = coopPlayer;

			internal void HandleResult()
			{

			}
		}
	}
	#endregion
}
