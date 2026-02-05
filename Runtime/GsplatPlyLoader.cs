// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Gsplat
{
    /// <summary>
    /// Runtime PLY file loader for Gaussian Splatting data
    /// </summary>
    public static class GsplatPlyLoader
    {
        public class PlyHeaderInfo
        {
            public uint VertexCount = 0;
            public int PropertyCount = 0;
            public int SHPropertyCount = 0;
            public int PositionOffset = -1;
            public int ColorOffset = -1;
            public int SHOffset = -1;
            public int OpacityOffset = -1;
            public int ScaleOffset = -1;
            public int RotationOffset = -1;
        }

        /// <summary>
        /// Read each line, used for header reading.
        /// </summary>
        private static string ReadLine(Stream stream)
        {
            List<byte> byteBuffer = new List<byte>();
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1 || b == '\n') break;
                byteBuffer.Add((byte)b);
            }

            // If line had CRLF line endings, remove the CR part
            if (byteBuffer.Count > 0 && byteBuffer.Last() == '\r')
            {
                byteBuffer.RemoveAt(byteBuffer.Count - 1);
            }

            return Encoding.UTF8.GetString(byteBuffer.ToArray());
        }

        /// <summary>
        /// Parse PLY header information
        /// </summary>
        private static PlyHeaderInfo ReadPlyHeader(Stream stream)
        {
            var info = new PlyHeaderInfo();

            while (ReadLine(stream) is { } line && line != "end_header")
            {
                var tokens = line.Split(' ');
                if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
                    info.VertexCount = uint.Parse(tokens[2]);
                if (tokens.Length != 3 || tokens[0] != "property") continue;
                switch (tokens[2])
                {
                    case "x":
                        info.PositionOffset = info.PropertyCount;
                        break;
                    case "f_dc_0":
                        info.ColorOffset = info.PropertyCount;
                        break;
                    case "f_rest_0":
                        info.SHOffset = info.PropertyCount;
                        break;
                    case "opacity":
                        info.OpacityOffset = info.PropertyCount;
                        break;
                    case "scale_0":
                        info.ScaleOffset = info.PropertyCount;
                        break;
                    case "rot_0":
                        info.RotationOffset = info.PropertyCount;
                        break;
                }

                if (tokens[2].StartsWith("f_rest_"))
                    info.SHPropertyCount++;
                info.PropertyCount++;
            }

            return info;
        }

        /// <summary>
        /// Load PLY file from file path and create GsplatAsset
        /// </summary>
        /// <param name="filePath">Path to PLY file</param>
        /// <returns>GsplatAsset or null if failed</returns>
        public static GsplatAsset LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"PLY file not found: {filePath}");
                return null;
            }

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    return LoadFromStream(fs);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load PLY file {filePath}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load PLY data from byte array and create GsplatAsset
        /// </summary>
        /// <param name="data">PLY file data</param>
        /// <returns>GsplatAsset or null if failed</returns>
        public static GsplatAsset LoadFromBytes(byte[] data)
        {
            try
            {
                using (var stream = new MemoryStream(data))
                {
                    return LoadFromStream(stream);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load PLY from bytes: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load PLY data from stream and create GsplatAsset
        /// </summary>
        /// <param name="stream">Stream containing PLY data</param>
        /// <returns>GsplatAsset or null if failed</returns>
        public static GsplatAsset LoadFromStream(Stream stream)
        {
            try
            {
                // Check file size limit (2GB due to array limitations)
                if (stream.Length >= 2 * 1024 * 1024 * 1024L)
                {
                    Debug.LogError("PLY file too large: files larger than 2GB are not supported");
                    return null;
                }

                var plyInfo = ReadPlyHeader(stream);
                var shCoeffs = plyInfo.SHPropertyCount / 3;
                
                // Validate PLY data
                if (plyInfo.VertexCount == 0)
                {
                    Debug.LogError("PLY file contains no vertices");
                    return null;
                }

                var shBands = GsplatUtils.CalcSHBandsFromSHPropertyCount(plyInfo.SHPropertyCount);
                
                if (shBands > 3 || GsplatUtils.SHBandsToCoefficientCount(shBands) * 3 != plyInfo.SHPropertyCount)
                {
                    Debug.LogError($"Unexpected SH property count {plyInfo.SHPropertyCount}");
                    return null;
                }

                if (plyInfo.PositionOffset == -1 || plyInfo.ColorOffset == -1 || plyInfo.OpacityOffset == -1 ||
                    plyInfo.ScaleOffset == -1 || plyInfo.RotationOffset == -1)
                {
                    Debug.LogError("Missing required properties in PLY header");
                    return null;
                }

                // Create GsplatAsset
                var gsplatAsset = ScriptableObject.CreateInstance<GsplatAsset>();
                gsplatAsset.SplatCount = plyInfo.VertexCount;
                gsplatAsset.SHBands = shBands;

                // Initialize arrays
                gsplatAsset.Positions = new Vector3[plyInfo.VertexCount];
                gsplatAsset.Colors = new Vector4[plyInfo.VertexCount];
                if (shCoeffs > 0)
                    gsplatAsset.SHs = new Vector3[plyInfo.VertexCount * shCoeffs];
                gsplatAsset.Scales = new Vector3[plyInfo.VertexCount];
                gsplatAsset.Rotations = new Vector4[plyInfo.VertexCount];

                var bounds = new Bounds();
                var buffer = new byte[plyInfo.PropertyCount * sizeof(float)];
                
                // Read vertex data
                for (uint i = 0; i < plyInfo.VertexCount; i++)
                {
                    var readBytes = stream.Read(buffer, 0, buffer.Length);
                    if (readBytes != buffer.Length)
                    {
                        Debug.LogError($"Unexpected end of file, got {readBytes} bytes at vertex {i}");
                        return null;
                    }

                    var properties = MemoryMarshal.Cast<byte, float>(buffer);
                    
                    // Position
                    gsplatAsset.Positions[i] = new Vector3(
                        properties[plyInfo.PositionOffset],
                        properties[plyInfo.PositionOffset + 1],
                        properties[plyInfo.PositionOffset + 2]);
                    
                    // Color and opacity
                    gsplatAsset.Colors[i] = new Vector4(
                        properties[plyInfo.ColorOffset],
                        properties[plyInfo.ColorOffset + 1],
                        properties[plyInfo.ColorOffset + 2],
                        GsplatUtils.Sigmoid(properties[plyInfo.OpacityOffset]));
                    
                    // Spherical harmonics
                    for (int j = 0; j < shCoeffs; j++)
                        gsplatAsset.SHs[i * shCoeffs + j] = new Vector3(
                            properties[j + plyInfo.SHOffset],
                            properties[j + plyInfo.SHOffset + shCoeffs],
                            properties[j + plyInfo.SHOffset + shCoeffs * 2]);
                    
                    // Scales
                    gsplatAsset.Scales[i] = new Vector3(
                        Mathf.Exp(properties[plyInfo.ScaleOffset]),
                        Mathf.Exp(properties[plyInfo.ScaleOffset + 1]),
                        Mathf.Exp(properties[plyInfo.ScaleOffset + 2]));
                    
                    // Rotations
                    gsplatAsset.Rotations[i] = new Vector4(
                        properties[plyInfo.RotationOffset],
                        properties[plyInfo.RotationOffset + 1],
                        properties[plyInfo.RotationOffset + 2],
                        properties[plyInfo.RotationOffset + 3]).normalized;

                    // Update bounds
                    if (i == 0) bounds = new Bounds(gsplatAsset.Positions[i], Vector3.zero);
                    else bounds.Encapsulate(gsplatAsset.Positions[i]);
                }

                gsplatAsset.Bounds = bounds;
                return gsplatAsset;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load PLY from stream: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Asynchronously load PLY file from file path
        /// </summary>
        /// <param name="filePath">Path to PLY file</param>
        /// <param name="progressCallback">Progress callback (0.0 to 1.0)</param>
        /// <returns>Coroutine that yields GsplatAsset or null</returns>
        public static IEnumerator LoadFromFileAsync(string filePath, System.Action<float> progressCallback = null)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"PLY file not found: {filePath}");
                yield break;
            }

            byte[] data = null;
            try
            {
                data = File.ReadAllBytes(filePath);
                progressCallback?.Invoke(0.5f);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to read PLY file {filePath}: {e.Message}");
                yield break;
            }

            yield return null; // Allow frame to pass

            var asset = LoadFromBytes(data);
            progressCallback?.Invoke(1.0f);
            yield return asset;
        }
    }
}
