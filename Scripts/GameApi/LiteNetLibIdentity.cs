﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using LiteNetLib;
using LiteNetLib.Utils;

namespace LiteNetLibHighLevel
{
    [DisallowMultipleComponent]
    public sealed class LiteNetLibIdentity : MonoBehaviour
    {
        public static uint HighestObjectId { get; private set; }
        [ReadOnly, SerializeField]
        private string assetId;
        [ReadOnly, SerializeField]
        private uint objectId;
        [ReadOnly, SerializeField]
        private long connectId;
        [ReadOnly, SerializeField]
        private LiteNetLibGameManager manager;
        private readonly List<LiteNetLibBehaviour> behaviours = new List<LiteNetLibBehaviour>();
        private readonly Dictionary<long, LiteNetLibPlayer> subscribers = new Dictionary<long, LiteNetLibPlayer>();
        public string AssetId { get { return assetId; } }
        public uint ObjectId { get { return objectId; } }
        public long ConnectId { get { return connectId; } }
        public LiteNetLibGameManager Manager { get { return manager; } }
        public bool IsServer
        {
            get {
                if (Manager == null)
                    Debug.LogError("Manager is empty");
                return Manager != null && Manager.IsServer; }
        }

        public bool IsClient
        {
            get { return Manager != null && Manager.IsClient; }
        }

        public bool IsLocalClient
        {
            get { return Manager != null && ConnectId == Manager.Client.Peer.ConnectId; }
        }

        internal void NetworkUpdate()
        {
            foreach (var behaviour in behaviours)
            {
                behaviour.NetworkUpdate();
            }
        }

#region IDs generate in Editor
#if UNITY_EDITOR
        private void OnValidate()
        {
            SetupIDs();
        }

        [ContextMenu("Reorder Scene Object Id")]
        public void ContextMenuReorderSceneObjectId()
        {
            ReorderSceneObjectId();
        }

        private void AssignAssetID(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            assetId = AssetDatabase.AssetPathToGUID(path);
        }

        private bool ThisIsAPrefab()
        {
            PrefabType prefabType = PrefabUtility.GetPrefabType(gameObject);
            if (prefabType == PrefabType.Prefab)
                return true;
            return false;
        }

        private bool ThisIsASceneObjectWithPrefabParent(out GameObject prefab)
        {
            prefab = null;
            PrefabType prefabType = PrefabUtility.GetPrefabType(gameObject);
            if (prefabType == PrefabType.None)
                return false;
            prefab = (GameObject)PrefabUtility.GetPrefabParent(gameObject);
            if (prefab == null)
            {
                Debug.LogError("Failed to find prefab parent for scene object [name:" + gameObject.name + "]");
                return false;
            }
            return true;
        }

        private void SetupIDs()
        {
            GameObject prefab;
            if (ThisIsAPrefab())
            {
                // This is a prefab
                AssignAssetID(gameObject);
                objectId = 0;
            }
            else if (ThisIsASceneObjectWithPrefabParent(out prefab))
            {
                // This is a scene object with prefab link
                AssignAssetID(prefab);
                ValidateObjectId();
            }
            else
            {
                // This is a pure scene object (Not a prefab)
                assetId = string.Empty;
                ValidateObjectId();
            }
        }
#endif
#endregion

        public LiteNetLibSyncField ProcessSyncField(LiteNetLibElementInfo info, NetDataReader reader)
        {
            if (info.objectId != ObjectId)
                return null;
            if (info.behaviourIndex < 0 || info.behaviourIndex >= behaviours.Count)
                return null;
            return behaviours[info.behaviourIndex].ProcessSyncField(info, reader);
        }

        public LiteNetLibFunction ProcessNetFunction(LiteNetLibElementInfo info, NetDataReader reader, bool hookCallback)
        {
            if (info.objectId != ObjectId)
                return null;
            if (info.behaviourIndex < 0 || info.behaviourIndex >= behaviours.Count)
                return null;
            return behaviours[info.behaviourIndex].ProcessNetFunction(info, reader, hookCallback);
        }

        public LiteNetLibSyncList ProcessSyncList(LiteNetLibElementInfo info, NetDataReader reader)
        {
            if (info.objectId != ObjectId)
                return null;
            if (info.behaviourIndex < 0 || info.behaviourIndex >= behaviours.Count)
                return null;
            return behaviours[info.behaviourIndex].ProcessSyncList(info, reader);
        }

        public void SendInitSyncFields()
        {
            foreach (var behaviour in behaviours)
            {
                behaviour.SendInitSyncFields();
            }
        }

        public void SendInitSyncFields(NetPeer peer)
        {
            foreach (var behaviour in behaviours)
            {
                behaviour.SendInitSyncFields(peer);
            }
        }

        public void SendInitSyncLists()
        {
            foreach (var behaviour in behaviours)
            {
                behaviour.SendInitSyncLists();
            }
        }

        public void SendInitSyncLists(NetPeer peer)
        {
            foreach (var behaviour in behaviours)
            {
                behaviour.SendInitSyncLists(peer);
            }
        }

