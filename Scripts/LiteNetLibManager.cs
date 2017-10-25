﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using LiteNetLibHighLevel.Utils;

namespace LiteNetLibHighLevel
{
    [RequireComponent(typeof(LiteNetLibAssets))]
    public class LiteNetLibManager : MonoBehaviour
    {
        public enum LogLevel : short
        {
            Developer = 0,
            Debug = 1,
            Info = 2,
            Warn = 3,
            Error = 4,
            Fatal = 5,
        };

        public LiteNetLibClient Client { get; protected set; }
        public LiteNetLibServer Server { get; protected set; }
        public bool IsServer { get { return Server != null; } }
        public bool IsClient { get { return Client != null; } }
        public bool IsClientConnected { get { return Client != null && Client.IsConnected; } }
        public bool LogDev { get { return currentLogLevel <= LogLevel.Developer; } }
        public bool LogDebug { get { return currentLogLevel <= LogLevel.Debug; } }
        public bool LogInfo { get { return currentLogLevel <= LogLevel.Info; } }
        public bool LogWarn { get { return currentLogLevel <= LogLevel.Warn; } }
        public bool LogError { get { return currentLogLevel <= LogLevel.Error; } }
        public bool LogFatal { get { return currentLogLevel <= LogLevel.Fatal; } }

        [Header("Client & Server Configs")]
        public LogLevel currentLogLevel = LogLevel.Info;
        public string connectKey = "SampleConnectKey";
        public string networkAddress = "localhost";
        public int networkPort = 7770;
        [SerializeField, Tooltip("enable messages receiving without connection. (with SendUnconnectedMessage method), default value: false")]
        private bool unconnectedMessagesEnabled;
        [SerializeField, Tooltip("enable nat punch messages, default value: false")]
        private bool natPunchEnabled;
        [SerializeField, Tooltip("library logic update (and send) period in milliseconds, default value: 15 msec. For games you can use 15 msec(66 ticks per second)")]
        private int updateTime = 15;
        [SerializeField, Tooltip("Interval for latency detection and checking connection, default value: 1000 msec.")]
        private int pingInterval = 1000;
        [SerializeField, Tooltip("if client or server doesn't receive any packet from remote peer during this time then connection will be closed (including library internal keepalive packets), default value: 5000 msec.")]
        private int disconnectTimeout = 5000;
        [SerializeField, Tooltip("Merge small packets into one before sending to reduce outgoing packets count. (May increase a bit outgoing data size), default value: false")]
        private bool mergeEnabled;

        [Header("Network Simulation")]
        [SerializeField, Tooltip("simulate packet loss by dropping random amout of packets. (Works only in DEBUG mode), default value: false")]
        private bool simulatePacketLoss;
        [SerializeField, Tooltip("simulate latency by holding packets for random time. (Works only in DEBUG mode), default value: false")]
        private bool simulateLatency;
        [SerializeField, Tooltip("chance of packet loss when simulation enabled. value in percents, default value: 10(%)")]
        private int simulationPacketLossChance = 10;
        [SerializeField, Tooltip("minimum simulated latency, default value: 30 msec")]
        private int simulationMinLatency = 30;
        [SerializeField, Tooltip("maximum simulated latency, default value: 100 msec")]
        private int simulationMaxLatency = 100;

        [Header("Network Discovery")]
        [SerializeField, Tooltip("Allows receive DiscoveryRequests, default value: false")]
        private bool discoveryEnabled;
        public string discoveryRequestData;
        public string discoveryResponseData;

        [Header("Server Only Configs")]
        [SerializeField]
        private int maxConnections = 4;

        [Header("Client Only Configs")]
        [SerializeField, Tooltip("delay betwen connection attempts, default value: 500 msec")]
        private int reconnectDelay = 500;
        [SerializeField, Tooltip("maximum connection attempts before client stops and call disconnect event, default value: 10")]
        private int maxConnectAttempts = 10;

        protected readonly Dictionary<long, NetPeer> peers = new Dictionary<long, NetPeer>();
        protected readonly Dictionary<short, MessageHandlerDelegate> serverMessageHandlers = new Dictionary<short, MessageHandlerDelegate>();
        protected readonly Dictionary<short, MessageHandlerDelegate> clientMessageHandlers = new Dictionary<short, MessageHandlerDelegate>();

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

