using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using SimpleJSON;
using Unity.Mathematics;
using UnityEngine.Networking;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Mobge.Sheets {
    [Serializable]
    public class GoogleSheet {
        [FormerlySerializedAs("sheetId")] [SerializeField] private string sheetIdOverride;
        public string sheetName;

        public static string OverrideSheetIdPropertyName => nameof(sheetIdOverride);
        
        public string SheetId {
            get {
                var sheetId = CleanSheetId(sheetIdOverride);
                return string.IsNullOrEmpty(sheetId)
                    ? CleanSheetId(GoogleSheetCredentials.Instance.defaultSheetId)
                    : sheetId;
            }
            set => sheetIdOverride = value;
        }

        public string CleanSheetId(string sheetId) {
            return Regex.Replace(sheetId.Trim(), @"\s+", "");
        }

        public async Task<int> GetSheetTabId() {
            UriBuilder b = new UriBuilder($"https://sheets.googleapis.com/v4/spreadsheets/{SheetId}");
            b.AddParameter("ranges", sheetName);
            var req = UnityWebRequest.Get(b.Uri);
            bool success = await GoogleAuthenticator.Instance.TryAddAuthentication(req);
            if(!success) {
                return default;
            }
            await req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success) {
                Debug.LogError(req.error);
                return default;
            }

            var json = SimpleJSON.JSON.Parse(req.downloadHandler.text);
            return json["sheets"][0]["properties"]["sheetId"].AsInt;
        }


        public async Task<JSONArray[]> GetValues(Dimension d, params string[] ranges) {
            var uri = new UriBuilder();
            uri.Reset($"https://sheets.googleapis.com/v4/spreadsheets/{SheetId}/values:batchGet");
            uri.AddParameter("majorDimension", d.ToString());
            for(int i = 0; i < ranges.Length; i++) {
                uri.AddParameter("ranges", sheetName + "!" + ranges[i]);
            }
            var req = UnityWebRequest.Get(uri.Uri);
            bool success = await GoogleAuthenticator.Instance.TryAddAuthentication(req);
            if(!success) {
                return default;
            }
            await req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success) {
                Debug.LogError(req.error);
                return default;
            }

            JSONArray[] result = new JSONArray[ranges.Length];
            var json = SimpleJSON.JSON.Parse(req.downloadHandler.text);
            var valueRanges = json["valueRanges"].AsArray;
            for(int i = 0; i < valueRanges.Count; i++) {
                var values = valueRanges[i]["values"].AsArray;
                result[i] = values;
            }
            return result;
        }
        public struct DropDownData {
            public string[] options;
            public int2 start, size;
            public bool multiSelect;
        }
        public async Task SetDropDowns(DropDownData[] dropDowns) {
            int sheetTabId = await GetSheetTabId();

            JSONClass jRoot = new();
            var jRequests = jRoot["requests"] = new JSONArray();
            for(int i = 0; i < dropDowns.Length; i++) {
                var dropDown = dropDowns[i];
                var jRequest = new JSONClass();
                jRequests.Add(jRequest);
                var jSetDataValidation = jRequest["setDataValidation"] = new JSONClass();
                var jRange = jSetDataValidation["range"] = new JSONClass();
                jRange["sheetId"].AsInt = sheetTabId;
                jRange["startColumnIndex"].AsInt = dropDown.start.x;
                jRange["endColumnIndex"].AsInt = dropDown.start.x + dropDown.size.x;
                jRange["startRowIndex"].AsInt = dropDown.start.y;
                jRange["endRowIndex"].AsInt = dropDown.start.y + dropDown.size.y;
                var jRule = jSetDataValidation["rule"] = new JSONClass();
                jRule["strict"].AsBool = true;
                jRule["showCustomUi"].AsBool = true;
                var jCondition = jRule["condition"] = new JSONClass();
                jCondition["type"] = "ONE_OF_LIST";
                var jValues = jCondition["values"] = new JSONArray();
                foreach(var entry in dropDown.options) {
                    JSONClass jEntry = new();
                    jValues.Add(null, jEntry);
                    jEntry["userEnteredValue"] = entry;
                }
            }
            
            string uri = $"https://sheets.googleapis.com/v4/spreadsheets/{this.SheetId}:batchUpdate";
            var req = new UnityWebRequest(uri, "POST");
            req.downloadHandler = new DownloadHandlerBuffer();
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jRoot.ToString());
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.SetRequestHeader("Content-Type", "application/json;charset=UTF-8");
            
            if(!await GoogleAuthenticator.Instance.TryAddAuthentication(req)){
                Debug.LogError("Authorization failed");
                return;
            }
            
            await req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) {
                Debug.LogError($"Error: {req.error}\n{req.downloadHandler.text}");
            } else {
                Debug.Log(req.downloadHandler.text);
            }
        }

        public async Task PutValues(Dimension d, JSONArray values, string range) {
            range = sheetName + "!" + range;
            UriBuilder b = new();
            b.Reset($"https://sheets.googleapis.com/v4/spreadsheets/{SheetId}/values/{range}");
            b.AddParameter("valueInputOption", "RAW");
            
            var req = new UnityWebRequest(b.Uri, "PUT");
            req.downloadHandler = new DownloadHandlerBuffer();
            
            SimpleJSON.JSONClass c = new JSONClass();
            c["range"] = range;
            c["values"] = values;
            c["majorDimension"] = d.ToString();
            
            byte[] bodyRaw = Encoding.UTF8.GetBytes(c.ToString());
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.SetRequestHeader("Content-Type", "application/json;charset=UTF-8");
            
            if(!await GoogleAuthenticator.Instance.TryAddAuthentication(req)){
                Debug.LogError("Authorization failed");
                return;
            }
            
            await req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) {
                Debug.LogError($"Error: {req.error}\n{req.downloadHandler.text}");
            } else {
                Debug.Log(req.downloadHandler.text);
            }
        }
    }
}
