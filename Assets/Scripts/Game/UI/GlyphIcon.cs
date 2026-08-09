using UnityEngine;
using UnityEngine.UIElements;

namespace Rush.UI
{
    /// <summary>라디얼 메뉴에 쓰는 아이콘 종류. 타워 4계열 + 관리 동작.</summary>
    public enum IconGlyph
    {
        Sword = 0,      // 보병
        Bow = 1,        // 궁수
        Staff = 2,      // 마도
        Bomb = 3,       // 포병
        Upgrade = 4,    // 강화
        Sell = 5,       // 판매
        Pause = 6,      // 일시정지
    }

    /// <summary>
    /// 아이콘을 Painter2D로 직접 그리는 요소. 프로젝트에 UI 스프라이트가 없어
    /// 에셋 없이 벡터로 그린다 (해상도 무관, 아틀라스 불필요).
    ///
    /// 도형은 0~1 설계 좌표로 적고, 종류마다 실제로 차지하는 범위(DesignBounds)가 다르므로
    /// 그 박스를 요소 중앙에 같은 크기로 맞춰 넣는다. 이걸 안 하면 칼은 작고 가운데,
    /// 활은 왼쪽, 폭탄은 오른쪽으로 쏠려 아이콘끼리 따로 논다.
    /// </summary>
    public class GlyphIcon : VisualElement
    {
        IconGlyph _glyph;
        Color _tint = Color.white;

