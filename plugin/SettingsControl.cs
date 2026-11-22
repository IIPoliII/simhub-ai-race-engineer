using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AIRaceEngineer
{
    /// <summary>Page du plugin dans le menu de gauche de SimHub.</summary>
    public class SettingsControl : UserControl
    {
        // Glyphes Segoe Fluent Icons / MDL2 Assets
        private const string GMic = "", GVolume = "", GRobot = "", GMegaphone = "", GGame = "",
            GSync = "", GSettings = "", GCheck = "", GPlay = "", GDoc = "", GFolder = "",
            GRefresh = "", GDownload = "", GLink = "", GPower = "", GHeadset = "";

        private readonly AIRaceEnginePlugin _plugin;
        private EngineerSettings S => _plugin.Settings;
        private bool Fr => S.Language == "fr";
        private readonly LlmTester _tester = new LlmTester();
        private readonly DispatcherTimer _timer;

        private Border _banner, _checklist, _aiCard, _pttCard, _testBox;
        private string _bannerMode;
        private bool _bannerDismissed, _installClicked;
        private TextBlock _bannerText, _checklistCount, _updateState, _pttLampText;
        private Border _checklistFill;
        private Grid _checklistTrack;
        private Step[] _steps;
        private StatusCard _gameCard, _clientCard, _pttStatusCard;
        private Button _clientButton;
        private string _clientButtonLabel;
        private Ellipse _pttLamp;
        private StackPanel _providerFields, _voiceFields, _calloutRows;
        private ComboBox _modelCombo;
        private int _testRev = -1;
        private string _testProvider;

        public static readonly ImageSource Icon = CreateIcon(Brushes.White);

        public SettingsControl(AIRaceEnginePlugin plugin)
        {
            _plugin = plugin;
            Resources.MergedDictionaries.Add(Theme.Resources);

            var root = new StackPanel { Margin = new Thickness(24, 20, 24, 32), MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Left };
            TextElementFont(root);
            root.Children.Add(BuildHeader());
            root.Children.Add(_banner = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 16, 0, 0), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed });
            root.Children.Add(BuildStatusCards());
            root.Children.Add(_checklist = BuildChecklist());
            root.Children.Add(_aiCard = BuildAiSection());
            root.Children.Add(BuildVoiceSection());
            root.Children.Add(BuildCalloutsSection());
            root.Children.Add(_pttCard = BuildPttSection());
            root.Children.Add(BuildMaintenanceSection());
            root.Children.Add(new TextBlock
            {
                Text = "AI Race Engineer · open source (MIT) · non affilié à SimHub",
                Foreground = Theme.B("Faint"), FontSize = 11, Margin = new Thickness(0, 22, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
            });

            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += (s, e) => SafeRefresh();
            Loaded += (s, e) => { SafeRefresh(); _timer.Start(); };
            Unloaded += (s, e) => _timer.Stop();
        }

        private static void TextElementFont(FrameworkElement e)
        {
            TextElement.SetFontFamily(e, new FontFamily("Segoe UI Variable Text, Segoe UI"));
            TextElement.SetForeground(e, Theme.B("Text"));
        }

        private void Changed() => _plugin.SettingsChanged();

        // =====================================================================
        // En-tête, bannière de mise à jour, cartes d'état
        // =====================================================================

        private UIElement BuildHeader()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border
            {
                Width = 62, Height = 62, CornerRadius = new CornerRadius(17), VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush(Color.FromRgb(0x42, 0xA5, 0xF5), Color.FromRgb(0x15, 0x65, 0xC0), 45),
                Effect = new DropShadowEffect { Color = Color.FromRgb(0x21, 0x96, 0xF3), BlurRadius = 24, ShadowDepth = 0, Opacity = 0.55 },
                Child = new Image { Source = Icon, Width = 30, Height = 30 },
            };
            grid.Children.Add(badge);

            var titles = new StackPanel { Margin = new Thickness(18, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock { Text = "AI Race Engineer", FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            var pill = Pill("v" + _plugin.Updater.Current, Theme.B("Text"), new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));
            pill.Margin = new Thickness(12, 4, 0, 0);
            pill.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(pill);
            titles.Children.Add(titleRow);
            titles.Children.Add(new TextBlock
            {
                Text = "Ton ingénieur de course à la radio. Tous les jeux SimHub, voix locale et gratuite, push-to-talk au volant.",
                Foreground = Theme.B("Muted"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
            });
            Grid.SetColumn(titles, 1);
            grid.Children.Add(titles);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(Btn("Radio check", () => _plugin.Say(Fr ? "Radio check. Tu me reçois cinq sur cinq ?" : "Radio check, how do you read me?"), "Btn", GHeadset));
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            return new Border
            {
                Background = Theme.B("Hero"), CornerRadius = new CornerRadius(14), Padding = new Thickness(24, 22, 24, 22),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0x64, 0xB5, 0xF6)), BorderThickness = new Thickness(1),
                Child = grid,
            };
        }

        private void RefreshBanner()
        {
            var u = _plugin.Updater;
            string mode = u.Installed ? "installed"
                        : u.Busy && _installClicked ? "busy"
                        : u.UpdateAvailable ? "available"
                        : null;
            if (mode == "installed" && _bannerDismissed) mode = null;

            if (mode != _bannerMode)
            {
                _bannerMode = mode;
                BuildBanner(mode);
            }
            if (_bannerText == null) return;
            if (mode == "busy") _bannerText.Text = u.State;
            else if (mode == "available")
                _bannerText.Text = u.State.StartsWith("Erreur") || u.State.StartsWith("Mise à jour annulée")
                    ? u.State
                    : $"Tu as la {u.Current}. Le client vocal est mis à jour en même temps.";
        }

        private void BuildBanner(string mode)
        {
            _bannerText = null;
            if (mode == null) { _banner.Visibility = Visibility.Collapsed; return; }
            var u = _plugin.Updater;

            Brush strong, soft;
            string glyph, title;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            UIElement extra = null;

            if (mode == "installed")
            {
                strong = Theme.B("Ok"); soft = Theme.B("OkSoft"); glyph = GCheck;
                title = $"Version {u.Latest} installée : redémarre SimHub pour l'activer";
                buttons.Children.Add(Btn("Redémarrer SimHub maintenant", AIRaceEnginePlugin.RestartSimHub, "BtnSuccess", GPower));
                buttons.Children.Add(Btn("Plus tard", () => { _bannerDismissed = true; }, "BtnGhost"));
            }
            else if (mode == "busy")
            {
                strong = Theme.B("Accent"); soft = Theme.B("AccentSoft"); glyph = GSync;
                title = $"Installation de la version {u.Latest}...";
                extra = new ProgressBar { IsIndeterminate = true, Height = 3, Margin = new Thickness(0, 10, 0, 0), Foreground = Theme.B("Accent"), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            }
            else
            {
                strong = Theme.B("Accent"); soft = Theme.B("AccentSoft"); glyph = GDownload;
                title = $"Nouvelle version disponible : {u.Latest}";
                buttons.Children.Add(Btn("Installer la mise à jour", () => { _installClicked = true; u.InstallAsync(_plugin.Client); }, "BtnPrimary", GDownload));
                buttons.Children.Add(Btn("Nouveautés", () => Open($"https://github.com/{Updater.Repo}/releases"), "BtnGhost", GLink));
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = soft, VerticalAlignment = VerticalAlignment.Center,
                Child = Glyph(glyph, 18, strong),
            };
            grid.Children.Add(icon);

            var texts = new StackPanel { Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            _bannerText = new TextBlock { Foreground = Theme.B("Muted"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            if (mode == "installed") _bannerText.Text = "Le plugin et le client vocal sont à jour. SimHub se ferme puis se relance tout seul.";
            texts.Children.Add(_bannerText);
            if (extra != null) texts.Children.Add(extra);
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);

            _banner.Child = grid;
            _banner.Background = soft;
            _banner.BorderBrush = strong;
            _banner.Visibility = Visibility.Visible;
        }

        private sealed class StatusCard
        {
            public Border Root;
            public Ellipse Dot;
            public TextBlock Value, Detail;
            public StackPanel Footer;
            private string _last;

            public void Set(string value, string detail, Brush color, bool glow = true)
            {
                var key = value + "|" + detail + "|" + color + "|" + glow;
                if (key == _last) return;
                _last = key;
                Value.Text = value;
                Detail.Text = detail ?? "";
                Dot.Fill = color;
                Dot.Effect = glow && color is SolidColorBrush sc
                    ? new DropShadowEffect { Color = sc.Color, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 }
                    : null;
            }
        }

        private UIElement BuildStatusCards()
        {
            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 16, 0, 0) };

            _gameCard = MakeStatusCard(GGame, "Jeu");
            _gameCard.Footer.Children.Add(Btn("Télémétrie", () => Open(_plugin.Url), "BtnGhost", GLink, small: true));

            _clientCard = MakeStatusCard(GMic, "Client vocal");
            _clientButton = Btn("Démarrer", () => _plugin.Client.Restart(), "Btn", GRefresh, small: true);
            _clientCard.Footer.Children.Add(_clientButton);
            _clientCard.Footer.Children.Add(Btn("Journal", () => Open(ClientRunner.LogPath), "BtnGhost", GDoc, small: true));

            _pttStatusCard = MakeStatusCard(GHeadset, "Push-to-talk");
            _pttStatusCard.Footer.Children.Add(Btn("Comment l'associer ?", () => _pttCard.BringIntoView(), "BtnGhost", null, small: true));

            _gameCard.Root.Margin = new Thickness(0, 0, 8, 0);
            _clientCard.Root.Margin = new Thickness(4, 0, 4, 0);
            _pttStatusCard.Root.Margin = new Thickness(8, 0, 0, 0);
            grid.Children.Add(_gameCard.Root);
            grid.Children.Add(_clientCard.Root);
            grid.Children.Add(_pttStatusCard.Root);
            return grid;
        }

        private static StatusCard MakeStatusCard(string glyph, string caption)
        {
            var c = new StatusCard();
            var stack = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(Glyph(glyph, 13, Theme.B("Muted")));
            head.Children.Add(new TextBlock { Text = caption.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.B("Muted"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(head);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            c.Dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(1, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            c.Value = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            row.Children.Add(c.Dot);
            row.Children.Add(c.Value);
            stack.Children.Add(row);

            c.Detail = new TextBlock { Foreground = Theme.B("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(21, 4, 0, 0), MinHeight = 34 };
            stack.Children.Add(c.Detail);
            c.Footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(13, 8, 0, 0) };
            stack.Children.Add(c.Footer);

            c.Root = Card(stack, new Thickness(18, 16, 18, 14));
            return c;
        }

        private void RefreshStatusCards()
        {
            // Jeu
            if (_plugin.ServerError != null) _gameCard.Set("Port occupé", _plugin.ServerError, Theme.B("Bad"));
            else if (_plugin.GameName != null) _gameCard.Set(_plugin.GameName, _plugin.GameDetail, Theme.B("Ok"));
            else _gameCard.Set("En attente", "Lance une session dans ton jeu : la télémétrie arrive toute seule.", Theme.B("Faint"), false);

            // Client vocal
            var client = _plugin.Client;
            var status = _plugin.ClientStatus ?? "";
            if (_plugin.ClientConnected)
            {
                if (status.StartsWith("Prêt")) _clientCard.Set("Connecté", "Prêt à répondre.", Theme.B("Ok"));
                else if (status.StartsWith("Erreur") || status.StartsWith("Micro") || status.StartsWith("ElevenLabs")) _clientCard.Set("Connecté", status, Theme.B("Warn"));
                else _clientCard.Set("Chargement...", status, Theme.B("Accent"));
            }
            else if (client.Running) _clientCard.Set("Démarrage...", "Le premier lancement peut prendre une minute.", Theme.B("Accent"));
            else if (client.LastError != null) _clientCard.Set("Arrêté", client.LastError, Theme.B("Bad"));
            else _clientCard.Set("Arrêté", S.AutoStartClient ? "Démarrage automatique dans un instant." : "Démarrage automatique désactivé.", Theme.B("Faint"), false);

            var label = client.Running || _plugin.ClientConnected ? "Redémarrer" : "Démarrer";
            if (label != _clientButtonLabel)
            {
                _clientButtonLabel = label;
                _clientButton.Content = BtnContent(label, label == "Démarrer" ? GPlay : GRefresh);
            }

            // Push-to-talk
            if (_plugin.PushToTalkDown) _pttStatusCard.Set("Appuyé", "Parle, puis relâche pour envoyer.", Theme.B("Ok"));
            else if (S.PttTested) _pttStatusCard.Set("Prêt", "Maintiens ton bouton pour parler.", Theme.B("Ok"), false);
            else _pttStatusCard.Set("Non associé", "Associe un bouton du volant, puis appuie dessus pour tester.", Theme.B("Warn"));
        }

        // =====================================================================
        // Mise en route
        // =====================================================================

        private sealed class Step
        {
            public Border Circle;
            public TextBlock CircleText, Title;
            public Button Action;
            public Func<bool> Done;
            public int Number;
            public bool? Last;
        }

        private Border BuildChecklist()
        {
            var body = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            titles.Children.Add(new TextBlock { Text = "Mise en route", FontSize = 16, FontWeight = FontWeights.SemiBold });
            titles.Children.Add(new TextBlock { Text = "Trois étapes et ton ingé est prêt à rouler.", FontSize = 12, Foreground = Theme.B("Muted"), Margin = new Thickness(0, 2, 0, 0) });
            head.Children.Add(titles);
            _checklistCount = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0xB5, 0xF6)), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(_checklistCount, 1);
            head.Children.Add(_checklistCount);
            body.Children.Add(head);

            _checklistTrack = new Grid { Height = 4, Margin = new Thickness(0, 14, 0, 8) };
            _checklistTrack.Children.Add(new Border { Background = Theme.B("Line"), CornerRadius = new CornerRadius(2) });
            _checklistFill = new Border { Background = Theme.B("Accent"), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            _checklistTrack.Children.Add(_checklistFill);
            body.Children.Add(_checklistTrack);

            _steps = new[]
            {
                MakeStep(1, "Choisis une IA et colle ta clé API", () => S.AiConfigured, "Configurer", () => _aiCard.BringIntoView()),
                MakeStep(2, "Associe le push-to-talk à un bouton du volant, puis appuie dessus", () => S.PttTested, "Comment faire ?", () => _pttCard.BringIntoView()),
                MakeStep(3, "Client vocal connecté", () => _plugin.ClientConnected, "Démarrer", () => _plugin.Client.Restart()),
            };
            foreach (var s in _steps) body.Children.Add(StepRow(s));

            var card = Card(body);
            card.Margin = new Thickness(0, 16, 0, 0);
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x21, 0x96, 0xF3));
            return card;
        }

        private static Step MakeStep(int n, string title, Func<bool> done, string action, Action onAction) => new Step
        {
            Number = n, Done = done,
            Title = new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap },
            Action = Btn(action, onAction, "Btn", null, small: true),
        };

        private static UIElement StepRow(Step s)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            s.CircleText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            s.Circle = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1.5), Child = s.CircleText, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(s.Circle);
            s.Title.Margin = new Thickness(12, 0, 12, 0);
            Grid.SetColumn(s.Title, 1);
            g.Children.Add(s.Title);
            s.Action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(s.Action, 2);
            g.Children.Add(s.Action);
            return g;
        }

        private void RefreshChecklist()
        {
            int done = 0;
            foreach (var s in _steps)
            {
                bool ok = s.Done();
                if (ok) done++;
                if (s.Last == ok) continue;
                s.Last = ok;
                s.Circle.Background = ok ? Theme.B("OkStrong") : Brushes.Transparent;
                s.Circle.BorderBrush = ok ? Theme.B("OkStrong") : Theme.B("Faint");
                s.CircleText.Text = ok ? GCheck : s.Number.ToString();
                s.CircleText.FontFamily = ok ? Theme.Icons : new FontFamily("Segoe UI");
                s.CircleText.Foreground = ok ? Brushes.White : Theme.B("Muted");
                s.Title.Foreground = ok ? Theme.B("Muted") : Theme.B("Text");
                s.Action.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            }
            _checklist.Visibility = done == _steps.Length ? Visibility.Collapsed : Visibility.Visible;
            _checklistCount.Text = $"{done}/{_steps.Length}";
            _checklistFill.Width = _checklistTrack.ActualWidth * done / _steps.Length;
        }

        // =====================================================================
        // Intelligence artificielle
        // =====================================================================

        private Border BuildAiSection()
        {
            var (card, body) = Section(GRobot, "Intelligence artificielle",
                "Comprend tes questions et répond à partir de la télémétrie. Les annonces automatiques n'en ont pas besoin.");

            var tiles = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            foreach (var info in ProviderInfo.All)
            {
                var content = new StackPanel { Width = 126 };
                content.Children.Add(new TextBlock { Text = info.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
                content.Children.Add(new TextBlock { Text = info.Tagline, FontSize = 11.5, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
                var id = info.Id;
                tiles.Children.Add(Tile("provider", content, S.Provider == id, () =>
                {
                    if (S.Provider == id) return;
                    S.Provider = id;
                    S.Current(); // crée l'entrée avec le modèle par défaut
                    Changed();
                    RebuildProviderFields();
                }));
            }
            body.Children.Add(tiles);

            _providerFields = new StackPanel();
            body.Children.Add(_providerFields);
            RebuildProviderFields();

            var testRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            testRow.Children.Add(Btn("Tester la connexion", RunTest, "BtnPrimary", GPlay));
            body.Children.Add(testRow);
            _testBox = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed, BorderThickness = new Thickness(1) };
            body.Children.Add(_testBox);
            return card;
        }

        private void RunTest()
        {
            _testProvider = S.Provider;
            _tester.TestAsync(ProviderInfo.Get(S.Provider), S.Current(), S.ModelOrDefault());
        }

        private void RebuildProviderFields()
        {
            _testRev = -1; // masque le résultat du test d'un autre fournisseur
            _providerFields.Children.Clear();
            var info = ProviderInfo.Get(S.Provider);
            var p = S.Current();

            if (info.Id == "custom")
                _providerFields.Children.Add(Field("URL de l'API", "Adresse compatible OpenAI (Ollama : http://localhost:11434/v1).", Text(info.BaseUrl(p), v => { p.BaseUrl = v; Changed(); })));
            if (info.AccountLabel != null)
                _providerFields.Children.Add(Field(info.AccountLabel, info.AccountHelp, Text(p.AccountId, v => { p.AccountId = v; Changed(); })));

            var keyRow = new StackPanel { Orientation = Orientation.Horizontal };
            keyRow.Children.Add(Secret(p.ApiKey, v => { p.ApiKey = v; Changed(); }));
            if (info.KeyHelpUrl != null) keyRow.Children.Add(Btn("Obtenir une clé", () => Open(info.KeyHelpUrl), "BtnGhost", GLink));
            _providerFields.Children.Add(Field(info.NeedsKey ? "Clé API" : "Clé API (optionnelle)",
                info.KeyHelp + " Enregistrée chiffrée sur ce PC.", keyRow));

            _modelCombo = new ComboBox { IsEditable = true, Width = 380, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var m in info.Models) _modelCombo.Items.Add(m);
            _modelCombo.Text = S.ModelOrDefault();
            _modelCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) =>
            {
                if (p.Model == _modelCombo.Text) return;
                p.Model = _modelCombo.Text;
                Changed();
            }));
            _providerFields.Children.Add(Field("Modèle", "Un petit modèle rapide suffit. Tu peux aussi taper un autre nom.", _modelCombo));
        }

        private void RefreshTest()
        {
            if (_tester.Revision == _testRev) return;
            _testRev = _tester.Revision;
            if (_tester.Revision == 0 || _testProvider != S.Provider) { _testBox.Visibility = Visibility.Collapsed; return; }

            Brush strong = _tester.Ok == true ? Theme.B("Ok") : _tester.Ok == false ? Theme.B("Bad") : Theme.B("Accent");
            Brush soft = _tester.Ok == true ? Theme.B("OkSoft") : _tester.Ok == false ? Theme.B("BadSoft") : Theme.B("AccentSoft");
            var glyph = _tester.Ok == true ? GCheck : _tester.Ok == false ? "" : GSync;

            var stack = new StackPanel();
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = Glyph(glyph, 14, strong);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 10, 0);
            line.Children.Add(icon);
            var text = new TextBlock { Text = _tester.Message, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            Grid.SetColumn(text, 1);
            line.Children.Add(text);
            stack.Children.Add(line);

            if (_tester.Suggestions.Length > 0)
            {
                var chips = new WrapPanel { Margin = new Thickness(24, 4, 0, 0) };
                foreach (var m in _tester.Suggestions)
                {
                    var model = m;
                    chips.Children.Add(Btn(model, () =>
                    {
                        _modelCombo.Text = model; // met à jour le réglage via TextChanged
                        RunTest();
                    }, "Chip"));
                }
                stack.Children.Add(chips);
            }

            _testBox.Child = stack;
            _testBox.Background = soft;
            _testBox.BorderBrush = new SolidColorBrush(((SolidColorBrush)strong).Color) { Opacity = 0.45 };
            _testBox.Visibility = Visibility.Visible;
        }

        // =====================================================================
        // Voix
        // =====================================================================

        private UIElement BuildVoiceSection()
        {
            var (card, body) = Section(GVolume, "Langue et voix", "Reconnaissance vocale et voix tournent sur ton PC : gratuit, sans internet.");

            body.Children.Add(Field("Langue", "Celle de l'ingé et de la reconnaissance vocale.", Segmented("lang",
                new[] { ("fr", "Français"), ("en", "English") }, S.Language, v =>
                {
                    S.Language = v;
                    Changed();
                    RebuildVoiceFields();
                    RebuildCallouts();
                })));
            body.Children.Add(Divider());

            var engines = new WrapPanel();
            foreach (var (id, name, sub) in new[]
            {
                ("kokoro", "Kokoro", "Local · gratuit · réaliste"),
                ("elevenlabs", "ElevenLabs", "Cloud · payant · ultra réaliste"),
            })
            {
                var content = new StackPanel { Width = 170 };
                content.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold });
                content.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, Foreground = Theme.B("Muted"), Margin = new Thickness(0, 3, 0, 0) });
                var engine = id;
                engines.Children.Add(Tile("engine", content, S.TtsEngine == engine, () => { S.TtsEngine = engine; Changed(); RebuildVoiceFields(); }));
            }
            body.Children.Add(Field("Moteur de voix", null, engines));

            _voiceFields = new StackPanel();
            body.Children.Add(_voiceFields);
            RebuildVoiceFields();

            body.Children.Add(Divider());
            body.Children.Add(ToggleRow("Effet radio", "Filtre radio et léger grésillement, comme une vraie radio d'équipe.", null, S.RadioEffect, v => { S.RadioEffect = v; Changed(); }));
            body.Children.Add(Divider());
            body.Children.Add(Field("Reconnaissance vocale", "Whisper, sur ton processeur.", Combo(new[]
            {
                ("small", "Précise  (≈ 1,3 s)"),
                ("base", "Rapide  (≈ 0,4 s, un peu moins précise)"),
            }, S.SttModel, v => { S.SttModel = v; Changed(); })));
            return card;
        }

        private void RebuildVoiceFields()
        {
            _voiceFields.Children.Clear();
            if (S.TtsEngine == "elevenlabs")
            {
                _voiceFields.Children.Add(Field("Clé API ElevenLabs", "Enregistrée chiffrée sur ce PC.", Secret(S.ElevenLabsKey, v => { S.ElevenLabsKey = v; Changed(); })));
                _voiceFields.Children.Add(Field("Voice ID", "L'ID d'une voix de ta bibliothèque ElevenLabs (multilingue).", Text(S.ElevenLabsVoiceId, v => { S.ElevenLabsVoiceId = v; Changed(); })));
                return;
            }

            var voices = VoiceInfo.Kokoro.Where(v => v.Language == S.Language).Select(v => (v.Id, v.Label)).ToArray();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Combo(voices, S.Voice, v =>
            {
                if (Fr) S.VoiceFr = v; else S.VoiceEn = v;
                Changed();
            }));
            row.Children.Add(Btn("Écouter", () => _plugin.Say(Fr ? "Box box, box box. Pneus mediums, on repart en P4." : "Box box, box box. Mediums on, you'll rejoin in P4."), "BtnGhost", GPlay));
            _voiceFields.Children.Add(Field("Voix", Fr ? "Voix françaises disponibles en local." : "En anglais, George sonne très « ingé britannique ».", row));
            _voiceFields.Children.Add(Field("Débit", null, Combo(new[] { ("1.0", "Normal"), ("1.1", "Un peu rapide"), ("1.25", "Rapide") },
                S.VoiceSpeed.ToString("0.0#", CultureInfo.InvariantCulture),
                v => { S.VoiceSpeed = double.Parse(v, CultureInfo.InvariantCulture); Changed(); })));
        }

        // =====================================================================
        // Annonces automatiques
        // =====================================================================

        private UIElement BuildCalloutsSection()
        {
            var (card, body) = Section(GMegaphone, "Annonces automatiques",
                "L'ingé te parle sans que tu demandes rien. Instantané et gratuit : pas d'IA, pas d'internet.");
            _calloutRows = new StackPanel();
            body.Children.Add(_calloutRows);
            RebuildCallouts();
            return card;
        }

        private void RebuildCallouts()
        {
            bool fr = Fr;
            var rows = _calloutRows.Children;
            rows.Clear();

            var lap = (fr ? "Tour en " : "Lap time ") + Callouts.SpokenLap(TimeSpan.FromMilliseconds(108_300), fr) + ". " +
                      (fr ? "Plus rapide de " : "Quicker by ") + Callouts.SpokenDelta(0.3, fr) + ".";
            var pb = fr ? "Meilleur tour !" : "Personal best!";
            var gaps = fr ? "Devant à 1,4, derrière à 0,8." : "Gap ahead 1.4, behind 0.8.";
            var pos = Callouts.SpokenPosition(5, 1, fr);
            var fuel = fr ? "Carburant : environ 3 tours restants." : "Fuel: about 3 laps left.";

            rows.Add(ToggleRow("Temps au tour", "À chaque passage sur la ligne, comparé au tour précédent.", lap, S.CallLapTime, v => { S.CallLapTime = v; Changed(); }));
            rows.Add(Divider());
            rows.Add(ToggleRow("Meilleur tour", "Quand tu bats ton meilleur temps de la session.", pb, S.CallPersonalBest, v => { S.CallPersonalBest = v; Changed(); }));
            rows.Add(Divider());
            rows.Add(ToggleRow("Écarts devant et derrière", "En course, à la fin de chaque tour.", gaps, S.CallGaps, v => { S.CallGaps = v; Changed(); }));
            rows.Add(Divider());
            rows.Add(ToggleRow("Positions", "En course, quand tu gagnes ou perds des places.", pos, S.CallPosition, v => { S.CallPosition = v; Changed(); }));
            rows.Add(Divider());
            rows.Add(ToggleRow("Carburant", "Quand il ne reste plus que quelques tours d'essence, puis au dernier tour.", fuel, S.CallFuel, v => { S.CallFuel = v; Changed(); }));
            rows.Add(Field("Prévenir à", null, Combo(new[] { ("2", "2 tours restants"), ("3", "3 tours restants"), ("4", "4 tours restants"), ("5", "5 tours restants") },
                S.FuelWarningLaps.ToString(), v => { S.FuelWarningLaps = int.Parse(v); Changed(); })));
        }

        // =====================================================================
        // Push-to-talk
        // =====================================================================

        private Border BuildPttSection()
        {
            var (card, body) = Section(GHeadset, "Push-to-talk", "Maintiens un bouton du volant pour parler à l'ingé, relâche pour envoyer.");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });

            var steps = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
            var texts = new[]
            {
                "Dans SimHub, ouvre « Controls and events ».",
                $"Ajoute un mapping et choisis l'action « AIRaceEnginePlugin.{AIRaceEnginePlugin.PushToTalkAction} ».",
                "Appuie sur le bouton de ton volant pour l'associer.",
                "En piste : maintiens pour parler. Un bip à l'appui, un bip au relâché.",
            };
            for (int i = 0; i < texts.Length; i++)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Theme.B("AccentSoft"), VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                });
                var t = new TextBlock { Text = texts[i], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 2, 0, 0), FontSize = 13 };
                Grid.SetColumn(t, 1);
                row.Children.Add(t);
                steps.Children.Add(row);
            }
            grid.Children.Add(steps);

            // Testeur en direct
            var lampPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _pttLamp = new Ellipse { Width = 46, Height = 46, StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Center };
            lampPanel.Children.Add(_pttLamp);
            _pttLampText = new TextBlock { FontSize = 12.5, Margin = new Thickness(0, 12, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            lampPanel.Children.Add(_pttLampText);
            var tester = new Border
            {
                Background = Theme.B("Card"), BorderBrush = Theme.B("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16), Child = lampPanel,
            };
            Grid.SetColumn(tester, 1);
            grid.Children.Add(tester);

            body.Children.Add(grid);
            return card;
        }

        private bool? _pttLast;

        private void RefreshPtt()
        {
            bool down = _plugin.PushToTalkDown;
            if (_pttLast == down) return;
            _pttLast = down;
            var ok = ((SolidColorBrush)Theme.B("Ok")).Color;
            _pttLamp.Fill = down ? Theme.B("Ok") : Theme.B("CardStrong");
            _pttLamp.Stroke = down ? Theme.B("Ok") : Theme.B("Line");
            _pttLamp.Effect = down ? new DropShadowEffect { Color = ok, BlurRadius = 28, ShadowDepth = 0, Opacity = 1 } : null;
            _pttLampText.Text = down ? "Bouton détecté : je t'écoute" : "Appuie sur ton bouton :\nle voyant s'allume";
            _pttLampText.Foreground = down ? Theme.B("Ok") : Theme.B("Muted");
        }

        // =====================================================================
        // Client vocal et mises à jour
        // =====================================================================

        private UIElement BuildMaintenanceSection()
        {
            var (card, body) = Section(GSettings, "Client vocal et mises à jour", null);

            body.Children.Add(ToggleRow("Démarrer le client vocal avec SimHub", "Il tourne en arrière-plan, sans fenêtre, et redémarre tout seul en cas de souci.",
                null, S.AutoStartClient, v => { S.AutoStartClient = v; Changed(); }));
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            tools.Children.Add(Btn("Redémarrer le client", () => _plugin.Client.Restart(), "Btn", GRefresh));
            tools.Children.Add(Btn("Journal du client", () => Open(ClientRunner.LogPath), "Btn", GDoc));
            tools.Children.Add(Btn("Ouvrir le dossier", () => Open(EngineerSettings.DataDir), "Btn", GFolder));
            body.Children.Add(tools);

            body.Children.Add(Divider());
            var u = _plugin.Updater;
            var updateRow = new StackPanel();
            _updateState = new TextBlock { Foreground = Theme.B("Muted"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            updateRow.Children.Add(_updateState);
            var ub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            ub.Children.Add(Btn("Rechercher une mise à jour", () => u.CheckAsync(), "Btn", GSync));
            ub.Children.Add(Btn("Notes de version", () => Open($"https://github.com/{Updater.Repo}/releases"), "BtnGhost", GLink));
            ub.Children.Add(Btn("GitHub", () => Open($"https://github.com/{Updater.Repo}"), "BtnGhost", GLink));
            updateRow.Children.Add(ub);
            body.Children.Add(Field($"Version {u.Current}", "Le plugin vérifie les mises à jour à chaque démarrage de SimHub.", updateRow));
            return card;
        }

        // =====================================================================
        // Rafraîchissement
        // =====================================================================

        private void SafeRefresh()
        {
            try
            {
                RefreshBanner();
                RefreshStatusCards();
                RefreshChecklist();
                RefreshPtt();
                RefreshTest();
                _updateState.Text = string.IsNullOrEmpty(_plugin.Updater.State) ? "" : _plugin.Updater.State;
            }
            catch (Exception ex)
            {
                // Une erreur d'affichage ne doit jamais faire tomber SimHub
                _timer.Stop();
                SimHub.Logging.Current.Error("[AI Race Engineer] page : " + ex);
            }
        }

        // =====================================================================
        // Briques d'interface
        // =====================================================================

        private static (Border Card, StackPanel Body) Section(string glyph, string title, string subtitle)
        {
            var body = new StackPanel();
            var head = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.Children.Add(new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = Theme.B("AccentSoft"), VerticalAlignment = VerticalAlignment.Top,
                Child = Glyph(glyph, 17, new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9))),
            });
            var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = title, FontSize = 16.5, FontWeight = FontWeights.SemiBold });
            if (subtitle != null)
                titles.Children.Add(new TextBlock { Text = subtitle, FontSize = 12.5, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(titles, 1);
            head.Children.Add(titles);
            body.Children.Add(head);

            var card = Card(body);
            card.Margin = new Thickness(0, 16, 0, 0);
            return (card, body);
        }

        private static Border Card(UIElement child, Thickness? padding = null) => new Border
        {
            Background = Theme.B("Card"), BorderBrush = Theme.B("Line"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = padding ?? new Thickness(22, 20, 22, 20), Child = child,
        };

        private static Border Divider() => new Border { Height = 1, Background = Theme.B("Line"), Margin = new Thickness(0, 6, 0, 6) };

        private static Border Pill(string text, Brush fg, Brush bg) => new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), Background = bg,
            Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = fg },
        };

        private static TextBlock Glyph(string glyph, double size, Brush brush) => new TextBlock
        {
            Text = glyph, FontFamily = Theme.Icons, FontSize = size, Foreground = brush,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>Ligne de réglage : libellé et aide à gauche, contrôle à droite.</summary>
        private static FrameworkElement Field(string label, string help, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
            left.Children.Add(new TextBlock { Text = label, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(help))
                left.Children.Add(new TextBlock { Text = help, FontSize = 12, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            g.Children.Add(left);
            if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        /// <summary>Ligne avec interrupteur : titre, description, exemple de phrase.</summary>
        private static FrameworkElement ToggleRow(string title, string description, string example, bool value, Action<bool> onChange)
        {
            var g = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
            left.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            if (description != null)
                left.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (example != null)
                left.Children.Add(new TextBlock
                {
                    Text = "« " + example + " »", FontSize = 12, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9)),
                });
            g.Children.Add(left);

            var toggle = new CheckBox { Style = Theme.S("Toggle"), IsChecked = value };
            toggle.Checked += (s, e) => onChange(true);
            toggle.Unchecked += (s, e) => onChange(false);
            Grid.SetColumn(toggle, 1);
            g.Children.Add(toggle);
            return g;
        }

        private static RadioButton Tile(string group, object content, bool selected, Action onSelect)
        {
            var r = new RadioButton { Style = Theme.S("Tile"), GroupName = group, Content = content, IsChecked = selected };
            r.Checked += (s, e) => onSelect();
            return r;
        }

        private static Panel Segmented(string group, (string Value, string Label)[] items, string selected, Action<string> onChange)
        {
            var panel = new WrapPanel();
            foreach (var (value, label) in items)
            {
                var v = value;
                var tile = Tile(group, new TextBlock { Text = label, FontSize = 13.5, FontWeight = FontWeights.SemiBold, MinWidth = 70 }, value == selected, () => onChange(v));
                tile.Margin = new Thickness(0, 0, 8, 0);
                panel.Children.Add(tile);
            }
            return panel;
        }

        private static object BtnContent(string text, string glyph)
        {
            if (glyph == null) return text;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        private static Button Btn(string text, Action onClick, string style = "Btn", string glyph = null, bool small = false)
        {
            var b = new Button { Style = Theme.S(style), Content = BtnContent(text, glyph) };
            if (small) { b.Padding = new Thickness(10, 5, 10, 5); b.FontSize = 12; }
            b.Click += (s, e) => onClick();
            return b;
        }

        private static ComboBox Combo((string Value, string Label)[] items, string selected, Action<string> onChange)
        {
            var combo = new ComboBox { Width = 380, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var (value, label) in items)
                combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected) ?? combo.Items[0];
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is ComboBoxItem item) onChange((string)item.Tag);
            };
            return combo;
        }

        private static TextBox Text(string value, Action<string> onChange)
        {
            var t = new TextBox { Text = value ?? "", Width = 380, HorizontalAlignment = HorizontalAlignment.Left };
            t.TextChanged += (s, e) => onChange(t.Text);
            return t;
        }

        private static PasswordBox Secret(string value, Action<string> onChange)
        {
            var p = new PasswordBox { Password = value ?? "", Width = 380, HorizontalAlignment = HorizontalAlignment.Left };
            p.PasswordChanged += (s, e) => onChange(p.Password);
            return p;
        }

        private static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }

        private static ImageSource CreateIcon(Brush brush)
        {
            // Micro : capsule + arceau + pied
            var geometry = Geometry.Parse(
                "M12,2 A3,3 0 0 1 15,5 L15,11 A3,3 0 0 1 9,11 L9,5 A3,3 0 0 1 12,2 Z " +
                "M5,10 L7,10 A5,5 0 0 0 17,10 L19,10 A7,7 0 0 1 13,16.9 L13,20 L16,20 L16,22 L8,22 L8,20 L11,20 L11,16.9 A7,7 0 0 1 5,10 Z");
            var image = new DrawingImage(new GeometryDrawing(brush, null, geometry));
            image.Freeze();
            return image;
        }
    }
}
