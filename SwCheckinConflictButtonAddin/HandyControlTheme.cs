using System;
using System.Windows;

namespace SwCheckinConflictButtonAddin
{
    /// <summary>
    /// Per-window HandyControl dictionaries. Avoid ResourceHelper.GetSkin which
    /// returns a process-wide SharedResourceDictionary that cannot be parented
    /// to a second Window after the first has used it.
    /// </summary>
    public sealed class HandyControlTheme : ResourceDictionary
    {
        public HandyControlTheme()
        {
            try
            {
                WpfApp.PrepareHandyControlThemeOnThisThread();
                MergedDictionaries.Add(Load("Themes/SkinDefault.xaml"));
                MergedDictionaries.Add(Load("Themes/Theme.xaml"));
            }
            catch (Exception ex)
            {
                AddinLog.Info("HandyControlTheme: " + ex);
            }
        }

        private static ResourceDictionary Load(string componentPath)
        {
            return new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/HandyControl;component/" + componentPath,
                    UriKind.Absolute)
            };
        }
    }
}
