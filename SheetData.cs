using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Networking;

namespace Mobge.Sheets {
	[CreateAssetMenu(menuName = "Mobge/Sheets/Data")]
    public class SheetData<T> : SheetData {
        public T[] data;
        public override Type RowType => typeof(T);
        internal override void UpdateData(object[] rows) {
            this.data = rows.Cast<T>().ToArray();
            OnDataUpdate();
        }
        protected virtual void OnDataUpdate() {

        }
    }
    public abstract class SheetData : ScriptableObject {
        public GoogleSheet googleSheet;
        public CellId tableStart;
        [Disabled, SerializeField, SerializeReference] public Mapping[] mappings;
        public abstract Type RowType { get; }
        internal abstract void UpdateData(object[] rows);

        
        [Serializable]
        public abstract class Mapping {
            public string fieldName;
            public abstract object GetObject(string key);
        }
        [Serializable]
        public class DefaultMapping : Mapping {
            public object defaultValue;
            public Pair[] pairs;

            public override object GetObject(string key) {
                // implement over pairs
                throw new NotImplementedException();
            }
        }
        [Serializable]
        public struct Pair {
            public string key;
            [SerializeReference] public object value;
        }
    }
    [Serializable]
    public struct CellId {
        public const char c_charStart = 'A';
        public const int c_charCount = 26; 
        public string column;
        public int row;
        public static int ColumnToIndex(string column) {
            int val = 0;
            for(int i = 0; i < column.Length; i++) {
                char c = column[i];
                c = Char.ToUpper(c);
                int index = c - c_charStart + 1;
                val *= c_charCount;
                val += index;
            }
            return val;
        }
        public static string IndexToColumn(int index) {
            string r = "";
            while(index > 0) {
                index--;
                int i = index / c_charCount;
                int c = index - i * c_charCount;
                index = i;
                r = (char)(c + c_charStart) + r;
            }

            return r;
        }
        public static string Add(string column, int offset) {
            int index = ColumnToIndex(column);
            return IndexToColumn(index + offset);
        }
        public string GetRange(int2 size) {
            string range = column + row;
            range += ":";
            range += CellId.Add(column, size.x - 1);
            range += row + size.y - 1;
            return range;
        }
        
    }
    public enum Dimension {
        ROWS,
        COLUMNS
    }
}
