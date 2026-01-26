using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Reflection;

namespace Mobge.Sheets
{
    public class UpdateSheetsWindow : EditorWindow
    {
        private List<(SerializedObject serializedObject, ISheetDataOwner owner, string name, string type, Object reference)> loadedSheets = new();
        private Vector2 scrollPos;
        private bool isUpdating = false;

        [MenuItem("Mobge/Sheets/Update Sheets")]
        public static void ShowWindow()
        {
            GetWindow<UpdateSheetsWindow>("Update Sheets");
        }

        private void OnGUI()
        {
            if (isUpdating)
            {
                EditorGUILayout.HelpBox("Updating... Please wait.", MessageType.Info);
                GUI.enabled = false;
            }
            if (GUILayout.Button("Load Sheets"))
            {
                LoadSheets();
            }
            if (GUILayout.Button("Update Sheets"))
            {
                UpdateSheets();
            }
            GUI.enabled = true;

            GUILayout.Space(10);
            GUILayout.Label("Loaded Sheets:", EditorStyles.boldLabel);
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Height(300));
            if (loadedSheets.Count == 0)
            {
                GUILayout.Label("No sheets loaded.");
            }
            else
            {
                foreach (var sheet in loadedSheets)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{sheet.name} ({sheet.type})", GUILayout.Width(250));
                    EditorGUILayout.ObjectField(sheet.reference, typeof(Object), true, GUILayout.Width(200));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }

        private IEnumerable<(SerializedObject, ISheetDataOwner, string, string, Object)> FindAllSheetDataOwnersInProject()
        {
            var guids = AssetDatabase.FindAssets("t:ScriptableObject");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset is ISheetDataOwner owner)
                {
                    yield return (new SerializedObject(asset), owner, asset.name, asset.GetType().Name, asset);
                }
            }
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var components = prefab.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var comp in components)
                {
                    if (comp is ISheetDataOwner owner)
                    {
                        yield return (new SerializedObject(comp), owner, prefab.name + "/" + comp.GetType().Name, comp.GetType().Name, comp);
                    }
                }
            }
        }

        private void LoadSheets()
        {
            loadedSheets.Clear();
            foreach (var sheetDataOwner in FindAllSheetDataOwnersInProject())
            {
                var sheetDataArray = sheetDataOwner.Item2.GetSheetData();
                if (sheetDataArray == null || sheetDataArray.Count == 0)
                {
                    continue;
                }
                foreach (var sheetData in sheetDataArray)
                {
                    if (sheetData == null || string.IsNullOrEmpty(sheetData.googleSheet.SheetId))
                    {
                        continue;
                    }
                    loadedSheets.Add((sheetDataOwner.Item1, sheetDataOwner.Item2, sheetDataOwner.Item3, sheetDataOwner.Item4, sheetDataOwner.Item5));
                }
            }
            Repaint();
        }

        private async void UpdateSheets()
        {
            isUpdating = true;
            Repaint();
            foreach (var sheet in loadedSheets)
            {
                var sheetDataArray = sheet.owner.GetSheetData();
                if (sheetDataArray == null) continue;

                foreach (var sheetData in sheetDataArray)
                {
                    if (sheetData == null || string.IsNullOrEmpty(sheetData.googleSheet.SheetId))
                    {
                        continue;
                    }
                    var result = await SheetData.DetectSize(sheetData);
                    var range = sheetData.tableStart.GetRange(result.Item1);

                    var obj = sheet.owner as Object;
                    await SheetData.ReadFromSheet(obj, sheetData, range, "unknown");
                    EditorExtensions.SetDirty(obj);
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            isUpdating = false;
            Repaint();
        }
    }
}