using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Mobge.Sheets.Test {
    public class TestSheetData : SheetData<TestSheetData.Row> {
        [Serializable]
        public struct Row {
            public string name;
            public int score;
            public Sprite icon;
            public ItemSet.ItemPath weapon;
        }
    }
}
