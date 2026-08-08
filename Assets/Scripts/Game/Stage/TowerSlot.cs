using Rush.Combat;
using UnityEngine;

namespace Rush.Stage
{
    /// <summary>
    /// 타워를 지을 수 있는 고정 위치 1칸. 기획서(코어 룰) 4장.
    /// 클릭 판정은 BuildMenu가 레이캐스트로 처리한다.
    ///
    /// 선택 링과 사거리 표시기는 에디터 셋업에서 자식으로 미리 배치(베이크)하고,
    /// 런타임에는 활성화/스케일만 바꾼다 (런타임 생성 회피).
    /// </summary>
    public class TowerSlot : MonoBehaviour
    {
        const string SelectionRingName = "SelectionRing";
        const string RangeIndicatorName = "RangeIndicator";

        GameObject _selectionRing;
        Transform _rangeIndicator;

        public Tower Occupant { get; set; }

        public bool IsOccupied => Occupant != null;

        /// <summary>타워가 세워질 위치 (슬롯 윗면).</summary>
        public Vector3 BuildPosition
        {
            get
            {
                return transform.position + Vector3.up * 0.25f;
            }
        }

        void Awake()
        {
            var ring = transform.Find(SelectionRingName);

            if (ring != null)
                _selectionRing = ring.gameObject;

            _rangeIndicator = transform.Find(RangeIndicatorName);

            // 에디터에서 프리뷰로 켜 둔 채 저장됐을 수 있으므로 시작 시 둘 다 명시적으로 끈다
            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (_selectionRing != null)
                _selectionRing.SetActive(selected);

            if (!selected)
                HideRange();
        }

        /// <summary>
        /// 사거리를 표시한다. 표시용 구는 셰이더가 씬 뎁스로 지형에 투영하므로 메시 자체는 보이지 않는다.
        /// 유닛 스피어 지름이 1이라 지름값을 그대로 스케일에 넣는다.
        /// </summary>
        public void ShowRange(float radius)
        {
            if (_rangeIndicator == null)
                return;

            if (radius <= 0f)
            {
                HideRange();
                return;
            }

            _rangeIndicator.localScale = new Vector3(1, 0, 1) * (radius * 2f) + Vector3.up;
            _rangeIndicator.gameObject.SetActive(true);
        }

        public void HideRange()
        {
            if (_rangeIndicator == null)
                return;

            _rangeIndicator.gameObject.SetActive(false);
        }
    }
}
