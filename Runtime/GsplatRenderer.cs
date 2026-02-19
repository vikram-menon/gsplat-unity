// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using UnityEngine;

namespace Gsplat
{
    [ExecuteAlways]
    public class GsplatRenderer : MonoBehaviour, IGsplat
    {
        public GsplatAsset GsplatAsset;
        [Range(0, 3)] public int SHDegree = 2;
        public bool GammaToLinear;
        [Header("Foveated Quality")]
        public bool EnableFoveatedQuality = true;
        public Vector2 FoveaCenterUV = new Vector2(0.5f, 0.5f);
        [Min(0f)] public float FoveaInnerRadius = 0.12f;
        [Min(0f)] public float FoveaOuterRadius = 0.45f;
        [Range(0, 3)] public int PeripheralSHDegree = 0;
        [Min(0f)] public float PeripheralMinSplatPixels = 8.0f;
        [Range(0f, 1f)] public float PeripheralKeepProbability = 0.15f;

        [Header("Active Set Compaction")]
        public bool EnableActiveSetCompaction = true;
        [Min(0f)] public float ActiveSetInnerRadius = 0.18f;
        [Min(0f)] public float ActiveSetOuterRadius = 0.55f;
        [Range(0f, 1f)] public float ActiveSetPeripheralKeepProbability = 0.65f;

        [Header("Upload")]
        public bool AsyncUpload;

        [Tooltip("Max splat count to be uploaded per frame")]
        public uint UploadBatchSize = 100000;

        public bool RenderBeforeUploadComplete = true;

        GsplatAsset m_prevAsset;
        GsplatRendererImpl m_renderer;

        public bool Valid => RenderBeforeUploadComplete ? SplatCount > 0 : SplatCount == GsplatAsset.SplatCount;
        public uint SplatCount => GsplatAsset ? GsplatAsset.SplatCount - m_pendingSplatCount : 0;
        uint DrawSplatCount
        {
            get
            {
                var fullCount = SplatCount;
                if (!EnableActiveSetCompaction || m_sortedSplatCount == 0)
                    return fullCount;
                return Math.Min(fullCount, m_sortedSplatCount);
            }
        }
        public ISorterResource SorterResource => m_renderer?.SorterResource;
        bool IGsplat.EnableActiveSetCompaction => EnableActiveSetCompaction;
        Vector2 IGsplat.ActiveSetCenterUV => FoveaCenterUV;
        float IGsplat.ActiveSetInnerRadius => ActiveSetInnerRadius;
        float IGsplat.ActiveSetOuterRadius => ActiveSetOuterRadius;
        float IGsplat.ActiveSetPeripheralKeepProbability => ActiveSetPeripheralKeepProbability;

        uint m_pendingSplatCount;
        uint m_sortedSplatCount;

        void SetBufferData()
        {
            m_renderer.PositionBuffer.SetData(GsplatAsset.Positions);
            m_renderer.ScaleBuffer.SetData(GsplatAsset.Scales);
            m_renderer.RotationBuffer.SetData(GsplatAsset.Rotations);
            m_renderer.ColorBuffer.SetData(GsplatAsset.Colors);
            if (GsplatAsset.SHBands > 0)
                m_renderer.SHBuffer.SetData(GsplatAsset.SHs);
        }


        void SetBufferDataAsync()
        {
            m_pendingSplatCount = GsplatAsset.SplatCount;
        }

        void UploadData()
        {
            var offset = (int)(GsplatAsset.SplatCount - m_pendingSplatCount);
            var count = (int)Math.Min(UploadBatchSize, m_pendingSplatCount);
            m_pendingSplatCount -= (uint)count;
            m_renderer.PositionBuffer.SetData(GsplatAsset.Positions, offset, offset, count);
            m_renderer.ScaleBuffer.SetData(GsplatAsset.Scales, offset, offset, count);
            m_renderer.RotationBuffer.SetData(GsplatAsset.Rotations, offset, offset, count);
            m_renderer.ColorBuffer.SetData(GsplatAsset.Colors, offset, offset, count);
            if (GsplatAsset.SHBands <= 0) return;
            var coefficientCount = GsplatUtils.SHBandsToCoefficientCount(GsplatAsset.SHBands);
            m_renderer.SHBuffer.SetData(GsplatAsset.SHs, coefficientCount * offset,
                coefficientCount * offset, coefficientCount * count);
        }


        void OnEnable()
        {
            GsplatSorter.Instance.RegisterGsplat(this);
            if (!GsplatAsset)
                return;
            m_renderer = new GsplatRendererImpl(GsplatAsset.SplatCount, GsplatAsset.SHBands);
#if UNITY_EDITOR
            if (AsyncUpload && Application.isPlaying)
#else
            if (AsyncUpload)
#endif
                SetBufferDataAsync();
            else
                SetBufferData();
            m_sortedSplatCount = SplatCount;
        }

        void OnDisable()
        {
            GsplatSorter.Instance.UnregisterGsplat(this);
            m_renderer?.Dispose();
            m_renderer = null;
            m_sortedSplatCount = 0;
        }

        void Update()
        {
            if (m_pendingSplatCount > 0)
                UploadData();

            if (m_prevAsset != GsplatAsset)
            {
                m_prevAsset = GsplatAsset;
                if (GsplatAsset)
                {
                    if (m_renderer == null)
                        m_renderer = new GsplatRendererImpl(GsplatAsset.SplatCount, GsplatAsset.SHBands);
                    else
                        m_renderer.RecreateResources(GsplatAsset.SplatCount, GsplatAsset.SHBands);
#if UNITY_EDITOR
                    if (AsyncUpload && Application.isPlaying)
#else
                    if (AsyncUpload)
#endif
                        SetBufferDataAsync();
                    else
                        SetBufferData();
                    m_sortedSplatCount = SplatCount;
                }
            }

            if (Valid)
            {
                var clampedCenter = new Vector2(Mathf.Clamp01(FoveaCenterUV.x), Mathf.Clamp01(FoveaCenterUV.y));
                var clampedInner = Mathf.Max(0f, FoveaInnerRadius);
                var clampedOuter = Mathf.Max(clampedInner + 0.0001f, FoveaOuterRadius);
                var clampedPeripheralShDegree = Mathf.Clamp(PeripheralSHDegree, 0, SHDegree);
                var clampedPeripheralMinSplatPixels = Mathf.Max(2f, PeripheralMinSplatPixels);
                var clampedPeripheralKeepProbability = Mathf.Clamp01(PeripheralKeepProbability);
                m_renderer.Render(DrawSplatCount, transform, GsplatAsset.Bounds, gameObject.layer, GammaToLinear,
                    SHDegree, EnableFoveatedQuality, clampedCenter, clampedInner, clampedOuter,
                    clampedPeripheralShDegree, clampedPeripheralMinSplatPixels, clampedPeripheralKeepProbability);
            }
        }

        void IGsplat.SetSortedSplatCount(uint count)
        {
            m_sortedSplatCount = count;
        }
    }
}
