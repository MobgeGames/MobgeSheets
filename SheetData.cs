using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mobge.Serialization;
using SerializeReferenceEditor;
using Sirenix.OdinInspector;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Networking;

namespace Mobge.Sheets {
	//[CreateAssetMenu(menuName = "Mobge/Sheets/Data")]
    [Serializable]
    public sealed class SheetData<T> : SheetData {
        public T[] data;
        public override Type RowType => typeof(T);
        public override void UpdateData(object[] rows) {
            this.data = rows.Cast<T>().ToArray();
            //OnDataUpdate();
        }
        public int Count => data.Length;
        public T this[int index] {
            get => data[index];
        }
        // protected virtual void OnDataUpdate() {

        // }
    }
    public abstract class SheetData {
        public static char[] s_trimChars = new char[]{' ', '\r', '\n'};
        public GoogleSheet googleSheet;
        public CellId tableStart;
        public MappingEntry[] mappings;
        public abstract Type RowType { get; }
        public abstract void UpdateData(object[] rows);



        [Serializable]
        public struct MappingEntry {
            public string fieldName;
            [SerializeReference] public AMapping mapping;
            public bool IsValid => mapping != null;
        }

        public abstract class AMapping {
            public abstract void GetAllKeys(List<string> keys);
            public abstract object GetObjectRaw(string key);
            public abstract bool ValidateValue(object value);
        }
        
        [Serializable]
        public abstract class AMapping<T> : AMapping {
            public abstract T GetObject(string key);
            public override bool ValidateValue(object value) {
                if(value is T t) {
                    return ValidateValueT(t);
                }
                return false;
            }
            public virtual bool ValidateValueT(T value) {
                if(value is UnityEngine.Object uo) {
                    return uo != null;
                }
                return value != null;
            }
            public override object GetObjectRaw(string key) {
                return GetObject(key);
            }
        }
        [Serializable]
        public class PairMapping<T> : AMapping<T> {
            public T defaultValue;
            public Pair[] pairs;
            public override T GetObject(string key) {
                int count = pairs.GetLength();
                for(int i = 0; i < count; i++) {
                    var p = pairs[i];
                    if(p.Key == key) {
                        return p.value;
                    }
                }
                return defaultValue;
            }
            public override void GetAllKeys(List<string> keys) {
                int count = pairs.GetLength();
                for(int i = 0; i < count; i++) {
                    keys.Add(pairs[i].Key);
                }
            }
            [Serializable]
            public struct Pair {
                public string key;
                public T value;
                public string Key {
                    get {
                        if(!string.IsNullOrEmpty(key)) {
                            return key;
                        }
                        if(value is UnityEngine.Object o) {
                            return o.name;
                        }
                        return "" + value;
                    }
                }
            }
        }
        public static bool TryGetFields(Type type, out Field[] fields) {
            if(!BinarySerializer.TryGetFields(type, out var ffs)) {
                fields = null;
                return false;
            }
            fields = new Field[ffs.Length];
            for(int i = 0; i < ffs.Length; i++) {
                fields[i] = new Field(ffs[i]);
            }
            return true;
        }
        public struct Field {
            public Type type;
            public FieldInfo fieldInfo;
            public bool isArray;
            public string Name => fieldInfo.Name;
            public Field(FieldInfo f) {
                this.fieldInfo = f;
                var t = f.FieldType;
                isArray = t.IsArray;
                if(isArray) {
                    type = t.GetElementType();
                }
                else {
                    type = t;
                }
            }
        }
        [Serializable]
        public class SpriteMapping : PairMapping<Sprite> {

        }
        [Serializable]
        public class ObjectMapping : PairMapping<UnityEngine.Object> {

        }
        [Serializable]
        public class ItemMapping : AMapping<ItemSet.ItemPath> {
            public ItemSet.ItemPath defaultValue;
            public ItemSet[] sets;
            public bool preferShortForm = true;
            public override bool ValidateValueT(ItemSet.ItemPath value) {
                return value.IsValid;
            }
            public override ItemSet.ItemPath GetObject(string key) {
                var values = key.Split(':');
                if(values.Length == 0) {
                    return defaultValue;
                }
                string setName = null;
                string itemName = values[0];
                if(values.Length >= 2) {
                    setName = values[0];
                    setName = setName.Trim(s_trimChars);
                    itemName = values[1];

                }
                itemName = itemName.Trim(s_trimChars);
                for(int i = 0; i < sets.Length; i++) {
                    var set = sets[i];
                    if(set == null || (!string.IsNullOrEmpty(setName) && setName != set.name)) {
                        continue;
                    }
                    
                    foreach(var pp in set.items) {
                        if(pp.Value.name == itemName) {
                            return new ItemSet.ItemPath(set, pp.Key);
                        }
                    }
                }
                return defaultValue;
            }
            public override void GetAllKeys(List<string> keys) {
                int count = sets.GetLength();
                for(int i = 0; i < count; i++) {
                    var set = sets[i];
                    if(set == null){
                        continue;
                    }
                    foreach(var item in set.items) {
                        if(preferShortForm) {
                            keys.Add(item.Value.name);
                        }
                        else {
                            keys.Add(set.name + ": " + item.Value.name);
                        }
                    }
                }
            }
        }
    }
    [Serializable]
    public struct CellId {
        public const char c_charStart = 'A';
        public const int c_charCount = 26; 
        public string column;
        public int row;
        public int2 ZeroBasedIndex {
            get {
                int2 i;
                i.x = ColumnToIndex(column) - 1;
                i.y = row - 1;
                return i;
            }
        }
        public static int ColumnToIndex(string column) {
            int val = 0;
            for(int i = 0; i < column.Length; i++) {
                char c = column[i];
                c = Char.ToUpper(c);
                int index = c - c_charStart + 1;
                val *= c_charCount;
                val += index;
            }
            return val;
        }
        public static string IndexToColumn(int index) {
            string r = "";
            while(index > 0) {
                index--;
                int i = index / c_charCount;
                int c = index - i * c_charCount;
                index = i;
                r = (char)(c + c_charStart) + r;
            }

            return r;
        }
        public static string Add(string column, int offset) {
            int index = ColumnToIndex(column);
            return IndexToColumn(index + offset);
        }
        public string GetRange(int2 size) {
            string range = column + row;
            range += ":";
            range += CellId.Add(column, size.x - 1);
            range += row + size.y - 1;
            return range;
        }
        
    }
    public enum Dimension {
        ROWS,
        COLUMNS
    }
}
