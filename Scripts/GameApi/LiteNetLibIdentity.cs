﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using LiteNetLib.Utils;
using UnityEngine.Profiling;
using LiteNetLib;
using UnityEngine.Rendering;

namespace LiteNetLibManager
{
    [DisallowMultipleComponent]
    public sealed class LiteNetLibIdentity : MonoBehaviour
    {
        public static uint HighestObjectId { get; private set; }
        [LiteNetLibReadOnly, SerializeField]
        private string assetId;
        [LiteNetLibReadOnly, SerializeField]
        private uint objectId;

        /// <summary>
        /// This will be true when identity was spawned by manager
        /// </summary>
        public bool IsSpawned { get; private set; }
        /// <summary>
        /// This will be true when identity was requested to destroy
        /// </summary>
        public bool IsDestroyed { get; private set; }
        /// <summary>
        /// This will be true when identity setup
        /// </summary>
        public bool IsSetupBehaviours { get; private set; }
        /// <summary>
        /// Array of all behaviours
        /// </summary>
        public LiteNetLibBehaviour[] Behaviours { get; private set; }
        /// <summary>
        /// This will be true if it can get visible checker component when setup
        /// </summary>
        public bool HasVisibleChecker { get; private set; }
        /// <summary>
        /// Visible checker component which attached to this identity
        /// </summary>
        public BaseLiteNetLibVisibleChecker VisibleChecker { get; private set; }
        /// <summary>
        /// List of sync fields from all behaviours (include children behaviours)
        /// </summary>
        internal readonly List<LiteNetLibSyncField> SyncFields = new List<LiteNetLibSyncField>();
        /// <summary>
        /// List of net functions from all behaviours (include children behaviours)
        /// </summary>
        internal readonly List<LiteNetLibFunction> NetFunctions = new List<LiteNetLibFunction>();
        /// <summary>
        /// List of sync lists from all behaviours (include children behaviours)
        /// </summary>
        internal readonly List<LiteNetLibSyncList> SyncLists = new List<LiteNetLibSyncList>();
        /// <summary>
        /// List of sync behaviours
        /// </summary>
        internal readonly List<LiteNetLibBehaviour> SyncBehaviours = new List<LiteNetLibBehaviour>();
        /// <summary>
        /// List of networked objects which subscribed by this identity
        /// </summary>
        internal readonly HashSet<uint> Subscribings = new HashSet<uint>();
        /// <summary>
        /// List of players which subscribe this identity
        /// </summary>
        internal readonly HashSet<long> Subscribers = new HashSet<long>();

        public string AssetId { get { return assetId; } }
        public int HashAssetId
        {
            get
            {
                unchecked
                {
                    int hash1 = 5381;
                    int hash2 = hash1;

                    for (int i = 0; i < AssetId.Length && AssetId[i] != '\0'; i += 2)
                    {
                        hash1 = ((hash1 << 5) + hash1) ^ AssetId[i];
                        if (i == AssetId.Length - 1 || AssetId[i + 1] == '\0')
                            break;
                        hash2 = ((hash2 << 5) + hash2) ^ AssetId[i + 1];
                    }

                    return hash1 + (hash2 * 1566083941);
                }
            }
        }
        public uint ObjectId { get { return objectId; } }
        public long ConnectionId { get; internal set; } = -1;
        public LiteNetLibGameManager Manager { get; internal set; }

        private string logTag;
        public string LogTag
        {
            get
            {
                if (string.IsNullOrEmpty(logTag))
                    logTag = $"{Manager.LogTag}->{name}({GetType().Name})";
                return logTag;
            }
        }

        public LiteNetLibPlayer Player
        {
            get
            {
                LiteNetLibPlayer foundPlayer;
                if (Manager == null || !Manager.TryGetPlayer(ConnectionId, out foundPlayer))
                    return null;
                return foundPlayer;
            }
        }

        public bool IsServer
        {
            get { return Manager != null && Manager.IsServer; }
        }

        public bool IsClient
        {
            get { return Manager != null && Manager.IsClient; }
        }

