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
            HideAll();
        }

        public void Show(TowerType type, Vector3 position)
        {
            var ghost = FindGhost(type);

            if (ghost == null)
                return;

            if (_active != null && _active != ghost)
                _active.SetActive(false);

            ghost.transform.position = position;
            ghost.SetActive(true);

            _active = ghost;
        }

        public void Hide()
        {
            if (_active == null)
                return;

            _active.SetActive(false);
            _active = null;
        }

        /// <summary>씬에 켜진 채로 저장된 고스트가 있어도 시작 시 전부 끈다.</summary>
        void HideAll()
        {
            for (int i = 0; i < _ghostObjects.Length; i++)
            {
                if (_ghostObjects[i] == null)
                    continue;

                _ghostObjects[i].SetActive(false);
            }

            _active = null;
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
