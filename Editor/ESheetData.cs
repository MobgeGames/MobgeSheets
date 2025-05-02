using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Mobge.Sheets;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mobge.DoubleKing {
    [CustomEditor(typeof(SheetData), true)]
    public class ESheetData : Editor {
        private SheetData _go;

        private List<IEnumerator> _routines;

        protected void OnEnable() {
            _go = target as SheetData;
            _routines = new();
            
        }

        public override void OnInspectorGUI() {
            base.OnInspectorGUI();
            if(GUILayout.Button("Update Sheet")) {
                UpdateSheet();
            }
            if(GUI.changed) {
                EditorExtensions.SetDirty(_go);
            }
            UpdateRoutines();
        }

        private async void UpdateSheet() {
            string range = await DetectRange();
            Debug.Log("Range: " + range);
        }

        private async Task<string> DetectRange() {
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
                return null;
            }
            var nodeH = nodes[0];
            var nodeV = nodes[1];
            int lengthH = 0;
            int lengthV = 0;

            if(nodeH.Count > 0) {
                var valsH = nodeH[0].AsArray;
                for(int i = 0; i < valsH.Count; i++) {
                    if(string.IsNullOrEmpty(valsH.Value)) {
                        break;
                    }
                    lengthH++;
                }
            }
            for(int i = 0; i < nodeV.Count; i++) {
                if(nodeV[i].AsArray.Count == 0) {
                    break;
                }
                lengthV++;
            }

            string range = start.column + start.row;
            range += ":";
            range += CellId.Add(start.column, lengthH);
            range += start.row + lengthV;

            return range;
        }

        private async void TestGet() {
            using (HttpClient c = new HttpClient()) {
                string sheetId = "1qJTeRzQZri03qlbtThP536iyxHRcYoi0FTGo_utEj_k";
                string apiKey = GoogleSheetCredentials.Instance.apiKey;
                var request = new HttpRequestMessage(new HttpMethod("GET"), $"https://sheets.googleapis.com/v4/spreadsheets/{sheetId}?");
                
                request.Properties.Add("key", apiKey);
                var response = await c.SendAsync(request);
                string r = await response.Content.ReadAsStringAsync();
                Debug.Log(r);
            }
        }

        private void UpdateRoutines() {
            for(int i = 0; i < _routines.Count; ) {
                var r = _routines[i];
                if(!r.MoveNext()) {
                    _routines.RemoveAt(i);
                }
                else {
                    i++;
                }
            }
            if(_routines.Count > 0) {
                Repaint();
            }
        }
    }
}
