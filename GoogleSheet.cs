using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using SimpleJSON;
using System.Net;
using Mobge.DoubleKing;
using Unity.Mathematics;
using UnityEngine.UI;

namespace Mobge.Sheets {
    [Serializable]
    public class GoogleSheet {
        public string sheetId;
        public string sheetName;

        public async Task<int> GetSheetTabId() {
            UriBuilder b = new UriBuilder($"https://sheets.googleapis.com/v4/spreadsheets/{sheetId}");
            b.AddParameter("ranges", sheetName);
            WebRequest r = WebRequest.Create(b.Uri);
            bool success = await GoogleAuthenticator.Instance.TryAddAccessToken(r);
            if(!success) {
                return default;
            }
            r.Method = "GET";
            var response = await r.GetResponseAsync();
            using(StreamReader reader = new StreamReader(response.GetResponseStream())) {
                string raw = await reader.ReadToEndAsync();
                var json = SimpleJSON.JSON.Parse(raw);
                return json["sheets"][0]["properties"]["sheetId"].AsInt;
            }
            //response["sheets"][0]
        }


        public async Task<JSONArray[]> GetValues(Dimension d, params string[] ranges) {
            //string apiKey = GoogleSheetCredentials.Instance.apiKey;
            // https://sheets.googleapis.com/v4/spreadsheets/{spreadsheetId}/values/{range}
            //string token = await GoogleAuthenticator.Instance.GetAccessToken();
            
            var uri = new UriBuilder();
            uri.Reset($"https://sheets.googleapis.com/v4/spreadsheets/{sheetId}/values:batchGet");
            //uri.AddParameter("key", apiKey);
            uri.AddParameter("majorDimension", d.ToString());
            for(int i = 0; i < ranges.Length; i++) {
                uri.AddParameter("ranges", sheetName + "!" + ranges[i]);
            }
            WebRequest r = WebRequest.Create(uri.Uri);
            bool success = await GoogleAuthenticator.Instance.TryAddAccessToken(r);
            if(!success) {
                return default;
            }
            r.Method = "GET";
            var response = await r.GetResponseAsync();
            JSONArray[] result = new JSONArray[ranges.Length];
            using(StreamReader reader = new StreamReader(response.GetResponseStream())) {
                string raw = await reader.ReadToEndAsync(); 
                var json = SimpleJSON.JSON.Parse(raw);
                var valueRanges = json["valueRanges"].AsArray;
                for(int i = 0; i < valueRanges.Count; i++) {
                    var values = valueRanges[i]["values"].AsArray;
                    result[i] = values;
                }
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
            
            string uri = $"https://sheets.googleapis.com/v4/spreadsheets/{this.sheetId}:batchUpdate";
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(uri);
            r.Method = "POST";
            r.ContentType = "application/json;charset=UTF-8";
            if(!await GoogleAuthenticator.Instance.TryAddAccessToken(r)){
                Debug.LogError("Authorization failed");
                return;
            }
            string sContent = jRoot.ToString();
            //Debug.Log(sContent);
            byte[] contentBytes = Encoding.UTF8.GetBytes(sContent);
            r.ContentLength = contentBytes.Length;
            using(var stream = r.GetRequestStream()) {
                await stream.WriteAsync(contentBytes, 0, contentBytes.Length);
            }
            try {
                var response = await r.GetResponseAsync();
                using(StreamReader s = new StreamReader(response.GetResponseStream())) {
                    Debug.Log(await s.ReadToEndAsync());
                }
            }
            catch(WebException we) {
                using(StreamReader s = new StreamReader(we.Response.GetResponseStream())) {
                    Debug.LogError(await s.ReadToEndAsync());
                }
            }
        }

        public async Task PutValues(Dimension d, JSONArray values, string range) {
            range = sheetName + "!" + range;
            UriBuilder b = new();
            b.Reset($"https://sheets.googleapis.com/v4/spreadsheets/{sheetId}/values/{range}");
            b.AddParameter("valueInputOption", "RAW");
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(b.Uri);
            //Debug.Log(b.Uri);
            r.Method = "PUT";
            r.ContentType = "application/json;charset=UTF-8";
            r.Accept = "Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            if(!await GoogleAuthenticator.Instance.TryAddAccessToken(r)){
                Debug.LogError("Authorization failed");
                return;
            }
            SimpleJSON.JSONClass c = new JSONClass();
            c["range"] = range;
            c["values"] = values;
            c["majorDimension"] = d.ToString();
            StringBuilder sb = new();
            c.ToJSON(sb);
            //Debug.Log(sb);
            byte[] contentBytes = Encoding.UTF8.GetBytes(sb.ToString());
            r.ContentLength = contentBytes.Length;
            using(var stream = r.GetRequestStream()) {
                await stream.WriteAsync(contentBytes, 0, contentBytes.Length);
            }

            try {
                var response = await r.GetResponseAsync();
                using(StreamReader s = new StreamReader(response.GetResponseStream())) {
                    Debug.Log(await s.ReadToEndAsync());
                }
            }
            catch(WebException we) {
                using(StreamReader s = new StreamReader(we.Response.GetResponseStream())) {
                    Debug.LogError(await s.ReadToEndAsync());
                }
            }
        }
    }
}
