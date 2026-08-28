using Playnite.SDK;
using System;
using System.Collections.Generic;

namespace GalgameMetadata.Models
{
    /// <summary>
    /// 設定画面のバインド先。ISettings は IEditableObject を継承しているので、
    /// BeginEdit/CancelEdit/EndEdit で「キャンセルしたら元に戻す」を実装する。
    /// Playnite に依存しないよう load/save は委譲で受け取る（テスト用）。
    /// </summary>
    public class PluginConfigViewModel : ObservableObject, ISettings
    {
        private readonly Action<PluginConfig> save;
        private PluginConfig backup;
        private PluginConfig settings;

        public PluginConfig Settings
        {
            get => settings;
            set => SetValue(ref settings, value);
        }

        public PluginConfigViewModel(Func<PluginConfig> load, Action<PluginConfig> save)
        {
            this.save = save;
            Settings = load?.Invoke() ?? new PluginConfig();
        }

        public void BeginEdit()
        {
            backup = Settings.Clone();
        }

        public void CancelEdit()
        {
            if (backup != null)
            {
                Settings = backup;
                backup = null;
            }
        }

        public void EndEdit()
        {
            backup = null;
            save?.Invoke(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = Settings.Validate();
            return errors.Count == 0;
        }
    }
}
