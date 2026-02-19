// Copyright (c) 2026
// SPDX-License-Identifier: MIT

using UnityEngine;

namespace Gsplat
{
    /// <summary>
    /// Lightweight submission-time tracking for development profiling.
    /// </summary>
    public static class GsplatPerfStats
    {
        const int k_LogIntervalFrames = 120;

        static double s_sortMsSum;
        static int s_sortSamples;
        static double s_drawMsSum;
        static int s_drawSamples;
        static int s_lastLogFrame = -1;

        static bool ShouldTrack => Debug.isDebugBuild || Application.isEditor;

        public static void RecordSortSubmissionMs(double ms)
        {
            if (!ShouldTrack)
                return;
            s_sortMsSum += ms;
            s_sortSamples++;
            TryLog();
        }

        public static void RecordDrawSubmissionMs(double ms)
        {
            if (!ShouldTrack)
                return;
            s_drawMsSum += ms;
            s_drawSamples++;
            TryLog();
        }

        static void TryLog()
        {
            var frame = Time.frameCount;
            if (frame <= 0 || frame == s_lastLogFrame || frame % k_LogIntervalFrames != 0)
                return;

            s_lastLogFrame = frame;
            var avgSort = s_sortSamples > 0 ? s_sortMsSum / s_sortSamples : 0.0;
            var avgDraw = s_drawSamples > 0 ? s_drawMsSum / s_drawSamples : 0.0;

            Debug.Log(
                $"[GsplatPerf] {k_LogIntervalFrames}f avg submit ms: sort={avgSort:F4} ({s_sortSamples} samples), draw={avgDraw:F4} ({s_drawSamples} samples)");

            s_sortMsSum = 0;
            s_sortSamples = 0;
            s_drawMsSum = 0;
            s_drawSamples = 0;
        }
    }
}
