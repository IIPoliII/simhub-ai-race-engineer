using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIRaceEngineer
{
    /// <summary>Page du plugin dans le menu de gauche de SimHub (construite en code, sans XAML).</summary>
    public class SettingsControl : UserControl
    {
        private readonly AIRaceEnginePlugin _plugin;
        private EngineerSettings S => _plugin.Settings;
        private readonly LlmTester _tester = new LlmTester();
        private readonly DispatcherTimer _timer;

        private TextBlock _gameStatus, _clientStatus, _pttStatus, _testResult, _updateStatus;
        private Button _clientButton, _installButton, _checkButton, _testButton;
        private StackPanel _providerFields, _voiceFields;

        public static readonly ImageSource Icon = CreateIcon();

        public SettingsControl(AIRaceEnginePlugin plugin)
        {
            _plugin = plugin;
            var root = new StackPanel { Margin = new Thickness(20), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };

            root.Children.Add(new TextBlock { Text = "AI Race Engineer", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
            _gameStatus = Status(root);
            _clientStatus = Status(root);
            root.Children.Add(Buttons(
                _clientButton = Btn("Redémarrer le client", () => _plugin.Client.Restart()),
                Btn("Tester la voix", () => _plugin.Say(S.Language == "fr" ? "Radio check, tu me reçois ?" : "Radio check, how do you read me?")),
                Btn("Journal du client", () => Open(ClientRunner.LogPath))));

            BuildVoiceSection(root);
            BuildAiSection(root);
            BuildCalloutsSection(root);
            BuildPushToTalkSection(root);
            BuildUpdateSection(root);

            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { Refresh(); _timer.Start(); };
            Unloaded += (s, e) => _timer.Stop();
        }

        private void Changed() => _plugin.SettingsChanged();

        // ---------------- Sections ----------------

        private void BuildVoiceSection(Panel root)
        {
            Section(root, "Langue et voix");
            root.Children.Add(Row("Langue", Combo(new[] { ("fr", "Français"), ("en", "English") }, S.Language, v =>
            {
                S.Language = v;
                Changed();
                RebuildVoiceFields();
            })));
            root.Children.Add(Row("Moteur de voix", Combo(new[]
            {
                ("kokoro", "Kokoro : local, gratuit"),
                ("elevenlabs", "ElevenLabs : cloud, payant, ultra réaliste"),
            }, S.TtsEngine, v => { S.TtsEngine = v; Changed(); RebuildVoiceFields(); })));

            _voiceFields = new StackPanel();
            root.Children.Add(_voiceFields);
            RebuildVoiceFields();

            root.Children.Add(Row("Effet radio", Check("Filtre radio et grésillement", S.RadioEffect, v => { S.RadioEffect = v; Changed(); })));
            root.Children.Add(Row("Reconnaissance vocale", Combo(new[]
            {
                ("small", "Précise (≈ 1,3 s)"),
                ("base", "Rapide (≈ 0,4 s, un peu moins précise)"),
            }, S.SttModel, v => { S.SttModel = v; Changed(); })));
        }

        private void RebuildVoiceFields()
        {
            _voiceFields.Children.Clear();
            if (S.TtsEngine == "elevenlabs")
            {
                _voiceFields.Children.Add(Row("Clé API ElevenLabs", Secret(S.ElevenLabsKey, v => { S.ElevenLabsKey = v; Changed(); })));
                _voiceFields.Children.Add(Row("Voice ID", Text(S.ElevenLabsVoiceId, v => { S.ElevenLabsVoiceId = v; Changed(); })));
                _voiceFields.Children.Add(Hint("Copie l'ID d'une voix depuis ta bibliothèque ElevenLabs. Les voix sont multilingues."));
                return;
            }

            var voices = VoiceInfo.Kokoro.Where(v => v.Language == S.Language).Select(v => (v.Id, v.Label)).ToArray();
            var current = S.Language == "fr" ? S.VoiceFr : S.VoiceEn;
            _voiceFields.Children.Add(Row("Voix", Combo(voices, current, v =>
            {
                if (S.Language == "fr") S.VoiceFr = v; else S.VoiceEn = v;
                Changed();
            })));
            _voiceFields.Children.Add(Row("Vitesse", Combo(new[] { ("1.0", "Normale"), ("1.1", "Un peu rapide"), ("1.25", "Rapide") },
                S.VoiceSpeed.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture),
                v => { S.VoiceSpeed = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); Changed(); })));
        }

        private void BuildAiSection(Panel root)
        {
            Section(root, "Intelligence artificielle");
            root.Children.Add(Row("Fournisseur", Combo(ProviderInfo.All.Select(p => (p.Id, p.Label)).ToArray(), S.Provider, v =>
            {
                S.Provider = v;
                S.Current(); // crée l'entrée avec le modèle par défaut
                Changed();
                RebuildProviderFields();
            })));
            _providerFields = new StackPanel();
            root.Children.Add(_providerFields);
            RebuildProviderFields();

            _testButton = Btn("Tester la connexion", () =>
            {
                var info = ProviderInfo.Get(S.Provider);
                var p = S.Current();
                var model = string.IsNullOrWhiteSpace(p.Model) ? info.DefaultModel : p.Model.Trim();
                _tester.TestAsync(info.BaseUrl(p), string.IsNullOrWhiteSpace(p.ApiKey) ? "none" : p.ApiKey.Trim(), model);
            });
            root.Children.Add(Row("", Buttons(_testButton)));
            _testResult = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(220, 4, 0, 0) };
            root.Children.Add(_testResult);
        }

        private void RebuildProviderFields()
        {
            _providerFields.Children.Clear();
            var info = ProviderInfo.Get(S.Provider);
            var p = S.Current();

            if (info.Id == "custom")
                _providerFields.Children.Add(Row("URL de l'API", Text(info.BaseUrl(p), v => { p.BaseUrl = v; Changed(); })));
            if (info.AccountLabel != null)
                _providerFields.Children.Add(Row(info.AccountLabel, Text(p.AccountId, v => { p.AccountId = v; Changed(); })));
            _providerFields.Children.Add(Row(info.NeedsKey ? "Clé API" : "Clé API (optionnelle)", Secret(p.ApiKey, v => { p.ApiKey = v; Changed(); })));
            _providerFields.Children.Add(Row("Modèle", Text(string.IsNullOrWhiteSpace(p.Model) ? info.DefaultModel : p.Model, v => { p.Model = v; Changed(); })));
            if (info.KeyHelpUrl != null)
                _providerFields.Children.Add(Row("", Buttons(Btn("Obtenir une clé", () => Open(info.KeyHelpUrl)))));
        }

        private void BuildCalloutsSection(Panel root)
        {
            Section(root, "Annonces automatiques");
            root.Children.Add(Hint("L'ingé te parle sans que tu demandes rien. Ces annonces sont gratuites : elles n'utilisent pas l'IA."));
            root.Children.Add(Row("Fin de tour", Check("Temps au tour, comparé au tour précédent", S.CallLapTime, v => { S.CallLapTime = v; Changed(); })));
            root.Children.Add(Row("", Check("Meilleur tour personnel", S.CallPersonalBest, v => { S.CallPersonalBest = v; Changed(); })));
            root.Children.Add(Row("", Check("Écarts devant et derrière (en course)", S.CallGaps, v => { S.CallGaps = v; Changed(); })));
            root.Children.Add(Row("Positions", Check("Places gagnées ou perdues (en course)", S.CallPosition, v => { S.CallPosition = v; Changed(); })));
            root.Children.Add(Row("Carburant", Check("Alerte quand il reste peu de tours", S.CallFuel, v => { S.CallFuel = v; Changed(); })));
            root.Children.Add(Row("Alerte à", Combo(new[] { ("2", "2 tours"), ("3", "3 tours"), ("4", "4 tours"), ("5", "5 tours") },
                S.FuelWarningLaps.ToString(), v => { S.FuelWarningLaps = int.Parse(v); Changed(); })));
        }

        private void BuildPushToTalkSection(Panel root)
        {
            Section(root, "Push-to-talk et démarrage");
            root.Children.Add(Hint("Dans « Controls and events », ajoute un mapping : choisis l'action « AIRaceEnginePlugin." +
                                   AIRaceEnginePlugin.PushToTalkAction + " », puis appuie sur le bouton de ton volant. " +
                                   "Maintiens-le pour parler, relâche pour envoyer."));
            _pttStatus = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(_pttStatus);
            root.Children.Add(Row("Client vocal", Check("Lancer automatiquement avec SimHub", S.AutoStartClient, v => { S.AutoStartClient = v; Changed(); })));
        }

        private void BuildUpdateSection(Panel root)
        {
            var updater = _plugin.Updater;
            Section(root, $"Mises à jour  (version installée : {updater.Current})");
            _updateStatus = new TextBlock { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            root.Children.Add(_updateStatus);
            root.Children.Add(Buttons(
                _installButton = Btn("Installer", () => updater.InstallAsync(_plugin.Client)),
                _checkButton = Btn("Vérifier", () => updater.CheckAsync()),
                Btn("Notes de version", () => Open($"https://github.com/{Updater.Repo}/releases"))));
        }

        // ---------------- Rafraîchissement ----------------

        private void Refresh()
        {
            if (_plugin.ServerError != null) Set(_gameStatus, _plugin.ServerError, Brushes.IndianRed);
            else Set(_gameStatus, _plugin.Status, _plugin.Status.StartsWith("Télémétrie OK") ? Brushes.MediumSeaGreen : Brushes.Goldenrod);

            var client = _plugin.Client;
            var detail = string.IsNullOrEmpty(_plugin.ClientStatus) ? "" : " : " + _plugin.ClientStatus;
            if (_plugin.ClientConnected) Set(_clientStatus, "Client vocal connecté" + detail, Brushes.MediumSeaGreen);
            else if (client.Running) Set(_clientStatus, "Client vocal en démarrage" + detail, Brushes.Goldenrod);
            else if (client.LastError != null) Set(_clientStatus, client.LastError, Brushes.IndianRed);
            else Set(_clientStatus, "Client vocal arrêté", Brushes.Gray);
            _clientButton.Content = client.Running ? "Redémarrer le client" : "Démarrer le client";

            _pttStatus.Text = _plugin.PushToTalkDown ? "● Bouton push-to-talk : APPUYÉ" : "○ Bouton push-to-talk : relâché";
            _pttStatus.Foreground = _plugin.PushToTalkDown ? Brushes.MediumSeaGreen : Brushes.Gray;

            _testResult.Text = _tester.Result;
            _testButton.IsEnabled = !_tester.Busy;

            var updater = _plugin.Updater;
            _updateStatus.Text = updater.State;
            _installButton.Visibility = updater.UpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
            _installButton.Content = $"Installer la version {updater.Latest}";
            _installButton.IsEnabled = _checkButton.IsEnabled = !updater.Busy;
        }

        // ---------------- Petits constructeurs d'UI ----------------

        private static void Section(Panel root, string title) =>
            root.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 26, 0, 10) });

        private static TextBlock Status(Panel root)
        {
            var t = new TextBlock { FontSize = 14, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
            root.Children.Add(t);
            return t;
        }

        private static void Set(TextBlock t, string text, Brush color)
        {
            t.Text = "● " + text;
            t.Foreground = color;
        }

        private static TextBlock Hint(string text) =>
            new TextBlock { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

        private static FrameworkElement Row(string label, FrameworkElement control)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };
            Grid.SetColumn(control, 1);
            grid.Children.Add(l);
            grid.Children.Add(control);
            return grid;
        }

        private static StackPanel Buttons(params Button[] buttons)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var b in buttons) panel.Children.Add(b);
            return panel;
        }

        private static Button Btn(string text, Action onClick)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (s, e) => onClick();
            return b;
        }

        private static ComboBox Combo((string Value, string Label)[] items, string selected, Action<string> onChange)
        {
            var combo = new ComboBox { MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 260 };
            foreach (var (value, label) in items)
                combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected) ?? combo.Items[0];
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is ComboBoxItem item) onChange((string)item.Tag);
            };
            return combo;
        }

        private static CheckBox Check(string label, bool value, Action<bool> onChange)
        {
            var c = new CheckBox { Content = label, IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
            c.Checked += (s, e) => onChange(true);
            c.Unchecked += (s, e) => onChange(false);
            return c;
        }

        private static TextBox Text(string value, Action<string> onChange)
        {
            var t = new TextBox { Text = value ?? "", MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
            t.TextChanged += (s, e) => onChange(t.Text);
            return t;
        }

        private static PasswordBox Secret(string value, Action<string> onChange)
        {
            var p = new PasswordBox { Password = value ?? "", MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
            p.PasswordChanged += (s, e) => onChange(p.Password);
            return p;
        }

        private static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
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
