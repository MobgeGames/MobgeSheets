using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Mobge.Serialization;
using SimpleJSON;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Mobge.Sheets {
    public partial class ESheetData {
        public static async void UpdateFromSheet(SerializedProperty p) {
            var go = p.ReadObject<SheetData>(out var t);
            int2 size = await DetectSize(go);
            var range = go.tableStart.GetRange(size);
            await ReadFromSheet(p, go, range);
        }

        private static async Task ReadFromSheet(SerializedProperty p, SheetData go, string range) {
            var result = await go.googleSheet.GetValues(Dimension.ROWS, range);
            var nodes = result[0];
            int rowCount = nodes.Count - 1;
            var header = nodes[0];
            ReadCellContext ctx;
            ctx.sheetData = go;
            ctx.report = new();
            ctx.report.Append("Updating Sheet: (");
            ctx.report.Append(p.serializedObject.targetObject.name);
            ctx.report.Append(", ");
            ctx.report.Append(p.propertyPath);
            ctx.report.Append(")");

            ctx.report.AppendLine(" Data count: " + rowCount);
            SheetData.TryGetFields(go.RowType, out var fields);
            int fieldCount = fields.GetLength();
            int[] columnIndexes = new int[fieldCount];
            SheetData.AMapping[] mappings = new SheetData.AMapping[fieldCount];
            for(int i = 0; i < fieldCount; i++) {
                var field = fields[i];
                int selectedIndex = -1;
                for(int ih = 0; ih < header.Count; ih++) {
                    var columnCell = header[ih];
                    if(columnCell.Value.Equals(field.Name, StringComparison.InvariantCultureIgnoreCase)) {
                        selectedIndex = ih;
                        break;
                    }
                }
                columnIndexes[i] = selectedIndex;
                if(selectedIndex < 0) {
                    ctx.report.AppendLine("No column found for field: " + field.Name);
                }

                if(!IsPrimitive(field.type)) {
                    int mappingCount = go.mappings.GetLength();
                    SheetData.AMapping selectedMapping = default;
                    for(int im = 0; im < mappingCount; im++) {
                        var mapping = go.mappings[im];
                        if(mapping.fieldName == field.Name) {
                            selectedMapping = mapping.mapping;
                            break;
                        }
                    }
                    mappings[i] = selectedMapping;
                    if(selectedMapping == null) {
                        ctx.report.AppendLine("No mapping found for column: " + field.Name);
                    }
                }
            }
            object[] data = new object[rowCount];
            for(int i = 0; i < rowCount; i++) {
                var rowCells = nodes[i + 1].AsArray;
                object rowData = Activator.CreateInstance(go.RowType);
                for(int iField = 0; iField < fieldCount; iField++) {
                    ctx.columnIndex = columnIndexes[iField];
                    if(ctx.columnIndex < 0) {
                        continue;
                    }
                    ctx.rowIndex = i;
                    var field = fields[iField];
                    var textValue = rowCells[ctx.columnIndex].Value;
                    object value = default;
                    if(field.isArray) {
                        var values = textValue.Split(',');
                        var arr = Array.CreateInstance(field.type, values.Length);
                        for(int v = 0; v < values.Length; v++) {
                            string arrValue = values[v];
                            var o = ConvertToObject(arrValue, field, mappings[iField], ctx);
                            arr.SetValue(o, v);
                        }
                        value = arr;
                    }
                    else {
                        value = ConvertToObject(textValue, field, mappings[iField], ctx);
                    }
                    field.fieldInfo.SetValue(rowData, value);
                }
                data[i] = rowData;
            }
            Undo.RecordObject(p.serializedObject.targetObject, "Update data from sheet");
            go.UpdateData(data);
            p.WriteObject(go);
            p.serializedObject.ApplyModifiedProperties();
            EditorExtensions.SetDirty(p.serializedObject.targetObject);
            Debug.Log(ctx.report, p.serializedObject.targetObject);
            
        }
        private static object ConvertToObject(string textValue, SheetData.Field field, SheetData.AMapping mapping, in ReadCellContext ctx) {
            textValue = textValue.Trim(SheetData.s_trimChars);
            object value = null;
            if(IsPrimitive(field.type)) {
                value = GetPrimitiveValue(textValue, field.type);
            }
            else {
                if(mapping != null) {
                    value = mapping.GetObjectRaw(textValue);
                    //Debug.Log($"value validated: {mapping}, {value} : {mapping.ValidateValue(value)}");
                    if(!mapping.ValidateValue(value)) {
                        var ts = ctx.sheetData.tableStart;
                        ts.column = CellId.Add(ts.column, ctx.columnIndex);
                        ts.row += ctx.rowIndex + 1;
                        ctx.report.AppendLine($"Mapping error at cell: {ts.column}:{ts.row}");
                    }
                }
            }
            return value;
        }

        private static object GetPrimitiveValue(string textValue, Type t) {
            object value = null;
            if(t == typeof(int)) {
                int.TryParse(textValue, out int i);
                value = i;
            }
            else if(t == typeof(string)) {
                value = textValue;
            }
            else if(t == typeof(bool)) {
                bool.TryParse(textValue, out bool i);
                value = i;
            }
            else if(t == typeof(float)) {
                float.TryParse(textValue, out float i);
                value = i;
            }
            else if(t == typeof(long)) {
                long.TryParse(textValue, out long i);
                value = i;
            }
            else if(t == typeof(double)) {
                double.TryParse(textValue, out double i);
                value = i;
            }
            return value;
        }

        public static async Task<int2>DetectSize(SheetData go) {
            var start = go.tableStart;
            if(string.IsNullOrEmpty(start.column)) {
                start.column = "A";
            }
            if(start.row <= 0) {
                start.row = 1;
            }
            string rangeH = start.column + start.row + ':' + start.row;
            string rangeV = start.column + start.row + ':' + start.column;
            var nodes = await go.googleSheet.GetValues(Dimension.ROWS, rangeH, rangeV);
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

        private void UpdateSheetField(Meta meta, SerializedProperty p) {
            meta.updateFieldOpen = EditorGUI.Foldout(s_layout.NextRect(), meta.updateFieldOpen, "Update Sheet", true);
            if(meta.updateFieldOpen) {
                EditorGUI.indentLevel++;
                s_layout.NextSplitRect(s_layout.Width * 0.5f, out var rCount, out var rButtons, 5);
                LayoutRectSource.SplitRect(rButtons, rButtons.width * 0.5f, out var rCreate, out var rDropdowns);
                meta.rowCount = EditorGUI.IntField(rCount, "Row Count", meta.rowCount);
                meta.rowCount = Mathf.Max(meta.rowCount, 1);
                if(GUI.Button(rCreate, "Create Template")) {
                    TryCreateTemplate(p, meta.rowCount);
                }
                if(GUI.Button(rDropdowns, "Update Dropdowns")) {
                    var _go = p.ReadObject<SheetData>(out _);
                    TryUpdateDropdowns(_go);
                }
                EditorGUI.indentLevel--;
            }
            
        }
        private async void TryCreateTemplate(SerializedProperty p, int rowCount) {
            var _go = p.ReadObject<SheetData>(out _);
            if(!SheetData.TryGetFields(_go.RowType, out var fields)) {
                Debug.LogError("Data Has no serializable fields.");
                return;
            }
            int2 size = new int2(fields.Length, rowCount + 1);
            string range = _go.tableStart.GetRange(size);
            var values = await _go.googleSheet.GetValues(Dimension.ROWS, range);
            if(values == null || values.Length == 0) {
                Debug.LogError("Failed to access sheet.");
                return;
            }
            if(values[0].AsArray.Count > 0) {
                Debug.LogError("There is no enough empty space in sheet.");
                return;
            }
            JSONArray root = new JSONArray();
            JSONArray row = new JSONArray();
            for(int i = 0; i < fields.Length; i++) {
                row.Add(fields[i].Name);
            }
            root[0] = row;
            await _go.googleSheet.PutValues(Dimension.ROWS, root, range);
            TryUpdateDropdowns(_go, rowCount);
        }
        private async void TryUpdateDropdowns(SheetData go) {
            int2 size = await DetectSize(go);
            if(size.y < 2) {
                Debug.LogError("Table has no rows.");
                return;
            }
            TryUpdateDropdowns(go, size.y - 1);

        }
        private async void TryUpdateDropdowns(SheetData _go, int rowCount) {
            if(_go.mappings.IsNullOrEmpty()) {
                Debug.LogError("No mapping defined.");
                return;
            }
            CellId start = _go.tableStart;
            string headerRange = start.column + start.row + ":" + start.row;
            var jHeaderValues = (await _go.googleSheet.GetValues(Dimension.ROWS, headerRange))[0];
            if(jHeaderValues.Count == 0) {
                Debug.LogError("Header row is not found.");
                return;
            }
            var jHeaderRow = jHeaderValues[0];
            List<GoogleSheet.DropDownData> d = new();
            List<string> allKeys = new();
            SheetData.TryGetFields(_go.RowType, out var fields);
            for(int i = 0; i < _go.mappings.Length; i++) {
                int columnOffset = -1;
                var mapping = _go.mappings[i];
                for(int j = 0; j < jHeaderRow.Count; j++) {
                    if(jHeaderRow[j].Value == mapping.fieldName) {
                        columnOffset = j;
                        break;
                    }
                }
                if(columnOffset == -1) {
                    Debug.LogError($"Column {mapping.fieldName} is not found.");
                    continue;
                }
                allKeys.Clear();
                mapping.mapping.GetAllKeys(allKeys);
                if(allKeys.Count == 0) {
                    Debug.LogError($"Mapping {mapping.fieldName} is not configured.");
                    continue;
                }
                GoogleSheet.DropDownData dd;
                dd.options = allKeys.ToArray();
                dd.start = start.ZeroBasedIndex;
                dd.start.x += columnOffset;
                dd.start.y += 1;
                dd.size = new int2(1, rowCount);
                dd.multiSelect = false;
                if(fields != null) {
                    for(int f = 0; f < fields.Length; f++) {
                        if(fields[f].Name == mapping.fieldName) {
                            dd.multiSelect = fields[f].isArray;
                        }
                    }
                }
                d.Add(dd);
            }
            if(d.Count == 0) {
                Debug.LogError("No dropdown configured.");
                return;
            }
            await _go.googleSheet.SetDropDowns(d.ToArray());
        }
        private struct ReadCellContext {
            public StringBuilder report;
            public int columnIndex;
            public int rowIndex;
            public SheetData sheetData;

        }
    }
}
