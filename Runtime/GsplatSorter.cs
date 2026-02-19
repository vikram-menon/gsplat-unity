// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    public interface IGsplat
    {
        public Transform transform { get; }
        public uint SplatCount { get; }
        public bool EnableActiveSetCompaction { get; }
        public Vector2 ActiveSetCenterUV { get; }
        public float ActiveSetInnerRadius { get; }
        public float ActiveSetOuterRadius { get; }
        public float ActiveSetPeripheralKeepProbability { get; }
        public ISorterResource SorterResource { get; }
        public bool isActiveAndEnabled { get; }
        public bool Valid { get; }
        public void SetSortedSplatCount(uint count);
    }

    public interface ISorterResource
    {
        public GraphicsBuffer PositionBuffer { get; }
        public GraphicsBuffer OrderBuffer { get; }
        public void Dispose();
    }

    // some codes of this class originated from the GaussianSplatRenderSystem in aras-p/UnityGaussianSplatting by Aras Pranckevičius
    // https://github.com/aras-p/UnityGaussianSplatting/blob/main/package/Runtime/GaussianSplatRenderer.cs
    public class GsplatSorter
    {
        class Resource : ISorterResource
        {
            public GraphicsBuffer PositionBuffer { get; }
            public GraphicsBuffer OrderBuffer { get; }

            public GraphicsBuffer InputKeys { get; private set; }
            public GraphicsBuffer ActiveIndexBuffer { get; private set; }
            public GraphicsBuffer ActiveCountBuffer { get; private set; }
            public GraphicsBuffer IndirectDispatchArgsBuffer { get; private set; }
            public GsplatSortPass.SupportResources Resources { get; }
            public bool Initialized;
            public uint LastKnownActiveCount { get; private set; }
            public bool HasValidActiveCount { get; private set; }
            public bool HasBuiltActiveSetAtLeastOnce { get; private set; }
            public int LastReadbackRequestFrame { get; private set; } = -1;

            AsyncGPUReadbackRequest m_activeCountReadbackRequest;
            bool m_hasActiveCountReadbackRequest;

            public Resource(uint count, GraphicsBuffer positionBuffer, GraphicsBuffer orderBuffer)
            {
                PositionBuffer = positionBuffer;
                OrderBuffer = orderBuffer;

                InputKeys = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)count, sizeof(uint));
                ActiveIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)count, sizeof(uint));
                ActiveCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
                IndirectDispatchArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 9, sizeof(uint));
                Resources = GsplatSortPass.SupportResources.Load(count);
                LastKnownActiveCount = count;
            }

            public void PumpActiveCountReadback(uint maxCount)
            {
                if (!m_hasActiveCountReadbackRequest || !m_activeCountReadbackRequest.done)
                    return;

                if (!m_activeCountReadbackRequest.hasError)
                {
                    var data = m_activeCountReadbackRequest.GetData<uint>();
                    if (data.Length > 0)
                    {
                        LastKnownActiveCount = (uint)Mathf.Clamp((int)data[0], 1, (int)maxCount);
                        HasValidActiveCount = true;
                    }
                }

                m_hasActiveCountReadbackRequest = false;
            }

            public void RequestActiveCountReadback(int frame)
            {
                if (m_hasActiveCountReadbackRequest || LastReadbackRequestFrame == frame)
                    return;
                m_activeCountReadbackRequest = AsyncGPUReadback.Request(ActiveCountBuffer);
                m_hasActiveCountReadbackRequest = true;
                LastReadbackRequestFrame = frame;
            }

            public void MarkActiveSetBuilt()
            {
                HasBuiltActiveSetAtLeastOnce = true;
            }

            public void Dispose()
            {
                InputKeys?.Dispose();
                ActiveIndexBuffer?.Dispose();
                ActiveCountBuffer?.Dispose();
                IndirectDispatchArgsBuffer?.Dispose();
                Resources.Dispose();

                InputKeys = null;
                ActiveIndexBuffer = null;
                ActiveCountBuffer = null;
                IndirectDispatchArgsBuffer = null;
            }
        }

        public static GsplatSorter Instance => s_instance ??= new GsplatSorter();
        static GsplatSorter s_instance;
        CommandBuffer m_commandBuffer;
        readonly HashSet<IGsplat> m_gsplats = new();
        readonly HashSet<Camera> m_camerasInjected = new();
        readonly List<IGsplat> m_activeGsplats = new();
        readonly Dictionary<Camera, CameraSortState> m_cameraSortStates = new();
        readonly Dictionary<IGsplat, TransformState> m_lastGsplatTransforms = new();
        int m_lastTransformCheckFrame = -1;
        bool m_transformsChangedThisFrame;
        GsplatSortPass m_sortPass;
        public const string k_PassName = "SortGsplats";

        public bool Valid => m_sortPass is { Valid: true };

        struct TransformState
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        class CameraSortState
        {
            public Vector3 LastPosition;
            public Quaternion LastRotation;
            public int FramesSinceSort;
            public bool Initialized;
        }

        public void InitSorter(ComputeShader computeShader)
        {
            m_sortPass = computeShader ? new GsplatSortPass(computeShader) : null;
        }

        public void RegisterGsplat(IGsplat gsplat)
        {
            if (m_gsplats.Count == 0)
            {
                if (!GraphicsSettings.currentRenderPipeline)
                    Camera.onPreCull += OnPreCullCamera;
            }

            m_gsplats.Add(gsplat);
        }

        public void UnregisterGsplat(IGsplat gsplat)
        {
            if (!m_gsplats.Remove(gsplat))
                return;
            m_lastGsplatTransforms.Remove(gsplat);
            if (m_gsplats.Count != 0) return;

            if (m_camerasInjected != null)
            {
                if (m_commandBuffer != null)
                    foreach (var cam in m_camerasInjected.Where(cam => cam))
                        cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, m_commandBuffer);
                m_camerasInjected.Clear();
            }

            m_activeGsplats.Clear();
            m_commandBuffer?.Dispose();
            m_commandBuffer = null;
            m_cameraSortStates.Clear();
            m_lastGsplatTransforms.Clear();
            Camera.onPreCull -= OnPreCullCamera;
        }

        public bool GatherGsplatsForCamera(Camera cam)
        {
            if (cam.cameraType == CameraType.Preview)
                return false;

            m_activeGsplats.Clear();
            foreach (var gs in m_gsplats.Where(gs => gs is { isActiveAndEnabled: true, Valid: true }))
                m_activeGsplats.Add(gs);
            return m_activeGsplats.Count != 0;
        }

        bool HaveGsplatTransformsChanged()
        {
            if (m_lastTransformCheckFrame == Time.frameCount)
                return m_transformsChangedThisFrame;

            m_lastTransformCheckFrame = Time.frameCount;
            var anyChanged = false;
            foreach (var gs in m_activeGsplats)
            {
                var current = new TransformState
                {
                    Position = gs.transform.position,
                    Rotation = gs.transform.rotation
                };
                if (!m_lastGsplatTransforms.TryGetValue(gs, out var prev))
                {
                    m_lastGsplatTransforms[gs] = current;
                    anyChanged = true;
                    continue;
                }

                if ((current.Position - prev.Position).sqrMagnitude > 1e-8f ||
                    Quaternion.Angle(current.Rotation, prev.Rotation) > 0.01f)
                {
                    m_lastGsplatTransforms[gs] = current;
                    anyChanged = true;
                }
            }

            m_transformsChangedThisFrame = anyChanged;
            return anyChanged;
        }

        bool ShouldSortCamera(Camera camera)
        {
            var settings = GsplatSettings.Instance;
            if (!settings.EnableSortThrottle)
                return true;

            if (!m_cameraSortStates.TryGetValue(camera, out var state))
            {
                state = new CameraSortState();
                m_cameraSortStates[camera] = state;
            }

            if (!state.Initialized)
                return true;

            if (HaveGsplatTransformsChanged())
                return true;

            var camMoved = (camera.transform.position - state.LastPosition).sqrMagnitude >=
                           settings.ResortPosThresholdMeters * settings.ResortPosThresholdMeters;
            var camRotated = Quaternion.Angle(camera.transform.rotation, state.LastRotation) >=
                             settings.ResortRotThresholdDegrees;
            if (camMoved || camRotated)
                return true;

            var staleFrames = Mathf.Max(1, settings.MaxSortStaleFrames);
            if (state.FramesSinceSort >= staleFrames)
                return true;

            var sortEvery = Mathf.Max(1, settings.SortEveryNFrames);
            return state.FramesSinceSort >= sortEvery;
        }

        void OnSortDecision(Camera camera, bool didSort)
        {
            if (!m_cameraSortStates.TryGetValue(camera, out var state))
            {
                state = new CameraSortState();
                m_cameraSortStates[camera] = state;
            }

            if (didSort)
            {
                state.LastPosition = camera.transform.position;
                state.LastRotation = camera.transform.rotation;
                state.FramesSinceSort = 0;
                state.Initialized = true;
                return;
            }

            if (state.Initialized)
                state.FramesSinceSort++;
        }

        void InitialClearCmdBuffer(Camera cam)
        {
            m_commandBuffer ??= new CommandBuffer { name = k_PassName };
            if (!GraphicsSettings.currentRenderPipeline && cam &&
                !m_camerasInjected.Contains(cam))
            {
                cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, m_commandBuffer);
                m_camerasInjected.Add(cam);
            }

            m_commandBuffer.Clear();
        }

        void OnPreCullCamera(Camera camera)
        {
            if (!Valid || !GsplatSettings.Instance.Valid || !GatherGsplatsForCamera(camera))
                return;

            InitialClearCmdBuffer(camera);
            DispatchSort(m_commandBuffer, camera);
        }

        public void DispatchSort(CommandBuffer cmd, Camera camera)
        {
            var shouldSort = ShouldSortCamera(camera);
            if (!shouldSort)
            {
                OnSortDecision(camera, false);
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var sortStartTime = Time.realtimeSinceStartupAsDouble;
#endif
            foreach (var gs in m_activeGsplats)
            {
                var res = (Resource)gs.SorterResource;
                if (!res.Initialized)
                {
                    m_sortPass.InitPayload(cmd, res.OrderBuffer, (uint)res.OrderBuffer.count);
                    res.Initialized = true;
                }

                res.PumpActiveCountReadback(gs.SplatCount);

                var activeSetFeatureEnabled =
                    GsplatSettings.Instance.EnableActiveSetCompaction && gs.EnableActiveSetCompaction;
                var enableCompaction = activeSetFeatureEnabled && res.HasValidActiveCount;
                var sortCount = gs.SplatCount;
                if (enableCompaction)
                    sortCount = (uint)Mathf.Clamp((int)res.LastKnownActiveCount, 1, (int)gs.SplatCount);

                var activeCenter = new Vector2(Mathf.Clamp01(gs.ActiveSetCenterUV.x),
                    Mathf.Clamp01(gs.ActiveSetCenterUV.y));
                var activeInner = Mathf.Max(0f, gs.ActiveSetInnerRadius);
                var activeOuter = Mathf.Max(activeInner + 0.0001f, gs.ActiveSetOuterRadius);
                var activePeripheralKeep = Mathf.Clamp01(gs.ActiveSetPeripheralKeepProbability);
                var activeMinKeep = Mathf.Clamp01(GsplatSettings.Instance.ActiveSetMinKeep);

                var sorterArgs = new GsplatSortPass.Args
                {
                    Count = gs.SplatCount,
                    SortCount = sortCount,
                    MatrixMv = camera.worldToCameraMatrix * gs.transform.localToWorldMatrix,
                    MatrixMvp = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) *
                                camera.worldToCameraMatrix * gs.transform.localToWorldMatrix,
                    BuildActiveSet = activeSetFeatureEnabled,
                    EnableActiveSetCompaction = enableCompaction,
                    ActiveSetCenterUV = activeCenter,
                    ActiveSetInnerRadius = activeInner,
                    ActiveSetOuterRadius = activeOuter,
                    ActiveSetPeripheralKeepProbability = activePeripheralKeep,
                    ActiveSetMinKeep = activeMinKeep,
                    PositionBuffer = res.PositionBuffer,
                    InputKeys = res.InputKeys,
                    InputValues = res.OrderBuffer,
                    ActiveIndexBuffer = res.ActiveIndexBuffer,
                    ActiveCountBuffer = res.ActiveCountBuffer,
                    IndirectDispatchArgsBuffer = res.IndirectDispatchArgsBuffer,
                    Resources = res.Resources
                };
                var sortedCount = m_sortPass.Dispatch(cmd, sorterArgs);
                gs.SetSortedSplatCount(sortedCount);
                if (activeSetFeatureEnabled)
                {
                    if (res.HasBuiltActiveSetAtLeastOnce)
                        res.RequestActiveCountReadback(Time.frameCount);
                    else
                        res.MarkActiveSetBuilt();
                }
            }
            OnSortDecision(camera, true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var sortEndTime = Time.realtimeSinceStartupAsDouble;
            GsplatPerfStats.RecordSortSubmissionMs((sortEndTime - sortStartTime) * 1000.0);
#endif
        }

        public ISorterResource CreateSorterResource(uint count, GraphicsBuffer positionBuffer,
            GraphicsBuffer orderBuffer)
        {
            return new Resource(count, positionBuffer, orderBuffer);
        }
    }
}
