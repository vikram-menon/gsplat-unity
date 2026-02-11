// Copyright (c) 2026
// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace Gsplat
{
    public enum QuestFoveationLevel
    {
        Off = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public class GsplatQuestFoveationController : MonoBehaviour
    {
        [Tooltip("Apply foveation settings when this component is enabled.")]
        public bool ApplyOnEnable = true;

        [Tooltip("Retry for a short period in case XR subsystems start after this component enables.")]
        public bool RetryUntilSubsystemReady = true;

        [Min(1)]
        [Tooltip("How many frames to retry when waiting for XR display subsystem startup.")]
        public int MaxRetryFrames = 120;

        [Tooltip("Only apply in Android player builds.")]
        public bool OnlyOnAndroid = true;

        [Tooltip("Target fixed foveation level.")]
        public QuestFoveationLevel Level = QuestFoveationLevel.Medium;

        [Tooltip("Allow gaze-directed foveation when the XR runtime supports it.")]
        public bool AllowGazeIfSupported = false;

        [Tooltip("Log foveation setup results in the Console.")]
        public bool LogResult = true;

        static readonly List<XRDisplaySubsystem> s_displays = new List<XRDisplaySubsystem>();
        Coroutine m_applyCoroutine;

        void OnEnable()
        {
            if (ApplyOnEnable)
            {
                if (m_applyCoroutine != null)
                    StopCoroutine(m_applyCoroutine);
                m_applyCoroutine = StartCoroutine(ApplyWhenReady());
            }
        }

        void OnDisable()
        {
            if (m_applyCoroutine == null)
                return;
            StopCoroutine(m_applyCoroutine);
            m_applyCoroutine = null;
        }

        [ContextMenu("Apply Foveation Settings")]
        public void ApplyFoveationSettings()
        {
            TryApplyFoveationSettings(true);
        }

        IEnumerator ApplyWhenReady()
        {
            var retries = RetryUntilSubsystemReady ? Mathf.Max(1, MaxRetryFrames) : 1;
            for (var frame = 0; frame < retries; frame++)
            {
                var logThisAttempt = LogResult && frame == retries - 1;
                if (TryApplyFoveationSettings(logThisAttempt))
                {
                    m_applyCoroutine = null;
                    yield break;
                }

                yield return null;
            }

            if (LogResult)
                Debug.LogWarning("[GsplatQuestFoveationController] Failed to apply foveation within retry window.");
            m_applyCoroutine = null;
        }

        bool TryApplyFoveationSettings(bool logResult)
        {
            if (OnlyOnAndroid && Application.platform != RuntimePlatform.Android)
            {
                if (logResult)
                    Debug.Log("[GsplatQuestFoveationController] Skipping setup: not running on Android.");
                return true;
            }

            s_displays.Clear();
            SubsystemManager.GetInstances(s_displays);
            if (s_displays.Count == 0)
            {
                if (logResult)
                    Debug.LogWarning("[GsplatQuestFoveationController] No XRDisplaySubsystem found.");
                return false;
            }

            var appliedAny = false;
            foreach (var display in s_displays)
            {
                if (display == null)
                    continue;

                var appliedLevel = TrySetFoveatedRenderingLevel(display, Level);
                var appliedFlags = TrySetFoveatedFlags(display, AllowGazeIfSupported);
                appliedAny |= appliedLevel || appliedFlags;

                if (!logResult)
                    continue;

                Debug.Log(
                    $"[GsplatQuestFoveationController] Display '{display.GetType().Name}': " +
                    $"levelApplied={appliedLevel}, flagsApplied={appliedFlags}, level={Level}, allowGaze={AllowGazeIfSupported}");
            }

            if (logResult && !appliedAny)
            {
                Debug.LogWarning(
                    "[GsplatQuestFoveationController] XR runtime does not expose writable foveation APIs on this Unity version.");
            }

            return appliedAny;
        }

        static bool TrySetFoveatedRenderingLevel(XRDisplaySubsystem display, QuestFoveationLevel level)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var prop = display.GetType().GetProperty("foveatedRenderingLevel", flags);
            if (prop == null || !prop.CanWrite)
                return false;

            try
            {
                if (prop.PropertyType == typeof(float))
                {
                    prop.SetValue(display, LevelToFloat(level));
                    return true;
                }

                if (prop.PropertyType == typeof(int))
                {
                    prop.SetValue(display, (int)level);
                    return true;
                }

                if (prop.PropertyType == typeof(uint))
                {
                    prop.SetValue(display, (uint)level);
                    return true;
                }
            }
            catch
            {
                // Ignore and fallback.
            }

            return false;
        }

        static bool TrySetFoveatedFlags(XRDisplaySubsystem display, bool allowGazeIfSupported)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var prop = display.GetType().GetProperty("foveatedRenderingFlags", flags);
            if (prop == null || !prop.CanWrite || !prop.PropertyType.IsEnum)
                return false;

            try
            {
                // Default to no flags and opt into gaze when requested and available.
                object value = Enum.ToObject(prop.PropertyType, 0);
                if (allowGazeIfSupported && TryGetEnumMember(prop.PropertyType, "GazeAllowed", out var gazeAllowed))
                    value = gazeAllowed;

                prop.SetValue(display, value);
                return true;
            }
            catch
            {
                // Ignore.
            }

            return false;
        }

        static bool TryGetEnumMember(Type enumType, string memberName, out object value)
        {
            foreach (var name in Enum.GetNames(enumType))
            {
                if (!string.Equals(name, memberName, StringComparison.Ordinal))
                    continue;
                value = Enum.Parse(enumType, name);
                return true;
            }

            value = null;
            return false;
        }

        static float LevelToFloat(QuestFoveationLevel level)
        {
            switch (level)
            {
                case QuestFoveationLevel.Off:
                    return 0f;
                case QuestFoveationLevel.Low:
                    return 0.33f;
                case QuestFoveationLevel.Medium:
                    return 0.66f;
                case QuestFoveationLevel.High:
                    return 1f;
                default:
                    return 0.66f;
            }
        }
    }
}
