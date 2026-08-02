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
        const float RangeIndicatorThickness = 0.01f;

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

            HideRange();
        }

        public void SetSelected(bool selected)
        {
            if (_selectionRing != null)
                _selectionRing.SetActive(selected);

            if (!selected)
                HideRange();
        }

        /// <summary>사거리 원판을 표시한다. 실린더 기본 지름이 1이므로 지름값을 그대로 스케일에 넣는다.</summary>
        public void ShowRange(float radius)
        {
            if (_rangeIndicator == null)
                return;

            if (radius <= 0f)
            {
                HideRange();
                return;
            }

            _rangeIndicator.localScale = new Vector3(radius * 2f, RangeIndicatorThickness, radius * 2f);
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
