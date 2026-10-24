using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 只负责球体基础拓扑，不负责地形。
/// 输入：subdivisions（每个三角面被连续拆成 4 个三角面）。
/// 输出：单位半径、顶点共用的 Icosphere Mesh。5 级 = 10242 顶点，6 级 = 40962 顶点。
/// 为了无需在 Blender 里预先准备模型，这个类提供零依赖的球体生成。
/// </summary>
public static class IcosphereBuilder
{
    private struct Triangle
    {
        public int a, b, c;
        public Triangle(int a, int b, int c) { this.a = a; this.b = b; this.c = c; }
    }

    /// <summary>
    /// 输入：subdivisions（球面三角形递归细分的次数，允许 0~6）。
    /// 输出：一份新建的单位球 Mesh；包含共享顶点、三角面索引与初始法线。
    /// 调用者负责决定 Mesh 何时释放或保存为 Asset。
    /// </summary>
    public static Mesh Build(int subdivisions)
    {
        subdivisions = Mathf.Clamp(subdivisions, 0, 6);
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;

        var vertices = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0),
            new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t),
            new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1),
            new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
        };
        for (int i = 0; i < vertices.Count; ++i)
            vertices[i] = vertices[i].normalized;

        var triangles = new List<Triangle>
        {
            new Triangle(0,11,5), new Triangle(0,5,1), new Triangle(0,1,7),
            new Triangle(0,7,10), new Triangle(0,10,11),
            new Triangle(1,5,9), new Triangle(5,11,4), new Triangle(11,10,2),
            new Triangle(10,7,6), new Triangle(7,1,8),
            new Triangle(3,9,4), new Triangle(3,4,2), new Triangle(3,2,6),
            new Triangle(3,6,8), new Triangle(3,8,9),
            new Triangle(4,9,5), new Triangle(2,4,11), new Triangle(6,2,10),
            new Triangle(8,6,7), new Triangle(9,8,1)
        };

        // 每一级重新使用边中点缓存，确保相邻三角面共享同一个顶点。
        for (int level = 0; level < subdivisions; ++level)
        {
            var cache = new Dictionary<ulong, int>();
            var next = new List<Triangle>(triangles.Count * 4);
            foreach (Triangle tri in triangles)
            {
                int ab = GetMiddle(tri.a, tri.b, vertices, cache);
                int bc = GetMiddle(tri.b, tri.c, vertices, cache);
                int ca = GetMiddle(tri.c, tri.a, vertices, cache);
                next.Add(new Triangle(tri.a, ab, ca));
                next.Add(new Triangle(tri.b, bc, ab));
                next.Add(new Triangle(tri.c, ca, bc));
                next.Add(new Triangle(ab, bc, ca));
            }
            triangles = next;
        }

        int[] indices = new int[triangles.Count * 3];
        for (int i = 0; i < triangles.Count; ++i)
        {
            indices[i * 3] = triangles[i].a;
            indices[i * 3 + 1] = triangles[i].b;
            indices[i * 3 + 2] = triangles[i].c;
        }

        var mesh = new Mesh { name = "PlanetGenerator_Icosphere_Generated" };
        // > 65535 顶点时，必须用 UInt32 索引；低细分时继续用较省内存的 UInt16。
        mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.triangles = indices;
        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// 输入：某条边的两个顶点 index、顶点表和本轮缓存。
    /// 输出：单位球面上的边中点 index；同一条边重复请求会复用已有 index。
    /// </summary>
    private static int GetMiddle(int a, int b, List<Vector3> vertices, Dictionary<ulong, int> cache)
    {
        uint lo = (uint)Mathf.Min(a, b);
        uint hi = (uint)Mathf.Max(a, b);
        ulong key = ((ulong)lo << 32) | hi;
        if (cache.TryGetValue(key, out int existing)) return existing;
        int index = vertices.Count;
        vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
        cache.Add(key, index);
        return index;
    }
}
