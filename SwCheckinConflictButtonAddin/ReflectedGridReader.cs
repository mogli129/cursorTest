using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    internal sealed class ReflectedGridRow
    {
        public object Bound { get; set; }
        public int Index { get; set; }
        public Dictionary<string, object> Cells { get; set; }
        public object NativeView { get; set; }
        public string OperationField { get; set; }
        public object OperationValue { get; set; }
        public object[] OperationItems { get; set; }
        public bool OperationIsCombo { get; set; }
    }

    internal static class ReflectedGridReader
    {
        public static List<ReflectedGridRow> TryRead(Control root)
        {
            var result = new List<ReflectedGridRow>();
            Control grid = FindGridControl(root);
            if (grid == null)
            {
                return result;
            }

            AddinLog.Info("反射表格 " + grid.GetType().FullName + " Name=" + grid.Name);
            object view = ReflectionValue.Get(grid, "MainView", "DefaultView") ?? grid;
            int count = GetRowCount(view);
            var columns = ReadColumns(view);
            LogColumns(columns);

            for (int i = 0; i < count; i++)
            {
                object bound = ReflectionValue.Call(view, "GetRow", i)
                    ?? ReflectionValue.Call(grid, "GetRow", i);
                if (bound == null)
                {
                    bound = GetUltraRowObject(view, i);
                }

                var cells = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (ColumnInfo column in columns)
                {
                    object value = GetCellValue(view, i, column, bound);
                    if (!string.IsNullOrEmpty(column.Caption))
                    {
                        cells[column.Caption] = value;
                    }

                    if (!string.IsNullOrEmpty(column.Field) && !cells.ContainsKey(column.Field))
                    {
                        cells[column.Field] = value;
                    }
                }

                ColumnInfo op = FindOperation(columns);
                var row = new ReflectedGridRow
                {
                    Bound = bound,
                    Index = i,
                    Cells = cells,
                    NativeView = view,
                    OperationField = op == null ? null : (op.Field ?? op.Caption),
                    OperationValue = op == null ? null : GetCellValue(view, i, op, bound),
                    OperationIsCombo = op != null && op.IsCombo,
                    OperationItems = op == null ? null : op.Items
                };
                result.Add(row);
            }

            return result;
        }

        public static void SetOperation(ReflectedGridRow row, object value)
        {
            if (row == null || row.NativeView == null || string.IsNullOrEmpty(row.OperationField))
            {
                return;
            }

            object set = ReflectionValue.Call(row.NativeView, "SetRowCellValue", row.Index, row.OperationField, value);
            if (set != null)
            {
                return;
            }

            ReflectionValue.Call(row.NativeView, "SetValue", row.Index, row.OperationField, value);
        }

        private static Control FindGridControl(Control parent)
        {
            Control best = null;
            int bestScore = -1;
            Walk(parent, control =>
            {
                if (control is DataGridView)
                {
                    return;
                }

                string name = control.GetType().Name;
                if (name.IndexOf("GridControl", StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf("UltraGrid", StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf("FlexGrid", StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf("FpSpread", StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf("GcSpread", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

                int score = GetRowCount(ReflectionValue.Get(control, "MainView", "DefaultView") ?? control);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = control;
                }
            });
            return best;
        }

        private static void Walk(Control parent, Action<Control> visit)
        {
            visit(parent);
            foreach (Control child in parent.Controls)
            {
                Walk(child, visit);
            }
        }

        private static int GetRowCount(object view)
        {
            object count = ReflectionValue.Get(view, "RowCount", "RowsCount", "Count");
            if (count is int integer)
            {
                return integer;
            }

            object rows = ReflectionValue.Get(view, "Rows");
            if (rows is ICollection collection)
            {
                return collection.Count;
            }

            return 0;
        }

        private static List<ColumnInfo> ReadColumns(object view)
        {
            var result = new List<ColumnInfo>();
            object columns = ReflectionValue.Get(view, "Columns")
                ?? ReflectionValue.Get(ReflectionValue.Get(view, "DisplayLayout"), "Bands");
            if (columns == null)
            {
                object layout = ReflectionValue.Get(view, "DisplayLayout");
                object bands = layout == null ? null : ReflectionValue.Get(layout, "Bands");
                object band0 = bands == null ? null : ReflectionValue.Call(bands, "get_Item", 0);
                columns = band0 == null ? null : ReflectionValue.Get(band0, "Columns");
            }

            IEnumerable enumerable = columns as IEnumerable;
            if (enumerable == null)
            {
                return result;
            }

            foreach (object column in enumerable)
            {
                if (column == null)
                {
                    continue;
                }

                var info = new ColumnInfo
                {
                    Native = column,
                    Field = ReflectionValue.GetString(column, "FieldName", "Key", "Name", "DataPropertyName"),
                    Caption = FirstNonEmpty(
                        ReflectionValue.GetString(column, "Caption", "HeaderText", "Text"),
                        ReflectionValue.GetString(ReflectionValue.Get(column, "Header"), "Caption", "Text")),
                    IsCombo = column.GetType().Name.IndexOf("Combo", StringComparison.OrdinalIgnoreCase) >= 0
                        || ReflectionValue.GetString(column, "ColumnEditName", "ColumnType").IndexOf("Combo", StringComparison.OrdinalIgnoreCase) >= 0
                };
                info.Items = ReadComboItems(column);
                if (string.IsNullOrEmpty(info.Caption))
                {
                    info.Caption = info.Field;
                }

                result.Add(info);
            }

            return result;
        }

        private static object[] ReadComboItems(object column)
        {
            object items = ReflectionValue.Get(column, "Items")
                ?? ReflectionValue.Get(ReflectionValue.Get(column, "Properties"), "Items")
                ?? ReflectionValue.Get(column, "DataSource");
            if (items is ICollection collection && collection.Count > 0 && collection.Count < 50)
            {
                var list = new List<object>();
                foreach (object item in collection)
                {
                    list.Add(item);
                }

                return list.ToArray();
            }

            return null;
        }

        private static void LogColumns(List<ColumnInfo> columns)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                AddinLog.Info("  rcol[" + i + "] caption=" + columns[i].Caption
                    + " field=" + columns[i].Field + " combo=" + columns[i].IsCombo);
            }
        }

        private static ColumnInfo FindOperation(List<ColumnInfo> columns)
        {
            foreach (ColumnInfo column in columns)
            {
                string blob = (column.Caption ?? "") + " " + (column.Field ?? "");
                if (blob.IndexOf("操作", StringComparison.Ordinal) >= 0
                    || blob.IndexOf("Action", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("Operation", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return column;
                }
            }

            foreach (ColumnInfo column in columns)
            {
                if (column.IsCombo)
                {
                    return column;
                }
            }

            return null;
        }

        private static object GetCellValue(object view, int index, ColumnInfo column, object bound)
        {
            object value = ReflectionValue.Call(view, "GetRowCellValue", index, column.Native)
                ?? ReflectionValue.Call(view, "GetRowCellValue", index, column.Field)
                ?? ReflectionValue.Call(view, "GetValue", index, column.Field);
            if (value != null)
            {
                return value;
            }

            if (bound != null && !string.IsNullOrEmpty(column.Field))
            {
                return ReflectionValue.Get(bound, column.Field);
            }

            return null;
        }

        private static object GetUltraRowObject(object view, int index)
        {
            object rows = ReflectionValue.Get(view, "Rows");
            if (rows == null)
            {
                return null;
            }

            object row = ReflectionValue.Call(rows, "GetItem", index)
                ?? ReflectionValue.Call(rows, "get_Item", index);
            return row == null ? null : ReflectionValue.Get(row, "ListObject", "DataBoundItem");
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private sealed class ColumnInfo
        {
            public object Native;
            public string Field;
            public string Caption;
            public bool IsCombo;
            public object[] Items;
        }
    }
}
