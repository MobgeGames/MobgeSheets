using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using Mobge.Serialization;
using SerializeReferenceEditor.Editor;
using SerializeReferenceEditor;
using System.Text;

namespace Mobge.Sheets {
    [CustomPropertyDrawer(typeof(CellId))]
    public class CellIdDrawer : PropertyDrawer {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            LayoutRectSource.SplitRect(position, EditorGUIUtility.labelWidth, out var rLabel, out var rValues, 5);
            LayoutRectSource.SplitRect(rValues, rValues.width * 0.5f, out var rColumn, out var rRow, 5);
            var pColumn = property.FindPropertyRelative(nameof(CellId.column));
            var pRow = property.FindPropertyRelative(nameof(CellId.row));
            EditorGUI.LabelField(rLabel, label);
            EditorGUI.PropertyField(rColumn, pColumn, new GUIContent());
            pColumn.stringValue = pColumn.stringValue.ToUpper();
            EditorGUI.PropertyField(rRow, pRow, new GUIContent());
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            return EditorGUIUtility.singleLineHeight;
        }
    }
    [CustomPropertyDrawer(typeof(SheetData), true)]
    public partial class ESheetData : PropertyDrawer {
        public const string c_defaultSettingsPath = "Assets/Editor/Resources/" + GoogleSheetCredentials.c_defaultAssetName + ".asset";

        [MenuItem("Window/Mobge/Google Sheet Settings")]
        public static void SheetSettings() {
            var a = AssetDatabase.LoadAssetAtPath<GoogleSheetCredentials>(c_defaultSettingsPath);
            if (a == null) {
                string path = c_defaultSettingsPath;
                var paths = path.Split("/");
                string parent = paths[0];
                for (int i = 1; i < paths.Length - 1; i++) {
                    AssetDatabase.CreateFolder(parent, paths[i]);
                    parent += "/" + paths[i];
                }
                var ins = ScriptableObject.CreateInstance<GoogleSheetCredentials>();
                AssetDatabase.CreateAsset(ins, c_defaultSettingsPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                a = AssetDatabase.LoadAssetAtPath<GoogleSheetCredentials>(c_defaultSettingsPath);
            }
            Selection.activeObject = a;
        }
        private static Meta GetMeta(SerializedProperty p) {
            if (!s_editorMetas.TryGetValue(p, out var meta)) {
                meta = new();
                s_editorMetas.Add(p, meta);
            }
            return meta;
        }

        private SRDrawer s_srDrawer = new();
        private static Dictionary<Type, SRAttribute> s_srAttributes = new();
        private static Dictionary<PropertyDescriptor, Meta> s_editorMetas = new();
        private ExposedList<MappingRef> _mappingRefs = new();
        private static LayoutRectSource s_layout = new();
        private class Meta {
            public EditorFoldGroups _groups = new EditorFoldGroups(EditorFoldGroups.FilterMode.NoFilter);
            public float height;
            public bool updateFieldOpen;
            public int rowCount;

        }
        private struct MappingRef {
            public bool valid;
            public int index;
            public string name;
            public Type fieldType;
        }

        // private SerializedProperty FindProperty(string name) {
        //     return serializedObject.FindProperty(name);
        // }
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            var go = property.ReadObject<SheetData>(out var t);
            if (EnsureTableStart(go)) {
                property.WriteObject(go);
            }
            var meta = GetMeta(property);
            s_layout.Reset(position);
            var rHeader = s_layout.NextRect();
            UpdateDataButton(rHeader, property);
            EditorGUI.PropertyField(rHeader, property);
            if (!property.isExpanded) {
                meta.height = s_layout.Height;
                return;
            }
            EditorGUI.indentLevel++;
            PropertyField(property, nameof(SheetData.googleSheet));
            PropertyField(property, nameof(SheetData.tableStart));
            MappingsEditor(property, go.RowType);
            UpdateSheetField(meta, property);

            PropertyField(property, nameof(SheetData<int>.data));
            EditorGUI.indentLevel--;
            meta.height = s_layout.Height;
            return;
        }

        private void UpdateDataButton(Rect rHeader, SerializedProperty property) {
            var gcUpdate = new GUIContent("Update Data");
            var gcWrite = new GUIContent("Write to Sheet");
            
            float updateButtonWidth = 10f + GUI.skin.button.CalcSize(gcUpdate).x;
            float writeButtonWidth = 10f + GUI.skin.button.CalcSize(gcWrite).x;
            float totalButtonWidth = updateButtonWidth + writeButtonWidth + 5f;
            
            Rect updateButtonRect = new Rect(rHeader.x + rHeader.width - totalButtonWidth, rHeader.y, updateButtonWidth, rHeader.height);
            Rect writeButtonRect = new Rect(updateButtonRect.xMax + 5f, rHeader.y, writeButtonWidth, rHeader.height);
            
            if (GUI.Button(updateButtonRect, gcUpdate)) {
                UpdateFromSheet(property);
            }
            
            if (GUI.Button(writeButtonRect, gcWrite)) {
                WriteToSheetAsync(property);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            return GetMeta(property).height;
        }
        private void PropertyField(SerializedProperty p, string path) {
            p = p.FindPropertyRelative(path);
            if (p == null) {
                return;
            }
            float height = EditorGUI.GetPropertyHeight(p);
            EditorGUI.PropertyField(s_layout.NextRect(height), p, true);
        }
        private bool EnsureTableStart(SheetData d) {
            bool edited = false;
            if (string.IsNullOrEmpty(d.tableStart.column)) {
                d.tableStart.column = "A";
                edited = true;
            }
            if (d.tableStart.row < 1) {
                d.tableStart.row = 1;
                edited = true;
            }
            return edited;
        }

        private void MappingsEditor(SerializedProperty property, Type rowType) {
            var pMappings = property.FindPropertyRelative(nameof(SheetData.mappings));

            pMappings.isExpanded = EditorGUI.Foldout(s_layout.NextRect(), pMappings.isExpanded, "Columns", true);
            if (!pMappings.isExpanded) {
                return;
            }
            UpdateMappings(pMappings, rowType, out int validCount);
            //EditorGUILayout.BeginHorizontal();
            var rButtons = s_layout.NextRect();
            //int count = _mappingRefs.Count;
            Rect r = rButtons;
            float spacing = 5;
            r.width = 0;
            //EditorGUILayout.LabelField("Columns", GUILayout.Width(110));
            float xMax = rButtons.xMax;
            for (int i = 0; i < _mappingRefs.Count; i++) {
                var mr = _mappingRefs.array[i];
                var buttonContent = new GUIContent(mr.name);
                float bWidth = GUI.skin.button.CalcSize(buttonContent).x + 5f;
                r.width = bWidth;
                if (i != 0 && r.xMax > xMax) {
                    r = s_layout.NextRect();
                    r.width = bWidth;
                }
                bool dEnabled = GUI.enabled;
                
                GUI.enabled = dEnabled && mr.valid && mr.index < 0;
                if (GUI.Button(r, buttonContent)) {
                    int mIndex = pMappings.arraySize;
                    pMappings.InsertArrayElementAtIndex(mIndex);
                    var pMapping = pMappings.GetArrayElementAtIndex(mIndex);
                    pMapping.FindPropertyRelative(nameof(SheetData.MappingEntry.fieldName)).stringValue = mr.name;
                    pMapping.FindPropertyRelative(nameof(SheetData.MappingEntry.mapping)).managedReferenceValue = null;

                }
                GUI.enabled = dEnabled;
                r.x += r.width + spacing;

            }
            //EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            MappingsField(pMappings);
            EditorGUI.indentLevel--;
        }
        private SRAttribute GetSrAttribute(Type type) {
            if (!s_srAttributes.TryGetValue(type, out var att)) {
                var gType = typeof(SheetData.AMapping<>).MakeGenericType(type);
                att = new SRAttribute(gType);
                s_srAttributes.Add(type, att);
            }
            return att;
        }
        private void MappingsField(SerializedProperty pMappings) {
            int deleteIndex = -1;
            float seperator = 3f;
            for (int i = 0; i < _mappingRefs.Count; i++) {
                s_layout.NextRect(seperator);
                var mr = _mappingRefs.array[i];
                if (mr.index < 0) {
                    continue;
                }
                var pMapping = pMappings.GetArrayElementAtIndex(mr.index);
                var pMap = pMapping.FindPropertyRelative(nameof(SheetData.MappingEntry.mapping));
                float boxHeight = EditorGUIUtility.singleLineHeight;
                float mapHeight = 0;
                var gc = new GUIContent(pMap.displayName);
                if (mr.fieldType != null) {

                    mapHeight = s_srDrawer.GetPropertyHeight(pMap, gc);
                    boxHeight += mapHeight;
                }
                EditorGUI.DrawRect(s_layout.NextRect(boxHeight + seperator, true), new Color(0, 0, 0, 0.15f));
                s_layout.NextSplitRect(s_layout.Width * 0.5f, out var rLabel, out var rButtons);
                LayoutRectSource.SplitRect(rButtons, rButtons.width - 30, out var rListKeys, out var rDelete, 5);
                EditorGUI.LabelField(rLabel, mr.name);
                if (GUI.Button(rListKeys, "List Keys")) {
                    SheetData.AMapping mapping = pMap.ReadObject<SheetData.AMapping>(out _);
                    if (mapping != null) {
                        List<string> allKeys = new();
                        mapping.GetAllKeys(allKeys);
                        StringBuilder sb = new();
                        sb.AppendLine("All Keys of field: " + mr.name);
                        foreach (var k in allKeys) {
                            sb.AppendLine(k);

                        }
                        Debug.Log(sb);
                    }
                    else {
                        Debug.Log("Mapping is not set");
                    }
                }
                if (GUI.Button(rDelete, "X")) {
                    deleteIndex = mr.index;
                }

                if (mr.fieldType != null) {
                    s_srDrawer.SetAttribute(GetSrAttribute(mr.fieldType));
                    var rect = s_layout.NextRect(mapHeight);
                    s_srDrawer.OnGUI(rect, pMap, gc);
                }
                s_layout.NextRect(seperator);

            }
            if (deleteIndex >= 0) {

                GUI.changed = true;
                pMappings.DeleteArrayElementAtIndex(deleteIndex);
            }

        }
        private int IndexOfMapping(int count, string fieldName) {
            for (int i = 0; i < count; i++) {
                if (_mappingRefs.array[i].name == fieldName) {
                    return i;
                }
            }
            return -1;
        }
        private void UpdateMappings(SerializedProperty pMappings, Type rowType, out int validCount) {
            _mappingRefs.Clear();
            if (SheetData.TryGetFields(rowType, out var fields)) {
                for (int i = 0; i < fields.Length; i++) {
                    var field = fields[i];
                    MappingRef mr;
                    mr.name = field.Name;
                    mr.valid = !IsPrimitive(field.type);
                    mr.index = -1;
                    mr.fieldType = field.type;
                    _mappingRefs.Add(mr);
                }
            }
            validCount = _mappingRefs.Count;
            for (int i = 0; i < pMappings.arraySize; i++) {
                var m = pMappings.GetArrayElementAtIndex(i);
                string fieldName = m.FindPropertyRelative(nameof(SheetData.MappingEntry.fieldName)).stringValue;
                int index = IndexOfMapping(validCount, fieldName);
                if (index >= 0) {
                    _mappingRefs.array[index].index = i;
                }
                else {
                    MappingRef mr;
                    mr.name = fieldName;
                    mr.valid = false;
                    mr.index = i;
                    mr.fieldType = default;
                    _mappingRefs.Add(mr);
                }
            }
        }

         private async void WriteToSheetAsync(SerializedProperty property) {
             try {
                 await WriteToSheet(property);
             }
             catch (System.Exception e) {
                 Debug.LogError($"Error writing to Google Sheets: {e.Message}");
             }
         }
        
       
    }
}
