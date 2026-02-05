// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Gsplat
{
    /// <summary>
    /// Runtime manager for loading and displaying PLY files
    /// </summary>
    [System.Serializable]
    public class GsplatLoadEvent : UnityEvent<GsplatAsset> { }

    [System.Serializable]
    public class GsplatProgressEvent : UnityEvent<float> { }

    [System.Serializable]
    public class GsplatErrorEvent : UnityEvent<string> { }

    public class GsplatRuntimeManager : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Auto-create renderer component if not present")]
        public bool autoCreateRenderer = true;
        
        [Tooltip("SH degree for rendering (0-3)")]
        [Range(0, 3)]
        public int shDegree = 3;
        
        [Tooltip("Enable gamma to linear conversion")]
        public bool gammaToLinear = false;
        
        [Tooltip("Enable async upload for better performance")]
        public bool asyncUpload = true;
        
        [Tooltip("Max splat count to upload per frame")]
        public uint uploadBatchSize = 100000;

        [Header("Events")]
        public GsplatLoadEvent OnGsplatLoaded = new GsplatLoadEvent();
        public GsplatProgressEvent OnLoadProgress = new GsplatProgressEvent();
        public GsplatErrorEvent OnLoadError = new GsplatErrorEvent();

        [Header("Current Asset")]
        [SerializeField] private GsplatAsset currentAsset;
        
        private GsplatRenderer gsplatRenderer;
        private Coroutine loadCoroutine;

        public GsplatAsset CurrentAsset => currentAsset;
        public GsplatRenderer Renderer => gsplatRenderer;
        public bool IsLoading => loadCoroutine != null;

        void Awake()
        {
            gsplatRenderer = GetComponent<GsplatRenderer>();
            if (gsplatRenderer == null && autoCreateRenderer)
            {
                gsplatRenderer = gameObject.AddComponent<GsplatRenderer>();
                UpdateRendererSettings();
            }
        }

        void UpdateRendererSettings()
        {
            if (gsplatRenderer == null) return;
            
            gsplatRenderer.SHDegree = shDegree;
            gsplatRenderer.GammaToLinear = gammaToLinear;
            gsplatRenderer.AsyncUpload = asyncUpload;
            gsplatRenderer.UploadBatchSize = uploadBatchSize;
        }

        /// <summary>
        /// Load PLY file from file path
        /// </summary>
        /// <param name="filePath">Path to PLY file</param>
        public void LoadPlyFile(string filePath)
        {
            if (IsLoading)
            {
                StopCoroutine(loadCoroutine);
            }

            loadCoroutine = StartCoroutine(LoadPlyFileCoroutine(filePath));
        }

        /// <summary>
        /// Load PLY file from byte array
        /// </summary>
        /// <param name="data">PLY file data</param>
        public void LoadPlyFromBytes(byte[] data)
        {
            if (IsLoading)
            {
                StopCoroutine(loadCoroutine);
            }

            loadCoroutine = StartCoroutine(LoadPlyFromBytesCoroutine(data));
        }

        /// <summary>
        /// Load PLY file from Resources folder
        /// </summary>
        /// <param name="resourcePath">Path in Resources folder (without .ply extension)</param>
        public void LoadPlyFromResources(string resourcePath)
        {
            if (IsLoading)
            {
                StopCoroutine(loadCoroutine);
            }

            loadCoroutine = StartCoroutine(LoadPlyFromResourcesCoroutine(resourcePath));
        }

        /// <summary>
        /// Load PLY file from StreamingAssets folder
        /// </summary>
        /// <param name="streamingAssetPath">Path relative to StreamingAssets folder</param>
        public void LoadPlyFromStreamingAssets(string streamingAssetPath)
        {
            if (IsLoading)
            {
                StopCoroutine(loadCoroutine);
            }

            loadCoroutine = StartCoroutine(LoadPlyFromStreamingAssetsCoroutine(streamingAssetPath));
        }

        /// <summary>
        /// Set an already loaded GsplatAsset
        /// </summary>
        /// <param name="asset">GsplatAsset to display</param>
        public void SetGsplatAsset(GsplatAsset asset)
        {
            if (IsLoading)
            {
                StopCoroutine(loadCoroutine);
                loadCoroutine = null;
            }

            currentAsset = asset;
            
            if (gsplatRenderer == null && autoCreateRenderer)
            {
                gsplatRenderer = gameObject.AddComponent<GsplatRenderer>();
                UpdateRendererSettings();
            }

            if (gsplatRenderer != null)
            {
                gsplatRenderer.GsplatAsset = currentAsset;
            }

            OnGsplatLoaded?.Invoke(currentAsset);
        }

        /// <summary>
        /// Clear current asset
        /// </summary>
        public void ClearAsset()
        {
            if (IsLoading)
            {
                StopCoroutine(loadCoroutine);
                loadCoroutine = null;
            }

            currentAsset = null;
            if (gsplatRenderer != null)
            {
                gsplatRenderer.GsplatAsset = null;
            }
        }

        private IEnumerator LoadPlyFileCoroutine(string filePath)
        {
            OnLoadProgress?.Invoke(0f);
            
            yield return StartCoroutine(GsplatPlyLoader.LoadFromFileAsync(filePath, 
                progress => OnLoadProgress?.Invoke(progress)));

            var result = GsplatPlyLoader.LoadFromFile(filePath);
            loadCoroutine = null;

            if (result != null)
            {
                SetGsplatAsset(result);
            }
            else
            {
                OnLoadError?.Invoke($"Failed to load PLY file: {filePath}");
            }
        }

        private IEnumerator LoadPlyFromBytesCoroutine(byte[] data)
        {
            OnLoadProgress?.Invoke(0f);
            yield return null; // Allow frame to pass

            OnLoadProgress?.Invoke(0.5f);
            var result = GsplatPlyLoader.LoadFromBytes(data);
            yield return null; // Allow frame to pass

            loadCoroutine = null;
            OnLoadProgress?.Invoke(1f);

            if (result != null)
            {
                SetGsplatAsset(result);
            }
            else
            {
                OnLoadError?.Invoke("Failed to load PLY from byte array");
            }
        }

        private IEnumerator LoadPlyFromStreamingAssetsCoroutine(string fileName)
        {
            OnLoadProgress?.Invoke(0f);
            
            Debug.Log($"[GsplatRuntimeManager] Loading {fileName} from StreamingAssets. Platform info: {GsplatQuestHelper.GetPlatformInfo()}");
            
            byte[] data = null;
            bool loadComplete = false;
            string errorMessage = null;
            
            yield return StartCoroutine(GsplatQuestHelper.LoadStreamingAssetBytes(fileName, 
                loadedData => {
                    data = loadedData;
                    loadComplete = true;
                    OnLoadProgress?.Invoke(0.7f);
                },
                error => {
                    errorMessage = error;
                    loadComplete = true;
                }));
            
            // Wait for load to complete
            while (!loadComplete)
            {
                yield return null;
            }
            
            if (data != null)
            {
                OnLoadProgress?.Invoke(0.8f);
                yield return null; // Allow frame to pass
                
                var result = GsplatPlyLoader.LoadFromBytes(data);
                loadCoroutine = null;
                OnLoadProgress?.Invoke(1f);
                
                if (result != null)
                {
                    Debug.Log($"[GsplatRuntimeManager] Successfully loaded {result.SplatCount} splats from {fileName}");
                    SetGsplatAsset(result);
                }
                else
                {
                    OnLoadError?.Invoke($"Failed to parse PLY data from {fileName}");
                }
            }
            else
            {
                loadCoroutine = null;
                OnLoadError?.Invoke(errorMessage ?? $"Failed to load {fileName} from StreamingAssets");
            }
        }

        private IEnumerator LoadPlyFromResourcesCoroutine(string resourcePath)
        {
            OnLoadProgress?.Invoke(0f);
            
            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset == null)
            {
                OnLoadError?.Invoke($"PLY file not found in Resources: {resourcePath}");
                loadCoroutine = null;
                yield break;
            }

            OnLoadProgress?.Invoke(0.5f);
            yield return null; // Allow frame to pass

            var result = GsplatPlyLoader.LoadFromBytes(textAsset.bytes);
            loadCoroutine = null;
            OnLoadProgress?.Invoke(1f);

            if (result != null)
            {
                SetGsplatAsset(result);
            }
            else
            {
                OnLoadError?.Invoke($"Failed to load PLY from Resources: {resourcePath}");
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying && gsplatRenderer != null)
            {
                UpdateRendererSettings();
            }
        }

        void OnDestroy()
        {
            if (IsLoading)
            {
                StopCoroutine(loadCoroutine);
            }
        }

        #region Public API Methods for Easy Integration

        /// <summary>
        /// Quick method to load and display a PLY file from StreamingAssets
        /// </summary>
        /// <param name="fileName">PLY file name in StreamingAssets folder</param>
        public static void QuickLoadFromStreamingAssets(string fileName, GameObject targetObject = null)
        {
            if (targetObject == null)
            {
                targetObject = new GameObject($"Gsplat_{fileName}");
            }

            var manager = targetObject.GetComponent<GsplatRuntimeManager>();
            if (manager == null)
            {
                manager = targetObject.AddComponent<GsplatRuntimeManager>();
            }

            manager.LoadPlyFromStreamingAssets(fileName);
        }

        /// <summary>
        /// Quick method to load and display a PLY file from file path
        /// </summary>
        /// <param name="filePath">Full path to PLY file</param>
        public static void QuickLoadFromFile(string filePath, GameObject targetObject = null)
        {
            if (targetObject == null)
            {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                targetObject = new GameObject($"Gsplat_{fileName}");
            }

            var manager = targetObject.GetComponent<GsplatRuntimeManager>();
            if (manager == null)
            {
                manager = targetObject.AddComponent<GsplatRuntimeManager>();
            }

            manager.LoadPlyFile(filePath);
        }

        #endregion
    }
}
