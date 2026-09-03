using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Forms;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WinForms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace SwCheckinConflictButtonAddin
{
    internal partial class CadPermissionWindow : Window
    {
        private const string LocalInsteadSrvText = "采用本地版本检出并替换服务器版本";
        private const string UseServerText = "采用服务器版本替换本地版本";

        private readonly WinForms.Form _hostForm;
        private readonly List<CadPermissionRow> _rows;
        private readonly ObservableCollection<CadPermissionViewItem> _items = new ObservableCollection<CadPermissionViewItem>();
        private WpfCheckBox SelectAllCheck;
        private bool _loadStarted;
        private volatile bool _closed;
        private bool _syncingOperations;
        private List<CadPermissionViewItem> _selectionSnapshot;
        private int _columnFitTries;
        private bool _keepingFolderStar;
        private EventHandler _columnWidthChanged;

        public CadPermissionWindow(WinForms.Form hostForm, List<CadPermissionRow> rows)
        {
            _hostForm = hostForm;
            _rows = rows ?? new List<CadPermissionRow>();
            InitializeComponent();
            GridRows.ItemsSource = _items;
            WatchColumnWidths();

            if (_hostForm != null && _hostForm.IsHandleCreated)
            {
                new WindowInteropHelper(this).Owner = _hostForm.Handle;
            }

            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);
        }

        private void CreateSelectAllCheckBox()
        {
            SelectAllCheck = new WpfCheckBox
            {
                IsThreeState = true,
                Focusable = false,
                ToolTip = "全选 / 取消全选",
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            SelectAllCheck.Click += OnSelectAllClick;

            if (GridRows.Columns.Count > 0)
            {
                GridRows.Columns[0].Header = SelectAllCheck;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (SelectAllCheck == null)
            {
                CreateSelectAllCheckBox();
            }

            if (_loadStarted)
            {
                return;
            }

            _loadStarted = true;
            SetLoading("正在读取 TeamSpace 登录信息…", true);
            ThreadPool.QueueUserWorkItem(_ => LoadPermissions());
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
                Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            try
            {
                if (SelectAllCheck != null)
                {
                    SelectAllCheck.Click -= OnSelectAllClick;
                }

                if (GridRows != null)
                {
                    UnwatchColumnWidths();
                    GridRows.ItemsSource = null;
                    if (GridRows.Columns.Count > 0)
                    {
                        GridRows.Columns[0].Header = null;
                    }
                }

                SelectAllCheck = null;
                Resources.MergedDictionaries.Clear();
            }
            catch (Exception ex)
            {
                AddinLog.Info("关闭权限窗口时清理资源: " + ex);
            }

            base.OnClosed(e);
        }

        private void LoadPermissions()
        {
            Exception error = null;
            try
            {
                TsSession session = TsSessionLocator.Resolve(_hostForm);
                if (!session.IsUsable)
                {
                    throw new InvalidOperationException(
                        "未能从 TeamSpace 取到服务器地址、登录 Token 或用户 OID。请确认已登录 TS 后再试。");
                }

                new PlmApiClient(session).Fill(_rows, (message, current, maximum) =>
                    ReportLoading(message));
            }
            catch (Exception ex)
            {
                error = ex;
                AddinLog.Info("加载权限数据失败: " + ex);
            }

            if (_closed)
            {
                return;
            }

            Exception captured = error;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed)
                {
                    return;
                }

                if (captured != null)
                {
                    SetLoading(null, false);
                    WpfMessageBox.Show(this, "打开权限界面失败: " + captured.Message, Title,
                        WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                    DialogResult = false;
                    Close();
                    return;
                }

                BindRows();
                SetLoading(null, false);
                Dispatcher.BeginInvoke(new Action(FitThenUnlockColumnWidths), DispatcherPriority.Loaded);
            }));
        }

        private void ReportLoading(string message)
        {
            if (_closed || string.IsNullOrEmpty(message))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_closed)
                {
                    SetLoading(message, true);
                }
            }));
        }

        private void SetLoading(string message, bool visible)
        {
            if (message != null)
            {
                LoadingText.Text = message;
            }

            LoadingMask.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ConfirmButton.IsEnabled = !visible && _items.Count > 0;
        }

        private void BindRows()
        {
            _items.Clear();
            int seq = 1;
            foreach (CadPermissionRow item in _rows)
            {
                object[] choices = OperationChoices(item);
                object replay = PickDefaultOperation(item, choices);
                _items.Add(new CadPermissionViewItem
                {
                    Seq = seq++,
                    DocNumber = item.Number ?? string.Empty,
                    DocName = item.Name ?? string.Empty,
                    FolderFullPath = item.FolderPath ?? string.Empty,
                    DocRead = PermText(item.DocRead),
                    DocModify = PermText(item.DocModify),
                    FolderRead = PermText(item.FolderRead),
                    FolderModify = PermText(item.FolderModify),
                    OperationTexts = ToTextList(choices),
                    ReplayValue = replay,
                    Operation = Convert.ToString(replay),
                    OperationItems = choices,
                    Source = item
                });
            }

            ConfirmButton.IsEnabled = _items.Count > 0;
            UpdateSelectAllCheck();
        }

        /// <summary>
        /// 打开时按表头/操作文案贴合列宽，随后改成像素宽度，用户可再拖动。
        /// </summary>
        private void FitThenUnlockColumnWidths()
        {
            if (_closed || GridRows == null)
            {
                return;
            }

            DataGridColumn[] permColumns = { ColDocRead, ColDocModify, ColFolderRead, ColFolderModify };
            foreach (DataGridColumn column in permColumns)
            {
                column.Width = new DataGridLength(1, DataGridLengthUnitType.SizeToHeader);
            }

            ColOperation.Width = new DataGridLength(MeasureOperationFitWidth());
            GridRows.UpdateLayout();

            if (ColDocRead.ActualWidth < 8 && _columnFitTries < 8)
            {
                _columnFitTries++;
                Dispatcher.BeginInvoke(new Action(FitThenUnlockColumnWidths), DispatcherPriority.Loaded);
                return;
            }

            foreach (DataGridColumn column in permColumns)
            {
                LockColumnToActualWidth(column);
            }

            LockColumnToActualWidth(ColOperation);
            KeepFolderStarFill();
        }

        private void WatchColumnWidths()
        {
            if (_columnWidthChanged != null || GridRows == null)
            {
                return;
            }

            _columnWidthChanged = OnAnyColumnWidthChanged;
            DependencyPropertyDescriptor descriptor = DependencyPropertyDescriptor.FromProperty(
                DataGridColumn.WidthProperty,
                typeof(DataGridColumn));
            foreach (DataGridColumn column in GridRows.Columns)
            {
                descriptor.AddValueChanged(column, _columnWidthChanged);
            }
        }

        private void UnwatchColumnWidths()
        {
            if (_columnWidthChanged == null || GridRows == null)
            {
                return;
            }

            DependencyPropertyDescriptor descriptor = DependencyPropertyDescriptor.FromProperty(
                DataGridColumn.WidthProperty,
                typeof(DataGridColumn));
            foreach (DataGridColumn column in GridRows.Columns)
            {
                descriptor.RemoveValueChanged(column, _columnWidthChanged);
            }

            _columnWidthChanged = null;
        }

        private void OnAnyColumnWidthChanged(object sender, EventArgs e)
        {
            KeepFolderStarFill();
        }

        private void KeepFolderStarFill()
        {
            if (_keepingFolderStar || _closed || ColFolder == null)
            {
                return;
            }

            if (ColFolder.Width.IsStar)
            {
                return;
            }

            _keepingFolderStar = true;
            try
            {
                ColFolder.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            }
            finally
            {
                _keepingFolderStar = false;
            }
        }

        private static void LockColumnToActualWidth(DataGridColumn column)
        {
            double width = column.ActualWidth;
            if (width < column.MinWidth)
            {
                width = column.MinWidth;
            }

            column.Width = new DataGridLength(width);
            column.CanUserResize = true;
        }

        private double MeasureOperationFitWidth()
        {
            double text = MeasureUiText("操作", 13, FontWeights.SemiBold);
            foreach (CadPermissionViewItem item in _items)
            {
                if (item.OperationTexts != null)
                {
                    foreach (string choice in item.OperationTexts)
                    {
                        text = Math.Max(text, MeasureUiText(choice, 12, FontWeights.Normal));
                    }
                }

                text = Math.Max(text, MeasureUiText(item.Operation, 12, FontWeights.Normal));
            }

            // 单元格左右 Padding 8+8，下拉内边距 11+28，边框与箭头余量
            return Math.Ceiling(text + 64);
        }

        private double MeasureUiText(string text, double fontSize, FontWeight weight)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            double pixelsPerDip = 1.0;
            try
            {
                pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            }
            catch
            {
            }

            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(
                    new FontFamily("Microsoft YaHei UI"),
                    FontStyles.Normal,
                    weight,
                    FontStretches.Normal),
                fontSize,
                Brushes.Black,
                pixelsPerDip);
            return formatted.WidthIncludingTrailingWhitespace;
        }

        private static string PermText(string value)
        {
            if (value == "有" || value == "无")
            {
                return value;
            }

            return string.IsNullOrEmpty(value) ? "未知" : value;
        }

        private static List<string> ToTextList(object[] items)
        {
            var list = new List<string>();
            if (items == null)
            {
                return list;
            }

            foreach (object entry in items)
            {
                string text = Convert.ToString(entry);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    list.Add(text);
                }
            }

            return list;
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

            return allGranted ? (object)LocalInsteadSrvText : UseServerText;
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

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            var box = sender as WpfCheckBox;
            if (box == null)
            {
                return;
            }

            if (box.IsChecked == true)
            {
                GridRows.SelectAll();
            }
            else
            {
                GridRows.UnselectAll();
            }
        }

        private void OnRowCheckPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var box = sender as WpfCheckBox;
            if (box == null)
            {
                return;
            }

            e.Handled = true;
            box.IsChecked = box.IsChecked != true;
            UpdateSelectAllCheck();
        }

        private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectAllCheck();
        }

        private void UpdateSelectAllCheck()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(UpdateSelectAllCheck));
                return;
            }

            if (SelectAllCheck == null)
            {
                return;
            }

            int selected = GridRows.SelectedItems.Count;
            int total = _items.Count;
            if (selected == 0 || total == 0)
            {
                SelectAllCheck.IsChecked = false;
            }
            else if (selected == total)
            {
                SelectAllCheck.IsChecked = true;
            }
            else
            {
                SelectAllCheck.IsChecked = null;
            }
        }

        private void OnOperationComboPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _selectionSnapshot = SnapshotSelection();
            var combo = sender as WpfComboBox;
            var row = FindAncestor<DataGridRow>(combo);
            if (combo != null && row != null && row.IsSelected && _selectionSnapshot.Count > 1)
            {
                e.Handled = true;
                combo.Focus();
                combo.IsDropDownOpen = true;
            }
        }

        private void OnOperationDropDownOpened(object sender, EventArgs e)
        {
            RestoreSelection(_selectionSnapshot);
        }

        private void OnOperationSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingOperations || e.AddedItems == null || e.AddedItems.Count == 0)
            {
                return;
            }

            var combo = sender as WpfComboBox;
            var source = combo == null ? null : combo.DataContext as CadPermissionViewItem;
            string text = combo == null ? null : Convert.ToString(combo.SelectedItem);
            if (source == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            List<CadPermissionViewItem> targets = SnapshotSelection();
            if (!targets.Contains(source))
            {
                return;
            }

            if (targets.Count <= 1)
            {
                return;
            }

            _syncingOperations = true;
            try
            {
                foreach (CadPermissionViewItem row in targets)
                {
                    ApplyOperationToRow(row, text);
                }
            }
            finally
            {
                _syncingOperations = false;
            }
        }

        private void ApplyOperationToRow(CadPermissionViewItem row, string text)
        {
            if (row == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (row.OperationTexts != null)
            {
                foreach (string option in row.OperationTexts)
                {
                    if (string.Equals(option, text, StringComparison.Ordinal))
                    {
                        row.Operation = option;
                        return;
                    }
                }
            }

            bool wantLocal = text.IndexOf("本地", StringComparison.Ordinal) >= 0
                && (text.IndexOf("检出", StringComparison.Ordinal) >= 0
                    || text.IndexOf("替换服务器", StringComparison.Ordinal) >= 0);
            object match = FindComboItem(row.OperationItems, wantLocal);
            if (match != null)
            {
                row.Operation = Convert.ToString(match);
            }
        }

        private List<CadPermissionViewItem> SnapshotSelection()
        {
            var list = new List<CadPermissionViewItem>();
            foreach (object entry in GridRows.SelectedItems)
            {
                var item = entry as CadPermissionViewItem;
                if (item != null)
                {
                    list.Add(item);
                }
            }

            return list;
        }

        private void RestoreSelection(List<CadPermissionViewItem> items)
        {
            if (items == null || items.Count <= 1)
            {
                return;
            }

            foreach (CadPermissionViewItem item in items)
            {
                if (!GridRows.SelectedItems.Contains(item))
                {
                    GridRows.SelectedItems.Add(item);
                }
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                T match = current as T;
                if (match != null)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
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
                WpfMessageBox.Show(this, "有 " + failed + " 行回写冲突界面失败，详见日志。", Title,
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
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
                object replay = item.ResolveReplay();
                DataGridViewRow sourceRow = grid.Rows[source.SourceRowIndex];
                DataGridViewCell cell = sourceRow.Cells[source.SourceOpColumn];
                cell.Value = replay;
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

        internal sealed class CadPermissionViewItem : INotifyPropertyChanged
        {
            private string _operation;

            public event PropertyChangedEventHandler PropertyChanged;

            public int Seq { get; set; }
            public string DocNumber { get; set; }
            public string DocName { get; set; }
            public string FolderFullPath { get; set; }
            public string DocRead { get; set; }
            public string DocModify { get; set; }
            public string FolderRead { get; set; }
            public string FolderModify { get; set; }
            public List<string> OperationTexts { get; set; }
            public object ReplayValue { get; set; }
            public object[] OperationItems { get; set; }
            public CadPermissionRow Source { get; set; }

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
                    ReplayValue = MatchReplay(value);
                    PropertyChangedEventHandler handler = PropertyChanged;
                    if (handler != null)
                    {
                        handler(this, new PropertyChangedEventArgs("Operation"));
                    }
                }
            }

            public object ResolveReplay()
            {
                return ReplayValue ?? MatchReplay(_operation);
            }

            private object MatchReplay(string text)
            {
                if (string.IsNullOrWhiteSpace(text) || OperationItems == null)
                {
                    return text;
                }

                foreach (object entry in OperationItems)
                {
                    if (string.Equals(Convert.ToString(entry), text, StringComparison.Ordinal))
                    {
                        return entry;
                    }
                }

                return text;
            }
        }
    }
}
