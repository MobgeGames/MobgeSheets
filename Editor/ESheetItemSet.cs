using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Reflection;
using System;

namespace Mobge.Sheets {
    [CustomEditor(typeof(SheetItemSet<>), true)]
    public class ESheetItemSet : EItemSetT {
        private class Dummy : ISetEntry {
            public int Id => throw new System.NotImplementedException();
            public string Name => throw new System.NotImplementedException();
            public Sprite Icon => throw new System.NotImplementedException();
        }
        private MethodInfo f_EnsureEditorData;

        public override void OnInspectorGUI() {
            if (f_EnsureEditorData == null) {
                f_EnsureEditorData = _go.GetType().GetMethod(nameof(SheetItemSet<Dummy>.EnsureEditorData));
            }
            if ((bool)f_EnsureEditorData.Invoke(_go, null)) {
                GUI.changed = true;
            }
            var pItemsReadOnly = serializedObject.FindProperty(nameof(SheetItemSet<Dummy>.itemsReadOnly));
            base.DrawInspectorGUI(pItemsReadOnly.boolValue);
        }
    }
}
