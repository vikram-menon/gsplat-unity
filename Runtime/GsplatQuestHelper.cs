// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Gsplat
{
    /// <summary>
    /// Helper class for Quest/Android platform compatibility
    /// </summary>
    public static class GsplatQuestHelper
    {
        /// <summary>
        /// Get the correct StreamingAssets path for the current platform
        /// </summary>
        public static string GetStreamingAssetsPath(string fileName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // On Android/Quest, StreamingAssets are in a JAR file and need UnityWebRequest
            return Path.Combine(Application.streamingAssetsPath, fileName);
#else
            // On PC/Editor, use direct file access
            return Path.Combine(Application.streamingAssetsPath, fileName);
#endif
        }

        /// <summary>
        /// Load file from StreamingAssets with Quest/Android compatibility
        /// </summary>
        public static IEnumerator LoadStreamingAssetBytes(string fileName, System.Action<byte[]> onComplete, System.Action<string> onError = null)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Use UnityWebRequest for Android/Quest
            string path = GetStreamingAssetsPath(fileName);
            using (UnityWebRequest request = UnityWebRequest.Get(path))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(request.downloadHandler.data);
                }
                else
                {
                    string error = $"Failed to load {fileName} from StreamingAssets: {request.error}";
                    Debug.LogError(error);
                    onError?.Invoke(error);
                }
            }
#else
            // Use direct file access for PC/Editor
            try
            {
                string path = GetStreamingAssetsPath(fileName);
                if (File.Exists(path))
                {
                    byte[] data = File.ReadAllBytes(path);
                    onComplete?.Invoke(data);
                }
                else
                {
                    string error = $"File not found: {path}";
                    Debug.LogError(error);
                    onError?.Invoke(error);
                }
            }
            catch (System.Exception e)
            {
                string error = $"Failed to load {fileName}: {e.Message}";
                Debug.LogError(error);
                onError?.Invoke(error);
            }
            yield return null;
#endif
        }

        /// <summary>
        /// Check if running on Quest/Android platform
        /// </summary>
        public static bool IsQuestPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Get platform-specific debug info
        /// </summary>
        public static string GetPlatformInfo()
        {
            return $"Platform: {Application.platform}, " +
                   $"StreamingAssets: {Application.streamingAssetsPath}, " +
                   $"PersistentData: {Application.persistentDataPath}, " +
                   $"IsQuest: {IsQuestPlatform()}";
        }
    }
}
