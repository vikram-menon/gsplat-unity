// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;
using UnityEngine.UI;

namespace Gsplat.Examples
{
    /// <summary>
    /// Example script demonstrating runtime PLY loading
    /// </summary>
    public class GsplatRuntimeExample : MonoBehaviour
    {
        [Header("UI References")]
        public Button loadFromStreamingAssetsButton;
        public Button loadFromFileButton;
        public Button clearButton;
        public Slider progressSlider;
        public Text statusText;
        
        [Header("Settings")]
        [Tooltip("PLY file name in StreamingAssets folder")]
        public string streamingAssetFileName = "example.ply";
        
        [Tooltip("File path for loading from file system")]
        public string filePath = "";

        private GsplatRuntimeManager runtimeManager;

        void Start()
        {
            // Get or create runtime manager
            runtimeManager = GetComponent<GsplatRuntimeManager>();
            if (runtimeManager == null)
            {
                runtimeManager = gameObject.AddComponent<GsplatRuntimeManager>();
            }

            // Setup event listeners
            runtimeManager.OnGsplatLoaded.AddListener(OnGsplatLoaded);
            runtimeManager.OnLoadProgress.AddListener(OnLoadProgress);
            runtimeManager.OnLoadError.AddListener(OnLoadError);

            // Setup UI event listeners
            if (loadFromStreamingAssetsButton != null)
                loadFromStreamingAssetsButton.onClick.AddListener(LoadFromStreamingAssets);
            
            if (loadFromFileButton != null)
                loadFromFileButton.onClick.AddListener(LoadFromFile);
            
            if (clearButton != null)
                clearButton.onClick.AddListener(ClearAsset);

            UpdateUI();
        }

        void LoadFromStreamingAssets()
        {
            if (string.IsNullOrEmpty(streamingAssetFileName))
            {
                UpdateStatus("Error: No streaming asset filename specified");
                return;
            }

            UpdateStatus($"Loading {streamingAssetFileName} from StreamingAssets...");
            runtimeManager.LoadPlyFromStreamingAssets(streamingAssetFileName);
        }

        void LoadFromFile()
        {
            if (string.IsNullOrEmpty(filePath))
            {
                UpdateStatus("Error: No file path specified");
                return;
            }

            UpdateStatus($"Loading {filePath}...");
            runtimeManager.LoadPlyFile(filePath);
        }

        void ClearAsset()
        {
            runtimeManager.ClearAsset();
            UpdateStatus("Asset cleared");
        }

        void OnGsplatLoaded(GsplatAsset asset)
        {
            UpdateStatus($"Loaded successfully! {asset.SplatCount} splats");
        }

        void OnLoadProgress(float progress)
        {
            if (progressSlider != null)
                progressSlider.value = progress;
        }

        void OnLoadError(string error)
        {
            UpdateStatus($"Error: {error}");
        }

        void UpdateStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
            Debug.Log($"[GsplatRuntimeExample] {message}");
        }

        void UpdateUI()
        {
            bool isLoading = runtimeManager != null && runtimeManager.IsLoading;
            
            if (loadFromStreamingAssetsButton != null)
                loadFromStreamingAssetsButton.interactable = !isLoading;
            
            if (loadFromFileButton != null)
                loadFromFileButton.interactable = !isLoading;
            
            if (clearButton != null)
                clearButton.interactable = !isLoading && runtimeManager != null && runtimeManager.CurrentAsset != null;
        }

        void Update()
        {
            UpdateUI();
        }

        void OnDestroy()
        {
            // Clean up event listeners
            if (runtimeManager != null)
            {
                runtimeManager.OnGsplatLoaded.RemoveListener(OnGsplatLoaded);
                runtimeManager.OnLoadProgress.RemoveListener(OnLoadProgress);
                runtimeManager.OnLoadError.RemoveListener(OnLoadError);
            }
        }
    }
}
