using System;
using System.Collections.Generic;
using System.Text;

namespace Wagenheimer.Tracking
{
    /// <summary>Pure helpers that keep event and parameter names and values acceptable to Unity Analytics.</summary>
    public static class AnalyticsNames
    {
        private const int MaxNameLength = 64;
        private const int MaxStringLength = 255;

        /// <summary>
        /// Letters, digits and underscore only, no leading digit, at most 64 characters. Spaces and other
        /// separators become an underscore, so <c>"Level Completed"</c> reports as <c>Level_Completed</c>.
        /// </summary>
        public static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unnamed";

            var builder = new StringBuilder(Math.Min(name.Length, MaxNameLength));
            foreach (var c in name.Trim())
            {
                if (builder.Length >= MaxNameLength) break;
                builder.Append(char.IsLetterOrDigit(c) && c < 128 || c == '_' ? c : '_');
            }

            if (builder.Length == 0) return "unnamed";
            if (char.IsDigit(builder[0])) builder.Insert(0, '_');
            return builder.ToString();
        }

        /// <summary>
        /// Converts a value to a type Unity Analytics accepts (string, int, long, float, double, bool).
        /// Returns null for null, which means "skip this parameter".
        /// </summary>
        public static object NormalizeValue(object value)
        {
            switch (value)
            {
                case null: return null;
                case string s: return s.Length > MaxStringLength ? s.Substring(0, MaxStringLength) : s;
                case int _:
                case long _:
                case float _:
                case double _:
                case bool _:
                    return value;
                case short s16: return (int)s16;
                case byte b: return (int)b;
                case uint u: return (long)u;
                case decimal m: return (double)m;
                case Enum e: return e.ToString();
                default: return value.ToString();
            }
        }

        /// <summary>One-line "key=value" summary for logging.</summary>
        public static string Describe(IDictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0) return string.Empty;

            var builder = new StringBuilder(" {");
            var first = true;
            foreach (var pair in parameters)
            {
                if (!first) builder.Append(", ");
                builder.Append(pair.Key).Append('=').Append(pair.Value);
                first = false;
            }

            return builder.Append('}').ToString();
        }
    }
}
