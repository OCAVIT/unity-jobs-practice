using System.Collections.Generic;
using UnityEngine;

namespace ProfilerCaptureSystem
{
    /// <summary>Меш средней полигональности: трилистник-узел (2560 треугольников) и ядро-сфера (384), у каждой части свой материал.</summary>
    internal static class OrbiterMesh
    {
        private const int Segments = 128;
        private const int Sides = 10;
        private const int TurnsP = 2;
        private const int TurnsQ = 3;
        private const float KnotRadius = 0.55f;
        private const float KnotDepth = 0.22f;
        private const float TubeRadius = 0.11f;
        private const int Stacks = 12;
        private const int Slices = 16;
        private const float CoreRadius = 0.2f;

        public static Mesh Build()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var knot = new List<int>();
            var core = new List<int>();

            AddKnot(vertices, normals, uvs, knot);
            AddCore(vertices, normals, uvs, core);

            var mesh = new Mesh { name = "Orbiter" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(knot, 0);
            mesh.SetTriangles(core, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddKnot(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            // Шаг должен быть заметным: Vector3.normalized обнуляет векторы короче 1e-5, а вторая разность мала.
            const float Step = 0.01f;
            for (var s = 0; s <= Segments; s++)
            {
                var t = s / (float)Segments * Mathf.PI * 2f;
                var center = Curve(t);
                var before = Curve(t - Step);
                var after = Curve(t + Step);
                var tangent = Normalize(after - before);
                var bend = Normalize(after + before - 2f * center);
                var binormal = Normalize(Vector3.Cross(tangent, bend));
                var normal = Vector3.Cross(binormal, tangent);

                for (var k = 0; k <= Sides; k++)
                {
                    var angle = k / (float)Sides * Mathf.PI * 2f;
                    var direction = Mathf.Cos(angle) * normal + Mathf.Sin(angle) * binormal;
                    vertices.Add(center + direction * TubeRadius);
                    normals.Add(direction);
                    uvs.Add(new Vector2(s / (float)Segments * 4f, k / (float)Sides));
                }
            }

            AddGrid(triangles, 0, Segments, Sides);
        }

        private static void AddCore(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            var start = vertices.Count;
            for (var stack = 0; stack <= Stacks; stack++)
            {
                var theta = stack / (float)Stacks * Mathf.PI;
                for (var slice = 0; slice <= Slices; slice++)
                {
                    var phi = slice / (float)Slices * Mathf.PI * 2f;
                    var direction = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    vertices.Add(direction * CoreRadius);
                    normals.Add(direction);
                    uvs.Add(new Vector2(slice / (float)Slices, stack / (float)Stacks));
                }
            }

            AddGrid(triangles, start, Stacks, Slices);
        }

        /// <summary>Сетка из строк и столбцов вершин. Порядок вершин даёт внешние нормали граней.</summary>
        private static void AddGrid(List<int> triangles, int start, int rows, int columns)
        {
            var stride = columns + 1;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var a = start + row * stride + column;
                    var b = a + stride;
                    triangles.Add(a);
                    triangles.Add(a + 1);
                    triangles.Add(b);
                    triangles.Add(a + 1);
                    triangles.Add(b + 1);
                    triangles.Add(b);
                }
            }
        }

        private static Vector3 Normalize(Vector3 vector)
        {
            return vector / vector.magnitude;
        }

        private static Vector3 Curve(float t)
        {
            var radius = KnotRadius + KnotDepth * Mathf.Cos(TurnsQ * t);
            return new Vector3(radius * Mathf.Cos(TurnsP * t), KnotDepth * Mathf.Sin(TurnsQ * t), radius * Mathf.Sin(TurnsP * t));
        }
    }
}
