using System;
using Mobge.Sheets;
using UnityEngine;

namespace Mobge.Sheets {
    [Serializable]
    public class ASheetSetItem : ISetEntry {
        [HideInInspector] public int id;
        [HideInInspector] public string name;
        [HideInInspector] public Sprite icon;
        
        public int Id => id;
        public string Name => name;
        public Sprite Icon => icon;
    }
}