using UnityEditor;
using UnityEngine;

namespace Common.Rendering.EditorTools
{
    /// <summary>
    /// <see cref="FogRange"/>를 Start/End 두 칸으로 한 줄에 그린다.
    /// 기본 표현은 구조체를 폴드아웃으로 접어 두 값을 같이 보기 어렵다.
    ///
    /// VolumeParameterDrawer가 아니라 일반 PropertyDrawer로 만든 이유:
    /// 볼륨 인스펙터는 override 체크박스 높이를 EditorGUI.GetPropertyHeight로 미리 예약하고,
    /// 파라미터 본문은 가로 레이아웃 스코프 안에서 그린다. PropertyDrawer로 두면
    /// 높이 계산과 그리기가 같은 클래스에서 나오므로 예약 높이와 실제 높이가 어긋나지 않는다.
    /// (VolumeParameterDrawer에서 GetControlRect로 줄을 새로 잡으면 다음 파라미터와 겹친다.)
    /// </summary>
    [CustomPropertyDrawer(typeof(FogRange))]
    sealed class FogRangePropertyDrawer : PropertyDrawer
    {
        static readonly GUIContent StartLabel = EditorGUIUtility.TrTextContent("Start", "LUT 좌표 0에 대응하는 값");
        static readonly GUIContent EndLabel = EditorGUIUtility.TrTextContent("End", "LUT 좌표 1에 대응하는 값");

        /// <summary>Start/End 접두 라벨에 줄 폭.</summary>
        const float SubLabelWidth = 36f;

        /// <summary>두 칸 사이 간격.</summary>
        const float FieldGap = 6f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty start = property.FindPropertyRelative(nameof(FogRange.start));
            SerializedProperty end = property.FindPropertyRelative(nameof(FogRange.end));

            if (start == null || end == null)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            Rect fieldArea = EditorGUI.PrefixLabel(position, label);

            float halfWidth = (fieldArea.width - FieldGap) * 0.5f;
            Rect startRect = new Rect(fieldArea.x, fieldArea.y, halfWidth, fieldArea.height);
            Rect endRect = new Rect(fieldArea.x + halfWidth + FieldGap, fieldArea.y, halfWidth, fieldArea.height);

            // PrefixLabel 이후 영역에는 들여쓰기와 바깥 라벨 폭을 그대로 쓰면 안 된다.
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            int previousIndent = EditorGUI.indentLevel;

            EditorGUIUtility.labelWidth = SubLabelWidth;
            EditorGUI.indentLevel = 0;

            EditorGUI.BeginChangeCheck();

            float newStart = EditorGUI.FloatField(startRect, StartLabel, start.floatValue);
            float newEnd = EditorGUI.FloatField(endRect, EndLabel, end.floatValue);

            if (EditorGUI.EndChangeCheck())
            {
                start.floatValue = newStart;
                end.floatValue = newEnd;
            }

            EditorGUI.indentLevel = previousIndent;
            EditorGUIUtility.labelWidth = previousLabelWidth;

            EditorGUI.EndProperty();
        }
    }
}
