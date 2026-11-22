using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIRaceEngineer
{
    /// <summary>Bouton « Tester » : un petit appel /chat/completions avec la config actuelle.</summary>
    public sealed class LlmTester
    {
        public string Result { get; private set; } = "";
        public bool Busy { get; private set; }

        public void TestAsync(string baseUrl, string apiKey, string model)
        {
            if (Busy) return;
            Busy = true;
            Result = "Test en cours...";
            Task.Run(() =>
            {
                try { Result = Test(baseUrl, apiKey, model); }
                catch (Exception ex) { Result = "Erreur : " + ex.Message; }
                finally { Busy = false; }
            });
        }

        private static string Test(string baseUrl, string apiKey, string model)
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
                    var answer = (string)JObject.Parse(json).SelectToken("choices[0].message.content") ?? "";
                    return $"OK en {ms} ms ({model}) : « {answer.Trim()} »";
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse r)
                {
                    string detail;
                    using (var reader = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                        detail = reader.ReadToEnd();
                    if (detail.Length > 300) detail = detail.Substring(0, 300) + "...";
                    return $"Erreur {(int)r.StatusCode} : {detail}";
                }
            }
        }
    }
}
