﻿using System;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace LiteNetLibHighLevel
{
    [RequireComponent(typeof(LiteNetLibAssets))]
    public class LiteNetLibGameManager : LiteNetLibManager
    {
        public class GameMsgTypes
        {
            public const short ClientReady = 1;
            public const short ClientCallFunction = 2;
            public const short ServerSpawnSceneObject = 3;
            public const short ServerSpawnObject = 4;
            public const short ServerDestroyObject = 5;
            public const short ServerUpdateSyncField = 6;
            public const short ServerCallFunction = 7;
            public const short Highest = 7;
        }

        private LiteNetLibAssets assets;
        public LiteNetLibAssets Assets
        {
            get
            {
                if (assets == null)
                    assets = GetComponent<LiteNetLibAssets>();
                return assets;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            Assets.ClearRegisteredPrefabs();
            Assets.RegisterPrefabs();
            Assets.RegisterSceneObjects();
        }

        public override bool StartServer()
        {
            if (base.StartServer())
            {
                Assets.SpawnSceneObjects();
                return true;
            }
            return false;
        }

        protected override void RegisterServerMessages()
        {
            base.RegisterServerMessages();
            RegisterServerMessage(GameMsgTypes.ClientReady, HandleClientReady);
            RegisterServerMessage(GameMsgTypes.ClientCallFunction, HandleClientCallFunction);
        }

        protected override void RegisterClientMessages()
        {
            base.RegisterClientMessages();
            RegisterClientMessage(GameMsgTypes.ServerSpawnSceneObject, HandleServerSpawnSceneObject);
            RegisterClientMessage(GameMsgTypes.ServerSpawnObject, HandleServerSpawnObject);
            RegisterClientMessage(GameMsgTypes.ServerDestroyObject, HandleServerDestroyObject);
            RegisterClientMessage(GameMsgTypes.ServerUpdateSyncField, HandleServerUpdateSyncField);
            RegisterClientMessage(GameMsgTypes.ServerCallFunction, HandleServerCallFunction);
        }

        public override void OnClientConnected(NetPeer peer)
        {
            base.OnClientConnected(peer);
            SendClientReady();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Assets.ClearSpawnedObjects();
            LiteNetLibIdentity.ResetObjectId();
            LiteNetLibAssets.ResetSpawnPositionCounter();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Assets.ClearSpawnedObjects();
            LiteNetLibIdentity.ResetObjectId();
            LiteNetLibAssets.ResetSpawnPositionCounter();
        }
        
        #region Relates components functions
        public LiteNetLibIdentity NetworkSpawn(GameObject gameObject)
        {
            return Assets.NetworkSpawn(gameObject);
        }

        public bool NetworkDestroy(GameObject gameObject)
        {
            return Assets.NetworkDestroy(gameObject);
        }
        #endregion

        public void SendClientReady()
        {
            if (!IsClientConnected)
                return;
            SendPacket(SendOptions.ReliableOrdered, Client.Peer, GameMsgTypes.ClientReady);
        }

        public void SendServerSpawnSceneObject(LiteNetLibIdentity identity)
        {
            if (!IsServer)
                return;
            foreach (var peer in peers.Values)
            {
                SendServerSpawnSceneObject(peer, identity);
            }
        }

        public void SendServerSpawnSceneObject(NetPeer peer, LiteNetLibIdentity identity)
        {
            if (!IsServer)
                return;
            var message = new ServerSpawnSceneObjectMessage();
            message.objectId = identity.ObjectId;
            message.position = identity.transform.position;
            SendPacket(SendOptions.ReliableOrdered, peer, GameMsgTypes.ServerSpawnSceneObject, message);
        }

        public void SendServerSpawnObject(LiteNetLibIdentity identity)
        {
            if (!IsServer)
                return;
            foreach (var peer in peers.Values)
            {
                SendServerSpawnObject(peer, identity);
            }
        }

        public void SendServerSpawnObject(NetPeer peer, LiteNetLibIdentity identity)
        {
            if (!IsServer)
                return;
            var message = new ServerSpawnObjectMessage();
            message.assetId = identity.AssetId;
            message.objectId = identity.ObjectId;
            message.connectId = identity.ConnectId;
            message.position = identity.transform.position;
            SendPacket(SendOptions.ReliableOrdered, peer, GameMsgTypes.ServerSpawnObject, message);
        }

        public void SendServerDestroyObject(uint objectId)
        {
            if (!IsServer)
                return;
            foreach (var peer in peers.Values)
            {
                SendServerDestroyObject(peer, objectId);
            }
        }

        public void SendServerDestroyObject(NetPeer peer, uint objectId)
        {
            if (!IsServer)
                return;
            var message = new ServerDestroyObjectMessage();
            message.objectId = objectId;
            SendPacket(SendOptions.ReliableOrdered, peer, GameMsgTypes.ServerDestroyObject, message);
        }

        public void SendServerUpdateSyncField<TField, TFieldType>(LiteNetLibSyncField<TField, TFieldType> syncField)
            where TField : LiteNetLibNetField<TFieldType>, new()
        {
            if (!IsServer)
                return;
            if (syncField.forOwnerOnly)
            {
                var connectId = syncField.Behaviour.ConnectId;
                if (peers.ContainsKey(connectId))
                    SendServerUpdateSyncField(peers[connectId], syncField);
            }
            else
            {
                foreach (var peer in peers.Values)
                {
                    SendServerUpdateSyncField(peer, syncField);
                }
            }
        }

        public void SendServerUpdateSyncField<TField, TFieldType>(NetPeer peer, LiteNetLibSyncField<TField, TFieldType> syncField)
            where TField : LiteNetLibNetField<TFieldType>, new()
        {
            if (!IsServer)
                return;
            SendPacket(syncField.sendOptions, peer, GameMsgTypes.ServerUpdateSyncField, (writer) => SerializeSyncField(writer, syncField));
        }

        protected void ServerCallNetFunction(NetPeer peer, LiteNetLibFunction netFunction)
        {
            SendPacket(netFunction.sendOptions, peer, GameMsgTypes.ServerCallFunction, (writer) => SerializeNetFunction(writer, netFunction));
        }

        protected void CallNetFunction(FunctionReceivers receivers, LiteNetLibFunction netFunction, long connectId)
        {
            if (IsServer)
            {
                switch (receivers)
                {
                    case FunctionReceivers.Target:
                        NetPeer targetPeer;
                        if (peers.TryGetValue(connectId, out targetPeer))
                            ServerCallNetFunction(targetPeer, netFunction);
                        break;
                    case FunctionReceivers.All:
                        foreach (var peer in peers.Values)
                        {
                            ServerCallNetFunction(peer, netFunction);
                        }
                        break;
                }
            }
            else if (IsClientConnected)
                SendPacket(netFunction.sendOptions, Client.Peer, GameMsgTypes.ClientCallFunction, (writer) => SerializeClientNetFunction(writer, receivers, netFunction, connectId));
        }

        public void CallNetFunction(FunctionReceivers receivers, LiteNetLibFunction netFunction)
        {
            CallNetFunction(receivers, netFunction, 0);
        }

        public void CallNetFunction(long connectId, LiteNetLibFunction netFunction)
        {
            CallNetFunction(FunctionReceivers.Target, netFunction, connectId);
        }

        protected void SerializeSyncField<TField, TFieldType>(NetDataWriter writer, LiteNetLibSyncField<TField, TFieldType> syncField)
            where TField : LiteNetLibNetField<TFieldType>, new()
        {
            var syncFieldInfo = syncField.GetSyncFieldInfo();
            writer.Put(syncFieldInfo.objectId);
            writer.Put(syncFieldInfo.behaviourIndex);
            writer.Put(syncFieldInfo.fieldId);
            syncField.Serialize(writer);
        }

        protected SyncFieldInfo DeserializeSyncFieldInfo(NetDataReader reader)
        {
            return new SyncFieldInfo(reader.GetUInt(), reader.GetInt(), reader.GetUShort());
        }

        protected void SerializeClientNetFunction(NetDataWriter writer, FunctionReceivers receivers, LiteNetLibFunction netFunction, long connectId)
        {
            writer.Put((byte)receivers);
            if (receivers == FunctionReceivers.Target)
                writer.Put(connectId);
            SerializeNetFunction(writer, netFunction);
        }

        protected void SerializeNetFunction(NetDataWriter writer, LiteNetLibFunction netFunction)
        {
            var netFunctionInfo = netFunction.GetNetFunctionInfo();
            writer.Put(netFunctionInfo.objectId);
            writer.Put(netFunctionInfo.behaviourIndex);
            writer.Put(netFunctionInfo.functionId);
            netFunction.Serialize(writer);
        }

        protected NetFunctionInfo DeserializeNetFunctionInfo(NetDataReader reader)
        {
            return new NetFunctionInfo(reader.GetUInt(), reader.GetInt(), reader.GetUShort());
        }

        protected virtual void SpawnPlayer(NetPeer peer)
        {
            if (Assets.PlayerPrefab == null)
                return;
            var spawnedObject = Assets.NetworkSpawn(Assets.PlayerPrefab.AssetId, Assets.GetPlayerSpawnPosition(), 0, peer.ConnectId);
            spawnedObject.SendUpdateAllSyncFields(peer);
        }

        protected virtual void HandleClientReady(LiteNetLibMessageHandler messageHandler)
        {
            var peer = messageHandler.peer;
            var spawnedObjects = Assets.SpawnedObjects.Values;
            foreach (var spawnedObject in spawnedObjects)
            {
                if (Assets.SceneObjects.ContainsKey(spawnedObject.ObjectId))
                    SendServerSpawnSceneObject(peer, spawnedObject);
                else
                    SendServerSpawnObject(peer, spawnedObject);
                spawnedObject.SendUpdateAllSyncFields(peer);
            }
            SpawnPlayer(peer);
        }

        protected virtual void HandleClientCallFunction(LiteNetLibMessageHandler messageHandler)
        {
            var reader = messageHandler.reader;
            FunctionReceivers receivers = (FunctionReceivers)reader.GetByte();
            long connectId = 0;
            if (receivers == FunctionReceivers.Target)
                connectId = reader.GetLong();
            NetFunctionInfo info = DeserializeNetFunctionInfo(reader);
            LiteNetLibIdentity identity;
            if (Assets.SpawnedObjects.TryGetValue(info.objectId, out identity))
            {
                if (receivers == FunctionReceivers.Server)
                    identity.ProcessNetFunction(info, reader, true);
                else
                {
                    var netFunction = identity.ProcessNetFunction(info, reader, false);
                    if (receivers == FunctionReceivers.Target)
                        netFunction.Call(connectId);
                    else
                        netFunction.Call(receivers);
                }
            }
        }

        protected virtual void HandleServerSpawnSceneObject(LiteNetLibMessageHandler messageHandler)
        {
            // Object spawned at server, if this is host (client and server) then skip it.
            if (IsServer)
                return;
            var message = messageHandler.ReadMessage<ServerSpawnSceneObjectMessage>();
            Assets.NetworkSpawnScene(message.objectId, message.position);
        }

        protected virtual void HandleServerSpawnObject(LiteNetLibMessageHandler messageHandler)
        {
            // Object spawned at server, if this is host (client and server) then skip it.
            if (IsServer)
                return;
            var message = messageHandler.ReadMessage<ServerSpawnObjectMessage>();
            Assets.NetworkSpawn(message.assetId, message.position, message.objectId, message.connectId);
        }

        protected virtual void HandleServerDestroyObject(LiteNetLibMessageHandler messageHandler)
        {
            // Object spawned at server, if this is host (client and server) then skip it.
            if (IsServer)
                return;
            var message = messageHandler.ReadMessage<ServerDestroyObjectMessage>();
            Assets.NetworkDestroy(message.objectId);
        }

        protected virtual void HandleServerUpdateSyncField(LiteNetLibMessageHandler messageHandler)
        {
            // Field updated at server, if this is host (client and server) then skip it.
            if (IsServer)
                return;
            var reader = messageHandler.reader;
            SyncFieldInfo info = DeserializeSyncFieldInfo(reader);
            LiteNetLibIdentity identity;
            if (Assets.SpawnedObjects.TryGetValue(info.objectId, out identity))
                identity.ProcessSyncField(info, reader);
        }

        protected virtual void HandleServerCallFunction(LiteNetLibMessageHandler messageHandler)
        {
            var reader = messageHandler.reader;
            NetFunctionInfo info = DeserializeNetFunctionInfo(reader);
            LiteNetLibIdentity identity;
            if (Assets.SpawnedObjects.TryGetValue(info.objectId, out identity))
                identity.ProcessNetFunction(info, reader, true);
        }
    }
}
