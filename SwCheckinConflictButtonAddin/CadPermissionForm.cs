using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    internal sealed class CadPermissionForm : AntdUI.Window
    {
        private const string LocalInsteadSrvText = "采用本地版本检出并替换服务器版本";
        private const string UseServerText = "采用服务器版本替换本地版本";

        private static readonly Color ColorPage = Color.White;
        private static readonly Color ColorHeader = Color.FromArgb(250, 250, 250);
        private static readonly Color ColorLine = Color.FromArgb(240, 240, 240);
        private static readonly Color ColorText = Color.FromArgb(38, 38, 38);
        private static readonly Color ColorHover = Color.FromArgb(245, 245, 245);
        private static readonly Color ColorSelected = Color.FromArgb(230, 244, 255);

        private readonly AntdUI.Table _table = new AntdUI.Table();
        private readonly BindingList<CadPermissionViewItem> _items = new BindingList<CadPermissionViewItem>();
        private readonly List<CadPermissionRow> _rows;

        public CadPermissionForm(List<CadPermissionRow> rows)
        {
            AntdUiApp.Ensure();
            _rows = rows ?? new List<CadPermissionRow>();

            Text = "根据冲突CAD文档权限来分配操作";
            Width = 1560;
            Height = 640;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Resizable = true;
            Font = new Font("Microsoft YaHei UI", 10f);
            BackColor = ColorPage;

            var header = new AntdUI.PageHeader
            {
                Dock = DockStyle.Top,
                Height = 48,
                Text = Text,
                ShowButton = true,
                MaximizeBox = true,
                MinimizeBox = false,
                DividerShow = true,
                DividerColor = ColorLine,
                UseTextBold = true
            };

            var bottom = new AntdUI.Panel
            {
                Dock = DockStyle.Bottom,
                Height = 64,
                Back = ColorPage
            };
            var footerLine = new AntdUI.Divider
            {
                Dock = DockStyle.Top,
                Height = 1,
                Thickness = 1f,
                ColorSplit = ColorLine
            };
            var cancel = new AntdUI.Button
            {
                Text = "取消",
                Size = new Size(88, 32),
                Type = AntdUI.TTypeMini.Default,
                Radius = 6,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                DialogResult = DialogResult.Cancel,
                TabIndex = 0
            };
            var confirm = new AntdUI.Button
            {
                Text = "确认使用当前界面所选操作",
                Size = new Size(240, 32),
                Type = AntdUI.TTypeMini.Primary,
                Radius = 6,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TabIndex = 1
            };
            Action layoutButtons = () =>
            {
                cancel.Left = bottom.ClientSize.Width - cancel.Width - 20;
                cancel.Top = 16;
                confirm.Left = cancel.Left - confirm.Width - 12;
                confirm.Top = 16;
            };
            confirm.Click += OnConfirmClick;
            bottom.Resize += (s, e) => layoutButtons();
            bottom.Controls.Add(confirm);
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(footerLine);
            layoutButtons();
            CancelButton = cancel;
            Shown += (s, e) => cancel.Focus();

            StyleTable();

            var body = new AntdUI.Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 12, 16, 8),
                Back = ColorPage
            };
            var card = new AntdUI.Panel
            {
                Dock = DockStyle.Fill,
                Radius = 8,
                BorderWidth = 1f,
                BorderColor = ColorLine,
                Back = ColorPage,
                Padding = new Padding(1)
            };
            _table.Dock = DockStyle.Fill;
            card.Controls.Add(_table);
            body.Controls.Add(card);

            Controls.Add(body);
            Controls.Add(bottom);
            Controls.Add(header);

            FillRows();
            _table.Binding(_items);
        }

        private void StyleTable()
        {
            _table.TabIndex = 2;
            _table.BackColor = ColorPage;
            _table.ForeColor = ColorText;
            _table.Font = new Font("Microsoft YaHei UI", 10f);
            _table.ColumnFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            _table.ColumnBack = ColorHeader;
            _table.ColumnFore = ColorText;
            _table.Bordered = false;
            _table.BorderColor = ColorLine;
            _table.BorderWidth = 1f;
            _table.Radius = 8;
            _table.EnableHeaderResizing = true;
            _table.FixedHeader = true;
            _table.ScrollBarAvoidHeader = true;
            _table.RowHeight = 48;
            _table.RowHeightHeader = 64;
            _table.Gap = 12;
            _table.GapCell = 12;
            _table.ShowTip = true;
            _table.RowHoverBg = ColorHover;
            _table.RowSelectedBg = ColorSelected;
            _table.RowSelectedFore = ColorText;
            _table.LostFocusClearSelection = true;
            _table.CellFocusedStyle = AntdUI.TableCellFocusedStyle.None;
            _table.EditMode = AntdUI.TEditMode.Click;
            _table.EmptyText = "没有冲突文档";
            _table.EmptyHeader = true;
            _table.Columns = BuildColumns();
            _table.CellBeginEdit += OnCellBeginEdit;
            _table.CellBeginEditInputStyle += OnCellBeginEditInputStyle;
            _table.CellEndEdit += OnCellEndEdit;
            _table.CellEndValueEdit += OnCellEndValueEdit;
        }

        private static AntdUI.ColumnCollection BuildColumns()
        {
            return new AntdUI.ColumnCollection
            {
                new AntdUI.Column("Seq", "序号", AntdUI.ColumnAlign.Center)
                    .SetWidth(52).SetFixed(true).SetEditable(false),
                new AntdUI.Column("DocNumber", "编号")
                    .SetWidth(120).SetMinWidth(100).SetEllipsis(true).SetEditable(false),
                new AntdUI.Column("DocName", "名称")
                    .SetWidth(140).SetMinWidth(100).SetEllipsis(true).SetEditable(false),
                new AntdUI.Column("FolderFullPath", "文件夹")
                    .SetWidth(240).SetMinWidth(180).SetEllipsis(true).SetEditable(false),
                PermCol("DocRead", "文档读取"),
                PermCol("DocModify", "文档修改"),
                PermCol("FolderRead", "文件夹读取"),
                PermCol("FolderModify", "文件夹修改"),
                new AntdUI.Column("Operation", "操作")
                    .SetWidth(360).SetMinWidth(300).SetEllipsis(true).SetEditable(true)
            };
        }

        private static AntdUI.Column PermCol(string key, string title)
        {
            return new AntdUI.Column(key, title, AntdUI.ColumnAlign.Center)
                .SetWidth(110).SetMinWidth(100).SetColumBreak(true).SetEditable(false);
        }

        private void FillRows()
        {
            int seq = 1;
            foreach (CadPermissionRow item in _rows)
            {
                object[] items = OperationChoices(item);
                object replay = PickDefaultOperation(item, items);
                _items.Add(new CadPermissionViewItem
                {
                    Seq = seq++,
                    DocNumber = TextCell(item.Number),
                    DocName = TextCell(item.Name),
                    FolderFullPath = TextCell(item.FolderPath),
                    DocRead = PermCell(item.DocRead),
                    DocModify = PermCell(item.DocModify),
                    FolderRead = PermCell(item.FolderRead),
                    FolderModify = PermCell(item.FolderModify),
                    Operation = Convert.ToString(replay),
                    ReplayValue = replay,
                    OperationItems = items,
                    Source = item
                });
            }
        }

        private static AntdUI.CellText TextCell(string value)
        {
            return new AntdUI.CellText(value ?? string.Empty, ColorText);
        }

        private static AntdUI.CellBadge PermCell(string value)
        {
            if (value == "有")
            {
                return new AntdUI.CellBadge(AntdUI.TState.Success, "有");
            }

            if (value == "无")
            {
                return new AntdUI.CellBadge(AntdUI.TState.Error, "无");
            }

            return new AntdUI.CellBadge(AntdUI.TState.Default, string.IsNullOrEmpty(value) ? "未知" : value);
        }

        private static object[] OperationChoices(CadPermissionRow item)
        {
            if (item.OperationItems != null && item.OperationItems.Length > 0)
            {
                return item.OperationItems;
            }

            return new object[] { UseServerText, LocalInsteadSrvText };
        }

        private static object PickDefaultOperation(CadPermissionRow item, object[] items)
        {
            bool allGranted = AllPermissionsGranted(item);
            object match = FindComboItem(items, allGranted);
            if (match != null)
            {
                return match;
            }

            return allGranted ? LocalInsteadSrvText : UseServerText;
        }

        private static object FindComboItem(object[] items, bool wantLocalInsteadSrv)
        {
            if (items == null)
            {
                return null;
            }

            foreach (object entry in items)
            {
                string text = Convert.ToString(entry) ?? string.Empty;
                if (wantLocalInsteadSrv)
                {
                    if (string.Equals(text, LocalInsteadSrvText, StringComparison.Ordinal)
                        || (text.IndexOf("本地", StringComparison.Ordinal) >= 0
                            && (text.IndexOf("检出", StringComparison.Ordinal) >= 0
                                || text.IndexOf("替换服务器", StringComparison.Ordinal) >= 0)))
                    {
                        return entry;
                    }
                }
                else if (string.Equals(text, UseServerText, StringComparison.Ordinal)
                    || (text.IndexOf("服务器", StringComparison.Ordinal) >= 0
                        && text.IndexOf("替换本地", StringComparison.Ordinal) >= 0)
                    || (text.IndexOf("服务器", StringComparison.Ordinal) >= 0
                        && text.IndexOf("本地", StringComparison.Ordinal) >= 0
                        && text.IndexOf("检出", StringComparison.Ordinal) < 0
                        && text.IndexOf("保留", StringComparison.Ordinal) < 0))
                {
                    return entry;
                }
            }

            return null;
        }

        private static bool AllPermissionsGranted(CadPermissionRow item)
        {
            return IsGranted(item.DocRead)
                && IsGranted(item.DocModify)
                && IsGranted(item.FolderRead)
                && IsGranted(item.FolderModify);
        }

        private static bool IsGranted(string value)
        {
            return value == "有";
        }

        private static bool OnCellBeginEdit(object sender, AntdUI.TableEventArgs e)
        {
            return e.Column != null && e.Column.Key == "Operation";
        }

        private static void OnCellBeginEditInputStyle(object sender, AntdUI.TableBeginEditInputStyleEventArgs e)
        {
            if (e.Column == null || e.Column.Key != "Operation")
            {
                return;
            }

            var row = e.Record as CadPermissionViewItem;
            AntdUI.Select select = CreateOperationSelect(row);
            e.Set(select, r => ConfigureOperationSelect(r.Input, row));
        }

        private static AntdUI.Select CreateOperationSelect(CadPermissionViewItem row)
        {
            var select = new AntdUI.Select();
            ConfigureOperationSelect(select, row);
            return select;
        }

        private static void ConfigureOperationSelect(AntdUI.Select select, CadPermissionViewItem row)
        {
            if (select == null)
            {
                return;
            }

            select.ReadOnly = true;
            select.ListAutoWidth = true;
            select.ClickSwitchDropdown = true;
            select.ExpandDrop = true;
            select.PlaceholderText = "请选择操作";
            select.Items.Clear();

            object[] items = OperationChoices(row);
            foreach (object item in items)
            {
                string text = Convert.ToString(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    select.Items.Add(text);
                }
            }

            select.MaxCount = Math.Max(select.Items.Count, 4);

            string current = row == null ? null : row.Operation;
            if (!string.IsNullOrEmpty(current))
            {
                for (int i = 0; i < select.Items.Count; i++)
                {
                    if (string.Equals(Convert.ToString(select.Items[i]), current, StringComparison.Ordinal))
                    {
                        select.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private static object[] OperationChoices(CadPermissionViewItem row)
        {
            if (row != null && row.OperationItems != null && row.OperationItems.Length > 0)
            {
                return row.OperationItems;
            }

            return new object[] { UseServerText, LocalInsteadSrvText };
        }

        private static bool OnCellEndEdit(object sender, AntdUI.TableEndEditEventArgs e)
        {
            var row = e.Record as CadPermissionViewItem;
            if (row == null)
            {
                return true;
            }

            object match = MatchReplay(row, e.Value);
            if (match == null)
            {
                return false;
            }

            row.ReplayValue = match;
            row.Operation = Convert.ToString(match);
            return true;
        }

        private static bool OnCellEndValueEdit(object sender, AntdUI.TableEndValueEditEventArgs e)
        {
            var row = e.Record as CadPermissionViewItem;
            if (row == null || e.Value == null)
            {
                return true;
            }

            var selectItem = e.Value as AntdUI.SelectItem;
            object value = selectItem != null && selectItem.Tag != null ? selectItem.Tag : e.Value;
            object match = MatchReplay(row, Convert.ToString(value));
            if (match == null)
            {
                match = MatchReplay(row, Convert.ToString(selectItem == null ? null : selectItem.Text));
            }

            if (match == null)
            {
                return false;
            }

            row.ReplayValue = match;
            row.Operation = Convert.ToString(match);
            return true;
        }

        private static object MatchReplay(CadPermissionViewItem row, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            object[] items = OperationChoices(row);
            foreach (object entry in items)
            {
                if (string.Equals(Convert.ToString(entry), text, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private void OnConfirmClick(object sender, EventArgs e)
        {
            int failed = 0;
            foreach (CadPermissionViewItem item in _items)
            {
                if (!Replay(item))
                {
                    failed++;
                }
            }

            if (failed > 0)
            {
                AntdUiApp.Alert(this, Text, "有 " + failed + " 行回写冲突界面失败，详见日志。", AntdUI.TType.Warn);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool Replay(CadPermissionViewItem item)
        {
            CadPermissionRow source = item == null ? null : item.Source;
            if (source == null || source.SourceGrid == null || source.SourceOpColumn < 0)
            {
                return false;
            }

            DataGridView grid = source.SourceGrid;
            if (source.SourceRowIndex < 0 || source.SourceRowIndex >= grid.Rows.Count)
            {
                return false;
            }

            try
            {
                DataGridViewRow sourceRow = grid.Rows[source.SourceRowIndex];
                DataGridViewCell cell = sourceRow.Cells[source.SourceOpColumn];
                cell.Value = item.ReplayValue;
                if (grid.IsCurrentCellDirty)
                {
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }

                grid.EndEdit();
                return true;
            }
            catch (Exception ex)
            {
                AddinLog.Info("回写冲突界面操作失败: " + ex);
                return false;
            }
        }

        private sealed class CadPermissionViewItem : AntdUI.NotifyProperty
        {
            private string _operation;

            public int Seq { get; set; }
            public AntdUI.CellText DocNumber { get; set; }
            public AntdUI.CellText DocName { get; set; }
            public AntdUI.CellText FolderFullPath { get; set; }
            public AntdUI.CellBadge DocRead { get; set; }
            public AntdUI.CellBadge DocModify { get; set; }
            public AntdUI.CellBadge FolderRead { get; set; }
            public AntdUI.CellBadge FolderModify { get; set; }

            public string Operation
            {
                get { return _operation; }
                set
                {
                    if (_operation == value)
                    {
                        return;
                    }

                    _operation = value;
                    OnPropertyChanged("Operation");
                }
            }

            public object ReplayValue { get; set; }
            public object[] OperationItems { get; set; }
            public CadPermissionRow Source { get; set; }
        }
    }
}
