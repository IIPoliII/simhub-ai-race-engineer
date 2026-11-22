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
        public string Id, Label, DefaultModel, AccountLabel, KeyHelpUrl;
        public Func<ProviderSettings, string> BaseUrl;
        public bool NeedsKey = true;

        public static readonly ProviderInfo[] All =
        {
            new ProviderInfo
            {
                Id = "infomaniak", Label = "Infomaniak (Suisse, 1M de crédits offerts)", DefaultModel = "Ministral-3-14B",
                AccountLabel = "Product ID", KeyHelpUrl = "https://www.infomaniak.com/fr/hebergement/ai-tools",
                BaseUrl = p => $"https://api.infomaniak.com/1/ai/{p.AccountId.Trim()}/openai",
            },
            new ProviderInfo
            {
                Id = "cloudflare", Label = "Cloudflare Workers AI (quota gratuit par jour)", DefaultModel = "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
                AccountLabel = "Account ID", KeyHelpUrl = "https://dash.cloudflare.com/profile/api-tokens",
                BaseUrl = p => $"https://api.cloudflare.com/client/v4/accounts/{p.AccountId.Trim()}/ai/v1",
            },
            new ProviderInfo
            {
                Id = "openai", Label = "OpenAI", DefaultModel = "gpt-4.1-mini",
                KeyHelpUrl = "https://platform.openai.com/api-keys",
                BaseUrl = p => "https://api.openai.com/v1",
            },
            new ProviderInfo
            {
                Id = "claude", Label = "Claude (Anthropic)", DefaultModel = "claude-haiku-5-5",
                KeyHelpUrl = "https://console.anthropic.com/settings/keys",
                BaseUrl = p => "https://api.anthropic.com/v1/",
            },
            new ProviderInfo
            {
                Id = "custom", Label = "Autre (compatible OpenAI, ex. Ollama en local)", DefaultModel = "qwen3:4b",
                NeedsKey = false,
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
    }

    public class EngineerSettings
    {
        public string Language { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";

        // IA
        public string Provider { get; set; } = "infomaniak";
        public Dictionary<string, ProviderSettings> Providers { get; set; } = new Dictionary<string, ProviderSettings>();

        // Voix
        public string TtsEngine { get; set; } = "kokoro"; // kokoro | elevenlabs
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

        public ProviderSettings Current(string id = null)
        {
            id = id ?? Provider;
            if (!Providers.TryGetValue(id, out var p))
                Providers[id] = p = new ProviderSettings { Model = ProviderInfo.Get(id).DefaultModel };
            return p;
        }

        public string Voice => Language == "fr" ? VoiceFr : VoiceEn;

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
                    return s;
                }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn("[AI Race Engineer] réglages illisibles, valeurs par défaut : " + ex.Message);
            }
            return new EngineerSettings();
        }

        public void Save()
        {
            var copy = JsonConvert.DeserializeObject<EngineerSettings>(JsonConvert.SerializeObject(this));
            copy.TransformSecrets(Protect);
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(copy, Formatting.Indented), Encoding.UTF8);
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
            bool configured = !info.NeedsKey || (!string.IsNullOrWhiteSpace(p.ApiKey) &&
                              (info.AccountLabel == null || !string.IsNullOrWhiteSpace(p.AccountId)));
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
                    model = string.IsNullOrWhiteSpace(p.Model) ? info.DefaultModel : p.Model.Trim(),
                    max_tokens = 120,
                    temperature = 0.3,
                },
                tts = new
                {
                    engine = TtsEngine,
                    kokoro_voice = Voice,
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
