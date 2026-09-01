using System.Windows.Forms;

namespace SwCheckinConflictButtonAddin
{
    internal sealed class CadPermissionRow
    {
        public string Number { get; set; }
        public string Name { get; set; }
        public string DocType { get; set; }
        public string DomainName { get; set; }
        public string FolderPath { get; set; }
        public string DocRead { get; set; }
        public string DocModify { get; set; }
        public string FolderRead { get; set; }
        public string FolderModify { get; set; }
        public object OperationValue { get; set; }
        public object[] OperationItems { get; set; }
        public bool OperationIsCombo { get; set; }
        public DataGridView SourceGrid { get; set; }
        public int SourceRowIndex { get; set; }
        public int SourceOpColumn { get; set; }
        public object DataBoundItem { get; set; }
        public object FolderObject { get; set; }
        public object NativeView { get; set; }
        public string SourceOpField { get; set; }
    }
}
