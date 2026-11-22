using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;

namespace AIRaceEngineer
{
    /// <summary>Guide pas à pas d'un fournisseur (bouton ? à côté de chaque IA).</summary>
    public sealed class Guide
    {
        public string Title, Summary, Cost, Note;
        public string[] Steps;

        // Mise en forme légère dans les textes : [lien](https://...), `code`, « libellé d'un bouton »
        public static readonly Dictionary<string, Guide> All = new Dictionary<string, Guide>
        {
            ["infomaniak"] = new Guide
            {
                Title = "Infomaniak",
                Summary = "Hébergeur suisse : l'IA tourne dans leurs centres de données en Suisse.",
                Cost = "1 million de crédits offerts pour tester pendant un mois, puis paiement à l'usage, facturé chaque mois. " +
                       "Une question à l'ingé consomme quelques milliers de tokens : le million offert suffit pour des centaines de questions. " +
                       "Une carte bancaire est demandée à la commande, pour vérifier ton identité.",
                Steps = new[]
                {
                    "Commande AI Tools sur [shop.infomaniak.com](https://shop.infomaniak.com/order2/ai-tools) : connecte-toi ou crée un compte Infomaniak, puis valide la commande (gratuite pour démarrer).",
                    "Ouvre AI Tools dans le [Manager](https://manager.infomaniak.com/v3/ng/products/cloud/ai-tools) et copie le Product ID de ton produit (un nombre). Colle-le dans le champ Product ID.",
                    "Sur la [page des tokens API](https://manager.infomaniak.com/v3/ng/profile/user/token/list), clique « Créer un token », choisis l'application AI Tools (scope `ai-tools`), puis confirme avec ton mot de passe.",
                    "Copie le token tout de suite (il ne sera plus affiché) et colle-le dans Clé API.",
                    "Garde le modèle conseillé et clique « Tester la connexion ».",
                },
            },
            ["cloudflare"] = new Guide
            {
                Title = "Cloudflare",
                Summary = "Workers AI, l'IA de Cloudflare. Rien à payer tant que tu restes dans le quota gratuit.",
                Cost = "Gratuit : 10 000 neurons (l'unité de Cloudflare) par jour, remis à zéro chaque jour à 00:00 UTC. Avec le modèle conseillé, " +
                       "ça fait environ une centaine de questions par jour (davantage avec le modèle 8B). Au-delà, l'ingé ne répond plus " +
                       "aux questions jusqu'au lendemain ; les annonces automatiques continuent.",
                Steps = new[]
                {
                    "Crée un compte gratuit sur [dash.cloudflare.com](https://dash.cloudflare.com/sign-up).",
                    "Ouvre [Workers AI](https://dash.cloudflare.com/?to=/:account/ai/workers-ai) et clique « Use REST API ».",
                    "Clique « Create a Workers AI API Token », vérifie les informations pré-remplies, puis « Create API Token » et « Copy API Token ». Colle le token dans Clé API.",
                    "Sur la même page, sous « Get Account ID », copie l'Account ID et colle-le dans le champ Account ID.",
                    "Clique « Tester la connexion ».",
                },
            },
            ["openai"] = new Guide
            {
                Title = "OpenAI",
                Summary = "Les modèles GPT, payants à l'usage, sans abonnement.",
                Cost = "Crédits prépayés : 5 $ minimum, valables un an. Avec gpt-4.1-mini, une question coûte moins d'un dixième de centime. " +
                       "Un abonnement ChatGPT ne compte pas : l'API se paie à part.",
                Steps = new[]
                {
                    "Crée un compte sur [platform.openai.com](https://platform.openai.com/signup).",
                    "Dans [Billing](https://platform.openai.com/settings/organization/billing/overview), ajoute un moyen de paiement et achète des crédits.",
                    "Sur la page [API keys](https://platform.openai.com/api-keys), clique « Create new secret key », copie la clé (elle ne s'affiche qu'une fois) et colle-la dans Clé API.",
                    "Garde `gpt-4.1-mini` (rapide et bon marché) et clique « Tester la connexion ».",
                },
            },
            ["claude"] = new Guide
            {
                Title = "Claude",
                Summary = "Les modèles d'Anthropic, très bons en français. Payants à l'usage, sans abonnement.",
                Cost = "Crédits prépayés, achetés dans la Console Claude (le minimum s'affiche au moment de l'achat). " +
                       "Un abonnement Claude.ai ne compte pas : l'API se paie à part.",
                Steps = new[]
                {
                    "Crée un compte sur la [Console Claude](https://platform.claude.com).",
                    "Dans Settings → Billing, clique « Buy credits ».",
                    "Dans [API keys](https://platform.claude.com/settings/keys), clique « Create key », donne-lui un nom, puis copie-la (elle commence par `sk-ant-` et ne s'affiche qu'une fois). Colle-la dans Clé API.",
                    "Garde `claude-haiku-5-5` (le plus rapide) et clique « Tester la connexion ».",
                },
            },
            ["custom"] = new Guide
            {
                Title = "IA sur ton PC (Ollama)",
                Summary = "Gratuite et sans compte, mais elle utilise ta carte graphique pendant que tu joues.",
                Cost = "Gratuit. Prends un petit modèle (3 à 4 milliards de paramètres) pour garder tes FPS : `qwen3:4b` ou `llama3.2:3b`.",
                Steps = new[]
                {
                    "Installe [Ollama](https://ollama.com/download).",
                    "Dans un terminal, télécharge un modèle : `ollama pull qwen3:4b`.",
                    "Laisse l'URL `http://localhost:11434/v1`, sans clé, mets le même nom de modèle (`qwen3:4b`) et clique « Tester la connexion ».",
                },
                Note = "LM Studio marche aussi : démarre son serveur local et mets l'URL `http://localhost:1234/v1`.",
            },
            ["elevenlabs"] = new Guide
            {
                Title = "ElevenLabs",
                Summary = "La voix la plus réaliste, de loin. Ça demande un peu de travail : un compte, une clé API et le choix d'une voix.",
                Cost = "Le plan gratuit (10 000 crédits par mois, usage personnel) suffit pour essayer, pas pour rouler régulièrement : " +
                       "le plan Starter coûte 6 $ par mois. Le plugin utilise le modèle Flash, le moins cher en crédits.",
                Steps = new[]
                {
                    "Crée un compte sur [elevenlabs.io](https://elevenlabs.io/app/sign-up).",
                    "Crée une clé sur la page [API keys](https://elevenlabs.io/app/settings/api-keys). Tu peux la limiter à la synthèse vocale (Text to Speech) et lui fixer un plafond de crédits. Copie-la et colle-la dans Clé API.",
                    "Choisis une voix dans la [bibliothèque](https://elevenlabs.io/app/voice-library), ajoute-la à tes voix et copie son Voice ID. Colle-le dans le champ Voice ID (par défaut : George, une voix britannique).",
                    "Clique « Radio check » en haut de la page pour l'entendre.",
                },
                Note = "Sur le plan gratuit, certaines voix de la bibliothèque peuvent être refusées par l'API. Dans ce cas, garde George ou passe au plan Starter.",
            },
        };
    }

