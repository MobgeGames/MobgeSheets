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
         private async void UpdateFromSheet() {
            int2 size = await DetectSize();
            var range = _go.tableStart.GetRange(size);
            await ReadFromSheet(range);
        }

        private async Task ReadFromSheet(string range) {
            var result = await _go.googleSheet.GetValues(Dimension.ROWS, range);
            var nodes = result[0];
            int rowCount = nodes.Count - 1;
            var header = nodes[0];
            StringBuilder report = new();
            report.Append("Updating Sheet: " + _go.name);
            report.AppendLine(" Data count: " + rowCount);
            BinarySerializer.TryGetFields(_go.RowType, out var fields);
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
                    report.AppendLine("No column found for field: " + field.Name);
                }

                if(!IsPrimitive(field.FieldType)) {
                    int mappingCount = _go.mappings.GetLength();
                    SheetData.AMapping selectedMapping = default;
                    for(int im = 0; im < mappingCount; im++) {
                        var mapping = _go.mappings[im];
                        if(mapping.fieldName == field.Name) {
                            selectedMapping = mapping.mapping;
                            break;
                        }
                    }
                    mappings[i] = selectedMapping;
                    if(selectedMapping == null) {
                        report.AppendLine("No mapping found for column: " + field.Name);
                    }
                }
            }
            object[] data = new object[rowCount];
            for(int i = 0; i < rowCount; i++) {
                var rowCells = nodes[i + 1].AsArray;
                object rowData = Activator.CreateInstance(_go.RowType);
                for(int iField = 0; iField < fieldCount; iField++) {
                    int columnIndex = columnIndexes[iField];
                    if(columnIndex < 0) {
                        continue;
                    }
                    var field = fields[iField];
                    var cellNode = rowCells[columnIndex];
                    object value = default;
                    if(IsPrimitive(field.FieldType)) {
                        value = GetPrimitiveValue(ref rowData, cellNode, field.FieldType);
                    }
                    else {
                        var mapping = mappings[iField];
                        if(mapping != null) {
                            value = mapping.GetObjectRaw(cellNode.Value);
                        }
                    }
                    field.SetValue(rowData, value);
                }
                data[i] = rowData;
            }
            Undo.RecordObject(_go, "Update data from sheet");
            _go.UpdateData(data);
            EditorExtensions.SetDirty(_go);
            Debug.Log(report);
            
        }

        private object GetPrimitiveValue(ref object rowData, JSONNode cellNode, Type t) {
            object value = null;
            if(t == typeof(int)) {
                value = cellNode.AsInt;
            }
            else if(t == typeof(string)) {
                value = cellNode.Value;
            }
            else if(t == typeof(bool)) {
                value = cellNode.AsBool;
            }
            else if(t == typeof(float)) {
                value = cellNode.AsFloat;
            }
            else if(t == typeof(long)) {
                value = cellNode.AsInt;
            }
            else if(t == typeof(double)) {
                value = cellNode.AsDouble;
            }
            return value;
        }

        private async Task<int2> DetectSize() {
            var start = _go.tableStart;
            if(string.IsNullOrEmpty(start.column)) {
                start.column = "A";
            }
            if(start.row <= 0) {
                start.row = 1;
            }
            string rangeH = start.column + start.row + ':' + start.row;
            string rangeV = start.column + start.row + ':' + start.column;
            var nodes = await _go.googleSheet.GetValues(Dimension.ROWS, rangeH, rangeV);
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
    }
}
