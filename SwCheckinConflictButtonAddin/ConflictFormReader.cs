using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    /// <summary>
    /// 只读冲突窗表格的可见列（编号、名称、操作），不依赖对方 RowData / CNetFileInfo。
    /// </summary>
    internal static class ConflictFormReader
    {
        public static List<CadPermissionRow> ReadCadRows(Form form)
        {
            var result = new List<CadPermissionRow>();
            if (form == null)
            {
                return result;
            }

            AddinLog.Info("冲突窗体类型=" + form.GetType().FullName);
            DataGridView grid = FindBestGrid(form);
            if (grid == null)
            {
                AddinLog.Info("未找到冲突表格");
                DumpChildren(form, 0);
                return result;
            }

            LogGrid(grid);
            int numberCol = FindNamedColumn(grid, "DocCode")
                ?? FindColumn(grid, "编号", "文档编号", "代号", "编码", "Code", "Number", "DocCode");
            int nameCol = FindNamedColumn(grid, "FileName")
                ?? FindColumn(grid, "文件名", "文档名称", "名称", "FileName");
            int opCol = FindColumn(grid, "操作", "处理", "Action", "Operation");
            if (opCol < 0)
            {
                opCol = FindOperationColumnByType(grid);
            }

            for (int r = 0; r < grid.Rows.Count; r++)
            {
                DataGridViewRow row = grid.Rows[r];
                if (row.IsNewRow)
                {
                    continue;
                }

                string number = GetCell(row, numberCol);
                string name = GetCell(row, nameCol);
                if (string.IsNullOrWhiteSpace(number) && string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var item = new CadPermissionRow
                {
                    Number = number,
                    Name = string.IsNullOrWhiteSpace(name) ? number : name,
                    DocOid = TryReadDocOid(row.Tag),
                    SourceForm = form,
                    SourceGrid = grid,
                    SourceRowIndex = r,
                    SourceOpColumn = opCol
                };
                FillOperation(item, row, opCol);
                result.Add(item);
                if (result.Count == 1)
                {
                    AddinLog.Info("首行 OID=" + item.DocOid
                        + " 代号=" + item.Number
                        + " 名称=" + item.Name
                        + (row.Tag == null
                            ? " tag=null"
                            : " tag=" + ReflectionValue.DescribeObject(row.Tag, 20)));
                }
            }

            AddinLog.Info("冲突 CAD 行=" + result.Count);
            return result;
        }

        /// <summary>
        /// 只取服务器 CAD 的 OID，不引用对方类型。
        /// </summary>
        private static string TryReadDocOid(object tag)
        {
            if (tag == null)
            {
                return string.Empty;
            }

            string oid = FirstOid(
                ReflectionValue.GetString(tag,
                    "PlmDocId", "plmDocId", "PLMDocId", "DocId", "docId", "objoid", "ObjOid", "Oid"));
            if (!string.IsNullOrEmpty(oid))
            {
                return oid;
            }

            object srv = ReflectionValue.Get(tag, "SrvFileInfo", "NetFileInfo", "FileInfo", "CadDoc");
            oid = FirstOid(ReflectionValue.GetString(srv,
                "PlmDocId", "plmDocId", "PLMDocId", "DocId", "docId", "Oid"));
            if (!string.IsNullOrEmpty(oid))
            {
                return oid;
            }

            object inner = ReflectionValue.Get(tag, "Tag");
            return FirstOid(ReflectionValue.GetString(inner, "objoid", "ObjOid", "plmDocId", "docId"));
        }

        private static string FirstOid(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim();
            return value == "0" ? string.Empty : value;
        }

        private static void FillOperation(CadPermissionRow item, DataGridViewRow row, int opCol)
        {
            if (opCol < 0 || opCol >= row.Cells.Count)
            {
                return;
            }

            DataGridViewCell cell = row.Cells[opCol];
            item.OperationValue = cell.Value;
            var items = new List<object>();
            CollectComboItems(items, row.DataGridView.Columns[opCol] as DataGridViewComboBoxColumn);
            CollectComboItems(items, cell as DataGridViewComboBoxCell);
            if (items.Count == 0)
            {
                return;
            }

            item.OperationIsCombo = true;
            item.OperationItems = items.ToArray();
        }

        private static void CollectComboItems(List<object> items, DataGridViewComboBoxColumn column)
        {
            if (column == null || column.Items == null)
            {
                return;
            }

            foreach (object entry in column.Items)
            {
                AddComboItem(items, entry);
            }
        }

        private static void CollectComboItems(List<object> items, DataGridViewComboBoxCell cell)
        {
            if (cell == null || cell.Items == null)
            {
                return;
            }

            foreach (object entry in cell.Items)
            {
                AddComboItem(items, entry);
            }
        }

        private static void AddComboItem(List<object> items, object entry)
        {
            if (entry == null)
            {
                return;
            }

            string text = Convert.ToString(entry);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            foreach (object existing in items)
            {
                if (string.Equals(Convert.ToString(existing), text, StringComparison.Ordinal))
                {
                    return;
                }
            }

            items.Add(entry);
        }

        private static void LogGrid(DataGridView grid)
        {
            AddinLog.Info("使用表格 Name=" + grid.Name + " Rows=" + grid.Rows.Count
                + " Cols=" + grid.Columns.Count);
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn column = grid.Columns[i];
                AddinLog.Info("  col[" + i + "] header=" + column.HeaderText
                    + " name=" + column.Name + " type=" + column.GetType().Name);
            }
        }

        private static DataGridView FindBestGrid(Control root)
        {
            var grids = new List<DataGridView>();
            CollectGrids(root, grids);
            DataGridView best = null;
            int bestScore = -1;
            foreach (DataGridView grid in grids)
            {
                int score = grid.Rows.Count;
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    string header = column.HeaderText ?? string.Empty;
                    string name = column.Name ?? string.Empty;
                    if (header.IndexOf("操作", StringComparison.Ordinal) >= 0
                        || name.IndexOf("op", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 80;
                    }

                    if (header.IndexOf("编号", StringComparison.Ordinal) >= 0
                        || name.IndexOf("Code", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 20;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = grid;
                }
            }

            return best;
        }

        private static void CollectGrids(Control parent, List<DataGridView> grids)
        {
            var grid = parent as DataGridView;
            if (grid != null)
            {
                grids.Add(grid);
            }

            foreach (Control child in parent.Controls)
            {
                CollectGrids(child, grids);
            }
        }

        private static int? FindNamedColumn(DataGridView grid, params string[] names)
        {
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                string name = grid.Columns[i].Name ?? string.Empty;
                foreach (string expected in names)
                {
                    if (name.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            return null;
        }

        private static int FindColumn(DataGridView grid, params string[] hints)
        {
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                string header = grid.Columns[i].HeaderText ?? string.Empty;
                string name = grid.Columns[i].Name ?? string.Empty;
                foreach (string hint in hints)
                {
                    if (header.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static int FindOperationColumnByType(DataGridView grid)
        {
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn column = grid.Columns[i];
                if (column is DataGridViewComboBoxColumn || column is DataGridViewButtonColumn)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string GetCell(DataGridViewRow row, int index)
        {
            if (index < 0 || index >= row.Cells.Count)
            {
                return string.Empty;
            }

            object value = row.Cells[index].FormattedValue ?? row.Cells[index].Value;
            return value == null ? string.Empty : Convert.ToString(value);
        }

        private static void DumpChildren(Control parent, int depth)
        {
            if (depth > 4)
            {
                return;
            }

            AddinLog.Info(new string(' ', depth * 2) + parent.GetType().Name + " " + parent.Name
                + " text=" + parent.Text);
            foreach (Control child in parent.Controls)
            {
                DumpChildren(child, depth + 1);
            }
        }
    }
}
