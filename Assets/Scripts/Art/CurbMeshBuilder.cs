using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rush.Art
{
    /// <summary>
    /// CurbPath 설정으로 경로를 따라 조각 메시를 나인슬라이스처럼 배치해 하나의 메시로 합친다.
    /// 시작 캡 - 중간 반복 - 끝 캡 순서로 채우고, 각 조각의 버텍스를 경로에 휘어 붙여 코너에서 틈이 벌어지지 않게 한다.
    /// 에디트 타임 베이크용 순수 로직이며 스스로 실행되는 부분은 없다.
    /// </summary>
    public static class CurbMeshBuilder
    {
        const float Epsilon = 0.0001f;

        /// <summary>베이크 결과 요약. 에디터 표시용.</summary>
        public struct BuildStats
        {
            public float pathLength;
            public int pieceCount;
            public int vertexCount;
            public int triangleCount;
        }

        /// <summary>
        /// path 설정으로 target 메시를 채운다. target은 호출자가 소유한다.
        /// </summary>
        public static bool Build(CurbPath path, Mesh target, out BuildStats stats, out string message)
        {
            stats = default;
            message = string.Empty;

            if (path == null)
            {
                message = "CurbPath가 없다.";
                return false;
            }

            if (target == null)
            {
                message = "대상 메시가 없다.";
                return false;
            }

            var rawPoints = CollectPoints(path);

            if (rawPoints.Count < 2)
            {
                message = "경로 포인트가 2개 미만이다.";
                return false;
            }

            bool closed = path.IsClosed;
            var polyline = new Polyline(Smooth(rawPoints, path.smoothIterations, closed), closed);

            if (polyline.Length < Epsilon)
            {
                message = "경로 길이가 0이다.";
                return false;
            }

            float scale = Mathf.Max(Epsilon, path.pieceScale);

            Mesh middleMesh = path.middle;

            if (middleMesh == null)
                middleMesh = CurbBuiltinProfile.SharedMesh;

            var middlePiece = SourcePiece.Create(middleMesh, path.forwardAxis, path.flipForward);

            if (middlePiece == null)
            {
                message = "중간 조각 메시를 읽을 수 없다.";
                return false;
            }

            SourcePiece startPiece = null;
            SourcePiece endPiece = null;

            // 닫힌 경로는 마감이 필요 없다.
            if (!closed)
            {
                startPiece = SourcePiece.Create(path.startCap, path.forwardAxis, path.flipForward);
                endPiece = SourcePiece.Create(path.endCap, path.forwardAxis, path.flipForward);
            }

            float startLength = 0f;

            if (startPiece != null)
                startLength = startPiece.length * scale;

            float endLength = 0f;

            if (endPiece != null)
                endLength = endPiece.length * scale;

            // 캡만으로 경로보다 길면 캡을 비율대로 줄인다.
            float capTotal = startLength + endLength;

            if (capTotal > polyline.Length && capTotal > Epsilon)
            {
                float shrink = polyline.Length / capTotal;
                startLength *= shrink;
                endLength *= shrink;
            }

            float available = polyline.Length - startLength - endLength;

            float middleNominal = middlePiece.length * scale;

            if (path.middleLengthOverride > Epsilon)
                middleNominal = path.middleLengthOverride;

            if (middleNominal < Epsilon)
            {
                message = "중간 조각의 진행축 길이가 0이다. 진행축(forwardAxis) 설정을 확인하라.";
                return false;
            }

            int middleCount = 0;

            if (available > Epsilon)
                middleCount = Mathf.Max(1, Mathf.RoundToInt(available / middleNominal));

            bool clampedCount = false;

            if (path.maxPieces > 0 && middleCount > path.maxPieces)
            {
                middleCount = path.maxPieces;
                clampedCount = true;
            }

            int subMeshCount = Mathf.Max(1, middlePiece.SubMeshCount);

            if (startPiece != null)
                subMeshCount = Mathf.Max(subMeshCount, startPiece.SubMeshCount);

            if (endPiece != null)
                subMeshCount = Mathf.Max(subMeshCount, endPiece.SubMeshCount);

            var accumulator = new MeshAccumulator(subMeshCount);
            accumulator.useColors = UseColors(startPiece, middlePiece, endPiece);

            float cursor = 0f;

            if (startPiece != null && startLength > Epsilon)
            {
                accumulator.Append(startPiece, polyline, cursor, startLength, scale, path.sectionOffset);
                cursor += startLength;
            }

            AppendMiddles(accumulator, middlePiece, polyline, cursor, available, middleNominal, middleCount, path, scale);

            if (endPiece != null && endLength > Epsilon)
                accumulator.Append(endPiece, polyline, polyline.Length - endLength, endLength, scale, path.sectionOffset);

            if (accumulator.vertices.Count == 0)
            {
                message = "생성된 버텍스가 없다.";
                return false;
            }

            bool nonTriangle = HasNonTriangleSubmesh(startPiece, middlePiece, endPiece);

            if (accumulator.TriangleCount == 0)
            {
                if (nonTriangle)
                {
                    message = "조각 메시가 삼각형이 아니다. FBX를 삼각형(Triangulate)으로 내보내라.";
                    return false;
                }

                message = "조각 메시에 삼각형이 없다.";
                return false;
            }

            accumulator.Write(target);

            stats.pathLength = polyline.Length;
            stats.pieceCount = accumulator.pieceCount;
            stats.vertexCount = accumulator.vertices.Count;
            stats.triangleCount = accumulator.TriangleCount;

            if (clampedCount)
            {
                message = $"중간 조각 수가 상한({path.maxPieces})에 걸려 늘어난 상태다. maxPieces를 올리거나 조각 길이를 키워라.";
                return true;
            }

            if (nonTriangle)
            {
                message = "삼각형이 아닌 서브메시를 건너뛰었다. FBX를 삼각형으로 내보내라.";
                return true;
            }

            return true;
        }

        static bool HasNonTriangleSubmesh(SourcePiece start, SourcePiece middle, SourcePiece end)
        {
            if (middle != null && middle.hasNonTriangleSubmesh)
                return true;

            if (start != null && start.hasNonTriangleSubmesh)
                return true;

            if (end != null && end.hasNonTriangleSubmesh)
                return true;

            return false;
        }

        static void AppendMiddles(MeshAccumulator accumulator, SourcePiece piece, Polyline polyline, float cursor,
            float available, float nominal, int count, CurbPath path, float scale)
        {
            if (count <= 0)
                return;

            // 전체를 균등하게 늘려 맞춘다.
            if (path.fitMode == CurbFitMode.StretchAll)
            {
                float each = available / count;

                for (int i = 0; i < count; i++)
                {
                    accumulator.Append(piece, polyline, cursor, each, scale, path.sectionOffset);
                    cursor += each;
                }

                return;
            }

            // 마지막 하나만 남는 길이를 흡수한다.
            for (int i = 0; i < count - 1; i++)
            {
                accumulator.Append(piece, polyline, cursor, nominal, scale, path.sectionOffset);
                cursor += nominal;
            }

            float lastLength = available - nominal * (count - 1);
            accumulator.Append(piece, polyline, cursor, Mathf.Max(Epsilon, lastLength), scale, path.sectionOffset);
        }

        static bool UseColors(SourcePiece start, SourcePiece middle, SourcePiece end)
        {
            if (middle != null && middle.HasColors)
                return true;

            if (start != null && start.HasColors)
                return true;

            if (end != null && end.HasColors)
                return true;

            return false;
        }

        static List<Vector3> CollectPoints(CurbPath path)
        {
            int count = path.PointCount;
            var result = new List<Vector3>(count);

            for (int i = 0; i < count; i++)
            {
                Vector3 point = path.GetPoint(i);

                // 같은 자리에 겹친 포인트는 접선 계산을 망치므로 버린다.
                if (result.Count > 0 && Vector3.Distance(result[result.Count - 1], point) < Epsilon)
                    continue;

                result.Add(point);
            }

            // 닫힌 경로에서 마지막 점이 첫 점과 겹치면 이음새에 0길이 구간이 생긴다.
            if (path.IsClosed && result.Count > 2 && Vector3.Distance(result[0], result[result.Count - 1]) < Epsilon)
                result.RemoveAt(result.Count - 1);

            return result;
        }

        /// <summary>Chaikin 방식으로 코너를 둥글게 만든다. 열린 경로는 양 끝점을 유지한다.</summary>
        static List<Vector3> Smooth(List<Vector3> source, int iterations, bool closed)
        {
            var current = source;

            for (int pass = 0; pass < iterations; pass++)
            {
                if (current.Count < 3)
                    return current;

                var next = new List<Vector3>(current.Count * 2);

                if (!closed)
                    next.Add(current[0]);

                int segmentCount = current.Count;

                if (!closed)
                    segmentCount = current.Count - 1;

                for (int i = 0; i < segmentCount; i++)
                {
                    Vector3 a = current[i];
                    Vector3 b = current[(i + 1) % current.Count];

                    next.Add(Vector3.Lerp(a, b, 0.25f));
                    next.Add(Vector3.Lerp(a, b, 0.75f));
                }

                if (!closed)
                    next.Add(current[current.Count - 1]);

                current = next;
            }

            return current;
        }

        static void Decompose(CurbForwardAxis axis, bool flip, Vector3 value, out float along, out Vector2 lateral)
        {
            switch (axis)
            {
                case CurbForwardAxis.X:
                    along = value.x;
                    lateral = new Vector2(-value.z, value.y);
                    break;

                // z를 뒤집지 않으면 반사 변환이 되어 삼각형 와인딩이 뒤집힌다.
                case CurbForwardAxis.Y:
                    along = value.y;
                    lateral = new Vector2(value.x, -value.z);
                    break;

                default:
                    along = value.z;
                    lateral = new Vector2(value.x, value.y);
                    break;
            }

            if (!flip)
                return;

            // 진행축을 뒤집을 때 좌우도 같이 뒤집어야 면이 뒤집히지 않는다.
            along = -along;
            lateral.x = -lateral.x;
        }

        /// <summary>
        /// 조각 메시를 진행축 기준으로 미리 분해해 둔 캐시. 같은 조각을 여러 번 배치할 때 재사용한다.
        /// </summary>
        sealed class SourcePiece
        {
            public float[] alongNormalized;
            public Vector2[] lateral;
            public Vector3[] frameNormals;
            public Vector2[] uv;
            public Color32[] colors;
            public int[][] submeshes;
            public float length;
            public bool hasNormals;
            public bool hasNonTriangleSubmesh;

            public int VertexCount
            {
                get { return lateral.Length; }
            }

            public int SubMeshCount
            {
                get { return submeshes.Length; }
            }

            public bool HasColors
            {
                get { return colors != null; }
            }

            public static SourcePiece Create(Mesh mesh, CurbForwardAxis axis, bool flip)
            {
                if (mesh == null)
                    return null;

                var vertices = mesh.vertices;

                if (vertices == null || vertices.Length == 0)
                    return null;

                int count = vertices.Length;
                var along = new float[count];
                var lateral = new Vector2[count];

                float min = float.MaxValue;
                float max = float.MinValue;

                for (int i = 0; i < count; i++)
                {
                    Decompose(axis, flip, vertices[i], out along[i], out lateral[i]);

                    if (along[i] < min)
                        min = along[i];

                    if (along[i] > max)
                        max = along[i];
                }

                var piece = new SourcePiece();
                piece.length = max - min;
                piece.lateral = lateral;
                piece.alongNormalized = new float[count];

                for (int i = 0; i < count; i++)
                {
                    if (piece.length < Epsilon)
                    {
                        piece.alongNormalized[i] = 0f;
                        continue;
                    }

                    piece.alongNormalized[i] = (along[i] - min) / piece.length;
                }

                piece.frameNormals = BuildFrameNormals(mesh, axis, flip, count, out piece.hasNormals);
                piece.uv = BuildUv(mesh, count);
                piece.colors = BuildColors(mesh, count);
                piece.submeshes = BuildSubmeshes(mesh, out piece.hasNonTriangleSubmesh);

                return piece;
            }

            public Color32 ColorAt(int index)
            {
                if (colors == null)
                    return new Color32(255, 255, 255, 255);

                return colors[index];
            }

            static Vector3[] BuildFrameNormals(Mesh mesh, CurbForwardAxis axis, bool flip, int count, out bool hasNormals)
            {
                var source = mesh.normals;
                var result = new Vector3[count];

                if (source == null || source.Length != count)
                {
                    hasNormals = false;

                    for (int i = 0; i < count; i++)
                        result[i] = Vector3.up;

                    return result;
                }

                hasNormals = true;

                for (int i = 0; i < count; i++)
                {
                    Decompose(axis, flip, source[i], out float normalAlong, out Vector2 normalLateral);
                    result[i] = new Vector3(normalLateral.x, normalLateral.y, normalAlong);
                }

                return result;
            }

            static Vector2[] BuildUv(Mesh mesh, int count)
            {
                var source = mesh.uv;

                if (source != null && source.Length == count)
                    return source;

                return new Vector2[count];
            }

            static Color32[] BuildColors(Mesh mesh, int count)
            {
                var source = mesh.colors32;

                if (source != null && source.Length == count)
                    return source;

                return null;
            }

            static int[][] BuildSubmeshes(Mesh mesh, out bool skippedNonTriangle)
            {
                skippedNonTriangle = false;

                int subCount = Mathf.Max(1, mesh.subMeshCount);
                var result = new int[subCount][];

                for (int i = 0; i < subCount; i++)
                {
                    // 쿼드로 내보낸 FBX 등 삼각형이 아닌 서브메시는 건너뛴다.
                    if (i >= mesh.subMeshCount || mesh.GetTopology(i) != MeshTopology.Triangles)
                    {
                        result[i] = new int[0];
                        skippedNonTriangle = true;
                        continue;
                    }

                    result[i] = mesh.GetTriangles(i);
                }

                return result;
            }
        }

        /// <summary>조각 인스턴스를 쌓아 최종 메시로 굽는 버퍼.</summary>
        sealed class MeshAccumulator
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Color32> colors = new List<Color32>();
            public readonly List<int>[] submeshes;

            public bool useColors;
            public int pieceCount;

            bool _needsNormalRecalculation;

            public MeshAccumulator(int subMeshCount)
            {
                submeshes = new List<int>[Mathf.Max(1, subMeshCount)];

                for (int i = 0; i < submeshes.Length; i++)
                    submeshes[i] = new List<int>();
            }

            public int TriangleCount
            {
                get
                {
                    int total = 0;

                    for (int i = 0; i < submeshes.Length; i++)
                        total += submeshes[i].Count / 3;

                    return total;
                }
            }

            public void Append(SourcePiece piece, Polyline polyline, float startDistance, float pieceLength,
                float scale, Vector2 offset)
            {
                if (piece == null)
                    return;

                if (!piece.hasNormals)
                    _needsNormalRecalculation = true;

                // 진행축 배율과 단면 배율이 다르므로 노멀의 진행축 성분을 보정한다(역전치 스케일).
                float normalAlongFactor = 1f;

                if (piece.length > Epsilon && pieceLength > Epsilon)
                    normalAlongFactor = scale * piece.length / pieceLength;

                int baseIndex = vertices.Count;

                for (int i = 0; i < piece.VertexCount; i++)
                {
                    float distance = startDistance + piece.alongNormalized[i] * pieceLength;
                    polyline.Frame(distance, out Vector3 origin, out Quaternion rotation);

                    Vector2 section = piece.lateral[i];
                    var local = new Vector3(section.x * scale + offset.x, section.y * scale + offset.y, 0f);

                    Vector3 frameNormal = piece.frameNormals[i];
                    frameNormal.z *= normalAlongFactor;

                    vertices.Add(origin + rotation * local);
                    normals.Add(rotation * frameNormal.normalized);
                    uv.Add(piece.uv[i]);

                    if (useColors)
                        colors.Add(piece.ColorAt(i));
                }

                int subCount = Mathf.Min(piece.SubMeshCount, submeshes.Length);

                for (int sub = 0; sub < subCount; sub++)
                {
                    var source = piece.submeshes[sub];
                    var destination = submeshes[sub];

                    for (int t = 0; t < source.Length; t++)
                        destination.Add(baseIndex + source[t]);
                }

                pieceCount++;
            }

            public void Write(Mesh target)
            {
                target.Clear();

                if (vertices.Count > 65000)
                    target.indexFormat = IndexFormat.UInt32;
                else
                    target.indexFormat = IndexFormat.UInt16;

                target.SetVertices(vertices);
                target.SetNormals(normals);
                target.SetUVs(0, uv);

                if (useColors)
                    target.SetColors(colors);

                target.subMeshCount = submeshes.Length;

                for (int i = 0; i < submeshes.Length; i++)
                    target.SetTriangles(submeshes[i], i, false);

                if (_needsNormalRecalculation)
                    target.RecalculateNormals();

                target.RecalculateBounds();
                target.RecalculateTangents();
            }
        }

        /// <summary>폴리라인을 아크 길이로 샘플링하는 헬퍼.</summary>
        sealed class Polyline
        {
            readonly Vector3[] _points;
            readonly float[] _cumulative;
            readonly bool _closed;

            public float Length { get; private set; }

            public Polyline(List<Vector3> points, bool closed)
            {
                _closed = closed;

                int nodeCount = points.Count;

                // 닫힌 경로는 첫 점을 끝에 복제해 마지막 구간을 만든다.
                if (closed)
                    nodeCount = points.Count + 1;

                _points = new Vector3[nodeCount];

                for (int i = 0; i < points.Count; i++)
                    _points[i] = points[i];

                if (closed)
                    _points[nodeCount - 1] = points[0];

                _cumulative = new float[nodeCount];

                for (int i = 1; i < nodeCount; i++)
                    _cumulative[i] = _cumulative[i - 1] + Vector3.Distance(_points[i - 1], _points[i]);

                Length = _cumulative[nodeCount - 1];
            }

            public Vector3 SamplePosition(float distance)
            {
                if (_closed && Length > Epsilon)
                    distance = Mathf.Repeat(distance, Length);

                distance = Mathf.Clamp(distance, 0f, Length);

                int low = 0;
                int high = _cumulative.Length - 1;

                while (high - low > 1)
                {
                    int mid = (low + high) / 2;

                    if (_cumulative[mid] <= distance)
                    {
                        low = mid;
                        continue;
                    }

                    high = mid;
                }

                float segmentLength = _cumulative[high] - _cumulative[low];

                if (segmentLength < Epsilon)
                    return _points[low];

                float t = (distance - _cumulative[low]) / segmentLength;
                return Vector3.Lerp(_points[low], _points[high], t);
            }

            /// <summary>distance 지점의 위치와 진행 방향 프레임을 구한다. 롤은 항상 월드 업 기준.</summary>
            public void Frame(float distance, out Vector3 position, out Quaternion rotation)
            {
                position = SamplePosition(distance);

                float step = Mathf.Max(0.005f, Length * 0.002f);
                Vector3 forward = SamplePosition(distance + step) - SamplePosition(distance - step);

                if (forward.sqrMagnitude < 1e-10f)
                    forward = SamplePosition(distance + step * 4f) - position;

                if (forward.sqrMagnitude < 1e-10f)
                    forward = Vector3.forward;

                forward.Normalize();

                Vector3 right = Vector3.Cross(Vector3.up, forward);

                if (right.sqrMagnitude < 1e-6f)
                    right = Vector3.Cross(Vector3.forward, forward);

                right.Normalize();

                Vector3 up = Vector3.Cross(forward, right);
                rotation = Quaternion.LookRotation(forward, up);
            }
        }
    }
}
