using System.Collections.Generic;
using UnityEngine;

namespace Rush.Art
{
    /// <summary>
    /// 조각 메시의 어느 로컬 축이 경로 진행 방향인지.
    /// </summary>
    public enum CurbForwardAxis
    {
        X,
        Y,
        Z,
    }

    /// <summary>
    /// 캡을 제외한 나머지 길이를 중간 조각으로 채우는 방식.
    /// </summary>
    public enum CurbFitMode
    {
        /// <summary>중간 조각 전부를 같은 길이로 늘리거나 줄여 딱 맞춘다.</summary>
        StretchAll,

        /// <summary>중간 조각은 원본 길이를 유지하고 마지막 하나만 늘리거나 줄여 맞춘다.</summary>
        StretchLast,
    }

    /// <summary>
    /// 경로를 따라 연석(curb) 같은 띠 형태 메시를 나인슬라이스처럼 생성하기 위한 설정 컴포넌트.
    /// 경로는 LineRenderer를 지정하면 그 포인트를, 없으면 자체 points 리스트를 사용한다.
    /// 실제 메시 생성은 에디터에서 베이크로 처리하며 런타임에서 자동 실행되는 코드는 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CurbPath : MonoBehaviour
    {
        [Header("경로")]
        [Tooltip("지정하면 이 LineRenderer의 포인트를 경로로 사용한다. 비우면 아래 points를 사용한다.")]
        public LineRenderer sourceLine;

        [Tooltip("LineRenderer를 쓰지 않을 때의 경로 포인트. 이 컴포넌트의 로컬 좌표.")]
        public List<Vector3> points = new List<Vector3>();

        [Tooltip("경로를 닫힌 고리로 처리한다. 닫히면 캡을 넣지 않는다.")]
        public bool closed;

        [Tooltip("코너를 둥글게 깎는 반경(월드 단위). 0이면 꺾인 그대로. 인접한 변 길이의 절반까지만 적용된다.")]
        public float cornerRadius = 0.5f;

        [Header("조각 메시")]
        [Tooltip("경로 시작 마감 조각. 비우면 중간 조각으로 채운다.")]
        public Mesh startCap;

        [Tooltip("반복되는 중간 조각 후보. 둘 이상이면 조각마다 랜덤으로 고른다. 비우면 임시 폴백 단면을 사용한다.")]
        public List<Mesh> middles = new List<Mesh>();

        [Tooltip("경로 끝 마감 조각. 비우면 중간 조각으로 채운다.")]
        public Mesh endCap;

        [Tooltip("조각 메시에서 경로 진행 방향에 해당하는 로컬 축.")]
        public CurbForwardAxis forwardAxis = CurbForwardAxis.Z;

        [Tooltip("진행 방향을 뒤집는다. FBX가 반대로 뽑혔을 때 사용.")]
        public bool flipForward;

        [Tooltip("조각 전체 배율. 단면 크기와 기준 길이에 동시에 적용된다.")]
        public float pieceScale = 1f;

        [Tooltip("단면 오프셋. x는 경로 기준 좌우, y는 높이.")]
        public Vector2 sectionOffset;

        [Tooltip("남는 길이를 중간 조각으로 채우는 방식.")]
        public CurbFitMode fitMode = CurbFitMode.StretchAll;

        [Tooltip("중간 조각 1개의 기준 길이를 월드 단위로 직접 지정한다. pieceScale이 적용되지 않은 최종 길이다. 0이면 메시 바운즈에서 계산.")]
        public float middleLengthOverride;

        [Header("랜덤")]
        [Tooltip("랜덤 시드. 같은 시드면 프리뷰와 베이크 결과가 항상 같다.")]
        public int randomSeed = 12345;

        [Tooltip("조각마다 더할 랜덤 회전의 최대 각도(도). x=피치, y=요, z=롤. 경로 진행 방향 기준이다.")]
        public Vector3 randomRotation;

        [Tooltip("조각마다 곱할 랜덤 배율 범위. x=최소, y=최대. (1,1)이면 랜덤 없음.")]
        public Vector2 randomScaleRange = Vector2.one;

        [Header("출력")]
        [Tooltip("생성 메시를 받을 자식 MeshFilter. 비어 있으면 베이크 시 자동 생성한다.")]
        public MeshFilter output;

        [Tooltip("생성 메시에 적용할 머티리얼. 비우면 베이크 시 원본 FBX에서 가져오려 시도한다.")]
        public Material[] materials;

        [Tooltip("중간 조각 최대 개수 상한. 실수로 거대한 메시가 만들어지는 걸 막는다.")]
        public int maxPieces = 512;

        /// <summary>경로 포인트 개수. LineRenderer가 지정되면 그쪽 기준.</summary>
        public int PointCount
        {
            get
            {
                if (sourceLine != null)
                    return sourceLine.positionCount;

                return Points.Count;
            }
        }

        /// <summary>머티리얼 참조 등에 쓸 대표 중간 조각. 없으면 null.</summary>
        public Mesh FirstMiddle
        {
            get
            {
                if (middles == null)
                    return null;

                for (int i = 0; i < middles.Count; i++)
                {
                    if (middles[i] != null)
                        return middles[i];
                }

                return null;
            }
        }

        /// <summary>직렬화 데이터가 비어 있어도 안전하게 쓰기 위한 접근자.</summary>
        List<Vector3> Points
        {
            get
            {
                if (points == null)
                    points = new List<Vector3>();

                return points;
            }
        }

        /// <summary>닫힌 경로인지. LineRenderer의 loop도 함께 본다.</summary>
        public bool IsClosed
        {
            get
            {
                if (sourceLine != null && sourceLine.loop)
                    return true;

                return closed;
            }
        }

        /// <summary>index번째 포인트를 이 컴포넌트의 로컬 좌표로 반환한다.</summary>
        public Vector3 GetPoint(int index)
        {
            if (sourceLine == null)
                return Points[index];

            Vector3 raw = sourceLine.GetPosition(index);

            if (sourceLine.useWorldSpace)
                return transform.InverseTransformPoint(raw);

            return transform.InverseTransformPoint(sourceLine.transform.TransformPoint(raw));
        }

        /// <summary>index번째 포인트를 로컬 좌표로 설정한다.</summary>
        public void SetPoint(int index, Vector3 localPoint)
        {
            if (sourceLine == null)
            {
                Points[index] = localPoint;
                return;
            }

            sourceLine.SetPosition(index, LocalToLineSpace(localPoint));
        }

        /// <summary>경로 끝에 포인트를 추가한다.</summary>
        public void AddPoint(Vector3 localPoint)
        {
            InsertPoint(PointCount, localPoint);
        }

        /// <summary>index 위치에 포인트를 끼워 넣는다.</summary>
        public void InsertPoint(int index, Vector3 localPoint)
        {
            if (sourceLine == null)
            {
                Points.Insert(Mathf.Clamp(index, 0, Points.Count), localPoint);
                return;
            }

            var buffer = ReadLinePositions();
            buffer.Insert(Mathf.Clamp(index, 0, buffer.Count), LocalToLineSpace(localPoint));
            WriteLinePositions(buffer);
        }

        /// <summary>index번째 포인트를 제거한다.</summary>
        public void RemovePoint(int index)
        {
            if (sourceLine == null)
            {
                if (index < 0 || index >= Points.Count)
                    return;

                Points.RemoveAt(index);
                return;
            }

            var buffer = ReadLinePositions();

            if (index < 0 || index >= buffer.Count)
                return;

            buffer.RemoveAt(index);
            WriteLinePositions(buffer);
        }

        /// <summary>LineRenderer의 포인트를 자체 points 리스트로 복사한다.</summary>
        public void CopyLineToPoints()
        {
            if (sourceLine == null)
                return;

            Points.Clear();

            for (int i = 0; i < sourceLine.positionCount; i++)
                Points.Add(GetPoint(i));
        }

        /// <summary>자체 points 리스트를 LineRenderer에 반영한다.</summary>
        public void ApplyPointsToLine(LineRenderer line)
        {
            if (line == null)
                return;

            line.positionCount = Points.Count;

            for (int i = 0; i < Points.Count; i++)
            {
                Vector3 world = transform.TransformPoint(Points[i]);

                if (line.useWorldSpace)
                {
                    line.SetPosition(i, world);
                    continue;
                }

                line.SetPosition(i, line.transform.InverseTransformPoint(world));
            }
        }

        Vector3 LocalToLineSpace(Vector3 localPoint)
        {
            Vector3 world = transform.TransformPoint(localPoint);

            if (sourceLine.useWorldSpace)
                return world;

            return sourceLine.transform.InverseTransformPoint(world);
        }

        List<Vector3> ReadLinePositions()
        {
            var buffer = new List<Vector3>(sourceLine.positionCount);

            for (int i = 0; i < sourceLine.positionCount; i++)
                buffer.Add(sourceLine.GetPosition(i));

            return buffer;
        }

        void WriteLinePositions(List<Vector3> buffer)
        {
            sourceLine.positionCount = buffer.Count;

            for (int i = 0; i < buffer.Count; i++)
                sourceLine.SetPosition(i, buffer[i]);
        }
    }
}
