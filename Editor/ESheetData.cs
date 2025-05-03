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
    [CustomEditor(typeof(SheetData), true)]
    public partial class ESheetData : Editor {
        private SheetData _go;

        private SRDrawer _srDrawer;
        private Dictionary<Type, SRAttribute> _srAttributes;

        private EditorFoldGroups _groups;
        private ExposedList<MappingRef> _mappingRefs;
        private struct MappingRef {
            public bool valid;
            public int index;
            public string name;
            public Type fieldType;
        }

        protected void OnEnable() {
            _go = target as SheetData;
            _groups = new EditorFoldGroups(EditorFoldGroups.FilterMode.NoFilter);
            _mappingRefs = new();
            _srDrawer = new();
            _srAttributes = new();
        }

        private SerializedProperty FindProperty(string name) {
            var root = serializedObject.GetIterator();
            root.Reset();
            root.NextVisible(true);
            do {
                if(root.propertyPath == name) {
                    return root;
                }
            }
            while(root.NextVisible(false));
            return default;
        }

        public override void OnInspectorGUI() {
            
            serializedObject.Update();
            
            Undo.RecordObject(_go, "sheet data edit");
            var root = serializedObject.GetIterator();
            
            root.NextVisible(true);
            var p = root;
            do {
                if(p.propertyPath != nameof(SheetData.mappings) && p.propertyPath != nameof(SheetData<int>.data)) {
                    EditorGUILayout.PropertyField(p, true);
                }
            }
            while(p.NextVisible(false));
            serializedObject.ApplyModifiedProperties();
           
            
            MappingsEditor();
            if(GUILayout.Button("Update From Sheet")) {
                UpdateFromSheet();
            }
            EditorGUILayout.PropertyField(FindProperty(nameof(SheetData<int>.data)));
            serializedObject.ApplyModifiedProperties();
            
            if(GUI.changed) {
                EditorExtensions.SetDirty(_go);
            }
        }
        private void MappingsEditor() {
            var pMappings = FindProperty(nameof(SheetData.mappings));
            pMappings.isExpanded = EditorGUILayout.Foldout(pMappings.isExpanded, "Columns", true);
            if(!pMappings.isExpanded) {
                return;
            }
            UpdateMappings(out int validCount);
            EditorGUILayout.BeginHorizontal();
            //EditorGUILayout.LabelField("Columns", GUILayout.Width(110));
            for(int i = 0; i < _mappingRefs.Count; i++) {
                var mr = _mappingRefs.array[i];
                bool dEnabled = GUI.enabled;
                GUI.enabled = dEnabled && mr.valid && mr.index < 0;
                if(GUILayout.Button(mr.name, GUILayout.ExpandWidth(false))) {
                    var nm = new SheetData.MappingEntry();
                    nm.fieldName = mr.name;
                    ArrayUtility.Add(ref _go.mappings, nm);
                }
                GUI.enabled = dEnabled;
                
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUI.indentLevel++;
            MappingsField(pMappings);
            serializedObject.ApplyModifiedProperties();
            EditorGUI.indentLevel--;
        }
        private SRAttribute GetSrAttribute(Type type) {
            if(!_srAttributes.TryGetValue(type, out var att)) {
                var gType = typeof(SheetData.AMapping<>).MakeGenericType(type);
                att = new SRAttribute(gType);
                _srAttributes.Add(type, att);
            }
            return att;
        }
        private void MappingsField(SerializedProperty pMappings) {
            int deleteIndex = -1;
            for(int i = 0; i < _mappingRefs.Count; i++) {
                var mr = _mappingRefs.array[i];
                if(mr.index < 0) {
                    continue;
                }
                var pMapping = pMappings.GetArrayElementAtIndex(mr.index);
                var pMap = pMapping.FindPropertyRelative(nameof(SheetData.MappingEntry.mapping));
                EditorGUILayout.BeginVertical("Box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(mr.name);
                if(GUILayout.Button("List Keys")) {
                    SheetData.AMapping mapping =  pMap.ReadObject<SheetData.AMapping>(out _);
                    if(mapping != null) {
                        List<string> allKeys = new();
                        mapping.GetAllKeys(allKeys);
                        StringBuilder sb = new();
                        sb.AppendLine("All Keys of field: " + mr.name);
                        foreach(var k in allKeys) {
                            sb.AppendLine(k);

                        }
                        Debug.Log(sb);
                    }
                    else {
                        Debug.Log("Mapping is not set");
                    }
                }
                if(GUILayout.Button("X")) {
                    deleteIndex = mr.index;
                }
                EditorGUILayout.EndHorizontal();
                if(mr.fieldType != null) {
                    _srDrawer.SetAttribute(GetSrAttribute(mr.fieldType));
                    var gc = new GUIContent(pMap.displayName);
                    float height = _srDrawer.GetPropertyHeight(pMap, gc);
                    var rect = EditorGUILayout.GetControlRect(true, height);
                    _srDrawer.OnGUI(rect, pMap, gc);
                }
                EditorGUILayout.EndVertical();
            }
            if(deleteIndex >= 0) {
                
                GUI.changed = true;
                pMappings.DeleteArrayElementAtIndex(deleteIndex);
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
                    MappingRef mr;
                    mr.name = field.Name;
                    mr.valid = !IsPrimitive(field.FieldType);
                    mr.index = -1;
                    mr.fieldType = field.FieldType;
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
                    mr.fieldType = default;
                    _mappingRefs.Add(mr);
                }
            }
        }
        private bool IsPrimitive(Type t) {
            return t == typeof(int) || t == typeof(string) || t == typeof(bool) || t == typeof(float) || t == typeof(long) || t == typeof(double);
        }

       
    }
}
