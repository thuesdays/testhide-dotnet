using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Testhide.Reporting.Core;

namespace Testhide.Reporting.VSTest
{
    /// <summary>
    /// VSTest logger that emits the Testhide Report Format v1 from <c>dotnet test</c>, regardless
    /// of the underlying framework (xUnit / NUnit / MSTest) — they all funnel results through
    /// VSTest. Enable with:
    ///     dotnet test --logger "testhide;LogFilePath=junittests.xml"
    /// Optional params: SuiteName, JiraUrl/JiraUsername/JiraPassword, meta.KEY=VALUE.
    /// </summary>
    [FriendlyName("testhide")]
    [ExtensionUri("logger://Testhide/TestLogger/v1")]
    public class TesthideTestLogger : ITestLoggerWithParameters
    {
        private static readonly Regex ExcTypeRe =
            new Regex(@"[A-Za-z_][A-Za-z0-9_.]*(?:Exception|Error|Failure)", RegexOptions.Compiled);

        private readonly List<TestCaseRecord> _records = new List<TestCaseRecord>();
        private readonly object _lock = new object();
        private readonly Dictionary<string, string> _metadata = new Dictionary<string, string>();
        private string _reportPath = "junittests.xml";
        private string _suiteName = "dotnet";
        private JiraHelper _jira;

        public void Initialize(TestLoggerEvents events, string testRunDirectory)
        {
            var p = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(testRunDirectory)) p["TestRunDirectory"] = testRunDirectory;
            Initialize(events, p);
        }

        public void Initialize(TestLoggerEvents events, Dictionary<string, string> parameters)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));

            string testRunDir = null;
            if (parameters != null)
            {
                foreach (var kv in parameters)
                {
                    if (kv.Key == "LogFilePath" || kv.Key == "report-xml")
                    {
                        if (!string.IsNullOrEmpty(kv.Value)) _reportPath = kv.Value;
                    }
                    else if (kv.Key == "SuiteName")
                    {
                        if (!string.IsNullOrEmpty(kv.Value)) _suiteName = kv.Value;
                    }
                    else if (kv.Key == "TestRunDirectory")
                    {
                        testRunDir = kv.Value;
                    }
                    else if (kv.Key.StartsWith("meta.", StringComparison.Ordinal))
                    {
                        _metadata[kv.Key.Substring(5)] = kv.Value;
                    }
                }

                string ju = Get(parameters, "JiraUrl");
                string un = Get(parameters, "JiraUsername");
                string pw = Get(parameters, "JiraPassword");
                if (!string.IsNullOrEmpty(ju) && !string.IsNullOrEmpty(un) && !string.IsNullOrEmpty(pw))
                    _jira = new JiraHelper(ju, un, pw);
            }

            if (!Path.IsPathRooted(_reportPath) && !string.IsNullOrEmpty(testRunDir))
                _reportPath = Path.Combine(testRunDir, _reportPath);

            events.TestResult += OnTestResult;
            events.TestRunComplete += OnTestRunComplete;
        }

        private static string Get(Dictionary<string, string> p, string k)
        {
            string v;
            return p.TryGetValue(k, out v) ? v : null;
        }

        private void OnTestResult(object sender, TestResultEventArgs e)
        {
            if (e == null || e.Result == null) return;
            var rec = Map(e.Result);
            lock (_lock) _records.Add(rec);
        }

        private void OnTestRunComplete(object sender, TestRunCompleteEventArgs e)
        {
            List<TestCaseRecord> snapshot;
            lock (_lock) snapshot = new List<TestCaseRecord>(_records);
            try
            {
                ReportWriter.Write(_reportPath, _suiteName, _metadata, snapshot);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[testhide] failed to write report '" + _reportPath + "': " + ex.Message);
            }
        }

        private TestCaseRecord Map(TestResult r)
        {
            var tc = r.TestCase;
            string fqn = (tc != null ? (tc.FullyQualifiedName ?? tc.DisplayName) : null) ?? "Unknown";
            string noParam = StripParams(fqn);

            string module, cls, method, classname;
            SplitFqn(noParam, out module, out cls, out method, out classname);

            var rec = new TestCaseRecord
            {
                Classname = classname,
                Name = method,
                Time = r.Duration.TotalSeconds,
                File = (tc != null ? tc.CodeFilePath : null) ?? "",
                Line = (tc != null && tc.LineNumber > 0)
                    ? tc.LineNumber.ToString(CultureInfo.InvariantCulture) : "",
            };

            if (tc != null && tc.Traits != null)
            {
                foreach (var t in tc.Traits)
                {
                    if (t.Name == "docstr") rec.Docstr = t.Value;
                    else if (t.Name == "jira" || t.Name == "issue") rec.Jira = t.Value;
                    else if (t.Name == "info") rec.Info = t.Value;
                }
            }

            if (r.Attachments != null)
            {
                foreach (var set in r.Attachments)
                {
                    if (set != null && set.Attachments != null)
                        foreach (var a in set.Attachments)
                            if (a != null && a.Uri != null) rec.Attachments.Add(a.Uri.ToString());
                }
            }

            if (r.Messages != null)
            {
                var sb = new StringBuilder();
                foreach (var m in r.Messages)
                    if (m != null && !string.IsNullOrEmpty(m.Text)) sb.Append(m.Text);
                if (sb.Length > 0) rec.SystemOut = sb.ToString();
            }

            switch (r.Outcome)
            {
                case TestOutcome.Passed:
                    rec.Outcome = Outcome.Passed;
                    rec.TestResolution = Resolutions.Passed;
                    rec.FailId = "";
                    break;

                case TestOutcome.Skipped:
                case TestOutcome.None:
                case TestOutcome.NotFound:
                    rec.Outcome = Outcome.Skipped;
                    rec.TestResolution = Resolutions.Skipped;
                    rec.SkipReason = r.ErrorMessage ?? "";
                    rec.FailId = "";
                    break;

                default: // Failed
                    rec.Outcome = Outcome.Failed;
                    rec.Message = r.ErrorMessage ?? "Test failed";
                    rec.Traceback = r.ErrorStackTrace ?? "";
                    string excType = ExtractExcType(rec.Message, rec.Traceback);
                    rec.FailId = FailId.Compute(module, cls, method, excType, rec.Message);
                    rec.TestResolution = Resolutions.Unresolved;
                    if (_jira != null && rec.FailId.Length > 0)
                    {
                        var enr = _jira.Enrich(rec.FailId);
                        if (enr != null)
                        {
                            rec.TestResolution = enr.Resolution;
                            rec.Jira = enr.Reference;
                            rec.Message = enr.Message;
                        }
                    }
                    break;
            }

            return rec;
        }

        private static string StripParams(string s)
        {
            int idx = s.IndexOf('(');
            return idx > 0 ? s.Substring(0, idx) : s;
        }

        private static void SplitFqn(string fqn, out string module, out string cls,
                                     out string method, out string classname)
        {
            var parts = fqn.Split('.');
            if (parts.Length >= 2)
            {
                method = parts[parts.Length - 1];
                cls = parts[parts.Length - 2];
                classname = string.Join(".", parts, 0, parts.Length - 1);
                module = parts.Length >= 3 ? string.Join(".", parts, 0, parts.Length - 2) : "";
            }
            else
            {
                method = fqn;
                cls = "";
                classname = "";
                module = "";
            }
        }

        private static string ExtractExcType(string message, string stack)
        {
            var m = ExcTypeRe.Match(message ?? "");
            if (m.Success) return m.Value;
            m = ExcTypeRe.Match(stack ?? "");
            if (m.Success) return m.Value;
            return "TestFailure";
        }
    }
}
