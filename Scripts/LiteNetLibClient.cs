﻿using Cysharp.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
using System;

namespace LiteNetLibManager
{
    public class LiteNetLibClient : TransportHandler
    {
        public LiteNetLibManager Manager { get; protected set; }
        public override string LogTag { get { return $"{(Manager == null ? "(No Manager)" : Manager.LogTag)}->LiteNetLibClient"; } }
        private bool isNetworkActive;
        public override bool IsNetworkActive { get { return isNetworkActive; } }

        public LiteNetLibClient(LiteNetLibManager manager) : base(manager.Transport)
        {
            Manager = manager;
        }

        public LiteNetLibClient(ITransport transport) : base(transport)
        {

        }

        public void Update()
        {
            if (!IsNetworkActive)
                return;
            while (Transport.ClientReceive(out tempEventData))
            {
                OnClientReceive(tempEventData);
            }
        }

        public bool StartClient(string address, int port)
        {
            if (IsNetworkActive)
            {
                Logging.LogWarning(LogTag, "Cannot Start Client, network already active");
                return false;
            }
            // Clear and reset request Id
            requestCallbacks.Clear();
            nextRequestId = 1;
            return isNetworkActive = Transport.StartClient(address, port);
        }

        public void StopClient()
        {
            Transport.StopClient();
            isNetworkActive = false;
        }

        public virtual void OnClientReceive(TransportEventData eventData)
        {
            switch (eventData.type)
            {
                case ENetworkEvent.ConnectEvent:
                    if (Manager.LogInfo) Logging.Log(LogTag, "OnPeerConnected");
                    Manager.OnClientConnected();
                    break;
                case ENetworkEvent.DataEvent:
                    ReadPacket(-1, eventData.reader);
                    break;
                case ENetworkEvent.DisconnectEvent:
                    if (Manager.LogInfo) Logging.Log(LogTag, "OnPeerDisconnected peer. disconnectInfo.Reason: " + eventData.disconnectInfo.Reason);
                    Manager.StopClient();
                    Manager.OnClientDisconnected(eventData.disconnectInfo);
                    break;
                case ENetworkEvent.ErrorEvent:
                    if (Manager.LogError) Logging.LogError(LogTag, "OnNetworkError endPoint: " + eventData.endPoint + " socketErrorCode " + eventData.socketError);
                    Manager.OnClientNetworkError(eventData.endPoint, eventData.socketError);
                    break;
            }
        }

        protected override void SendMessage(long connectionId, DeliveryMethod deliveryMethod, NetDataWriter writer)
        {
            Transport.ClientSend(deliveryMethod, writer);
        }

        public void SendPacket(DeliveryMethod deliveryMethod, ushort msgType, SerializerDelegate serializer)
        {
            // Send packet to server, so connection id will not being used
            WritePacket(writer, msgType, serializer);
            SendMessage(-1, deliveryMethod, writer);
        }

        public bool SendRequest<TRequest>(ushort requestType, TRequest request, ResponseDelegate<INetSerializable> responseDelegate = null, int millisecondsTimeout = 30000, SerializerDelegate extraSerializer = null)
            where TRequest : INetSerializable
        {
            // Send request to server, so connection id will not being used
            if (!CreateAndWriteRequest(writer, requestType, request, responseDelegate, millisecondsTimeout, extraSerializer))
                return false;
            SendMessage(-1, DeliveryMethod.ReliableOrdered, writer);
            return true;
        }
    }
}
