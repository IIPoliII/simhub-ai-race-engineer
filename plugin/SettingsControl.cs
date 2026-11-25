using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using static AIRaceEngineer.L10n;

namespace AIRaceEngineer
{
    /// <summary>
    /// Page du plugin dans SimHub : une bande d'état, puis les réglages en colonnes (3, 2 ou 1 selon la largeur).
    /// En français ou en anglais (<see cref="L10n"/>) : changer de langue reconstruit la page.
    /// </summary>
    public class SettingsControl : UserControl
    {
        // Glyphes Segoe Fluent Icons / MDL2 Assets
        private const string GCheck = "", GPlay = "", GDoc = "", GFolder = "", GRefresh = "",
            GDownload = "", GLink = "", GPower = "", GSync = "", GError = "";

        private readonly AIRaceEnginePlugin _plugin;
        private EngineerSettings S => _plugin.Settings;
        private readonly LlmTester _tester = new LlmTester();
        private readonly ModelCatalog _catalog = new ModelCatalog();
        private int _catalogRev = -1;
        private TextBlock _modelHelp;
        private readonly DispatcherTimer _timer;
        private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(200), Slow = TimeSpan.FromSeconds(1);
        private Window _window;
        private int _refreshErrors; // erreurs d'affichage depuis l'ouverture de la page
        private const int MaxRefreshErrors = 5;
        private bool _noticeDismissed;
        private DispatcherTimer _saveTimer;

        private Border _banner, _aiCard, _voiceCard, _calloutsCard, _pttCard, _clientCard, _testBox;
        private string _bannerMode;
        private bool _bannerDismissed, _installClicked, _installSeen;
        private DateTime _installClickedAt;
        private TextBlock _bannerText, _updateState, _pttLampText, _providerHint, _engineHint;
        private Cell _aiCell, _clientCell, _pttCell, _gameCell;
        private UniformGrid _strip;
        private Grid _columns;
        private int _columnCount;
        private Button _clientButton, _clientCardButton, _installButton, _testButton, _radioButton, _listenButton, _providerHelp, _elevenHelp;
        // Carte « Client vocal et mises à jour » : son bouton principal suit la mise à jour, comme la bannière
        private Button _cardCheckButton, _cardInstallButton, _cardRestartButton;
        private string _cardInstallLabel;
        private DateTime _sayClicked = DateTime.MinValue;
        private bool _saySeen, _sayBusyLast;
        private string _clientButtonLabel;
        private Ellipse _pttLamp;
        private string _pttLast;
        private TextBlock _pttResult;
        private StackPanel _providerFields, _voiceFields, _calloutRows;
        private ComboBox _modelCombo;
        private bool _fillingModels;
        private int _testRev = -1;
        private bool _testShown;
        private string _testKey;

        public static readonly ImageSource Icon = CreateIcon(Brushes.White);

        public SettingsControl(AIRaceEnginePlugin plugin)
        {
            _plugin = plugin;
            Resources.MergedDictionaries.Add(Theme.Resources);
            Build();

            _timer = new DispatcherTimer { Interval = Fast };
            _timer.Tick += (s, e) => SafeRefresh();
            Loaded += (s, e) => Watch(true);
            Unloaded += (s, e) => { Watch(false); FlushPendingSave(); };
            IsVisibleChanged += (s, e) => UpdateTimer();
        }

        /// <summary>
        /// Suit la fenêtre de SimHub : 5 rafraîchissements par seconde au premier plan, 1 quand elle est derrière le jeu,
        /// aucun quand elle est réduite ou cachée.
        /// </summary>
        private void Watch(bool loaded)
        {
            if (_window != null)
            {
                _window.StateChanged -= OnWindowChanged;
                _window.Activated -= OnWindowChanged;
                _window.Deactivated -= OnWindowChanged;
            }
            _window = loaded ? Window.GetWindow(this) : null;
            if (_window != null)
            {
                _window.StateChanged += OnWindowChanged;
                _window.Activated += OnWindowChanged;
                _window.Deactivated += OnWindowChanged;
            }
            if (loaded) _refreshErrors = 0; // comme avant : une page rouverte réessaie
            UpdateTimer();
        }

        private void OnWindowChanged(object sender, EventArgs e) => UpdateTimer();

        private void UpdateTimer()
        {
            var w = _window;
            if (_refreshErrors >= MaxRefreshErrors || !IsLoaded || !IsVisible || w?.WindowState == WindowState.Minimized) { _timer.Stop(); return; }
            var interval = w == null || w.IsActive ? Fast : Slow;
            if (_timer.Interval != interval) _timer.Interval = interval;
            if (_timer.IsEnabled) return;
            _timer.Start();
            SafeRefresh(); // trop d'erreurs l'arrêtent aussitôt
        }

        /// <summary>Construit toute la page, dans la langue des réglages.</summary>
        private void Build()
        {
            L10n.Language = S.Language;
            // Les éléments de l'ancienne page sont oubliés : tout se redessine au prochain rafraîchissement
            _columnCount = 0;
            _bannerMode = null;
            _bannerText = null;
            _installButton = null;
            _clientButtonLabel = null;
            _pttLast = null;
            _sayBusyLast = false;
            _testRev = -1;

            var root = new StackPanel { Margin = new Thickness(24, 16, 24, 32), MaxWidth = 1600 };
            TextElement.SetFontFamily(root, Theme.Ui);
            TextElement.SetForeground(root, Theme.B("Text"));
            root.Children.Add(BuildHeader());
            if (S.LoadNotice != null && !_noticeDismissed) root.Children.Add(BuildNotice());
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
        }

