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
            public virtual bool Compare(T t1, T t2) {
                return t1.Equals(t2);
            }
            public string GetKey(T o) {
                List<string> keys = new();
                GetAllKeys(keys);
                for (int i = 0; i < keys.Count; i++) {
                    if (Compare(o, GetObject(keys[i]))) {
                        return keys[i];
                    }
                }
                return "";
            }
            public override bool ValidateValue(object value) {
                if (value is T t) {
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
        [Serializable]
        public class EnumMapping<T> : AMapping<T> where T : Enum
        {
            public override void GetAllKeys(List<string> keys)
            {
                var names = Enum.GetNames(typeof(T));
                keys.AddRange(names);
            }

            public override T GetObject(string key) {
                if (Enum.TryParse(typeof(T), key, out var result) && result is T value)
                    return value;
                else {
                    Debug.LogError($"Key: {key} can't parsed, return default enum value");
                    return default;
                }
            }

            public override bool ValidateValueT(T value)
            {
                return Enum.IsDefined(typeof(T), value);
            }
        }
        private static bool TryGetFields(Type type, Stack<FieldInfo> parentFields, List<Field> fields) {
            if (!BinarySerializer.TryGetFields(type, out var ffs)) {
                return false;
            }
            for (int i = 0; i < ffs.Length; i++) {
                var fieldInfo = ffs[i];
                parentFields.Push(fieldInfo);
                var att = fieldInfo.GetCustomAttribute<SeperateColumns>();
                if (att != null) {
                    TryGetFields(fieldInfo.FieldType, parentFields, fields);
                }
                else {
                    fields.Add(new Field(parentFields.Reverse().ToArray()));
                }
                parentFields.Pop();
            }
            return true;
        }
        public static bool TryGetFields(Type type, out Field[] fields) {
            List<Field> r = new();
            Stack<FieldInfo> parentFields = new();
            TryGetFields(type, parentFields, r);
            if (r.Count == 0) {
                fields = null;
                return false;
            }
            fields = r.ToArray();
            return true;
        }

        public struct Field {
            public Type type;
            private FieldInfo[] _fieldInfos;
            public bool isArray;
            public string Name { get; private set; }
            public void SetValue(object root, object value) {
                SetValue(root, value, 0);
            }
            private void SetValue(object obj, object value, int index) {
                var fInfo = _fieldInfos[index];
                if (index == _fieldInfos.Length - 1) {
                    fInfo.SetValue(obj, value);
                    return;
                }
                var fieldValue = fInfo.GetValue(obj);
                if (fieldValue == null) {
                    fieldValue = Activator.CreateInstance(fInfo.FieldType);
                }
                SetValue(fieldValue, value, index + 1);
                fInfo.SetValue(obj, fieldValue);
            }
            public Field(FieldInfo[] f) {
                this._fieldInfos = f;
                Name = GetName(f);
                var t = f[^1].FieldType;
                isArray = t.IsArray;
                if (isArray) {
                    type = t.GetElementType();
                }
                else {
                    type = t;
                }
            }

            private static string GetName(FieldInfo[] _fieldInfos) {
                string s = "";
                for (int i = 0; i < _fieldInfos.Length; i++) {
                    if (i != 0) {
                        s += ".";
                    }
                    s += _fieldInfos[i].Name;
                }
                return s;
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
    public class SeperateColumns : PropertyAttribute {

    }
    public enum Dimension {
        ROWS,
        COLUMNS
    }

    public interface ISheetDataOwner{
        public List<SheetData> GetSheetData();
    }
}
