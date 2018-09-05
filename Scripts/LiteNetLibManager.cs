﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using LiteNetLibManager.Utils;
using UnityEngine.Profiling;

namespace LiteNetLibManager
{
    public class LiteNetLibManager : MonoBehaviour
    {
        public enum LogLevel : byte
        {
            Developer = 0,
            Debug = 1,
            Info = 2,
            Warn = 3,
            Error = 4,
            Fatal = 5,
        }

        public LiteNetLibClient Client { get; protected set; }
        public LiteNetLibServer Server { get; protected set; }
        public bool IsServer { get { return Server != null; } }
        public bool IsClient { get { return Client != null; } }
        public bool IsClientConnected { get { return Client != null && Client.IsConnected; } }
        public bool IsNetworkActive { get { return Server != null || Client != null; } }
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
        [Tooltip("enable messages receiving without connection. (with SendUnconnectedMessage method), default value: false")]
        public bool unconnectedMessagesEnabled;
        [Tooltip("enable nat punch messages, default value: false")]
        public bool natPunchEnabled;
        [Tooltip("library logic update (and send) period in milliseconds, default value: 15 msec. For games you can use 15 msec(66 ticks per second)")]
        public int updateTime = 15;
        [Tooltip("Interval for latency detection and checking connection, default value: 1000 msec.")]
        public int pingInterval = 1000;
        [Tooltip("if client or server doesn't receive any packet from remote peer during this time then connection will be closed (including library internal keepalive packets), default value: 5000 msec.")]
        public int disconnectTimeout = 5000;
        [Tooltip("Merge small packets into one before sending to reduce outgoing packets count. (May increase a bit outgoing data size), default value: false")]
        public bool mergeEnabled;

        [Header("Network Simulation")]
        [Tooltip("simulate packet loss by dropping random amout of packets. (Works only in DEBUG mode), default value: false")]
        public bool simulatePacketLoss;
        [Tooltip("simulate latency by holding packets for random time. (Works only in DEBUG mode), default value: false")]
        public bool simulateLatency;
        [Tooltip("chance of packet loss when simulation enabled. value in percents, default value: 10(%)")]
        public int simulationPacketLossChance = 10;
        [Tooltip("minimum simulated latency, default value: 30 msec")]
        public int simulationMinLatency = 30;
        [Tooltip("maximum simulated latency, default value: 100 msec")]
        public int simulationMaxLatency = 100;

        [Header("Network Discovery")]
        [Tooltip("Allows receive DiscoveryRequests, default value: false")]
        public bool discoveryEnabled;
        public string discoveryRequestData;
        public string discoveryResponseData;

        [Header("Server Only Configs")]
        public int maxConnections = 4;

        [Header("Client Only Configs")]
        [Tooltip("delay betwen connection attempts, default value: 500 msec")]
        public int reconnectDelay = 500;
        [Tooltip("maximum connection attempts before client stops and call disconnect event, default value: 10")]
        public int maxConnectAttempts = 10;

        protected readonly Dictionary<long, NetPeer> Peers = new Dictionary<long, NetPeer>();

        protected virtual void Awake() { }

        protected virtual void Start() { }

        protected virtual void Update()
        {
            if (IsServer)
                Server.PollEvents();
            if (IsClient)
            {
                Client.PollEvents();
                if (discoveryEnabled)
                    Client.NetManager.SendDiscoveryRequest(StringBytesConverter.ConvertToBytes(discoveryRequestData), networkPort);
            }
        }

        protected virtual void OnDestroy()
        {
            StopHost();
        }