        /// <summary>Changement de langue : la page est reconstruite une fois le clic terminé.</summary>
        private void Rebuild()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Build();
                if (IsLoaded) SafeRefresh();
            }), DispatcherPriority.Background);
        }

        private void Changed()
        {
            StopPendingSave(); // tout est enregistré d'un coup, champs texte compris
            _plugin.SettingsChanged();
        }

        /// <summary>
        /// Champs texte : la valeur change tout de suite en mémoire, mais on n'enregistre et n'envoie au client
        /// qu'après une pause dans la frappe, en quittant le champ ou la page, ou quand SimHub se ferme.
        /// </summary>
        private void ChangedSoon()
        {
            if (_saveTimer == null)
            {
                _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _saveTimer.Tick += (s, e) => Changed();
            }
            if (!_saveTimer.IsEnabled) Dispatcher.ShutdownStarted += SaveOnShutdown;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void FlushPendingSave()
        {
            if (_saveTimer != null && _saveTimer.IsEnabled) Changed();
        }

        private void StopPendingSave()
        {
            if (_saveTimer == null || !_saveTimer.IsEnabled) return;
            _saveTimer.Stop();
            Dispatcher.ShutdownStarted -= SaveOnShutdown;
        }

        private void SaveOnShutdown(object sender, EventArgs e) => FlushPendingSave();

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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var welcome = Help(T("Bienvenue et guide de démarrage", "Welcome and getting started"), GuideWindows.ShowWelcome);
            welcome.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(welcome, 2);
            grid.Children.Add(welcome);

            var left = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            left.Inlines.Add(new Run(T("Ton ingénieur de course à la radio, pour tous les jeux SimHub", "Your race engineer on the radio, for every SimHub game")) { FontSize = 14, Foreground = Theme.B("Text2") });
            left.Inlines.Add(new Run("   v" + _plugin.Updater.Current) { FontSize = 12, FontFamily = Theme.Mono, Foreground = Theme.B("Muted") });
            grid.Children.Add(left);

            var radio = _radioButton = Btn("Radio check", () => SayTest(T("Radio check. Tu me reçois cinq sur cinq ?", "Radio check, how do you read me?")), "Btn", GPlay);
            radio.Margin = new Thickness(0);
            Grid.SetColumn(radio, 1);
            grid.Children.Add(radio);
            return grid;
        }

        /// <summary>settings.json abîmé au chargement : ce qui a été repris, affiché jusqu'à ce qu'on le ferme.</summary>
        private UIElement BuildNotice()
        {
            var notice = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 16, 0, 0),
                BorderThickness = new Thickness(1), Background = Theme.B("Card"), BorderBrush = Theme.B("Line"),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Glyph(GError, 16, Theme.B("Warn")));
            var text = new TextBlock { Text = S.LoadNotice.ToString(), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(Btn(T("Dossier", "Folder"), () => Open(EngineerSettings.DataDir), "BtnGhost", GFolder));
            buttons.Children.Add(Btn(T("Compris", "Got it"), () => { _noticeDismissed = true; notice.Visibility = Visibility.Collapsed; }, "BtnGhost"));
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
            notice.Child = grid;
            return notice;
        }

        private void RefreshBanner()
        {
            var u = _plugin.Updater;
            // Installation lancée d'ici : affichée tant que l'Updater y travaille (jusqu'au succès, à l'échec ou à l'annulation)
            if (_installClicked)
            {
                if (u.Busy) _installSeen = true;
                else if (_installSeen || (DateTime.UtcNow - _installClickedAt).TotalSeconds > 5) _installClicked = false;
            }
            string mode = u.Installed ? "installed"
                        : _installClicked ? "busy"
                        : u.UpdateAvailable ? "available"
                        : null;
            RefreshCardUpdate(mode); // la carte garde « Redémarrer SimHub » même quand la bannière est fermée
            if (mode == "installed" && _bannerDismissed) mode = null;

            if (mode != _bannerMode)
            {
                _bannerMode = mode;
                BuildBanner(mode);
            }
            // Pendant une recherche de mise à jour, Installer ne ferait rien : grisé
            if (_installButton != null) _installButton.IsEnabled = !u.Busy;
            if (_bannerText == null) return;
            if (mode == "busy") _bannerText.Text = u.State;
            else if (mode == "available")
                _bannerText.Text = u.Failed
                    ? u.State
                    : T($"Tu as la {u.Current}. Le client vocal est mis à jour en même temps", $"You have {u.Current}. The voice client is updated at the same time");
        }

        /// <summary>Installer de la bannière et de la carte « Client vocal et mises à jour ».</summary>
        private void StartInstall()
        {
            var u = _plugin.Updater;
            if (u.Busy || _installClicked) return; // InstallAsync ne ferait rien (recherche en cours, double clic)
            _installClicked = true;
            _installSeen = false;
            _installClickedAt = DateTime.UtcNow;
            u.InstallAsync(_plugin.Client);
        }

        /// <summary>
        /// Bouton principal de la carte : « Installer la X » quand une mise à jour attend (grisé pendant l'installation),
        /// « Redémarrer SimHub » quand elle est installée, sinon « Rechercher une mise à jour ».
        /// </summary>
        private void RefreshCardUpdate(string mode)
        {
            if (_cardCheckButton == null) return;
            var u = _plugin.Updater;
            bool install = mode == "available" || mode == "busy";
            _cardInstallButton.Visibility = install ? Visibility.Visible : Visibility.Collapsed;
            _cardRestartButton.Visibility = mode == "installed" ? Visibility.Visible : Visibility.Collapsed;
            _cardCheckButton.Visibility = mode == null ? Visibility.Visible : Visibility.Collapsed;
            if (!install) return;
            _cardInstallButton.IsEnabled = mode == "available" && !u.Busy;
            string label = mode == "busy" ? T("Installation...", "Installing...") : T($"Installer la {u.Latest}", $"Install {u.Latest}");
            if (label != _cardInstallLabel)
            {
                _cardInstallLabel = label;
                _cardInstallButton.Content = BtnContent(label, GDownload);
            }
        }

        private void BuildBanner(string mode)
        {
            _bannerText = null;
            _installButton = null;
            if (mode == null) { _banner.Visibility = Visibility.Collapsed; return; }
            var u = _plugin.Updater;

            Brush line;
            string glyph, title;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            UIElement extra = null;

            if (mode == "installed")
            {
                line = Theme.B("Ok"); glyph = GCheck;
                // Sans admin, la DLL n'est que préparée : elle s'installe à la fermeture de SimHub
                title = u.Staged
                    ? T($"Version {u.Latest} prête, redémarre SimHub pour l'installer", $"Version {u.Latest} ready, restart SimHub to install it")
                    : T($"Version {u.Latest} installée, redémarre SimHub pour l'activer", $"Version {u.Latest} installed, restart SimHub to activate it");
                buttons.Children.Add(Btn(T("Redémarrer SimHub", "Restart SimHub"), AIRaceEnginePlugin.RestartSimHub, "BtnSuccess", GPower));
                buttons.Children.Add(Btn(T("Plus tard", "Later"), () => { _bannerDismissed = true; }, "BtnGhost"));
            }
            else if (mode == "busy")
            {
                line = Theme.B("AccentLine"); glyph = GSync;
                title = T($"Installation de la version {u.Latest}", $"Installing version {u.Latest}");
                extra = new ProgressBar { IsIndeterminate = true, Height = 2, Margin = new Thickness(0, 8, 0, 0), Foreground = Theme.B("AccentLine"), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            }
            else
            {
                line = Theme.B("AccentLine"); glyph = GDownload;
                title = T($"Version {u.Latest} disponible", $"Version {u.Latest} available");
                buttons.Children.Add(_installButton = Btn(T("Installer", "Install"), StartInstall, "BtnPrimary", GDownload));
                buttons.Children.Add(Btn(T("Notes de version", "Release notes"), OpenOnlineReleaseNotes, "BtnGhost", GLink));
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Glyph(glyph, 16, line));

            var texts = new StackPanel { Margin = new Thickness(12, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
            _bannerText = new TextBlock { Foreground = Theme.B("Text2"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            if (mode == "installed") _bannerText.Text = T("SimHub se ferme puis se relance tout seul", "SimHub closes, then starts again on its own");
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

            /// <param name="tip">texte complet au survol, sinon le détail (il ne tient que sur deux lignes)</param>
            public void Set(string value, string detail, Brush color, string tip = null)
            {
                var key = value + "|" + detail + "|" + color + "|" + tip;
                if (key == _last) return;
                _last = key;
                Value.Text = value;
                Detail.Text = detail ?? "";
                tip = tip ?? detail;
                Detail.ToolTip = string.IsNullOrEmpty(tip) ? null : tip;
                Dot.Fill = color;
            }
        }

        private UIElement BuildStrip()
        {
            _strip = new UniformGrid { Columns = 4 };
            _aiCell = AddCell(T("Intelligence artificielle", "Artificial intelligence"));
            _aiCell.Actions.Children.Add(Btn(T("Configurer", "Set up"), () => Reveal(_aiCard), "BtnGhost", null, small: true));
            _clientCell = AddCell(T("Client vocal", "Voice client"));
            _clientButton = Btn(T("Démarrer", "Start"), () => _plugin.Client.Restart(), "BtnGhost", GRefresh, small: true);
            _clientCell.Actions.Children.Add(_clientButton);
            _clientCell.Actions.Children.Add(Btn(T("Journal", "Log"), () => Open(ClientRunner.LogPath), "BtnGhost", GDoc, small: true));
            _pttCell = AddCell("Push-to-talk");
            _pttCell.Actions.Children.Add(Btn(T("Comment l'associer", "How to map it"), () => Reveal(_pttCard), "BtnGhost", null, small: true));
            _gameCell = AddCell(T("Jeu", "Game"));
            _gameCell.Actions.Children.Add(Btn(T("Télémétrie", "Telemetry"), () => Open(_plugin.Url), "BtnGhost", GLink, small: true));

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

        private bool Speaking => _plugin.ClientConnected && _plugin.ClientState == "speaking";

        /// <summary>
        /// Occupé du clic jusqu'à la fin de la phrase : le client passe à l'état « speaking » puis « ready ».
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
                _listenButton.Content = BtnContent(busy ? T("À la radio", "On the radio") : T("Écouter", "Listen"), busy ? GSync : GPlay);
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

        private void RefreshStrip(string testKey)
        {
            // IA
            var info = ProviderInfo.Get(S.Provider);
            if (S.AiConfigured)
            {
                bool failed = _tester.Ok == false && _testKey == testKey;
                _aiCell.Set(info.Name, S.ModelOrDefault() + (failed ? T(" · dernier test en échec", " · last test failed") : ""), failed ? Theme.B("Warn") : Theme.B("Ok"));
            }
            else _aiCell.Set(T("À configurer", "Not set up"), T("Choisis un fournisseur et colle ta clé", "Choose a provider and paste your key"), Theme.B("Warn"));
            _aiCell.Actions.Visibility = S.AiConfigured ? Visibility.Hidden : Visibility.Visible;

            // Client vocal
            var client = _plugin.Client;
            // Le client envoie un état indépendant de la langue, et un message déjà dans la langue des réglages
            var status = _plugin.ClientStatus ?? "";
            var connected = T("Connecté", "Connected");
            if (_plugin.ClientConnected)
            {
                switch (_plugin.ClientState)
                {
                    // Relancé tout seul après un arrêt : on le montre (sinon « Prêt » le cacherait), jusqu'à un Redémarrer
                    case "ready" when client.LastCrash is string crash: _clientCell.Set(connected, crash, Theme.B("Warn"), crash); break;
                    case "ready": _clientCell.Set(connected, T("Prêt à répondre", "Ready to answer"), Theme.B("Ok")); break;
                    case "speaking": _clientCell.Set(connected, T("À la radio", "On the radio"), Theme.B("Ok")); break;
                    case "warn":
                    case "error": _clientCell.Set(connected, status, Theme.B("Warn")); break;
                    default: _clientCell.Set(T("Chargement", "Loading"), status, Theme.B("AccentLine")); break;
                }
            }
            else if (client.Running && client.SetupStep is string step)
            {
                // La case n'a que deux lignes : l'étape en titre court, la ligne en direct dessous
                // (ex. « Téléchargement de la voix : 40 % »), le texte complet au survol
                var detail = client.SetupDetail;
                _clientCell.Set(T("Installation", "Installing") + StepCount(step), detail ?? step, Theme.B("AccentLine"), detail != null ? step + "\n" + detail : step);
            }
            else if (client.Running) _clientCell.Set(T("Démarrage", "Starting"), T("Le premier lancement peut prendre une minute", "The first launch can take a minute"), Theme.B("AccentLine"));
            // Sans serveur local, le watchdog ne lance pas le client
            else if (_plugin.ServerError != null) _clientCell.Set(T("Arrêté", "Stopped"), T($"Port {AIRaceEnginePlugin.Port} occupé : le client vocal ne peut pas se connecter", $"Port {AIRaceEnginePlugin.Port} in use: the voice client can't connect"), Theme.B("Bad"));
            else if (client.LastError != null) _clientCell.Set(T("Arrêté", "Stopped"), client.LastError, Theme.B("Bad"));
            else _clientCell.Set(T("Arrêté", "Stopped"), S.AutoStartClient ? T("Démarrage automatique dans un instant", "Starting automatically in a moment") : T("Démarrage automatique désactivé", "Automatic start is off"), Theme.B("Muted"));

            // Même libellé dans la bande et dans la carte du client
            bool restart = client.Running || _plugin.ClientConnected;
            var label = restart ? T("Redémarrer", "Restart") : T("Démarrer", "Start");
            if (label != _clientButtonLabel)
            {
                _clientButtonLabel = label;
                _clientButton.Content = BtnContent(label, restart ? GRefresh : GPlay);
                _clientCardButton.Content = BtnContent(label, restart ? GRefresh : GPlay);
            }

            // Push-to-talk
            if (_plugin.PushToTalkDown) _pttCell.Set(T("Appuyé", "Pressed"), S.PttToggle ? T("Parle, puis réappuie pour envoyer", "Speak, then press again to send") : T("Parle, puis relâche pour envoyer", "Speak, then release to send"), Theme.B("Ok"));
            else if (_plugin.LastPtt?.Pulse == true && !S.PttToggle)
                _pttCell.Set(T("Mal associé", "Wrong mapping"), T("En ShortPress : choisis During dans le mapping", "Set to ShortPress: pick During in the mapping"), Theme.B("Warn"));
            else if (S.PttTested) _pttCell.Set(T("Associé", "Mapped"), S.PttToggle ? T("Appuie pour parler, réappuie pour envoyer", "Press to talk, press again to send") : T("Maintiens ton bouton pour parler", "Hold your button to talk"), Theme.B("Ok"));
            else _pttCell.Set(T("Non associé", "Not mapped"), T("Associe un bouton du volant puis appuie dessus", "Map a wheel button, then press it"), Theme.B("Warn"));
            _pttCell.Actions.Visibility = S.PttTested && (_plugin.LastPtt?.Pulse != true || S.PttToggle) ? Visibility.Hidden : Visibility.Visible;

            // Jeu
            if (_plugin.ServerError != null) _gameCell.Set(T("Port occupé", "Port in use"), _plugin.ServerError, Theme.B("Bad"));
            else if (_plugin.GameName != null) _gameCell.Set(_plugin.GameName, _plugin.GameDetail, Theme.B("Ok"));
            else _gameCell.Set(T("En attente", "Waiting"), T("Lance une session, la télémétrie arrive toute seule", "Start a session, the telemetry comes in on its own"), Theme.B("Muted"));
        }

        /// <summary>« 2/2 » tiré de « Première mise en route (2/2) : ... », sinon rien.</summary>
        private static string StepCount(string step)
        {
            int i = step.IndexOf('(');
            return i >= 0 && i + 4 < step.Length && step[i + 2] == '/' && step[i + 4] == ')' ? " " + step.Substring(i + 1, 3) : "";
        }

        // =====================================================================
        // Intelligence artificielle
        // =====================================================================

        private Border BuildAiCard()
        {
            var (card, body) = Section(T("Intelligence artificielle", "Artificial intelligence"), T("Comprend tes questions et répond avec la télémétrie", "Understands your questions and answers with the telemetry"));

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
            // Accroche du fournisseur, et son guide pas à pas (où créer le compte, quoi copier, combien ça coûte)
            var hintRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _providerHint = new TextBlock { FontSize = 12, Foreground = Theme.B("Muted"), VerticalAlignment = VerticalAlignment.Center };
            hintRow.Children.Add(_providerHint);
            _providerHelp = Help("", () => GuideWindows.ShowGuide(S.Provider));
            _providerHelp.Margin = new Thickness(8, 0, 0, 0);
            hintRow.Children.Add(_providerHelp);
            body.Children.Add(hintRow);

            _providerFields = new StackPanel();
            body.Children.Add(_providerFields);
            RebuildProviderFields();

            var test = _testButton = Btn(T("Tester la connexion", "Test connection"), RunTest, "BtnPrimary", GPlay);
            test.HorizontalAlignment = HorizontalAlignment.Left;
            test.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(test);
            _testBox = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
            body.Children.Add(_testBox);
            return card;
        }

        /// <summary>Ce que vérifie un test (fournisseur, compte, clé, adresse, modèle) : son résultat ne vaut que pour ça.</summary>
        private string TestKey() => ModelCatalog.KeyFor(ProviderInfo.Get(S.Provider), S.Current()) + "|" + S.ModelOrDefault();

        private void RunTest()
        {
            if (_tester.Busy) return; // TestAsync ignorerait l'appel : le bouton est grisé en attendant
            _testKey = TestKey();
            _tester.TestAsync(ProviderInfo.Get(S.Provider), S.Current(), S.ModelOrDefault());
        }

        private void RebuildProviderFields()
        {
            _testRev = -1; // masque le résultat du test d'un autre fournisseur
            _providerFields.Children.Clear();
            var info = ProviderInfo.Get(S.Provider);
            var p = S.Current();
            _providerHint.Text = info.Tagline;
            _providerHelp.ToolTip = info.Id == "custom" ? T("Comment installer une IA sur ton PC", "How to install an AI on your PC") : T("Comment configurer ", "How to set up ") + info.Name;

            // La liste des modèles se recharge quand on quitte un champ d'identification
            RoutedEventHandler reload = (s, e) => _catalog.Refresh(info, p);
            if (info.Id == "custom")
            {
                var url = Text(info.BaseUrl(p), v => { p.BaseUrl = v; ChangedSoon(); });
                url.LostFocus += reload;
                _providerFields.Children.Add(Field(T("URL de l'API", "API URL"), url, T("Ollama : http://localhost:11434 · LM Studio : http://localhost:1234 (/v1 ajouté s'il manque)",
                                                                                                "Ollama: http://localhost:11434 · LM Studio: http://localhost:1234 (/v1 added if missing)")));
            }
            if (info.AccountLabel != null)
            {
                var account = Text(p.AccountId, v => { p.AccountId = v; ChangedSoon(); });
                account.LostFocus += reload;
                _providerFields.Children.Add(Field(info.AccountLabel, account, info.AccountHelp.ToString().TrimEnd('.')));
            }

            var keyHelp = new TextBlock { FontSize = 12, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap };
            // Phrase à part : l'aide du fournisseur peut parler d'un « token » (masculin) ou de rien du tout
            keyHelp.Inlines.Add(new Run(info.KeyHelp.ToString().TrimEnd('.') + T(". Ta clé est enregistrée chiffrée sur ce PC", ". Your key is stored encrypted on this PC")));
            if (info.KeyHelpUrl != null)
            {
                var link = new Hyperlink(new Run(T("Obtenir une clé", "Get a key"))) { Foreground = Theme.B("AccentLine") };
                link.Click += (s, e) => Open(info.KeyHelpUrl);
                keyHelp.Inlines.Add(new Run(" · "));
                keyHelp.Inlines.Add(link);
            }
            var secret = Secret(p.ApiKey, v => { p.ApiKey = v; ChangedSoon(); });
            secret.LostFocus += reload;
            _providerFields.Children.Add(Field(info.NeedsKey ? T("Clé API", "API key") : T("Clé (optionnelle)", "Key (optional)"), secret, keyHelp));

            _modelCombo = new ComboBox { IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch, FontFamily = Theme.Mono };
            _modelCombo.DropDownOpened += (s, e) => _catalog.Refresh(info, p);
            _modelCombo.DropDownClosed += (s, e) => ShowModelStart();
            _modelCombo.Loaded += (s, e) => ShowModelStart();
            _modelCombo.LostFocus += (s, e) => FlushPendingSave();
            _catalogRev = -1; // remplit la liste au prochain rafraîchissement
            FillModels();
            _modelCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) =>
            {
                if (_fillingModels || p.Model == _modelCombo.Text) return;
                p.Model = _modelCombo.Text;
                ChangedSoon();
            }));
            _modelHelp = new TextBlock { FontSize = 12, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap };
            _providerFields.Children.Add(Field(T("Modèle", "Model"), _modelCombo, _modelHelp));
            _catalog.Refresh(info, p);
        }

        /// <summary>Liste « Modèle » : les modèles récupérés chez le fournisseur, sinon les conseillés.</summary>
        private void FillModels()
        {
            var info = ProviderInfo.Get(S.Provider);
            var p = S.Current();
            bool fresh = _catalog.Key == ModelCatalog.KeyFor(info, p);
            var models = fresh && _catalog.Models.Length > 0 ? _catalog.Models : info.Models;
            if (_modelCombo.Items.Cast<string>().SequenceEqual(models)) return; // même liste : rien à refaire

            // En pleine frappe : on garde le texte tapé et le curseur
            var box = _modelCombo.Template?.FindName("PART_EditableTextBox", _modelCombo) as TextBox;
            bool typing = box != null && box.IsKeyboardFocused;
            int start = typing ? box.SelectionStart : 0, length = typing ? box.SelectionLength : 0;
            var text = typing ? _modelCombo.Text : S.ModelOrDefault();

            // Items.Clear() vide le texte un instant : ni enregistrement ni config envoyée pour ça
            _fillingModels = true;
            try
            {
                _modelCombo.Items.Clear();
                foreach (var m in models) _modelCombo.Items.Add(m);
                _modelCombo.Text = text;
            }
            finally { _fillingModels = false; }

            if (typing) box.Select(Math.Min(start, text.Length), Math.Min(length, Math.Max(0, text.Length - start)));
            else ShowModelStart();
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
                _catalog.Busy ? T(" · récupération de la liste des modèles", " · fetching the model list")
                : !fresh ? ""
                : _catalog.Error != null ? " · " + _catalog.Error
                : _catalog.Models.Length > 0 ? T($" · {_catalog.Models.Length} disponibles", $" · {_catalog.Models.Length} available")
                : "";
            var help = T("Un petit modèle rapide suffit", "A small, fast model is enough") + extra;
            if (_modelHelp.Text != help) _modelHelp.Text = help;

            if (_catalog.Revision != _catalogRev && !_catalog.Busy)
            {
                if (_modelCombo.IsDropDownOpen) return; // on attend que la liste soit refermée
                _catalogRev = _catalog.Revision;
                FillModels();
            }
        }

        private void RefreshTest(string testKey)
        {
            _testButton.IsEnabled = !_tester.Busy;
            // Le résultat ne vaut que pour la config testée : il disparaît dès qu'un champ change
            bool shown = _tester.Revision > 0 && _testKey == testKey;
            if (_tester.Revision == _testRev && shown == _testShown) return;
            _testRev = _tester.Revision;
            _testShown = shown;
            if (!shown) { _testBox.Visibility = Visibility.Collapsed; return; }

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
            var (card, body) = Section(T("Langue et voix", "Language and voice"), T("La reconnaissance vocale tourne sur ton PC", "Speech recognition runs on your PC"));

            // La langue de l'ingé est aussi celle de la page : on la reconstruit
            body.Children.Add(Field(T("Langue", "Language"), Segments("lang", new[] { ("fr", "Français"), ("en", "English") }, S.Language, v =>
            {
                if (S.Language == v) return;
                S.Language = v;
                Changed();
                Rebuild();
            })));
            // Français seulement : la page est reconstruite quand la langue change
            if (S.Language == "fr")
                body.Children.Add(ToggleRow(T("Nombres à la suisse", "Swiss numbers"),
                    T("Septante, huitante, nonante au lieu de soixante-dix, quatre-vingts, quatre-vingt-dix",
                      "Septante, huitante, nonante instead of soixante-dix, quatre-vingts, quatre-vingt-dix"),
                    S.SwissNumbers, v => { S.SwissNumbers = v; Changed(); }));

            _engineHint = new TextBlock { FontSize = 12, Foreground = Theme.B("Muted"), TextWrapping = TextWrapping.Wrap };
            var engines = Segments("engine", new[] { ("neural", T("En ligne", "Online")), ("kokoro", T("Hors ligne", "Offline")), ("elevenlabs", "ElevenLabs") }, S.TtsEngine, v =>
            {
                S.TtsEngine = v;
                Changed();
                RebuildVoiceFields();
            });
            var engineHelp = new Grid();
            engineHelp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            engineHelp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            engineHelp.Children.Add(_engineHint);
            _elevenHelp = Help(T("Comment configurer ElevenLabs", "How to set up ElevenLabs"), () => GuideWindows.ShowGuide("elevenlabs"));
            _elevenHelp.Margin = new Thickness(8, 0, 0, 0);
            _elevenHelp.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(_elevenHelp, 1);
            engineHelp.Children.Add(_elevenHelp);
            body.Children.Add(Field(T("Moteur", "Engine"), engines, engineHelp));

            _voiceFields = new StackPanel();
            body.Children.Add(_voiceFields);
            RebuildVoiceFields();

            // Volume de la voix, quel que soit le moteur
            body.Children.Add(Field(T("Volume", "Volume"), Volume(T("Volume de la voix", "Voice volume"), S.VoiceVolume, 50, 200, v => S.VoiceVolume = v),
                T("Au-delà de 100 %, les crêtes sont arrondies pour ne pas saturer", "Above 100%, peaks are rounded off so it doesn't distort")));

            body.Children.Add(Field(T("Reconnaissance", "Recognition"), Combo(new[]
            {
                ("small", T("Précise, environ 1,3 s", "Accurate, about 1.3 s")),
                ("base", T("Rapide, environ 0,4 s", "Fast, about 0.4 s")),
            }, S.SttModel, v => { S.SttModel = v; Changed(); })));
            body.Children.Add(Divider());
            FrameworkElement track = null, noise = null;
            body.Children.Add(ToggleRow(T("Effet radio", "Radio effect"), T("Filtre radio et léger grésillement", "Radio filter and light crackle"), S.RadioEffect, v =>
            {
                S.RadioEffect = v;
                track.IsEnabled = v;
                noise.IsEnabled = v && S.TrackNoise;
                Changed();
            }));
            track = ToggleRow(T("Bruits de piste", "Track noise"), T("Des F1 passent parfois derrière l'ingé, jamais deux fois pareil. Sa voix reste devant",
                                                                  "F1 cars sometimes pass behind the engineer, never the same way twice. The voice stays in front"), S.TrackNoise, v =>
            {
                S.TrackNoise = v;
                noise.IsEnabled = v && S.RadioEffect;
                Changed();
            });
            track.IsEnabled = S.RadioEffect;
            body.Children.Add(track);
            // Le fond seulement : la voix reste au volume réglé plus haut
            noise = Field(T("Volume du fond", "Noise level"), Volume(T("Volume du fond", "Noise level"), S.NoiseVolume, 0, 200, v => S.NoiseVolume = v));
            noise.IsEnabled = S.RadioEffect && S.TrackNoise;
            body.Children.Add(noise);
            return card;
        }

        /// <summary>
        /// Curseur de volume en %, avec sa valeur à droite. La valeur change en mémoire tout de suite, mais n'est
        /// enregistrée et envoyée au client qu'au lâcher (ou après une pause au clavier) : pas un envoi par pixel.
        /// </summary>
        private FrameworkElement Volume(string name, int value, int min, int max, Action<int> onChange)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            var slider = new Slider
            {
                Style = Theme.S("Slider"), Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)),
                SmallChange = 5, LargeChange = 10, TickFrequency = 5, IsSnapToTickEnabled = true,
                ToolTip = T("Double-clic : 100 %", "Double-click: 100%"),
            };
            // Field n'étiquette que les contrôles : ici une grille, on nomme donc le curseur lui-même
            AutomationProperties.SetName(slider, name);
            var label = new TextBlock
            {
                FontFamily = Theme.Mono, FontSize = 12, Foreground = Theme.B("Text2"),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            };
            void Show(int v) => label.Text = v + " %";
            Show((int)slider.Value);
            slider.ValueChanged += (s, e) =>
            {
                int v = (int)Math.Round(e.NewValue);
                Show(v);
                onChange(v);
                ChangedSoon();
            };
            // Au lâcher de la souris, tout de suite : le prochain message de l'ingé a le nouveau volume
            slider.LostMouseCapture += (s, e) => FlushPendingSave();
            // Double-clic : retour à 100 %
            slider.MouseDoubleClick += (s, e) => slider.Value = Math.Max(min, Math.Min(max, 100));
            g.Children.Add(slider);
            Grid.SetColumn(label, 1);
            g.Children.Add(label);
            return g;
        }

        private void RebuildVoiceFields()
        {
            _voiceFields.Children.Clear();
            _engineHint.Text =
                S.TtsEngine == "neural" ? T("Voix neuronales Microsoft, gratuites et très naturelles. Sans internet, la voix hors ligne prend le relais",
                                            "Microsoft neural voices, free and very natural. Without internet, the offline voice takes over")
                : S.TtsEngine == "kokoro" ? T("Kokoro, sur ton PC et sans internet. Moins naturelle en français", "Kokoro runs on your PC, no internet needed. British and American voices")
                : T("La plus réaliste, payante au-delà de l'offre gratuite. Demande un compte, une clé et une voix",
                    "The most realistic, paid beyond the free plan. Needs an account, a key and a voice");
            _elevenHelp.Visibility = S.TtsEngine == "elevenlabs" ? Visibility.Visible : Visibility.Collapsed;

            if (S.TtsEngine == "elevenlabs")
            {
                _voiceFields.Children.Add(Field(T("Clé API", "API key"), Secret(S.ElevenLabsKey, v => { S.ElevenLabsKey = v; ChangedSoon(); }), T("Enregistrée chiffrée sur ce PC", "Stored encrypted on this PC")));
                _voiceFields.Children.Add(Field("Voice ID", Text(S.ElevenLabsVoiceId, v => { S.ElevenLabsVoiceId = v; ChangedSoon(); }), T("Copié depuis ta bibliothèque ElevenLabs", "Copied from your ElevenLabs library")));
                return;
            }

            bool neural = S.TtsEngine == "neural";
            var list = (neural ? VoiceInfo.Neural : VoiceInfo.Kokoro).Where(v => v.Language == S.Language).Select(v => (v.Id, v.Label)).ToArray();
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var voice = Combo(list, neural ? S.NeuralVoice : S.Voice, v =>
            {
                if (neural) { if (Fr) S.NeuralVoiceFr = v; else S.NeuralVoiceEn = v; }
                else { if (Fr) S.VoiceFr = v; else S.VoiceEn = v; }
                Changed();
            });
            AutomationProperties.SetName(voice, T("Voix", "Voice")); // dans une ligne : Field ne peut pas l'étiqueter
            row.Children.Add(voice);
            var listen = _listenButton = Btn(T("Écouter", "Listen"), () => SayTest(T(
                "Box box, ce tour-ci. Tu ressors en P4, la voiture derrière est à deux secondes.",
                "Box box, this lap. You'll rejoin in P4, the car behind is two seconds back.")), "Btn", GPlay);
            listen.Margin = new Thickness(8, 0, 0, 0);
            _sayBusyLast = false; // le nouveau bouton reprend l'état au prochain rafraîchissement
            Grid.SetColumn(listen, 1);
            row.Children.Add(listen);
            _voiceFields.Children.Add(Field(T("Voix", "Voice"), row));
            _voiceFields.Children.Add(Field(T("Débit", "Speed"), Combo(new[] { ("1.0", "Normal"), ("1.1", T("Un peu rapide", "A bit fast")), ("1.25", T("Rapide", "Fast")) },
                S.VoiceSpeed.ToString("0.0#", CultureInfo.InvariantCulture),
                v => { S.VoiceSpeed = double.Parse(v, CultureInfo.InvariantCulture); Changed(); })));
        }

        // =====================================================================
        // Annonces automatiques
        // =====================================================================

        private Border BuildCalloutsCard()
        {
            var (card, body) = Section(T("Annonces automatiques", "Automatic callouts"), T("Sans IA ni internet, donc instantanées et gratuites", "No AI and no internet, so instant and free"));
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

            rows.Add(ToggleRow(T("Temps au tour", "Lap time"), lap, S.CallLapTime, v => { S.CallLapTime = v; Changed(); }, radio: true));
            rows.Add(Divider());
            rows.Add(ToggleRow(T("Meilleur tour", "Personal best"), fr ? "Meilleur tour !" : "Personal best!", S.CallPersonalBest, v => { S.CallPersonalBest = v; Changed(); }, radio: true));
            rows.Add(Divider());
            rows.Add(ToggleRow(T("Tour invalidé, dès que le jeu l'invalide", "Lap invalidated, as soon as the game says so"),
                fr ? "Tour invalidé." : "Lap invalidated.", S.CallInvalidLap, v => { S.CallInvalidLap = v; Changed(); }, radio: true));
            rows.Add(Divider());
            rows.Add(ToggleRow(T("Secteurs, hors course : écart au meilleur tour", "Sectors, outside races: gap to your best lap"),
                Callouts.SpokenSplit(1, -0.2, fr) + " " + Callouts.SpokenSplit(2, 0.1, fr), S.CallSplits, v => { S.CallSplits = v; Changed(); }, radio: true));
            rows.Add(Divider());
            rows.Add(ToggleRow(T("Écarts, en course", "Gaps, in races"), gaps, S.CallGaps, v => { S.CallGaps = v; Changed(); }, radio: true));
            rows.Add(Divider());
            rows.Add(ToggleRow(T("Places gagnées ou perdues", "Places gained or lost"), Callouts.SpokenPosition(5, 1, fr), S.CallPosition, v => { S.CallPosition = v; Changed(); }, radio: true));
            rows.Add(Divider());
            rows.Add(ToggleRow(T("Dégâts, crevaison, roue perdue", "Damage, puncture, wheel lost"),
                fr ? "Crevaison avant gauche ! Suspension avant gauche touchée." : "Puncture, front left! Front left suspension damaged.",
                S.CallDamage, v => { S.CallDamage = v; Changed(); }, radio: true));
            rows.Add(Divider());
            rows.Add(ToggleRow(T("Carburant", "Fuel"), fr ? "Carburant : environ 3 tours restants." : "Fuel: about 3 laps left.", S.CallFuel, v => { S.CallFuel = v; Changed(); }, radio: true));
            var left = T("tours restants", "laps left");
            rows.Add(Field(T("Prévenir à", "Warn at"), Combo(new[] { ("2", "2 " + left), ("3", "3 " + left), ("4", "4 " + left), ("5", "5 " + left) },
                S.FuelWarningLaps.ToString(), v => { S.FuelWarningLaps = int.Parse(v); Changed(); })));
        }

        // =====================================================================
        // Push-to-talk
        // =====================================================================

        private Border BuildPttCard()
        {
            var (card, body) = Section("Push-to-talk", T("Un bouton du volant pour parler à l'ingé", "A wheel button to talk to the engineer"));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });

            var steps = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            var texts = new[]
            {
                T("Ouvre Controls and events dans SimHub", "Open Controls and events in SimHub"),
                T($"Ajoute un mapping avec l'action AIRaceEnginePlugin.{AIRaceEnginePlugin.PushToTalkAction}, en mode During", $"Add a mapping with the AIRaceEnginePlugin.{AIRaceEnginePlugin.PushToTalkAction} action, set to During"),
                T("Appuie sur le bouton du volant à associer", "Press the wheel button to map"),
                T("Teste ici, sans jeu : dis une phrase à la radio, la page montre ce que l'ingé a entendu", "Test it here, no game needed: say something on the radio, the page shows what the engineer heard"),
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
            body.Children.Add(ToggleRow(T("Appuyer pour parler, réappuyer pour envoyer", "Press to talk, press again to send"),
                T("Au lieu de maintenir le bouton. Marche avec tous les types d'appui de SimHub ; la radio se referme seule après 30 secondes",
                  "Instead of holding the button. Works with every SimHub press type; the radio closes on its own after 30 seconds"),
                S.PttToggle, v => { S.PttToggle = v; Changed(); }));
            // Résultat du dernier essai : mapping, micro utilisé, niveau, phrase comprise
            _pttResult = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
            body.Children.Add(_pttResult);
            return card;
        }

        private void RefreshPtt()
        {
            bool down = _plugin.PushToTalkDown;
            var r = _plugin.LastPtt;
            var age = r == null ? 0 : (DateTime.UtcNow - r.At).TotalSeconds;
            // Un appui en ShortPress ne dure pas assez pour allumer la lampe : elle s'allume en orange un instant
            bool pulse = !down && r != null && r.Pulse && !S.PttToggle && age < 1.5;
            bool waiting = r != null && r.Kind == null && age < 3;
            var key = $"{down}|{pulse}|{waiting}|{r?.At.Ticks}|{r?.Kind}|{r?.Text}|{_plugin.ClientConnected}|{S.PttToggle}";
            if (_pttLast == key) return;
            _pttLast = key;

            var lamp = down ? Theme.B("Ok") : pulse ? Theme.B("Warn") : null;
            _pttLamp.Fill = lamp ?? Theme.B("Raised");
            _pttLamp.Stroke = lamp ?? Theme.B("Line");
            _pttLampText.Text = down ? (S.PttToggle ? T("Je t'écoute, réappuie pour envoyer", "Listening, press again to send") : T("Je t'écoute", "Listening"))
                : pulse ? T("Relâché aussitôt", "Released at once") : T("Appuie pour tester", "Press to test");
            _pttLampText.Foreground = lamp ?? Theme.B("Muted");

            _pttResult.Inlines.Clear();
            var (main, detail, color) = down ? (null, null, null) : PttResult(r, waiting);
            _pttResult.Visibility = main == null ? Visibility.Collapsed : Visibility.Visible;
            if (main == null) return;
            _pttResult.Inlines.Add(new Run(main) { Foreground = color });
            if (detail != null) _pttResult.Inlines.Add(new Run("\n" + detail) { Foreground = Theme.B("Muted"), FontSize = 12 });
        }

        /// <summary>Ce que le dernier appui a donné, du mapping jusqu'à la phrase comprise.</summary>
        private (string main, string detail, Brush color) PttResult(PttReport r, bool waiting)
        {
            if (r == null) return (null, null, null);
            var c = L10n.Language == "fr" ? CultureInfo.GetCultureInfo("fr-FR") : CultureInfo.InvariantCulture;
            if (r.Pulse && !S.PttToggle)
                return (T("Le bouton envoie l'appui et le relâchement d'un coup : le mapping est en ShortPress.",
                          "The button sends the press and the release at once: the mapping is set to ShortPress."),
                        T("Dans Controls and events, clique sur ShortPress à gauche du mapping et choisis During. Ou active « Appuyer pour parler, réappuyer pour envoyer » ci-dessus.",
                          "In Controls and events, click ShortPress on the left of the mapping and pick During. Or turn on « Press to talk, press again to send » above."), Theme.B("Warn"));
            var held = (r.PressMs / 1000.0).ToString("0.0", c) + " s";
            var press = r.Toggle ? T($"Radio ouverte {held}", $"Radio open for {held}") : T($"Appui de {held} reçu", $"{held} press received");
            if (r.Kind == null)
                return waiting ? (press, null, Theme.B("Muted"))
                    : !_plugin.ClientConnected ? (press + T(", mais le client vocal n'est pas lancé", ", but the voice client isn't running"), null, Theme.B("Warn"))
                    : (press + T(", mais rien d'enregistré", ", but nothing was recorded"),
                       T("L'état du client vocal, en haut de la page, dit pourquoi (micro introuvable ?)", "The voice client status at the top of the page says why (microphone not found?)"), Theme.B("Warn"));

            var mic = r.Device != null ? T($"micro « {r.Device} »", $"microphone \"{r.Device}\"") : T("micro par défaut", "default microphone");
            var level = r.Peak >= 0 ? T($", niveau {r.Peak} %", $", level {r.Peak}%") : "";
            var detail = r.Seconds.ToString("0.0", c) + " s, " + mic + level;
            var windowsMic = T("Choisis le bon micro dans Windows : Paramètres → Système → Son → Entrée",
                               "Pick the right microphone in Windows: Settings → System → Sound → Input");
            if (r.Kind == "short")
                return (r.Toggle ? T("Trop court : réappuie seulement quand tu as fini de parler", "Too short: press again only once you've finished talking")
                                 : T("Trop court : garde le bouton enfoncé pendant que tu parles", "Too short: hold the button while you talk"), detail, Theme.B("Warn"));
            if (r.Peak == 0)
                return (T($"Aucun son reçu du {mic}", $"No sound from the {mic}"), windowsMic, Theme.B("Bad"));
            if (r.Kind == "rec")
                return (T("Enregistré, transcription…", "Recorded, transcribing…"), detail, Theme.B("Muted"));
            bool faint = r.Peak >= 0 && r.Peak < 5;
            if (string.IsNullOrEmpty(r.Text))
                return (faint ? T("Rien compris : le micro est presque muet", "Didn't catch anything: the microphone is almost silent")
                              : T("Rien compris : parle plus fort ou plus près du micro", "Didn't catch anything: speak louder or closer to the microphone"),
                        detail + (faint ? "\n" + windowsMic : ""), Theme.B("Warn"));
            return (T($"Entendu : « {r.Text} »", $"Heard: \"{r.Text}\""), detail, Theme.B("Ok"));
        }

        // =====================================================================
        // Client vocal et mises à jour
        // =====================================================================

        private Border BuildClientCard()
        {
            var (card, body) = Section(T("Client vocal et mises à jour", "Voice client and updates"), null);

            body.Children.Add(ToggleRow(T("Démarrer avec SimHub", "Start with SimHub"), T("En arrière-plan, relancé tout seul en cas de souci", "Runs in the background, restarts on its own if needed"), S.AutoStartClient, v => { S.AutoStartClient = v; Changed(); }));
            var tools = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            // Libellé (Démarrer ou Redémarrer) tenu à jour avec celui de la bande
            tools.Children.Add(_clientCardButton = Btn(T("Redémarrer", "Restart"), () => _plugin.Client.Restart(), "Btn", GRefresh));
            tools.Children.Add(Btn(T("Journal", "Log"), () => Open(ClientRunner.LogPath), "Btn", GDoc));
            tools.Children.Add(Btn(T("Dossier", "Folder"), () => Open(EngineerSettings.DataDir), "Btn", GFolder));
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
            // Un seul des trois est visible (RefreshCardUpdate)
            _cardInstallLabel = null;
            ub.Children.Add(_cardInstallButton = Btn(T("Installer", "Install"), StartInstall, "BtnPrimary", GDownload));
            ub.Children.Add(_cardRestartButton = Btn(T("Redémarrer SimHub", "Restart SimHub"), AIRaceEnginePlugin.RestartSimHub, "BtnSuccess", GPower));
            ub.Children.Add(_cardCheckButton = Btn(T("Rechercher une mise à jour", "Check for updates"), () => u.CheckAsync(), "Btn", GSync));
            _cardInstallButton.Visibility = _cardRestartButton.Visibility = Visibility.Collapsed;
            ub.Children.Add(Btn(T("Notes de version", "Release notes"), OpenReleaseNotes, "BtnGhost", GDoc));
            body.Children.Add(ub);
            return card;
        }

        /// <summary>Notes des versions installées, dans SimHub et sans internet : le changelog est intégré à la DLL.</summary>
        private static void OpenReleaseNotes() => GuideWindows.ShowReleaseNotes();

        /// <summary>Bannière d'une mise à jour pas encore installée : ses notes ne sont que sur GitHub.</summary>
        private static void OpenOnlineReleaseNotes() => Open($"https://github.com/{Updater.Repo}/blob/main/" + T("CHANGELOG.fr.md", "CHANGELOG.md"));

        // =====================================================================
        // Rafraîchissement
        // =====================================================================

        private void SafeRefresh()
        {
            try
            {
                var testKey = TestKey();
                RefreshBanner();
                RefreshStrip(testKey);
                RefreshPtt();
                RefreshTest(testKey);
                RefreshModels();
                RefreshSayButtons();
                var state = (_plugin.Updater.State ?? "").TrimEnd('.');
                _updateState.Text = state;
                _updateState.Visibility = state.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex)
            {
                // Une erreur d'affichage ne doit jamais faire tomber SimHub. Une erreur passagère (un état lu pendant
                // qu'un autre thread le change) ne fige pas la page : le rafraîchissement s'arrête à la 5e (journal court)
                SimHub.Logging.Current.Error("[AI Race Engineer] page : " + ex);
                if (++_refreshErrors >= MaxRefreshErrors) _timer.Stop();
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
            var name = new TextBlock { Text = label, FontSize = 13, Foreground = Theme.B("Text2"), Margin = new Thickness(0, 6, 12, 0), TextWrapping = TextWrapping.Wrap };
            g.Children.Add(name);
            // Lecteurs d'écran : le champ porte le nom de son libellé
            if (control is Control) AutomationProperties.SetLabeledBy(control, name);

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
            // L'interrupteur n'a pas de texte : les lecteurs d'écran lisent le titre de la ligne
            AutomationProperties.SetName(toggle, title);
            if (detail != null) AutomationProperties.SetHelpText(toggle, detail);
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
            b.Click += (s, e) => SafeClick(onClick);
            return b;
        }

        /// <summary>Petit bouton rond « ? » qui ouvre un guide.</summary>
        private static Button Help(string tooltip, Action onClick)
        {
            var b = new Button { Style = Theme.S("BtnHelp"), Content = "?", ToolTip = tooltip, VerticalAlignment = VerticalAlignment.Center };
            b.Click += (s, e) => SafeClick(onClick);
            return b;
        }

        /// <summary>Un clic en erreur ne doit jamais remonter jusqu'à SimHub : l'erreur va dans son journal.</summary>
        private static void SafeClick(Action onClick)
        {
            try { onClick(); }
            catch (Exception ex) { SimHub.Logging.Current.Error("[AI Race Engineer] bouton : " + ex); }
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

        /// <summary>Champ texte : onChange à chaque frappe (avec ChangedSoon), enregistré en quittant le champ.</summary>
        private TextBox Text(string value, Action<string> onChange)
        {
            var t = new TextBox { Text = value ?? "", HorizontalAlignment = HorizontalAlignment.Stretch };
            t.TextChanged += (s, e) => onChange(t.Text);
            t.LostFocus += (s, e) => FlushPendingSave();
            return t;
        }

        private PasswordBox Secret(string value, Action<string> onChange)
        {
            var p = new PasswordBox { Password = value ?? "", HorizontalAlignment = HorizontalAlignment.Stretch };
            p.PasswordChanged += (s, e) => onChange(p.Password);
            p.LostFocus += (s, e) => FlushPendingSave();
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
