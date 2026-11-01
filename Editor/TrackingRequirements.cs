using System;
using System.Collections.Generic;

namespace Wagenheimer.Tracking.Editor
{
    /// <summary>Severity of a project-supplied requirement.</summary>
    public enum TrackingRequirementStatus
    {
        Pass,
        Warning,
        Fail,
        Info
    }

    /// <summary>
    /// One project-specific ATT requirement (for example "the iOS Free build profile ships ads and must
    /// declare tracking"). The tracking package stays ad-SDK agnostic: the game registers a provider and
    /// the checker, the report and the build gate reuse the result.
    /// </summary>
    public sealed class TrackingRequirement
    {
        public TrackingRequirement(string title, TrackingRequirementStatus status, string detail = null)
        {
            Title = title ?? string.Empty;
            Status = status;
            Detail = detail ?? string.Empty;
        }

        public string Title { get; }
        public TrackingRequirementStatus Status { get; }
        public string Detail { get; }

        public string FixLabel { get; private set; }

        public Action Fix { get; private set; }

        public List<string> Facts { get; } = new List<string>();

        public TrackingRequirement WithFacts(params string[] facts)
        {
            if (facts != null)
            {
                foreach (var fact in facts)
                {
                    if (!string.IsNullOrEmpty(fact))
                        Facts.Add(fact);
                }
            }

            return this;
        }

        public TrackingRequirement WithFix(string label, Action fix)
        {
            FixLabel = label;
            Fix = fix;
            return this;
        }
    }

    /// <summary>
    /// Registry for project-specific requirements. Call <see cref="SetProvider"/> once from an
    /// <c>[InitializeOnLoadMethod]</c> in the game's editor assembly; the provider runs every time the
    /// checker refreshes and again before an iOS build starts.
    /// </summary>
    public static class TrackingRequirements
    {
        private static Func<IEnumerable<TrackingRequirement>> provider;

        /// <summary>True when the project registered a requirements provider.</summary>
        public static bool HasProvider => provider != null;

        public static void SetProvider(Func<IEnumerable<TrackingRequirement>> value)
        {
            provider = value;
        }

        public static void ClearProvider()
        {
            provider = null;
        }

        /// <summary>Runs the provider and returns its requirements. A throwing provider becomes a failure.</summary>
        internal static List<TrackingRequirement> Evaluate()
        {
            var result = new List<TrackingRequirement>();

            var current = provider;
            if (current == null)
                return result;

            try
            {
                var items = current();
                if (items == null)
                    return result;

                foreach (var item in items)
                {
                    if (item != null)
                        result.Add(item);
                }
            }
            catch (Exception exception)
            {
                result.Add(new TrackingRequirement(
                    "Project requirements provider",
                    TrackingRequirementStatus.Fail,
                    $"The provider registered with TrackingRequirements.SetProvider threw: {exception.Message}"));
            }

            return result;
        }
    }
}
