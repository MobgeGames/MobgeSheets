using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GameAnalyticsSDK.Utilities;
using Mobge.DoubleKing;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;
using Random = UnityEngine.Random;

namespace Mobge.Sheets  {
	//TODO made instance
	public class SheetCacher {

		public static SheetCacher _instance;
		public static SheetCacher Instance => _instance ??= new SheetCacher();

		public static void InjectInstance(SheetCacher sheetCacher) {
			_instance = sheetCacher;
		}
		
		private static string RootFolderPath => Path.Combine(Application.persistentDataPath, "Sheets");

		public virtual bool TryGetValues(GoogleSheet sheet, Dimension dimension, string[] ranges,
			out JSONArray[] result) {
			return TryGetValues(sheet.sheetId, sheet.sheetName, dimension, ranges, out result);
		}

		public async Task TestCacher(GoogleSheet sheet, Dimension dimension, string[] ranges) {
			Debug.Log($"Testing cacher on {sheet.sheetName}, {ranges}.");
			var hasCache = TryGetValues(sheet.sheetId, sheet.sheetName, dimension, ranges, out var cacheResult);
			if (!hasCache) {
				Debug.Log($"The sheet is not cached, was not able to test on {sheet.sheetName}.");
				return;
			}
			var webRequestResult = await sheet.GetValues(dimension, ranges);
			var cacheText = SheetData.ResultToText(cacheResult);
			var webRequestText = SheetData.ResultToText(webRequestResult);
			var testPassed = cacheText == webRequestText;
			if (testPassed) {
				Debug.Log($"The test passed.");
			} else {
				Debug.LogError($"The test failed.");
				Debug.Log($"Text from cache; \\n" + cacheText);
				Debug.Log($"Text from web request; \\n" + webRequestText);
			}
		}
		
