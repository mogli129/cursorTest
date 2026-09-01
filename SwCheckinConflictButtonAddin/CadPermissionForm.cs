using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    internal sealed class CadPermissionForm : Form
    {
        private readonly DataGridView _grid = new DataGridView();
        private readonly List<CadPermissionRow> _rows;

        public CadPermissionForm(List<CadPermissionRow> rows)
        {
            _rows = rows ?? new List<CadPermissionRow>();
            Text = "CAD冲突文档权限";
            Width = 1360;
            Height = 560;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ShowInTaskbar = false;

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.ReadOnly = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            _grid.BackgroundColor = SystemColors.Window;
            Controls.Add(_grid);

            BuildColumns();
            FillRows();
            _grid.CellContentClick += OnCellContentClick;
            _grid.CellValueChanged += OnCellValueChanged;
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty)
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
        }

        private void BuildColumns()
        {
            _grid.Columns.Add(TextCol("Number", "CAD文档编号", true));
            _grid.Columns.Add(TextCol("Name", "CAD文档名称", true));
            _grid.Columns.Add(TextCol("FolderPath", "CAD文档所属文件夹(带全路径)", true));
            _grid.Columns.Add(TextCol("DocRead", "CAD文档读取权限", true));
            _grid.Columns.Add(TextCol("DocModify", "CAD文档修改权限", true));
            _grid.Columns.Add(TextCol("FolderRead", "CAD文档所属文件夹的读取权限", true));
            _grid.Columns.Add(TextCol("FolderModify", "CAD文档所属文件夹的修改权限", true));

            DataGridViewColumn operation = TryCloneSourceOperationColumn();
            if (operation != null)
            {
                _grid.Columns.Add(operation);
                return;
            }

            bool combo = false;
            object[] items = null;
            foreach (CadPermissionRow row in _rows)
            {
                if (row.OperationIsCombo)
                {
                    combo = true;
                    items = row.OperationItems;
                    break;
                }
            }

            if (combo)
            {
                var op = new DataGridViewComboBoxColumn
                {
                    Name = "Operation",
                    HeaderText = "操作",
                    FlatStyle = FlatStyle.Flat
                };
                if (items != null)
                {
                    op.Items.AddRange(items);
                }

                _grid.Columns.Add(op);
            }
            else
            {
                _grid.Columns.Add(new DataGridViewButtonColumn
                {
                    Name = "Operation",
                    HeaderText = "操作",
                    Text = "执行",
                    UseColumnTextForButtonValue = false
                });
            }
        }

        private DataGridViewColumn TryCloneSourceOperationColumn()
        {
            foreach (CadPermissionRow row in _rows)
            {
                if (row.SourceGrid == null || row.SourceOpColumn < 0
                    || row.SourceOpColumn >= row.SourceGrid.Columns.Count)
                {
                    continue;
                }

                try
                {
                    DataGridViewColumn source = row.SourceGrid.Columns[row.SourceOpColumn];
                    DataGridViewColumn clone = (DataGridViewColumn)source.Clone();
                    clone.Name = "Operation";
                    clone.HeaderText = "操作";
                    clone.ReadOnly = source.ReadOnly;
                    return clone;
                }
                catch (Exception ex)
                {
                    AddinLog.Info("克隆操作列失败: " + ex.Message);
                    return null;
                }
            }

            return null;
        }

        private static DataGridViewTextBoxColumn TextCol(string name, string header, bool readOnly)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
        }

        private void FillRows()
        {
            foreach (CadPermissionRow item in _rows)
            {
                int index = _grid.Rows.Add();
                DataGridViewRow viewRow = _grid.Rows[index];
                viewRow.Tag = item;
                viewRow.Cells["Number"].Value = item.Number;
                viewRow.Cells["Name"].Value = item.Name;
                viewRow.Cells["FolderPath"].Value = item.FolderPath;
                viewRow.Cells["DocRead"].Value = item.DocRead;
                viewRow.Cells["DocModify"].Value = item.DocModify;
                viewRow.Cells["FolderRead"].Value = item.FolderRead;
                viewRow.Cells["FolderModify"].Value = item.FolderModify;
                DataGridViewCell opCell = viewRow.Cells["Operation"];
                if (opCell is DataGridViewComboBoxCell combo)
                {
                    if (item.OperationItems != null && combo.Items.Count == 0)
                    {
                        combo.Items.AddRange(item.OperationItems);
                    }

                    combo.Value = item.OperationValue;
                }
                else
                {
                    opCell.Value = item.OperationValue ?? "执行";
                }

                ApplyPrivilegeColor(viewRow.Cells["DocRead"]);
                ApplyPrivilegeColor(viewRow.Cells["DocModify"]);
                ApplyPrivilegeColor(viewRow.Cells["FolderRead"]);
                ApplyPrivilegeColor(viewRow.Cells["FolderModify"]);
            }
        }

        private static void ApplyPrivilegeColor(DataGridViewCell cell)
        {
            string text = Convert.ToString(cell.Value);
            if (text == "无")
            {
                cell.Style.ForeColor = Color.Firebrick;
            }
            else if (text == "有")
            {
                cell.Style.ForeColor = Color.DarkGreen;
            }
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Operation")
            {
                return;
            }

            Replay(_grid.Rows[e.RowIndex], _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value, false);
        }

        private void OnCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Operation")
            {
                return;
            }

            if (_grid.Columns[e.ColumnIndex] is DataGridViewButtonColumn)
            {
                Replay(_grid.Rows[e.RowIndex], _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value, true);
            }
        }

        private static void Replay(DataGridViewRow viewRow, object value, bool buttonClick)
        {
            var item = viewRow.Tag as CadPermissionRow;
            if (item == null)
            {
                return;
            }

            if (item.NativeView is ReflectedGridRow reflected)
            {
                try
                {
                    ReflectedGridReader.SetOperation(reflected, value);
                }
                catch (Exception ex)
                {
                    ShowReplayError(ex);
                }

                return;
            }

            if (item.SourceGrid == null || item.SourceOpColumn < 0)
            {
                return;
            }

            DataGridView grid = item.SourceGrid;
            if (item.SourceRowIndex < 0 || item.SourceRowIndex >= grid.Rows.Count)
            {
                return;
            }

            try
            {
                DataGridViewCell cell = grid.Rows[item.SourceRowIndex].Cells[item.SourceOpColumn];
                if (buttonClick || cell is DataGridViewButtonCell)
                {
                    RaiseGridEvent(grid, "OnCellContentClick", item.SourceOpColumn, item.SourceRowIndex);
                }
                else
                {
                    cell.Value = value;
                    grid.NotifyCurrentCellDirty(true);
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    grid.EndEdit();
                    RaiseGridEvent(grid, "OnCellValueChanged", item.SourceOpColumn, item.SourceRowIndex);
                }
            }
            catch (Exception ex)
            {
                ShowReplayError(ex);
            }
        }

        private static void ShowReplayError(Exception ex)
        {
            AddinLog.Info("回写冲突界面操作失败: " + ex);
            MessageBox.Show("回写冲突界面操作失败: " + ex.Message, "CAD冲突文档权限",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static void RaiseGridEvent(DataGridView grid, string methodName, int column, int row)
        {
            grid.CurrentCell = grid.Rows[row].Cells[column];
            MethodInfo method = typeof(DataGridView).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method != null)
            {
                method.Invoke(grid, new object[] { new DataGridViewCellEventArgs(column, row) });
            }
        }
    }
}
