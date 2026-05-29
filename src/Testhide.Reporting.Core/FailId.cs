using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Testhide.Reporting.Core
{
    /// <summary>
    /// Stable failure key — identical formula to the Testhide pytest/unittest plugins:
    /// <c>md5("module.class.function.ExceptionType(message)")</c>. The backend dedups and
    /// links Jira on this value, so it must match across languages.
    /// </summary>
    public static class FailId
    {
        private static readonly Regex ParamSuffix = new Regex(@"\(.*\)$", RegexOptions.Compiled);

        public static string Compute(string module, string cls, string func, string excType, string message)
        {
            func = ParamSuffix.Replace(func ?? string.Empty, string.Empty);
            string raw = string.Format("{0}.{1}.{2}.{3}({4})",
                module ?? string.Empty,
                string.IsNullOrEmpty(cls) ? (module ?? string.Empty) : cls,
                func,
                excType ?? string.Empty,
                message ?? string.Empty);

            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