    /// <summary>Fenêtres de la page : bienvenue (au premier démarrage, puis bouton ?) et guides des fournisseurs.</summary>
    public static class GuideWindows
    {
        public static void ShowGuide(string id)
        {
            if (!Guide.All.TryGetValue(id, out var g)) return;
            Open("Configurer " + g.Title, w => BuildGuide(g, w), activate: true);
        }

        /// <param name="activate">false au démarrage de SimHub : la fenêtre ne doit pas voler le focus (un jeu en plein écran)</param>
        public static void ShowWelcome(bool activate) => Open("Bienvenue", BuildWelcome, activate);
        public static void ShowWelcome() => ShowWelcome(true);

        public static FrameworkElement BuildGuide(string id, Action close) => BuildGuide(Guide.All[id], close);

        private static FrameworkElement BuildGuide(Guide g, Action close)
        {
            var body = new StackPanel();
            body.Children.Add(Para(g.Summary, "Text2", 13, 0, 16));
            body.Children.Add(Heading("Ce que ça coûte"));
            body.Children.Add(Para(g.Cost, "Text", 13, 0, 18));
            body.Children.Add(Heading("Étapes"));
            for (int i = 0; i < g.Steps.Length; i++) body.Children.Add(Step(i + 1, g.Steps[i]));
            if (g.Note != null) body.Children.Add(Para(g.Note, "Muted", 12, 6, 0));
            return Root(g.Title, body, "Fermer", close);
        }

