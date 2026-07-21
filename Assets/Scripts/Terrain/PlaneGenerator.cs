// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Teapot
{
    public struct PlaneData
    {
        public List<Vector3> Vertices;
        public List<Vector3> Normals;
        public List<Vector2> UVs;
        public List<int> Triangles;
    }

    public static class PlaneGenerator
    {
        public const float MinSize = 0.01f;
        public const float MaxSize = 100;
        public const float DefaultSize = 1;

        public const int MinResolution = 1;
        public const int MaxResolution = 512;
        public const int DefaultResolution = 128;

        public static PlaneData Generate(Vector2 size, int2 resolution)
        {
            // Clamp size (no zero/negative values)
            size.x = Mathf.Clamp(size.x, MinSize, MaxSize);
            size.y = Mathf.Clamp(size.y, MinSize, MaxSize);

            // Clamp resolution (minimum 1)
            resolution.x = Mathf.Clamp(resolution.x, MinResolution, MaxResolution);
            resolution.y = Mathf.Clamp(resolution.y, MinResolution, MaxResolution);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // Calculate vertices, normals, and uvs
            var xPerStep = size.x / resolution.x;
            var yPerStep = size.y / resolution.y;

            var halfSizeX = 0.5f * size.x;
            var halfSizeY = 0.5f * size.y;

            for (var y = 0; y <= resolution.y; ++y)
            {
                for (var x = 0; x <= resolution.x; ++x)
                {
                    var xPos = x * xPerStep - halfSizeX;
                    var yPos = y * yPerStep - halfSizeY;

                    vertices.Add(new Vector3(xPos, 0, yPos));
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2((xPos + halfSizeX) / size.x, (yPos + halfSizeY) / size.y));
                }
            }

            // Set triangles indices
            for (var row = 0; row < resolution.y; ++row)
            {
                for (var col = 0; col < resolution.x; ++col)
                {
                    var i = row * (resolution.x + 1) + col;

                    triangles.Add(i);
                    triangles.Add(i + resolution.x + 1);
                    triangles.Add(i + resolution.x + 2);

                    triangles.Add(i);
                    triangles.Add(i + resolution.x + 2);
                    triangles.Add(i + 1);
                }
            }

            return new PlaneData
            {
                Vertices = vertices,
                Normals = normals,
                UVs = uvs,
                Triangles = triangles
            };
        }
    }
}
