using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        public string Id, Name, Tagline, AccountLabel, AccountHelp, KeyHelpUrl, KeyHelp;
        public string[] Models;
        public Func<ProviderSettings, string> BaseUrl;
        public bool NeedsKey = true;
        public string DefaultModel => Models[0];

        public static readonly ProviderInfo[] All =
        {
            new ProviderInfo
            {
                Id = "infomaniak", Name = "Infomaniak", Tagline = "Suisse · 1M de crédits offerts",
                Models = new[] { "mistral3", "mistral24b", "qwen3" },
                AccountLabel = "Product ID", AccountHelp = "Manager Infomaniak → AI Tools : le numéro du produit.",
                KeyHelp = "Token API avec le scope « ai-tools ».",
                KeyHelpUrl = "https://www.infomaniak.com/fr/hebergement/ai-tools",
                BaseUrl = p => $"https://api.infomaniak.com/1/ai/{p.AccountId.Trim()}/openai",
            },
            new ProviderInfo
            {
                Id = "cloudflare", Name = "Cloudflare", Tagline = "Workers AI · quota gratuit / jour",
                Models = new[] { "@cf/meta/llama-3.3-70b-instruct-fp8-fast", "@cf/meta/llama-3.1-8b-instruct-fast", "@cf/mistralai/mistral-small-3.1-24b-instruct" },
                AccountLabel = "Account ID", AccountHelp = "Tableau de bord Cloudflare → Workers AI.",
                KeyHelp = "Token API avec la permission « Workers AI ».",
                KeyHelpUrl = "https://dash.cloudflare.com/profile/api-tokens",
                BaseUrl = p => $"https://api.cloudflare.com/client/v4/accounts/{p.AccountId.Trim()}/ai/v1",
            },
            new ProviderInfo
            {
                Id = "openai", Name = "OpenAI", Tagline = "GPT · quelques centimes / course",
                Models = new[] { "gpt-4.1-mini", "gpt-4.1-nano", "gpt-4o-mini" },
                KeyHelp = "Clé API de ton compte OpenAI.",
                KeyHelpUrl = "https://platform.openai.com/api-keys",
                BaseUrl = p => "https://api.openai.com/v1",
            },
            new ProviderInfo
            {
                Id = "claude", Name = "Claude", Tagline = "Anthropic · très bon en français",
                Models = new[] { "claude-haiku-5-5", "claude-sonnet-5-5" },
                KeyHelp = "Clé API de la console Anthropic.",
                KeyHelpUrl = "https://console.anthropic.com/settings/keys",
                BaseUrl = p => "https://api.anthropic.com/v1/",
            },
            new ProviderInfo
            {
                Id = "custom", Name = "Autre", Tagline = "Ollama, LM Studio, API compatible",
                Models = new[] { "qwen3:4b", "llama3.2:3b", "mistral-small3.2" },
                NeedsKey = false, KeyHelp = "Souvent inutile en local.",
                BaseUrl = p => string.IsNullOrWhiteSpace(p.BaseUrl) ? "http://localhost:11434/v1" : p.BaseUrl.Trim(),
            },
        };

        public static ProviderInfo Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
    }

    public class VoiceInfo
    {
        public string Id, Label, Language;

        public static readonly VoiceInfo[] Kokoro =
        {
            new VoiceInfo { Language = "fr", Id = "ff_siwis", Label = "Siwis (femme)" },
            new VoiceInfo { Language = "en", Id = "bm_george", Label = "George (britannique, homme)" },
            new VoiceInfo { Language = "en", Id = "bm_lewis", Label = "Lewis (britannique, homme)" },
            new VoiceInfo { Language = "en", Id = "bm_daniel", Label = "Daniel (britannique, homme)" },
            new VoiceInfo { Language = "en", Id = "bf_emma", Label = "Emma (britannique, femme)" },
            new VoiceInfo { Language = "en", Id = "am_michael", Label = "Michael (américain, homme)" },
            new VoiceInfo { Language = "en", Id = "am_adam", Label = "Adam (américain, homme)" },
            new VoiceInfo { Language = "en", Id = "af_heart", Label = "Heart (américaine, femme)" },
        };

        /// <summary>Voix neuronales Microsoft (en ligne, gratuites).</summary>
        public static readonly VoiceInfo[] Neural =
        {
            new VoiceInfo { Language = "fr", Id = "fr-FR-HenriNeural", Label = "Henri (homme)" },
            new VoiceInfo { Language = "fr", Id = "fr-FR-DeniseNeural", Label = "Denise (femme)" },
            new VoiceInfo { Language = "fr", Id = "fr-FR-RemyMultilingualNeural", Label = "Rémy (homme, plus lent à générer)" },
            new VoiceInfo { Language = "fr", Id = "fr-CA-AntoineNeural", Label = "Antoine (québécois, homme)" },
            new VoiceInfo { Language = "en", Id = "en-GB-RyanNeural", Label = "Ryan (britannique, homme)" },
            new VoiceInfo { Language = "en", Id = "en-GB-ThomasNeural", Label = "Thomas (britannique, homme)" },
            new VoiceInfo { Language = "en", Id = "en-GB-SoniaNeural", Label = "Sonia (britannique, femme)" },
            new VoiceInfo { Language = "en", Id = "en-US-AndrewNeural", Label = "Andrew (américain, homme)" },
            new VoiceInfo { Language = "en", Id = "en-AU-WilliamNeural", Label = "William (australien, homme)" },
        };
    }

    public class EngineerSettings
    {
        public string Language { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";

        // IA
        public string Provider { get; set; } = "infomaniak";
        public Dictionary<string, ProviderSettings> Providers { get; set; } = new Dictionary<string, ProviderSettings>();

        // Voix
        public string TtsEngine { get; set; } = "neural"; // neural | kokoro | elevenlabs
        public string NeuralVoiceFr { get; set; } = "fr-FR-HenriNeural";
        public string NeuralVoiceEn { get; set; } = "en-GB-RyanNeural";
        public int SettingsVersion { get; set; } // absent des anciens fichiers = 0
        public string VoiceFr { get; set; } = "ff_siwis";
        public string VoiceEn { get; set; } = "bm_george";
        public double VoiceSpeed { get; set; } = 1.1;
        public string ElevenLabsKey { get; set; } = "";
        public string ElevenLabsVoiceId { get; set; } = "JBFqnCBsd6RMkjVDRZzb";
        public bool RadioEffect { get; set; } = true;
        public string SttModel { get; set; } = "small"; // base = rapide, small = précis

        // Annonces automatiques
        public bool CallLapTime { get; set; } = true;
        public bool CallPersonalBest { get; set; } = true;
        public bool CallGaps { get; set; } = true;
        public bool CallPosition { get; set; } = true;
        public bool CallFuel { get; set; } = true;
        public int FuelWarningLaps { get; set; } = 3;

        public bool AutoStartClient { get; set; } = true;
        public bool PttTested { get; set; }

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

        public static EngineerSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonConvert.DeserializeObject<EngineerSettings>(File.ReadAllText(FilePath, Encoding.UTF8)) ?? new EngineerSettings();
                    s.TransformSecrets(Unprotect);
                    s.Migrate();
                    return s;
                }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn("[AI Race Engineer] réglages illisibles, valeurs par défaut : " + ex.Message);
            }
            return new EngineerSettings { SettingsVersion = 2 };
        }

        public void Save()
        {
            var copy = JsonConvert.DeserializeObject<EngineerSettings>(JsonConvert.SerializeObject(this));
            copy.TransformSecrets(Protect);
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(copy, Formatting.Indented), Encoding.UTF8);
        }

        private void Migrate()
        {
            // 0.2.0 proposait un nom de modèle Infomaniak refusé par l'API
            if (Providers.TryGetValue("infomaniak", out var inf) && inf.Model == "Ministral-3-14B") inf.Model = "mistral3";
            // 0.4.0 : la voix française de Kokoro comprend mal l'anglais de course, les voix neuronales deviennent la voix par défaut
            if (SettingsVersion < 2 && TtsEngine == "kokoro") TtsEngine = "neural";
            SettingsVersion = 2;
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
            return new
            {
                rev,
                language = Language,
                llm = new
                {
                    provider = info.Id,
                    configured,
                    base_url = info.BaseUrl(p),
                    api_key = string.IsNullOrWhiteSpace(p.ApiKey) ? "none" : p.ApiKey.Trim(),
                    model = ModelOrDefault(),
                    max_tokens = 120,
                    temperature = 0.3,
                },
                tts = new
                {
                    engine = TtsEngine,
                    kokoro_voice = Voice,
                    neural_voice = NeuralVoice,
                    kokoro_speed = VoiceSpeed,
                    radio_effect = RadioEffect,
                },
                elevenlabs = new
                {
                    api_key = ElevenLabsKey?.Trim() ?? "",
                    voice_id = ElevenLabsVoiceId?.Trim() ?? "",
                    model = "eleven_flash_v2_5",
                },
                stt = new { model = SttModel, device = "cpu", compute_type = "int8" },
            };
        }
    }
}
