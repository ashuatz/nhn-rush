using Rush.Data;
using UnityEngine;

namespace Rush.Stage
{
    /// <summary>
    /// 건설 예정 타워의 실루엣(고스트)을 슬롯 위에 보여준다.
    ///
    /// 고스트 오브젝트는 에디터 셋업에서 타워 종류별로 미리 베이크(비활성)해 두고,
    /// 런타임에는 위치 이동과 활성화만 한다 (런타임 생성 회피).
    /// </summary>
    public class BuildGhostPreview : MonoBehaviour
    {
        [SerializeField] TowerType[] _ghostTypes = new TowerType[0];
        [SerializeField] GameObject[] _ghostObjects = new GameObject[0];

        GameObject _active;

        void Awake()
        {
            // 에디터에서 켜 둔 채 저장됐을 수 있으므로 시작 시 전부 끈다
            Hide();
        }

        public void Show(TowerType type, Vector3 position)
        {
            var ghost = FindGhost(type);

            if (ghost == null)
                return;

            // _active만 끄면 에디터 프리뷰 등 다른 경로로 켜진 고스트가 남는다.
            // 개수가 타워 종류 수뿐이라 매번 전부 정리하는 편이 안전하다
            HideAllExcept(ghost);

            ghost.transform.position = position;
            ghost.SetActive(true);

            _active = ghost;
        }

        public void Hide()
        {
            HideAllExcept(null);
        }

        /// <summary>keep을 제외한 모든 고스트를 끈다. keep이 null이면 전부 끈다.</summary>
        void HideAllExcept(GameObject keep)
        {
            for (int i = 0; i < _ghostObjects.Length; i++)
            {
                if (_ghostObjects[i] == null)
                    continue;

                if (_ghostObjects[i] == keep)
                    continue;

                _ghostObjects[i].SetActive(false);
            }

            _active = keep;
        }

        GameObject FindGhost(TowerType type)
        {
            int count = Mathf.Min(_ghostTypes.Length, _ghostObjects.Length);

            for (int i = 0; i < count; i++)
            {
                if (_ghostTypes[i] != type)
                    continue;

                return _ghostObjects[i];
            }

            return null;
        }
    }
}
