namespace Wagenheimer.Tracking
{
    /// <summary>
    /// Pure declaration rules shared by the build processor, the checker and the tests.
    /// No UnityEditor and no UnityEngine dependency, so it can be unit tested.
    /// </summary>
    public static class TrackingDeclaration
    {
        /// <summary>
        /// True when the build declares tracking in App Privacy and therefore must carry the
        /// purpose string.
        /// </summary>
        public static bool DeclaresTracking(bool enabled, TrackingDeclarationMode mode, bool definePresent)
        {
            if (!enabled)
                return false;

            switch (mode)
            {
                case TrackingDeclarationMode.Always:
                    return true;
                case TrackingDeclarationMode.Never:
                    return false;
                default:
                    return definePresent;
            }
        }

        /// <summary>True when the build processor should write the purpose string.</summary>
        public static bool ShouldWritePurposeString(
            bool enabled, TrackingDeclarationMode mode, bool definePresent, bool writePurposeString)
        {
            return writePurposeString && DeclaresTracking(enabled, mode, definePresent);
        }

        /// <summary>True when the build processor should remove the purpose string.</summary>
        public static bool ShouldRemovePurposeString(
            bool enabled, TrackingDeclarationMode mode, bool definePresent, bool removeWhenNotDeclared)
        {
            return removeWhenNotDeclared && !DeclaresTracking(enabled, mode, definePresent);
        }

        /// <summary>Human-readable declaration summary used by logs, reports and the checker.</summary>
        public static string Describe(
            bool enabled, TrackingDeclarationMode mode, string define, bool definePresent)
        {
            if (!enabled)
                return "Disabled (master switch off).";

            switch (mode)
            {
                case TrackingDeclarationMode.Always:
                    return "Always declares tracking.";
                case TrackingDeclarationMode.Never:
                    return "Never declares tracking.";
                default:
                    return definePresent
                        ? $"Declares tracking: define '{define}' is applied to this build."
                        : $"Does not declare tracking: define '{define}' is not applied to this build.";
            }
        }
    }
}
