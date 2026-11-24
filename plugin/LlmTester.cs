using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
        private Msg _message;
        public string Message => _message?.ToString() ?? "";
        public string[] Suggestions { get; private set; } = new string[0];
        public int Revision { get; private set; }

        public void TestAsync(ProviderInfo info, ProviderSettings p, string model)
        {
            if (Busy) return;

            Msg missing = null;
            if (info.NeedsKey && string.IsNullOrWhiteSpace(p.ApiKey)) missing = new Msg("Colle d'abord ta clé API.", "Paste your API key first.");
            else if (info.AccountLabel != null && string.IsNullOrWhiteSpace(p.AccountId)) missing = new Msg($"Renseigne d'abord le champ {info.AccountLabel}.", $"Fill in the {info.AccountLabel} first.");
            if (missing != null) { Publish(false, missing, null); return; }

            Busy = true;
            Publish(null, new Msg("Test en cours...", "Testing..."), null);
            var baseUrl = info.BaseUrl(p);
            var key = string.IsNullOrWhiteSpace(p.ApiKey) ? "none" : p.ApiKey.Trim();
            Task.Run(() =>
            {
                try { Test(info, baseUrl, key, model); }
                catch (Exception ex) { Publish(false, new Msg("Erreur : " + ex.Message, "Error: " + ex.Message), null); }
                finally { Busy = false; }
            });
        }

        private void Publish(bool? ok, Msg message, string[] suggestions)
        {
            Ok = ok;
            _message = message;
            Suggestions = suggestions ?? new string[0];
            Revision++;
        }

        private void Test(ProviderInfo info, string baseUrl, string apiKey, string model)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            // Même requête que le client vocal (marge, temperature, raisonnement) : un test réussi veut dire que les questions passeront
            var req = info.RequestFor(model);
            var request = new JObject
            {
                ["model"] = model,
                ["messages"] = new JArray(new JObject { ["role"] = "user", ["content"] = "Réponds seulement : OK" }),
                [req.MaxTokensField] = req.MaxTokens,
            };
            if (req.Temperature != null) request["temperature"] = req.Temperature.Value;
            if (req.ReasoningEffort != null) request["reasoning_effort"] = req.ReasoningEffort;
            var body = request.ToString(Formatting.None);
            var started = DateTime.UtcNow;
            using (var wc = new WebClient { Encoding = Encoding.UTF8 })
            {
                wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
                try
                {
                    string json;
                    try { json = wc.UploadString(baseUrl.TrimEnd('/') + "/chat/completions", body); }
                    catch (WebException ex) when (req.ReasoningEffort != null && ex.Response is HttpWebResponse bad && (int)bad.StatusCode == 400
                                                  && Regex.IsMatch(ReadBody(bad), "reasoning|think", RegexOptions.IgnoreCase))
                    {
                        // Serveur qui ne connaît pas reasoning_effort (ou modèle sans réflexion qui le refuse) : sans, comme le client vocal
                        request.Remove("reasoning_effort");
                        started = DateTime.UtcNow;
                        json = wc.UploadString(baseUrl.TrimEnd('/') + "/chat/completions", request.ToString(Formatting.None));
                    }
                    var ms = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                    var answer = ((string)JObject.Parse(json).SelectToken("choices[0].message.content") ?? "").Trim();
                    if (answer.Length == 0)
                    {
                        // Sur un PC (Ollama), on nomme les petits modèles qui répondent sans réfléchir
                        bool local = info.Id == "custom";
                        string fr = local ? $" : {info.Models[0]} ou {info.Models[1]} (ollama pull {info.Models[0]})." : ".";
                        string en = local ? $": {info.Models[0]} or {info.Models[1]} (ollama pull {info.Models[0]})." : ".";
                        // En local, on a déjà demandé de ne pas réfléchir (reasoning_effort « none ») : ce modèle ne sait pas s'en passer
                        string asked = local ? " et ne sait pas répondre sans réfléchir" : "";
                        string askedEn = local ? " and can't answer without thinking" : "";
                        Publish(false, new Msg($"{model} répond, mais sa réponse est vide : c'est un modèle qui réfléchit avant de parler{asked}, trop lent pour la radio. Choisis-en un plus direct" + fr,
                                               $"{model} answers, but its reply is empty: it is a model that thinks before speaking{askedEn}, too slow for the radio. Pick a more direct one" + en), null);
                    }
                    else
                        Publish(true, new Msg($"Connexion OK · {model} · réponse en {ms} ms" + (answer.Length < 40 ? $" : « {answer} »" : ""),
                                              $"Connection OK · {model} · answered in {ms} ms" + (answer.Length < 40 ? $": “{answer}”" : "")), null);
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse r)
                {
                    string detail = ReadBody(r);
                    int code = (int)r.StatusCode;
                    var allowed = AllowedValues(detail);
                    if (allowed.Length > 0)
                        Publish(false, new Msg($"Le modèle « {model} » n'est pas accepté par {info.Name.FrText}. Choisis-en un :", $"{info.Name.EnText} does not accept the model “{model}”. Pick one:"), allowed);
                    else if (code == 401 || code == 403)
                        Publish(false, new Msg($"Clé API refusée ({code}). Vérifie la clé" + (info.Id == "infomaniak" ? " et qu'elle a le scope « ai-tools »." : "."),
                                               $"API key rejected ({code}). Check the key" + (info.Id == "infomaniak" ? " and that it has the ai-tools scope." : ".")), null);
                    else if (code == 404 && ModelError(detail) is string why)
                    {
                        // Modèle retiré ou pas installé : l'adresse est bonne, on le dit avec le message du fournisseur
                        var others = Recommended(info, model);
                        bool pick = others != null && others.Length > 0;
                        bool local = info.Id == "custom";
                        Publish(false, new Msg($"Le modèle « {model} » " + (local ? "est introuvable sur ce serveur" : $"n'est pas disponible chez {info.Name.FrText}") + $" : {Sentence(Short(why))}" + (pick ? " Essaie un modèle conseillé :" : ""),
                                               $"The model “{model}” " + (local ? "was not found on this server" : $"is not available at {info.Name.EnText}") + $": {Sentence(Short(why))}" + (pick ? " Try a recommended model:" : "")),
                                others);
                    }
                    else if (code == 404)
                        Publish(false, new Msg("Adresse introuvable (404)" + (info.AccountLabel != null ? $" : vérifie le champ {info.AccountLabel}." : info.Id == "custom" ? $" : vérifie l'URL de l'API ({baseUrl})." : "."),
                                               "Address not found (404)" + (info.AccountLabel != null ? $": check the {info.AccountLabel}." : info.Id == "custom" ? $": check the API URL ({baseUrl})." : ".")), null);
                    else if (code == 429)
                        Publish(false, new Msg("Quota dépassé (429). Réessaie plus tard ou change de fournisseur.", "Quota exceeded (429). Try again later or switch provider."), null);
                    else
                        Publish(false, new Msg($"Erreur {code} : {Short(Describe(detail))}", $"Error {code}: {Short(Describe(detail))}"), null);
                }
                catch (WebException ex)
                {
                    Publish(false, new Msg("Connexion impossible : " + ex.Message, "Cannot connect: " + ex.Message), null);
                }
            }
        }

        // Corps de la dernière réponse d'erreur : le flux ne se lit qu'une fois, et un test ne tourne qu'à la fois (Busy)
        private HttpWebResponse _bodyOf;
        private string _body;

        private string ReadBody(HttpWebResponse r)
        {
            if (ReferenceEquals(r, _bodyOf)) return _body;
            string body;
            try { using (var reader = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) body = reader.ReadToEnd(); }
            catch (Exception) { body = ""; }
            _bodyOf = r;
            return _body = body;
        }

        /// <summary>Listes de valeurs autorisées dans une erreur de validation (ex. Infomaniak : "values": [...]).</summary>
        internal static string[] AllowedValues(string body)
        {
            try
            {
                var root = JToken.Parse(body);
                var tokens = new System.Collections.Generic.List<JToken> { root };
                // Infomaniak v2 : l'erreur d'origine est un JSON glissé dans error.message
                foreach (var str in root.SelectTokens("$..message").Where(t => t.Type == JTokenType.String).Select(t => (string)t))
                {
                    int i = str.IndexOf('{');
                    if (i >= 0) try { tokens.Add(JToken.Parse(str.Substring(i))); } catch { }
                }
                return tokens.SelectMany(t => t.SelectTokens("$..values").Concat(t.SelectTokens("$..allowed_values")))
                    .OfType<JArray>()
                    .SelectMany(a => a.Where(t => t.Type == JTokenType.String).Select(t => (string)t))
                    .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length < 80)
                    .Distinct().Take(16).ToArray();
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

        /// <summary>Message d'une erreur qui parle du modèle (OpenAI : model_not_found ; Claude, Ollama : « model ... »), sinon null.</summary>
        private static string ModelError(string body)
        {
            try
            {
                var t = JToken.Parse(body);
                var msg = (string)t.SelectTokens("$..message").FirstOrDefault(x => x.Type == JTokenType.String);
                var code = (string)t.SelectTokens("$..code").FirstOrDefault(x => x.Type == JTokenType.String);
                if (code == "model_not_found" || (msg != null && msg.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0))
                    return string.IsNullOrWhiteSpace(msg) ? code : msg.Trim();
            }
            catch { }
            return null;
        }

        /// <summary>Modèles conseillés à proposer d'un clic (pas pour une IA sur le PC : il faut d'abord les télécharger).</summary>
        private static string[] Recommended(ProviderInfo info, string model) =>
            info.Id == "custom" ? null : info.Models.Where(m => m != model).ToArray();

        private static string Sentence(string s) => s.EndsWith(".") ? s : s + ".";

        private static string Short(string s) => s.Length > 220 ? s.Substring(0, 220) + "..." : s;
    }
}