        protected virtual void OnApplicationQuit()
        {
            StopHost();
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

        /// <summary>
        /// Override this function to register messages that calling from clients to do anything at server
        /// </summary>
        protected virtual void RegisterServerMessages() { }

        /// <summary>
        /// Override this function to register messages that calling from server to do anything at clients
        /// </summary>
        protected virtual void RegisterClientMessages() { }

        public virtual bool StartServer()
        {
            return StartServer(false);
        }

        protected virtual bool StartServer(bool isOffline)
        {
            if (Server != null)
                return true;

            var maxConnections = !isOffline ? this.maxConnections : 1;
            Server = new LiteNetLibServer(this, maxConnections, connectKey);
            RegisterServerMessages();
            SetConfigs(Server.NetManager);
            var canStartServer = !isOffline ? Server.Start(networkPort) : Server.Start();
            if (!canStartServer)
            {
                if (LogError) Debug.LogError("[" + name + "] LiteNetLibManager::StartServer cannot start server at port: " + networkPort);
                Server = null;
                return false;
            }
            OnStartServer();
            return true;
        }

        public virtual LiteNetLibClient StartClient()
        {
            return StartClient(networkAddress, networkPort, connectKey);
        }

        public virtual LiteNetLibClient StartClient(string networkAddress, int networkPort)
        {
            return StartClient(networkAddress, networkPort, connectKey);
        }

        public virtual LiteNetLibClient StartClient(string networkAddress, int networkPort, string connectKey)
        {
            if (Client != null)
                return Client;

            this.networkAddress = networkAddress;
            this.networkPort = networkPort;
            this.connectKey = connectKey;
            if (LogDev) Debug.Log("Client connecting to " + networkAddress + ":" + networkPort);
            Client = new LiteNetLibClient(this, connectKey);
            RegisterClientMessages();
            SetConfigs(Client.NetManager);
            Client.Start();
            Client.Connect(networkAddress, networkPort);
            OnStartClient(Client);
            return Client;
        }

        public virtual LiteNetLibClient StartHost(bool isOffline = false)
        {
            OnStartHost();
            if (StartServer(isOffline))
                return ConnectLocalClient();
            return null;
        }

        protected virtual LiteNetLibClient ConnectLocalClient()
        {
            return StartClient("localhost", Server.NetManager.LocalPort, connectKey);
        }

        public void StopHost()
        {
            OnStopHost();

            StopClient();
            StopServer();
        }

        public void StopServer()
        {
            if (Server == null)
                return;

            if (LogInfo) Debug.Log("[" + name + "] LiteNetLibManager::StopServer");
            Server.Stop();
            Server = null;

            OnStopServer();
        }

        public void StopClient()
        {
            if (Client == null)
                return;

            if (LogInfo) Debug.Log("[" + name + "] LiteNetLibManager::StopClient");
            Client.Stop();
            Client = null;

            OnStopClient();
        }

        internal void AddPeer(NetPeer peer)
        {
            if (peer == null)
                return;
            Peers.Add(peer.ConnectId, peer);
        }

        internal bool RemovePeer(NetPeer peer)
        {
            if (peer == null)
                return false;
            return Peers.Remove(peer.ConnectId);
        }

        internal bool TryGetPeer(long connectId, out NetPeer peer)
        {
            return Peers.TryGetValue(connectId, out peer);
        }

        internal IEnumerable<NetPeer> GetPeers()
        {
            return Peers.Values;
        }

        #region Relates components functions
        public void SendPacketToAllPeers(SendOptions options, ushort msgType, System.Action<NetDataWriter> serializer)
        {
            foreach (var peer in Peers.Values)
            {
                LiteNetLibPacketSender.SendPacket(options, peer, msgType, serializer);
            }
        }

        public void SendPacketToAllPeers<T>(SendOptions options, ushort msgType, T messageData) where T : ILiteNetLibMessage
        {
            foreach (var peer in Peers.Values)
            {
                LiteNetLibPacketSender.SendPacket(options, peer, msgType, messageData);
            }
        }

        public void SendPacketToAllPeers(SendOptions options, ushort msgType)
        {
            foreach (var peer in Peers.Values)
            {
                LiteNetLibPacketSender.SendPacket(options, peer, msgType);
            }
        }

        public void RegisterServerMessage(ushort msgType, MessageHandlerDelegate handlerDelegate)
        {
            Server.RegisterMessage(msgType, handlerDelegate);
        }

        public void UnregisterServerMessage(ushort msgType)
        {
            Server.UnregisterMessage(msgType);
        }

        public void RegisterClientMessage(ushort msgType, MessageHandlerDelegate handlerDelegate)
        {
            Client.RegisterMessage(msgType, handlerDelegate);
        }

        public void UnregisterClientMessage(ushort msgType)
        {
            Client.UnregisterMessage(msgType);
        }
        #endregion

        #region Network Events Callbacks
        /// <summary>
        /// This event will be called at server when there are any network error
        /// </summary>
        /// <param name="endPoint"></param>
        /// <param name="socketErrorCode"></param>
        public virtual void OnPeerNetworkError(NetEndPoint endPoint, int socketErrorCode) { }

        /// <summary>
        /// This event will be called at server when any client connected
        /// </summary>
        /// <param name="peer"></param>
        public virtual void OnPeerConnected(NetPeer peer) { }

        /// <summary>
        /// This event will be called at server when any client disconnected
        /// </summary>
        /// <param name="peer"></param>
        /// <param name="disconnectInfo"></param>
        public virtual void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) { }

        /// <summary>
        /// This event will be called at server when received discovery request from client
        /// </summary>
        /// <param name="endPoint"></param>
        /// <param name="data"></param>
        public virtual void OnServerReceivedDiscoveryRequest(NetEndPoint endPoint, string data) { }

        /// <summary>
        /// This event will be called at client when there are any network error
        /// </summary>
        /// <param name="endPoint"></param>
        /// <param name="socketErrorCode"></param>
        public virtual void OnClientNetworkError(NetEndPoint endPoint, int socketErrorCode) { }

        /// <summary>
        /// This event will be called at client when connected to server
        /// </summary>
        /// <param name="peer"></param>
        public virtual void OnClientConnected(NetPeer peer) { }

        /// <summary>
        /// This event will be called at client when disconnected from server
        /// </summary>
        /// <param name="peer"></param>
        public virtual void OnClientDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) { }

        /// <summary>
        /// This event will be called at server when received discovery response from server
        /// </summary>
        /// <param name="endPoint"></param>
        /// <param name="data"></param>
        public virtual void OnClientReceivedDiscoveryResponse(NetEndPoint endPoint, string data) { }
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
