using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


namespace Mobge.Sheets {
    [CustomPropertyDrawer(typeof(GoogleSheet))]
    public class GoogleSheetDrawer : PropertyDrawer {
        private LayoutRectSource s_layout = new();
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            s_layout.Reset(position);
            var rTitle = s_layout.NextRect();
            var rButton = rTitle;
            float buttonWidth = 100;
            rTitle.width -= buttonWidth;
            rButton.x = rButton.xMax - buttonWidth;
            rButton.width = buttonWidth;
            EditorGUI.PropertyField(rTitle, property, label, false);
            if (GUI.Button(rButton, "Open")) {
                var sheet = property.ReadObject<GoogleSheet>(out _);
                OpenPage(sheet);
            }
            if (!property.isExpanded) {
                return;
            }
            EditorGUI.indentLevel++;
            PropertyField(property.FindPropertyRelative(GoogleSheet.OverrideSheetIdPropertyName));
            
            PropertyField(property.FindPropertyRelative(nameof(GoogleSheet.sheetName)));
            EditorGUI.indentLevel--;
        }
        private async void OpenPage(GoogleSheet sheet) {
            int tableId = await sheet.GetSheetTabId();
            UriBuilder b = new UriBuilder($"https://docs.google.com/spreadsheets/d/{sheet.SheetId}/edit");
            b.AddParameter("gid", tableId.ToString());
            System.Diagnostics.Process.Start(b.Uri);
            Debug.Log("open page");     
        }

        private void PropertyField(SerializedProperty pp) {
            var rect = s_layout.NextRect(EditorGUI.GetPropertyHeight(pp, true));
            EditorGUI.PropertyField(rect, pp, true);


        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            return EditorGUI.GetPropertyHeight(property, true);
        }
    }
}
