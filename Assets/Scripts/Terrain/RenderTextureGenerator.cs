// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Teapot
{
    public static class RenderTextureGenerator
    {
        private static RenderTexture AllocIntRT(int w, int h, GraphicsFormat fmt, bool mips, string name)
        {
            var desc = new RenderTextureDescriptor(w, h)
            {
                graphicsFormat = fmt, // R32_SInt or R32_UInts
                depthBufferBits = 0,
                msaaSamples = 1,
                mipCount = 1,
                dimension = TextureDimension.Tex2D,
                volumeDepth = 1,
                sRGB = false,
                enableRandomWrite = true,
                useMipMap = mips,
                autoGenerateMips = false
            };
            var rt = new RenderTexture(desc)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
            rt.Create();
            return rt;
        }

        public static void EnsureRT(ref RenderTexture rt, int w, int h, GraphicsFormat fmt, bool mips, string name)
        {
            if (rt != null && rt.width == w && rt.height == h && rt.graphicsFormat == fmt &&
                rt.useMipMap == mips) return;
            if (rt != null) rt.Release();
            rt = AllocIntRT(w, h, fmt, mips, name);
        }
    }
}