		public bool TryGetValues(string spreadSheetId, string sheetName, Dimension dimension, string[] ranges, out JSONArray[] result) {
			Debug.Log($"Trying to get sheet data from cache {spreadSheetId}");
			var filePath = Path.Combine(RootFolderPath, spreadSheetId, sheetName + ".csv");
			if (!File.Exists(filePath)) {
				Debug.Log($"Sheet not found on cache {spreadSheetId}");
				result = null;
				return false;
			}
			
			var raw = File.ReadAllText(filePath);
			var grid = ParseCsv(raw);
			
			int rowCount = grid.Length;
			int colCount = 0;
			for (int i = 0; i < grid.Length; i++) {
				if (grid[i].Length > colCount) colCount = grid[i].Length;
			}
			Vector2Int size = new Vector2Int(colCount, rowCount);
			
			result = new JSONArray[ranges.Length];
			for(int i = 0; i < ranges.Length; i++) {
				if(TryGetRange(ranges[i], size, out var start, out var end)) {
					var rangeResult = new JSONArray();
					if (dimension == Dimension.ROWS) {
						for (int y = start.y; y <= end.y; y++) {
							var json = new JSONArray();
							for (int x = start.x; x <= end.x; x++) {
								if (grid[y].Length <= x) {
									continue;
								}

								if (string.IsNullOrEmpty(grid[y][x])) {
									json.Add(new JSONData(String.Empty));
									
								} else {
									json.Add(grid[y][x]);
								}
							}

							rangeResult.Add(json);
						}
					}
					else {
						for (int x = start.x; x <= end.x; x++) {
							var json = new JSONArray();
							for (int y = start.y; y <= end.y; y++) {
								if (grid[y].Length <= x) {
									continue;
								}
								
								if (string.IsNullOrEmpty(grid[y][x])) {
									json.Add(new JSONData(String.Empty));
									
								} else {
									json.Add(grid[y][x]);
								}
							}
							
							rangeResult.Add(json);
						}
					}

					for (int j = 0; j < rangeResult.Count; j++) {
						var json = rangeResult[j];
						for (int k = json.Count - 1; k >= 0; k--) {
							if (!string.IsNullOrEmpty(json[k])) {
								break;
							}

							json.Remove(k);
						}
					}

					for (int j = rangeResult.Count - 1; j >= 0; j--) {
						if (rangeResult[j].Count > 0) {
							break;
						}

						rangeResult.Remove(j);
					}
					
					result[i] = rangeResult;
				}
				else {
					result[i] = new JSONArray();
				}
			}
			
			Debug.Log($"Sheet found on cache returning result {spreadSheetId}");
			return true;
		}
		protected string[][] ParseCsv(string text) {
			var lines = text.Split('\n');
			var result = new string[lines.Length][];
			for (var index = 0; index < lines.Length; index++) {
				result[index] = lines[index].Split(',');
			}
			return result;
		}
		protected bool TryGetRange(string range, Vector2Int size, out Vector2Int start, out Vector2Int end) {
            start = Vector2Int.zero;
            end = Vector2Int.zero;
            
            var parts = range.Split('!');
            var rangePart = parts[parts.Length - 1];
            
            var subParts = rangePart.Split(':');
            if (subParts.Length != 2) return false;
            
            if (!TryParseCell(subParts[0], size, true, out start)) return false;
            if (!TryParseCell(subParts[1], size, false, out end)) return false;
            
            return true;
        }
        protected bool TryParseCell(string cell, Vector2Int size, bool isMin, out Vector2Int coord) {
            coord = Vector2Int.zero;
            string letters = "";
            string numbers = "";
            
            foreach (char c in cell) {
                if (char.IsLetter(c)) letters += c;
                else if (char.IsDigit(c)) numbers += c;
            }
            
            if (string.IsNullOrEmpty(letters) && string.IsNullOrEmpty(numbers)) return false;

            if(string.IsNullOrEmpty(letters)) {
                if(isMin) {
                    coord.x = 0;
                }
                else {
                    coord.x = size.x - 1;
                }
            } else {
                coord.x = CellId.ColumnToIndex(letters) - 1;
            }

            if(string.IsNullOrEmpty(numbers)) {
                if(isMin) {
                    coord.y = 0;
                }
                else {
                    coord.y = size.y - 1;
                }
            }
            else {
                if (int.TryParse(numbers, out int row)) {
                    coord.y = row - 1; // 0-based
                }
            }
            
            
            return true;
        }
		public virtual async Task CacheSheet(string sheetId) {
			Debug.Log($"Caching sheet {sheetId}");
			var folderPath = Path.Combine(RootFolderPath, sheetId);
			if (Directory.Exists(folderPath)) {
				Debug.Log($"Sheet already cached, skipping {sheetId}");
				return;
			}
			await DownloadSpreadSheet(sheetId);
		}
		protected static async Task DownloadSpreadSheet(string spreadSheetID) {
			Debug.Log($"Starting spread sheet with id {spreadSheetID} downloading...");
			var sheetNames = await GetSheetNames(spreadSheetID);
			if (sheetNames != null && sheetNames.Count > 0) {
				await DownloadAllSheets(spreadSheetID, sheetNames);
			}
			Debug.Log($"Spread sheet with id {spreadSheetID} downloaded...");
		}
		protected static async Task<List<string>> GetSheetNames(string spreadSheetID)
		{
			string url = $"https://sheets.googleapis.com/v4/spreadsheets/{spreadSheetID}";
			var req = UnityWebRequest.Get(url);
			bool success = await GoogleAuthenticator.Instance.TryAddAuthentication(req, true);
			if (!success) {
				Debug.LogError("Google authentication failed!");
				return null;
			}
			await req.SendWebRequest();
			if (req.result != UnityWebRequest.Result.Success) {
				Debug.LogError(req.error);
				return null;
			}
			
			var json = req.downloadHandler.text;
			// Using MiniJSON here as well for consistency, though JsonUtility worked for this part
			var data = GA_MiniJSON.Deserialize(json) as Dictionary<string, object>;
			
			List<string> sheetNames = new List<string>();
			if (data != null && data.ContainsKey("sheets")) {
				var sheets = data["sheets"] as List<object>;
				foreach (var s in sheets) {
					var sheetDict = s as Dictionary<string, object>;
					if (sheetDict != null && sheetDict.ContainsKey("properties")) {
						var props = sheetDict["properties"] as Dictionary<string, object>;
						if (props != null && props.ContainsKey("title")) {
							string title = props["title"] as string;
							sheetNames.Add(title);
						}
					}
				}
			}
			return sheetNames;
		}
		protected static async Task DownloadAllSheets(string spreadsheetId, IList<string> sheetNames) {
			var url = $"https://sheets.googleapis.com/v4/spreadsheets/{spreadsheetId}/values:batchGet?";
			foreach (var sheet in sheetNames)
				url += $"ranges={UnityWebRequest.EscapeURL(sheet)}&";

			Debug.Log($"Requesting batch download...");
			var req = UnityWebRequest.Get(url);
			bool success = await GoogleAuthenticator.Instance.TryAddAuthentication(req, true);
			if (!success) return;

			await req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success) {
				Debug.LogError($"Batch download error: {req.error}");
				return;
			}

