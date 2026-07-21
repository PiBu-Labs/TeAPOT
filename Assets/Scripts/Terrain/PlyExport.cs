// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using System;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Teapot
{
    public static class PlyExport
    {
        /// <summary>
        /// Export heightfield as a point cloud PLY (binary little endian), one point per valid cell.
        /// Coordinates are in local plane coordinates: x in [0..planeSize.x], z in [0..planeSize.y], y = height.
        /// If weightTex is provided, only cells with weight >= weightMin are exported.
        /// </summary>
        public static void ExportPointCloudBinary(
            RenderTexture heightMu,
            Vector2 planeSize,
            string path,
            RenderTexture weightTex = null,
            float weightMin = 0.0f,
            Texture2D colorTex = null,
            bool includeNormals = false,
            bool flipX = false
        )
        {
            if (heightMu == null) throw new ArgumentNullException(nameof(heightMu));
            if (heightMu.graphicsFormat != GraphicsFormat.R32_SFloat)
                throw new Exception("heightMu must be GraphicsFormat.R32_SFloat for this export.");

            if (weightTex != null && weightTex.graphicsFormat != GraphicsFormat.R32_SFloat)
                throw new Exception("weightTex must be GraphicsFormat.R32_SFloat if provided.");

            // Read back heightMu
            var reqH = AsyncGPUReadback.Request(heightMu, 0, TextureFormat.RFloat);
            reqH.WaitForCompletion();
            if (reqH.hasError) throw new Exception("AsyncGPUReadback failed for heightMu.");
            var hData = reqH.GetData<float>();

            var w = heightMu.width;
            var h = heightMu.height;
            if (hData.Length != w * h) throw new Exception("Unexpected heightMu readback size.");

            // Optional: read back weights
            NativeArray<float> wData = default;
            var hasW = weightTex != null;
            if (hasW)
            {
                var reqW = AsyncGPUReadback.Request(weightTex, 0, TextureFormat.RFloat);
                reqW.WaitForCompletion();
                if (reqW.hasError) throw new Exception("AsyncGPUReadback failed for weightTex.");
                wData = reqW.GetData<float>();
                if (wData.Length != w * h) throw new Exception("Unexpected weightTex readback size.");
            }

            // Optional: CPU color texture
            if (colorTex != null && (colorTex.width != w || colorTex.height != h))
                throw new Exception("colorTex must have the same dimensions as heightMu.");

            var hasC = colorTex != null;
            Color32[] cData = null;
            if (hasC)
            {
                cData = colorTex.GetPixels32();
                if (cData.Length != w * h)
                    throw new Exception("Unexpected colorTex size.");
            }

            // Pre-count valid points
            var nValid = 0;
            for (var iy = 0; iy < h; iy++)
            for (var ix = 0; ix < w; ix++)
            {
                var idx = iy * w + ix;
                var height = hData[idx];
                if (float.IsNaN(height) || float.IsInfinity(height)) continue;

                if (hasW)
                {
                    var ww = wData[idx];
                    if (float.IsNaN(ww) || ww < weightMin) continue;
                }

                nValid++;
            }

            Vector3[] normals = null;
            if (includeNormals)
            {
                normals = new Vector3[w * h];
                var dx = (w > 1) ? planeSize.x / (w - 1) : 1f;
                var dz = (h > 1) ? planeSize.y / (h - 1) : 1f;

                for (var iy = 0; iy < h; iy++)
                for (var ix = 0; ix < w; ix++)
                {
                    var idx = iy * w + ix;

                    var ix0 = Mathf.Max(ix - 1, 0);
                    var ix1 = Mathf.Min(ix + 1, w - 1);
                    var iy0 = Mathf.Max(iy - 1, 0);
                    var iy1 = Mathf.Min(iy + 1, h - 1);

                    var hl = hData[iy * w + ix0];
                    var hr = hData[iy * w + ix1];
                    var hd = hData[iy0 * w + ix];
                    var hu = hData[iy1 * w + ix];

                    var hc = hData[idx];
                    if (!float.IsFinite(hl)) hl = hc;
                    if (!float.IsFinite(hr)) hr = hc;
                    if (!float.IsFinite(hd)) hd = hc;
                    if (!float.IsFinite(hu)) hu = hc;

                    var dhdx = (hr - hl) / (Mathf.Max(1, ix1 - ix0) * dx);
                    var dhdz = (hu - hd) / (Mathf.Max(1, iy1 - iy0) * dz);

                    var tx = new Vector3(1f, dhdx, 0f);
                    var tz = new Vector3(0f, dhdz, 1f);

                    normals[idx] = Vector3.Cross(tz, tx).normalized;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            var sb = new StringBuilder();
            sb.AppendLine("ply");
            sb.AppendLine("format binary_little_endian 1.0");
            sb.AppendLine($"element vertex {nValid}");
            sb.AppendLine("property float x");
            sb.AppendLine("property float y");
            sb.AppendLine("property float z");

            if (includeNormals)
            {
                sb.AppendLine("property float nx");
                sb.AppendLine("property float ny");
                sb.AppendLine("property float nz");
            }

            if (hasC)
            {
                sb.AppendLine("property uchar red");
                sb.AppendLine("property uchar green");
                sb.AppendLine("property uchar blue");
            }

            sb.AppendLine("end_header");
            bw.Write(Encoding.ASCII.GetBytes(sb.ToString()));

            for (var iy = 0; iy < h; iy++)
            for (var ix = 0; ix < w; ix++)
            {
                var idx = iy * w + ix;
                var height = hData[idx];
                if (!float.IsFinite(height)) continue;

                if (hasW)
                {
                    var ww = wData[idx];
                    if (!float.IsFinite(ww) || ww < weightMin) continue;
                }

                var u = (w > 1) ? (float)ix / (w - 1) : 0f;
                var v = (h > 1) ? (float)iy / (h - 1) : 0f;

                var x = u * planeSize.x - 0.5f * planeSize.x;
                var z = v * planeSize.y - 0.5f * planeSize.y;

                if (flipX) x = -x;

                bw.Write(x);
                bw.Write(height);
                bw.Write(z);

                if (includeNormals)
                {
                    var n = normals[idx];

                    bw.Write(flipX ? -n.x : n.x);
                    bw.Write(n.y);
                    bw.Write(n.z);
                }

                if (hasC)
                {
                    var cix = flipX ? (w - 1 - ix) : ix;
                    var c = cData[iy * w + cix];
                    bw.Write(c.r);
                    bw.Write(c.g);
                    bw.Write(c.b);
                }
            }
        }

        public static Texture2D BuildDebugTexture(
            int w,
            int h,
            Vector2 planeSize,
            float centerSizeMeters = 0.075f, // 7.5 cm
            float cornerSizeMeters = 0.075f
        )
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false, true);
            var pixels = new Color32[w * h];

            // Fill everything white
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 255);

            void FillRect(int x0, int y0, int x1, int y1, Color32 c)
            {
                x0 = Mathf.Clamp(x0, 0, w - 1);
                x1 = Mathf.Clamp(x1, 0, w - 1);
                y0 = Mathf.Clamp(y0, 0, h - 1);
                y1 = Mathf.Clamp(y1, 0, h - 1);

                for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    pixels[y * w + x] = c;
            }

            // meters per texel in exported point layout
            var dx = (w > 1) ? planeSize.x / (w - 1) : planeSize.x;
            var dy = (h > 1) ? planeSize.y / (h - 1) : planeSize.y;

            // Convert desired physical sizes to texel counts
            var centerW = Mathf.Max(1, Mathf.RoundToInt(centerSizeMeters / dx));
            var centerH = Mathf.Max(1, Mathf.RoundToInt(centerSizeMeters / dy));

            var cornerW = Mathf.Max(1, Mathf.RoundToInt(cornerSizeMeters / dx));
            var cornerH = Mathf.Max(1, Mathf.RoundToInt(cornerSizeMeters / dy));

            // Center rectangle, approximately 7.76 cm × 7.76 cm
            var cx = w / 2;
            var cy = h / 2;
            var cx0 = cx - centerW / 2;
            var cy0 = cy - centerH / 2;
            var cx1 = cx0 + centerW - 1;
            var cy1 = cy0 + centerH - 1;

            var red = new Color32(255, 0, 0, 255);
            var green = new Color32(0, 255, 0, 255);
            var blue = new Color32(0, 0, 255, 255);

            // Corner patches
            // texture coords: (0,0)=bottom-left, (0,h-1)=top-left
            FillRect(0, h - cornerH, cornerW - 1, h - 1, green); // top-left
            FillRect(w - cornerW, h - cornerH, w - 1, h - 1, red); // top-right

            // Center patch
            FillRect(cx0, cy0, cx1, cy1, blue);

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}
