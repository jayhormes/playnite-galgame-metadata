using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace GalgameMetadata.Views
{
    /// <summary>
    /// 設定画面。XAML を持たずコードで組む（ビルド構成を増やさないため）。
    /// DataContext は PluginConfigViewModel。
    /// </summary>
    public class SettingsView : UserControl
    {
        public SettingsView()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };

            panel.Children.Add(Header("ErogameScape scores"));
            panel.Children.Add(Note(
                "Scores are read from NocoDB first, then from a Wayback Machine snapshot. "
                + "ErogameScape itself is never contacted."));
            panel.Children.Add(TextRow(
                "Tavily API key", "Settings.TavilyApiKey",
                "Optional. Lets the plugin fetch a live score when Wayback has no snapshot."));
            panel.Children.Add(CheckRow(
                "Prefer Tavily over Wayback", "Settings.PreferTavily",
                "Uses live values first. Needs an API key."));

            panel.Children.Add(Header("NocoDB"));
            panel.Children.Add(Note(
                "Optional. When set, your own collection provides cover art, tags, scores, "
                + "brand and release date before any online source."));
            panel.Children.Add(TextRow(
                "Base URL", "Settings.NocoDbBaseUrl", "For example https://nocodb.example.com"));
            panel.Children.Add(TextRow(
                "API token", "Settings.NocoDbApiToken", null, isPassword: true));
            panel.Children.Add(TextRow(
                "Games table ID", "Settings.NocoDbGamesTableId", null));
            panel.Children.Add(TextRow(
                "Genre tags column ID", "Settings.NocoDbGenreLinkId",
                "Column id of the Links field, not its name. It starts with 'c'."));
            panel.Children.Add(TextRow(
                "Attribute tags column ID", "Settings.NocoDbAttrLinkId", null));
            panel.Children.Add(TextRow(
                "Max tags", "Settings.NocoDbMaxTags", "0 means no limit."));
            panel.Children.Add(CheckRow(
                "Use NocoDB tags only", "Settings.PreferNocoDbTags",
                "Off appends VNDB tags after the NocoDB ones."));
            panel.Children.Add(CheckRow(
                "Use scores stored in NocoDB", "Settings.FallbackNocoDbScores",
                "Off ignores them and always goes to Wayback or Tavily."));
            panel.Children.Add(CheckRow(
                "Ignore certificate errors for this host", "Settings.NocoDbIgnoreSslErrors",
                "For a self-signed certificate. Only this host skips validation; "
                + "everything else Playnite talks to is still verified."));

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            };
        }

        private static TextBlock Header(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 16, 0, 4)
            };
        }

        private static TextBlock Note(string text)
        {
            return new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private static FrameworkElement TextRow(
            string label, string path, string hint, bool isPassword = false)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });

            var box = new TextBox();
            var binding = new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            box.SetBinding(TextBox.TextProperty, binding);
            // トークンは肩越しに見えないよう伏せ字にするが、貼り付け・確認はできるようにする
            if (isPassword)
            {
                box.FontFamily = new FontFamily("Consolas");
            }
            stack.Children.Add(box);

            if (!string.IsNullOrEmpty(hint))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = hint,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            return stack;
        }

        private static FrameworkElement CheckRow(string label, string path, string hint)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            var check = new CheckBox { Content = label };
            check.SetBinding(CheckBox.IsCheckedProperty, new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            stack.Children.Add(check);

            if (!string.IsNullOrEmpty(hint))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = hint,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                    Margin = new Thickness(20, 2, 0, 0)
                });
            }

            return stack;
        }
    }
}