        public static FrameworkElement BuildWelcome(Action close)
        {
            var body = new StackPanel();
            body.Children.Add(Para("Ton ingé de course à la radio. Il annonce tout seul tes temps, tes écarts, tes places et le carburant, " +
                                   "et répond à tes questions quand tu maintiens un bouton du volant.", "Text2", 13, 0, 8));
            body.Children.Add(Para("Au premier démarrage, le plugin installe la voix (quelques minutes) : l'avancement s'affiche dans la case Client vocal.", "Muted", 12, 0, 18));

            body.Children.Add(Heading("1. Choisis une IA"));
            body.Children.Add(Para("C'est elle qui comprend tes questions et y répond avec la télémétrie. Les annonces automatiques n'en ont pas besoin : " +
                                   "elles marchent déjà, sans internet. Plusieurs IA au choix, chacune avec son guide :", "Text", 13, 0, 8));
            body.Children.Add(Choice("Infomaniak", "suisse, 1 million de crédits offerts pour commencer. Le choix par défaut.", "infomaniak"));
            body.Children.Add(Choice("Cloudflare", "un quota gratuit chaque jour, sans rien payer.", "cloudflare"));
            body.Children.Add(Choice("OpenAI", "payant à l'usage, quelques centimes par course.", "openai"));
            body.Children.Add(Choice("Claude", "payant à l'usage, très bon en français.", "claude"));
            body.Children.Add(Choice("Autre", "une IA sur ton PC (Ollama). Gratuite, mais elle prend de la carte graphique au jeu.", "custom"));

            body.Children.Add(Heading("2. La voix", 18));
            body.Children.Add(Choice("En ligne", "par défaut. Voix Microsoft gratuites et naturelles, rien à régler.", null));
            body.Children.Add(Choice("ElevenLabs", "de loin la plus réaliste, mais ça demande un peu de travail : un compte, une clé API et le choix d'une voix. " +
                                                   "Payant au-delà de l'essai gratuit.", "elevenlabs"));
            body.Children.Add(Choice("Hors ligne", "Kokoro, sans internet, moins naturelle en français.", null));

            body.Children.Add(Heading("3. Le push-to-talk", 18));
            body.Children.Add(Para("Dans SimHub, ouvre « Controls and events », ajoute un mapping avec l'action `AIRaceEnginePlugin.PushToTalk` " +
                                   "et appuie sur le bouton de ton volant. La page du plugin indique quand le bouton est détecté.", "Text", 13, 0, 18));

            body.Children.Add(Para("Tu retrouves cette fenêtre avec le bouton ? en haut de la page du plugin.", "Muted", 12, 0, 0));
            return Root("Bienvenue dans AI Race Engineer", body, "C'est parti", close);
        }

        // ---------- Fenêtre ----------

        public static readonly Brush Background = Frozen(Color.FromRgb(0x1F, 0x1F, 0x22));

        private static void Open(string windowTitle, Func<Action, FrameworkElement> build, bool activate)
        {
            var app = Application.Current;
            var owner = app?.MainWindow;
            var window = new Window
            {
                Title = windowTitle + " · AI Race Engineer",
                Width = 600, SizeToContent = SizeToContent.Height,
                MaxHeight = SystemParameters.WorkArea.Height * 0.9,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = activate,
                Background = Background,
                WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            };
            if (owner != null && owner.IsVisible) window.Owner = owner;
            window.Resources.MergedDictionaries.Add(Theme.Resources);
            window.SourceInitialized += (s, e) => DarkTitleBar(window);
            window.Content = build(window.Close);
            window.Show();
        }

