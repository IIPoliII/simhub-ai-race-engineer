using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AIRaceEngineer
{
    /// <summary>
    /// Récupère les modèles de chat disponibles chez le fournisseur choisi, pour remplir la liste « Modèle ».
    /// Les modèles conseillés (ProviderInfo.Models) restent en tête, le reste suit par ordre alphabétique.
    /// </summary>
    public sealed class ModelCatalog
    {
        public bool Busy { get; private set; }
        public string[] Models { get; private set; } = new string[0];
        private Msg _error;
        public string Error => _error;
        public string Key { get; private set; }         // fournisseur + compte + clé de la dernière liste
        public int Revision { get; private set; }

        public static string KeyFor(ProviderInfo info, ProviderSettings p) =>
            info.Id + "|" + (p.AccountId ?? "").Trim() + "|" + (p.ApiKey ?? "").Trim() + "|" + info.BaseUrl(p);

        public static bool CanFetch(ProviderInfo info, ProviderSettings p) =>
            (!info.NeedsKey || !string.IsNullOrWhiteSpace(p.ApiKey)) &&
            (info.AccountLabel == null || !string.IsNullOrWhiteSpace(p.AccountId));

        /// <summary>Lance la récupération si la liste n'est pas déjà à jour pour ce fournisseur et cette clé.</summary>
        public void Refresh(ProviderInfo info, ProviderSettings p, bool force = false)
        {
            if (Busy || !CanFetch(info, p)) return;
            var key = KeyFor(info, p);
            if (!force && key == Key && Error == null) return;

            Busy = true;
            var baseUrl = info.BaseUrl(p).TrimEnd('/');
            var apiKey = string.IsNullOrWhiteSpace(p.ApiKey) ? "none" : p.ApiKey.Trim();
            var account = (p.AccountId ?? "").Trim();
            Task.Run(() =>
            {
                string[] models = null;
                Msg error = null;
                try { models = Fetch(info, baseUrl, apiKey, account); }
                catch (WebException ex) when (ex.Response is HttpWebResponse r) { error = new Msg($"liste des modèles indisponible ({(int)r.StatusCode})", $"model list unavailable ({(int)r.StatusCode})"); }
                catch (Exception ex) { error = new Msg("liste des modèles indisponible : " + ex.Message, "model list unavailable: " + ex.Message); }

                Models = Order(info, models ?? new string[0]);
                _error = error;
                Key = key;
                Busy = false;
                Revision++;
            });
        }

        private static string[] Fetch(ProviderInfo info, string baseUrl, string apiKey, string account)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var wc = new WebClient { Encoding = Encoding.UTF8 })
            {
                switch (info.Id)
                {
                    case "infomaniak":
                        // Le catalogue indique le type de chaque modèle : on ne garde que les LLM
                        wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
                        return JObject.Parse(wc.DownloadString("https://api.infomaniak.com/1/ai/models"))["data"]
                            .Where(m => (string)m["type"] == "llm")
                            .Select(m => (string)m["name"]).ToArray();

                    case "cloudflare":
                        wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
                        return JObject.Parse(wc.DownloadString(
                                $"https://api.cloudflare.com/client/v4/accounts/{account}/ai/models/search?task=Text%20Generation&per_page=100"))["result"]
                            .Select(m => (string)m["name"]).ToArray();

                    case "claude":
                        wc.Headers["x-api-key"] = apiKey;
                        wc.Headers["anthropic-version"] = "2023-06-01";
                        return JObject.Parse(wc.DownloadString("https://api.anthropic.com/v1/models?limit=100"))["data"]
                            .Select(m => (string)m["id"]).ToArray();

                    default:
                        // API compatible OpenAI (OpenAI, Ollama, LM Studio...)
                        wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
                        var ids = JObject.Parse(wc.DownloadString(baseUrl + "/models"))["data"].Select(m => (string)m["id"]);
                        return info.Id == "openai" ? ids.Where(IsOpenAiChat).ToArray() : ids.ToArray();
                }
            }
        }

        private static readonly Regex NotChat = new Regex("audio|realtime|transcribe|tts|image|search|embedding|moderation|dall-e|whisper|davinci|babbage|codex|computer",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool IsOpenAiChat(string id) =>
            (id.StartsWith("gpt-") || Regex.IsMatch(id, "^o[0-9]")) && !NotChat.IsMatch(id);

        private static string[] Order(ProviderInfo info, IEnumerable<string> fetched)
        {
            var set = new HashSet<string>(fetched.Where(m => !string.IsNullOrWhiteSpace(m)), StringComparer.Ordinal);
            if (set.Count == 0) return new string[0];
            // Les conseillés d'abord
            var head = info.Models.ToList();
            return head.Concat(set.Where(m => !head.Contains(m)).OrderBy(m => m, StringComparer.OrdinalIgnoreCase)).ToArray();
        }
    }
}
