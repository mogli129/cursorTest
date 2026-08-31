namespace SwCheckinConflictButtonAddin
{
    /// <summary>
    /// 目标窗口与按钮外观。WinForms 类名后缀（如 3c41aa6_r45_ad1）会随会话变化，因此只匹配前缀。
    /// </summary>
    internal static class AddinOptions
    {
        public const string TargetWindowTitle = "检入文档冲突处理";
        public const string WinFormsClassPrefix = "WindowsForms10.Window";

        public const string ButtonText = "自定义";
        public const int ButtonWidth = 72;
        public const int ButtonHeight = 24;
        public const int ButtonGapFromCaptionButtons = 6;
    }
}
