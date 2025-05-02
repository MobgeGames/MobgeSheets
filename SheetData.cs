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
        protected override Type RowType => typeof(T);
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
        protected abstract Type RowType { get; }
        internal abstract void UpdateData(object[] rows);
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
        
    }
    public enum Dimension {
        ROWS,
        COLUMNS
    }
}
