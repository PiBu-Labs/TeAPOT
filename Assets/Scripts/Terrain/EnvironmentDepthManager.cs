// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using System;
using UnityEngine;

namespace Teapot
{
    [DefaultExecutionOrder(-40)]
    public class EnvironmentDepthManager : MonoBehaviour
    {
        private const string TAG = "EnvironmentDepthManager";

        public static bool DepthAvailable { get; private set; }
        public static bool PreprocessedDepthAvailable { get; private set; }

        public static int ID(string str) => Shader.PropertyToID(str);

        private static readonly int MetaDepthTexID = ID("_EnvironmentDepthTexture");
        private static readonly int MetaPreprocessedDepthTexID = ID("_PreprocessedEnvironmentDepthTexture");
        private static readonly int MetaReprojectionMatsID = ID("_EnvironmentDepthReprojectionMatrices");

        public static readonly int DepthTexSizeID = ID("_DepthTextureSize");

        private static readonly int InvDepthReprojectionMatsID = ID("_InvDepthReprojectionMatrices");

        private static Matrix4x4[] _invReprojectionMats = Array.Empty<Matrix4x4>();

        private void Update()
        {
            UpdateCurrentRenderingState();
        }

        private static void UpdateCurrentRenderingState()
        {
            // Check availability of depth texture
            var depthTex = Shader.GetGlobalTexture(MetaDepthTexID);

            switch (DepthAvailable)
            {
                case false when depthTex:
                    Logging.Log($"[{TAG}] Depth texture available: {depthTex.width} x {depthTex.height}");
                    break;
                case true when !depthTex:
                    Logging.LogError($"[{TAG}] Depth texture not available");
                    break;
            }

            DepthAvailable = depthTex;

            if (!DepthAvailable) return;

            // Check availability of preprocessed depth texture
            var preprocessedDepthTex = Shader.GetGlobalTexture(MetaPreprocessedDepthTexID);

            switch (PreprocessedDepthAvailable)
            {
                case false when preprocessedDepthTex:
                    Logging.Log(
                        $"[{TAG}] Preprocessed depth texture available: {preprocessedDepthTex.width} x {preprocessedDepthTex.height}");
                    break;
                case true when !preprocessedDepthTex:
                    Logging.LogError($"[{TAG}] Preprocessed depth texture not available");
                    break;
            }

            PreprocessedDepthAvailable = preprocessedDepthTex;

            if (!PreprocessedDepthAvailable) return;

            // Set depth texture size as global vector
            Shader.SetGlobalVector(DepthTexSizeID, new Vector2(depthTex.width, depthTex.height));

            // Set inverse depth reprojection matrices as global matrix array
            var reprojectionMats = Shader.GetGlobalMatrixArray(MetaReprojectionMatsID);
            if (reprojectionMats == null || reprojectionMats.Length == 0) return;

            if (_invReprojectionMats.Length != reprojectionMats.Length)
                _invReprojectionMats = new Matrix4x4[reprojectionMats.Length];

            for (var i = 0; i < reprojectionMats.Length; ++i)
                _invReprojectionMats[i] = reprojectionMats[i].inverse;

            Shader.SetGlobalMatrixArray(InvDepthReprojectionMatsID, _invReprojectionMats);
        }
    }
}