        public bool IsOwnerClient
        {
            get { return IsClient && Manager.ClientConnectionId >= 0 && ConnectionId >= 0 && Manager.ClientConnectionId == ConnectionId; }
        }

        public bool IsSceneObject
        {
            get; private set;
        }

        internal void NetworkUpdate(float deltaTime)
        {
            if (Manager == null)
                return;

            Profiler.BeginSample("LiteNetLibIdentity - Network Update");
            int loopCounter;
            for (loopCounter = 0; loopCounter < SyncFields.Count; ++loopCounter)
            {
                SyncFields[loopCounter].NetworkUpdate(deltaTime);
            }

            for (loopCounter = 0; loopCounter < SyncBehaviours.Count; ++loopCounter)
            {
                SyncBehaviours[loopCounter].NetworkUpdate(deltaTime);
            }
            Profiler.EndSample();
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
#if UNITY_2018_3_OR_NEWER
            return PrefabUtility.IsPartOfPrefabAsset(gameObject);
#else
            PrefabType prefabType = PrefabUtility.GetPrefabType(gameObject);
            if (prefabType == PrefabType.Prefab)
                return true;
            return false;
#endif
        }

        private bool ThisIsASceneObjectWithThatReferencesPrefabAsset(out GameObject prefab)
        {
            prefab = null;
#if UNITY_2018_3_OR_NEWER
            if (!PrefabUtility.IsPartOfNonAssetPrefabInstance(gameObject))
                return false;
#else
            PrefabType prefabType = PrefabUtility.GetPrefabType(gameObject);
            if (prefabType == PrefabType.None)
                return false;
#endif
#if UNITY_2018_2_OR_NEWER
            prefab = (GameObject)PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
#else
            prefab = (GameObject)PrefabUtility.GetPrefabParent(gameObject);
#endif
            if (prefab == null)
            {
                Logging.LogError(LogTag, $"Failed to find prefab parent for scene object: {gameObject.name}.");
                return false;
            }
            return true;
        }

        private void SetupIDs()
        {
            string oldAssetId = assetId;
            uint oldObjectId = objectId;
            GameObject prefab;
            if (ThisIsAPrefab())
            {
                // This is a prefab, can create prefab while playing so it will still assign asset ID and reset object ID
                AssignAssetID(gameObject);
                objectId = 0;
            }
            else if (ThisIsASceneObjectWithThatReferencesPrefabAsset(out prefab))
            {
                if (!Application.isPlaying)
                {
                    // This is a scene object with prefab link
                    AssignAssetID(prefab);
                    if (gameObject.scene == SceneManager.GetActiveScene())
                    {
                        // Assign object id if it is in scene
                        AssignSceneObjectId();
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                    }
                    else
                    {
                        // Difference working scene?, clear object Id
                        objectId = 0;
                    }
                }
            }
            else
            {
                if (!Application.isPlaying)
                {
                    // This is a pure scene object (Not a prefab)
                    assetId = string.Empty;
                    if (gameObject.scene == SceneManager.GetActiveScene())
                    {
                        // Assign object id if it is in scene
                        AssignSceneObjectId();
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                    }
                    else
                    {
                        // Difference working scene?, clear object Id
                        objectId = 0;
                    }
                }
            }
            // Do not mark dirty while playing
            if (!Application.isPlaying && (oldAssetId != assetId || oldObjectId != objectId))
                EditorUtility.SetDirty(this);
        }
#endif
        #endregion

        #region SyncField Functions
        internal LiteNetLibSyncField ProcessSyncField(LiteNetLibElementInfo info, NetDataReader reader, bool isInitial)
        {
            return ProcessSyncField(GetSyncField(info), reader, isInitial);
        }

        internal LiteNetLibSyncField ProcessSyncField(LiteNetLibSyncField syncField, NetDataReader reader, bool isInitial)
        {
            if (syncField == null)
                return null;
            syncField.Deserialize(reader, isInitial);
            return syncField;
        }