        public GlyphIcon(IconGlyph glyph)
        {
            _glyph = glyph;

            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        public IconGlyph Glyph
        {
            get => _glyph;
            set
            {
                if (_glyph == value)
                    return;

                _glyph = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>골드가 모자란 버튼을 흐리게 만들 때 쓴다.</summary>
        public Color Tint
        {
            get => _tint;
            set
            {
                if (_tint == value)
                    return;

                _tint = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>설계 좌표에서 요소 좌표로 옮기는 변환. 균등 스케일 + 중앙 정렬.</summary>
        readonly struct Space
        {
            readonly Vector2 _origin;
            readonly float _scale;

            public Space(Vector2 origin, float scale)
            {
                _origin = origin;
                _scale = scale;
            }

            public Vector2 P(float x, float y) => new Vector2(_origin.x + x * _scale, _origin.y + y * _scale);

            public float L(float length) => length * _scale;
        }

        void OnGenerateVisualContent(MeshGenerationContext context)
        {
            var rect = contentRect;

            if (rect.width <= 1f || rect.height <= 1f)
                return;

            float size = Mathf.Min(rect.width, rect.height);
            float lineWidth = Mathf.Max(1.5f, size * 0.1f);

            // 선은 경로 바깥으로 두께의 절반만큼 번지므로 그만큼 빼고 맞춘다
            var bounds = DesignBounds(_glyph);
            float scale = (size - lineWidth) / Mathf.Max(bounds.width, bounds.height);

            var center = new Vector2(rect.width * 0.5f, rect.height * 0.5f);
            var space = new Space(center - bounds.center * scale, scale);

            var painter = context.painter2D;

            painter.strokeColor = _tint;
            painter.fillColor = _tint;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.lineWidth = lineWidth;

            switch (_glyph)
            {
                case IconGlyph.Sword:
                    DrawSword(painter, space);
                    break;
                case IconGlyph.Bow:
                    DrawBow(painter, space);
                    break;
                case IconGlyph.Staff:
                    DrawStaff(painter, space);
                    break;
                case IconGlyph.Bomb:
                    DrawBomb(painter, space);
                    break;
                case IconGlyph.Upgrade:
                    DrawUpgrade(painter, space);
                    break;
                case IconGlyph.Sell:
                    DrawSell(painter, space);
                    break;
                case IconGlyph.Pause:
                    DrawPause(painter, space);
                    break;
            }
        }

        /// <summary>
        /// 종류별 도형이 실제로 차지하는 설계 좌표 범위. 아래 Draw 함수들과 반드시 같이 고쳐야 한다.
        /// 값이 틀어지면 아이콘이 중앙에서 밀리거나 잘린다.
        /// </summary>
        static Rect DesignBounds(IconGlyph glyph)
        {
            switch (glyph)
            {
                case IconGlyph.Sword:
                    return new Rect(0.28f, 0.06f, 0.44f, 0.82f);
                case IconGlyph.Bow:
                    return new Rect(0.24f, 0.209f, 0.68f, 0.582f);
                case IconGlyph.Staff:
                    return new Rect(0.26f, 0.09f, 0.55f, 0.81f);
                case IconGlyph.Bomb:
                    return new Rect(0.16f, 0.09f, 0.77f, 0.83f);
                case IconGlyph.Upgrade:
                    return new Rect(0.22f, 0.22f, 0.56f, 0.70f);
                case IconGlyph.Sell:
                    return new Rect(0.16f, 0.16f, 0.68f, 0.68f);
                case IconGlyph.Pause:
                    return new Rect(0.24f, 0.12f, 0.52f, 0.76f);
                default:
                    return new Rect(0f, 0f, 1f, 1f);
            }
        }

        // ── 종류별 도형 ──────────────────────────────────────────────────────
        // UI Toolkit은 y축이 아래로 증가한다. 아래 좌표는 전부 그 기준.

        /// <summary>보병: 위로 선 칼. 날 + 코등이 + 손잡이 끝.</summary>
        static void DrawSword(Painter2D painter, Space space)
        {
            Line(painter, space, 0.50f, 0.06f, 0.50f, 0.80f);   // 날 + 손잡이
            Line(painter, space, 0.28f, 0.60f, 0.72f, 0.60f);   // 코등이
            Line(painter, space, 0.40f, 0.88f, 0.60f, 0.88f);   // 손잡이 끝
        }

        /// <summary>궁수: 왼쪽으로 휜 활 + 시위 + 오른쪽을 겨눈 화살.</summary>
        static void DrawBow(Painter2D painter, Space space)
        {
            painter.BeginPath();
            painter.Arc(space.P(0.62f, 0.50f), space.L(0.38f), Angle.Degrees(130f), Angle.Degrees(230f));
            painter.Stroke();

            Line(painter, space, 0.376f, 0.209f, 0.376f, 0.791f);   // 시위
            Line(painter, space, 0.30f, 0.50f, 0.92f, 0.50f);       // 화살대
            Line(painter, space, 0.92f, 0.50f, 0.78f, 0.41f);       // 촉
            Line(painter, space, 0.92f, 0.50f, 0.78f, 0.59f);
        }

        /// <summary>마도: 비스듬한 지팡이 + 끝의 보주.</summary>
        static void DrawStaff(Painter2D painter, Space space)
        {
            Line(painter, space, 0.26f, 0.90f, 0.58f, 0.36f);   // 자루

            painter.BeginPath();
            painter.Arc(space.P(0.66f, 0.24f), space.L(0.15f), Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
        }

        /// <summary>포병: 둥근 폭탄 몸통 + 휘어진 심지 + 불꽃.</summary>
        static void DrawBomb(Painter2D painter, Space space)
        {
            painter.BeginPath();
            painter.Arc(space.P(0.44f, 0.64f), space.L(0.28f), Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();

            // 심지: 몸통 오른쪽 위에서 바깥으로 휘어 나간다
            painter.BeginPath();
            painter.MoveTo(space.P(0.61f, 0.45f));
            painter.BezierCurveTo(space.P(0.74f, 0.36f), space.P(0.68f, 0.24f), space.P(0.80f, 0.20f));
            painter.Stroke();

            Line(painter, space, 0.83f, 0.16f, 0.90f, 0.09f);   // 불꽃
            Line(painter, space, 0.85f, 0.22f, 0.93f, 0.20f);
        }

        /// <summary>강화: 위를 향한 화살표 + 받침선.</summary>
        static void DrawUpgrade(Painter2D painter, Space space)
        {
            Line(painter, space, 0.50f, 0.82f, 0.50f, 0.24f);   // 화살대
            Line(painter, space, 0.22f, 0.52f, 0.50f, 0.22f);   // 촉 왼쪽
            Line(painter, space, 0.78f, 0.52f, 0.50f, 0.22f);   // 촉 오른쪽
            Line(painter, space, 0.28f, 0.92f, 0.72f, 0.92f);   // 받침
        }

        /// <summary>판매: 동전 테두리 + 화폐 기호.</summary>
        static void DrawSell(Painter2D painter, Space space)
        {
            painter.BeginPath();
            painter.Arc(space.P(0.50f, 0.50f), space.L(0.34f), Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();

            Line(painter, space, 0.50f, 0.28f, 0.50f, 0.72f);
            Line(painter, space, 0.39f, 0.41f, 0.61f, 0.41f);
            Line(painter, space, 0.39f, 0.59f, 0.61f, 0.59f);
        }

        /// <summary>일시정지: 세로 막대 두 개. 선이 아니라 면으로 그려야 굵기가 일정하다.</summary>
        static void DrawPause(Painter2D painter, Space space)
        {
            FillRect(painter, space, 0.24f, 0.12f, 0.42f, 0.88f);
            FillRect(painter, space, 0.58f, 0.12f, 0.76f, 0.88f);
        }

        static void FillRect(Painter2D painter, Space space, float x0, float y0, float x1, float y1)
        {
            painter.BeginPath();
            painter.MoveTo(space.P(x0, y0));
            painter.LineTo(space.P(x1, y0));
            painter.LineTo(space.P(x1, y1));
            painter.LineTo(space.P(x0, y1));
            painter.ClosePath();
            painter.Fill();
        }

        static void Line(Painter2D painter, Space space, float x0, float y0, float x1, float y1)
        {
            painter.BeginPath();
            painter.MoveTo(space.P(x0, y0));
            painter.LineTo(space.P(x1, y1));
            painter.Stroke();
        }
    }
}
