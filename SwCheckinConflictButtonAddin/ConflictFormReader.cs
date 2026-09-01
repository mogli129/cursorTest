using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    internal static class ConflictFormReader
    {
        private static readonly string[] CadExtensions =
        {
            ".sldprt", ".sldasm", ".slddrw", ".sldlfp",
            ".prtdot", ".asmdot", ".drwdot",
            ".prt", ".asm", ".drw", ".par", ".psm", ".pwd"
        };

        public static List<CadPermissionRow> ReadCadRows(Form form)
        {
            var result = new List<CadPermissionRow>();
            if (form == null)
            {
                return result;
            }

            AddinLog.Info("冲突窗体类型=" + form.GetType().FullName
                + " 程序集=" + form.GetType().Assembly.Location);

            DataGridView grid = FindBestGrid(form);
            if (grid == null)
            {
                AddinLog.Info("未找到 DataGridView");
                DumpChildren(form, 0);
                return result;
            }

            AddinLog.Info("使用表格 Name=" + grid.Name + " Rows=" + grid.Rows.Count
                + " Cols=" + grid.Columns.Count);
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn column = grid.Columns[i];
                AddinLog.Info("  col[" + i + "] header=" + column.HeaderText
                    + " name=" + column.Name + " type=" + column.GetType().Name);
            }

            int numberCol = FindColumn(grid, "编号", "文档编号", "代号", "编码", "Code", "Number");
            int nameCol = FindColumn(grid, "名称", "文档名称", "文件名", "Name", "FileName");
            int pathCol = FindColumn(grid, "路径", "文件夹", "目录", "位置", "Path", "Folder");
            int opCol = FindColumn(grid, "操作", "处理", "Action", "Operation");
            if (opCol < 0)
            {
                opCol = FindOperationColumnByType(grid);
            }

            int cadCount = 0;
            var allRows = new List<CadPermissionRow>();
            for (int r = 0; r < grid.Rows.Count; r++)
            {
                DataGridViewRow row = grid.Rows[r];
                if (row.IsNewRow)
                {
                    continue;
                }

                object bound = row.DataBoundItem;
                if (r == 0 && bound != null)
                {
                    AddinLog.Info("首行绑定对象 " + ReflectionValue.DescribeObject(bound, 24));
                }

                string number = GetCell(row, numberCol);
                string name = GetCell(row, nameCol);
                string path = GetCell(row, pathCol);
                if (bound != null)
                {
                    if (string.IsNullOrWhiteSpace(number))
                    {
                        number = ReflectionValue.GetString(bound,
                            "Number", "DocNumber", "DocumentNumber", "Code", "ItemCode",
                            "PartNumber", "ObjectNumber", "DocNo", "编号", "文档编号", "Id");
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = ReflectionValue.GetString(bound,
                            "Name", "DocName", "DocumentName", "FileName", "DisplayName",
                            "名称", "文档名称", "文件名");
                    }

                    if (string.IsNullOrWhiteSpace(path))
                    {
                        path = ReflectionValue.GetString(bound,
                            "FolderPath", "FullPath", "Path", "FolderFullPath", "ParentPath",
                            "ContainerPath", "Directory", "文件夹", "路径", "全路径");
                    }
                }

                var item = new CadPermissionRow
                {
                    Number = number,
                    Name = string.IsNullOrWhiteSpace(name) ? number : name,
                    FolderPath = path,
                    SourceGrid = grid,
                    SourceRowIndex = r,
                    SourceOpColumn = opCol,
                    DataBoundItem = bound
                };
                FillOperation(item, row, opCol);
                PermissionResolver.Fill(item);
                allRows.Add(item);

                if (IsCadRow(name, number, bound))
                {
                    cadCount++;
                    result.Add(item);
                }
            }

            if (result.Count == 0 && allRows.Count > 0)
            {
                AddinLog.Info("未按扩展名识别到 CAD，展示全部冲突行 " + allRows.Count);
                result.AddRange(allRows);
            }

            AddinLog.Info("冲突行=" + grid.Rows.Count + " CAD行=" + cadCount + " 展示=" + result.Count);
            return result;
        }

        private static void FillOperation(CadPermissionRow item, DataGridViewRow row, int opCol)
        {
            if (opCol < 0 || opCol >= row.Cells.Count)
            {
                return;
            }

            DataGridViewCell cell = row.Cells[opCol];
            item.OperationValue = cell.Value;
            DataGridViewColumn column = row.DataGridView.Columns[opCol];
            if (column is DataGridViewComboBoxColumn combo)
            {
                item.OperationIsCombo = true;
                var items = new List<object>();
                if (combo.Items != null)
                {
                    foreach (object entry in combo.Items)
                    {
                        items.Add(entry);
                    }
                }

                item.OperationItems = items.ToArray();
            }
        }

        private static bool IsCadRow(string name, string number, object bound)
        {
            string type = bound == null
                ? string.Empty
                : ReflectionValue.GetString(bound,
                    "Type", "DocType", "DocumentType", "FileType", "CadType", "ObjectType", "类型");
            string blob = (name ?? "") + " " + (number ?? "") + " " + type;
            string lower = blob.ToLowerInvariant();
            foreach (string ext in CadExtensions)
            {
                if (lower.Contains(ext))
                {
                    return true;
                }
            }

            if (blob.IndexOf("零件", StringComparison.Ordinal) >= 0
                || blob.IndexOf("装配", StringComparison.Ordinal) >= 0
                || blob.IndexOf("工程图", StringComparison.Ordinal) >= 0
                || blob.IndexOf("SolidWorks", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("CAD", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string fileName = bound == null
                ? name
                : ReflectionValue.GetString(bound, "FileName", "FilePath", "LocalPath", "文件名");
            if (!string.IsNullOrEmpty(fileName))
            {
                string ext = Path.GetExtension(fileName);
                if (!string.IsNullOrEmpty(ext))
                {
                    foreach (string cadExt in CadExtensions)
                    {
                        if (ext.Equals(cadExt, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
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
                    if (header.IndexOf("操作", StringComparison.Ordinal) >= 0)
                    {
                        score += 80;
                    }

                    if (header.IndexOf("编号", StringComparison.Ordinal) >= 0
                        || header.IndexOf("名称", StringComparison.Ordinal) >= 0)
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
            if (parent is DataGridView grid)
            {
                grids.Add(grid);
            }

            foreach (Control child in parent.Controls)
            {
                CollectGrids(child, grids);
            }
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
