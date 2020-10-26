﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine.Profiling;
using Cysharp.Threading.Tasks;

namespace LiteNetLibManager
{
    [RequireComponent(typeof(LiteNetLibAssets))]
    public class LiteNetLibGameManager : LiteNetLibManager
    {
        public class GameMsgTypes
        {
            public const ushort EnterGame = 0;
            public const ushort ClientReady = 1;
            public const ushort ClientNotReady = 2;
            public const ushort CallFunction = 3;
            public const ushort ServerSpawnSceneObject = 4;
            public const ushort ServerSpawnObject = 5;
            public const ushort ServerDestroyObject = 6;
            public const ushort UpdateSyncField = 7;
            public const ushort InitialSyncField = 8;
            public const ushort OperateSyncList = 9;
            public const ushort ServerSyncBehaviour = 11;
            public const ushort ServerError = 12;
            public const ushort ServerSceneChange = 13;
            public const ushort ClientSendTransform = 14;
            public const ushort ServerSetObjectOwner = 15;
            public const ushort Ping = 16;
            public const ushort GenericResponse = 17;
            public const ushort Highest = 17;
        }

        public class DestroyObjectReasons
        {
            public const byte RequestedToDestroy = 0;
            public const byte RemovedFromSubscribing = 1;
            public const byte Highest = 1;
        }

        public float pingDuration = 1f;
        public bool doNotEnterGameOnConnect;
        public bool doNotDestroyOnSceneChanges;

        protected readonly Dictionary<long, LiteNetLibPlayer> Players = new Dictionary<long, LiteNetLibPlayer>();

        private float tempDeltaTime;
        private float sendPingCountDown;
        private string serverSceneName;
        private AsyncOperation loadSceneAsyncOperation;
        private bool isPinging;
        private long pingTime;

        public long ClientConnectionId { get; protected set; }

        public long Rtt { get; private set; }
        public long Timestamp { get { return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); } }
        public long ServerUnixTimeOffset { get; protected set; }
        public long ServerUnixTime
        {
            get
            {
                if (IsServer)
                    return Timestamp;
                return Timestamp + ServerUnixTimeOffset;
            }
        }

        public string ServerSceneName
        {
            get
            {
                if (IsServer)
                    return serverSceneName;
                return string.Empty;
            }
        }

        public LiteNetLibAssets Assets { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            Assets = GetComponent<LiteNetLibAssets>();
            serverSceneName = string.Empty;
            if (doNotDestroyOnSceneChanges)
                DontDestroyOnLoad(gameObject);
        }

        protected override void LateUpdate()
        {
            if (loadSceneAsyncOperation == null)
            {
                tempDeltaTime = Time.unscaledDeltaTime;
                // Update Spawned Objects
                Profiler.BeginSample("LiteNetLibGameManager - Update Spawned Objects");
                foreach (LiteNetLibIdentity spawnedObject in Assets.GetSpawnedObjects())
                {
                    if (spawnedObject == null)
                        continue;
                    spawnedObject.NetworkUpdate(tempDeltaTime);
                }
                Profiler.EndSample();

                if (IsClientConnected)
                {
                    // Send ping from client
                    sendPingCountDown -= tempDeltaTime;
                    if (sendPingCountDown <= 0f && !isPinging)
                    {
                        SendClientPing();
                        sendPingCountDown = pingDuration;
                    }
                }
            }
            base.LateUpdate();
        }

        public virtual uint PacketVersion()
        {
            return 1;
        }

        public bool TryGetPlayer(long connectionId, out LiteNetLibPlayer player)
        {
            return Players.TryGetValue(connectionId, out player);
        }

        public bool ContainsPlayer(long connectionId)
        {
            return Players.ContainsKey(connectionId);
        }

        public LiteNetLibPlayer GetPlayer(long connectionId)
        {
            return Players[connectionId];
        }

        public Dictionary<long, LiteNetLibPlayer>.ValueCollection GetPlayers()
        {
            return Players.Values;
        }

        public int PlayersCount
        {
            get { return Players.Count; }
        }

        /// <summary>
        /// Call this function to change gameplay scene at server, then the server will tell clients to change scene
        /// </summary>
        /// <param name="sceneName"></param>
        public void ServerSceneChange(string sceneName)
        {
            if (!IsServer)
                return;
            LoadSceneRoutine(sceneName, true).Forget();
        }

