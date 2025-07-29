using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Mobge.Serialization;
using SimpleJSON;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using static Mobge.Sheets.SheetData;

namespace Mobge.Sheets {
    public partial class ESheetData {

        private static bool IsPrimitive(Type t) {
            return t == typeof(int) || t == typeof(string) || t == typeof(bool) || t == typeof(float) || t == typeof(long) || t == typeof(double);
        }

        private static void PopulateMappings(SheetData go, Field[] fields, AMapping[] mappings) {
            for (int i = 0; i < fields.Length; i++) {
                var field = fields[i];

                if (!IsPrimitive(field.type)) {
                    AMapping selectedMapping = null;
                    int mappingCount = go.mappings.GetLength();

                    for (int im = 0; im < mappingCount; im++) {
                        var mapping = go.mappings[im];
                        if (mapping.fieldName == field.Name) {
                            selectedMapping = mapping.mapping;
                            break;
                        }
                    }

                    mappings[i] = selectedMapping;
                }
            }
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



        private static async Task UpdateDropdownsCommon(SheetData go, int rowCount) {
            if (go.mappings.IsNullOrEmpty()) {
                return;
            }

            CellId start = go.tableStart;
            List<GoogleSheet.DropDownData> dropdowns = new List<GoogleSheet.DropDownData>();
            List<string> allKeys = new List<string>();

            SheetData.TryGetFields(go.RowType, out var fields);

            for (int i = 0; i < go.mappings.Length; i++) {
                var mapping = go.mappings[i];
                if (mapping.mapping == null) continue;

                int columnOffset = -1;
                for (int f = 0; f < fields.Length; f++) {
                    if (fields[f].Name == mapping.fieldName) {
                        columnOffset = f;
                        break;
                    }
                }

                if (columnOffset == -1) continue;

                allKeys.Clear();
                mapping.mapping.GetAllKeys(allKeys);
                if (allKeys.Count == 0) continue;

                GoogleSheet.DropDownData dd;
                dd.options = allKeys.ToArray();
                dd.start = start.ZeroBasedIndex;
                dd.start.x += columnOffset;
                dd.start.y += 1; // Header
                dd.size = new int2(1, rowCount);
                dd.multiSelect = fields[columnOffset].isArray;

                dropdowns.Add(dd);
            }

            if (dropdowns.Count > 0) {
                await go.googleSheet.SetDropDowns(dropdowns.ToArray());
            }
        }


        public static async Task UpdateFromSheet(SerializedProperty p) {
            var go = p.ReadObject<SheetData>(out var t);
            int2 size = await DetectSize(go);
            var range = go.tableStart.GetRange(size);
            await ReadFromSheet(p, go, range);
        }
        private static CellContext FindMapping(SheetData go, JSONNode header) {
            CellContext ctx = default;
            ctx.sheetData = go;
            ctx.report = new StringBuilder();
            SheetData.TryGetFields(go.RowType, out ctx.fields);
            ctx.fieldCount = ctx.fields.GetLength();
            ctx.columnIndexes = new int[ctx.fieldCount];
            ctx.mappings = new SheetData.AMapping[ctx.fieldCount];

            ctx.emptyValueCount = 0;
            ctx.emptyFields = new List<string>();

            PopulateMappings(go, ctx.fields, ctx.mappings);

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
                    ctx.report.AppendLine("No column found for field: " + field.Name);
                }

                if (!IsPrimitive(field.type) && ctx.mappings[i] == null) {
                    ctx.report.AppendLine("No mapping found for column: " + field.Name);
                }
            }
            return ctx;
        }
        public static async Task ReadFromSheet(SerializedProperty p, SheetData go, string range) {
            var result = await go.googleSheet.GetValues(Dimension.ROWS, range);
            var nodes = result[0];
            int rowCount = nodes.Count - 1;
            var header = nodes[0];
            CellContext ctx = FindMapping(go, header);
            ctx.report.Append("Updating Sheet: (");
            if (p != null) {
                ctx.report.Append(p.serializedObject.targetObject.name);
                ctx.report.Append(", ");
                ctx.report.Append(p.propertyPath);
            }
            else {
                ctx.report.Append("Unknown");
            }
            ctx.report.Append(")");

            ctx.report.AppendLine(" Data count: " + rowCount);

            object[] data = new object[rowCount];
            for (int i = 0; i < rowCount; i++) {
                var rowCells = nodes[i + 1].AsArray;
                object rowData = Activator.CreateInstance(go.RowType);
                for (int iField = 0; iField < ctx.fieldCount; iField++) {
                    ctx.columnIndex = ctx.columnIndexes[iField];
                    if (ctx.columnIndex < 0) {
                        continue;
                    }
                    ctx.rowIndex = i;
                    var field = ctx.fields[iField];
                    var textValue = rowCells[ctx.columnIndex].Value;
                    object value;
                    if (field.isArray) {
                        var values = textValue.Split(',');
                        var arr = Array.CreateInstance(field.type, values.Length);
                        for (int v = 0; v < values.Length; v++) {
                            string arrValue = values[v];
                            var o = ConvertToObject(arrValue, field, ctx.mappings[iField], ctx);
                            arr.SetValue(o, v);
                        }
                        value = arr;
                    }
                    else {
                        value = ConvertToObject(textValue, field, ctx.mappings[iField], ctx);
                    }
                    field.SetValue(rowData, value);

                }
                data[i] = rowData;
            }

            if (p != null) {
                Undo.RecordObject(p.serializedObject.targetObject, "Update data from sheet");
            }

            go.UpdateData(data);

            if (p != null) {
                p.WriteObject(go);

                p.serializedObject.ApplyModifiedProperties();
                EditorExtensions.SetDirty(p.serializedObject.targetObject);
                Debug.Log(ctx.report, p.serializedObject.targetObject);
            }

        }
        private static object ConvertToObject(string textValue, SheetData.Field field, SheetData.AMapping mapping, in CellContext ctx) {
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
                        ctx.report.AppendLine($"Mapping error at cell: {ts.column}:{ts.row}");
                    }
                }
            }
            return value;
        }

        public static async Task<int2> DetectSize(SheetData go) {
            var start = go.tableStart;
            if (string.IsNullOrEmpty(start.column)) {
                start.column = "A";
            }
            if (start.row <= 0) {
                start.row = 1;
            }
            string rangeH = start.column + start.row + ':' + start.row;
            string rangeV = start.column + start.row + ':' + start.column;
            var nodes = await go.googleSheet.GetValues(Dimension.ROWS, rangeH, rangeV);
            if (nodes.IsNullOrEmpty()) {
                return default;
            }
            var nodeH = nodes[0];
            var nodeV = nodes[1];
            int2 size = new int2(1, 1);

            if (nodeH.Count > 0) {
                var valsH = nodeH[0].AsArray;
                for (int i = 1; i < valsH.Count; i++) {
                    if (string.IsNullOrEmpty(valsH[i].Value)) {
                        break;
                    }
                    size.x++;
                }
            }
            for (int i = 1; i < nodeV.Count; i++) {
                if (nodeV[i].AsArray.Count == 0) {
                    break;
                }
                size.y++;
            }


            return size;
        }

        private void UpdateSheetField(Meta meta, SerializedProperty p) {
            meta.updateFieldOpen = EditorGUI.Foldout(s_layout.NextRect(), meta.updateFieldOpen, "Update Sheet", true);
            if (meta.updateFieldOpen) {
                EditorGUI.indentLevel++;
                s_layout.NextSplitRect(s_layout.Width * 0.5f, out var rCount, out var rButtons, 5);
                LayoutRectSource.SplitRect(rButtons, rButtons.width * 0.5f, out var rCreate, out var rDropdowns);
                meta.rowCount = EditorGUI.IntField(rCount, "Row Count", meta.rowCount);
                meta.rowCount = Mathf.Max(meta.rowCount, 1);
                if (GUI.Button(rCreate, "Create Template")) {
                    TryCreateTemplate(p, meta.rowCount);
                }
                if (GUI.Button(rDropdowns, "Update Dropdowns")) {
                    var _go = p.ReadObject<SheetData>(out _);
                    TryUpdateDropdowns(_go);
                }
                EditorGUI.indentLevel--;
            }

        }
        private async void TryCreateTemplate(SerializedProperty p, int rowCount) {
            var _go = p.ReadObject<SheetData>(out _);
            if (!SheetData.TryGetFields(_go.RowType, out var fields)) {
                Debug.LogError("Data Has no serializable fields.");
                return;
            }
            int2 size = new int2(fields.Length, rowCount + 1);
            string range = _go.tableStart.GetRange(size);
            var values = await _go.googleSheet.GetValues(Dimension.ROWS, range);
            if (values == null || values.Length == 0) {
                Debug.LogError("Failed to access sheet.");
                return;
            }
            if (values[0].AsArray.Count > 0) {
                Debug.LogError("There is no enough empty space in sheet.");
                return;
            }
            JSONArray root = new JSONArray();
            JSONArray row = new JSONArray();
            for (int i = 0; i < fields.Length; i++) {
                row.Add(fields[i].Name);
            }
            root[0] = row;
            await _go.googleSheet.PutValues(Dimension.ROWS, root, range);
            await UpdateDropdownsCommon(_go, rowCount);
        }
        private async void TryUpdateDropdowns(SheetData go) {
            int2 size = await DetectSize(go);
            if (size.y < 2) {
                Debug.LogError("Table has no rows.");
                return;
            }
            await UpdateDropdownsCommon(go, size.y - 1);

        }

        private struct CellContext {
            public StringBuilder report;
            public int columnIndex;
            public int rowIndex;
            public SheetData sheetData;
            internal int fieldCount;
            internal int[] columnIndexes;
            public Field[] fields;
            internal AMapping[] mappings;
            public int emptyValueCount;
            public List<string> emptyFields;
        }

        public static async Task WriteToSheet(SerializedProperty p) {
            var go = p.ReadObject<SheetData>(out var t);
            await WriteToSheet(p, go);
        }

        public static async Task WriteToSheet(SerializedProperty p, SheetData go) {
            if (!SheetData.TryGetFields(go.RowType, out var fields)) {
                Debug.LogError("No serializable fields found in data type.");
                return;
            }

            int2 size = await DetectSize(go);
            if (size.y > 1) {
                bool proceed = EditorUtility.DisplayDialog(
                    "Warning",
                    "There is existing data in Google Sheets. Continuing will delete existing data. Do you want to continue?",
                    "Yes",
                    "No"
                );
                if (!proceed) {
                    Debug.Log("Operation cancelled.");
                    return;
                }
            }

            var dataProperty = p.FindPropertyRelative("data");
            if (dataProperty == null || dataProperty.arraySize == 0) {
                Debug.LogError("No data found in Unity to export.");
                return;
            }

            CellContext ctx = CreateWriteContext(go, fields);
            if (ctx.report.Length > 0) {
                Debug.LogWarning("Mapping warnings:\n" + ctx.report.ToString());
            }

            JSONArray root = new JSONArray();

            JSONArray headerRow = new JSONArray();
            for (int i = 0; i < fields.Length; i++) {
                headerRow.Add(fields[i].Name);
            }
            root.Add(headerRow);

            for (int rowIndex = 0; rowIndex < dataProperty.arraySize; rowIndex++) {
                var rowProperty = dataProperty.GetArrayElementAtIndex(rowIndex);
                JSONArray dataRow = new JSONArray();

                for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++) {
                    var field = fields[fieldIndex];
                    var fieldProperty = FindFieldProperty(rowProperty, field);
                    string cellValue = ConvertToString(fieldProperty, field, ctx.mappings[fieldIndex], ctx);
                    if (double.TryParse(cellValue, out var d))
                    {
                        JSONData dd = new JSONData(d);
                        dataRow.Add(dd);
                    }
                    else
                    {
                        dataRow.Add(cellValue);
                    }
                }

                root.Add(dataRow);
            }

            int totalRows = dataProperty.arraySize + 1; // +1 for header
            int2 writeSize = new int2(fields.Length, totalRows);
            string range = go.tableStart.GetRange(writeSize);

            await go.googleSheet.PutValues(Dimension.ROWS, root, range);

            await UpdateDropdownsCommon(go, dataProperty.arraySize);

            StringBuilder finalReport = new StringBuilder();
            finalReport.AppendLine($"{dataProperty.arraySize} rows of data exported from Unity to Google Sheets.");

            if (ctx.emptyFields.Count > 0) {
                finalReport.AppendLine($"Fields without mapping ({ctx.emptyFields.Count}): {string.Join(", ", ctx.emptyFields)}");
            }

            if (ctx.emptyValueCount > 0) {
                finalReport.AppendLine($"Total {ctx.emptyValueCount} cells written with empty values.");
            }

            Debug.Log(finalReport.ToString(), p?.serializedObject.targetObject);
        }

        private static CellContext CreateWriteContext(SheetData go, Field[] fields) {
            CellContext ctx = default;
            ctx.sheetData = go;
            ctx.report = new StringBuilder();
            ctx.fields = fields;
            ctx.fieldCount = fields.Length;
            ctx.mappings = new AMapping[ctx.fieldCount];
            ctx.emptyValueCount = 0;
            ctx.emptyFields = new List<string>();

            ctx.columnIndexes = new int[ctx.fieldCount];
            ctx.columnIndex = 0;
            ctx.rowIndex = 0;

            PopulateMappings(go, fields, ctx.mappings);

            for (int i = 0; i < ctx.fieldCount; i++) {
                var field = ctx.fields[i];

                if (!IsPrimitive(field.type) && ctx.mappings[i] == null) {
                    ctx.report.AppendLine($"No mapping found for field '{field.Name}', empty value will be written.");
                    ctx.emptyFields.Add(field.Name);
                }
            }

            return ctx;
        }

        private static SerializedProperty FindFieldProperty(SerializedProperty rowProperty, Field field) {
            SerializedProperty current = rowProperty;

            string[] pathParts = field.Name.Split('.');
            for (int i = 0; i < pathParts.Length; i++) {
                current = current.FindPropertyRelative(pathParts[i]);
                if (current == null) {
                    break;
                }
            }

            return current;
        }

        private static string ConvertToString(SerializedProperty property, Field field, AMapping mapping, CellContext ctx) {
            if (property == null) {
                return "";
            }

            if (field.isArray) {
                List<string> arrayValues = new List<string>();
                for (int i = 0; i < property.arraySize; i++) {
                    var elementProperty = property.GetArrayElementAtIndex(i);
                    string elementValue = ConvertSingleValueToString(elementProperty, field.type, mapping, ctx);
                    arrayValues.Add(elementValue);
                }
                return string.Join(",", arrayValues);
            }
            else {
                return ConvertSingleValueToString(property, field.type, mapping, ctx);
            }
        }

        private static string ConvertSingleValueToString(SerializedProperty property, Type fieldType, AMapping mapping, CellContext ctx) {
            if (property == null) {
                return "";
            }

            if (IsPrimitive(fieldType)) {
                return GetPrimitiveString(property, fieldType);
            }
            else {
                if (mapping != null) {
                    object objectValue = GetObjectFromProperty(property, fieldType);
                    if (objectValue != null && mapping.ValidateValue(objectValue)) {
                        // Reverse mapping 
                        return mapping.GetKeyFromObject(objectValue);
                    }
                    else if (objectValue != null) {
                        Debug.LogWarning($"Value '{objectValue}' of type '{fieldType.Name}' not found in mapping, writing empty value.");
                        return "";
                    }
                }
                ctx.emptyValueCount++;
                return "";
            }
        }

        private static string GetPrimitiveString(SerializedProperty property, Type fieldType) {
            if (fieldType == typeof(int)) {
                return property.intValue.ToString(CultureInfo.InvariantCulture);
            }
            else if (fieldType == typeof(string)) {
                return property.stringValue ?? "";
            }
            else if (fieldType == typeof(bool)) {
                return property.boolValue.ToString();
            }
            else if (fieldType == typeof(float)) {
                return property.floatValue.ToString(CultureInfo.InvariantCulture);
            }
            else if (fieldType == typeof(long)) {
                return property.longValue.ToString(CultureInfo.InvariantCulture);
            }
            else if (fieldType == typeof(double)) {
                return property.doubleValue.ToString(CultureInfo.InvariantCulture);
            }
            return "";
        }

        private static object GetObjectFromProperty(SerializedProperty property, Type fieldType) {
            if (property.propertyType == SerializedPropertyType.ObjectReference) {
                return property.objectReferenceValue;
            }
            else if (property.propertyType == SerializedPropertyType.Enum) {
                return Enum.ToObject(fieldType, property.enumValueIndex);
            }
            else if (property.propertyType == SerializedPropertyType.ManagedReference) {
                return property.managedReferenceValue;
            }

            try {
                return property.boxedValue;
            }
            catch {
                return null;
            }
        }

    }
}
