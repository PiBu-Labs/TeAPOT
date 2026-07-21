// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using UnityEngine;

namespace Teapot
{
    public readonly struct ComputeKernel
    {
        private readonly ComputeShader _shader;
        private readonly int _kernel;

        public readonly (int x, int y, int z) _groupSize;

        private ComputeKernel(ComputeShader shader, int kernel)
        {
            _shader = shader;
            _kernel = kernel;

            shader.GetKernelThreadGroupSizes(kernel, out var x, out var y, out var z);
            _groupSize = ((int)x, (int)y, (int)z);
        }

        public ComputeKernel(ComputeShader shader, string kernel) : this(shader, shader.FindKernel(kernel))
        {
        }

        public void Set(int id, int value)
        {
            _shader.SetInt(id, value);
        }

        public void Set(int id, float value)
        {
            _shader.SetFloat(id, value);
        }

        public void Set(int id, Vector4 vector)
        {
            _shader.SetVector(id, vector);
        }

        public void Set(int id, Matrix4x4 matrix)
        {
            _shader.SetMatrix(id, matrix);
        }

        public void Set(int id, Matrix4x4[] matrices)
        {
            _shader.SetMatrixArray(id, matrices);
        }

        public void Set(int id, Texture texture)
        {
            _shader.SetTexture(_kernel, id, texture);
        }

        public void Set(int id, ComputeBuffer buffer)
        {
            _shader.SetBuffer(_kernel, id, buffer);
        }

        public void Set(string id, int value)
        {
            _shader.SetInt(id, value);
        }

        public void Set(string id, float value)
        {
            _shader.SetFloat(id, value);
        }

        public void Set(string id, Vector3 vector)
        {
            _shader.SetVector(id, vector);
        }

        public void Set(string id, Matrix4x4 matrix)
        {
            _shader.SetMatrix(id, matrix);
        }

        public void Set(string id, Matrix4x4[] matrices)
        {
            _shader.SetMatrixArray(id, matrices);
        }

        public void Set(string id, Texture texture)
        {
            _shader.SetTexture(_kernel, id, texture);
        }

        public void Set(string id, ComputeBuffer buffer)
        {
            _shader.SetBuffer(_kernel, id, buffer);
        }

        public void Dispatch(int x, int y, int z) => _shader.Dispatch(_kernel, x, y, z);

        public void DispatchGroups(int fillX, int fillY, int fillZ = 1)
        {
            var numGroupsX = Mathf.CeilToInt(fillX / (float)_groupSize.x);
            var numGroupsY = Mathf.CeilToInt(fillY / (float)_groupSize.y);
            var numGroupsZ = Mathf.CeilToInt(fillZ / (float)_groupSize.z);

            Dispatch(numGroupsX, numGroupsY, numGroupsZ);
        }
    }
}
