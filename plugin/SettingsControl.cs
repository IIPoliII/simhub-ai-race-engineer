using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIRaceEngineer
{
    /// <summary>Panneau affiché dans le menu de gauche de SimHub (construit en code, sans XAML).</summary>
    public class SettingsControl : UserControl
    {
        private readonly AIRaceEnginePlugin _plugin;
        private readonly TextBlock _status;
        private readonly DispatcherTimer _timer;

        // Icône micro pour le menu de gauche
        public static readonly ImageSource Icon = CreateIcon();

        public SettingsControl(AIRaceEnginePlugin plugin)
        {
            _plugin = plugin;

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "AI Race Engineer", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });

            _status = new TextBlock { FontSize = 14, Margin = new Thickness(0, 0, 0, 16) };
            panel.Children.Add(_status);

            panel.Children.Add(new TextBlock { Text = "Endpoint de télémétrie pour le client vocal :", Opacity = 0.7 });
            var open = new Button { Content = _plugin.Url, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 6, 0, 16) };
            open.Click += (s, e) => Process.Start(new ProcessStartInfo(_plugin.Url) { UseShellExecute = true });
            panel.Children.Add(open);

            panel.Children.Add(new TextBlock
            {
                Text = "Lance ensuite le client :  cd client  puis  uv run engineer.py\n" +
                       "Maintiens ton bouton push-to-talk, pose ta question, relâche.",
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
            });

            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { Refresh(); _timer.Start(); };
            Unloaded += (s, e) => _timer.Stop();
        }

        private void Refresh()
        {
            if (_plugin.ServerError != null)
            {
                _status.Text = "● " + _plugin.ServerError;
                _status.Foreground = Brushes.IndianRed;
                return;
            }
            bool live = _plugin.Status.StartsWith("Télémétrie OK");
            _status.Text = "● " + _plugin.Status;
            _status.Foreground = live ? Brushes.MediumSeaGreen : Brushes.Goldenrod;
        }

        private static ImageSource CreateIcon()
        {
            // Micro : capsule + arceau + pied
            var geometry = Geometry.Parse(
                "M12,2 A3,3 0 0 1 15,5 L15,11 A3,3 0 0 1 9,11 L9,5 A3,3 0 0 1 12,2 Z " +
                "M5,10 L7,10 A5,5 0 0 0 17,10 L19,10 A7,7 0 0 1 13,16.9 L13,20 L16,20 L16,22 L8,22 L8,20 L11,20 L11,16.9 A7,7 0 0 1 5,10 Z");
            var image = new DrawingImage(new GeometryDrawing(Brushes.White, null, geometry));
            image.Freeze();
            return image;
        }
    }
}
