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

namespace Mobge.Sheets  {
	public class SheetCacher {
		private static string RootFolderPath => Path.Combine(Application.persistentDataPath, "Sheets");

		public virtual bool TryGetValues(GoogleSheet sheet, Dimension dimension, string[] ranges,
			out JSONArray[] result) {
			return TryGetValues(sheet.sheetId, sheet.sheetName, dimension, ranges, out result);
		}

		public bool TryGetValues(string spreadSheetId, string sheetName, Dimension dimension, string[] ranges, out JSONArray[] result) {
			Debug.Log($"Trying to get sheet data from cache {spreadSheetId}");
			foreach (var range in ranges) {
				Debug.Log("Range " + range);
			}
			var filePath = Path.Combine(RootFolderPath, spreadSheetId, sheetName + ".csv");
			if (!File.Exists(filePath)) {
				Debug.Log($"Sheet not found on cache {spreadSheetId}");
				result = null;
				return false;
			}
			
			var raw = File.ReadAllText(filePath);
			var grid = ParseCsv(raw);
			
			result = new JSONArray[ranges.Length];
			for(int i = 0; i < ranges.Length; i++) {
				if(TryGetRange(ranges[i], out var start, out var end)) {
					var rowArray = new JSONArray();
					// Ensure range is within grid bounds
					int startRow = Mathf.Clamp(start.y, 0, grid.Count - 1);
					int endRow = Mathf.Clamp(end.y, 0, grid.Count - 1);
					
					for (int r = startRow; r <= endRow; r++) {
						var row = grid[r];
						var colArray = new JSONArray();
						
						int startCol = Mathf.Clamp(start.x, 0, row.Count - 1);
						// Note: different rows might have different lengths in CSV
						// but end.x is the requested range end. 
						// We should probably respect the requested range but pad with empty or stop at row end?
						// Google Sheets API returns available values. If row is short, it stops.
						int endCol = Mathf.Clamp(end.x, 0, row.Count - 1);

						// However, if the requested range goes BEYOND the row length, we just stop.
						// If the requested range starts after row length, we add empty row?
						// Let's stick to: iterate requested range, if valid in grid, add it.
						
						// Re-calculating loop bounds based on request vs actual
						// Actually, we should iterate the REQUESTED range, and fill with empty if missing?
						// Google API behavior: "Empty trailing rows and columns are omitted."
						// But existing code expects "values" array.
						
						int loopEndCol = Mathf.Min(end.x, row.Count - 1);
						
						if (start.x < row.Count) {
							for (int c = start.x; c <= loopEndCol; c++) {
								colArray.Add(row[c]);
							}
						}
						
						if (colArray.Count > 0) {
							rowArray.Add(colArray);
						}
					}
					result[i] = rowArray;
				}
				else {
					// Fallback or error for invalid range format? 
					// existing code didn't handle errors much.
					result[i] = new JSONArray();
				}
			}
			
			Debug.Log($"Sheet found on cache returning result {spreadSheetId}");
			return true;
		}
		protected List<List<string>> ParseCsv(string text) {
			var result = new List<List<string>>();
			int pos = 0;
			while (pos < text.Length) {
				var row = new List<string>();
				while (pos < text.Length) {
					// Parse cell
					if (text[pos] == '"') {
						// Quoted
						pos++;
						int start = pos;
						while (true) {
							int nextQuote = text.IndexOf('"', pos);
							if (nextQuote == -1) {
								// Broken CSV, take rest
								pos = text.Length;
								break;
							}
							if (nextQuote + 1 < text.Length && text[nextQuote + 1] == '"') {
								// Escaped quote
								pos = nextQuote + 2;
							} else {
								// End of cell
								row.Add(text.Substring(start, nextQuote - start).Replace("\"\"", "\""));
								pos = nextQuote + 1;
								break;
							}
						}
					} else {
						// Unquoted
						int nextComma = text.IndexOf(',', pos);
						int nextLine = text.IndexOf('\n', pos);
						if (nextLine != -1 && (nextLine < nextComma || nextComma == -1)) {
							// End of line comes first
							// Check for \r
							int end = nextLine;
							if (end > pos && text[end - 1] == '\r') end--;
							
							row.Add(text.Substring(pos, end - pos));
							pos = nextLine + 1;
							// End of row
							break;
						} else if (nextComma != -1) {
							// Comma
							row.Add(text.Substring(pos, nextComma - pos));
							pos = nextComma + 1;
						} else {
							// End of file
							row.Add(text.Substring(pos));
							pos = text.Length;
							break;
						}
					}
					
					// Consume comma if we just finished a cell and next is comma
					if (pos < text.Length && text[pos] == ',') {
						pos++;
					} else if (pos < text.Length && (text[pos] == '\n' || text[pos] == '\r')) {
						// End of row
						if (text[pos] == '\r' && pos + 1 < text.Length && text[pos+1] == '\n') pos++;
						pos++;
						break;
					}
				}
				result.Add(row);
			}
			return result;
		}
		protected bool TryGetRange(string range, out Vector2Int start, out Vector2Int end) {
			start = Vector2Int.zero;
			end = Vector2Int.zero;
			
			// Format: "SheetName!A1:B5" or "A1:B5" (assuming sheet matches file)
			// The passed range might contain sheet name, split by !
			var parts = range.Split('!');
			var rangePart = parts[parts.Length - 1];
			
			var subParts = rangePart.Split(':');
			if (subParts.Length != 2) return false;
			
			if (!TryParseCell(subParts[0], out start)) return false;
			if (!TryParseCell(subParts[1], out end)) return false;
			
			return true;
		}
		protected bool TryParseCell(string cell, out Vector2Int coord) {
			coord = Vector2Int.zero;
			string letters = "";
			string numbers = "";
			
			foreach (char c in cell) {
				if (char.IsLetter(c)) letters += c;
				else if (char.IsDigit(c)) numbers += c;
			}
			
			if (string.IsNullOrEmpty(letters) || string.IsNullOrEmpty(numbers)) return false;
			
			// Column
			int col = 0;
			foreach (char c in letters) {
				col *= 26;
				col += (char.ToUpper(c) - 'A' + 1);
			}
			coord.x = col - 1; // 0-based
			
			// Row
			if (int.TryParse(numbers, out int row)) {
				coord.y = row - 1; // 0-based
				return true;
			}
			
			return false;
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
				await DownloadAllSheets(spreadSheetID, sheetNames.ToArray());
			}
			Debug.Log($"Spread sheet with id {spreadSheetID} downloaded...");
		}
		protected static async Task<List<string>> GetSheetNames(string spreadSheetID)
		{
			string url = $"https://sheets.googleapis.com/v4/spreadsheets/{spreadSheetID}";
			UnityWebRequest req = UnityWebRequest.Get(url);
			bool success = await GoogleAuthenticator.Instance.TryAddAccessToken(req);
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
		protected static async Task DownloadAllSheets(string spreadsheetId, string[] sheetNames) {
			var url = $"https://sheets.googleapis.com/v4/spreadsheets/{spreadsheetId}/values:batchGet?";
			foreach (var sheet in sheetNames)
				url += $"ranges={UnityWebRequest.EscapeURL(sheet)}&";

			url += "valueRenderOption=UNFORMATTED_VALUE";

			Debug.Log($"Requesting batch download...");
			var req = UnityWebRequest.Get(url);
			bool success = await GoogleAuthenticator.Instance.TryAddAccessToken(req);
			if (!success) return;

			await req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success) {
				Debug.LogError($"Batch download error: {req.error}");
				return;
			}

			var json = req.downloadHandler.text;
			// Debug.Log($"Batch JSON: {json}"); // Too large to log typically

			var data = GA_MiniJSON.Deserialize(json) as Dictionary<string, object>;
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
					await SaveCsv(spreadsheetId, range, csv);
				}
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
		protected static async Task SaveCsv(string spreadSheetId, string range, string csv)
		{
			// "Users!A1:D20" → "Users"
			string sheetName = range.Split('!')[0];
			// Clean up sheet name just in case
			sheetName = sheetName.Replace("'", ""); 

			var folderPath = Path.Combine(RootFolderPath, spreadSheetId);
			if (!Directory.Exists(folderPath)) {
				Directory.CreateDirectory(folderPath);
			}
			
			string path = Path.Combine(folderPath, $"{sheetName}.csv");
			await File.WriteAllTextAsync(path, csv, Encoding.UTF8);
			Debug.Log($"Saved: {path}");
		}
	}
}