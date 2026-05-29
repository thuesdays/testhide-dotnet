using System.Collections.Generic;

namespace Testhide.Reporting.Core
{
    public enum Outcome
    {
        Passed,
        Failed,
        Error,
        Skipped,
        XFail, // expected failure that did fail -> emitted as <failure> + "Known Issue"
    }

    /// <summary>One test result, ready to be serialized into a Testhide v1 &lt;testcase&gt;.</summary>
    public sealed class TestCaseRecord
    {
        public string Classname = "";
        public string Name = "";
        public double Time;
        public Outcome Outcome = Outcome.Passed;
        public string File = "";
        public string Line = "";
        public string FailId = "";
        public string TestResolution = "Unresolved";
        public string Message = "";
        public string Traceback = "";
        public string SkipReason = "";
        public string? Docstr;
        public string? Info;
        public string? Jira;
        public List<string> Attachments = new List<string>();
        public string? SystemOut;
    }
}
