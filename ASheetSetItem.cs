using System;
using Mobge.Sheets;
using UnityEngine;

namespace Mobge.Sheets {
    [Serializable]
    public class ASheetSetItem : ISetEntry {
        [HideInInspector] public int id;
        [HideInInspector] public string name;
        [HideInInspector, SerializeField] private Sprite icon;
        
        public int Id => id;
        public string Name => name;
        public virtual Sprite Icon => icon;
    }
}