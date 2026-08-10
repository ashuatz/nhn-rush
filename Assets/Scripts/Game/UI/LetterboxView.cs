using Rush.Stage;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Rush.UI
{
    /// <summary>
    /// 16:9 레터박스. 창 비율이 16:9에서 벗어나면 카메라 뷰포트를 16:9로 잘라 맵 바깥이 드러나지 않게 하고,
    /// 남는 띠를 게임 화면을 흐리게 만든 이미지로 덮는다.
    ///
    /// 카메라를 렌더텍스처로 빼지 않고 camera.rect를 쓰는 이유:
    /// BuildMenu의 슬롯 클릭(ScreenPointToRay)과 MonsterHealthOverlay(WorldToScreenPoint)가 화면 좌표를
    /// 그대로 쓰는데, rect 방식은 Unity가 그 좌표계까지 같이 잘라줘 두 경로를 건드릴 필요가 없다.
    ///
    /// 띠를 "전체를 깔고 가운데를 뚫는" 방식으로 만들지 않는 이유:
    /// UI Toolkit 패널은 항상 3D 위에 그려지므로 전체를 깔면 게임 화면을 덮어버린다. 띠 영역만 덮는다.
    /// (URP는 rect 밖을 지우지 않아 이전 프레임이 남는데, 이 띠가 그걸 가리는 역할도 한다.)
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class LetterboxView : MonoBehaviour
    {
        /// <summary>이 정도 비율 차이는 레터박스 없이 그냥 채운다.</summary>
        const float AspectEpsilon = 0.002f;

        [SerializeField] Camera _camera;

        /// <summary>띠 배경을 채울 저해상도 렌더 카메라. 메인 카메라 자식으로 에디터에서 미리 만들어 둔다.</summary>
        [SerializeField] Camera _backdropCamera;

        [SerializeField] float _targetAspect = 16f / 9f;

        /// <summary>배경 렌더 높이(픽셀). 낮출수록 더 뭉개진다 - 다운샘플 후 확대가 곧 블러다.</summary>
        [SerializeField] int _backdropHeight = 180;

        /// <summary>띠를 어둡게 하는 정도. 플레이 영역이 더 또렷하게 읽힌다.</summary>
        [SerializeField, Range(0f, 1f)] float _backdropDim = 0.35f;

        /// <summary>플레이 영역 경계 바깥에 깔리는 음영의 두께(패널 픽셀). AO처럼 경계에 붙어 번진다.</summary>
        [SerializeField] float _edgeShadeWidth = 34f;

        /// <summary>경계 음영의 진하기. 플레이 영역이 한 장 떠 있는 것처럼 보이게 만드는 값.</summary>
        [SerializeField, Range(0f, 1f)] float _edgeShadeStrength = 0.55f;

        /// <summary>음영이 붙는 방향. 띠에서 플레이 영역과 맞닿은 변을 가리킨다.</summary>
        enum ShadeEdge
        {
            Left,
            Right,
            Top,
            Bottom,
        }

        RenderTexture _backdrop;

        /// <summary>경계 음영에 쓰는 알파 그라디언트. 가로 띠용/세로 띠용을 따로 만든다.</summary>
        Texture2D _rampHorizontal;
        Texture2D _rampVertical;

        VisualElement _root;

        /// <summary>좌(또는 상) 띠와 우(또는 하) 띠. 각각 잘라내는 상자이고 안에 전체화면 크기 이미지를 담는다.</summary>
        VisualElement _barA;
        VisualElement _barB;
        VisualElement _imageA;
        VisualElement _imageB;
        VisualElement _shadeA;
        VisualElement _shadeB;

        int _appliedWidth;
        int _appliedHeight;

        void OnEnable()
        {
            if (_camera == null)
                _camera = Camera.main;

            BuildUI(GetComponent<UIDocument>().rootVisualElement);

            // 레이아웃이 잡힌 뒤에야 패널 크기를 알 수 있다. 창 크기가 바뀔 때도 다시 온다.
            // 문서 루트는 이 컴포넌트보다 오래 살기 때문에 익명 람다로 걸면 켜고 끌 때마다 콜백이 쌓인다.
            _root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

            Apply(force: true);
        }

        void OnRootGeometryChanged(GeometryChangedEvent evt)
        {
            Apply(force: true);
        }

        void OnDisable()
        {
            // 뷰포트를 되돌리지 않으면 컴포넌트를 꺼도 화면이 잘린 채 남는다
            if (_camera != null)
                _camera.rect = new Rect(0f, 0f, 1f, 1f);

            if (_backdropCamera != null)
            {
                _backdropCamera.targetTexture = null;
                _backdropCamera.enabled = false;
            }

            ReleaseBackdrop();
            ReleaseRamps();

            if (_root != null)
            {
                _root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

                _barA.RemoveFromHierarchy();
                _barB.RemoveFromHierarchy();
                _root = null;
            }

            _appliedWidth = 0;
            _appliedHeight = 0;
        }

        void Update()
        {
            Apply(force: false);
            SyncBackdropCamera();
        }

        /// <summary>
        /// 띠가 보이는 동안 메인 카메라의 화각/컬링을 계속 따라간다.
        /// 해상도가 바뀔 때만 맞추면 런타임에 화각이 바뀐 경우 띠 영상만 이전 설정으로 남는다.
        /// (위치/회전은 배경 카메라가 메인 카메라의 자식이라 저절로 따라온다.)
        /// </summary>
        void SyncBackdropCamera()
        {
            if (_camera == null || _backdropCamera == null)
                return;

            if (!_backdropCamera.enabled)
                return;

            _backdropCamera.orthographic = _camera.orthographic;
            _backdropCamera.orthographicSize = _camera.orthographicSize;
            _backdropCamera.fieldOfView = _camera.fieldOfView;
            _backdropCamera.nearClipPlane = _camera.nearClipPlane;
            _backdropCamera.farClipPlane = _camera.farClipPlane;
            _backdropCamera.cullingMask = _camera.cullingMask;
            _backdropCamera.clearFlags = _camera.clearFlags;
            _backdropCamera.backgroundColor = _camera.backgroundColor;
        }

        void Apply(bool force)
        {
            if (_camera == null || _root == null)
                return;

            if (!force && Screen.width == _appliedWidth && Screen.height == _appliedHeight)
                return;

            // 화면이나 패널 크기가 아직 안 잡힌 프레임은 "적용했다"고 기록하지 않는다.
            // 기록해 버리면 뷰포트만 잘린 채 띠 배치가 실패한 상태로 굳어, 지워지지 않은 rect 밖이 그대로 보인다.
            if (Screen.width <= 0 || Screen.height <= 0
                || _root.resolvedStyle.width <= 0f || _root.resolvedStyle.height <= 0f)
            {
                _camera.rect = new Rect(0f, 0f, 1f, 1f);
                HideBars();
                return;
            }

            _appliedWidth = Screen.width;
            _appliedHeight = Screen.height;

            float actual = (float)Screen.width / Screen.height;

            // 비율이 사실상 같으면 띠도 배경 렌더도 필요 없다
            if (Mathf.Abs(actual - _targetAspect) <= AspectEpsilon)
            {
                _camera.rect = new Rect(0f, 0f, 1f, 1f);
                HideBars();
                return;
            }

            if (actual > _targetAspect)
                ApplyWide(actual);
            else
                ApplyTall(actual);

            EnsureBackdrop();
        }

        /// <summary>창이 16:9보다 넓다 - 좌우에 띠가 생긴다.</summary>
        void ApplyWide(float actual)
        {
            float width = _targetAspect / actual;
            float x = (1f - width) * 0.5f;

            _camera.rect = new Rect(x, 0f, width, 1f);

            // 왼쪽 띠는 오른쪽 변이, 오른쪽 띠는 왼쪽 변이 플레이 영역과 맞닿는다
            PlaceBar(_barA, _imageA, _shadeA, 0f, 0f, x, 1f, ShadeEdge.Right);
            PlaceBar(_barB, _imageB, _shadeB, x + width, 0f, x, 1f, ShadeEdge.Left);
        }

        /// <summary>창이 16:9보다 좁다(길쭉하다) - 위아래에 띠가 생긴다.</summary>
        void ApplyTall(float actual)
        {
            float height = actual / _targetAspect;
            float y = (1f - height) * 0.5f;

            _camera.rect = new Rect(0f, y, 1f, height);

            // 위쪽 띠는 아래 변이, 아래쪽 띠는 위 변이 플레이 영역과 맞닿는다
            PlaceBar(_barA, _imageA, _shadeA, 0f, 0f, 1f, y, ShadeEdge.Bottom);
            PlaceBar(_barB, _imageB, _shadeB, 0f, y + height, 1f, y, ShadeEdge.Top);
        }

        /// <summary>
        /// 띠 하나를 화면 비율(0~1) 좌표로 배치한다.
        /// 안쪽 이미지는 전체화면 크기로 두고 띠만큼 밀어 넣는다. 그래야 두 띠가 한 장의 흐린 화면처럼 이어진다.
        ///
        /// 패널 좌표는 화면 픽셀과 배율이 다를 수 있어(PanelSettings 스케일) 루트 크기를 기준으로 환산한다.
        /// </summary>
        void PlaceBar(VisualElement bar, VisualElement image, VisualElement shade,
            float nx, float ny, float nw, float nh, ShadeEdge edge)
        {
            if (nw <= 0f || nh <= 0f)
            {
                bar.style.display = DisplayStyle.None;
                return;
            }

            float panelWidth = _root.resolvedStyle.width;
            float panelHeight = _root.resolvedStyle.height;

            if (panelWidth <= 0f || panelHeight <= 0f)
                return;

            float left = nx * panelWidth;
            float top = ny * panelHeight;
            float barWidth = nw * panelWidth;
            float barHeight = nh * panelHeight;

            bar.style.display = DisplayStyle.Flex;
            bar.style.left = left;
            bar.style.top = top;
            bar.style.width = barWidth;
            bar.style.height = barHeight;

            image.style.width = panelWidth;
            image.style.height = panelHeight;
            image.style.left = -left;
            image.style.top = -top;

            PlaceShade(shade, barWidth, barHeight, edge);
        }

        /// <summary>
        /// 경계 음영을 띠 안쪽 변에 붙인다. 띠보다 두꺼운 음영은 요구하지 않는다.
        /// 그라디언트는 짙은 쪽이 텍스처 시작점이라, 반대 방향으로 붙일 때는 축을 뒤집어 쓴다.
        /// </summary>
        void PlaceShade(VisualElement shade, float barWidth, float barHeight, ShadeEdge edge)
        {
            if (_edgeShadeStrength <= 0f || _edgeShadeWidth <= 0f)
            {
                shade.style.display = DisplayStyle.None;
                return;
            }

            shade.style.display = DisplayStyle.Flex;
            shade.style.scale = new Scale(Vector2.one);

            if (edge == ShadeEdge.Right)
            {
                float thickness = Mathf.Min(_edgeShadeWidth, barWidth);

                shade.style.backgroundImage = Background.FromTexture2D(_rampHorizontal);
                shade.style.left = barWidth - thickness;
                shade.style.top = 0f;
                shade.style.width = thickness;
                shade.style.height = barHeight;

                // 짙은 쪽이 왼쪽인 램프라 좌우를 뒤집어 경계에 붙인다
                shade.style.scale = new Scale(new Vector2(-1f, 1f));
                return;
            }

            if (edge == ShadeEdge.Left)
            {
                float thickness = Mathf.Min(_edgeShadeWidth, barWidth);

                shade.style.backgroundImage = Background.FromTexture2D(_rampHorizontal);
                shade.style.left = 0f;
                shade.style.top = 0f;
                shade.style.width = thickness;
                shade.style.height = barHeight;
                return;
            }

            if (edge == ShadeEdge.Bottom)
            {
                float thickness = Mathf.Min(_edgeShadeWidth, barHeight);

                shade.style.backgroundImage = Background.FromTexture2D(_rampVertical);
                shade.style.left = 0f;
                shade.style.top = barHeight - thickness;
                shade.style.width = barWidth;
                shade.style.height = thickness;

                shade.style.scale = new Scale(new Vector2(1f, -1f));
                return;
            }

            float topThickness = Mathf.Min(_edgeShadeWidth, barHeight);

            shade.style.backgroundImage = Background.FromTexture2D(_rampVertical);
            shade.style.left = 0f;
            shade.style.top = 0f;
            shade.style.width = barWidth;
            shade.style.height = topThickness;
        }

        /// <summary>
        /// 경계 음영용 알파 램프. 짙은 쪽이 시작점(가로는 왼쪽, 세로는 위)이고 바깥으로 갈수록 사라진다.
        /// 제곱 감쇠를 쓰면 선형보다 경계에 붙어 번져 AO처럼 읽힌다.
        /// </summary>
        void EnsureRamps()
        {
            if (_rampHorizontal != null && _rampVertical != null)
                return;

            const int Steps = 64;

            var pixels = new Color[Steps];

            for (int i = 0; i < Steps; i++)
            {
                float t = i / (float)(Steps - 1);
                float falloff = (1f - t) * (1f - t);

                pixels[i] = new Color(1f, 1f, 1f, falloff);
            }

            _rampHorizontal = CreateRamp(Steps, 1, pixels, "LetterboxShadeH");

            // 세로 램프는 텍스처 아래쪽이 v=0 이라, 위가 짙게 보이도록 순서를 뒤집어 채운다
            var flipped = new Color[Steps];

            for (int i = 0; i < Steps; i++)
                flipped[i] = pixels[Steps - 1 - i];

            _rampVertical = CreateRamp(1, Steps, flipped, "LetterboxShadeV");

            var shadeColor = new Color(0f, 0f, 0f, _edgeShadeStrength);
            _shadeA.style.unityBackgroundImageTintColor = shadeColor;
            _shadeB.style.unityBackgroundImageTintColor = shadeColor;
        }

        static Texture2D CreateRamp(int width, int height, Color[] pixels, string name)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels(pixels);
            texture.Apply();

            return texture;
        }

        void ReleaseRamps()
        {
            if (_rampHorizontal != null)
            {
                Destroy(_rampHorizontal);
                _rampHorizontal = null;
            }

            if (_rampVertical != null)
            {
                Destroy(_rampVertical);
                _rampVertical = null;
            }
        }

        void HideBars()
        {
            _barA.style.display = DisplayStyle.None;
            _barB.style.display = DisplayStyle.None;

            if (_backdropCamera != null)
                _backdropCamera.enabled = false;
        }

        /// <summary>띠가 보일 때만 배경 카메라를 돌린다. 16:9 창에서는 공짜다.</summary>
        void EnsureBackdrop()
        {
            // 오지정 방어. 메인 카메라를 여기에 넣으면 메인의 targetTexture가 RT로 바뀌어 화면이 아예 안 나온다.
            if (_backdropCamera != null && _backdropCamera == _camera)
            {
                GameLog.Warn("UI", "LetterboxView: 배경 카메라 칸에 메인 카메라가 지정됨 - 흐린 배경을 끈다");
                _backdropCamera = null;
            }

            if (_backdropCamera == null)
            {
                // 카메라가 없거나 도중에 파괴되면 마지막 프레임이 얼어붙는다. 검은 띠로 되돌린다.
                _imageA.style.backgroundImage = StyleKeyword.None;
                _imageB.style.backgroundImage = StyleKeyword.None;

                ReleaseBackdrop();
                return;
            }

            int height = Mathf.Max(16, _backdropHeight);
            int width = Mathf.Max(16, Mathf.RoundToInt(height * _targetAspect));

            if (_backdrop == null || _backdrop.width != width || _backdrop.height != height)
            {
                ReleaseBackdrop();

                _backdrop = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
                _backdrop.name = "LetterboxBackdrop";

                // 확대할 때 부드럽게 번지도록. 이 보간이 블러 역할을 한다.
                _backdrop.filterMode = FilterMode.Bilinear;
                _backdrop.wrapMode = TextureWrapMode.Clamp;
                _backdrop.Create();

                _imageA.style.backgroundImage = Background.FromRenderTexture(_backdrop);
                _imageB.style.backgroundImage = Background.FromRenderTexture(_backdrop);
            }

            // 메인 카메라와 같은 화각/설정을 유지한다. rect와 대상만 따로 잡는다.
            _backdropCamera.CopyFrom(_camera);
            _backdropCamera.rect = new Rect(0f, 0f, 1f, 1f);
            _backdropCamera.targetTexture = _backdrop;
            _backdropCamera.depth = _camera.depth - 1f;

            // URP 추가 데이터는 CopyFrom 대상이 아니다. Overlay로 잡혀 있으면 RT 단독 렌더가 되지 않는다.
            var urp = _backdropCamera.GetComponent<UniversalAdditionalCameraData>();

            if (urp != null)
            {
                urp.renderType = CameraRenderType.Base;
                urp.renderPostProcessing = false;
            }

            _backdropCamera.enabled = true;

            var tint = new Color(1f - _backdropDim, 1f - _backdropDim, 1f - _backdropDim, 1f);
            _imageA.style.unityBackgroundImageTintColor = tint;
            _imageB.style.unityBackgroundImageTintColor = tint;
        }

        void ReleaseBackdrop()
        {
            if (_backdrop == null)
                return;

            if (_backdropCamera != null && _backdropCamera.targetTexture == _backdrop)
                _backdropCamera.targetTexture = null;

            _backdrop.Release();
            Destroy(_backdrop);
            _backdrop = null;
        }

        void BuildUI(VisualElement root)
        {
            _root = root;

            _barA = CreateBar(out _imageA, out _shadeA);
            _barB = CreateBar(out _imageB, out _shadeB);

            // 띠는 HUD보다 뒤에 있어야 한다. 맨 앞에 끼워 넣으면 나중에 추가된 HUD가 자연히 위로 온다.
            root.Insert(0, _barA);
            root.Insert(1, _barB);

            EnsureRamps();
        }

        static VisualElement CreateBar(out VisualElement image, out VisualElement shade)
        {
            var bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.display = DisplayStyle.None;

            // 안쪽 이미지가 전체화면 크기라 띠 밖으로 나간다. 잘라내야 가운데 게임 화면을 덮지 않는다.
            bar.style.overflow = Overflow.Hidden;

            // 배경 띠는 클릭을 먹지 않는다. 뒤쪽 UI/월드 클릭 판정을 방해하면 안 된다.
            bar.pickingMode = PickingMode.Ignore;

            // 배경 렌더가 아직 없을 때 이전 프레임이 비쳐 보이지 않도록 깔아 두는 색
            bar.style.backgroundColor = Color.black;

            image = new VisualElement();
            image.style.position = Position.Absolute;
            image.pickingMode = PickingMode.Ignore;
            bar.Add(image);

            // 경계 음영은 흐린 배경 위에 얹힌다. 배경보다 뒤에 두면 가려진다.
            shade = new VisualElement();
            shade.style.position = Position.Absolute;
            shade.pickingMode = PickingMode.Ignore;
            bar.Add(shade);

            return bar;
        }
    }
}
