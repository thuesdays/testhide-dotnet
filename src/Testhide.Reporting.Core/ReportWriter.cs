using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;

namespace Testhide.Reporting.Core
{
    /// <summary>
    /// Serializes a set of <see cref="TestCaseRecord"/> into the Testhide Report Format v1
    /// (JUnit-extended dialect) the agent's ParseJUnit reads. See REPORT-FORMAT-V1.md.
    /// </summary>
    public static class ReportWriter
    {
        public const string SchemaVersion = "1";

        public static void Write(string reportPath, string suiteName,
                                 IDictionary<string, string>? metadata,
                                 IList<TestCaseRecord> records)
        {
            reportPath = Path.GetFullPath(reportPath);
            string dir = Path.GetDirectoryName(reportPath) ?? ".";
            if (dir.Length > 0) Directory.CreateDirectory(dir);

            int failures = 0, errors = 0, skipped = 0;
            double totalTime = 0;
            foreach (var r in records)
            {
                totalTime += r.Time;
                switch (r.Outcome)
                {
                    case Outcome.Failed:
                    case Outcome.XFail: failures++; break;
                    case Outcome.Error: errors++; break;
                    case Outcome.Skipped: skipped++; break;
                }
            }

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "\t",
                Encoding = new UTF8Encoding(false),
                NewLineHandling = NewLineHandling.Entitize,
            };

            string tmp = reportPath + ".tmp";
            using (var w = XmlWriter.Create(tmp, settings))
            {
                w.WriteStartDocument();
                w.WriteStartElement("testsuites");
                w.WriteStartElement("testsuite");
                w.WriteAttributeString("name", suiteName);
                w.WriteAttributeString("timestamp",
                    DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "Z");
                w.WriteAttributeString("hostname", Hostname());
                w.WriteAttributeString("tests", records.Count.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("failures", failures.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("errors", errors.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("skipped", skipped.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("time", totalTime.ToString("0.000", CultureInfo.InvariantCulture));

                // suite properties
                w.WriteStartElement("properties");
                WriteProperty(w, "testhide_schema_version", SchemaVersion);
                WriteProperty(w, "ip_address", IpAddress());
                WriteProperty(w, "hostname", Hostname());
                if (metadata != null)
                    foreach (var kv in metadata)
                        WriteProperty(w, kv.Key, kv.Value);
                w.WriteEndElement(); // properties

                foreach (var r in records)
                    WriteCase(w, r);

                w.WriteEndElement(); // testsuite
                w.WriteEndElement(); // testsuites
                w.WriteEndDocument();
            }
            // atomic replace
            if (File.Exists(reportPath)) File.Delete(reportPath);
            File.Move(tmp, reportPath);
        }

        private static void WriteCase(XmlWriter w, TestCaseRecord r)
        {
            w.WriteStartElement("testcase");
            w.WriteAttributeString("classname", r.Classname ?? "");
            w.WriteAttributeString("name", r.Name ?? "");
            w.WriteAttributeString("file", r.File ?? "");
            w.WriteAttributeString("line", r.Line ?? "");
            w.WriteAttributeString("time", r.Time.ToString("0.000", CultureInfo.InvariantCulture));
            w.WriteAttributeString("fail_id", r.FailId ?? "");
            w.WriteAttributeString("test_resolution",
                string.IsNullOrEmpty(r.TestResolution) ? Resolutions.Unresolved : r.TestResolution);

            if (r.Outcome == Outcome.Failed || r.Outcome == Outcome.XFail)
            {
                w.WriteStartElement("failure");
                w.WriteAttributeString("message", Clean(r.Message));
                if (!string.IsNullOrEmpty(r.Traceback)) w.WriteCData(SanitizeCData(r.Traceback));
                w.WriteEndElement();
            }
            else if (r.Outcome == Outcome.Error)
            {
                w.WriteStartElement("error");
                w.WriteAttributeString("message", Clean(r.Message));
                if (!string.IsNullOrEmpty(r.Traceback)) w.WriteCData(SanitizeCData(r.Traceback));
                w.WriteEndElement();
            }
            else if (r.Outcome == Outcome.Skipped)
            {
                w.WriteStartElement("skipped");
                w.WriteAttributeString("type", "skip");
                w.WriteAttributeString("message", Clean(r.SkipReason.Length > 0 ? r.SkipReason : r.Message));
                string txt = r.Traceback.Length > 0 ? r.Traceback : r.SkipReason;
                if (!string.IsNullOrEmpty(txt)) w.WriteString(Clean(txt));
                w.WriteEndElement();
            }

            var props = new List<KeyValuePair<string, string>>();
            if (!string.IsNullOrEmpty(r.Docstr)) props.Add(new KeyValuePair<string, string>("docstr", r.Docstr!));
            foreach (var a in r.Attachments)
                if (!string.IsNullOrEmpty(a)) props.Add(new KeyValuePair<string, string>("attachment", a));
            if (!string.IsNullOrEmpty(r.Info)) props.Add(new KeyValuePair<string, string>("info", r.Info!));
            if (!string.IsNullOrEmpty(r.Jira)) props.Add(new KeyValuePair<string, string>("jira", r.Jira!));
            if (props.Count > 0)
            {
                w.WriteStartElement("properties");
                foreach (var p in props) WriteProperty(w, p.Key, p.Value);
                w.WriteEndElement();
            }

            if (!string.IsNullOrEmpty(r.SystemOut))
            {
                w.WriteStartElement("system-out");
                w.WriteString(Clean(r.SystemOut!));
                w.WriteEndElement();
            }

            w.WriteEndElement(); // testcase
        }

        private static void WriteProperty(XmlWriter w, string name, string value)
        {
            w.WriteStartElement("property");
            w.WriteAttributeString("name", name ?? "");
            w.WriteAttributeString("value", Clean(value));
            w.WriteEndElement();
        }

        // Strip XML-1.0-illegal control chars (XmlWriter would otherwise throw). Keeps BMP text.
        private static string Clean(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s!.Length);
            foreach (char c in s)
            {
                int code = c;
                if (code == 0x09 || code == 0x0A || code == 0x0D ||
                    (code >= 0x20 && code <= 0xD7FF) || (code >= 0xE000 && code <= 0xFFFD))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        // CData can't contain the "]]>" sequence; split it, and strip illegal chars.
        private static string SanitizeCData(string s)
        {
            return Clean(s).Replace("]]>", "]]]]><![CDATA[>");
        }

        private static string Hostname()
        {
            try { return Dns.GetHostName(); } catch { return "unknown"; }
        }

        private static string IpAddress()
        {
            try
            {
                foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                        return ip.ToString();
            }
            catch { }
            return "0.0.0.0";
        }
    }
}
