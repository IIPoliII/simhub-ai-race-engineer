using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace AIRaceEngineer
{
    public class ProviderSettings
    {
        public string ApiKey { get; set; } = "";
        public string AccountId { get; set; } = ""; // Product ID Infomaniak / Account ID Cloudflare
        public string Model { get; set; } = "";
        public string BaseUrl { get; set; } = "";   // seulement pour "custom"
    }

    /// <summary>Description d'un fournisseur d'IA compatible OpenAI.</summary>
    public class ProviderInfo
    {
        public string Id, AccountLabel;
        public Msg Name, Tagline, AccountHelp, KeyHelp, KeyHelpUrl;
        public string[] Models;
        public Func<ProviderSettings, string> BaseUrl;
        public bool NeedsKey = true;
        public string DefaultModel => Models[0];

        public static readonly ProviderInfo[] All =
        {
            new ProviderInfo
            {
                Id = "infomaniak", Name = "Infomaniak", Tagline = new Msg("Suisse · 1M de crédits offerts", "Swiss · 1M free credits"),
                Models = new[] { "mistralai/Ministral-3-14B-Instruct-2512", "mistralai/Mistral-Small-4-119B-2603", "google/gemma-4-31B-it" },
                AccountLabel = "Product ID", AccountHelp = new Msg("Manager Infomaniak → AI Tools : le numéro du produit.", "Infomaniak Manager → AI Tools: the product number."),
                KeyHelp = new Msg("Token API avec le scope « ai-tools ».", "API token with the ai-tools scope."),
                // La page des tokens API, comme dans le guide (l'ancienne page produit est devenue « AI Services »)
                KeyHelpUrl = "https://manager.infomaniak.com/v3/ng/profile/user/token/list",
                // API v2 : accepte tout le catalogue (la v1 ne connaît que mistral3, mistral24b et qwen3)
                BaseUrl = p => $"https://api.infomaniak.com/2/ai/{p.AccountId.Trim()}/openai/v1",
            },
            new ProviderInfo
            {
                Id = "cloudflare", Name = "Cloudflare", Tagline = new Msg("Workers AI · quota gratuit / jour", "Workers AI · free daily quota"),
                // Le Llama 8B « -fast » a quitté le catalogue : Gemma 4 26B (4B actifs) coûte bien moins de neurons
                Models = new[] { "@cf/meta/llama-3.3-70b-instruct-fp8-fast", "@cf/google/gemma-4-26b-a4b-it", "@cf/mistralai/mistral-small-3.1-24b-instruct" },
                AccountLabel = "Account ID", AccountHelp = new Msg("Tableau de bord Cloudflare → Workers AI.", "Cloudflare dashboard → Workers AI."),
                KeyHelp = new Msg("Token API avec la permission « Workers AI ».", "API token with the Workers AI permission."),
                KeyHelpUrl = "https://dash.cloudflare.com/profile/api-tokens",
                BaseUrl = p => $"https://api.cloudflare.com/client/v4/accounts/{p.AccountId.Trim()}/ai/v1",
            },
            new ProviderInfo
            {
                Id = "openai", Name = "OpenAI", Tagline = new Msg("GPT · quelques centimes / course", "GPT · a few cents / race"),
                // gpt-4.1-nano est retiré, gpt-4.1-mini et gpt-4o-mini le sont le 14/01/2027 ; Luna : le moins cher, raisonnement coupé
                Models = new[] { "gpt-6-luna", "gpt-6.1-terra" },
                KeyHelp = new Msg("Clé API de ton compte OpenAI.", "API key from your OpenAI account."),
                KeyHelpUrl = "https://platform.openai.com/api-keys",
                BaseUrl = p => "https://api.openai.com/v1",
            },
            new ProviderInfo
            {
                Id = "claude", Name = "Claude", Tagline = new Msg("Anthropic · très bon en français", "Anthropic · natural, accurate answers"),
                Models = new[] { "claude-haiku-5-5", "claude-sonnet-5-5" },
                KeyHelp = new Msg("Clé API de la Console Claude.", "API key from the Claude Console."),
                KeyHelpUrl = "https://platform.claude.com/settings/keys",
                BaseUrl = p => "https://api.anthropic.com/v1/",
            },
            new ProviderInfo
            {
                Id = "custom", Name = new Msg("Autre", "Other"), Tagline = new Msg("Ollama, LM Studio, API compatible", "Ollama, LM Studio, compatible API"),
                // Petits modèles qui répondent sans réfléchir (« qwen3:4b » est devenu la version thinking)
                Models = new[] { "qwen3:4b-instruct", "llama3.2:3b", "gemma3:4b" },
                NeedsKey = false, KeyHelp = new Msg("Souvent inutile en local.", "Usually not needed locally."),
                BaseUrl = p => string.IsNullOrWhiteSpace(p.BaseUrl) ? "http://localhost:11434/v1" : CompatibleUrl(p.BaseUrl),
            },
        };

        /// <summary>
        /// Adresse de l'API compatible OpenAI. Ollama et LM Studio la servent sous /v1 : une adresse sans chemin
        /// (« http://10.0.0.5:11434/ ») répondait 404 partout, on y ajoute /v1. Sans http://, on le met.
        /// </summary>
        internal static string CompatibleUrl(string url)
        {
            url = (url ?? "").Trim();
            if (url.Length == 0) return url;
            if (url.IndexOf("://", StringComparison.Ordinal) < 0) url = "http://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
            return uri.AbsolutePath.Trim('/').Length == 0 && uri.Query.Length == 0 ? url.TrimEnd('/') + "/v1" : url;
        }

        public static ProviderInfo Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];

        /// <summary>Paramètres de la requête pour ce modèle : les mêmes pour le client vocal et pour « Tester la connexion ».</summary>
        public LlmRequest RequestFor(string model)
        {
            model = (model ?? "").Trim();
            if (Id == "claude")
                // Claude 5 réfléchit toujours par l'API compatible OpenAI (reasoning_effort y est ignoré) : de la marge pour
                // la réflexion, le prompt garde les réponses courtes. Pas de temperature : refusée sous 1 depuis Claude 4.7
                return new LlmRequest { MaxTokens = 1024, Temperature = null };
            if (Id == "openai")
            {
                // max_tokens est refusé par les modèles qui raisonnent ; max_completion_tokens passe partout chez OpenAI
                var r = new LlmRequest { MaxTokensField = "max_completion_tokens" };
                if (Regex.IsMatch(model, "^(o[0-9]|gpt-[5-9])"))
                {
                    // Raisonnement coupé quand le modèle le permet (Luna, Terra), sinon au plus bas ; pas de temperature
                    r.ReasoningEffort = model.Contains("luna") || model.Contains("terra") ? "none" : "low";
                    r.MaxTokens = 1024;
                    r.Temperature = null;
                }
                return r;
            }
            if (Id == "custom")
                // Ollama : reasoning_effort « none » coupe la réflexion des modèles qui réfléchissent (Qwen3, DeepSeek...).
                // Sans ça, la réflexion épuise max_tokens et la réponse arrive vide. Un serveur qui le refuse :
                // le client vocal et le test le retirent et réessaient
                return new LlmRequest { ReasoningEffort = "none" };
            return new LlmRequest();
        }
    }

    public sealed class LlmRequest
    {
        public int MaxTokens = 120;                    // une ou deux phrases à la radio
        public string MaxTokensField = "max_tokens";
        public double? Temperature = 0.3;              // null : pas envoyée
        public string ReasoningEffort;                 // null : pas envoyé
    }

    public class VoiceInfo
    {
        public string Id, Label, Language;

        public static readonly VoiceInfo[] Kokoro =
        {
            new VoiceInfo { Language = "fr", Id = "ff_siwis", Label = "Siwis (femme)" },
            new VoiceInfo { Language = "en", Id = "bm_george", Label = "George (British, male)" },
            new VoiceInfo { Language = "en", Id = "bm_lewis", Label = "Lewis (British, male)" },
            new VoiceInfo { Language = "en", Id = "bm_daniel", Label = "Daniel (British, male)" },
            new VoiceInfo { Language = "en", Id = "bf_emma", Label = "Emma (British, female)" },
            new VoiceInfo { Language = "en", Id = "am_michael", Label = "Michael (American, male)" },
            new VoiceInfo { Language = "en", Id = "am_adam", Label = "Adam (American, male)" },
            new VoiceInfo { Language = "en", Id = "af_heart", Label = "Heart (American, female)" },
        };

        /// <summary>Voix neuronales Microsoft (en ligne, gratuites).</summary>
        public static readonly VoiceInfo[] Neural =
        {
            new VoiceInfo { Language = "fr", Id = "fr-FR-HenriNeural", Label = "Henri (homme)" },
            new VoiceInfo { Language = "fr", Id = "fr-FR-DeniseNeural", Label = "Denise (femme)" },
            new VoiceInfo { Language = "fr", Id = "fr-FR-RemyMultilingualNeural", Label = "Rémy (homme, plus lent à générer)" },
            new VoiceInfo { Language = "fr", Id = "fr-CH-FabriceNeural", Label = "Fabrice (suisse, homme)" },
            new VoiceInfo { Language = "fr", Id = "fr-CH-ArianeNeural", Label = "Ariane (suisse, femme)" },
            new VoiceInfo { Language = "fr", Id = "fr-CA-AntoineNeural", Label = "Antoine (québécois, homme)" },
            new VoiceInfo { Language = "en", Id = "en-GB-RyanNeural", Label = "Ryan (British, male)" },
            new VoiceInfo { Language = "en", Id = "en-GB-ThomasNeural", Label = "Thomas (British, male)" },
            new VoiceInfo { Language = "en", Id = "en-GB-SoniaNeural", Label = "Sonia (British, female)" },
            new VoiceInfo { Language = "en", Id = "en-US-AndrewNeural", Label = "Andrew (American, male)" },
            new VoiceInfo { Language = "en", Id = "en-AU-WilliamMultilingualNeural", Label = "William (Australian, male)" },
        };
    }

    public class EngineerSettings
    {
        public string Language { get; set; } = DefaultLanguage();

        /// <summary>
        /// Langue de départ : celle choisie dans l'installateur (HKCU\Software\AIRaceEngineer, valeur Language),
        /// sinon celle de Windows.
        /// </summary>
        public static string DefaultLanguage()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\AIRaceEngineer"))
                    if (key?.GetValue("Language") is string chosen && (chosen == "fr" || chosen == "en")) return chosen;
            }
            catch { }
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";
        }

        /// <summary>Septante, huitante, nonante : coché d'office si Windows est réglé sur la Suisse (fr-CH).</summary>
        public bool SwissNumbers { get; set; } = SwissRegion();

        private static bool SwissRegion()
        {
            try
            {
                // Région de l'utilisateur (Panneau de configuration) : SimHub peut changer la culture de ses threads
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\International"))
                    if (key?.GetValue("LocaleName") is string locale) return locale.EndsWith("-CH", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            return false;
        }

        // IA
        public string Provider { get; set; } = "infomaniak";
        public Dictionary<string, ProviderSettings> Providers { get; set; } = new Dictionary<string, ProviderSettings>();

        // Voix
        public string TtsEngine { get; set; } = "neural"; // neural | kokoro | elevenlabs
        public string NeuralVoiceFr { get; set; } = SwissRegion() ? "fr-CH-ArianeNeural" : "fr-FR-HenriNeural";
        public string NeuralVoiceEn { get; set; } = "en-GB-RyanNeural";
        public int SettingsVersion { get; set; } // absent des anciens fichiers = 0
        public string VoiceFr { get; set; } = "ff_siwis";
        public string VoiceEn { get; set; } = "bm_george";
        public double VoiceSpeed { get; set; } = 1.1;
        public string ElevenLabsKey { get; set; } = "";
        public string ElevenLabsVoiceId { get; set; } = "JBFqnCBsd6RMkjVDRZzb";
        public bool RadioEffect { get; set; } = true;
        public bool TrackNoise { get; set; } = true;   // des F1 passent derrière l'ingé (avec l'effet radio)
        public int VoiceVolume { get; set; } = 100;    // en %, 50 à 200 : au-delà de 100, les crêtes sont arrondies
        public int NoiseVolume { get; set; } = 100;    // bruits de piste, en %, 0 à 200
        public string SttModel { get; set; } = "small"; // base = rapide, small = précis

        // Annonces automatiques
        public bool CallLapTime { get; set; } = true;
        public bool CallPersonalBest { get; set; } = true;
        public bool CallGaps { get; set; } = true;
        public bool CallPosition { get; set; } = true;
        public bool CallFuel { get; set; } = true;
        public bool CallInvalidLap { get; set; } = true;   // « Tour invalidé. » au moment où le jeu l'invalide
        public bool CallSplits { get; set; } = true;       // écart au meilleur tour à chaque secteur, hors course
        public bool CallDamage { get; set; } = true;       // dégâts, crevaison, roue perdue
        public int FuelWarningLaps { get; set; } = 3;

        public bool AutoStartClient { get; set; } = true;
        public bool PttTested { get; set; }
        /// <summary>Un appui ouvre la radio, l'appui suivant envoie (au lieu de maintenir le bouton).</summary>
        public bool PttToggle { get; set; }
        public bool WelcomeShown { get; set; }   // fenêtre de bienvenue déjà montrée au premier démarrage
        public string NotesSeenVersion { get; set; }  // dernière version dont les nouveautés ont été montrées (0.5.3+)

        /// <summary>Réglages abîmés au chargement : ce qui a été repris, à montrer dans la page (null sinon).</summary>
        [JsonIgnore] public Msg LoadNotice { get; private set; }

        public ProviderSettings Current(string id = null)
        {
            id = id ?? Provider;
            if (!Providers.TryGetValue(id, out var p))
                Providers[id] = p = new ProviderSettings { Model = ProviderInfo.Get(id).DefaultModel };
            return p;
        }

        [JsonIgnore] public string Voice => Language == "fr" ? VoiceFr : VoiceEn;
        [JsonIgnore] public string NeuralVoice => Language == "fr" ? NeuralVoiceFr : NeuralVoiceEn;

        /// <summary>Clé (et identifiant de compte) renseignés pour le fournisseur choisi.</summary>
        [JsonIgnore]
        public bool AiConfigured
        {
            get
            {
                var info = ProviderInfo.Get(Provider);
                var p = Current();
                return !info.NeedsKey || (!string.IsNullOrWhiteSpace(p.ApiKey) &&
                       (info.AccountLabel == null || !string.IsNullOrWhiteSpace(p.AccountId)));
            }
        }

        public string ModelOrDefault()
        {
            var p = Current();
            return string.IsNullOrWhiteSpace(p.Model) ? ProviderInfo.Get(Provider).DefaultModel : p.Model.Trim();
        }

        // ---------- Persistance (%LOCALAPPDATA%\AIRaceEngineer\settings.json, clés chiffrées avec DPAPI) ----------

        public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIRaceEngineer");
        private static string FilePath => Path.Combine(DataDir, "settings.json");
        private static string BackupPath => FilePath + ".bak";   // version précédente, gardée à chaque enregistrement
        private static string BadPath => FilePath + ".bad";      // fichier illisible mis de côté
        private static string TempPath => FilePath + ".tmp";     // nouvelle version en cours d'enregistrement
        private const int CurrentVersion = 4;                     // voir Migrate()

        public static EngineerSettings Load()
        {
            if (!File.Exists(FilePath)) return Resume() ?? new EngineerSettings { SettingsVersion = CurrentVersion };
            var s = Read(FilePath, out var error);
            if (s != null) return s;

            // Fichier abîmé (coupure pendant un enregistrement...) : on le garde de côté au lieu de l'écraser,
            // puis on reprend la version précédente si elle se lit
            SimHub.Logging.Current.Warn("[AI Race Engineer] réglages illisibles : " + error);
            bool kept = false;
            try { File.Copy(FilePath, BadPath, true); kept = true; }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[AI Race Engineer] copie de settings.json impossible : " + ex.Message); }
            var where = kept ? new Msg(@" L'ancien fichier est gardé dans %LOCALAPPDATA%\AIRaceEngineer (settings.json.bad).", @" The old file is kept in %LOCALAPPDATA%\AIRaceEngineer (settings.json.bad).")
                             : new Msg("", "");

            s = File.Exists(BackupPath) ? Read(BackupPath, out _) : null;
            // Le fichier abîmé ne reste pas en place, sinon l'avertissement reviendrait à chaque démarrage
            try
            {
                if (s != null) File.Copy(BackupPath, FilePath, true);
                else if (kept) File.Delete(FilePath);
            }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[AI Race Engineer] remise en état de settings.json impossible : " + ex.Message); }
            if (s != null)
            {
                SimHub.Logging.Current.Warn("[AI Race Engineer] réglages repris de settings.json.bak");
                s.LoadNotice = new Msg("Tes réglages étaient abîmés : la version précédente a été reprise." + where.FrText,
                                       "Your settings file was damaged: the previous version was restored." + where.EnText);
                return s;
            }
            return new EngineerSettings
            {
                SettingsVersion = CurrentVersion,
                LoadNotice = new Msg("Tes réglages étaient illisibles : retour aux valeurs par défaut." + where.FrText,
                                     "Your settings could not be read: back to the defaults." + where.EnText),
            };
        }

        /// <summary>
        /// settings.json absent mais un enregistrement interrompu entre les deux renommages de File.Replace : l'ancien
        /// fichier est déjà devenu settings.json.bak, le nouveau est encore settings.json.tmp (écrit jusqu'au disque).
        /// On reprend le plus récent qui se lit, null s'il n'y a rien (première installation).
        /// </summary>
        private static EngineerSettings Resume()
        {
            foreach (var path in new[] { TempPath, BackupPath })
            {
                if (!File.Exists(path)) continue;
                var s = Read(path, out _);
                if (s == null) continue;
                try { File.Copy(path, FilePath, false); }
                catch (Exception ex) { SimHub.Logging.Current.Warn("[AI Race Engineer] remise en place de settings.json impossible : " + ex.Message); }
                SimHub.Logging.Current.Warn("[AI Race Engineer] réglages repris de " + Path.GetFileName(path));
                s.LoadNotice = new Msg("Ton dernier enregistrement a été interrompu : tes réglages ont été repris.",
                                       "Your last save was interrupted: your settings were restored.");
                return s;
            }
            return null;
        }

        private static EngineerSettings Read(string path, out string error)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    var s = JsonConvert.DeserializeObject<EngineerSettings>(File.ReadAllText(path, Encoding.UTF8));
                    if (s == null) { error = "fichier vide"; return null; }
                    s.TransformSecrets(Unprotect);
                    s.Migrate();
                    error = null;
                    return s;
                }
                // Fichier pris un instant (antivirus, sauvegarde) : on réessaie avant de le croire abîmé
                catch (IOException) when (attempt < 3) { System.Threading.Thread.Sleep(200); }
                catch (Exception ex) { error = ex.Message; return null; }
            }
        }

        private static readonly object SaveLock = new object(); // SimHub peut avoir deux instances du plugin

        public void Save()
        {
            var copy = JsonConvert.DeserializeObject<EngineerSettings>(JsonConvert.SerializeObject(this));
            copy.TransformSecrets(Protect);
            var json = JsonConvert.SerializeObject(copy, Formatting.Indented);
            lock (SaveLock)
            {
                Directory.CreateDirectory(DataDir);
                // Écriture sûre : un fichier temporaire écrit jusqu'au disque, puis échangé d'un coup avec settings.json
                // (l'ancien devient settings.json.bak). Une coupure ne laisse jamais un fichier à moitié écrit.
                var tmp = TempPath;
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(stream, Encoding.UTF8, 4096, true)) writer.Write(json);
                    stream.Flush(true);
                }
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, BackupPath, true);
                else File.Move(tmp, FilePath);
            }
        }

        private void Migrate()
        {
            // 0.2.0 proposait un nom de modèle Infomaniak refusé par l'API
            // « mistral3 » est l'ancien alias de l'API v1 pour ce modèle : on garde son vrai nom
            if (Providers.TryGetValue("infomaniak", out var inf) && (inf.Model == "Ministral-3-14B" || inf.Model == "mistral3")) inf.Model = "mistralai/Ministral-3-14B-Instruct-2512";
            // 0.4.0 : la voix française de Kokoro comprend mal l'anglais de course, les voix neuronales deviennent la voix par défaut
            if (SettingsVersion < 2 && TtsEngine == "kokoro") TtsEngine = "neural";
            // Microsoft a renommé la voix William (même voix)
            if (NeuralVoiceEn == "en-AU-WilliamNeural") NeuralVoiceEn = "en-AU-WilliamMultilingualNeural";
            // 0.5.0 : modèles retirés par leur fournisseur (gpt-4.1-nano et le Llama 8B « -fast » ne répondent plus) :
            // on passe au modèle par défaut à chaque chargement. gpt-4.1-mini et gpt-4o-mini s'arrêtent le 14/01/2027 :
            // remplacés une seule fois, à la mise à jour ; choisis de nouveau à la main, ils restent.
            if (Providers.TryGetValue("openai", out var oai)
                && (oai.Model == "gpt-4.1-nano" || (SettingsVersion < 3 && (oai.Model == "gpt-4.1-mini" || oai.Model == "gpt-4o-mini"))))
                oai.Model = ProviderInfo.Get("openai").DefaultModel;
            if (Providers.TryGetValue("cloudflare", out var cf) && cf.Model == "@cf/meta/llama-3.1-8b-instruct-fast")
                cf.Model = ProviderInfo.Get("cloudflare").DefaultModel;
            // 0.5.2 : en Suisse, la voix française par défaut devient Ariane (fr-CH), une seule fois
            if (SettingsVersion < 4 && SwissRegion() && NeuralVoiceFr == "fr-FR-HenriNeural") NeuralVoiceFr = "fr-CH-ArianeNeural";
            // Fichier modifié à la main : volumes gardés dans les bornes des curseurs
            VoiceVolume = Math.Max(50, Math.Min(200, VoiceVolume));
            NoiseVolume = Math.Max(0, Math.Min(200, NoiseVolume));
            SettingsVersion = CurrentVersion;
        }

        private void TransformSecrets(Func<string, string> f)
        {
            foreach (var p in Providers.Values) p.ApiKey = f(p.ApiKey);
            ElevenLabsKey = f(ElevenLabsKey);
        }

        private const string Prefix = "dpapi:";

        private static string Protect(string s) =>
            string.IsNullOrEmpty(s) ? s
            : Prefix + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser));

        private static string Unprotect(string s)
        {
            if (string.IsNullOrEmpty(s) || !s.StartsWith(Prefix)) return s;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s.Substring(Prefix.Length)), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }

        // ---------- Configuration envoyée au client vocal ----------

        public object ToClientConfig(int rev)
        {
            var info = ProviderInfo.Get(Provider);
            var p = Current();
            bool configured = AiConfigured;
            var model = ModelOrDefault();
            var req = info.RequestFor(model);
            return new
            {
                rev,
                language = Language,
                swiss_numbers = SwissNumbers && Language == "fr",
                llm = new
                {
                    provider = info.Id,
                    configured,
                    base_url = info.BaseUrl(p),
                    api_key = string.IsNullOrWhiteSpace(p.ApiKey) ? "none" : p.ApiKey.Trim(),
                    model,
                    max_tokens = req.MaxTokens,
                    max_tokens_field = req.MaxTokensField,     // nom du paramètre : max_tokens ou max_completion_tokens
                    temperature = req.Temperature,             // null : ne pas l'envoyer
                    reasoning_effort = req.ReasoningEffort,    // null : ne pas l'envoyer
                },
                tts = new
                {
                    engine = TtsEngine,
                    kokoro_voice = Voice,
                    neural_voice = NeuralVoice,
                    kokoro_speed = VoiceSpeed,
                    radio_effect = RadioEffect,
                    track_noise = TrackNoise,
                },
                elevenlabs = new
                {
                    api_key = ElevenLabsKey?.Trim() ?? "",
                    voice_id = ElevenLabsVoiceId?.Trim() ?? "",
                    model = "eleven_flash_v2_5",
                },
                stt = new { model = SttModel, device = "cpu", compute_type = "int8" },
                // À part de tts : changer un volume ne recharge pas la voix
                audio = new { voice_volume = VoiceVolume / 100.0, noise_volume = NoiseVolume / 100.0 },
            };
        }
    }
}
