using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Wagenheimer.Tracking
{
    /// <summary>
    /// On-device diagnostics for App Tracking Transparency and build identity.
    ///
    /// Everything is exposed as a list of <see cref="Check"/>s so the runtime overlay and the editor
    /// checker can show one row per item, with copy/AI actions for anything that is not OK.
    /// Project-specific checks (for example the ad SDK state) are appended through
    /// <see cref="AddCheckProvider"/>.
    /// </summary>
    public static class TrackingDiagnostics
    {
        private const string LogPrefix = "[Tracking]";

        public enum CheckStatus
        {
            Ok,
            Info,
            Warning,
            Error
        }

        public enum NativeAttStatus
        {
            Unavailable = -1,
            NotDetermined = 0,
            Restricted = 1,
            Denied = 2,
            Authorized = 3
        }

        public sealed class Check
        {
            public Check(string title, CheckStatus status, string detail, string fix = null)
            {
                Title = title ?? string.Empty;
                Status = status;
                Detail = detail ?? string.Empty;
                Fix = fix ?? string.Empty;
            }

            public string Title { get; }
            public CheckStatus Status { get; }
            public string Detail { get; }
            public string Fix { get; }

            public bool IsProblem => Status == CheckStatus.Warning || Status == CheckStatus.Error;

            public string StatusLabel => Status switch
            {
                CheckStatus.Ok => "OK",
                CheckStatus.Warning => "WARN",
                CheckStatus.Error => "FAIL",
                _ => "INFO"
            };
        }

        /// <summary>Everything the overlay/checker needs about ATT, in one call.</summary>
        public sealed class AttSnapshot
        {
            public bool Enabled;
            public TrackingDeclarationMode DeclarationMode;
            public TrackingRequirementMode RequirementMode;
            public bool Required;
            public bool CanRequest;
            public bool FrameworkAvailable;
            public bool HasPurposeString;
            public string PurposeString = string.Empty;
            public NativeAttStatus NativeStatus;
            public TrackingConsent.State ManagedState;
            public bool Resolved;
            public bool Requesting;
            public bool Answered;
            public bool WasRequested;
            public int RequestCount;
            public DateTime? FirstRequestUtc;
            public DateTime? LastRequestUtc;
            public string BlockedReason = string.Empty;

            public bool Blocked => !string.IsNullOrEmpty(BlockedReason);

            /// <summary>Human status: prefers the native value, falls back to the managed state.</summary>
            public string StatusText => NativeStatus != NativeAttStatus.Unavailable
                ? NativeStatus.ToString()
                : ManagedState.ToString();

            public string StatusDescription
            {
                get
                {
                    switch (NativeStatus)
                    {
                        case NativeAttStatus.Authorized:
                            return "Authorized — the advertising identifier is available to ad SDKs.";
                        case NativeAttStatus.Denied:
                            return "Denied — iOS answered and ad SDKs run without the advertising identifier.";
                        case NativeAttStatus.Restricted:
                            return "Restricted — the system blocks tracking (parental controls/MDM).";
                        case NativeAttStatus.NotDetermined:
                            return "Not determined — iOS still accepts (and expects) the ATT alert.";
                        case NativeAttStatus.Unavailable:
                            return Required
                                ? "ATT is unavailable in this app (framework not loaded)."
                                : "Not applicable to this platform/build.";
                        default:
                            return "Unknown state.";
                    }
                }
            }
        }

        // ── Custom check providers ───────────────────────────────────────────

        private static readonly List<Func<IEnumerable<Check>>> providers = new List<Func<IEnumerable<Check>>>();

        /// <summary>
        /// Registers project-specific checks (ad SDK, consent manager, backend, ...). Providers run
        /// on the main thread every time the overlay refreshes.
        /// </summary>
        public static void AddCheckProvider(Func<IEnumerable<Check>> provider)
        {
            if (provider == null || providers.Contains(provider))
                return;

            providers.Add(provider);
        }

        /// <summary>Removes a provider registered with <see cref="AddCheckProvider"/>.</summary>
        public static void RemoveCheckProvider(Func<IEnumerable<Check>> provider)
        {
            if (provider == null)
                return;

            providers.Remove(provider);
        }

        /// <summary>Removes every custom provider (tests/teardown).</summary>
        public static void ClearCheckProviders() => providers.Clear();

        // ── Native probe ─────────────────────────────────────────────────────

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int _WTTrackingFrameworkAvailable();
        [DllImport("__Internal")] private static extern int _WTTrackingHasPurposeString();
        [DllImport("__Internal")] private static extern int _WTTrackingStatus();
        [DllImport("__Internal")] private static extern IntPtr _WTTrackingPurposeString();
        [DllImport("__Internal")] private static extern IntPtr _WTBundleShortVersion();
        [DllImport("__Internal")] private static extern IntPtr _WTBundleBuildNumber();
        [DllImport("__Internal")] private static extern int _WTTrackingOpenSettings();
        [DllImport("__Internal")] private static extern void _WTCopyToClipboard(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        private static bool NativePlatform => Application.platform == RuntimePlatform.IPhonePlayer;

        public static bool IsFrameworkAvailable => NativePlatform && _WTTrackingFrameworkAvailable() != 0;
        public static bool HasPurposeString => NativePlatform && _WTTrackingHasPurposeString() != 0;
        public static NativeAttStatus NativeStatus =>
            NativePlatform ? (NativeAttStatus)_WTTrackingStatus() : NativeAttStatus.Unavailable;

        public static string PurposeString => ReadString(_WTTrackingPurposeString);
        public static string BundleShortVersion => ReadString(_WTBundleShortVersion);
        public static string BundleBuildNumber => ReadString(_WTBundleBuildNumber);

        /// <summary>Opens this app's page in the iOS Settings app.</summary>
        public static bool OpenAppSettings()
        {
            if (!NativePlatform)
                return false;

            try
            {
                if (_WTTrackingOpenSettings() != 0)
                    return true;
            }
            catch (Exception)
            {
                // Fall through to the URL scheme below.
            }

            try
            {
                Application.OpenURL("app-settings:");
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                _WTCopyToClipboard(text);
            }
            catch (Exception)
            {
                GUIUtility.systemCopyBuffer = text;
            }
        }

        private static string ReadString(Func<IntPtr> read)
        {
            try
            {
                IntPtr pointer = read();
                return pointer == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringAnsi(pointer) ?? string.Empty);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
#else
        public static bool IsFrameworkAvailable => false;
        public static bool HasPurposeString => false;
        public static NativeAttStatus NativeStatus => NativeAttStatus.Unavailable;
        public static string PurposeString => string.Empty;
        public static string BundleShortVersion => Application.version;
        public static string BundleBuildNumber => string.Empty;
        public static bool OpenAppSettings() => false;
        public static void CopyToClipboard(string text) => GUIUtility.systemCopyBuffer = text;
#endif

        // ── Snapshot ─────────────────────────────────────────────────────────

        public static AttSnapshot GetAttSnapshot()
        {
            var settings = TrackingSettings.Instance;

            var snapshot = new AttSnapshot
            {
                Enabled = settings.Enabled,
                DeclarationMode = settings.DeclarationMode,
                RequirementMode = settings.RequirementMode,
                Required = TrackingConsent.IsRequired,
                CanRequest = TrackingConsent.CanRequest,
                FrameworkAvailable = IsFrameworkAvailable,
                HasPurposeString = HasPurposeString,
                PurposeString = PurposeString,
                NativeStatus = NativeStatus,
                ManagedState = TrackingConsent.Current,
                Resolved = TrackingConsent.IsResolved,
                Requesting = TrackingConsent.IsRequesting,
                WasRequested = TrackingConsent.WasRequested,
                RequestCount = TrackingConsent.RequestCount,
                FirstRequestUtc = TrackingConsent.FirstRequestUtc,
                LastRequestUtc = TrackingConsent.LastRequestUtc,
                BlockedReason = TrackingConsent.LastBlockedReason
            };

            snapshot.Answered =
                snapshot.NativeStatus == NativeAttStatus.Authorized ||
                snapshot.NativeStatus == NativeAttStatus.Denied ||
                snapshot.NativeStatus == NativeAttStatus.Restricted;

            // Editor/other platforms: the native probe is unavailable, trust the managed state.
            if (snapshot.NativeStatus == NativeAttStatus.Unavailable)
            {
                snapshot.Answered =
                    snapshot.ManagedState == TrackingConsent.State.Authorized ||
                    snapshot.ManagedState == TrackingConsent.State.Denied ||
                    snapshot.ManagedState == TrackingConsent.State.Restricted;
            }

            return snapshot;
        }

        // ── Checks ───────────────────────────────────────────────────────────

        public static List<Check> GetChecks()
        {
            var settings = TrackingSettings.Instance;
            var att = GetAttSnapshot();

            var checks = new List<Check>
            {
                new Check("App / build", CheckStatus.Info,
                    $"{Application.identifier}   v{Application.version} ({VersionOr(BundleBuildNumber)})"),

                new Check("Platform", CheckStatus.Info,
                    $"{Application.platform}   {SystemInfo.operatingSystem}   {SystemInfo.deviceModel}"),

                new Check("Package",
                    settings.Enabled ? CheckStatus.Ok : CheckStatus.Info,
                    settings.Enabled
                        ? "Tracking Helper is enabled in the settings."
                        : "Tracking Helper is disabled (master switch off)."),

                new Check("Declaration",
                    CheckStatus.Info,
                    TrackingDeclaration.Describe(
                        settings.Enabled,
                        settings.DeclarationMode,
                        settings.ScriptingDefine,
                        att.HasPurposeString) +
                    (settings.DeclarationMode == TrackingDeclarationMode.ScriptingDefine
                        ? " (runtime evidence: purpose string in the app bundle)"
                        : string.Empty)),

                new Check("Requirement",
                    att.Required ? CheckStatus.Ok : CheckStatus.Info,
                    att.Required
                        ? att.CanRequest
                            ? "ATT is required and the request is possible."
                            : "ATT is required but the purpose string is missing — the request is blocked."
                        : "ATT is not required for this build.")
            };

            if (settings.RequirementMode == TrackingRequirementMode.ForceRequired && !att.HasPurposeString)
                checks.Add(new Check("Requirement override", CheckStatus.Error,
                    "ForceRequired is set but the app bundle has no NSUserTrackingUsageDescription.",
                    "Disable the override or fix the build so the purpose string is written."));

            if (Application.platform != RuntimePlatform.IPhonePlayer)
            {
                checks.Add(new Check("App Tracking Transparency", CheckStatus.Info,
                    "Not applicable outside iOS."));
                AddCustomChecks(checks);
                return checks;
            }

            checks.Add(new Check("ATT framework",
                IsFrameworkAvailable ? CheckStatus.Ok : CheckStatus.Error,
                IsFrameworkAvailable
                    ? "ATTrackingManager is available."
                    : "ATTrackingManager was NOT found — the framework was not loaded.",
                IsFrameworkAvailable
                    ? null
                    : "Weak-link AppTrackingTransparency.framework in the UnityFramework target and require iOS 14.5+."));

            checks.Add(new Check("NSUserTrackingUsageDescription",
                HasPurposeString ? CheckStatus.Ok : CheckStatus.Error,
                HasPurposeString ? PurposeString : "The key is missing or empty in the app bundle Info.plist.",
                HasPurposeString
                    ? null
                    : "Declare tracking in the settings (or apply the scripting define) and rebuild so the package writes the key."));

            checks.Add(BuildAskedCheck(att));
            checks.Add(BuildAttStatusCheck(att));
            checks.Add(BuildHistoryCheck(att));

            if (att.Enabled && att.Resolved &&
                NativeStatus != NativeAttStatus.Unavailable &&
                !string.Equals(NativeStatus.ToString(), att.ManagedState.ToString(), StringComparison.Ordinal))
            {
                checks.Add(new Check("Native vs managed status", CheckStatus.Warning,
                    $"Divergence: native={NativeStatus}, managed={att.ManagedState}.",
                    "Another SDK may have requested ATT first. Make this package the single owner and check the [Tracking] log."));
            }

            AddCustomChecks(checks);
            return checks;
        }

        private static Check BuildAskedCheck(AttSnapshot att)
        {
            if (!att.Enabled)
                return new Check("Was ATT asked?", CheckStatus.Info,
                    "Not applicable: the package is disabled in the settings.");

            if (att.Requesting)
                return new Check("Was ATT asked?", CheckStatus.Info,
                    "A request is in progress — the iOS alert should be on screen.");

            if (!att.Resolved && !att.WasRequested)
                return new Check("Was ATT asked?", CheckStatus.Info,
                    "Not yet — the boot request is about to run (initial delay + first frame).");

            if (att.Answered)
                return new Check("Was ATT asked?", CheckStatus.Ok,
                    $"Yes — the player already answered ({att.StatusText}). iOS does not show the alert again.",
                    "To see the alert again: Settings > Privacy & Security > Tracking (allow requests) or reinstall the app.");

            if (att.WasRequested)
                return new Check("Was ATT asked?", CheckStatus.Warning,
                    $"The request was sent {att.RequestCount}x and the status is still NotDetermined.",
                    "Use 'Request ATT again' in the overlay. If it stays NotDetermined, confirm the purpose string and that the app was foreground.");

            return new Check("Was ATT asked?", CheckStatus.Warning,
                "No — no ATT request is recorded on this device.",
                "Use 'Request ATT again' in the overlay to trigger the alert now.");
        }

        private static Check BuildAttStatusCheck(AttSnapshot att)
        {
            if (!att.Enabled)
                return new Check("ATT status", CheckStatus.Info, "Not requested: the package is disabled.");

            if (!att.Required)
                return new Check("ATT status", CheckStatus.Info, "Not requested in this build.");

            if (!att.Resolved && !att.Answered)
                return new Check("ATT status", CheckStatus.Warning,
                    $"Waiting for the player's answer ({att.ManagedState}).",
                    "If the alert does not appear, check the purpose string and that the request runs with the app active.");

            switch (att.NativeStatus)
            {
                case NativeAttStatus.Authorized:
                    return new Check("ATT status", CheckStatus.Ok,
                        "Authorized — the advertising identifier is available.");

                case NativeAttStatus.Denied:
                case NativeAttStatus.Restricted:
                    return new Check("ATT status", CheckStatus.Ok,
                        $"Answered ({att.NativeStatus}) — ad SDKs run without the advertising identifier.",
                        "Use 'Open app settings' to change the permission (iOS does not allow asking again from the app).");

                case NativeAttStatus.NotDetermined:
                    return new Check("ATT status", CheckStatus.Warning,
                        "Still NotDetermined after the request — the alert was probably not presented.",
                        "Use 'Request ATT again' and confirm the purpose string. See the [Tracking] log on the device.");

                default:
                    return new Check("ATT status", CheckStatus.Warning,
                        $"Unexpected state: {att.ManagedState}.",
                        "Check the ATT binding (com.unity.ads.ios-support) and the native log.");
            }
        }

        private static Check BuildHistoryCheck(AttSnapshot att)
        {
            if (!att.Enabled)
                return new Check("Request history", CheckStatus.Info, "Not applicable: the package is disabled.");

            if (!att.WasRequested)
                return new Check("Request history", CheckStatus.Info,
                    "No request recorded locally since the install.");

            return new Check("Request history", CheckStatus.Info,
                $"Attempts on this device: {att.RequestCount}   First: {FormatLocal(att.FirstRequestUtc)}   Last: {FormatLocal(att.LastRequestUtc)}");
        }

        private static void AddCustomChecks(List<Check> checks)
        {
            foreach (var provider in providers.ToArray())
            {
                try
                {
                    var custom = provider();
                    if (custom == null)
                        continue;

                    foreach (var check in custom)
                    {
                        if (check != null)
                            checks.Add(check);
                    }
                }
                catch (Exception exception)
                {
                    checks.Add(new Check("Custom check provider", CheckStatus.Warning,
                        $"A custom check provider threw: {exception.Message}"));
                }
            }
        }

        // ── Verdict / reports ────────────────────────────────────────────────

        public static string Verdict()
        {
            var checks = GetChecks();
            int errors = 0, warnings = 0;

            foreach (var check in checks)
            {
                if (check.Status == CheckStatus.Error) errors++;
                else if (check.Status == CheckStatus.Warning) warnings++;
            }

            if (errors > 0)
                return $"FAIL — {errors} item(s) to fix" + (warnings > 0 ? $" and {warnings} warning(s)" : string.Empty);

            if (warnings > 0)
                return $"WARN — {warnings} warning(s)";

            if (!TrackingConsent.IsRequired)
                return "OK — ATT does not apply to this build";

            return "OK — ATT ready";
        }

        public static bool IsReady
        {
            get
            {
                foreach (var check in GetChecks())
                {
                    if (check.Status == CheckStatus.Error || check.Status == CheckStatus.Warning)
                        return false;
                }

                return true;
            }
        }

        public static string BuildReport()
        {
            var settings = TrackingSettings.Instance;
            var att = GetAttSnapshot();
            var builder = new StringBuilder(2400);

            builder.AppendLine("===== TRACKING / PRIVACY =====");
            builder.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"App: {Application.identifier} v{Application.version} ({VersionOr(BundleBuildNumber)})");
            builder.AppendLine($"Platform: {Application.platform} | {SystemInfo.operatingSystem} | {SystemInfo.deviceModel}");
            builder.AppendLine($"ATT: status={att.NativeStatus} | asked={(att.Answered ? "yes" : att.WasRequested ? "sent, no answer" : "no")} | attempts={att.RequestCount} | last={FormatLocal(att.LastRequestUtc)}");
            builder.AppendLine($"Declaration: {TrackingDeclaration.Describe(settings.Enabled, settings.DeclarationMode, settings.ScriptingDefine, att.HasPurposeString)}");
            builder.AppendLine($"Requirement: required={att.Required} canRequest={att.CanRequest} mode={settings.RequirementMode}");
            if (att.Blocked)
                builder.AppendLine($"Blocked: {att.BlockedReason}");
            builder.AppendLine();

            foreach (var check in GetChecks())
            {
                builder.AppendLine($"[{check.StatusLabel}] {check.Title}: {check.Detail}");
                if (check.IsProblem && !string.IsNullOrEmpty(check.Fix))
                    builder.AppendLine($"        Fix: {check.Fix}");
            }

            builder.AppendLine("---------------------------------");
            builder.AppendLine($"VERDICT: {Verdict()}");
            return builder.ToString();
        }

        public static string BuildCheckText(Check check)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"[{check.StatusLabel}] {check.Title}");
            builder.AppendLine(check.Detail);
            if (!string.IsNullOrEmpty(check.Fix))
                builder.AppendLine($"Fix: {check.Fix}");
            builder.AppendLine();
            builder.AppendLine("Context:");
            builder.AppendLine($"  App.......: {Application.identifier} v{Application.version} ({VersionOr(BundleBuildNumber)})");
            builder.AppendLine($"  Platform..: {Application.platform} {SystemInfo.operatingSystem} {SystemInfo.deviceModel}");
            builder.AppendLine($"  ATT.......: {(TrackingConsent.IsRequired ? "required" : "not applicable")} / {TrackingConsent.Current} / resolved={TrackingConsent.IsResolved}");
            return builder.ToString();
        }

        public static string BuildAttStatusText()
        {
            var settings = TrackingSettings.Instance;
            var att = GetAttSnapshot();
            var builder = new StringBuilder(1400);

            builder.AppendLine("===== APP TRACKING TRANSPARENCY =====");
            builder.AppendLine($"Date.............: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"Platform.........: {Application.platform} | {SystemInfo.operatingSystem} | {SystemInfo.deviceModel}");
            builder.AppendLine($"App..............: {Application.identifier} v{Application.version} ({VersionOr(BundleBuildNumber)})");
            builder.AppendLine($"Package enabled..: {att.Enabled}");
            builder.AppendLine($"Declaration......: {settings.DeclarationMode} / define={settings.ScriptingDefine}");
            builder.AppendLine($"Requirement......: {settings.RequirementMode} (required={att.Required}, canRequest={att.CanRequest})");
            builder.AppendLine($"Framework........: {(att.FrameworkAvailable ? "available" : "unavailable")}");
            builder.AppendLine($"Purpose string...: {(att.HasPurposeString ? att.PurposeString : "(missing)")}");
            builder.AppendLine($"Native status....: {att.NativeStatus}");
            builder.AppendLine($"Managed status...: {att.ManagedState} (resolved={att.Resolved}, requesting={att.Requesting})");
            builder.AppendLine($"Was ATT asked?...: {(att.Answered ? "yes - answered" : att.WasRequested ? $"sent {att.RequestCount}x, no answer" : "no")}");
            builder.AppendLine($"Attempts.........: {att.RequestCount}");
            builder.AppendLine($"First request....: {FormatLocal(att.FirstRequestUtc)}");
            builder.AppendLine($"Last request.....: {FormatLocal(att.LastRequestUtc)}");
            if (att.Blocked)
                builder.AppendLine($"Blocked..........: {att.BlockedReason}");
            builder.AppendLine($"Description......: {att.StatusDescription}");
            return builder.ToString();
        }

        /// <summary>
        /// Ready-to-paste prompt for an AI assistant. With <paramref name="only"/> it focuses on a
        /// single failing check; otherwise it includes every non-OK check.
        /// </summary>
        public static string BuildAiPrompt(Check only = null)
        {
            var settings = TrackingSettings.Instance;
            var problems = new List<Check>();

            if (only != null && only.IsProblem)
            {
                problems.Add(only);
            }
            else
            {
                foreach (var check in GetChecks())
                {
                    if (check.IsProblem)
                        problems.Add(check);
                }
            }

            var builder = new StringBuilder(3600);
            builder.AppendLine("You are an iOS/Unity engineer. Analyze the diagnosis below and fix the project.");
            builder.AppendLine();
            builder.AppendLine("CONTEXT");
            builder.AppendLine($"- App: {Application.identifier} v{Application.version} ({VersionOr(BundleBuildNumber)})");
            builder.AppendLine($"- Platform: {Application.platform} | {SystemInfo.operatingSystem} | {SystemInfo.deviceModel}");
            builder.AppendLine($"- Stack: {settings.StackDescription}");
            builder.AppendLine("- Apple requirement 5.1.2(i): request App Tracking Transparency before any SDK reads the IDFA.");
            builder.AppendLine("- The com.wagenheimer.tracking package is the single owner of the request and of NSUserTrackingUsageDescription.");
            builder.AppendLine($"- Declaration mode: {settings.DeclarationMode} (define '{settings.ScriptingDefine}').");
            builder.AppendLine($"- Relevant files: {ContextFiles(settings)}");
            builder.AppendLine();

            builder.AppendLine(problems.Count > 0 ? "PROBLEMS (status other than OK)" : "PROBLEMS: none in error/warning");
            foreach (var check in problems)
            {
                builder.AppendLine($"- [{check.StatusLabel}] {check.Title}: {check.Detail}");
                if (!string.IsNullOrEmpty(check.Fix))
                    builder.AppendLine($"  Expected fix: {check.Fix}");
            }

            builder.AppendLine();
            builder.AppendLine("FULL REPORT");
            builder.AppendLine(BuildReport());
            builder.AppendLine();
            builder.AppendLine("RESPOND WITH");
            builder.AppendLine("1. Probable cause of each problem.");
            builder.AppendLine("2. Exact patch (file + diff) to fix it.");
            builder.AppendLine("3. How to validate on device ([Tracking] log/overlay) and in the build (checker).");
            return builder.ToString();
        }

        private static string ContextFiles(TrackingSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.ContextFiles))
                return settings.ContextFiles.Replace("\r", string.Empty).Replace("\n", ", ").Trim().TrimEnd(',');

            return "Packages/com.wagenheimer.tracking/Runtime/TrackingConsent.cs, " +
                   "Packages/com.wagenheimer.tracking/Runtime/TrackingDiagnostics.cs, " +
                   "Packages/com.wagenheimer.tracking/Editor/TrackingBuildProcessor.cs";
        }

        private static string FormatLocal(DateTime? utc) =>
            utc.HasValue ? utc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "never";

        private static string VersionOr(string value) => string.IsNullOrEmpty(value) ? "?" : value;
    }
}
