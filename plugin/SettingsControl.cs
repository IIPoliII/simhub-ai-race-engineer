using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AIRaceEngineer
{
    /// <summary>
    /// Page du plugin dans SimHub : une bande d'état, puis les réglages en colonnes (3, 2 ou 1 selon la largeur).
    /// </summary>
    public class SettingsControl : UserControl
    {
        // Glyphes Segoe Fluent Icons / MDL2 Assets
        private const string GCheck = "", GPlay = "", GDoc = "", GFolder = "", GRefresh = "",
            GDownload = "", GLink = "", GPower = "", GSync = "", GError = "";

        private readonly AIRaceEnginePlugin _plugin;
        private EngineerSettings S => _plugin.Settings;
        private bool Fr => S.Language == "fr";
        private readonly LlmTester _tester = new LlmTester();
        private readonly ModelCatalog _catalog = new ModelCatalog();
        private int _catalogRev = -1;
        private TextBlock _modelHelp;
        private readonly DispatcherTimer _timer;

        private Border _banner, _aiCard, _voiceCard, _calloutsCard, _pttCard, _clientCard, _testBox;
        private string _bannerMode;
        private bool _bannerDismissed, _installClicked;
        private TextBlock _bannerText, _updateState, _pttLampText, _providerHint, _engineHint;
        private Cell _aiCell, _clientCell, _pttCell, _gameCell;
        private UniformGrid _strip;
        private Grid _columns;
        private int _columnCount;
        private Button _clientButton, _radioButton, _listenButton;
        private DateTime _sayClicked = DateTime.MinValue;
        private bool _saySeen, _sayBusyLast;
        private string _clientButtonLabel;
        private Ellipse _pttLamp;
        private bool? _pttLast;
        private StackPanel _providerFields, _voiceFields, _calloutRows;
        private ComboBox _modelCombo;
        private int _testRev = -1;
        private string _testProvider;

        public static readonly ImageSource Icon = CreateIcon(Brushes.White);

        public SettingsControl(AIRaceEnginePlugin plugin)
        {
            _plugin = plugin;
            Resources.MergedDictionaries.Add(Theme.Resources);

            var root = new StackPanel { Margin = new Thickness(24, 16, 24, 32), MaxWidth = 1600 };
            TextElement.SetFontFamily(root, Theme.Ui);
            TextElement.SetForeground(root, Theme.B("Text"));
            root.Children.Add(BuildHeader());
            root.Children.Add(_banner = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 16, 0, 0), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed });
            root.Children.Add(BuildStrip());

            _aiCard = BuildAiCard();
            _voiceCard = BuildVoiceCard();
            _calloutsCard = BuildCalloutsCard();
            _pttCard = BuildPttCard();
            _clientCard = BuildClientCard();
            _columns = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            root.Children.Add(_columns);
            root.SizeChanged += (s, e) => Arrange(e.NewSize.Width);
            Arrange(1400);

            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += (s, e) => SafeRefresh();
            Loaded += (s, e) => { SafeRefresh(); _timer.Start(); };
            Unloaded += (s, e) => _timer.Stop();
        }

        private void Changed() => _plugin.SettingsChanged();

        // =====================================================================
        // Mise en page en colonnes
        // =====================================================================

        private void Arrange(double width)
        {
            int n = width >= 1280 ? 3 : width >= 840 ? 2 : 1;
            _strip.Columns = width >= 840 ? 4 : 2;
            for (int i = 0; i < _strip.Children.Count; i++)
                ((Border)_strip.Children[i]).BorderThickness = new Thickness(i % _strip.Columns == 0 ? 0 : 1, i < _strip.Columns ? 0 : 1, 0, 0);
            if (n == _columnCount) return;
            _columnCount = n;

            var layout = n == 3
                ? new[] { new[] { _aiCard, _clientCard }, new[] { _voiceCard, _pttCard }, new[] { _calloutsCard } }
                : n == 2
                    ? new[] { new[] { _aiCard, _pttCard, _clientCard }, new[] { _voiceCard, _calloutsCard } }
                    : new[] { new[] { _aiCard, _voiceCard, _calloutsCard, _pttCard, _clientCard } };

            foreach (var panel in _columns.Children.OfType<Panel>()) panel.Children.Clear();
            _columns.Children.Clear();
            _columns.ColumnDefinitions.Clear();
            for (int i = 0; i < n; i++)
            {
                _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var col = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 8, 0, i == n - 1 ? 0 : 8, 0) };
                foreach (var card in layout[i])
                {
                    card.Margin = new Thickness(0, col.Children.Count == 0 ? 0 : 16, 0, 0);
                    col.Children.Add(card);
                }
                Grid.SetColumn(col, i);
                _columns.Children.Add(col);
            }
        }

        // =====================================================================
        // En-tête et bannière de mise à jour
        // =====================================================================

        private UIElement BuildHeader()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            left.Inlines.Add(new Run("Ton ingénieur de course à la radio, pour tous les jeux SimHub") { FontSize = 14, Foreground = Theme.B("Text2") });
            left.Inlines.Add(new Run("   v" + _plugin.Updater.Current) { FontSize = 12, FontFamily = Theme.Mono, Foreground = Theme.B("Muted") });
            grid.Children.Add(left);

            var radio = _radioButton = Btn("Radio check", () => SayTest(Fr ? "Radio check. Tu me reçois cinq sur cinq ?" : "Radio check, how do you read me?"), "Btn", GPlay);
            radio.Margin = new Thickness(0);
            Grid.SetColumn(radio, 1);
            grid.Children.Add(radio);
            return grid;
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
                    : $"Tu as la {u.Current}. Le client vocal est mis à jour en même temps";
        }

        private void BuildBanner(string mode)
        {
            _bannerText = null;
            if (mode == null) { _banner.Visibility = Visibility.Collapsed; return; }
            var u = _plugin.Updater;

            Brush line;
            string glyph, title;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            UIElement extra = null;

            if (mode == "installed")
            {
                line = Theme.B("Ok"); glyph = GCheck;
                title = $"Version {u.Latest} installée, redémarre SimHub pour l'activer";
                buttons.Children.Add(Btn("Redémarrer SimHub", AIRaceEnginePlugin.RestartSimHub, "BtnSuccess", GPower));
                buttons.Children.Add(Btn("Plus tard", () => { _bannerDismissed = true; }, "BtnGhost"));
            }
            else if (mode == "busy")
            {
                line = Theme.B("AccentLine"); glyph = GSync;
                title = $"Installation de la version {u.Latest}";
                extra = new ProgressBar { IsIndeterminate = true, Height = 2, Margin = new Thickness(0, 8, 0, 0), Foreground = Theme.B("AccentLine"), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            }
            else
            {
                line = Theme.B("AccentLine"); glyph = GDownload;
                title = $"Version {u.Latest} disponible";
                buttons.Children.Add(Btn("Installer", () => { _installClicked = true; u.InstallAsync(_plugin.Client); }, "BtnPrimary", GDownload));
                buttons.Children.Add(Btn("Nouveautés", () => Open($"https://github.com/{Updater.Repo}/releases"), "BtnGhost", GLink));
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Glyph(glyph, 16, line));

            var texts = new StackPanel { Margin = new Thickness(12, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
            _bannerText = new TextBlock { Foreground = Theme.B("Text2"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            if (mode == "installed") _bannerText.Text = "SimHub se ferme puis se relance tout seul";
            texts.Children.Add(_bannerText);
            if (extra != null) texts.Children.Add(extra);
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);

            _banner.Child = grid;
            _banner.Background = Theme.B("Card");
            _banner.BorderBrush = Theme.B("Line");
            _banner.Visibility = Visibility.Visible;
        }

        // =====================================================================
        // Bande d'état : IA, client vocal, push-to-talk, jeu
        // =====================================================================

        private sealed class Cell
        {
            public Ellipse Dot;
            public TextBlock Value, Detail;
            public StackPanel Actions;
            private string _last;

            public void Set(string value, string detail, Brush color)
            {
                var key = value + "|" + detail + "|" + color;
                if (key == _last) return;
                _last = key;
                Value.Text = value;
                Detail.Text = detail ?? "";
                Dot.Fill = color;
            }
        }

        private UIElement BuildStrip()
        {
            _strip = new UniformGrid { Columns = 4 };
            _aiCell = AddCell("Intelligence artificielle");
            _aiCell.Actions.Children.Add(Btn("Configurer", () => Reveal(_aiCard), "BtnGhost", null, small: true));
            _clientCell = AddCell("Client vocal");
            _clientButton = Btn("Démarrer", () => _plugin.Client.Restart(), "BtnGhost", GRefresh, small: true);
            _clientCell.Actions.Children.Add(_clientButton);
            _clientCell.Actions.Children.Add(Btn("Journal", () => Open(ClientRunner.LogPath), "BtnGhost", GDoc, small: true));
            _pttCell = AddCell("Push-to-talk");
            _pttCell.Actions.Children.Add(Btn("Comment l'associer", () => Reveal(_pttCard), "BtnGhost", null, small: true));
            _gameCell = AddCell("Jeu");
            _gameCell.Actions.Children.Add(Btn("Télémétrie", () => Open(_plugin.Url), "BtnGhost", GLink, small: true));

            var card = Card(_strip, new Thickness(0));
            card.Margin = new Thickness(0, 16, 0, 0);
            return card;
        }

        /// <summary>
        /// Amène une carte à l'écran et l'entoure un instant. SimHub place la page dans son propre défilement :
        /// on cherche le premier ScrollViewer qui défile vraiment. Si la carte est déjà visible, le contour suffit à la montrer.
        /// </summary>
        private void Reveal(Border card)
        {
            for (DependencyObject d = VisualTreeHelper.GetParent(card); d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is ScrollViewer sv && sv.ScrollableHeight > 0)
                {
                    var top = card.TransformToAncestor(sv).Transform(new Point(0, 0)).Y;
                    if (top < 0 || top + Math.Min(card.ActualHeight, sv.ViewportHeight) > sv.ViewportHeight)
                        sv.ScrollToVerticalOffset(Math.Max(0, sv.VerticalOffset + top - 16));
                    break;
                }
            }
            card.BorderBrush = Theme.B("AccentLine");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            timer.Tick += (s, e) => { timer.Stop(); card.BorderBrush = Theme.B("Line"); };
            timer.Start();
        }

        // ---------- Radio check / Écouter : une phrase à la fois ----------

        private bool Speaking => _plugin.ClientConnected && (_plugin.ClientStatus ?? "").StartsWith("À la radio");

        /// <summary>
        /// Occupé du clic jusqu'à la fin de la phrase : le client passe « À la radio » puis « Prêt ».
        /// Si le client ne répond pas (arrêté, ancienne version), on libère au bout de 8 s.
        /// </summary>
        private bool SayBusy
        {
            get
            {
                if (Speaking) { _saySeen = true; return true; }
                var waited = (DateTime.UtcNow - _sayClicked).TotalSeconds;
                return !_saySeen && waited < 8;
            }
        }

        private void SayTest(string text)
        {
            if (SayBusy) return;
            _plugin.Say(text);
            _sayClicked = DateTime.UtcNow;
            _saySeen = false;
            RefreshSayButtons();
        }

        private void RefreshSayButtons()
        {
            bool busy = SayBusy;
            if (busy == _sayBusyLast) return;
            _sayBusyLast = busy;
            if (_radioButton != null) _radioButton.IsEnabled = !busy;
            if (_listenButton != null)
            {
                _listenButton.IsEnabled = !busy;
                _listenButton.Content = BtnContent(busy ? "À la radio" : "Écouter", busy ? GSync : GPlay);
            }
        }

        private Cell AddCell(string label)
        {
            var c = new Cell();
            var stack = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            c.Dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            head.Children.Add(c.Dot);
            head.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Theme.B("Text2") });
            stack.Children.Add(head);
            c.Value = new TextBlock { FontSize = 15, FontWeight = FontWeights.Medium, Margin = new Thickness(16, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            stack.Children.Add(c.Value);
            c.Detail = new TextBlock { FontSize = 12, Foreground = Theme.B("Muted"), Margin = new Thickness(16, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Height = 32, TextTrimming = TextTrimming.CharacterEllipsis };
            stack.Children.Add(c.Detail);
            c.Actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 0, 0) };
            stack.Children.Add(c.Actions);

            _strip.Children.Add(new Border
            {
                Padding = new Thickness(16, 16, 16, 12), Child = stack,
                BorderBrush = Theme.B("Line"),
            });
            return c;
        }

        private void RefreshStrip()
        {
            // IA
            var info = ProviderInfo.Get(S.Provider);
            if (S.AiConfigured)
            {
                bool failed = _tester.Ok == false && _testProvider == S.Provider;
                _aiCell.Set(info.Name, S.ModelOrDefault() + (failed ? " · dernier test en échec" : ""), failed ? Theme.B("Warn") : Theme.B("Ok"));
            }
            else _aiCell.Set("À configurer", "Choisis un fournisseur et colle ta clé", Theme.B("Warn"));
            _aiCell.Actions.Visibility = S.AiConfigured ? Visibility.Hidden : Visibility.Visible;

            // Client vocal
            var client = _plugin.Client;
            var status = _plugin.ClientStatus ?? "";
            if (_plugin.ClientConnected)
            {
                if (status.StartsWith("Prêt")) _clientCell.Set("Connecté", "Prêt à répondre", Theme.B("Ok"));
                else if (status.StartsWith("À la radio")) _clientCell.Set("Connecté", "À la radio", Theme.B("Ok"));
                else if (status.StartsWith("Erreur") || status.StartsWith("Micro") || status.StartsWith("ElevenLabs") || status.StartsWith("Voix en ligne"))
                    _clientCell.Set("Connecté", status, Theme.B("Warn"));
                else _clientCell.Set("Chargement", status, Theme.B("AccentLine"));
            }
            else if (client.Running) _clientCell.Set("Démarrage", "Le premier lancement peut prendre une minute", Theme.B("AccentLine"));
            else if (client.LastError != null) _clientCell.Set("Arrêté", client.LastError, Theme.B("Bad"));
            else _clientCell.Set("Arrêté", S.AutoStartClient ? "Démarrage automatique dans un instant" : "Démarrage automatique désactivé", Theme.B("Muted"));

            var label = client.Running || _plugin.ClientConnected ? "Redémarrer" : "Démarrer";
            if (label != _clientButtonLabel)
            {
                _clientButtonLabel = label;
                _clientButton.Content = BtnContent(label, label == "Démarrer" ? GPlay : GRefresh);
            }

            // Push-to-talk
            if (_plugin.PushToTalkDown) _pttCell.Set("Appuyé", "Parle, puis relâche pour envoyer", Theme.B("Ok"));
            else if (S.PttTested) _pttCell.Set("Associé", "Maintiens ton bouton pour parler", Theme.B("Ok"));
            else _pttCell.Set("Non associé", "Associe un bouton du volant puis appuie dessus", Theme.B("Warn"));
            _pttCell.Actions.Visibility = S.PttTested ? Visibility.Hidden : Visibility.Visible;

            // Jeu
            if (_plugin.ServerError != null) _gameCell.Set("Port occupé", _plugin.ServerError, Theme.B("Bad"));
            else if (_plugin.GameName != null) _gameCell.Set(_plugin.GameName, _plugin.GameDetail, Theme.B("Ok"));
            else _gameCell.Set("En attente", "Lance une session, la télémétrie arrive toute seule", Theme.B("Muted"));
        }

        // =====================================================================
        // Intelligence artificielle
        // =====================================================================

        private Border BuildAiCard()
        {
            var (card, body) = Section("Intelligence artificielle", "Comprend tes questions et répond avec la télémétrie");

            var segments = new WrapPanel();
            foreach (var info in ProviderInfo.All)
            {
                var id = info.Id;
                segments.Children.Add(Segment("provider", info.Name, S.Provider == id, () =>
                {
                    if (S.Provider == id) return;
                    S.Provider = id;
                    S.Current(); // crée l'entrée avec le modèle par défaut
                    Changed();
                    RebuildProviderFields();
                }));
            }
            body.Children.Add(segments);
            _providerHint = new TextBlock { FontSize = 12, Foreground = Theme.B("Muted"), Margin = new Thickness(0, 0, 0, 8) };
            body.Children.Add(_providerHint);

            _providerFields = new StackPanel();
            body.Children.Add(_providerFields);
            RebuildProviderFields();

            var test = Btn("Tester la connexion", RunTest, "BtnPrimary", GPlay);
            test.HorizontalAlignment = HorizontalAlignment.Left;
            test.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(test);
            _testBox = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
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
            _providerHint.Text = info.Tagline;

            // La liste des modèles se recharge quand on quitte un champ d'identification
            RoutedEventHandler reload = (s, e) => _catalog.Refresh(info, p);
            if (info.Id == "custom")
            {
                var url = Text(info.BaseUrl(p), v => { p.BaseUrl = v; Changed(); });
                url.LostFocus += reload;
                _providerFields.Children.Add(Field("URL de l'API", url, "Ollama : http://localhost:11434/v1"));
            }
            if (info.AccountLabel != null)
            {
                var account = Text(p.AccountId, v => { p.AccountId = v; Changed(); });
                account.LostFocus += reload;
                _providerFields.Children.Add(Field(info.AccountLabel, account, info.AccountHelp.TrimEnd('.')));
            }

            var keyHelp = new TextBlock { FontSize = 12, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap };
            keyHelp.Inlines.Add(new Run(info.KeyHelp.TrimEnd('.') + ", enregistrée chiffrée sur ce PC"));
            if (info.KeyHelpUrl != null)
            {
                var link = new Hyperlink(new Run("Obtenir une clé")) { Foreground = Theme.B("AccentLine") };
                link.Click += (s, e) => Open(info.KeyHelpUrl);
                keyHelp.Inlines.Add(new Run(" · "));
                keyHelp.Inlines.Add(link);
            }
            var secret = Secret(p.ApiKey, v => { p.ApiKey = v; Changed(); });
            secret.LostFocus += reload;
            _providerFields.Children.Add(Field(info.NeedsKey ? "Clé API" : "Clé (optionnelle)", secret, keyHelp));

            _modelCombo = new ComboBox { IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch, FontFamily = Theme.Mono };
            _modelCombo.DropDownOpened += (s, e) => _catalog.Refresh(info, p);
            _modelCombo.DropDownClosed += (s, e) => ShowModelStart();
            _modelCombo.Loaded += (s, e) => ShowModelStart();
            _catalogRev = -1; // remplit la liste au prochain rafraîchissement
            FillModels();
            _modelCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) =>
            {
                if (p.Model == _modelCombo.Text) return;
                p.Model = _modelCombo.Text;
                Changed();
            }));
            _modelHelp = new TextBlock { FontSize = 12, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap };
            _providerFields.Children.Add(Field("Modèle", _modelCombo, _modelHelp));
            _catalog.Refresh(info, p);
        }

        /// <summary>Liste « Modèle » : les modèles récupérés chez le fournisseur, sinon les conseillés.</summary>
        private void FillModels()
        {
            var info = ProviderInfo.Get(S.Provider);
            var p = S.Current();
            bool fresh = _catalog.Key == ModelCatalog.KeyFor(info, p);
            var models = fresh && _catalog.Models.Length > 0 ? _catalog.Models : info.Models;

            var text = S.ModelOrDefault();
            _modelCombo.Items.Clear();
            foreach (var m in models) _modelCombo.Items.Add(m);
            _modelCombo.Text = text;
            ShowModelStart();
        }

        /// <summary>Les noms longs (« mistralai/... ») s'affichent depuis le début, pas coupés à gauche.</summary>
        private void ShowModelStart()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_modelCombo?.Template?.FindName("PART_EditableTextBox", _modelCombo) is TextBox box && !box.IsKeyboardFocused)
                {
                    box.CaretIndex = 0;
                    box.ScrollToHome();
                }
            }), DispatcherPriority.Background);
        }

        private void RefreshModels()
        {
            if (_modelHelp == null) return;
            var info = ProviderInfo.Get(S.Provider);
            var p = S.Current();
            bool fresh = _catalog.Key == ModelCatalog.KeyFor(info, p);
            string extra =
                _catalog.Busy ? " · récupération de la liste des modèles"
                : !fresh ? ""
                : _catalog.Error != null ? " · " + _catalog.Error
                : _catalog.Models.Length > 0 ? $" · {_catalog.Models.Length} disponibles"
                : "";
            var help = "Un petit modèle rapide suffit" + extra;
            if (_modelHelp.Text != help) _modelHelp.Text = help;

            if (_catalog.Revision != _catalogRev && !_catalog.Busy)
            {
                if (_modelCombo.IsDropDownOpen) return; // on attend que la liste soit refermée
                _catalogRev = _catalog.Revision;
                FillModels();
            }
        }

        private void RefreshTest()
        {
            if (_tester.Revision == _testRev) return;
            _testRev = _tester.Revision;
            if (_tester.Revision == 0 || _testProvider != S.Provider) { _testBox.Visibility = Visibility.Collapsed; return; }

            Brush strong = _tester.Ok == true ? Theme.B("Ok") : _tester.Ok == false ? Theme.B("Bad") : Theme.B("Text2");
            Brush soft = _tester.Ok == true ? Theme.B("OkSoft") : _tester.Ok == false ? Theme.B("BadSoft") : Theme.B("Raised");
            var glyph = _tester.Ok == true ? GCheck : _tester.Ok == false ? GError : GSync;

            var stack = new StackPanel();
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = Glyph(glyph, 12, strong);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 4, 8, 0);
            line.Children.Add(icon);
            var text = new TextBlock { Text = _tester.Message, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            Grid.SetColumn(text, 1);
            line.Children.Add(text);
            stack.Children.Add(line);

            if (_tester.Suggestions.Length > 0)
            {
                var chips = new WrapPanel { Margin = new Thickness(20, 0, 0, 0) };
                foreach (var m in _tester.Suggestions)
                {
                    var model = m;
                    chips.Children.Add(Btn(model, () => { _modelCombo.Text = model; RunTest(); }, "Chip"));
                }
                stack.Children.Add(chips);
            }

            _testBox.Child = stack;
            _testBox.Background = soft;
            _testBox.Visibility = Visibility.Visible;
        }

        // =====================================================================
        // Langue et voix
        // =====================================================================

        private Border BuildVoiceCard()
        {
            var (card, body) = Section("Langue et voix", "La reconnaissance vocale tourne sur ton PC");

            body.Children.Add(Field("Langue", Segments("lang", new[] { ("fr", "Français"), ("en", "English") }, S.Language, v =>
            {
                S.Language = v;
                Changed();
                RebuildVoiceFields();
                RebuildCallouts();
            })));

            _engineHint = new TextBlock { FontSize = 12, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap };
            var engines = Segments("engine", new[] { ("neural", "En ligne"), ("kokoro", "Hors ligne"), ("elevenlabs", "ElevenLabs") }, S.TtsEngine, v =>
            {
                S.TtsEngine = v;
                Changed();
                RebuildVoiceFields();
            });
            body.Children.Add(Field("Moteur", engines, _engineHint));

            _voiceFields = new StackPanel();
            body.Children.Add(_voiceFields);
            RebuildVoiceFields();

            body.Children.Add(Field("Reconnaissance", Combo(new[]
            {
                ("small", "Précise, environ 1,3 s"),
                ("base", "Rapide, environ 0,4 s"),
            }, S.SttModel, v => { S.SttModel = v; Changed(); })));
            body.Children.Add(Divider());
            FrameworkElement track = null;
            body.Children.Add(ToggleRow("Effet radio", "Filtre radio et léger grésillement", S.RadioEffect, v => { S.RadioEffect = v; track.IsEnabled = v; Changed(); }));
            track = ToggleRow("Bruits de piste", "Des F1 passent parfois derrière l'ingé, jamais deux fois pareil. Sa voix reste devant", S.TrackNoise, v => { S.TrackNoise = v; Changed(); });
            track.IsEnabled = S.RadioEffect;
            body.Children.Add(track);
            return card;
        }

        private void RebuildVoiceFields()
        {
            _voiceFields.Children.Clear();
            _engineHint.Text =
                S.TtsEngine == "neural" ? "Voix neuronales Microsoft, gratuites et très naturelles. Sans internet, la voix hors ligne prend le relais"
                : S.TtsEngine == "kokoro" ? "Kokoro, sur ton PC et sans internet. Moins naturelle en français"
                : "La plus réaliste, payante à l'usage";

            if (S.TtsEngine == "elevenlabs")
            {
                _voiceFields.Children.Add(Field("Clé API", Secret(S.ElevenLabsKey, v => { S.ElevenLabsKey = v; Changed(); }), "Enregistrée chiffrée sur ce PC"));
                _voiceFields.Children.Add(Field("Voice ID", Text(S.ElevenLabsVoiceId, v => { S.ElevenLabsVoiceId = v; Changed(); }), "Copié depuis ta bibliothèque ElevenLabs"));
                return;
            }

            bool neural = S.TtsEngine == "neural";
            var list = (neural ? VoiceInfo.Neural : VoiceInfo.Kokoro).Where(v => v.Language == S.Language).Select(v => (v.Id, v.Label)).ToArray();
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Combo(list, neural ? S.NeuralVoice : S.Voice, v =>
            {
                if (neural) { if (Fr) S.NeuralVoiceFr = v; else S.NeuralVoiceEn = v; }
                else { if (Fr) S.VoiceFr = v; else S.VoiceEn = v; }
                Changed();
            }));
            var listen = _listenButton = Btn("Écouter", () => SayTest(Fr
                ? "Box box, ce tour-ci. Tu ressors en P4, la voiture derrière est à deux secondes."
                : "Box box, this lap. You'll rejoin in P4, the car behind is two seconds back."), "Btn", GPlay);
            listen.Margin = new Thickness(8, 0, 0, 0);
            _sayBusyLast = false; // le nouveau bouton reprend l'état au prochain rafraîchissement
            Grid.SetColumn(listen, 1);
            row.Children.Add(listen);
            _voiceFields.Children.Add(Field("Voix", row));
            _voiceFields.Children.Add(Field("Débit", Combo(new[] { ("1.0", "Normal"), ("1.1", "Un peu rapide"), ("1.25", "Rapide") },
                S.VoiceSpeed.ToString("0.0#", CultureInfo.InvariantCulture),
                v => { S.VoiceSpeed = double.Parse(v, CultureInfo.InvariantCulture); Changed(); })));
        }

        // =====================================================================
        // Annonces automatiques
        // =====================================================================

        private Border BuildCalloutsCard()
        {
            var (card, body) = Section("Annonces automatiques", "Sans IA ni internet, donc instantanées et gratuites");
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
            var gaps = fr ? "Devant à 1,4, derrière à 0,8." : "Gap ahead 1.4, behind 0.8.";

            rows.Add(ToggleRow("Temps au tour", lap, S.CallLapTime, v => { S.CallLapTime = v; Changed(); }, radio: true));
            rows.Add(Divider());
            rows.Add(ToggleRow("Meilleur tour", fr ? "Meilleur tour !" : "Personal best!", S.CallPersonalBest, v => { S.CallPersonalBest = v; Changed(); }, radio: true));
            rows.Add(Divider());
            rows.Add(ToggleRow("Écarts, en course", gaps, S.CallGaps, v => { S.CallGaps = v; Changed(); }, radio: true));
            rows.Add(Divider());
            rows.Add(ToggleRow("Places gagnées ou perdues", Callouts.SpokenPosition(5, 1, fr), S.CallPosition, v => { S.CallPosition = v; Changed(); }, radio: true));
            rows.Add(Divider());
            rows.Add(ToggleRow("Carburant", fr ? "Carburant : environ 3 tours restants." : "Fuel: about 3 laps left.", S.CallFuel, v => { S.CallFuel = v; Changed(); }, radio: true));
            rows.Add(Field("Prévenir à", Combo(new[] { ("2", "2 tours restants"), ("3", "3 tours restants"), ("4", "4 tours restants"), ("5", "5 tours restants") },
                S.FuelWarningLaps.ToString(), v => { S.FuelWarningLaps = int.Parse(v); Changed(); })));
        }

        // =====================================================================
        // Push-to-talk
        // =====================================================================

        private Border BuildPttCard()
        {
            var (card, body) = Section("Push-to-talk", "Maintiens un bouton du volant pour parler, relâche pour envoyer");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });

            var steps = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            var texts = new[]
            {
                "Ouvre Controls and events dans SimHub",
                $"Ajoute un mapping avec l'action AIRaceEnginePlugin.{AIRaceEnginePlugin.PushToTalkAction}",
                "Appuie sur le bouton du volant à associer",
            };
            for (int i = 0; i < texts.Length; i++)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontFamily = Theme.Mono, FontSize = 12, Foreground = Theme.B("Muted"), Margin = new Thickness(0, 1, 0, 0) });
                var t = new TextBlock { Text = texts[i], TextWrapping = TextWrapping.Wrap, FontSize = 13 };
                Grid.SetColumn(t, 1);
                row.Children.Add(t);
                steps.Children.Add(row);
            }
            grid.Children.Add(steps);

            var lamp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _pttLamp = new Ellipse { Width = 32, Height = 32, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Center };
            lamp.Children.Add(_pttLamp);
            _pttLampText = new TextBlock { FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            lamp.Children.Add(_pttLampText);
            var tester = new Border { Background = Theme.B("Card"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = lamp };
            Grid.SetColumn(tester, 1);
            grid.Children.Add(tester);

            body.Children.Add(grid);
            return card;
        }

        private void RefreshPtt()
        {
            bool down = _plugin.PushToTalkDown;
            if (_pttLast == down) return;
            _pttLast = down;
            _pttLamp.Fill = down ? Theme.B("Ok") : Theme.B("Raised");
            _pttLamp.Stroke = down ? Theme.B("Ok") : Theme.B("Line");
            _pttLampText.Text = down ? "Je t'écoute" : "Appuie pour tester";
            _pttLampText.Foreground = down ? Theme.B("Ok") : Theme.B("Muted");
        }

        // =====================================================================
        // Client vocal et mises à jour
        // =====================================================================

        private Border BuildClientCard()
        {
            var (card, body) = Section("Client vocal et mises à jour", null);

            body.Children.Add(ToggleRow("Démarrer avec SimHub", "En arrière-plan, relancé tout seul en cas de souci", S.AutoStartClient, v => { S.AutoStartClient = v; Changed(); }));
            var tools = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            tools.Children.Add(Btn("Redémarrer", () => _plugin.Client.Restart(), "Btn", GRefresh));
            tools.Children.Add(Btn("Journal", () => Open(ClientRunner.LogPath), "Btn", GDoc));
            tools.Children.Add(Btn("Dossier", () => Open(EngineerSettings.DataDir), "Btn", GFolder));
            body.Children.Add(tools);

            body.Children.Add(Divider());
            var u = _plugin.Updater;
            var version = new TextBlock { FontSize = 13, Margin = new Thickness(0, 8, 0, 0) };
            version.Inlines.Add(new Run("Version "));
            version.Inlines.Add(new Run(u.Current.ToString()) { FontFamily = Theme.Mono });
            body.Children.Add(version);
            _updateState = new TextBlock { Foreground = Theme.B("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            body.Children.Add(_updateState);
            var ub = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            ub.Children.Add(Btn("Rechercher une mise à jour", () => u.CheckAsync(), "Btn", GSync));
            ub.Children.Add(Btn("Notes de version", () => Open($"https://github.com/{Updater.Repo}/releases"), "BtnGhost", GLink));
            body.Children.Add(ub);
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
                RefreshStrip();
                RefreshPtt();
                RefreshTest();
                RefreshModels();
                RefreshSayButtons();
                var state = (_plugin.Updater.State ?? "").TrimEnd('.');
                _updateState.Text = state;
                _updateState.Visibility = state.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
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

        private static (Border Card, StackPanel Body) Section(string title, string subtitle)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = title, FontSize = 15, FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, subtitle == null ? 12 : 0),
            });
            if (subtitle != null)
                body.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 16) });
            return (Card(body, new Thickness(20)), body);
        }

        private static Border Card(UIElement child, Thickness padding) => new Border
        {
            Background = Theme.B("Card"), BorderBrush = Theme.B("Line"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = padding, Child = child,
        };

        private static Border Divider() => new Border { Height = 1, Background = Theme.B("Line"), Margin = new Thickness(0, 4, 0, 4) };

        private static TextBlock Glyph(string glyph, double size, Brush brush) => new TextBlock
        {
            Text = glyph, FontFamily = Theme.Icons, FontSize = size, Foreground = brush,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>Ligne de réglage : libellé à gauche, contrôle et aide éventuelle à droite.</summary>
        private static FrameworkElement Field(string label, UIElement control, object help = null)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Theme.B("Text2"), Margin = new Thickness(0, 6, 12, 0), TextWrapping = TextWrapping.Wrap });

            var right = new StackPanel();
            right.Children.Add(control);
            if (help is string h)
                right.Children.Add(new TextBlock { Text = h, FontSize = 12, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            else if (help is FrameworkElement fe)
            {
                fe.Margin = new Thickness(0, 4, 0, 0);
                right.Children.Add(fe);
            }
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            return g;
        }

        /// <summary>Ligne avec interrupteur. radio : la ligne d'aide est une phrase de l'ingé, en mono comme un journal radio.</summary>
        private static FrameworkElement ToggleRow(string title, string detail, bool value, Action<bool> onChange, bool radio = false)
        {
            var g = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(new TextBlock { Text = title, FontSize = 13 });
            if (detail != null)
                left.Children.Add(radio ? RadioLine(detail) : new TextBlock { Text = detail, FontSize = 12, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            g.Children.Add(left);

            var toggle = new CheckBox { Style = Theme.S("Toggle"), IsChecked = value };
            toggle.Checked += (s, e) => onChange(true);
            toggle.Unchecked += (s, e) => onChange(false);
            Grid.SetColumn(toggle, 1);
            g.Children.Add(toggle);
            return g;
        }

        /// <summary>Signature de la page : une phrase de l'ingé, en mono, avec un trait comme dans un journal radio.</summary>
        private static UIElement RadioLine(string text) => new Border
        {
            BorderBrush = Theme.B("Hover"), BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(8, 0, 0, 0), Margin = new Thickness(0, 4, 0, 0),
            Child = new TextBlock { Text = text, FontFamily = Theme.Mono, FontSize = 12, Foreground = Theme.B("Text2"), TextWrapping = TextWrapping.Wrap },
        };

        private static RadioButton Segment(string group, string label, bool selected, Action onSelect)
        {
            var r = new RadioButton { Style = Theme.S("Segment"), GroupName = group, Content = label, IsChecked = selected };
            r.Checked += (s, e) => onSelect();
            return r;
        }

        private static Panel Segments(string group, (string Value, string Label)[] items, string selected, Action<string> onChange)
        {
            var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, -8) };
            foreach (var (value, label) in items)
            {
                var v = value;
                panel.Children.Add(Segment(group, label, value == selected, () => onChange(v)));
            }
            return panel;
        }

        private static object BtnContent(string text, string glyph)
        {
            if (glyph == null) return text;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        private static Button Btn(string text, Action onClick, string style = "Btn", string glyph = null, bool small = false)
        {
            var b = new Button { Style = Theme.S(style), Content = BtnContent(text, glyph) };
            if (small) { b.Padding = new Thickness(8, 4, 8, 4); b.FontSize = 12; }
            b.Click += (s, e) => onClick();
            return b;
        }

        private static ComboBox Combo((string Value, string Label)[] items, string selected, Action<string> onChange)
        {
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
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
            var t = new TextBox { Text = value ?? "", HorizontalAlignment = HorizontalAlignment.Stretch };
            t.TextChanged += (s, e) => onChange(t.Text);
            return t;
        }

        private static PasswordBox Secret(string value, Action<string> onChange)
        {
            var p = new PasswordBox { Password = value ?? "", HorizontalAlignment = HorizontalAlignment.Stretch };
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
