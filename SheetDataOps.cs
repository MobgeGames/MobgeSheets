using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Mobge.Serialization;
using SimpleJSON;
using UnityEngine;

namespace Mobge.Sheets {
    public partial class SheetData  {
        
        
        public static CellContext FindMapping(SheetData sheetData, JSONNode header) {
            CellContext ctx = default;
            ctx.sheetData = sheetData;
            ctx.report = new StringBuilder();
            ctx.FieldCount = SheetData.TryGetFields(sheetData.RowType, sheetData.mappings, out ctx.rootField);
            

            ctx.emptyValueCount = 0;
            ctx.emptyFields = new List<string>();


            ctx.header = new SheetHeader(header, ctx.rootField);

            
            var en = ctx.rootField.GetEnumerator();
            while(en.MoveNext()) {
                var f = en.Current;
                if(!ctx.header.HasColumn(f.FullName)) {
                    ctx.isError = true;
                    ctx.report.AppendLine("No column found for field: " + f.FullName);
                }
                if(!IsPrimitive(f.type) && f.mapping == null) {
                    ctx.isError = true;
                    ctx.report.AppendLine("No mapping found for column: " + f.FullName);
                }
            }
            return ctx;
        }
        
        
        public struct SheetColumn {
            public string fullName;
            public int[] indexes;
            public int columnIndex;
            public Field field;
        }
        public struct SheetHeader {
            public JSONNode row;
            public List<SheetColumn> columns;
            private Field rootField;
            private List<KeyValuePair<string, int>> _tempPath;

            

            public bool HasColumn(string fullName) {
                for(int i = 0; i < columns.Count; i++) {
                    if(columns[i].fullName == fullName) {
                        return true;
                    }
                }
                return false;
            }
            public SheetHeader(JSONNode row, Field root) {
                this.row = row;
                rootField = root;
                _tempPath = new();
                columns = new();
                InitializePaths();
            }
            private bool TryGetFieldFromTemp(out Field f) {
                f = rootField;
                for(int i = 0; i < _tempPath.Count; i++) {
                    var p = _tempPath[i].Key;
                    if(!f.TryGetChild(p, out f)) {
                        return false;
                    }
                }
                return true;
            }
            private void InitializePaths() {
                for(int i = 0; i < row.Count; i++) {
                    string val = row[i].Value;
                    var path = val.Trim().Split('.');
                    _tempPath.Clear();
                    string fullName = "";
                    Field f = rootField;
                    bool broken = false;
                    for(int a = 0; a < path.Length; a++) {
                        var pathName = path[a];
                        if(int.TryParse(pathName, out int arrIndex)) {
                            int lastIndex = _tempPath.Count - 1;
                            var pl = _tempPath[lastIndex];
                            _tempPath[lastIndex] = new (pl.Key, arrIndex);
                        }
                        else {
                            if(!f.TryGetChild(pathName, out f)) {
                                broken = true;
                                break;
                            }
                            if(a != 0) {
                                fullName += '.';
                            }
                            fullName += pathName;
                            bool isArray = f.IsArray;
                            // following line is required because;
                            // single cells containing multiple values
                            // should not consider last part as array
                            isArray &= a < path.Length - 1;
                            _tempPath.Add(new(pathName, isArray ? 0 : -1));
                        }
                    }
                    if (broken) {
                        continue;
                    }
                    SheetColumn c;
                    c.fullName = fullName;
                    c.field = f;
                    c.indexes = _tempPath.Select((v)=>v.Value).ToArray();
                    c.columnIndex = i;
                    this.columns.Add(c);
                    
                }
            }
        }
        public struct CellContext {
            public StringBuilder report;
            public bool isError;
            public int rowIndex;
            public SheetData sheetData;

            public SheetHeader header;
            public int FieldCount { get; set; }
            