        internal LiteNetLibSyncField GetSyncField(LiteNetLibElementInfo info)
        {
            if (info.objectId != ObjectId)
                return null;
            if (info.elementId >= 0 && info.elementId < SyncFields.Count)
                return SyncFields[info.elementId];
            if (Manager.LogError)
                Logging.LogError(LogTag, $"Cannot find sync field: {info.elementId}.");
            return null;
        }
        #endregion

        #region NetFunction Functions
        internal LiteNetLibFunction ProcessNetFunction(LiteNetLibElementInfo info, NetDataReader reader, bool hookCallback)
        {
            return ProcessNetFunction(GetNetFunction(info), reader, hookCallback);
        }

        internal LiteNetLibFunction ProcessNetFunction(LiteNetLibFunction netFunction, NetDataReader reader, bool hookCallback)
        {
            if (netFunction == null)
                return null;
            netFunction.DeserializeParameters(reader);
            if (hookCallback)
                netFunction.HookCallback();
            return netFunction;
        }

        internal LiteNetLibFunction GetNetFunction(LiteNetLibElementInfo info)
        {
            if (info.objectId != ObjectId)
                return null;
            if (info.elementId >= 0 && info.elementId < NetFunctions.Count)
                return NetFunctions[info.elementId];
            if (Manager.LogError)
                Logging.LogError(LogTag, $"Cannot find net function: {info.elementId}.");
            return null;
        }
        #endregion

        #region SyncList Functions
        internal LiteNetLibSyncList ProcessSyncList(LiteNetLibElementInfo info, NetDataReader reader)
        {
            return ProcessSyncList(GetSyncList(info), reader);
        }

        internal LiteNetLibSyncList ProcessSyncList(LiteNetLibSyncList syncList, NetDataReader reader)
        {
            if (syncList == null)
                return null;
            syncList.DeserializeOperation(reader);
            return syncList;
        }

        internal LiteNetLibSyncList GetSyncList(LiteNetLibElementInfo info)
        {
            if (info.objectId != ObjectId)
                return null;
            if (info.elementId >= 0 && info.elementId < SyncLists.Count)
                return SyncLists[info.elementId];
            if (Manager.LogError)
                Logging.LogError(LogTag, $"Cannot find sync list: {info.elementId}.");
            return null;
        }
        #endregion

        internal LiteNetLibBehaviour ProcessSyncBehaviour(byte behaviourIndex, NetDataReader reader)
        {
            if (behaviourIndex >= Behaviours.Length)
                return null;
            LiteNetLibBehaviour behaviour = Behaviours[behaviourIndex];
            behaviour.Deserialize(reader);
            return behaviour;
        }

        internal bool TryGetBehaviour<T>(byte behaviourIndex, out T behaviour)
            where T : LiteNetLibBehaviour
        {
            behaviour = null;
            if (behaviourIndex >= Behaviours.Length)
                return false;
            behaviour = Behaviours[behaviourIndex] as T;
            return behaviour != null;
        }

        internal void WriteInitialSyncFields(NetDataWriter writer)
        {
            foreach (LiteNetLibSyncField field in SyncFields)
            {
                if (field.doNotSyncInitialDataImmediately)
                    continue;
                field.Serialize(writer);
            }
        }

        internal void ReadInitialSyncFields(NetDataReader reader)
        {
            foreach (LiteNetLibSyncField field in SyncFields)
            {
                if (field.doNotSyncInitialDataImmediately)
                    continue;
                field.Deserialize(reader, true);
            }
        }

        internal void SendInitSyncFields(long connectionId)
        {
            foreach (LiteNetLibSyncField field in SyncFields)
            {
                if (!field.doNotSyncInitialDataImmediately)
                    continue;
                field.SendUpdate(true, connectionId, DeliveryMethod.ReliableOrdered);
            }
        }

        internal void SendInitSyncLists(long connectionId)
        {
            foreach (LiteNetLibSyncList list in SyncLists)
            {
                for (int i = 0; i < list.Count; ++i)
                    list.SendOperation(connectionId, LiteNetLibSyncList.Operation.Insert, i);
            }
        }

