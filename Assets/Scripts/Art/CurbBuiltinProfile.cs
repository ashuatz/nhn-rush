using System.Collections.Generic;
using UnityEngine;

namespace Rush.Art
{
    /// <summary>
    /// 연석 FBX가 준비되기 전에 쓰는 임시 단면. +Z 방향으로 길이 1인 사다리꼴 압출 메시를 만든다.
    /// 실제 ProBuilder FBX를 CurbPath에 지정하면 더 이상 쓰이지 않는다.
    /// </summary>
    public static class CurbBuiltinProfile
    {
        // XY 평면 단면. 반시계 방향 순서라서 측면 노멀이 바깥을 향한다.
        static readonly Vector2[] Section =
        {
            new Vector2(-0.16f, 0.00f),
            new Vector2(0.16f, 0.00f),
            new Vector2(0.16f, 0.24f),
            new Vector2(-0.11f, 0.24f),
            new Vector2(-0.16f, 0.18f),
        };

        static Mesh _cached;

        /// <summary>길이 1의 폴백 조각 메시. 캐시되며 에셋으로 저장되지 않는다.</summary>
        public static Mesh SharedMesh
        {
            get
            {
                if (_cached != null)
                    return _cached;

                _cached = Build();
                return _cached;
            }
        }

        static Mesh Build()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();

            AppendSides(vertices, normals, uv, triangles);
            AppendEndCap(vertices, normals, uv, triangles, 1f, true);
            AppendEndCap(vertices, normals, uv, triangles, 0f, false);

            var mesh = new Mesh();
            mesh.name = "CurbBuiltinProfile";
            mesh.hideFlags = HideFlags.DontSave;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        static void AppendSides(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, List<int> triangles)
        {
            float uCursor = 0f;

            for (int i = 0; i < Section.Length; i++)
            {
                Vector2 a = Section[i];
                Vector2 b = Section[(i + 1) % Section.Length];

                Vector2 edge = b - a;
                float edgeLength = edge.magnitude;

                if (edgeLength < 0.0001f)
                    continue;

                // 압출 방향(+Z)과 단면 엣지의 외적이 바깥 방향 노멀이 된다.
                var normal = new Vector3(edge.y, -edge.x, 0f).normalized;

                int baseIndex = vertices.Count;

                vertices.Add(new Vector3(a.x, a.y, 0f));
                vertices.Add(new Vector3(b.x, b.y, 0f));
                vertices.Add(new Vector3(b.x, b.y, 1f));
                vertices.Add(new Vector3(a.x, a.y, 1f));

                for (int n = 0; n < 4; n++)
                    normals.Add(normal);

                uv.Add(new Vector2(uCursor, 0f));
                uv.Add(new Vector2(uCursor + edgeLength, 0f));
                uv.Add(new Vector2(uCursor + edgeLength, 1f));
                uv.Add(new Vector2(uCursor, 1f));

                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 3);

                uCursor += edgeLength;
            }
        }

        static void AppendEndCap(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, List<int> triangles,
            float z, bool facingForward)
        {
            int baseIndex = vertices.Count;

            var normal = new Vector3(0f, 0f, -1f);

            if (facingForward)
                normal = new Vector3(0f, 0f, 1f);

            for (int i = 0; i < Section.Length; i++)
            {
                vertices.Add(new Vector3(Section[i].x, Section[i].y, z));
                normals.Add(normal);
                uv.Add(Section[i]);
            }

            // 단면이 반시계 방향이므로 그대로 팬 삼각화하면 +Z를 향한다.
            for (int i = 1; i < Section.Length - 1; i++)
            {
                if (facingForward)
                {
                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex + i);
                    triangles.Add(baseIndex + i + 1);
                    continue;
                }

                triangles.Add(baseIndex);
                triangles.Add(baseIndex + i + 1);
                triangles.Add(baseIndex + i);
            }
        }
    }
}
