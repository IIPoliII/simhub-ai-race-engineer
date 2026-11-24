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
using static AIRaceEngineer.L10n;

namespace AIRaceEngineer
{
    /// <summary>Guide pas à pas d'un fournisseur (bouton ? à côté de chaque IA).</summary>
    public sealed class Guide
    {
        public string Title, Summary, Cost, Note;
        public string WindowTitle;   // titre de la fenêtre, quand « Configurer » + Title ne se lit pas bien
        public string[] Steps;

        /// <summary>Guides dans la langue de la page.</summary>
        public static Dictionary<string, Guide> All => L10n.Fr ? Fr : En;

        // Mise en forme légère dans les textes : [lien](https://...), `code`, « libellé d'un bouton »
        private static readonly Dictionary<string, Guide> Fr = new Dictionary<string, Guide>
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
                       "ça fait environ une centaine de questions par jour (davantage avec Gemma 4 26B). Au-delà, l'ingé ne répond plus " +
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
                Cost = "Crédits prépayés : 5 $ minimum, valables un an. Avec gpt-6-luna, une question coûte moins d'un dixième de centime. " +
                       "Un abonnement ChatGPT ne compte pas : l'API se paie à part.",
                Steps = new[]
                {
                    "Crée un compte sur [platform.openai.com](https://platform.openai.com/signup).",
                    "Dans [Billing](https://platform.openai.com/settings/organization/billing/overview), ajoute un moyen de paiement et achète des crédits.",
                    "Sur la page [API keys](https://platform.openai.com/api-keys), clique « Create new secret key », copie la clé (elle ne s'affiche qu'une fois) et colle-la dans Clé API.",
                    "Garde `gpt-6-luna` (rapide et bon marché) et clique « Tester la connexion ».",
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
                WindowTitle = "Installer une IA sur ton PC (Ollama)",
                Summary = "Gratuite et sans compte, mais elle utilise ta carte graphique pendant que tu joues.",
                Cost = "Gratuit. Prends un petit modèle (3 à 4 milliards de paramètres) pour garder tes FPS : `qwen3:4b-instruct` ou `llama3.2:3b`. " +
                       "Évite les modèles `thinking`, qui réfléchissent avant de répondre : trop lents pour la radio.",
                Steps = new[]
                {
                    "Installe [Ollama](https://ollama.com/download).",
                    "Dans un terminal, télécharge un modèle : `ollama pull qwen3:4b-instruct`.",
                    "Laisse l'URL `http://localhost:11434/v1`, sans clé, mets le même nom de modèle (`qwen3:4b-instruct`) et clique « Tester la connexion ».",
                },
                Note = "LM Studio marche aussi : démarre son serveur local et mets l'URL `http://localhost:1234/v1`. " +
                       "Ollama sur un autre PC : mets son adresse, par exemple `http://192.168.1.20:11434` (le `/v1` est ajouté s'il manque). " +
                       "Les modèles qui réfléchissent avant de répondre (Qwen3...) sont mis en réponse directe quand ils le permettent.",
            },
            ["elevenlabs"] = new Guide
            {
                Title = "ElevenLabs",
                Summary = "La voix la plus réaliste, de loin. Ça demande un peu de travail : un compte, une clé API et le choix d'une voix.",
                Cost = "L'offre gratuite (10 000 crédits par mois, usage personnel) suffit pour essayer, pas pour rouler régulièrement : " +
                       "l'offre Starter coûte 6 $ par mois. Le plugin utilise le modèle Flash, le moins cher en crédits.",
                Steps = new[]
                {
                    "Crée un compte sur [elevenlabs.io](https://elevenlabs.io/app/sign-up).",
                    "Crée une clé sur la page [API keys](https://elevenlabs.io/app/settings/api-keys). Tu peux la limiter à la synthèse vocale (Text to Speech) et lui fixer un plafond de crédits. Copie-la et colle-la dans Clé API.",
                    "Choisis une voix dans la [bibliothèque](https://elevenlabs.io/app/voice-library), ajoute-la à tes voix et copie son Voice ID. Colle-le dans le champ Voice ID (par défaut : George, une voix britannique).",
                    "Clique « Radio check » en haut de la page pour l'entendre.",
                },
                Note = "Avec l'offre gratuite, certaines voix de la bibliothèque peuvent être refusées par l'API. Dans ce cas, garde George ou passe à l'offre Starter.",
            },
        };

