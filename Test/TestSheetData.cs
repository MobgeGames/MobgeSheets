using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Mobge.Sheets.Test {
    public class TestSheetData : ScriptableObject {
        public SheetData<Character> characters;
        [Serializable]
        public struct Character {
            public string name;
            public int score;
            public Sprite icon;
            public ItemSet.ItemPath weapon;
        }
    }
}