			var json = req.downloadHandler.text;
			// Debug.Log($"Batch JSON: {json}"); // Too large to log typically

			var data = GA_MiniJSON.Deserialize(json) as Dictionary<string, object>;
			var rnd = Random.Range(1000, 10000);
			var tempFolderPath = Path.Combine(RootFolderPath, $"{spreadsheetId}_{rnd}");
			if (data != null && data.ContainsKey("valueRanges")) {
				var valueRanges = data["valueRanges"] as List<object>;
				foreach (var rangeObj in valueRanges) {
					var rangeDict = rangeObj as Dictionary<string, object>;
					if (rangeDict == null) continue;

					string range = rangeDict.ContainsKey("range") ? rangeDict["range"] as string : "Unknown";

					List<List<string>> rows = new List<List<string>>();
					if (rangeDict.ContainsKey("values")) {
						var valuesObj = rangeDict["values"] as List<object>;
						if (valuesObj != null) {
							foreach (var rowObj in valuesObj) {
								var rowList = rowObj as List<object>;
								List<string> rowStrs = new List<string>();
								if (rowList != null) {
									foreach (var cell in rowList) {
										rowStrs.Add(cell == null ? "" : cell.ToString());
									}
								}

								rows.Add(rowStrs);
							}
						}
					}

					string csv = ConvertToCsv(rows);
					await SaveCsv(tempFolderPath, range, csv);
				}
				
				var newFolderPath = Path.Combine(RootFolderPath, spreadsheetId);
				Directory.Move(tempFolderPath, newFolderPath);
			}
		}
		protected static string ConvertToCsv(List<List<string>> rows)
		{
			if (rows == null) return "";

			var sb = new StringBuilder();

			foreach (var row in rows)
			{
				for (int i = 0; i < row.Count; i++)
				{
					string cell = row[i] ?? "";

					// CSV escape
					if (cell.Contains(",") || cell.Contains("\"") || cell.Contains("\n"))
						cell = $"\"{cell.Replace("\"", "\"\"")}\"";

					sb.Append(cell);
					if (i < row.Count - 1)
						sb.Append(",");
				}
				sb.AppendLine();
			}

			return sb.ToString();
		}
		protected static async Task SaveCsv(string folderPath, string range, string csv)
		{
			// "Users!A1:D20" → "Users"
			string sheetName = range.Split('!')[0];
			// Clean up sheet name just in case
			sheetName = sheetName.Replace("'", ""); 

			if (!Directory.Exists(folderPath)) {
				Directory.CreateDirectory(folderPath);
			}
			
			string path = Path.Combine(folderPath, $"{sheetName}.csv");
			await File.WriteAllTextAsync(path, csv, Encoding.UTF8);
			Debug.Log($"Saved: {path}");
		}
	}
}