        // Mêmes guides en anglais : mêmes faits, mêmes liens, mêmes étapes
        private static readonly Dictionary<string, Guide> En = new Dictionary<string, Guide>
        {
            ["infomaniak"] = new Guide
            {
                Title = "Infomaniak",
                Summary = "Swiss host: the AI runs in their data centres in Switzerland.",
                Cost = "1 million free credits to try it for a month, then pay as you go, billed monthly. " +
                       "A question to the engineer uses a few thousand tokens: the free million covers hundreds of questions. " +
                       "A bank card is requested when you order, to verify your identity.",
                Steps = new[]
                {
                    "Order AI Tools on [shop.infomaniak.com](https://shop.infomaniak.com/order2/ai-tools): sign in or create an Infomaniak account, then confirm the order (free to start).",
                    "Open AI Tools in the [Manager](https://manager.infomaniak.com/v3/ng/products/cloud/ai-tools) and copy your product's Product ID (a number). Paste it into the Product ID field.",
                    "On the [API tokens page](https://manager.infomaniak.com/v3/ng/profile/user/token/list), click « Create a token », choose the AI Tools application (scope `ai-tools`), then confirm with your password.",
                    "Copy the token right away (it won't be shown again) and paste it into API key.",
                    "Keep the recommended model and click « Test connection ».",
                },
            },
            ["cloudflare"] = new Guide
            {
                Title = "Cloudflare",
                Summary = "Workers AI, Cloudflare's AI. Nothing to pay as long as you stay within the free quota.",
                Cost = "Free: 10,000 neurons (Cloudflare's unit) per day, reset every day at 00:00 UTC. With the recommended model, " +
                       "that's about a hundred questions a day (more with Gemma 4 26B). Beyond that, the engineer stops answering " +
                       "questions until the next day; automatic callouts keep going.",
                Steps = new[]
                {
                    "Create a free account on [dash.cloudflare.com](https://dash.cloudflare.com/sign-up).",
                    "Open [Workers AI](https://dash.cloudflare.com/?to=/:account/ai/workers-ai) and click « Use REST API ».",
                    "Click « Create a Workers AI API Token », check the pre-filled details, then « Create API Token » and « Copy API Token ». Paste the token into API key.",
                    "On the same page, under « Get Account ID », copy the Account ID and paste it into the Account ID field.",
                    "Click « Test connection ».",
                },
            },
            ["openai"] = new Guide
            {
                Title = "OpenAI",
                Summary = "The GPT models, pay as you go, no subscription.",
                Cost = "Prepaid credits: $5 minimum, valid for one year. With gpt-6-luna, a question costs less than a tenth of a cent. " +
                       "A ChatGPT subscription doesn't count: the API is paid separately.",
                Steps = new[]
                {
                    "Create an account on [platform.openai.com](https://platform.openai.com/signup).",
                    "In [Billing](https://platform.openai.com/settings/organization/billing/overview), add a payment method and buy credits.",
                    "On the [API keys](https://platform.openai.com/api-keys) page, click « Create new secret key », copy the key (it is shown only once) and paste it into API key.",
                    "Keep `gpt-6-luna` (fast and cheap) and click « Test connection ».",
                },
            },
            ["claude"] = new Guide
            {
                Title = "Claude",
                Summary = "Anthropic's models, natural and accurate answers. Pay as you go, no subscription.",
                Cost = "Prepaid credits, bought in the Claude Console (the minimum is shown when you buy). " +
                       "A Claude.ai subscription doesn't count: the API is paid separately.",
                Steps = new[]
                {
                    "Create an account on the [Claude Console](https://platform.claude.com).",
                    "In Settings → Billing, click « Buy credits ».",
                    "In [API keys](https://platform.claude.com/settings/keys), click « Create key », give it a name, then copy it (it starts with `sk-ant-` and is shown only once). Paste it into API key.",
                    "Keep `claude-haiku-5-5` (the fastest) and click « Test connection ».",
                },
            },
            ["custom"] = new Guide
            {
                Title = "AI on your PC (Ollama)",
                WindowTitle = "Set up an AI on your PC (Ollama)",
                Summary = "Free and no account needed, but it uses your graphics card while you play.",
                Cost = "Free. Pick a small model (3 to 4 billion parameters) to keep your FPS: `qwen3:4b-instruct` or `llama3.2:3b`. " +
                       "Avoid `thinking` models, which reason before they answer: too slow for the radio.",
                Steps = new[]
                {
                    "Install [Ollama](https://ollama.com/download).",
                    "In a terminal, download a model: `ollama pull qwen3:4b-instruct`.",
                    "Leave the URL `http://localhost:11434/v1`, no key, enter the same model name (`qwen3:4b-instruct`) and click « Test connection ».",
                },
                Note = "LM Studio works too: start its local server and use the URL `http://localhost:1234/v1`. " +
                       "Ollama on another PC: enter its address, for example `http://192.168.1.20:11434` (`/v1` is added if missing). " +
                       "Models that think before answering (Qwen3...) are switched to direct answers when they allow it.",
            },
            ["elevenlabs"] = new Guide
            {
                Title = "ElevenLabs",
                Summary = "By far the most realistic voice. It takes a bit of work: an account, an API key and choosing a voice.",
                Cost = "The free plan (10,000 credits a month, personal use) is enough to try it, not to race regularly: " +
                       "the Starter plan costs $6 a month. The plugin uses the Flash model, the cheapest in credits.",
                Steps = new[]
                {
                    "Create an account on [elevenlabs.io](https://elevenlabs.io/app/sign-up).",
                    "Create a key on the [API keys](https://elevenlabs.io/app/settings/api-keys) page. You can restrict it to speech synthesis (Text to Speech) and give it a credit limit. Copy it and paste it into API key.",
                    "Pick a voice in the [library](https://elevenlabs.io/app/voice-library), add it to your voices and copy its Voice ID. Paste it into the Voice ID field (default: George, a British voice).",
                    "Click « Radio check » at the top of the page to hear it.",
                },
                Note = "On the free plan, the API may refuse some library voices. If so, keep George or move to the Starter plan.",
            },
        };
    }

    /// <summary>Fenêtres de la page : bienvenue (au premier démarrage, puis bouton ?) et guides des fournisseurs.</summary>
    public static class GuideWindows
    {
        public static void ShowGuide(string id)
        {
            if (!Guide.All.TryGetValue(id, out var g)) return;
            Open(g.WindowTitle ?? T("Configurer ", "Set up ") + g.Title, w => BuildGuide(g, w), activate: true);
        }

        /// <param name="activate">false au démarrage de SimHub : la fenêtre ne doit pas voler le focus (un jeu en plein écran)</param>
        public static void ShowWelcome(bool activate) => Open(T("Bienvenue", "Welcome"), BuildWelcome, activate);
        public static void ShowWelcome() => ShowWelcome(true);

        /// <summary>Notes de version. <paramref name="since"/> : après une mise à jour, seulement ce qui est nouveau depuis cette version
        /// (null : la version installée) ; sinon tout l'historique.</summary>
        public static void ShowReleaseNotes(bool afterUpdate, Version since = null, bool activate = true)
        {
            var title = afterUpdate ? T("Nouveautés", "What's new") : T("Notes de version", "Release notes");
            Open(title, close => BuildReleaseNotes(afterUpdate, since, close), activate);
        }

        public static void ShowReleaseNotes() => ShowReleaseNotes(false);

        public static FrameworkElement BuildReleaseNotes(bool afterUpdate, Version since, Action close)
        {
            var releases = afterUpdate ? ReleaseNotes.Since(L10n.Fr, since) : ReleaseNotes.All(L10n.Fr);
            var body = new StackPanel();
            bool first = true;
            foreach (var r in releases)
            {
                var head = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, first ? 0 : 18, 0, 6) };
                head.Inlines.Add(new Run(r.Version.ToString()));
                if (r.Date != null) head.Inlines.Add(new Run("  " + r.Date) { FontSize = 12, FontWeight = FontWeights.Normal, Foreground = Theme.B("Muted") });
                body.Children.Add(head);
                first = false;
                foreach (var g in r.Groups)
                {
                    body.Children.Add(Para(g.Title, "Muted", 12, 4, 4));
                    foreach (var item in g.Items) body.Children.Add(Bullet(item));
                }
            }
            if (releases.Count == 0)
                body.Children.Add(Para(T("Pas de notes pour cette version.", "No notes for this version."), "Muted", 13, 0, 0));
            if (afterUpdate)
            {
                var more = new TextBlock { FontSize = 12, Margin = new Thickness(0, 16, 0, 0) };
                var link = new Hyperlink(new Run(T("Toutes les versions", "All versions"))) { Foreground = Theme.B("AccentLine") };
                link.Click += (s, e) => ShowReleaseNotes(false);
                more.Inlines.Add(link);
                body.Children.Add(more);
            }
            var title = afterUpdate
                ? T($"Nouveautés de la {ReleaseNotes.Current}", $"What's new in {ReleaseNotes.Current}")
                : T("Notes de version", "Release notes");
            return Root(title, body, afterUpdate ? T("Compris", "Got it") : T("Fermer", "Close"), close);
        }

        private static FrameworkElement Bullet(string markup)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new TextBlock { Text = "•", FontSize = 13, Foreground = Theme.B("Muted") });
            var text = Para(markup, "Text", 13, 0, 0);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            return g;
        }

        public static FrameworkElement BuildGuide(string id, Action close) => BuildGuide(Guide.All[id], close);

        private static FrameworkElement BuildGuide(Guide g, Action close)
        {
            var body = new StackPanel();
            body.Children.Add(Para(g.Summary, "Text2", 13, 0, 16));
            body.Children.Add(Heading(T("Ce que ça coûte", "What it costs")));
            body.Children.Add(Para(g.Cost, "Text", 13, 0, 18));
            body.Children.Add(Heading(T("Étapes", "Steps")));
            for (int i = 0; i < g.Steps.Length; i++) body.Children.Add(Step(i + 1, g.Steps[i]));
            if (g.Note != null) body.Children.Add(Para(g.Note, "Muted", 12, 6, 0));
            return Root(g.Title, body, T("Fermer", "Close"), close);
        }

        public static FrameworkElement BuildWelcome(Action close)
        {
            var body = new StackPanel();
            body.Children.Add(Para(T("Ton ingé de course à la radio. Il annonce tout seul tes temps, tes écarts, tes places et le carburant, " +
                                     "et répond à tes questions quand tu maintiens un bouton du volant.",
                                     "Your race engineer on the radio. It calls out your lap times, gaps, positions and fuel on its own, " +
                                     "and answers your questions while you hold a wheel button."), "Text2", 13, 0, 8));
            body.Children.Add(Para(T("Au premier démarrage, le plugin installe la voix (quelques minutes) : l'avancement s'affiche dans la case Client vocal.",
                                     "On first start, the plugin installs the voice (a few minutes): progress shows in the Voice client box."), "Muted", 12, 0, 18));

            body.Children.Add(Heading(T("1. Choisis une IA", "1. Choose an AI")));
            body.Children.Add(Para(T("C'est elle qui comprend tes questions et y répond avec la télémétrie. Les annonces automatiques n'en ont pas besoin : " +
                                     "elles marchent dès que la voix est installée, même sans internet. Plusieurs IA au choix, chacune avec son guide :",
                                     "It understands your questions and answers them with the telemetry. Automatic callouts don't need it: " +
                                     "they work as soon as the voice is installed, even without internet. Several AIs to choose from, each with its guide:"), "Text", 13, 0, 8));
            body.Children.Add(Choice("Infomaniak", T("suisse, 1 million de crédits offerts pour commencer. Le choix par défaut.", "Swiss, 1 million free credits to start. The default choice."), "infomaniak"));
            body.Children.Add(Choice("Cloudflare", T("un quota gratuit chaque jour, sans rien payer.", "a free quota every day, nothing to pay."), "cloudflare"));
            body.Children.Add(Choice("OpenAI", T("payant à l'usage, quelques centimes par course.", "pay as you go, a few cents per race."), "openai"));
            body.Children.Add(Choice("Claude", T("payant à l'usage, très bon en français.", "pay as you go, natural answers."), "claude"));
            body.Children.Add(Choice(T("Autre", "Other"), T("une IA sur ton PC (Ollama). Gratuite, mais elle prend de la carte graphique au jeu.",
                                                            "an AI on your PC (Ollama). Free, but it takes graphics card power away from the game."), "custom"));

            body.Children.Add(Heading(T("2. La voix", "2. The voice"), 18));
            body.Children.Add(Choice(T("En ligne", "Online"), T("par défaut. Voix Microsoft gratuites et naturelles, rien à régler.", "the default. Free, natural Microsoft voices, nothing to set up."), null));
            body.Children.Add(Choice("ElevenLabs", T("de loin la plus réaliste, mais ça demande un peu de travail : un compte, une clé API et le choix d'une voix. " +
                                                     "Payant au-delà de l'offre gratuite.",
                                                     "by far the most realistic, but it takes a bit of work: an account, an API key and choosing a voice. " +
                                                     "Paid beyond the free plan."), "elevenlabs"));
            body.Children.Add(Choice(T("Hors ligne", "Offline"), T("Kokoro, sans internet, moins naturelle en français.", "Kokoro, no internet needed. British and American voices."), null));

            body.Children.Add(Heading(T("3. Le push-to-talk", "3. Push-to-talk"), 18));
            body.Children.Add(Para(T("Dans SimHub, ouvre « Controls and events », ajoute un mapping avec l'action `AIRaceEnginePlugin.PushToTalk` " +
                                     "et appuie sur le bouton de ton volant. La page du plugin indique quand le bouton est détecté.",
                                     "In SimHub, open « Controls and events », add a mapping with the `AIRaceEnginePlugin.PushToTalk` action " +
                                     "and press your wheel button. The plugin page shows when the button is detected."), "Text", 13, 0, 18));

            body.Children.Add(Para(T("Tu retrouves cette fenêtre avec le bouton ? en haut de la page du plugin.",
                                     "You can open this window again with the ? button at the top of the plugin page."), "Muted", 12, 0, 0));
            return Root(T("Bienvenue dans AI Race Engineer", "Welcome to AI Race Engineer"), body, T("C'est parti", "Let's go"), close);
        }

        // ---------- Fenêtre ----------

        public static readonly Brush Background = Frozen(Color.FromRgb(0x1F, 0x1F, 0x22));

        // Une seule fenêtre par titre (le titre porte la langue) : un double-clic sur ? ne l'ouvre pas deux fois
        private static readonly Dictionary<string, Window> OpenWindows = new Dictionary<string, Window>();

        private static void Open(string windowTitle, Func<Action, FrameworkElement> build, bool activate)
        {
            if (OpenWindows.TryGetValue(windowTitle, out var existing))
            {
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                if (activate) existing.Activate();
                return;
            }
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
            window.Closed += (s, e) => OpenWindows.Remove(windowTitle);
            window.Show();
            OpenWindows[windowTitle] = window;
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
            t.Inlines.Add(new Run(T(" : ", ": ") + text));
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
                    // Libellé d'un bouton : entre guillemets en français, entre guillemets anglais sinon
                    inlines.Add(new Run(T("« " + m.Groups["ui"].Value + " »", "“" + m.Groups["ui"].Value + "”")) { FontWeight = FontWeights.SemiBold });
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
