using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using SimpleJSON;

namespace Mobge.Sheets {
    [Serializable]
    public class GoogleSheet {
        public string sheetId;
        public string sheetName;

        public async Task<JSONArray[]> GetValues(Dimension d, params string[] ranges) {
            
            using (HttpClient c = new HttpClient()) {
                return await GetValues(c, d, ranges);
            }
        }
        public async Task<JSONArray[]> GetValues(HttpClient c, Dimension d, params string[] ranges) {
            string apiKey = GoogleSheetCredentials.Instance.apiKey;
            // https://sheets.googleapis.com/v4/spreadsheets/{spreadsheetId}/values/{range}
            var uri = new UriBuilder();
            uri.Reset($"https://sheets.googleapis.com/v4/spreadsheets/{sheetId}/values:batchGet");
            uri.AddParameter("key", apiKey);
            uri.AddParameter("majorDimension", d.ToString());
            for(int i = 0; i < ranges.Length; i++) {
                uri.AddParameter("ranges", sheetName + "!" + ranges[i]);
            }
            var request = new HttpRequestMessage(HttpMethod.Get, uri.Uri);
            
            var response = await c.SendAsync(request);
            JSONArray[] result = new JSONArray[ranges.Length];
            string raw = await response.Content.ReadAsStringAsync();
            var json = SimpleJSON.JSON.Parse(raw);
            var valueRanges = json["valueRanges"].AsArray;
            for(int i = 0; i < valueRanges.Count; i++) {
                var values = valueRanges[i]["values"].AsArray;
                result[i] = values;
            }
            return result;
        }


        
    }
}
