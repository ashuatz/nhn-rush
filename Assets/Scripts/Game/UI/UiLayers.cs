using UnityEngine.UIElements;

namespace Rush.UI
{
    /// <summary>
    /// 여러 UI 컴포넌트가 UIDocument 하나를 공유하므로, 무엇을 어디에 붙일지 한 곳에서 정한다.
    ///
    /// 화면 앵커 UI(좌상단 자원바, 우측 패널, 하단 카운트다운 등)는 Content 칸에 넣는다.
    /// 레터박스가 걸리면 LetterboxView가 이 칸을 16:9 안쪽으로 좁혀 UI가 띠 위로 새지 않게 한다.
    ///
    /// 월드 앵커 UI(입구 깃발, HP 바, 슬롯 라디얼)는 문서 루트에 그대로 둔다.
    /// camera.rect 덕분에 월드->화면 좌표가 이미 레터박스 안쪽으로 나오므로,
    /// 좁혀진 칸에 넣으면 그만큼 두 번 밀려 어긋난다.
    /// </summary>
    public static class UiLayers
    {
        public const string ContentName = "letterbox-content";

        /// <summary>
        /// 화면 앵커 UI가 들어가는 칸. 컴포넌트들의 OnEnable 순서를 보장할 수 없어
        /// 이름으로 찾고 없을 때만 만든다 - 누가 먼저 켜져도 같은 칸을 쓴다.
        /// </summary>
        public static VisualElement Content(VisualElement documentRoot)
        {
            if (documentRoot == null)
                return null;

            var existing = documentRoot.Q(ContentName);

            if (existing != null)
                return existing;

            var content = new VisualElement { name = ContentName };

            // left/top + width/height 로만 잡는다. right/bottom 까지 걸어 두면
            // LetterboxView가 크기를 지정할 때 어느 쪽이 이기는지가 헷갈린다.
            content.style.position = Position.Absolute;
            content.style.left = 0;
            content.style.top = 0;
            content.style.width = Length.Percent(100);
            content.style.height = Length.Percent(100);

            // 반드시 Ignore. Position이면 BuildMenu의 panel.Pick이 빈 공간에서도 이 칸에 걸려
            // "UI 위 클릭"으로 판정되고 타워 배치가 아예 안 된다.
            content.pickingMode = PickingMode.Ignore;

            // 안쪽으로 좁혀졌을 때 HUD가 레터박스 띠 위로 삐져나가지 않게 잘라낸다.
            content.style.overflow = Overflow.Hidden;

            documentRoot.Add(content);

            return content;
        }
    }
}