        public bool IsSceneObjectExists(uint objectId)
        {
            if (Manager != null)
            {
                // If this is spawned while gameplay, find it by manager assets
                return Manager.Assets.ContainsSceneObject(objectId);
            }
            // If this is now spawned while gameplay, find objects in scene
            LiteNetLibIdentity[] netObjects = FindObjectsOfType<LiteNetLibIdentity>();
            foreach (LiteNetLibIdentity netObject in netObjects)
            {
                if (netObject.objectId == objectId && netObject != this)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Initial Identity, will be called when spawned. If object id == 0, it will generate new object id
        /// </summary>
        /// <param name="objectId"></param>
        /// <param name="connectionId"></param>
        internal void Initial(LiteNetLibGameManager manager, bool isSceneObject, uint objectId = 0, long connectionId = -1)
        {
            this.objectId = objectId;
            Manager = manager;
            ConnectionId = connectionId;
            IsDestroyed = false;
            IsSpawned = true;
            if (objectId > HighestObjectId)
                HighestObjectId = objectId;
            IsSceneObject = isSceneObject;
            if (!IsSceneObject)
                AssignSceneObjectId();

            if (!IsSetupBehaviours)
            {
                // Setup behaviours index, we will use this as reference for network functions
                // NOTE: Maximum network behaviour for a identity is 255 (included children)
                Behaviours = GetComponentsInChildren<LiteNetLibBehaviour>();
                SyncBehaviours.Clear();
                byte loopCounter;
                for (loopCounter = 0; loopCounter < Behaviours.Length; ++loopCounter)
                {
                    Behaviours[loopCounter].Setup(loopCounter);
                    if (Behaviours[loopCounter].CanSyncBehaviour())
                        SyncBehaviours.Add(Behaviours[loopCounter]);
                }
                VisibleChecker = GetComponent<BaseLiteNetLibVisibleChecker>();
                HasVisibleChecker = VisibleChecker != null;
                IsSetupBehaviours = true;
            }

            // If this is host, hide it then will be showned when initialize subscribings
            if (IsServer && IsClient)
                OnServerSubscribingRemoved();

            InitializeSubscribings();
        }

        internal void OnSetOwnerClient(bool isOwnerClient)
        {
            int loopCounter;
            for (loopCounter = 0; loopCounter < Behaviours.Length; ++loopCounter)
            {
                Behaviours[loopCounter].OnSetOwnerClient(isOwnerClient);
            }

            if (IsServer && IsClient && isOwnerClient)
                OnServerSubscribingAdded();
        }

        internal void InitTransform(Vector3 position, Quaternion rotation)
        {
            int loopCounter;
            for (loopCounter = 0; loopCounter < Behaviours.Length; ++loopCounter)
            {
                Behaviours[loopCounter].InitTransform(position, rotation);
            }
        }

        internal void OnStartServer()
        {
            int loopCounter;
            for (loopCounter = 0; loopCounter < Behaviours.Length; ++loopCounter)
            {
                Behaviours[loopCounter].OnStartServer();
            }
        }

        internal void OnStartClient()
        {
            int loopCounter;
            for (loopCounter = 0; loopCounter < Behaviours.Length; ++loopCounter)
            {
                Behaviours[loopCounter].OnStartClient();
            }
        }

        internal void OnStartOwnerClient()
        {
            int loopCounter;
            for (loopCounter = 0; loopCounter < Behaviours.Length; ++loopCounter)
            {
                Behaviours[loopCounter].OnStartOwnerClient();
            }
        }

        internal void AssignSceneObjectId()
        {
            if (objectId == 0 || IsSceneObjectExists(objectId))
                objectId = GetNewObjectId();
        }

        internal static void ResetObjectId()
        {
            HighestObjectId = 0;
        }

        internal static uint GetNewObjectId()
        {
            if (HighestObjectId == 0)
            {
                uint result = HighestObjectId;
                LiteNetLibIdentity[] netObjects = FindObjectsOfType<LiteNetLibIdentity>();
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
#if UNITY_EDITOR
                // Do not mark dirty while playing
                if (!Application.isPlaying)
                    EditorUtility.SetDirty(netObject);
#endif
            }
#if UNITY_EDITOR
            // Do not mark dirty while playing
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
#endif
        }

        public bool AddSubscriber(long connectionId)
        {
            return Subscribers.Add(connectionId);
        }

        public bool RemoveSubscriber(long connectionId)
        {
            return Subscribers.Remove(connectionId);
        }

        public bool HasSubscriber(long connectionId)
        {
            return Subscribers.Contains(connectionId);
        }

        public int CountSubscribers()
        {
            return Subscribers.Count;
        }

        public bool IsSubscribedOrOwning(long connectionId)
        {
            return connectionId == ConnectionId || Subscribers.Contains(connectionId);
        }

        private void InitializeSubscribings()
        {
            if (!IsServer || ConnectionId < 0)
            {
                // This is not player's networked object
                return;
            }
            // Always add controlled network object to subscribe it
            Subscribings.Add(ObjectId);
            if (!HasVisibleChecker)
            {
                // Subscribes all spawned objects
                foreach (uint objectId in Manager.Assets.SpawnedObjects.Keys)
                {
                    Subscribings.Add(objectId);
                }
            }
            else
            {
                // Find objects to subscribes by visible checker
                foreach (uint objectId in VisibleChecker.GetInitializeSubscribings())
                {
                    Subscribings.Add(objectId);
                }
            }
            foreach (uint objectId in Subscribings)
            {
                Player.Subscribe(objectId);
            }
        }

        public void UpdateSubscribings(HashSet<uint> newSubscribings)
        {
            if (!IsServer || ConnectionId < 0)
            {
                // This is not player's networked object
                return;
            }
            // Always add controlled network object to subscribe it
            newSubscribings.Add(ObjectId);
            foreach (uint oldSubscribing in Subscribings)
            {
                if (!newSubscribings.Contains(oldSubscribing))
                {
                    Player.Unsubscribe(oldSubscribing, true);
                    if (Manager.LogDebug)
                        Logging.Log(LogTag, $"Player: {ConnectionId} unsubscribe object ID: {oldSubscribing}.");
                }
            }
            Subscribings.Clear();
            foreach (uint newSubscribing in newSubscribings)
            {
                Subscribings.Add(newSubscribing);
                Player.Subscribe(newSubscribing);
                if (Manager.LogDebug)
                    Logging.Log(LogTag, $"Player: {ConnectionId} subscribe object ID: {newSubscribing}.");
            }
        }

        public void OnServerSubscribingAdded()
        {
            int loopCounter;
            for (loopCounter = 0; loopCounter < Behaviours.Length; ++loopCounter)
            {
                Behaviours[loopCounter].OnServerSubscribingAdded();
            }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; ++i)
            {
                renderers[i].forceRenderingOff = false;
            }
        }

        public void OnServerSubscribingRemoved()
        {
            int loopCounter;
            for (loopCounter = 0; loopCounter < Behaviours.Length; ++loopCounter)
            {
                Behaviours[loopCounter].OnServerSubscribingRemoved();
            }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; ++i)
            {
                renderers[i].forceRenderingOff = true;
            }
        }

        public void SetOwnerClient(long connectionId)
        {
            if (!IsServer)
                return;

            Manager.Assets.SetObjectOwner(ObjectId, connectionId);
        }

        public void NetworkDestroy()
        {
            if (!IsServer)
                return;

            NetworkDestroyFunction();
        }

        public void NetworkDestroy(float delay)
        {
            if (!IsServer)
                return;

            Invoke(nameof(NetworkDestroyFunction), delay);
        }

        private void NetworkDestroyFunction()
        {
            if (!IsDestroyed)
            {
                Manager.Assets.NetworkDestroy(ObjectId, DestroyObjectReasons.RequestedToDestroy);
                Subscribings.Clear();
                IsDestroyed = true;
                IsSpawned = false;
            }
        }

        public void OnNetworkDestroy(byte reasons)
        {
            int loopCounter;
            for (loopCounter = 0; loopCounter < Behaviours.Length; ++loopCounter)
            {
                Behaviours[loopCounter].OnNetworkDestroy(reasons);
            }
        }
    }
}