        public bool IsSceneObjectExists(uint objectId)
        {
            LiteNetLibIdentity[] netObjects = FindObjectsOfType<LiteNetLibIdentity>();
            foreach (LiteNetLibIdentity netObject in netObjects)
            {
                if (netObject == this)
                    continue;
                if (netObject.objectId == objectId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Initial Identity, will be called when spawned. If object id == 0, it will generate new object id
        /// </summary>
        /// <param name="manager"></param>
        /// <param name="objectId"></param>
        /// <param name="connectId"></param>
        public void Initial(LiteNetLibGameManager manager, uint objectId = 0, long connectId = 0)
        {
            this.objectId = objectId;
            this.connectId = connectId;
            this.manager = manager;
            if (objectId > HighestObjectId)
                HighestObjectId = objectId;
            ValidateObjectId();

            // Setup behaviours index, we will use this as reference for network functions
            behaviours.Clear();
            var behaviourComponents = GetComponents<LiteNetLibBehaviour>();
            foreach (var behaviour in behaviourComponents)
            {
                behaviour.Setup(behaviours.Count);
                behaviours.Add(behaviour);
            }

            RebuildSubscribers(true);
        }

        private void ValidateObjectId()
        {
            if (objectId == 0 || IsSceneObjectExists(objectId))
                objectId = GetNewObjectId();
        }

        public static void ResetObjectId()
        {
            HighestObjectId = 0;
        }

        public static uint GetNewObjectId()
        {
            LiteNetLibIdentity[] netObjects = FindObjectsOfType<LiteNetLibIdentity>();
            if (HighestObjectId == 0)
            {
                uint result = HighestObjectId;
                foreach (LiteNetLibIdentity netObject in netObjects)
                {
                    if (netObject.objectId > result)
                        result = netObject.objectId;
                }
                HighestObjectId = result;
            }
            ++HighestObjectId;
            return HighestObjectId;
        }

        private static void ReorderSceneObjectId()
        {
            ResetObjectId();
            LiteNetLibIdentity[] netObjects = FindObjectsOfType<LiteNetLibIdentity>();
            foreach (LiteNetLibIdentity netObject in netObjects)
            {
                netObject.objectId = ++HighestObjectId;
            }
        }

        internal void ClearSubscribers()
        {
            var values = subscribers.Values;
            foreach (var subscriber in values)
            {
                subscriber.RemoveSubscribing(this, true);
            }
            subscribers.Clear();
        }

        internal void AddSubscriber(LiteNetLibPlayer subscriber)
        {
            if (subscribers.ContainsKey(subscriber.ConnectId))
            {
                if (Manager.LogDebug)
                    Debug.Log("Subscriber [" + subscriber.ConnectId + "] already added to [" + gameObject + "]");
                return;
            }

            subscribers[subscriber.ConnectId] = subscriber;
            subscriber.AddSubscribing(this);
        }

        internal void RemoveSubscriber(LiteNetLibPlayer subscriber, bool removePlayerSubscribing = true)
        {
            subscribers.Remove(subscriber.ConnectId);
            if (removePlayerSubscribing)
                subscriber.RemoveSubscribing(this, false);
        }

        public void RebuildSubscribers(bool initialize)
        {
            bool changed = false;
            bool shouldRebuild = false;
            HashSet<LiteNetLibPlayer> newObservers = new HashSet<LiteNetLibPlayer>();
            HashSet<LiteNetLibPlayer> oldObservers = new HashSet<LiteNetLibPlayer>(subscribers.Values);

            var count = behaviours.Count;
            for (int i = 0; i < count; ++i)
            {
                var behaviour = behaviours[i];
                shouldRebuild |= behaviour.OnRebuildSubscribers(newObservers, initialize);
            }

            // If shouldRebuild is FALSE, it's means it does not have to rebuild subscribers
            if (!shouldRebuild)
            {
                // none of the behaviours rebuilt our observers, use built-in rebuild method
                if (initialize)
                {
                    var players = Manager.Players.Values;
                    foreach (var player in players)
                    {
                        if (player == null)
                            continue;
                        AddSubscriber(player);
                    }
                }
                return;
            }

            // Apply changes from rebuild
            foreach (var subscriber in newObservers)
            {
                if (subscriber == null)
                    continue;

                if (!subscriber.IsReady)
                {
                    if (Manager.LogWarn)
                        Debug.Log("Subscriber [" + subscriber.ConnectId + "] is not ready");
                    continue;
                }

                if (initialize || !oldObservers.Contains(subscriber))
                {
                    subscriber.AddSubscribing(this);
                    if (Manager.LogDebug)
                        Debug.Log("Add subscriber [" + subscriber.ConnectId + "] to [" + gameObject + "]");
                    changed = true;
                }
            }

            foreach (var subscriber in oldObservers)
            {
                if (!newObservers.Contains(subscriber))
                {
                    subscriber.RemoveSubscribing(this, false);
                    if (Manager.LogDebug)
                        Debug.Log("Remove subscriber [" + subscriber.ConnectId + "] from [" + gameObject +"]");
                    changed = true;
                }
            }

            if (!changed)
                return;

            subscribers.Clear();
            foreach (var subscriber in newObservers)
                subscribers.Add(subscriber.ConnectId, subscriber);
        }
    }
}