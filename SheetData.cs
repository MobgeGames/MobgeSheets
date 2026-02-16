using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Mobge.Serialization;
using SerializeReferenceEditor;
using SimpleJSON;
using Sirenix.OdinInspector;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

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

        public async Task TestSheetCacher() {
            var start = tableStart;
            if (string.IsNullOrEmpty(start.column)) {
                start.column = "A";
            }
            if (start.row <= 0) {
                start.row = 1;
            }
            string rangeH = start.column + start.row + ':' + start.row;
            string rangeV = start.column + start.row + ':' + start.column;
            var ranges = new[] { rangeH, rangeV };
            await SheetCacher.Instance.TestCacher(googleSheet, Dimension.ROWS, ranges);
            var result = await DetectSize(this);
            var range = tableStart.GetRange(result.Item1);
            await SheetCacher.Instance.TestCacher(googleSheet, Dimension.ROWS, new []{range});
        }

        public static async Task<bool> UpdateFromSheet(Object obj, SheetData sheetData, string sheetDataName, bool forceUseSheetCacher = false) {
            var result = await DetectSize(sheetData, forceUseSheetCacher);
            if (!result.Item2) {
                return false;
            }
            var range = sheetData.tableStart.GetRange(result.Item1);
            if (!await ReadFromSheet(obj, sheetData, range, sheetDataName, forceUseSheetCacher)) {
                return false;
            }

            return true;
        }
        public static async Task<(int2, bool)> DetectSize(SheetData go, bool forceUseSheetCacher = false) {
            var result = (await DetectSizeAndHeader(go, forceUseSheetCacher));
            return (result.Item1, result.Item3);
        }
        public static async Task<(int2, JSONArray, bool)> DetectSizeAndHeader(SheetData sheetData,
            bool forceUseSheetCacher = false) {
            var start = sheetData.tableStart;
            if (string.IsNullOrEmpty(start.column)) {
                start.column = "A";
            }
            if (start.row <= 0) {
                start.row = 1;
            }
            string rangeH = start.column + start.row + ':' + start.row;
            string rangeV = start.column + start.row + ':' + start.column;

            JSONArray[] nodes;
            // if (Application.isEditor && false) {
            if (Application.isEditor && !forceUseSheetCacher) {
                nodes = await sheetData.googleSheet.GetValues(Dimension.ROWS, rangeH, rangeV);
            } else {
                if (!SheetCacher.Instance.TryGetValues(sheetData.googleSheet, Dimension.ROWS, new[] { rangeH, rangeV }, out nodes)) {
                    return default;
                }
            }
            
            if (nodes.IsNullOrEmpty()) {
                return default;
            }
            var nodeH = nodes[0];
            var nodeV = nodes[1];
            int2 size = new int2(1, 1);
            JSONArray header = null;
            if (nodeH.Count > 0)
            {
                var valsH = nodeH[0].AsArray;
                header = new JSONArray();
                if(valsH.Count > 0) {
                    header.Add(valsH[0]);
                    for (int i = 1; i < valsH.Count; i++)
                    {
                        if (string.IsNullOrEmpty(valsH[i].Value))
                        {
                            break;
                        }
                        size.x++;
                        header.Add(valsH[i]);

                    }
                }
            }
            for (int i = 1; i < nodeV.Count; i++) {
                if (nodeV[i].AsArray.Count == 0) {
                    break;
                }
                size.y++;
            }


            return (size, header, true);
        }

        public static string ResultToText(JSONArray[] nodes)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Nodes: {nodes.Length}");
            sb.AppendLine($"{nodes.Length}:{nodes[0].Count}");
            foreach (var node in nodes) {
                sb.AppendLine($"{node}");
            }

            return sb.ToString();
        }

        public static async Task<bool> ReadFromSheet(Object obj, SheetData sheetData, string range,
            string sheetDataName, bool forceUseSheetCacher = false)
        {
            JSONArray[] result;
            // if (Application.isEditor && false) {
            if (Application.isEditor && !forceUseSheetCacher) {
                result = await sheetData.googleSheet.GetValues(Dimension.ROWS, range);
            } else {
                if (!SheetCacher.Instance.TryGetValues(sheetData.googleSheet, Dimension.ROWS, new []{range}, out result)) {
                    return false;
                }
            }
            var nodes = result[0];
            int rowCount = nodes.Count - 1;
            var header = nodes[0];
            CellContext ctx = FindMapping(sheetData, header);
            CreateReportHeader(obj, sheetDataName, "Updating Sheet", ctx, rowCount);

            object[] data = new object[rowCount];
            for (int i = 0; i < rowCount; i++)
            {
                var rowCells = nodes[i + 1].AsArray;
                object rowData = Activator.CreateInstance(sheetData.RowType);
                for (int iField = 0; iField < ctx.fieldCount; iField++)
                {
                    ctx.columnIndex = ctx.columnIndexes[iField];
                    if (ctx.columnIndex < 0)
                    {
                        continue;
                    }
                    ctx.rowIndex = i;
                    var field = ctx.fields[iField];
                    var textValue = rowCells[ctx.columnIndex].Value;
                    object value;
                    if (field.isArray)
                    {
                        var values = textValue.Split(',');
                        var arr = Array.CreateInstance(field.type, values.Length);
                        for (int v = 0; v < values.Length; v++)
                        {
                            string arrValue = values[v];
                            var o = ConvertToObject(arrValue, field, ctx.mappings[iField], ref ctx);
                            arr.SetValue(o, v);
                        }
                        value = arr;
                    }
                    else
                    {
                        value = ConvertToObject(textValue, field, ctx.mappings[iField], ref ctx);
                    }
                    field.SetValue(rowData, value);

                }
                data[i] = rowData;
            }

            sheetData.UpdateData(data);
            if (ctx.isError) Debug.LogError(ctx.report, obj);
            else Debug.Log(ctx.report, obj);
            return true;
        }
        
        private static void CreateReportHeader(Object obj, string sheetDataName, string label, CellContext ctx, int rowCount) {
            ctx.report.Append(label);
            ctx.report.Append(": (");
            ctx.report.Append(obj.name);
            ctx.report.Append(", ");
            ctx.report.Append(sheetDataName);
            ctx.report.Append(")");

            ctx.report.AppendLine(" Data count: " + rowCount);
        }

        
        private static object ConvertToObject(string textValue, SheetData.Field field, SheetData.AMapping mapping, ref CellContext ctx) {
            textValue = textValue.Trim(SheetData.s_trimChars);
            object value = null;
            if (IsPrimitive(field.type)) {
                value = GetPrimitiveValue(textValue, field.type);
            }
            else {
                if (mapping != null) {
                    value = mapping.GetObjectRaw(textValue);
                    //Debug.Log($"value validated: {mapping}, {value} : {mapping.ValidateValue(value)}");
                    if (!mapping.ValidateValue(value)) {
                        var ts = ctx.sheetData.tableStart;
                        ts.column = CellId.Add(ts.column, ctx.columnIndex);
                        ts.row += ctx.rowIndex + 1;
                        ctx.isError = true;
                        ctx.report.AppendLine($"Mapping error at cell: {ts.column}:{ts.row}");
                    }
                }
            }
            return value;
        }
        
        private static object GetPrimitiveValue(string textValue, Type t) {
            object value = null;
            if (t == typeof(int)) {
                int.TryParse(textValue, NumberStyles.Any, CultureInfo.InvariantCulture, out int i);
                value = i;
            }
            else if (t == typeof(string)) {
                value = textValue;
            }
            else if (t == typeof(bool)) {
                bool.TryParse(textValue, out bool i);
                value = i;
            }
            else if (t == typeof(float)) {
                float.TryParse(textValue, NumberStyles.Any, CultureInfo.InvariantCulture, out float i);
                value = i;
            }
            else if (t == typeof(long)) {
                long.TryParse(textValue, NumberStyles.Any, CultureInfo.InvariantCulture, out long i);
                value = i;
            }
            else if (t == typeof(double)) {
                double.TryParse(textValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double i);
                value = i;
            }
            return value;
        }
        
        public static CellContext FindMapping(SheetData sheetData, JSONNode header) {
            CellContext ctx = default;
            ctx.sheetData = sheetData;
            ctx.report = new StringBuilder();
            SheetData.TryGetFields(sheetData.RowType, out ctx.fields);
            ctx.fieldCount = ctx.fields.GetLength();
            ctx.columnIndexes = new int[ctx.fieldCount];
            ctx.mappings = new SheetData.AMapping[ctx.fieldCount];

            ctx.emptyValueCount = 0;
            ctx.emptyFields = new List<string>();

            PopulateMappings(sheetData, ctx.fields, ctx.mappings);

            for (int i = 0; i < ctx.fieldCount; i++) {
                var field = ctx.fields[i];
                int selectedIndex = -1;
                for (int ih = 0; ih < header.Count; ih++) {
                    var columnCell = header[ih];
                    if (columnCell.Value.Equals(field.Name, StringComparison.InvariantCultureIgnoreCase)) {
                        selectedIndex = ih;
                        break;
                    }
                }
                ctx.columnIndexes[i] = selectedIndex;
                if (selectedIndex < 0) {
                    ctx.isError = true;
                    ctx.report.AppendLine("No column found for field: " + field.Name);
                }

                if (!IsPrimitive(field.type) && ctx.mappings[i] == null) {
                    ctx.isError = true;
                    ctx.report.AppendLine("No mapping found for column: " + field.Name);
                }
            }
            return ctx;
        }

        public static bool IsPrimitive(Type t) {
            return t == typeof(int) || t == typeof(string) || t == typeof(bool) || t == typeof(float) || t == typeof(long) || t == typeof(double);
        }

        private static void PopulateMappings(SheetData sheetData, Field[] fields, AMapping[] mappings) {
            for (int i = 0; i < fields.Length; i++) {
                var field = fields[i];

                if (!IsPrimitive(field.type)) {
                    AMapping selectedMapping = null;
                    int mappingCount = sheetData.mappings.GetLength();

                    for (int im = 0; im < mappingCount; im++) {
                        var mapping = sheetData.mappings[im];
                        if (mapping.fieldName == field.Name) {
                            selectedMapping = mapping.mapping;
                            break;
                        }
                    }

                    mappings[i] = selectedMapping;
                }
            }
        }

        public struct CellContext {
            public StringBuilder report;
            public bool isError;
            public int columnIndex;
            public int rowIndex;
            public SheetData sheetData;
            public int fieldCount;
            public int[] columnIndexes;
            public Field[] fields;
            public AMapping[] mappings;
            public int emptyValueCount;
            public List<string> emptyFields;
        }

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
            public abstract string GetKeyFromObject(object obj);
            
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
            public override string GetKeyFromObject(object obj) {
                if (obj is T t) {
                    return GetKey(t);
                }
                return "";
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
                    Type childType = fieldInfo.FieldType;
                    if (childType.IsArray)
                    {
                        childType = childType.GetElementType();
                    }
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
                    throw new Exception("ItemMapping: key is empty");
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
                throw new Exception($"ItemMapping: Item not found: {key}");
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
        public static CellId operator +(CellId c1, int2 offset)
        {
            CellId c = c1;
            if (offset.x > 0)
            {
                int ci = ColumnToIndex(c.column);
                ci += offset.x;
                if (ci < 0)
                {
                    throw new InvalidOperationException();
                }
                c.column = IndexToColumn(ci);
            }
            c.row += offset.y;
            return c;
        } 
        public string GetRange(int2 size)
        {
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
