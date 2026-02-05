// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;

namespace Gsplat.Examples
{
    /// <summary>
    /// Simple example script showing basic PLY loading functionality
    /// </summary>
    public class SimpleGsplatLoader : MonoBehaviour
    {
        [Header("Load Settings")]
        [Tooltip("PLY file name in StreamingAssets folder")]
        public string plyFileName = "example.ply";
        
        [Tooltip("Load automatically on Start")]
        public bool loadOnStart = true;

        private GsplatRuntimeManager runtimeManager;

        void Start()
        {
            // Setup runtime manager
            runtimeManager = GetComponent<GsplatRuntimeManager>();
            if (runtimeManager == null)
            {
                runtimeManager = gameObject.AddComponent<GsplatRuntimeManager>();
            }

            // Setup callbacks
            runtimeManager.OnGsplatLoaded.AddListener(OnLoaded);
            runtimeManager.OnLoadError.AddListener(OnError);

            if (loadOnStart)
            {
                LoadPly();
            }
        }

        [ContextMenu("Load PLY")]
        public void LoadPly()
        {
            if (string.IsNullOrEmpty(plyFileName))
            {
                Debug.LogError("No PLY filename specified!");
                return;
            }

            runtimeManager.LoadPlyFromStreamingAssets(plyFileName);
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            runtimeManager.ClearAsset();
        }

        void OnLoaded(GsplatAsset asset)
        {
            Debug.Log($"Successfully loaded PLY with {asset.SplatCount} splats!");
        }

        void OnError(string error)
        {
            Debug.LogError($"Failed to load PLY: {error}");
        }

        void OnDestroy()
        {
            if (runtimeManager != null)
            {
                runtimeManager.OnGsplatLoaded.RemoveListener(OnLoaded);
                runtimeManager.OnLoadError.RemoveListener(OnError);
            }
        }
    }
}
