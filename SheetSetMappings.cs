

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static Mobge.Sheets.SheetData;

namespace Mobge.Sheets
{
    [Serializable]
    public class ScriptableObjectMapping : PairMapping<ScriptableObject>
    {

    }
    
    [Serializable]
    public class ColorMapping : AMapping<Color>
    {
        public Color defaultValue = Color.white;

        public override Color GetObject(string key)
        {
            if (ColorUtility.TryParseHtmlString("#" + key, out var color))
            {
                return color;
            }
            return defaultValue;
        }
        public override void GetAllKeys(List<string> keys)
        {
        }

        public override object GetObjectRaw(string key)
        {
            return GetObject(key);
        }

        public override bool ValidateValue(object value)
        {
            return value is Color;
        }
    }

    [Serializable]
    public class SpriteFolderMapping : AMapping<Sprite>
    {
        public string path;
        public string extension = ".png";

        public override void GetAllKeys(List<string> keys)
        {
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*" + extension);
                foreach (var file in files)
                {
                    var withoutExtension = Path.GetFileNameWithoutExtension(file);
                    keys.Add(withoutExtension);
                }
            }
        }

        public override Sprite GetObject(string key)
        {
#if UNITY_EDITOR
            var spritePath = Path.Combine(path, key + extension);
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
#else
            return null;
#endif
        }
    }
}