        protected virtual void Awake()
        {
            Assets.ClearRegisterPrefabs();
            Assets.RegisterPrefabs();
        }

        protected virtual void Update()
        {
            if (IsServer)
                Server.NetManager.PollEvents();
            if (IsClient)
            {
                Client.NetManager.PollEvents();
                if (discoveryEnabled)
                    Client.NetManager.SendDiscoveryRequest(StringBytesConverter.ConvertToBytes(discoveryRequestData), networkPort);
            }
        }

        protected virtual void OnDestroy()
        {
            if (IsServer)
                Server.NetManager.Stop();
            if (IsClient)
                Client.NetManager.Stop();
        }

        protected void SetConfigs(NetManager netManager)
        {
            netManager.UnconnectedMessagesEnabled = unconnectedMessagesEnabled;
            netManager.NatPunchEnabled = natPunchEnabled;
            netManager.UpdateTime = updateTime;
            netManager.PingInterval = pingInterval;
            netManager.DisconnectTimeout = disconnectTimeout;
            netManager.SimulatePacketLoss = simulatePacketLoss;
            netManager.SimulateLatency = simulateLatency;
            netManager.SimulationPacketLossChance = simulationPacketLossChance;
            netManager.SimulationMinLatency = simulationMinLatency;
            netManager.SimulationMaxLatency = simulationMaxLatency;
            netManager.DiscoveryEnabled = discoveryEnabled;
            netManager.MergeEnabled = mergeEnabled;
            netManager.ReconnectDelay = reconnectDelay;
            netManager.MaxConnectAttempts = maxConnectAttempts;
        }

        public virtual bool StartServer()
        {
            if (Server != null)
                return true;

            OnStartServer();
            Server = new LiteNetLibServer(this, maxConnections, connectKey);
            RegisterServerMessages();
            SetConfigs(Server.NetManager);
            if (!Server.NetManager.Start(networkPort))
            {
                if (LogError) Debug.LogError("[" + name + "] LiteNetLibManager::StartServer cannot start server at port: " + networkPort);
                Server = null;
                return false;
            }
            return true;
        }

        public virtual LiteNetLibClient StartClient()
        {
            if (Client != null)
                return Client;

            Client = new LiteNetLibClient(this, connectKey);
            RegisterClientMessages();
            SetConfigs(Client.NetManager);
            Client.NetManager.Start();
            Client.NetManager.Connect(networkAddress, networkPort);
            OnStartClient(Client);
            return Client;
        }

        public virtual LiteNetLibClient StartHost()
        {
            OnStartHost();
            if (StartServer())
            {
                var localClient = ConnectLocalClient();
                OnStartClient(localClient);
                return localClient;
            }
            return null;
        }

        protected virtual LiteNetLibClient ConnectLocalClient()
        {
            if (LogInfo) Debug.Log("[" + name + "] LiteNetLibManager::StartHost port: " + networkPort);
            networkAddress = "localhost";
            return StartClient();
        }

        public void StopHost()
        {
            OnStopHost();

            StopServer();
            StopClient();
        }

        public void StopServer()
        {
            if (Server == null)
                return;

            OnStopServer();

            if (LogInfo) Debug.Log("[" + name + "] LiteNetLibManager::StopServer");
            Server.NetManager.Stop();
            Server = null;
            peers.Clear();
        }

        public void StopClient()
        {
            if (Client == null)
                return;

            OnStopClient();

            if (LogInfo) Debug.Log("[" + name + "] LiteNetLibManager::StopClient");
            Client.NetManager.Stop();
            Client = null;
        }

        public void AddPeer(NetPeer peer)
        {
            if (peer == null)
                return;
            peers.Add(peer.ConnectId, peer);
        }

        public bool RemovePeer(NetPeer peer)
        {
            if (peer == null)
                return false;
            return peers.Remove(peer.ConnectId);
        }

        public bool TryGetPeer(long connectId, out NetPeer peer)
        {
            return peers.TryGetValue(connectId, out peer);
        }

        public void ServerReadPacket(NetPeer peer, NetDataReader reader)
        {
            ReadPacket(peer, reader, serverMessageHandlers);
        }

