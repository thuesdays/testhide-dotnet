using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Testhide.Reporting.VSTest
{
    public sealed class JiraEnrichment
    {
        public string Resolution;
        public string Reference;
        public string Message;
    }

    /// <summary>
    /// Optional Jira enrichment (parity with the pytest plugin), so offline agents work without
    /// the server. Looks up an issue by fail_id (used as a label) and maps its status to a
    /// Testhide test_resolution. Dependency-free: a best-effort REST call with minimal JSON
    /// scraping — any failure degrades to a no-op (returns null).
    /// </summary>
    public sealed class JiraHelper
    {
        private readonly HttpClient _http;
        private readonly string _base;
        private readonly bool _ok;

        public JiraHelper(string url, string user, string token)
        {
            try
            {
                _base = url.TrimEnd('/');
                _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + token));
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
                _ok = true;
            }
            catch
            {
                _ok = false;
            }
        }

        public JiraEnrichment Enrich(string failId)
        {
            if (!_ok || string.IsNullOrEmpty(failId)) return null;
            try
            {
                string jql = Uri.EscapeDataString("labels = \"" + failId + "\" ORDER BY updated DESC");
                string url = _base + "/rest/api/2/search?maxResults=1&fields=summary,status,fixVersions&jql=" + jql;
                var resp = _http.GetAsync(url).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode) return null;
                string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                string key = Extract(body, "\"key\":\"");
                if (string.IsNullOrEmpty(key)) return null;
                string summary = Extract(body, "\"summary\":\"") ?? "";
                string status = ExtractStatus(body) ?? "";
                bool hasFix = body.Contains("\"fixVersions\":[{");

                string s = status.ToLowerInvariant();
                string resolution;
                if (s == "done" || s == "closed" || s == "resolved")
                    resolution = hasFix ? "Resolved in branch" : "Verified at Branch";
                else if (s == "reopened" || s == "to do" || s == "open")
                    resolution = "Need to reopen";
                else
                    resolution = "Known Issue";

                return new JiraEnrichment
                {
                    Resolution = resolution,
                    Reference = key + " " + resolution + " [" + summary + "]",
                    Message = key + ": " + summary,
                };
            }
            catch
            {
                return null;
            }
        }

        private static string Extract(string body, string token)
        {
            int i = body.IndexOf(token, StringComparison.Ordinal);
            if (i < 0) return null;
            i += token.Length;
            int j = body.IndexOf('"', i);
            return j > i ? body.Substring(i, j - i) : null;
        }

        private static string ExtractStatus(string body)
        {
            int i = body.IndexOf("\"status\":", StringComparison.Ordinal);
            if (i < 0) return null;
            int n = body.IndexOf("\"name\":\"", i, StringComparison.Ordinal);
            if (n < 0) return null;
            n += "\"name\":\"".Length;
            int j = body.IndexOf('"', n);
            return j > n ? body.Substring(n, j - n) : null;
        }
    }
}