        /// <summary>
        /// This function will be called to load scene async
        /// </summary>
        /// <param name="sceneName"></param>
        /// <param name="online"></param>
        /// <returns></returns>
        private async UniTaskVoid LoadSceneRoutine(string sceneName, bool online)
        {
            if (loadSceneAsyncOperation == null)
            {
                // If doNotDestroyOnSceneChanges not TRUE still not destroy this game object
                // But it will be destroyed after scene loaded, if scene is offline scene
                if (!doNotDestroyOnSceneChanges)
                    DontDestroyOnLoad(gameObject);

                if (online)
                {
                    foreach (LiteNetLibPlayer player in Players.Values)
                    {
                        player.IsReady = false;
                        player.SubscribingObjects.Clear();
                        player.SpawnedObjects.Clear();
                    }
                    Assets.Clear(true);
                }

                if (LogDev) Logging.Log(LogTag, "Loading Scene: " + sceneName + " is online: " + online);
                if (Assets.onLoadSceneStart != null)
                    Assets.onLoadSceneStart.Invoke(sceneName, online, 0f);

                loadSceneAsyncOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                while (loadSceneAsyncOperation != null && !loadSceneAsyncOperation.isDone)
                {
                    if (Assets.onLoadSceneProgress != null)
                        Assets.onLoadSceneProgress.Invoke(sceneName, online, loadSceneAsyncOperation.progress);
                    await UniTask.Yield();
                }
                loadSceneAsyncOperation = null;

                if (online)
                {
                    Assets.Initialize();
                    if (LogDev) Logging.Log(LogTag, "Loaded Scene: " + sceneName + " -> Assets.Initialize()");
                    if (IsServer)
                    {
                        serverSceneName = sceneName;
                        Assets.SpawnSceneObjects();
                        if (LogDev) Logging.Log(LogTag, "Loaded Scene: " + sceneName + " -> Assets.SpawnSceneObjects()");
                        OnServerOnlineSceneLoaded();
                        if (LogDev) Logging.Log(LogTag, "Loaded Scene: " + sceneName + " -> OnServerOnlineSceneLoaded()");
                        SendServerSceneChange(sceneName);
                        if (LogDev) Logging.Log(LogTag, "Loaded Scene: " + sceneName + " -> SendServerSceneChange()");
                    }
                    if (IsClient)
                    {
                        while (ClientConnectionId < 0)
                        {
                            // Wait for client connection Id from server
                            await UniTask.Yield();
                        }
                        OnClientOnlineSceneLoaded();
                        if (LogDev) Logging.Log(LogTag, "Loaded Scene: " + sceneName + " -> OnClientOnlineSceneLoaded()");
                        SendClientReady();
                        if (LogDev) Logging.Log(LogTag, "Loaded Scene: " + sceneName + " -> SendClientReady()");
                    }
                }
                else if (!doNotDestroyOnSceneChanges)
                {
                    // Destroy manager's game object if loaded scene is not online scene
                    Destroy(gameObject);
                }

                if (LogDev) Logging.Log(LogTag, "Loaded Scene: " + sceneName + " is online: " + online);
                if (Assets.onLoadSceneFinish != null)
                    Assets.onLoadSceneFinish.Invoke(sceneName, online, 1f);
            }
        }

        protected override void RegisterServerMessages()
        {
            base.RegisterServerMessages();
            RegisterServerMessage(GameMsgTypes.EnterGame, HandleClientEnterGame);
            RegisterServerMessage(GameMsgTypes.ClientReady, HandleClientReady);
            RegisterServerMessage(GameMsgTypes.ClientNotReady, HandleClientNotReady);
            RegisterServerMessage(GameMsgTypes.CallFunction, HandleClientCallFunction);
            RegisterServerMessage(GameMsgTypes.UpdateSyncField, HandleClientUpdateSyncField);
            RegisterServerMessage(GameMsgTypes.InitialSyncField, HandleClientInitialSyncField);
            RegisterServerMessage(GameMsgTypes.ClientSendTransform, HandleClientSendTransform);
            RegisterServerMessage(GameMsgTypes.Ping, HandleClientPing);
            RegisterServerMessage(GameMsgTypes.GenericResponse, HandleGenericResponse);
        }

        protected override void RegisterClientMessages()
        {
            base.RegisterClientMessages();
            RegisterClientMessage(GameMsgTypes.ServerSpawnSceneObject, HandleServerSpawnSceneObject);
            RegisterClientMessage(GameMsgTypes.ServerSpawnObject, HandleServerSpawnObject);
            RegisterClientMessage(GameMsgTypes.ServerDestroyObject, HandleServerDestroyObject);
            RegisterClientMessage(GameMsgTypes.CallFunction, HandleServerCallFunction);
            RegisterClientMessage(GameMsgTypes.UpdateSyncField, HandleServerUpdateSyncField);
            RegisterClientMessage(GameMsgTypes.InitialSyncField, HandleServerInitialSyncField);
            RegisterClientMessage(GameMsgTypes.OperateSyncList, HandleServerUpdateSyncList);
            RegisterClientMessage(GameMsgTypes.ServerSyncBehaviour, HandleServerSyncBehaviour);
            RegisterClientMessage(GameMsgTypes.ServerError, HandleServerError);
            RegisterClientMessage(GameMsgTypes.ServerSceneChange, HandleServerSceneChange);
            RegisterClientMessage(GameMsgTypes.ServerSetObjectOwner, HandleServerSetObjectOwner);
            RegisterClientMessage(GameMsgTypes.Ping, HandleServerPing);
            RegisterClientMessage(GameMsgTypes.GenericResponse, HandleGenericResponse);
        }

