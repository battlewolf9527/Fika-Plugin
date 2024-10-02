﻿using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using Fika.Core.Coop.GameMode;
using Fika.Core.Coop.Players;
using Fika.Core.Coop.Utils;
using Fika.Core.Networking;
using Fika.Core.Utils;
using LiteNetLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fika.Core.Coop.Components
{
	/// <summary>
	/// CoopHandler is the User 1-2-1 communication to the Server. This can be seen as an extension component to CoopGame.
	/// </summary>
	public class CoopHandler : MonoBehaviour
	{
		#region Fields/Properties
		public CoopGame LocalGameInstance { get; internal set; }
		public string ServerId { get; set; } = null;
		public Dictionary<int, CoopPlayer> Players = [];
		public List<CoopPlayer> HumanPlayers = [];
		public int AmountOfHumans = 1;
		public List<int> ExtractedPlayers = [];
		public List<string> queuedProfileIds = [];
		public CoopPlayer MyPlayer
		{
			get
			{
				return (CoopPlayer)Singleton<GameWorld>.Instance.MainPlayer;
			}
		}

		private ManualLogSource Logger;
		private readonly Queue<SpawnObject> spawnQueue = new(50);
		private bool ready;
		private bool isClient;
		private float charSyncCounter;
		private bool requestQuitGame = false;

		public class SpawnObject(Profile profile, Vector3 position, bool isAlive, bool isAI, int netId, MongoID currentId, ushort firstOperationId)
		{
			public Profile Profile = profile;
			public Vector3 Position = position;
			public bool IsAlive = isAlive;
			public bool IsAI = isAI;
			public int NetId = netId;
			public MongoID CurrentId = currentId;
			public ushort FirstOperationId = firstOperationId;
			public EHandsControllerType ControllerType;
			public string ItemId;
			public bool IsStationary;
			public byte[] HealthBytes;
		}
		#endregion

		public static bool TryGetCoopHandler(out CoopHandler coopHandler)
		{
			coopHandler = null;
			IFikaNetworkManager networkManager = Singleton<IFikaNetworkManager>.Instance;
			if (networkManager != null)
			{
				coopHandler = networkManager.CoopHandler;
				return true;
			}

			return false;
		}

		public static string GetServerId()
		{
			IFikaNetworkManager networkManager = Singleton<IFikaNetworkManager>.Instance;
			if (networkManager != null && networkManager.CoopHandler != null)
			{
				return networkManager.CoopHandler.ServerId;
			}

			return FikaBackendUtils.GetGroupId();
		}

		protected void Awake()
		{
			Logger = BepInEx.Logging.Logger.CreateLogSource("CoopHandler");
		}

		protected void Start()
		{
			if (FikaBackendUtils.IsClient)
			{
				isClient = true;
				charSyncCounter = 0f;
				return;
			}

			isClient = false;
			ready = true;
			Singleton<GameWorld>.Instance.World_0.method_0(null);
		}

		protected private void Update()
		{
			if (LocalGameInstance == null)
			{
				return;
			}

			if (!ready)
			{
				return;
			}

			if (spawnQueue.Count > 0)
			{
				SpawnPlayer(spawnQueue.Dequeue());
			}

			ProcessQuitting();

			if (isClient)
			{
				charSyncCounter += Time.deltaTime;
				int waitTime = LocalGameInstance.Status == GameStatus.Started ? 15 : 2;

				if (charSyncCounter > waitTime)
				{
					charSyncCounter = 0f;

					if (Players == null)
					{
						return;
					}

					SyncPlayersWithServer();
				}
			}
		}

		protected void OnDestroy()
		{
			Players.Clear();
			HumanPlayers.Clear();
		}

		public EQuitState GetQuitState()
		{
			EQuitState quitState = EQuitState.None;

			if (LocalGameInstance == null)
			{
				return quitState;
			}

			if (MyPlayer == null)
			{
				return quitState;
			}

			// Check alive status
			if (!MyPlayer.HealthController.IsAlive)
			{
				quitState = EQuitState.Dead;
			}

			// Extractions
			if (LocalGameInstance.ExtractedPlayers.Contains(MyPlayer.NetId))
			{
				quitState = EQuitState.Extracted;
			}

			return quitState;
		}

		/// <summary>
		/// This handles the ways of exiting the active game session
		/// </summary>
		private void ProcessQuitting()
		{
			EQuitState quitState = GetQuitState();

			if (FikaPlugin.ExtractKey.Value.IsDown() && quitState != EQuitState.None && !requestQuitGame)
			{
				//Log to both the in-game console as well as into the BepInEx logfile
				ConsoleScreen.Log($"{FikaPlugin.ExtractKey.Value} pressed, attempting to extract!");
				Logger.LogInfo($"{FikaPlugin.ExtractKey.Value} pressed, attempting to extract!");

				requestQuitGame = true;
				CoopGame coopGame = (CoopGame)Singleton<IFikaGame>.Instance;

				// If you are the server / host
				if (FikaBackendUtils.IsServer)
				{
					// A host needs to wait for the team to extract or die!
					if ((Singleton<FikaServer>.Instance.NetServer.ConnectedPeersCount > 0) && quitState != EQuitState.None)
					{
						NotificationManagerClass.DisplayWarningNotification(LocaleUtils.HOST_CANNOT_EXTRACT.Localized());
						requestQuitGame = false;
						return;
					}
					else if (Singleton<FikaServer>.Instance.NetServer.ConnectedPeersCount == 0
						&& Singleton<FikaServer>.Instance.TimeSinceLastPeerDisconnected > DateTime.Now.AddSeconds(-5)
						&& Singleton<FikaServer>.Instance.HasHadPeer)
					{
						NotificationManagerClass.DisplayWarningNotification(LocaleUtils.HOST_WAIT_5_SECONDS.Localized());
						requestQuitGame = false;
						return;
					}
					else
					{
						coopGame.Stop(Singleton<GameWorld>.Instance.MainPlayer.ProfileId, coopGame.MyExitStatus, MyPlayer.ActiveHealthController.IsAlive ? coopGame.MyExitLocation : null, 0);
					}
				}
				else
				{
					coopGame.Stop(Singleton<GameWorld>.Instance.MainPlayer.ProfileId, coopGame.MyExitStatus, MyPlayer.ActiveHealthController.IsAlive ? coopGame.MyExitLocation : null, 0);
				}
				return;
			}
		}

		public void SetReady(bool state)
		{
			ready = state;
		}

		private void SyncPlayersWithServer()
		{
			AllCharacterRequestPacket requestPacket = new(MyPlayer.ProfileId);

			if (Players.Count > 0)
			{
				requestPacket.HasCharacters = true;
				List<string> characters = new(queuedProfileIds);
				foreach (CoopPlayer player in Players.Values)
				{
					characters.Add(player.ProfileId);
				}
				requestPacket.Characters = [.. characters];
			}

			FikaClient client = Singleton<FikaClient>.Instance;
			if (client.NetClient.FirstPeer != null)
			{
				client.SendData(ref requestPacket, DeliveryMethod.ReliableOrdered);
			}
		}

		private async void SpawnPlayer(SpawnObject spawnObject)
		{
			if (spawnObject.Profile == null)
			{
				Logger.LogError("SpawnPlayer: Profile was null!");
				queuedProfileIds.Remove(spawnObject.Profile.ProfileId);
				return;
			}

			foreach (IPlayer player in Singleton<GameWorld>.Instance.AllPlayersEverExisted)
			{
				if (player.ProfileId == spawnObject.Profile.ProfileId)
				{
					return;
				}
			}

			int playerId = LocalGameInstance.method_15();

			IEnumerable<ResourceKey> allPrefabPaths = spawnObject.Profile.GetAllPrefabPaths();
			if (allPrefabPaths.Count() == 0)
			{
				Logger.LogError($"SpawnPlayer::{spawnObject.Profile.Info.Nickname}::PrefabPaths are empty!");
				return;
			}

			await Singleton<PoolManager>.Instance.LoadBundlesAndCreatePools(PoolManager.PoolsCategory.Raid,
				PoolManager.AssemblyType.Local, allPrefabPaths.ToArray(), JobPriority.Low).ContinueWith(x =>
				{
					if (x.IsFaulted)
					{
						Logger.LogError($"SpawnPlayer::{spawnObject.Profile.Info.Nickname}::Load Failed");
					}
					else if (x.IsCanceled)
					{
						Logger.LogError($"SpawnPlayer::{spawnObject.Profile.Info.Nickname}::Load Cancelled");
					}
				});

			ObservedCoopPlayer otherPlayer = SpawnObservedPlayer(spawnObject);

			if (!spawnObject.IsAlive)
			{
				otherPlayer.OnDead(EDamageType.Undefined);
				otherPlayer.NetworkHealthController.IsAlive = false;
			}

			if (FikaBackendUtils.IsServer)
			{
				if (LocalGameInstance != null)
				{
					BotsController botController = LocalGameInstance.BotsController;
					if (botController != null)
					{
						// Start Coroutine as botController might need a while to start sometimes...
#if DEBUG
						Logger.LogInfo("Starting AddClientToBotEnemies routine.");
#endif
						StartCoroutine(AddClientToBotEnemies(botController, otherPlayer));
					}
					else
					{
						Logger.LogError("botController was null when trying to add player to enemies!");
					}
				}
				else
				{
					Logger.LogError("LocalGameInstance was null when trying to add player to enemies!");
				}
			}

			queuedProfileIds.Remove(spawnObject.Profile.ProfileId);
		}

		public void QueueProfile(Profile profile, byte[] healthByteArray, Vector3 position, int netId, bool isAlive, bool isAI, MongoID firstId, ushort firstOperationId,
			EHandsControllerType controllerType = EHandsControllerType.None, string itemId = null)
		{
			GameWorld gameWorld = Singleton<GameWorld>.Instance;
			if (gameWorld == null)
			{
				return;
			}

			foreach (IPlayer player in gameWorld.AllPlayersEverExisted)
			{
				if (player.ProfileId == profile.ProfileId)
				{
					return;
				}
			}

			if (queuedProfileIds.Contains(profile.ProfileId))
			{
				return;
			}

			queuedProfileIds.Add(profile.ProfileId);
#if DEBUG
			Logger.LogInfo($"Queueing profile: {profile.Nickname}, {profile.ProfileId}");
#endif
			SpawnObject spawnObject = new(profile, position, isAlive, isAI, netId, firstId, firstOperationId);
			if (controllerType != EHandsControllerType.None)
			{
				spawnObject.ControllerType = controllerType;
				if (!string.IsNullOrEmpty(itemId))
				{
					spawnObject.ItemId = itemId;
				}
			}
			if (healthByteArray != null)
			{
				spawnObject.HealthBytes = healthByteArray;
			}
			spawnQueue.Enqueue(spawnObject);
		}

		private ObservedCoopPlayer SpawnObservedPlayer(SpawnObject spawnObject)
		{
			bool isAi = spawnObject.IsAI;
			Profile profile = spawnObject.Profile;
			Vector3 position = spawnObject.Position;
			int netId = spawnObject.NetId;
			MongoID firstId = spawnObject.CurrentId;
			ushort firstOperationId = spawnObject.FirstOperationId;
			bool isDedicatedProfile = !isAi && profile.Info.MainProfileNickname.Contains("dedicated_");
			byte[] healthBytes = spawnObject.HealthBytes;

			// Handle null bytes on players
			if (!isAi && (spawnObject.HealthBytes == null || spawnObject.HealthBytes.Length == 0))
			{
				healthBytes = profile.Health.SerializeHealthInfo();
			}

			// Check for GClass increments on filter
			ObservedCoopPlayer otherPlayer = ObservedCoopPlayer.CreateObservedPlayer(LocalGameInstance.GameWorld_0, netId, position, Quaternion.identity, "Player",
				isAi == true ? "Bot_" : $"Player_{profile.Nickname}_", EPointOfView.ThirdPerson, profile, healthBytes, isAi,
				EUpdateQueue.Update, Player.EUpdateMode.Manual, Player.EUpdateMode.Auto,
				BackendConfigAbstractClass.Config.CharacterController.ObservedPlayerMode, FikaGlobals.GetOtherPlayerSensitivity, FikaGlobals.GetOtherPlayerSensitivity,
				GClass1574.Default, firstId, firstOperationId).Result;

			if (otherPlayer == null)
			{
				return null;
			}

			otherPlayer.NetId = netId;
#if DEBUG
			Logger.LogInfo($"SpawnObservedPlayer: {profile.Nickname} spawning with NetId {netId}");
#endif
			if (!isAi)
			{
				AmountOfHumans++;
			}

			if (!Players.ContainsKey(netId))
			{
				Players.Add(netId, otherPlayer);
			}
			else
			{
				Logger.LogError($"Trying to add {otherPlayer.Profile.Nickname} to list of players but it was already there!");
			}

			if (!isAi && !isDedicatedProfile && !HumanPlayers.Contains(otherPlayer))
			{
				HumanPlayers.Add(otherPlayer);
			}

			foreach (CoopPlayer player in Players.Values)
			{
				if (player is not ObservedCoopPlayer)
				{
					continue;
				}

				Collider playerCollider = otherPlayer.GetCharacterControllerCommon().GetCollider();
				Collider otherCollider = player.GetCharacterControllerCommon().GetCollider();

				if (playerCollider != null && otherCollider != null)
				{
					EFTPhysicsClass.IgnoreCollision(playerCollider, otherCollider);
				}
			}

			if (isAi)
			{
				if (profile.Info.Side is EPlayerSide.Bear or EPlayerSide.Usec)
				{
					Item backpack = profile.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;
					backpack?.GetAllItems()
						.Where(i => i != backpack)
						.ExecuteForEach(i => i.SpawnedInSession = true);

					// We still want DogTags to be 'FiR'
					Item item = otherPlayer.Inventory.Equipment.GetSlot(EquipmentSlot.Dogtag).ContainedItem;
					if (item != null)
					{
						item.SpawnedInSession = true;
					}
				}
			}
			else if (profile.Info.Side != EPlayerSide.Savage)// Make Player PMC items are all not 'FiR'
			{
				profile.SetSpawnedInSession(false);
			}

			otherPlayer.InitObservedPlayer(isDedicatedProfile);

#if DEBUG
			Logger.LogInfo($"CreateLocalPlayer::{profile.Info.Nickname}::Spawned.");
#endif

			EHandsControllerType controllerType = spawnObject.ControllerType;
			string itemId = spawnObject.ItemId;
			bool isStationary = spawnObject.IsStationary;
			if (controllerType != EHandsControllerType.None)
			{
				if (controllerType != EHandsControllerType.Empty && string.IsNullOrEmpty(itemId))
				{
					Logger.LogError($"CreateLocalPlayer: ControllerType was not Empty but itemId was null! ControllerType: {controllerType}");
				}
				else
				{
					otherPlayer.SpawnHandsController(controllerType, itemId, isStationary);
				}
			}
			return otherPlayer;
		}

		private IEnumerator AddClientToBotEnemies(BotsController botController, LocalPlayer playerToAdd)
		{
			CoopGame coopGame = LocalGameInstance;

			Logger.LogInfo($"AddClientToBotEnemies: " + playerToAdd.Profile.Nickname);

			while (coopGame.Status != GameStatus.Running && !botController.IsEnable)
			{
				yield return null;
			}

			while (coopGame.BotsController.BotSpawner == null)
			{
				yield return null;
			}

#if DEBUG
			Logger.LogInfo($"Adding Client {playerToAdd.Profile.Nickname} to enemy list");
#endif
			botController.AddActivePLayer(playerToAdd);

			bool found = false;

			for (int i = 0; i < botController.BotSpawner.PlayersCount; i++)
			{
				if (botController.BotSpawner.GetPlayer(i) == playerToAdd)
				{
					found = true;
					break;
				}
			}

			if (found)
			{
#if DEBUG
				Logger.LogInfo($"Verified that {playerToAdd.Profile.Nickname} was added to the enemy list.");
#endif
			}
			else
			{
				Logger.LogError($"Failed to add {playerToAdd.Profile.Nickname} to the enemy list.");
			}
		}

		/// <summary>
		/// The state your character or game is in to Quit.
		/// </summary>
		public enum EQuitState
		{
			None = -1,
			Dead,
			Extracted
		}
	}
}