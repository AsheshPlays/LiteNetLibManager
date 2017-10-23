﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;

public class LiteNetLibManager : MonoBehaviour
{
    public LiteNetLibClient client { get; protected set; }
    public LiteNetLibServer server { get; protected set; }
    public bool isNetworkActive { get; protected set; }

    [Header("Client & Server Configs")]
    [SerializeField]
    private string connectKey = "SampleConnectKey";
    [SerializeField]
    private string networkAddress = "localhost";
    [SerializeField]
    private int networkPort = 7770;
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

    [Header("Server Only Configs")]
    [SerializeField]
    private int maxConnections = 4;

    [Header("Client Only Configs")]
    [SerializeField, Tooltip("delay betwen connection attempts, default value: 500 msec")]
    private int reconnectDelay = 500;
    [SerializeField, Tooltip("maximum connection attempts before client stops and call disconnect event, default value: 10")]
    private int maxConnectAttempts = 10;

    [Header("Logging")]
    public bool writeLog;

    public bool IsServer
    {
        get { return server != null; }
    }

    public bool IsClient
    {
        get { return client != null; }
    }

    protected virtual void Update()
    {
        if (IsServer)
            server.netManager.PollEvents();
        if (IsClient)
            client.netManager.PollEvents();
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
        if (server != null)
            return true;

        OnStartServer();
        server = new LiteNetLibServer(this, maxConnections, connectKey);
        RegisterServerMessages();
        SetConfigs(server.netManager);
        server.netManager.Start(networkPort);

        return isNetworkActive;
    }

    public virtual LiteNetLibClient StartClient()
    {
        if (client != null)
            return client;

        client = new LiteNetLibClient(this, connectKey);
        RegisterClientMessages();
        SetConfigs(client.netManager);
        client.netManager.Start();
        client.netManager.Connect(networkAddress, networkPort);
        isNetworkActive = true;
        OnStartClient(client);
        return client;
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
        if (writeLog) Debug.Log("[" + name + "] NetworkManagerSimple StartHost port:" + networkPort);
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
        isNetworkActive = false;

        if (server == null)
            return;

        OnStopServer();

        if (writeLog) Debug.Log("[" + name + "] NetworkManagerSimple StopServer");
        server.netManager.Stop();
        server = null;
    }

    public void StopClient()
    {
        isNetworkActive = false;

        if (client == null)
            return;

        OnStopClient();

        if (writeLog) Debug.Log("[" + name + "] NetworkManagerSimple StopClient");
        client.netManager.Stop();
        client = null;
    }

    // ----------------------------- Message Registration --------------------------------

    protected virtual void RegisterServerMessages()
    {
    }

    protected virtual void RegisterClientMessages()
    {
    }

    //------------------------------ Start & Stop callbacks -----------------------------------

    // Since there are multiple versions of StartServer, StartClient and StartHost, to reliably customize
    // their functionality, users would need override all the versions. Instead these callbacks are invoked
    // from all versions, so users only need to implement this one case.
    /// <summary>
    /// This hook is invoked when a host is started.
    /// </summary>
    public virtual void OnStartHost()
    {
        if (writeLog) Debug.Log("[" + name + "] NetworkManagerSimple:OnStartHost");
    }

    /// <summary>
    /// This hook is invoked when a server is started - including when a host is started.
    /// </summary>
    public virtual void OnStartServer()
    {
        if (writeLog) Debug.Log("[" + name + "] NetworkManagerSimple:OnStartServer");
    }

    /// <summary>
    /// This is a hook that is invoked when the client is started.
    /// </summary>
    /// <param name="client"></param>
    public virtual void OnStartClient(LiteNetLibClient client)
    {
        if (writeLog) Debug.Log("[" + name + "] NetworkManagerSimple:OnStartClient");
    }

    /// <summary>
    /// This hook is called when a server is stopped - including when a host is stopped.
    /// </summary>
    public virtual void OnStopServer()
    {
        if (writeLog) Debug.Log("[" + name + "] NetworkManagerSimple:OnStopServer");
    }

    /// <summary>
    /// This hook is called when a client is stopped.
    /// </summary>
    public virtual void OnStopClient()
    {
        if (writeLog) Debug.Log("[" + name + "] NetworkManagerSimple:OnStopClient");
    }

    /// <summary>
    /// This hook is called when a host is stopped.
    /// </summary>
    public virtual void OnStopHost()
    {
        if (writeLog) Debug.Log("[" + name + "] NetworkManagerSimple:OnStopHost");
    }
}