        public void ClientReadPacket(NetPeer peer, NetDataReader reader)
        {
            ReadPacket(peer, reader, clientMessageHandlers);
        }

        private void ReadPacket(NetPeer peer, NetDataReader reader, Dictionary<short, MessageHandlerDelegate> registerDict)
        {
            var msgType = reader.GetShort();
            MessageHandlerDelegate handlerDelegate;
            if (registerDict.TryGetValue(msgType, out handlerDelegate))
            {
                var messageHandler = new LiteNetLibMessageHandler();
                messageHandler.msgType = msgType;
                messageHandler.peer = peer;
                messageHandler.reader = reader;
                handlerDelegate.Invoke(messageHandler);
            }
        }

        public void RegisterServerMessage(short msgType, MessageHandlerDelegate handlerDelegate)
        {
            serverMessageHandlers[msgType] = handlerDelegate;
        }

        public void UnregisterServerMessage(short msgType)
        {
            serverMessageHandlers.Remove(msgType);
        }

        public void RegisterClientMessage(short msgType, MessageHandlerDelegate handlerDelegate)
        {
            clientMessageHandlers[msgType] = handlerDelegate;
        }

        public void UnregisterClientMessage(short msgType)
        {
            clientMessageHandlers.Remove(msgType);
        }

        public LiteNetLibIdentity NetworkSpawn(GameObject gameObject)
        {
            return Assets.NetworkSpawn(gameObject);
        }

        public bool NetworkDestroy(GameObject gameObject)
        {
            return Assets.NetworkDestroy(gameObject);
        }

        #region Messages Registeration
        protected virtual void RegisterServerMessages()
        {

        }

        protected virtual void RegisterClientMessages()
        {

        }
        #endregion

        #region Network Events Callbacks
        public virtual void OnServerNetworkError(NetEndPoint endPoint, int socketErrorCode)
        {

        }

        public virtual void OnServerConnected(NetPeer peer)
        {

        }

        public virtual void OnServerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {

        }

        public virtual void OnServerReceivedDiscoveryRequest(NetEndPoint endPoint, string data)
        {

        }

        public virtual void OnClientNetworkError(NetEndPoint endPoint, int socketErrorCode)
        {

        }

        public virtual void OnClientConnected(NetPeer peer)
        {

        }

        public virtual void OnClientDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {

        }

        public virtual void OnClientReceivedDiscoveryResponse(NetEndPoint endPoint, string data)
        {

        }
        #endregion

        #region Start / Stop Callbacks
        // Since there are multiple versions of StartServer, StartClient and StartHost, to reliably customize
        // their functionality, users would need override all the versions. Instead these callbacks are invoked
        // from all versions, so users only need to implement this one case.
        /// <summary>
        /// This hook is invoked when a host is started.
        /// </summary>
        public virtual void OnStartHost()
        {
            if (LogInfo) Debug.Log("[" + name + "] LiteNetLibManager::OnStartHost");
        }

        /// <summary>
        /// This hook is invoked when a server is started - including when a host is started.
        /// </summary>
        public virtual void OnStartServer()
        {
            if (LogInfo) Debug.Log("[" + name + "] LiteNetLibManager::OnStartServer");
        }

        /// <summary>
        /// This is a hook that is invoked when the client is started.
        /// </summary>
        /// <param name="client"></param>
        public virtual void OnStartClient(LiteNetLibClient client)
        {
            if (LogInfo) Debug.Log("[" + name + "] LiteNetLibManager::OnStartClient");
        }

        /// <summary>
        /// This hook is called when a server is stopped - including when a host is stopped.
        /// </summary>
        public virtual void OnStopServer()
        {
            if (LogInfo) Debug.Log("[" + name + "] LiteNetLibManager::OnStopServer");
        }

        /// <summary>
        /// This hook is called when a client is stopped.
        /// </summary>
        public virtual void OnStopClient()
        {
            if (LogInfo) Debug.Log("[" + name + "] LiteNetLibManager::OnStopClient");
        }

        /// <summary>
        /// This hook is called when a host is stopped.
        /// </summary>
        public virtual void OnStopHost()
        {
            if (LogInfo) Debug.Log("[" + name + "] LiteNetLibManager::OnStopHost");
        }
        #endregion
    }
}