            public Field rootField;
            public int emptyValueCount;
            public List<string> emptyFields;
        }
        public static int TryGetFields(Type type, MappingEntry[] mappings, out Field root) {
            root = new Field(null, true, null, mappings);
            return root.PopulateTree(type, mappings);
        }
        public struct Field {
            public Type type;
            private FieldInfo _fieldInfo;
            private List<Field> childs;
            public AMapping mapping;
            public bool IsArray => _fieldInfo.FieldType.IsArray;
            public string Name => _fieldInfo.Name;
            public string FullName { get; private set; }
            public bool TryGetChild(string name, out Field f) {
                if(childs == null) {
                    f = default;
                    return false;
                }
                for(int i = 0; i < childs.Count; i++) {
                    var c = childs[i];
                    if(c.Name == name) {
                        f = c;
                        return true;
                    }
                }
                f = default;
                return false;
            }
            public void SetValue(object root, object value, SheetColumn column) {
                if(_fieldInfo != null) {
                    throw new Exception("This method can only be called from root field. Root field has null " + nameof(_fieldInfo) + ".");
                }
                var path = column.fullName.Split('.');
                if(TryGetChild(path[0], out var cf)) {
                    cf.SetValue(root, value, column, path, 1);
                }
            }
            public Enumerator GetEnumerator() => new Enumerator(this);
            private bool SetValue(object obj, object value, SheetColumn column, string[] path, int depth) {
                int arrayIndex = column.indexes[depth - 1];
                Array arr = default;
                if(arrayIndex >= 0) {
                    var val = _fieldInfo.GetValue(obj);
                    if(val == null) {
                        val = Activator.CreateInstance(_fieldInfo.FieldType, arrayIndex + 1);
                    }
                    arr = (Array)val;
                    if(arr.Length < arrayIndex + 1) {
                        var na = (Array)Activator.CreateInstance(_fieldInfo.FieldType, arrayIndex + 1);
                        Array.Copy(arr, na, arr.Length);
                        arr = na;
                    }
                }
                object ownValue;
                if(childs == null) {
                    ownValue = value;
                }
                else {
                    string name = path[depth];
                    if(!TryGetChild(name, out var cf)) {
                        return false;
                    }
                    if(arrayIndex >= 0) {
                        ownValue = arr.GetValue(arrayIndex);
                    }
                    else {
                        ownValue = _fieldInfo.GetValue(obj);
                    }
                    if(ownValue == null) {
                        ownValue = Activator.CreateInstance(type);
                    }
                    if(!cf.SetValue(ownValue, value, column, path, depth + 1)) {
                        return false;
                    }
                }
                if(arrayIndex >= 0) {
                    arr.SetValue(ownValue, arrayIndex);
                    ownValue = arr;
                }
                _fieldInfo.SetValue(obj, ownValue);
                return true;
            }
            public Field(FieldInfo f, bool seperateColumns, string prefix, MappingEntry[] mappingList) {
                this._fieldInfo = f;
                if(seperateColumns) {
                    childs = new();
                }
                else {
                    childs = null;
                }
                mapping = null;
                if(_fieldInfo == null) {
                    type = null;
                    FullName = null;
                }
                else {
                    FullName = prefix + f.Name;
                    type = _fieldInfo.FieldType;
                    if (IsArray) {
                        type = type.GetElementType();
                    }
                    int mCount = CollectionExtensions.GetLength(mappingList);
                    for(int i = 0; i < mCount; i++) {
                        var mapping = mappingList[i];
                        if(mapping.IsValid && mapping.fieldName == FullName) {
                            this.mapping = mapping.mapping;
                            break;
                        }
                    }
                }
            }
            
            public int PopulateTree(Type type, MappingEntry[] mappings) {
                if (!BinarySerializer.TryGetFields(type, out var ffs)) {
                    return 0;
                }
                int count = 0;
                for (int i = 0; i < ffs.Length; i++) {
                    var fieldInfo = ffs[i];
                    var att = fieldInfo.GetCustomAttribute<SeperateColumns>();
                    string prefix = "";
                    if(!string.IsNullOrEmpty(FullName)) {
                        prefix = FullName + ".";
                    }
                    var cf = new Field(fieldInfo, att != null, prefix, mappings);
                    if (cf.childs != null) {
                        int cCount = cf.PopulateTree(cf.type, mappings);
                        if(cCount == 0) {
                            continue;
                        }
                        count += cCount;
                    }
                    else {
                        count++;
                    }
                    
                    childs.Add(cf);
                }
                return count;
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
            
            public struct Enumerator : IEnumerator<Field> {
                private List<(Field, int)> parents;
                public Field Current { get; private set; }
                public string CurrentPath {
                    get {
                        string s = "";
                        for(int i = 1; i < parents.Count; i++) {
                            var p = parents[i].Item1;
                            s += p.Name + '.';
                            if(p.IsArray) {
                                s += "0.";
                            }
                        }
                        return s + Current.Name;
                    }
                }

                object IEnumerator.Current => Current;

                public Enumerator(Field f) {
                    parents = new();
                    Current = default;
                    
                    if(f.childs != null) {
                        parents.Add((f, -1));
                    }
                }

                public bool MoveNext() {
                    while(parents.Count > 0) {
                        int lastIndex = parents.Count - 1;
                        var (f, index) = parents[lastIndex];
                        if(index + 1 < f.childs.Count) {
                            index++;
                            parents[lastIndex] = (f, index);
                            Current = f.childs[index];
                            if(Current.childs == null) {
                                return true;
                            }
                            parents.Add((Current, -1));
                        }
                        else {
                            parents.Pop();
                        }
                    }
                    return false;
                }
                public void Reset() {
                    throw new NotImplementedException();
                }
                public void Dispose() { }

            }
        }
        
    }
    
}