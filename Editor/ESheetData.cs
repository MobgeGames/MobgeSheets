using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Mobge.Sheets;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Unity.Mathematics;
using Mobge.Serialization;
using Mobge.Sheets.Test;

namespace Mobge.DoubleKing {
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
    [CustomEditor(typeof(SheetData), true)]
    public class ESheetData : Editor {
        private SheetData _go;

        private EditorFoldGroups _groups;
        private ExposedList<MappingRef> _mappingRefs;
        private struct MappingRef {
            public bool valid;
            public int index;
            public string name;
        }

        protected void OnEnable() {
            _go = target as SheetData;
            _groups = new EditorFoldGroups(EditorFoldGroups.FilterMode.NoFilter);
            _mappingRefs = new();
        }

        public override void OnInspectorGUI() {
            
            Undo.RecordObject(_go, "sheet data edit");
            var p = serializedObject.GetIterator();
            p.Next(true);
            while(p.Next(false)) {
                if(p.propertyPath != nameof(SheetData.mappings)) {
                    EditorGUILayout.PropertyField(p, true);
                }
            }
            _groups.GuilayoutField(CreateFields);
            if(GUILayout.Button("Update Sheet")) {
                UpdateSheet();
            }
            if(GUI.changed) {
                serializedObject.ApplyModifiedProperties();
                EditorExtensions.SetDirty(_go);
            }
        }
        private void CreateFields(EditorFoldGroups.Group group) {
            group.AddChild("Mappings", () => {
                if(_go.mappings == null) {
                    _go.mappings = new SheetData.Mapping[0];
                }
                UpdateMappings(out int validCount);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Add Mapping", GUILayout.Width(110));
                for(int i = 0; i < _mappingRefs.Count; i++) {
                    var mr = _mappingRefs.array[i];
                    if(mr.index < 0) {
                        if(GUILayout.Button(mr.name)) {
                            var nm = new TestSheetData.DefaultMapping();
                            nm.fieldName = mr.name;
                            ArrayUtility.Add(ref _go.mappings, nm);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.BeginChangeCheck();
                var pMappings = this.serializedObject.FindProperty(nameof(SheetData.mappings));
                MappingsField(pMappings);
            });
        }
        private void MappingsField(SerializedProperty pMappings) {
            
            for(int i = 0; i < _mappingRefs.Count; i++) {
                var mr = _mappingRefs.array[i];
                if(mr.index < 0) {
                    continue;
                }
                var pMapping = pMappings.GetArrayElementAtIndex(mr.index);
                EditorGUILayout.PropertyField(pMapping, true);
                
            }
        }
        private int IndexOfMapping(int count, string fieldName) {
            for(int i = 0; i < count; i++) {
                if(_mappingRefs.array[i].name == fieldName) {
                    return i;
                }
            }
            return -1;
        }
        private void UpdateMappings(out int validCount) {
            _mappingRefs.Clear();
            if(BinarySerializer.TryGetFields(_go.RowType, out var fields)) {
                for(int i = 0; i < fields.Length; i++) {
                    var field = fields[i];
                    if(IsPrimitive(field.FieldType)) {
                        continue;
                    }
                    MappingRef mr;
                    mr.name = field.Name;
                    mr.valid = true;
                    mr.index = -1;
                    _mappingRefs.Add(mr);
                }
            }
            validCount = _mappingRefs.Count;
            for(int i = 0; i < _go.mappings.Length; i++) {
                var m = _go.mappings[i];
                int index = IndexOfMapping(validCount, m.fieldName);
                if(index >= 0) {
                    _mappingRefs.array[index].index = i;
                }
                else {
                    MappingRef mr;
                    mr.name = m.fieldName;
                    mr.valid = false;
                    mr.index = i;
                    _mappingRefs.Add(mr);
                }
            }
        }
        private bool IsPrimitive(Type t) {
            return t == typeof(int) || t == typeof(string) || t == typeof(bool) || t == typeof(float) || t == typeof(long) || t == typeof(double);
        }

        private async void UpdateSheet() {
            int2 size = await DetectSize();
            var range = _go.tableStart.GetRange(size);
            Debug.Log("Range: " + range);
        }

        private async Task<int2> DetectSize() {
            var start = _go.tableStart;
            if(string.IsNullOrEmpty(start.column)) {
                start.column = "A";
            }
            if(start.row <= 0) {
                start.row = 1;
            }
            string rangeH = start.column + start.row + ':' + start.row;
            string rangeV = start.column + start.row + ':' + start.column;
            var nodes = await _go.googleSheet.GetValues(Dimension.ROWS, rangeH, rangeV);
            if(nodes.IsNullOrEmpty()) {
                return default;
            }
            var nodeH = nodes[0];
            var nodeV = nodes[1];
            int2 size = new int2(1,1);

            if(nodeH.Count > 0) {
                var valsH = nodeH[0].AsArray;
                for(int i = 1; i < valsH.Count; i++) {
                    if(string.IsNullOrEmpty(valsH[i].Value)) {
                        break;
                    }
                    size.x++;
                }
            }
            for(int i = 1; i < nodeV.Count; i++) {
                if(nodeV[i].AsArray.Count == 0) {
                    break;
                }
                size.y++;
            }


            return size;
        }
    }
}
