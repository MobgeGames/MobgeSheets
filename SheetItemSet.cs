using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mobge.Sheets {
    public interface SetEntry {
        public string Name { get; }
        public Sprite Icon { get; }
    }
    public abstract class SheetItemSet<T> : ItemSetT<T> where T : SetEntry {
        public Data data;

#if UNITY_EDITOR
        public bool EnsureEditorData() {
            bool edited = false;
            if (data == null) {
                data = new Data();
                edited = true;
            }
            if (data.Editor_set != this) {
                data.Editor_set = this;
                edited = true;
            }
            return edited;
        }
#endif
        [Serializable]
        public class Data : SheetData {
#if UNITY_EDITOR
            public ItemSet Editor_set { get; set; }
#endif
            public override Type RowType => typeof(T);

            public override void UpdateData(object[] rows) {
#if UNITY_EDITOR
                if (Editor_set == null) {
                    return;
                }
                var arr = rows.Cast<T>().ToArray();
                Editor_set.items.Clear();

#else
                return;
#endif
            }

        }
    }
}
