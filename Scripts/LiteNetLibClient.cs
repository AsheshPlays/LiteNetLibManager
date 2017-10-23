﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;

public class LiteNetLibClient : INetEventListener
{
    public LiteNetLibManager manager { get; protected set; }
    public NetManager netManager { get; protected set; }
    public LiteNetLibClient(LiteNetLibManager manager, string connectKey)
    {
        this.manager = manager;
        netManager = new NetManager(this, connectKey);
    }

    public void OnNetworkError(NetEndPoint endPoint, int socketErrorCode)
    {
        if (manager.writeLog) Debug.LogError("[" + manager.name + "] LiteNetLibClient::OnNetworkError endPoint: " + endPoint + " socketErrorCode " + socketErrorCode);
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
    }

    public void OnNetworkReceive(NetPeer peer, NetDataReader reader)
    {
    }

    public void OnNetworkReceiveUnconnected(NetEndPoint remoteEndPoint, NetDataReader reader, UnconnectedMessageType messageType)
    {
    }

    public void OnPeerConnected(NetPeer peer)
    {
        if (manager.writeLog) Debug.LogError("[" + manager.name + "] LiteNetLibClient::OnPeerConnected peer.ConnectId: " + peer.ConnectId);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (manager.writeLog) Debug.LogError("[" + manager.name + "] LiteNetLibClient::OnPeerDisconnected peer.ConnectId: " + peer.ConnectId + " disconnectInfo.Reason: " + disconnectInfo.Reason);
    }
}
