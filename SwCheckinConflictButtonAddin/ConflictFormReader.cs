using System;
using System.Collections;
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

            IList boundList = HostBindingLocator.FindConflictList(form);
            DataGridView grid = FindBestGrid(form);
            if (grid != null)
            {
                LogGrid(grid);
                result = ReadFromDataGrid(grid, boundList);
            }
            else
            {
                List<ReflectedGridRow> reflected = ReflectedGridReader.TryRead(form);
                if (reflected.Count > 0)
                {
                    result = ReadFromReflected(reflected);
                }
                else if (boundList != null && boundList.Count > 0)
                {
                    result = ReadFromList(boundList);
                }
                else
                {
                    AddinLog.Info("未找到冲突表格或绑定列表");
                    DumpChildren(form, 0);
                    return result;
                }
            }

            if (result.Count > 0)
            {
                PermissionServiceProbe.Prepare(result[0].DataBoundItem ?? form);
                foreach (CadPermissionRow row in result)
                {
                    PermissionResolver.Fill(row);
                }
            }

            List<CadPermissionRow> cad = FilterCad(result);
            AddinLog.Info("冲突行=" + result.Count + " CAD行=" + cad.Count);
            return cad;
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

        private static List<CadPermissionRow> ReadFromDataGrid(DataGridView grid, IList boundList)
        {
            int numberCol = FindColumn(grid, "编号", "文档编号", "代号", "编码", "Code", "Number");
            int nameCol = FindColumn(grid, "名称", "文档名称", "文件名", "Name", "FileName");
            int pathCol = FindColumn(grid, "路径", "文件夹", "目录", "位置", "Path", "Folder");
            int typeCol = FindColumn(grid, "类型", "文档类型", "对象类型", "Type", "DocType");
            int opCol = FindColumn(grid, "操作", "处理", "Action", "Operation");
            if (opCol < 0)
            {
                opCol = FindOperationColumnByType(grid);
            }

            var rows = new List<CadPermissionRow>();
            int dataIndex = 0;
            for (int r = 0; r < grid.Rows.Count; r++)
            {
                DataGridViewRow row = grid.Rows[r];
                if (row.IsNewRow)
                {
                    continue;
                }

                object bound = row.DataBoundItem;
                if (bound == null && boundList != null && dataIndex < boundList.Count)
                {
                    bound = boundList[dataIndex];
                }

                dataIndex++;
                if (rows.Count == 0 && bound != null)
                {
                    AddinLog.Info("首行绑定对象 " + ReflectionValue.DescribeObject(bound, 32));
                }

                string number = GetCell(row, numberCol);
                string name = GetCell(row, nameCol);
                string path = GetCell(row, pathCol);
                string type = GetCell(row, typeCol);
                FillFromBound(ref number, ref name, ref path, ref type, bound);

                var item = new CadPermissionRow
                {
                    Number = number,
                    Name = string.IsNullOrWhiteSpace(name) ? number : name,
                    FolderPath = path,
                    DocType = type,
                    SourceGrid = grid,
                    SourceRowIndex = r,
                    SourceOpColumn = opCol,
                    DataBoundItem = bound
                };
                FillOperation(item, row, opCol);
                rows.Add(item);
            }

            return rows;
        }

        private static List<CadPermissionRow> ReadFromReflected(List<ReflectedGridRow> reflected)
        {
            var rows = new List<CadPermissionRow>();
            for (int i = 0; i < reflected.Count; i++)
            {
                ReflectedGridRow source = reflected[i];
                if (i == 0 && source.Bound != null)
                {
                    AddinLog.Info("首行绑定对象 " + ReflectionValue.DescribeObject(source.Bound, 32));
                }

                string number = Cell(source, "编号", "文档编号", "代号", "编码", "Code", "Number");
                string name = Cell(source, "名称", "文档名称", "文件名", "Name", "FileName");
                string path = Cell(source, "路径", "文件夹", "目录", "位置", "Path", "Folder");
                string type = Cell(source, "类型", "文档类型", "对象类型", "Type", "DocType");
                FillFromBound(ref number, ref name, ref path, ref type, source.Bound);
                rows.Add(new CadPermissionRow
                {
                    Number = number,
                    Name = string.IsNullOrWhiteSpace(name) ? number : name,
                    FolderPath = path,
                    DocType = type,
                    DataBoundItem = source.Bound,
                    NativeView = source,
                    SourceOpField = source.OperationField,
                    OperationValue = source.OperationValue,
                    OperationItems = source.OperationItems,
                    OperationIsCombo = source.OperationIsCombo,
                    SourceRowIndex = source.Index,
                    SourceOpColumn = -1
                });
            }

            return rows;
        }

        private static List<CadPermissionRow> ReadFromList(IList boundList)
        {
            var rows = new List<CadPermissionRow>();
            for (int i = 0; i < boundList.Count; i++)
            {
                object bound = boundList[i];
                if (i == 0 && bound != null)
                {
                    AddinLog.Info("首行绑定对象 " + ReflectionValue.DescribeObject(bound, 32));
                }

                string number = string.Empty;
                string name = string.Empty;
                string path = string.Empty;
                string type = string.Empty;
                FillFromBound(ref number, ref name, ref path, ref type, bound);
                rows.Add(new CadPermissionRow
                {
                    Number = number,
                    Name = string.IsNullOrWhiteSpace(name) ? number : name,
                    FolderPath = path,
                    DocType = type,
                    DataBoundItem = bound,
                    SourceRowIndex = i,
                    SourceOpColumn = -1,
                    OperationValue = ReflectionValue.Get(bound, "Operation", "Action", "HandleType", "操作")
                });
            }

            return rows;
        }

        private static void FillFromBound(ref string number, ref string name, ref string path, ref string type, object bound)
        {
            if (bound == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(number))
            {
                number = ReflectionValue.GetString(bound,
                    "Number", "DocNumber", "DocumentNumber", "Code", "ItemCode",
                    "PartNumber", "ObjectNumber", "DocNo", "编号", "文档编号", "Id", "Symbol");
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

            if (string.IsNullOrWhiteSpace(type))
            {
                type = ReflectionValue.GetString(bound,
                    "Type", "DocType", "DocumentType", "FileType", "CadType", "ObjectType",
                    "ClassName", "类型", "文档类型");
            }
        }

        private static List<CadPermissionRow> FilterCad(List<CadPermissionRow> all)
        {
            bool hasType = false;
            var cad = new List<CadPermissionRow>();
            foreach (CadPermissionRow row in all)
            {
                if (!string.IsNullOrWhiteSpace(row.DocType))
                {
                    hasType = true;
                }

                if (IsCadRow(row.Name, row.Number, row.DataBoundItem, row.DocType))
                {
                    cad.Add(row);
                }
            }

            if (cad.Count > 0)
            {
                return cad;
            }

            if (hasType)
            {
                AddinLog.Info("类型列存在但没有 CAD 文档");
                return cad;
            }

            AddinLog.Info("未按扩展名/类型识别到 CAD，展示全部冲突行 " + all.Count);
            return all;
        }

        private static string Cell(ReflectedGridRow row, params string[] hints)
        {
            foreach (string hint in hints)
            {
                foreach (KeyValuePair<string, object> pair in row.Cells)
                {
                    if (pair.Key.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return pair.Value == null ? string.Empty : Convert.ToString(pair.Value);
                    }
                }
            }

            return string.Empty;
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

                if (items.Count == 0 && combo.DataSource is IEnumerable source)
                {
                    foreach (object entry in source)
                    {
                        items.Add(entry);
                    }
                }

                item.OperationItems = items.ToArray();
            }
        }

        private static bool IsCadRow(string name, string number, object bound, string type)
        {
            string blob = (name ?? "") + " " + (number ?? "") + " " + (type ?? "");
            if (bound != null)
            {
                blob += " " + ReflectionValue.GetString(bound,
                    "Type", "DocType", "DocumentType", "FileType", "CadType", "ObjectType", "类型");
            }

            string lower = blob.ToLowerInvariant();
            foreach (string ext in CadExtensions)
            {
                if (lower.Contains(ext))
                {
                    return true;
                }
            }

            if (ContainsCadKeyword(blob))
            {
                return true;
            }

            string fileName = bound == null
                ? name
                : FirstNonEmpty(
                    ReflectionValue.GetString(bound, "FileName", "FilePath", "LocalPath", "文件名"),
                    name);
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

        private static bool ContainsCadKeyword(string blob)
        {
            return blob.IndexOf("零件", StringComparison.Ordinal) >= 0
                || blob.IndexOf("装配", StringComparison.Ordinal) >= 0
                || blob.IndexOf("工程图", StringComparison.Ordinal) >= 0
                || blob.IndexOf("CAD文档", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("CadDocument", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("EPMDocument", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("SolidWorks", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("SLDPRT", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("SLDASM", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("SLDDRW", StringComparison.OrdinalIgnoreCase) >= 0;
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
    }
}
