﻿using Cysharp.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;

namespace LiteNetLibManager
{
    public class LiteNetLibClient : TransportHandler
    {
        public LiteNetLibManager Manager { get; protected set; }
        public override string LogTag { get { return (Manager == null ? "(No Manager)" : Manager.LogTag) + "->LiteNetLibClient"; } }
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
            if (isNetworkActive = Transport.StartClient(address, port))
            {
                OnStartClient();
                return true;
            }
            return false;
        }

        protected virtual void OnStartClient() { }

        public void StopClient()
        {
            Transport.StopClient();
            isNetworkActive = false;
            OnStopClient();
        }

        protected virtual void OnStopClient() { }

        public virtual void OnClientReceive(TransportEventData eventData)
        {
            switch (eventData.type)
            {
                case ENetworkEvent.ConnectEvent:
                    if (Manager.LogInfo) Logging.Log(LogTag, "OnClientConnected");
                    Manager.OnClientConnected();
                    break;
                case ENetworkEvent.DataEvent:
                    ReadPacket(-1, eventData.reader);
                    break;
                case ENetworkEvent.DisconnectEvent:
                    if (Manager.LogInfo) Logging.Log(LogTag, "OnClientDisconnected peer. disconnectInfo.Reason: " + eventData.disconnectInfo.Reason);
                    Manager.StopClient();
                    Manager.OnClientDisconnected(eventData.disconnectInfo);
                    break;
                case ENetworkEvent.ErrorEvent:
                    if (Manager.LogError) Logging.LogError(LogTag, "OnClientNetworkError endPoint: " + eventData.endPoint + " socketErrorCode " + eventData.socketError + " errorMessage " + eventData.errorMessage);
                    Manager.OnClientNetworkError(eventData.endPoint, eventData.socketError);
                    break;
            }
        }

        public override void SendMessage(long connectionId, byte dataChannel, DeliveryMethod deliveryMethod, byte[] data)
        {
            Transport.ClientSend(dataChannel, deliveryMethod, data);
        }

        public void SendMessage(byte dataChannel, DeliveryMethod deliveryMethod, byte[] data)
        {
            SendMessage(dataChannel, deliveryMethod, data);
        }

        public void SendPacket(byte dataChannel, DeliveryMethod deliveryMethod, ushort msgType, SerializerDelegate serializer)
        {
            WritePacket(writer, msgType, serializer);
            // Send packet to server, so connection id will not being used
            SendMessage(dataChannel, deliveryMethod, writer.Data);
        }

        public bool SendRequest<TRequest>(ushort requestType, TRequest request, ResponseDelegate<INetSerializable> responseDelegate = null, int millisecondsTimeout = 30000, SerializerDelegate extraSerializer = null)
            where TRequest : INetSerializable, new()
        {
            if (!CreateAndWriteRequest(writer, requestType, request, responseDelegate, millisecondsTimeout, extraSerializer))
                return false;
            // Send request to server, so connection id will not being used
            SendMessage(0, DeliveryMethod.ReliableUnordered, writer.Data);
            return true;
        }

        public async UniTask<AsyncResponseData<TResponse>> SendRequestAsync<TRequest, TResponse>(ushort requestType, TRequest request, int millisecondsTimeout = 30000, SerializerDelegate extraSerializer = null)
            where TRequest : INetSerializable, new()
            where TResponse : INetSerializable, new()
        {
            bool done = false;
            AsyncResponseData<TResponse> responseData = default;
            // Create request
            CreateAndWriteRequest(writer, requestType, request, (requestHandler, responseCode, response) =>
            {
                if (!(response is TResponse))
                    response = default(TResponse);
                responseData = new AsyncResponseData<TResponse>(requestHandler, responseCode, (TResponse)response);
                done = true;
            }, millisecondsTimeout, extraSerializer);
            // Send request to server, so connection id will not being used
            SendMessage(0, DeliveryMethod.ReliableUnordered, writer.Data);
            // Wait for response
            do
            {
                await UniTask.Yield();
            } while (!done);
            // Return response data
            return responseData;
        }
    }
}
