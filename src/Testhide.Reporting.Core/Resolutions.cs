namespace Testhide.Reporting.Core
{
    /// <summary>The v1 closed set of test_resolution values (see REPORT-FORMAT-V1.md §3).</summary>
    public static class Resolutions
    {
        public const string Passed = "Passed";
        public const string Skipped = "Skipped";
        public const string CollectionError = "Collection Error";
        public const string TeardownError = "Teardown Error";
        public const string KnownIssue = "Known Issue";
        public const string NeedToReopen = "Need to reopen";
        public const string ResolvedInBranch = "Resolved in branch";
        public const string VerifiedAtBranch = "Verified at Branch";
        public const string Unresolved = "Unresolved";
    }
}
