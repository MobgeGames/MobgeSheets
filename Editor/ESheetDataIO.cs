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
        private static async Task UpdateDropdownsCommon(SheetData go, int rowCount, CellContext ctx) {
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
                int fieldOffset = -1;
                for (int f = 0; f < fields.Length; f++)
                {
                    if (fields[f].Name == mapping.fieldName)
                    {
                        fieldOffset = f;
                        columnOffset = ctx.columnIndexes[f];
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
                dd.multiSelect = fields[fieldOffset].isArray;

                dropdowns.Add(dd);
            }

            if (dropdowns.Count > 0) {
                await go.googleSheet.SetDropDowns(dropdowns.ToArray());
            }
        }
        public static async Task UpdateFromSheet(SerializedProperty p) {
            Undo.RecordObject(p.serializedObject.targetObject, "Update data from sheet");
            var sheetData = p.ReadObject<SheetData>(out var t);
            await SheetData.UpdateFromSheet(p.serializedObject.targetObject, sheetData, p.propertyPath);
            p.WriteObject(sheetData);
            p.serializedObject.ApplyModifiedProperties();
            EditorExtensions.SetDirty(p.serializedObject.targetObject);
        }
        private static void CreateReportHeader(SerializedProperty p, string label, CellContext ctx, int rowCount) {
            ctx.report.Append(label);
            ctx.report.Append(": (");
            if (p != null)
            {
                ctx.report.Append(p.serializedObject.targetObject.name);
                ctx.report.Append(", ");
                ctx.report.Append(p.propertyPath);
            }
            else
            {
                ctx.report.Append("Unknown");
            }
            ctx.report.Append(")");

            ctx.report.AppendLine(" Data count: " + rowCount);
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
            var ctx = FindMapping(_go, row);
            await UpdateDropdownsCommon(_go, rowCount, ctx);
        }
        private async void TryUpdateDropdowns(SheetData go) {
            (var size, var header) = await DetectSizeAndHeader(go);
            if (size.y < 2) {
                Debug.LogError("Table has no rows.");
                return;
            }
            var ctx = FindMapping(go, header);
            await UpdateDropdownsCommon(go, size.y - 1, ctx);

        }
        public static async Task WriteToSheet(SerializedProperty p) {
            var go = p.ReadObject<SheetData>(out var t);
            await WriteToSheet(p, go);
        }
        public static async Task WriteToSheet(SerializedProperty p, SheetData go)
        {
            if (!SheetData.TryGetFields(go.RowType, out var fields))
            {
                Debug.LogError("No serializable fields found in data type.");
                return;
            }
            (int2 size, JSONArray header) = await DetectSizeAndHeader(go);
            
            var dataProperty = p.FindPropertyRelative("data");
            int totalRows = dataProperty.arraySize;
            int2 writeSize = new int2(size.x, totalRows);
            var start = go.tableStart + new int2(0, 1);
            string range = start.GetRange(writeSize);

            var contentData = await go.googleSheet.GetValues(Dimension.ROWS, range);
            if (contentData[0].Count > 0)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Warning",
                    "There is existing data in Google Sheets. Continuing will delete existing data. Do you want to continue?",
                    "Yes",
                    "No"
                );
                if (!proceed)
                {
                    Debug.Log("Operation cancelled.");
                    return;
                }
            }

            if (dataProperty == null || dataProperty.arraySize == 0)
            {
                Debug.LogError("No data found in Unity to export.");
                return;
            }
            if (size.x == 0)
            {
                Debug.LogError("Header is not found.");
                return;
            }

            CellContext ctx = FindMapping(go, header);
            CreateReportHeader(p, "Writing To Sheet", ctx, dataProperty.arraySize);

            JSONArray root = new JSONArray();
            for (int rowIndex = 0; rowIndex < dataProperty.arraySize; rowIndex++)
            {
                var rowProperty = dataProperty.GetArrayElementAtIndex(rowIndex);
                JSONArray dataRow = new JSONArray();
                
                for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                {
                    var field = fields[fieldIndex];
                    ctx.columnIndex = ctx.columnIndexes[fieldIndex];
                    if (ctx.columnIndex < 0)
                    {
                        continue;
                    }
                    while (dataRow.Count <= ctx.columnIndex)
                    {
                        dataRow.Add("");
                    }
                    var fieldProperty = FindFieldProperty(rowProperty, field);
                    string cellValue = ConvertToString(fieldProperty, field, ctx.mappings[fieldIndex], ctx);

                    if (double.TryParse(cellValue, out var d))
                    {
                        JSONData dd = new JSONData(d);
                        dataRow[ctx.columnIndex] = dd;
                    }
                    else
                    {
                        dataRow[ctx.columnIndex] = cellValue;
                    }
                }

                root.Add(dataRow);
            }

            

            await go.googleSheet.PutValues(Dimension.ROWS, root, range);

            await UpdateDropdownsCommon(go, dataProperty.arraySize, ctx);

            
            ctx.report.AppendLine($"{dataProperty.arraySize} rows of data exported from Unity to Google Sheets.");

            if (ctx.emptyFields.Count > 0)
            {
                ctx.report.AppendLine($"Fields without mapping ({ctx.emptyFields.Count}): {string.Join(", ", ctx.emptyFields)}");
            }

            if (ctx.emptyValueCount > 0)
            {
                ctx.report.AppendLine($"Total {ctx.emptyValueCount} cells written with empty values.");
            }

            Debug.Log(ctx.report, p?.serializedObject.targetObject);
        }
        private static SerializedProperty FindFieldProperty(SerializedProperty rowProperty, Field field)
        {
            SerializedProperty current = rowProperty;

            string[] pathParts = field.Name.Split('.');
            for (int i = 0; i < pathParts.Length; i++)
            {
                current = current.FindPropertyRelative(pathParts[i]);
                if (current == null)
                {
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
                return string.Join(", ", arrayValues);
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
