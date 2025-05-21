using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mobge.Sheets {
    public interface ISetEntry {
        public int Id { get; }
        public string Name { get; }
        public Sprite Icon { get; }
    }
    public abstract class SheetItemSet<T> : ItemSetT<T> where T : ISetEntry  {
        public Data data;
        public bool keepIdsInRows;

        protected virtual void UpdateData(object[] rows, ItemSet editorSet) { }

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
                var itemset = (SheetItemSet<T>)Editor_set;
                if (!itemset.keepIdsInRows) {
                    itemset.items.Clear();
                    itemset.items = new Map();
                    foreach (var item in arr) {
                        itemset.items.AddElement(new Item {
                            name = item.Name,
                            sprite = item.Icon,
                            serializedContent = item,
                        });
                    }
                }
                else {
                    var dict = new Dictionary<int, Item>();
                    foreach (var item in arr) {
                        dict[item.Id] = new Item {
                            name = item.Name,
                            sprite = item.Icon,
                            serializedContent = item,
                        };
                    }
                    itemset.items.SetAll(dict);
                }

                itemset.UpdateData(rows, Editor_set);
#else
                return;
#endif
            }

        }
    }
}