        public override void OnPeerConnected(long connectionId)
        {
            base.OnPeerConnected(connectionId);
            if (!Players.ContainsKey(connectionId))
                Players[connectionId] = new LiteNetLibPlayer(this, connectionId);
        }

        public override void OnPeerDisconnected(long connectionId, DisconnectInfo disconnectInfo)
        {
            base.OnPeerDisconnected(connectionId, disconnectInfo);
            if (Players.ContainsKey(connectionId))
            {
                LiteNetLibPlayer player = Players[connectionId];
                player.ClearSubscribing(false);
                player.DestroyAllObjects();
                Players.Remove(connectionId);
            }
        }

        public override void OnClientConnected()
        {
            base.OnClientConnected();
            // Reset client connection id, will be received from server later
            ClientConnectionId = -1;
            isPinging = false;
            Rtt = 0;
            if (!doNotEnterGameOnConnect)
                SendClientEnterGame();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Reset client connection id, will be received from server later
            ClientConnectionId = -1;
            isPinging = false;
            Rtt = 0;
            if (!Assets.onlineScene.IsSet() || Assets.onlineScene.SceneName.Equals(SceneManager.GetActiveScene().name))
            {
                serverSceneName = SceneManager.GetActiveScene().name;
                Assets.Initialize();
                Assets.SpawnSceneObjects();
                OnServerOnlineSceneLoaded();
            }
            else
            {
                serverSceneName = Assets.onlineScene.SceneName;
                LoadSceneRoutine(Assets.onlineScene.SceneName, true).Forget();
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Players.Clear();
            Assets.Clear();
            if (Assets.offlineScene.IsSet() && !Assets.offlineScene.SceneName.Equals(SceneManager.GetActiveScene().name))
                LoadSceneRoutine(Assets.offlineScene.SceneName, false).Forget();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (!IsServer)
            {
                Players.Clear();
                Assets.Clear();
                if (Assets.offlineScene.IsSet() && !Assets.offlineScene.SceneName.Equals(SceneManager.GetActiveScene().name))
                    LoadSceneRoutine(Assets.offlineScene.SceneName, false).Forget();
            }
        }

        #region Send messages functions
        public void SendClientEnterGame()
        {
            if (!IsClientConnected)
                return;
            ClientSendRequest<BaseAckMessage, EnterGameResponseMessage>(
                GameMsgTypes.EnterGame,
                new BaseAckMessage(),
                OnEnterGame,
                SerializeEnterGameData);
        }

        protected virtual void OnEnterGame(EnterGameResponseMessage message)
        {
            if (message.responseCode == AckResponseCode.Success)
            {
                ClientConnectionId = message.connectionId;
                HandleServerSceneChange(message.serverSceneName);
            }
            else
            {
                if (LogInfo) Logging.Log(LogTag, "Enter game request was refused by server, disconnecting...");
                StopClient();
            }
        }

        public void SendClientReady()
        {
            if (!IsClientConnected)
                return;
            ClientSendRequest<BaseAckMessage, BaseAckMessage>(
                GameMsgTypes.ClientReady,
                new BaseAckMessage(),
                OnClientReady,
                SerializeClientReadyData);
        }

        protected virtual void OnClientReady(BaseAckMessage message)
        {

        }

        public void SendClientNotReady()
        {
            if (!IsClientConnected)
                return;
            ClientSendRequest<BaseAckMessage, BaseAckMessage>(
                GameMsgTypes.ClientNotReady,
                new BaseAckMessage(),
                OnClientNotReady,
                SerializeEnterGameData);
        }

        protected virtual void OnClientNotReady(BaseAckMessage message)
        {

        }

        public void SendClientPing()
        {
            if (!IsClientConnected)
                return;
            if (isPinging)
                return;
            isPinging = true;
            pingTime = Timestamp;
            ClientSendPacket(DeliveryMethod.ReliableOrdered, GameMsgTypes.Ping);
        }

        public void SendServerSpawnSceneObject(LiteNetLibIdentity identity)
        {
            if (!IsServer)
                return;
            foreach (long connectionId in ConnectionIds)
            {
                SendServerSpawnSceneObject(connectionId, identity);
            }
        }

        public void SendServerSpawnSceneObject(long connectionId, LiteNetLibIdentity identity)
        {
            if (!IsServer)
                return;
            LiteNetLibPlayer player;
            if (!Players.TryGetValue(connectionId, out player) || !player.IsReady)
                return;
            ServerSpawnSceneObjectMessage message = new ServerSpawnSceneObjectMessage();
            message.objectId = identity.ObjectId;
            message.connectionId = identity.ConnectionId;
            message.position = identity.transform.position;
            message.rotation = identity.transform.rotation;
            ServerSendPacket(connectionId, DeliveryMethod.ReliableOrdered, GameMsgTypes.ServerSpawnSceneObject, message, identity.WriteInitialSyncFields);
        }

        public void SendServerSpawnObject(LiteNetLibIdentity identity)
        {
            if (!IsServer)
                return;
            foreach (long connectionId in ConnectionIds)
            {
                SendServerSpawnObject(connectionId, identity);
            }
        }

        public void SendServerSpawnObject(long connectionId, LiteNetLibIdentity identity)
        {
            if (!IsServer)
                return;
            LiteNetLibPlayer player;
            if (!Players.TryGetValue(connectionId, out player) || !player.IsReady)
                return;
            ServerSpawnObjectMessage message = new ServerSpawnObjectMessage();
            message.hashAssetId = identity.HashAssetId;
            message.objectId = identity.ObjectId;
            message.connectionId = identity.ConnectionId;
            message.position = identity.transform.position;
            message.rotation = identity.transform.rotation;
            ServerSendPacket(connectionId, DeliveryMethod.ReliableOrdered, GameMsgTypes.ServerSpawnObject, message, identity.WriteInitialSyncFields);
        }

        public void SendServerSpawnObjectWithData(long connectionId, LiteNetLibIdentity identity)
        {
            if (identity == null)
                return;

            if (Assets.ContainsSceneObject(identity.ObjectId))
                SendServerSpawnSceneObject(connectionId, identity);
            else
                SendServerSpawnObject(connectionId, identity);
            identity.SendInitSyncFields(connectionId);
            identity.SendInitSyncLists(connectionId);
        }

        public void SendServerDestroyObject(uint objectId, byte reasons)
        {
            if (!IsServer)
                return;
            foreach (long connectionId in ConnectionIds)
            {
                SendServerDestroyObject(connectionId, objectId, reasons);
            }
        }

        public void SendServerDestroyObject(long connectionId, uint objectId, byte reasons)
        {
            if (!IsServer)
                return;
            LiteNetLibPlayer player;
            if (!Players.TryGetValue(connectionId, out player) || !player.IsReady)
                return;
            ServerDestroyObjectMessage message = new ServerDestroyObjectMessage();
            message.objectId = objectId;
            message.reasons = reasons;
            ServerSendPacket(connectionId, DeliveryMethod.ReliableOrdered, GameMsgTypes.ServerDestroyObject, message);
        }

        public void SendServerError(bool shouldDisconnect, string errorMessage)
        {
            if (!IsServer)
                return;
            foreach (long connectionId in ConnectionIds)
            {
                SendServerError(connectionId, shouldDisconnect, errorMessage);
            }
        }

        public void SendServerError(long connectionId, bool shouldDisconnect, string errorMessage)
        {
            if (!IsServer)
                return;
            LiteNetLibPlayer player;
            if (!Players.TryGetValue(connectionId, out player) || !player.IsReady)
                return;
            ServerErrorMessage message = new ServerErrorMessage();
            message.shouldDisconnect = shouldDisconnect;
            message.errorMessage = errorMessage;
            ServerSendPacket(connectionId, DeliveryMethod.ReliableOrdered, GameMsgTypes.ServerDestroyObject, message);
        }

        public void SendServerSceneChange(string sceneName)
        {
            if (!IsServer)
                return;
            foreach (long connectionId in ConnectionIds)
            {
                if (IsClientConnected && connectionId == ClientConnectionId)
                    continue;
                SendServerSceneChange(connectionId, sceneName);
            }
        }

        public void SendServerSceneChange(long connectionId, string sceneName)
        {
            if (!IsServer)
                return;
            ServerSceneChangeMessage message = new ServerSceneChangeMessage();
            message.serverSceneName = sceneName;
            ServerSendPacket(connectionId, DeliveryMethod.ReliableOrdered, GameMsgTypes.ServerSceneChange, message);
        }

        public void SendServerSetObjectOwner(uint objectId, long ownerConnectionId)
        {
            if (!IsServer)
                return;
            foreach (long connectionId in ConnectionIds)
            {
                SendServerSetObjectOwner(connectionId, objectId, ownerConnectionId);
            }
        }

        public void SendServerSetObjectOwner(long connectionId, uint objectId, long ownerConnectionId)
        {
            if (!IsServer)
                return;
            ServerSetObjectOwner message = new ServerSetObjectOwner();
            message.objectId = objectId;
            message.connectionId = ownerConnectionId;
            ServerSendPacket(connectionId, DeliveryMethod.ReliableOrdered, GameMsgTypes.ServerSetObjectOwner, message);
        }
        #endregion

        #region Message Handlers
        protected void HandleClientEnterGame(LiteNetLibMessageHandler messageHandler)
        {
            BaseAckMessage message = messageHandler.ReadMessage<BaseAckMessage>();
            EnterGameResponseMessage response = new EnterGameResponseMessage()
            {
                ackId = message.ackId,
            };
            if (DeserializeEnterGameData(messageHandler.connectionId, messageHandler.reader))
            {
                response.responseCode = AckResponseCode.Success;
                response.connectionId = messageHandler.connectionId;
                response.serverSceneName = ServerSceneName;
            }
            else
            {
                response.responseCode = AckResponseCode.Error;
            }
            ServerSendResponse(messageHandler.connectionId, response);
        }

        protected virtual void HandleClientReady(LiteNetLibMessageHandler messageHandler)
        {
            BaseAckMessage message = messageHandler.ReadMessage<BaseAckMessage>();
            BaseAckMessage response = new BaseAckMessage()
            {
                ackId = message.ackId,
            };
            if (SetPlayerReady(messageHandler.connectionId, messageHandler.reader))
            {
                response.responseCode = AckResponseCode.Success;
            }
            else
            {
                response.responseCode = AckResponseCode.Error;
            }
            ServerSendResponse(messageHandler.connectionId, response);
        }

        protected virtual void HandleClientNotReady(LiteNetLibMessageHandler messageHandler)
        {
            BaseAckMessage message = messageHandler.ReadMessage<BaseAckMessage>();
            BaseAckMessage response = new BaseAckMessage()
            {
                ackId = message.ackId,
            };
            if (SetPlayerNotReady(messageHandler.connectionId, messageHandler.reader))
            {
                response.responseCode = AckResponseCode.Success;
            }
            else
            {
                response.responseCode = AckResponseCode.Error;
            }
            ServerSendResponse(messageHandler.connectionId, response);
        }

        protected virtual void HandleClientInitialSyncField(LiteNetLibMessageHandler messageHandler)
        {
            // Field updated at owner-client, if this is server then multicast message to other clients
            if (!IsServer)
                return;
            NetDataReader reader = messageHandler.reader;
            LiteNetLibElementInfo info = LiteNetLibElementInfo.DeserializeInfo(reader);
            LiteNetLibIdentity identity;
            if (Assets.TryGetSpawnedObject(info.objectId, out identity))
            {
                LiteNetLibSyncField syncField = identity.GetSyncField(info);
                // Sync field at server also have to be client multicast to allow it to multicast to other clients
                if (syncField != null && syncField.syncMode == LiteNetLibSyncField.SyncMode.ClientMulticast)
                {
                    // If this is server but it is not host, set data (deserialize) then pass to other clients
                    // If this is host don't set data because it's already did (in LiteNetLibSyncField class)
                    if (!identity.IsOwnerClient)
                        syncField.Deserialize(reader, true);
                    // Send to other clients
                    foreach (long connectionId in GetConnectionIds())
                    {
                        // Don't send the update to owner client because it was updated before send update to server
                        if (connectionId == messageHandler.connectionId)
                            continue;
                        // Send update to clients except owner client
                        if (identity.IsSubscribedOrOwning(connectionId))
                            syncField.SendUpdate(true, connectionId);
                    }
                }
            }
        }

        protected virtual void HandleClientUpdateSyncField(LiteNetLibMessageHandler messageHandler)
        {
            // Field updated at owner-client, if this is server then multicast message to other clients
            if (!IsServer)
                return;
            NetDataReader reader = messageHandler.reader;
            LiteNetLibElementInfo info = LiteNetLibElementInfo.DeserializeInfo(reader);
            LiteNetLibIdentity identity;
            if (Assets.TryGetSpawnedObject(info.objectId, out identity))
            {
                LiteNetLibSyncField syncField = identity.GetSyncField(info);
                // Sync field at server also have to be client multicast to allow it to multicast to other clients
                if (syncField != null && syncField.syncMode == LiteNetLibSyncField.SyncMode.ClientMulticast)
                {
                    // If this is server but it is not host, set data (deserialize) then pass to other clients
                    // If this is host don't set data because it's already did (in LiteNetLibSyncField class)
                    if (!identity.IsOwnerClient)
                        syncField.Deserialize(reader, false);
                    // Send to other clients
                    foreach (long connectionId in GetConnectionIds())
                    {
                        // Don't send the update to owner client because it was updated before send update to server
                        if (connectionId == messageHandler.connectionId)
                            continue;
                        // Send update to clients except owner client
                        if (identity.IsSubscribedOrOwning(connectionId))
                            syncField.SendUpdate(false, connectionId);
                    }
                }
            }
        }

        protected virtual void HandleClientCallFunction(LiteNetLibMessageHandler messageHandler)
        {
            NetDataReader reader = messageHandler.reader;
            FunctionReceivers receivers = (FunctionReceivers)reader.GetByte();
            long connectionId = -1;
            if (receivers == FunctionReceivers.Target)
                connectionId = reader.GetPackedLong();
            LiteNetLibElementInfo info = LiteNetLibElementInfo.DeserializeInfo(reader);
            LiteNetLibIdentity identity;
            if (Assets.TryGetSpawnedObject(info.objectId, out identity))
            {
                LiteNetLibFunction netFunction = identity.GetNetFunction(info);
                if (netFunction == null)
                {
                    // There is no net function that player try to call (player may try to hack)
                    return;
                }
                if (!netFunction.CanCallByEveryone && messageHandler.connectionId != identity.ConnectionId)
                {
                    // The function not allowed anyone except owner client to call this net function
                    // And the client is also not the owner client
                    return;
                }
                if (receivers == FunctionReceivers.Server)
                {
                    // Request from client to server, so hook callback at server immediately
                    identity.ProcessNetFunction(netFunction, reader, true);
                }
                else
                {
                    // Request from client to other clients, so hook callback later
                    identity.ProcessNetFunction(netFunction, reader, false);
                    // Use call with out parameters set because parameters already set while process net function
                    if (receivers == FunctionReceivers.Target)
                        netFunction.CallWithoutParametersSet(connectionId);
                    else
                        netFunction.CallWithoutParametersSet(DeliveryMethod.ReliableOrdered, receivers);
                }
            }
        }

        protected virtual void HandleClientSendTransform(LiteNetLibMessageHandler messageHandler)
        {
            NetDataReader reader = messageHandler.reader;
            uint objectId = reader.GetPackedUInt();
            byte behaviourIndex = reader.GetByte();
            LiteNetLibIdentity identity;
            if (Assets.TryGetSpawnedObject(objectId, out identity))
            {
                LiteNetLibTransform netTransform;
                if (identity.TryGetBehaviour(behaviourIndex, out netTransform))
                    netTransform.HandleClientSendTransform(reader);
            }
        }

        protected void HandleClientPing(LiteNetLibMessageHandler messageHandler)
        {
            ServerSendPacket(messageHandler.connectionId, DeliveryMethod.ReliableOrdered, GameMsgTypes.Ping, (writer) =>
            {
                // Send server time
                writer.PutPackedLong(ServerUnixTime);
            });
        }

        protected virtual void HandleServerSpawnSceneObject(LiteNetLibMessageHandler messageHandler)
        {
            ServerSpawnSceneObjectMessage message = messageHandler.ReadMessage<ServerSpawnSceneObjectMessage>();
            if (!IsServer)
                Assets.NetworkSpawnScene(message.objectId, message.position, message.rotation, message.objectId);
            LiteNetLibIdentity identity;
            if (Assets.TryGetSpawnedObject(message.objectId, out identity))
            {
                // If it is not server, read its initial data
                if (!IsServer)
                    identity.ReadInitialSyncFields(messageHandler.reader);
                // If it is host, it may hidden so show it
                if (IsServer)
                    identity.OnServerSubscribingAdded();
            }
        }

        protected virtual void HandleServerSpawnObject(LiteNetLibMessageHandler messageHandler)
        {
            ServerSpawnObjectMessage message = messageHandler.ReadMessage<ServerSpawnObjectMessage>();
            if (!IsServer)
                Assets.NetworkSpawn(message.hashAssetId, message.position, message.rotation, message.objectId, message.connectionId);
            LiteNetLibIdentity identity;
            if (Assets.TryGetSpawnedObject(message.objectId, out identity))
            {
                // If it is not server, read its initial data
                if (!IsServer)
                    identity.ReadInitialSyncFields(messageHandler.reader);
                // If it is host, it may hidden so show it
                if (IsServer)
                    identity.OnServerSubscribingAdded();
            }
        }

        protected virtual void HandleServerDestroyObject(LiteNetLibMessageHandler messageHandler)
        {
            ServerDestroyObjectMessage message = messageHandler.ReadMessage<ServerDestroyObjectMessage>();
            if (!IsServer)
                Assets.NetworkDestroy(message.objectId, message.reasons);
            // If this is host and reasons is removed from subscribing so hide it, don't destroy it
            LiteNetLibIdentity identity;
            if (IsServer && message.reasons == DestroyObjectReasons.RemovedFromSubscribing && Assets.TryGetSpawnedObject(message.objectId, out identity))
                identity.OnServerSubscribingRemoved();
        }

        protected virtual void HandleServerInitialSyncField(LiteNetLibMessageHandler messageHandler)
        {
            // Field updated at server, if this is host (client and server) then skip it.
            if (IsServer)
                return;
            NetDataReader reader = messageHandler.reader;
            LiteNetLibElementInfo info = LiteNetLibElementInfo.DeserializeInfo(reader);
            LiteNetLibIdentity identity;
            if (Assets.TryGetSpawnedObject(info.objectId, out identity))
                identity.ProcessSyncField(info, reader, true);
        }

        protected virtual void HandleServerUpdateSyncField(LiteNetLibMessageHandler messageHandler)
        {
            // Field updated at server, if this is host (client and server) then skip it.
            if (IsServer)
                return;
            NetDataReader reader = messageHandler.reader;
            LiteNetLibElementInfo info = LiteNetLibElementInfo.DeserializeInfo(reader);
            LiteNetLibIdentity identity;
            if (Assets.TryGetSpawnedObject(info.objectId, out identity))
                identity.ProcessSyncField(info, reader, false);
        }

        protected virtual void HandleServerCallFunction(LiteNetLibMessageHandler messageHandler)
        {
            NetDataReader reader = messageHandler.reader;
            LiteNetLibElementInfo info = LiteNetLibElementInfo.DeserializeInfo(reader);
            LiteNetLibIdentity identity;
            if (Assets.TryGetSpawnedObject(info.objectId, out identity))
            {
                // All function from server will be processed (because it's always trust server)
                identity.ProcessNetFunction(info, reader, true);
            }
        }

        protected virtual void HandleServerUpdateSyncList(LiteNetLibMessageHandler messageHandler)
        {
            // List updated at server, if this is host (client and server) then skip it.
            if (IsServer)
                return;
            NetDataReader reader = messageHandler.reader;
            LiteNetLibElementInfo info = LiteNetLibElementInfo.DeserializeInfo(reader);
            LiteNetLibIdentity identity;
            if (Assets.TryGetSpawnedObject(info.objectId, out identity))
                identity.ProcessSyncList(info, reader);
        }

        protected virtual void HandleServerSyncBehaviour(LiteNetLibMessageHandler messageHandler)
        {
            // Behaviour sync from server, if this is host (client and server) then skip it.
            if (IsServer)
                return;
            NetDataReader reader = messageHandler.reader;
            uint objectId = reader.GetPackedUInt();
            byte behaviourIndex = reader.GetByte();
            LiteNetLibIdentity identity;
            if (Assets.TryGetSpawnedObject(objectId, out identity))
                identity.ProcessSyncBehaviour(behaviourIndex, reader);
        }

        protected virtual void HandleServerError(LiteNetLibMessageHandler messageHandler)
        {
            // Error sent from server
            ServerErrorMessage message = messageHandler.ReadMessage<ServerErrorMessage>();
            OnServerError(message);
        }

        protected virtual void HandleServerSceneChange(LiteNetLibMessageHandler messageHandler)
        {
            // Received scene name from server
            ServerSceneChangeMessage message = messageHandler.ReadMessage<ServerSceneChangeMessage>();
            HandleServerSceneChange(message.serverSceneName);
        }

        protected virtual void HandleServerSceneChange(string serverSceneName)
        {
            if (IsServer)
            {
                // If it is host (both client and server) it will send ready state to spawn player's character without scene load
                if (string.IsNullOrEmpty(serverSceneName) || serverSceneName.Equals(SceneManager.GetActiveScene().name))
                {
                    OnClientOnlineSceneLoaded();
                    SendClientReady();
                }
                return;
            }

            if (string.IsNullOrEmpty(serverSceneName) || serverSceneName.Equals(SceneManager.GetActiveScene().name))
            {
                Assets.Initialize();
                OnClientOnlineSceneLoaded();
                SendClientReady();
            }
            else
            {
                // If scene is difference, load changing scene
                LoadSceneRoutine(serverSceneName, true).Forget();
            }
        }

        protected virtual void HandleServerSetObjectOwner(LiteNetLibMessageHandler messageHandler)
        {
            ServerSetObjectOwner message = messageHandler.ReadMessage<ServerSetObjectOwner>();
            if (!IsServer)
                Assets.SetObjectOwner(message.objectId, message.connectionId);
        }

        protected void HandleServerPing(LiteNetLibMessageHandler messageHandler)
        {
            isPinging = false;
            Rtt = Timestamp - pingTime;
            // Time offset = server time - current timestamp - rtt
            ServerUnixTimeOffset = messageHandler.reader.GetPackedLong() - Timestamp - Rtt;
            if (LogDev) Logging.Log(LogTag, "Rtt: " + Rtt + ", ServerUnixTimeOffset: " + ServerUnixTimeOffset);
        }
        #endregion

        protected void HandleGenericResponse(LiteNetLibMessageHandler messageHandler)
        {
            messageHandler.ReadResponse();
        }

        /// <summary>
        /// Overrride this function to send custom data when send enter game message
        /// </summary>
        /// <param name="writer"></param>
        public virtual void SerializeEnterGameData(NetDataWriter writer) { }

        /// <summary>
        /// Override this function to read custom data that come with enter game message
        /// </summary>
        /// <param name="connectionId"></param>
        /// <param name="reader"></param>
        /// <returns>Return `true` if allow player to enter game.</returns>
        public virtual bool DeserializeEnterGameData(long connectionId, NetDataReader reader) { return true; }

        /// <summary>
        /// Overrride this function to send custom data when send client ready message
        /// </summary>
        /// <param name="writer"></param>
        public virtual void SerializeClientReadyData(NetDataWriter writer) { }

        /// <summary>
        /// Override this function to read custom data that come with client ready message
        /// </summary>
        /// <param name="playerIdentity"></param>
        /// <param name="connectionId"></param>
        /// <param name="reader"></param>
        /// <returns>Return `true` if player is ready to play.</returns>
        public virtual bool DeserializeClientReadyData(LiteNetLibIdentity playerIdentity, long connectionId, NetDataReader reader) { return true; }

        /// <summary>
        /// Override this function to do anything after online scene loaded at server side
        /// </summary>
        public virtual void OnServerOnlineSceneLoaded() { }

        /// <summary>
        /// Override this function to do anything after online scene loaded at client side
        /// </summary>
        public virtual void OnClientOnlineSceneLoaded() { }

        /// <summary>
        /// Override this function to show error message / disconnect
        /// </summary>
        /// <param name="message"></param>
        public virtual void OnServerError(ServerErrorMessage message)
        {
            if (message.shouldDisconnect && !IsServer)
                StopClient();
        }

        public virtual bool SetPlayerReady(long connectionId, NetDataReader reader)
        {
            if (!IsServer)
                return false;

            LiteNetLibPlayer player = Players[connectionId];
            if (player.IsReady)
                return false;

            player.IsReady = true;
            if (!DeserializeClientReadyData(SpawnPlayer(connectionId), connectionId, reader))
            {
                player.IsReady = false;
                return false;
            }

            foreach (LiteNetLibIdentity spawnedObject in Assets.GetSpawnedObjects())
            {
                if (spawnedObject.ConnectionId == player.ConnectionId)
                    continue;

                if (spawnedObject.ShouldAddSubscriber(player))
                    spawnedObject.AddSubscriber(player);
            }
            return true;
        }

        public virtual bool SetPlayerNotReady(long connectionId, NetDataReader reader)
        {
            if (!IsServer)
                return false;

            LiteNetLibPlayer player = Players[connectionId];
            if (!player.IsReady)
                return false;

            player.IsReady = false;
            player.ClearSubscribing(true);
            player.DestroyAllObjects();
            return true;
        }

        protected LiteNetLibIdentity SpawnPlayer(long connectionId)
        {
            if (Assets.PlayerPrefab == null)
                return null;
            return SpawnPlayer(connectionId, Assets.PlayerPrefab);
        }

        protected LiteNetLibIdentity SpawnPlayer(long connectionId, LiteNetLibIdentity prefab)
        {
            if (prefab == null)
                return null;
            return SpawnPlayer(connectionId, prefab.HashAssetId);
        }

        protected LiteNetLibIdentity SpawnPlayer(long connectionId, int hashAssetId)
        {
            LiteNetLibIdentity spawnedObject = Assets.NetworkSpawn(hashAssetId, Assets.GetPlayerSpawnPosition(), Quaternion.identity, 0, connectionId);
            if (spawnedObject != null)
                return spawnedObject;
            return null;
        }

        public void ClientSendResponse<TResponse>(TResponse response, System.Action<NetDataWriter> extraSerializer = null)
            where TResponse : BaseAckMessage, new()
        {
            Client.SendResponse(GameMsgTypes.GenericResponse, response, extraSerializer);
        }

        public void ServerSendResponse<TResponse>(long connectionId, TResponse response, System.Action<NetDataWriter> extraSerializer = null)
            where TResponse : BaseAckMessage, new()
        {
            Server.SendResponse(connectionId, GameMsgTypes.GenericResponse, response, extraSerializer);
        }
    }
}
