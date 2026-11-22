using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIRaceEngineer
{
    /// <summary>
    /// Bouton « Tester la connexion » : un petit appel /chat/completions avec la config actuelle,
    /// traduit en message clair. Si l'API refuse le modèle en listant ceux qu'elle accepte, on les propose.
    /// </summary>
    public sealed class LlmTester
    {
        public bool Busy { get; private set; }
        public bool? Ok { get; private set; }          // null : pas encore testé / en cours
        public string Message { get; private set; } = "";
        public string[] Suggestions { get; private set; } = new string[0];
        public int Revision { get; private set; }

        public void TestAsync(ProviderInfo info, ProviderSettings p, string model)
        {
            if (Busy) return;

            string missing = null;
            if (info.NeedsKey && string.IsNullOrWhiteSpace(p.ApiKey)) missing = "Colle d'abord ta clé API.";
            else if (info.AccountLabel != null && string.IsNullOrWhiteSpace(p.AccountId)) missing = $"Renseigne d'abord le {info.AccountLabel}.";
            if (missing != null) { Publish(false, missing, null); return; }

            Busy = true;
            Publish(null, "Test en cours...", null);
            var baseUrl = info.BaseUrl(p);
            var key = string.IsNullOrWhiteSpace(p.ApiKey) ? "none" : p.ApiKey.Trim();
            Task.Run(() =>
            {
                try { Test(info, baseUrl, key, model); }
                catch (Exception ex) { Publish(false, "Erreur : " + ex.Message, null); }
                finally { Busy = false; }
            });
        }

        private void Publish(bool? ok, string message, string[] suggestions)
        {
            Ok = ok;
            Message = message;
            Suggestions = suggestions ?? new string[0];
            Revision++;
        }

        private void Test(ProviderInfo info, string baseUrl, string apiKey, string model)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var body = JsonConvert.SerializeObject(new
            {
                model,
                messages = new[] { new { role = "user", content = "Réponds seulement : OK" } },
                max_tokens = 10,
            });
            var started = DateTime.UtcNow;
            using (var wc = new WebClient { Encoding = Encoding.UTF8 })
            {
                wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
                try
                {
                    var json = wc.UploadString(baseUrl.TrimEnd('/') + "/chat/completions", body);
                    var ms = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                    var answer = ((string)JObject.Parse(json).SelectToken("choices[0].message.content") ?? "").Trim();
                    Publish(true, $"Connexion OK · {model} · réponse en {ms} ms" + (answer.Length > 0 && answer.Length < 40 ? $" : « {answer} »" : ""), null);
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse r)
                {
                    string detail;
                    using (var reader = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                        detail = reader.ReadToEnd();
                    int code = (int)r.StatusCode;
                    var allowed = AllowedValues(detail);
                    if (allowed.Length > 0)
                        Publish(false, $"Le modèle « {model} » n'est pas accepté par {info.Name}. Choisis-en un :", allowed);
                    else if (code == 401 || code == 403)
                        Publish(false, $"Clé API refusée ({code}). Vérifie la clé" +
                                       (info.Id == "infomaniak" ? " et qu'elle a le scope « ai-tools »." : "."), null);
                    else if (code == 404)
                        Publish(false, "Adresse introuvable (404)" + (info.AccountLabel != null ? $" : vérifie le {info.AccountLabel}." : "."), null);
                    else if (code == 429)
                        Publish(false, "Quota dépassé (429). Réessaie plus tard ou change de fournisseur.", null);
                    else
                        Publish(false, $"Erreur {code} : {Short(Describe(detail))}", null);
                }
                catch (WebException ex)
                {
                    Publish(false, "Connexion impossible : " + ex.Message, null);
                }
            }
        }

        /// <summary>Listes de valeurs autorisées dans une erreur de validation (ex. Infomaniak : "values": [...]).</summary>
        internal static string[] AllowedValues(string body)
        {
            try
            {
                return JToken.Parse(body).SelectTokens("$..values").Concat(JToken.Parse(body).SelectTokens("$..allowed_values"))
                    .OfType<JArray>()
                    .SelectMany(a => a.Where(t => t.Type == JTokenType.String).Select(t => (string)t))
                    .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length < 80)
                    .Distinct().Take(12).ToArray();
            }
            catch { return new string[0]; }
        }

        private static string Describe(string body)
        {
            try
            {
                var t = JToken.Parse(body);
                var msg = t.SelectTokens("$..description").Concat(t.SelectTokens("$..message"))
                           .FirstOrDefault(x => x.Type == JTokenType.String);
                if (msg != null) return (string)msg;
            }
            catch { }
            return body;
        }

        private static string Short(string s) => s.Length > 220 ? s.Substring(0, 220) + "..." : s;
    }
}
