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
            [SeperateColumns] public Nested1[] arr;
            [SeperateColumns] public Nested1 single;
            [SeperateColumns] public Vector3[] vectors;
        }
        [Serializable]
        public class Nested1 {
            public float val;
            [SeperateColumns] public Nested2[] nArr;
            [SeperateColumns] public Nested2 nSingle;

        }
        [Serializable]
        public class Nested2 {
            public Sprite icon;
            public float val;
        }
    }
}