        /// <summary>Contenu d'une fenêtre : titre, texte qui défile, bouton de fermeture.</summary>
        private static FrameworkElement Root(string title, UIElement body, string closeLabel, Action closeAction)
        {
            var root = new DockPanel { Margin = new Thickness(28, 24, 28, 20) };
            root.Resources.MergedDictionaries.Add(Theme.Resources);
            TextElement.SetFontFamily(root, Theme.Ui);
            TextElement.SetForeground(root, Theme.B("Text"));

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            var close = new Button { Style = Theme.S("BtnPrimary"), Content = closeLabel, IsDefault = true, IsCancel = true, Margin = new Thickness(0) };
            close.Click += (s, e) => closeAction();
            footer.Children.Add(close);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var heading = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(heading, Dock.Top);
            root.Children.Add(heading);
            root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 4, 0) });
            return root;
        }

        private static TextBlock Heading(string text, double top = 0) => new TextBlock
        {
            Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, top, 0, 8),
        };

        private static TextBlock Para(string markup, string brush, double size, double top, double bottom)
        {
            var t = new TextBlock { FontSize = size, Foreground = Theme.B(brush), TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.45, Margin = new Thickness(0, top, 0, bottom) };
            AddInlines(t.Inlines, markup);
            return t;
        }

        private static FrameworkElement Step(int n, string markup)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Theme.B("Raised"),
                VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 0),
                Child = new TextBlock { Text = n.ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var text = Para(markup, "Text", 13, 0, 0);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            return g;
        }

        /// <summary>Ligne de la bienvenue : nom en gras, description, et lien vers le guide s'il y en a un.</summary>
        private static FrameworkElement Choice(string name, string text, string guide)
        {
            var t = new TextBlock { FontSize = 13, Foreground = Theme.B("Text"), TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(12, 0, 0, 6) };
            t.Inlines.Add(new Run(name) { FontWeight = FontWeights.SemiBold });
            t.Inlines.Add(new Run(" : " + text));
            if (guide != null)
            {
                var link = new Hyperlink(new Run("Guide")) { Foreground = Theme.B("AccentLine") };
                link.Click += (s, e) => ShowGuide(guide);
                t.Inlines.Add(new Run("  "));
                t.Inlines.Add(link);
            }
            return t;
        }

        private static readonly Regex Markup = new Regex(@"\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)|`(?<code>[^`]+)`|«\s?(?<ui>[^»]+?)\s?»");

        private static void AddInlines(InlineCollection inlines, string markup)
        {
            int pos = 0;
            foreach (Match m in Markup.Matches(markup))
            {
                if (m.Index > pos) inlines.Add(new Run(markup.Substring(pos, m.Index - pos)));
                if (m.Groups["url"].Success)
                {
                    var url = m.Groups["url"].Value;
                    var link = new Hyperlink(new Run(m.Groups["label"].Value)) { Foreground = Theme.B("AccentLine"), ToolTip = url };
                    link.Click += (s, e) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
                    inlines.Add(link);
                }
                else if (m.Groups["code"].Success)
                    inlines.Add(new Run(m.Groups["code"].Value) { FontFamily = Theme.Mono, FontSize = 12, Background = Theme.B("Raised") });
                else
                    inlines.Add(new Run("« " + m.Groups["ui"].Value + " »") { FontWeight = FontWeights.SemiBold });
                pos = m.Index + m.Length;
            }
            if (pos < markup.Length) inlines.Add(new Run(markup.Substring(pos)));
        }

        // Barre de titre sombre (Windows 10 20H1 et plus), comme SimHub
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private static void DarkTitleBar(Window window)
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref on, sizeof(int));
            }
            catch { }
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
