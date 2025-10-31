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
            public int[] score;
            public Sprite icon;
            public ItemSet.ItemPath weapon;
            [SeperateColumns] public Nested1 nested;
            [SeperateColumns] public Vector3[] vector;
        }
        [Serializable]
        public class Nested1 {
            public float val;
            [SeperateColumns] public Nested2 nested2;

        }
        [Serializable]
        public class Nested2 {
            public Sprite icon;
            public float val;
        }
    }
}
