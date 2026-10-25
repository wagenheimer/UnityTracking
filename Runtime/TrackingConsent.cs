using System;
using System.Collections;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using Unity.Advertisement.IosSupport;
#endif

namespace Wagenheimer.Tracking
{
    /// <summary>
    /// Runtime App Tracking Transparency gate.
    ///
    /// The request runs after the first scene is visible (the system alert needs a key window and
    /// is silently dropped when requested from the first frame). Every SDK that reads the
    /// advertising identifier must wait for <see cref="WaitUntilResolved"/>.
    ///
    /// The package is the only owner of the request: the purpose string in the built app is the
    /// source of truth for whether this build declares tracking, so a request is never sent from a
    /// build that lacks <c>NSUserTrackingUsageDescription</c> (iOS terminates the app in that case).
    ///
    /// A small local history (attempts and timestamps) answers "was ATT already asked on this
    /// device?"; <see cref="RequestAgain"/> re-sends the request even when the answer is final,
    /// which iOS accepts as a no-op and logs for QA.
    /// </summary>
    public static class TrackingConsent
    {
        private const string LogPrefix = "[Tracking]";
        private const string KeyCount = "RequestCount";
        private const string KeyFirstUtc = "FirstRequestUtc";
        private const string KeyLastUtc = "LastRequestUtc";

        public enum State
        {
            NotSupported,
            NotDetermined,
            Restricted,
            Denied,
            Authorized,
            Unknown
        }

        private static bool runnerActive;
        private static bool initialized;
        private static string lastBlockedReason = string.Empty;

        // Session-only history used when persistHistory is off.
        private static int sessionCount;
        private static DateTime? sessionFirstUtc;
        private static DateTime? sessionLastUtc;

        static TrackingConsent()
        {
            var settings = TrackingSettings.Instance;
            Current = settings.Enabled ? State.NotDetermined : State.NotSupported;
            IsResolved = !(IsRequired && CanRequest);
        }

        // ── State ────────────────────────────────────────────────────────────

        /// <summary>
        /// True when this build declares tracking and the player still has to be asked
        /// (or the app needs to read the answer). See <see cref="CanRequest"/> for the technical
        /// capability.
        /// </summary>
        public static bool IsRequired
        {
            get
            {
                var settings = TrackingSettings.Instance;
                if (!settings.Enabled)
                    return false;

                switch (settings.RequirementMode)
                {
                    case TrackingRequirementMode.ForceNotRequired:
                        return false;
                    case TrackingRequirementMode.ForceRequired:
                        return true;
                    default:
                        return IsAttSupportedPlatform && HasPurposeString;
                }
            }
        }

        /// <summary>
        /// True when the package can talk to the ATT API in this build: iOS device, master switch
        /// on, and the purpose string present. Without the key the request is never attempted.
        /// </summary>
        public static bool CanRequest =>
            TrackingSettings.Instance.Enabled && IsAttSupportedPlatform && HasPurposeString;

        /// <summary>True on an iOS player. ATT does not exist anywhere else.</summary>
        public static bool IsAttSupportedPlatform => Application.platform == RuntimePlatform.IPhonePlayer;

        /// <summary>True when the app bundle carries a non-empty <c>NSUserTrackingUsageDescription</c>.</summary>
        public static bool HasPurposeString => TrackingDiagnostics.HasPurposeString;

        /// <summary>
        /// True once the boot wait is over (or when there is nothing to wait for). This is the flag
        /// ad SDKs should wait on; use <see cref="IsAnswered"/> for "the player already chose".
        /// </summary>
        public static bool IsResolved { get; private set; }

        /// <summary>Last known ATT status.</summary>
        public static State Current { get; private set; } = State.NotSupported;

        /// <summary>True while a request/alert is in flight.</summary>
        public static bool IsRequesting { get; private set; }

        /// <summary>Why a request was not attempted (missing plist key, disabled, etc.).</summary>
        public static string LastBlockedReason => lastBlockedReason;

        /// <summary>True when iOS already has a final answer.</summary>
        public static bool IsAnswered =>
            Current == State.Authorized || Current == State.Denied || Current == State.Restricted;

        /// <summary>Maximum wait used by <see cref="WaitUntilResolved"/> when no value is passed.</summary>
        public static float DefaultTimeoutSeconds => TrackingSettings.Instance.TimeoutSeconds;

        /// <summary>Raised when the status, request state or history changes.</summary>
        public static event Action Changed;

        // ── History ──────────────────────────────────────────────────────────

        /// <summary>Number of ATT requests sent by this install (or session when persistence is off).</summary>
        public static int RequestCount =>
            TrackingSettings.Instance.PersistHistory
                ? PlayerPrefs.GetInt(HistoryKey(KeyCount), 0)
                : sessionCount;

        /// <summary>True when at least one request was sent.</summary>
        public static bool WasRequested => RequestCount > 0;

        /// <summary>UTC timestamp of the first request.</summary>
        public static DateTime? FirstRequestUtc =>
            TrackingSettings.Instance.PersistHistory
                ? ReadUtc(HistoryKey(KeyFirstUtc))
                : sessionFirstUtc;

        /// <summary>UTC timestamp of the last request.</summary>
        public static DateTime? LastRequestUtc =>
            TrackingSettings.Instance.PersistHistory
                ? ReadUtc(HistoryKey(KeyLastUtc))
                : sessionLastUtc;

        // ── API ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Waits (bounded) for the ATT answer. Returns immediately when ATT does not apply or the
        /// answer is already known. Pass a negative value to use the settings timeout.
        /// </summary>
        public static IEnumerator WaitUntilResolved(float timeoutSeconds = -1f)
        {
            if (!IsRequired || IsResolved)
                yield break;

            float timeout = timeoutSeconds > 0f ? timeoutSeconds : DefaultTimeoutSeconds;
            float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, timeout);

            while (!IsResolved && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!IsResolved)
            {
                Debug.LogWarning(
                    $"{LogPrefix} ATT not answered after {timeout:F0}s — continuing without the advertising identifier.");
            }
        }

        /// <summary>
        /// Re-sends the ATT request even when the answer is already final, records the attempt and
        /// re-reads the status. iOS never shows the alert twice; reset the permission
        /// (Settings &gt; Privacy &amp; Security &gt; Tracking, or reinstall) to test the prompt.
        /// </summary>
        public static bool RequestAgain()
        {
            if (!TrackingSettings.Instance.Enabled)
            {
                Debug.LogWarning($"{LogPrefix} ATT re-request ignored: the package is disabled in the settings.");
                return false;
            }

            if (!IsAttSupportedPlatform)
            {
                Debug.LogWarning($"{LogPrefix} ATT re-request ignored: ATT only exists on an iOS device build.");
                return false;
            }

            if (!HasPurposeString)
            {
                lastBlockedReason =
                    "NSUserTrackingUsageDescription is missing from the app bundle — requesting ATT would terminate the app.";
                Debug.LogError($"{LogPrefix} {lastBlockedReason}");
                NotifyChanged();
                return false;
            }

            if (runnerActive)
            {
                Debug.LogWarning($"{LogPrefix} ATT re-request ignored: a request is already in progress.");
                return false;
            }

            lastBlockedReason = string.Empty;
            return StartRunner(allowPrompt: true, forceRequest: true);
        }

        /// <summary>Re-reads the native status without prompting (e.g. after returning from Settings).</summary>
        public static void RefreshStatus()
        {
            if (!IsAttSupportedPlatform || IsRequesting)
                return;

            var previous = Current;
            Current = ReadNative();
            if (IsAnswered)
                IsResolved = true;

            if (Current != previous)
                Debug.Log($"{LogPrefix} ATT status refreshed: {previous} -> {Current}");

            NotifyChanged();
        }

        /// <summary>Clears the local request history (this does not change the iOS permission).</summary>
        public static void ResetHistory()
        {
            var settings = TrackingSettings.Instance;

            if (settings.PersistHistory)
            {
                PlayerPrefs.DeleteKey(HistoryKey(KeyCount));
                PlayerPrefs.DeleteKey(HistoryKey(KeyFirstUtc));
                PlayerPrefs.DeleteKey(HistoryKey(KeyLastUtc));
                PlayerPrefs.Save();
            }

            sessionCount = 0;
            sessionFirstUtc = null;
            sessionLastUtc = null;

            Debug.Log($"{LogPrefix} Local ATT request history cleared (the iOS permission is unchanged).");
            NotifyChanged();
        }

        // ── Bootstrap / runner ───────────────────────────────────────────────

        internal static void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            var settings = TrackingSettings.Instance;
            Current = IsAttSupportedPlatform ? ReadNative() : State.NotSupported;

            if (!settings.Enabled)
            {
                IsResolved = true;
                return;
            }

            if (settings.RequirementMode == TrackingRequirementMode.ForceRequired && !HasPurposeString)
            {
                lastBlockedReason =
                    "Requirement is ForceRequired but NSUserTrackingUsageDescription is missing — the request is blocked.";
                Debug.LogError($"{LogPrefix} {lastBlockedReason}");
            }

            if (!IsRequired)
            {
                IsResolved = true;
                return;
            }

            if (!CanRequest)
            {
                IsResolved = true;
                return;
            }

            StartRunner(allowPrompt: settings.RequestOnBoot, forceRequest: false);
        }

        internal static bool StartRunner(bool allowPrompt, bool forceRequest)
        {
            if (runnerActive)
                return false;

            runnerActive = true;

            var host = new GameObject("[WagenheimerTracking]");
            UnityEngine.Object.DontDestroyOnLoad(host);

            var runner = host.AddComponent<TrackingConsentRunner>();
            runner.Configure(allowPrompt, forceRequest);
            return true;
        }

        internal static void EndRunner()
        {
            runnerActive = false;
            IsRequesting = false;
        }

        internal static void SetRequesting(bool requesting)
        {
            IsRequesting = requesting;
            NotifyChanged();
        }

        internal static void SetStatus(State status, bool resolved)
        {
            Current = status;
            if (resolved)
                IsResolved = true;
            NotifyChanged();
        }

        internal static void SetBlockedReason(string reason)
        {
            lastBlockedReason = reason ?? string.Empty;
        }

        internal static void RecordRequest()
        {
            var settings = TrackingSettings.Instance;
            var now = DateTime.UtcNow;
            int count = RequestCount + 1;

            sessionCount = count;
            if (sessionFirstUtc == null)
                sessionFirstUtc = now;
            sessionLastUtc = now;

            if (settings.PersistHistory)
            {
                PlayerPrefs.SetInt(HistoryKey(KeyCount), count);
                if (count == 1)
                    PlayerPrefs.SetString(HistoryKey(KeyFirstUtc), now.ToString("o"));
                PlayerPrefs.SetString(HistoryKey(KeyLastUtc), now.ToString("o"));
                PlayerPrefs.Save();
            }

            NotifyChanged();
        }

        internal static void NotifyChanged()
        {
            var handler = Changed;
            if (handler == null)
                return;

            try
            {
                handler();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        internal static State ReadNative()
        {
#if UNITY_IOS && !UNITY_EDITOR
            switch (ATTrackingStatusBinding.GetAuthorizationTrackingStatus())
            {
                case ATTrackingStatusBinding.AuthorizationTrackingStatus.NOT_DETERMINED:
                    return State.NotDetermined;
                case ATTrackingStatusBinding.AuthorizationTrackingStatus.RESTRICTED:
                    return State.Restricted;
                case ATTrackingStatusBinding.AuthorizationTrackingStatus.DENIED:
                    return State.Denied;
                case ATTrackingStatusBinding.AuthorizationTrackingStatus.AUTHORIZED:
                    return State.Authorized;
                default:
                    return State.Unknown;
            }
#else
            return State.NotSupported;
#endif
        }

        internal static void RequestNativeAuthorization()
        {
#if UNITY_IOS && !UNITY_EDITOR
            ATTrackingStatusBinding.RequestAuthorizationTracking();
#endif
        }

        private static string HistoryKey(string suffix) =>
            TrackingSettings.Instance.HistoryKeyPrefix + "." + suffix;

        private static DateTime? ReadUtc(string key)
        {
            string raw = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(raw))
                return null;

            return DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var value)
                ? value
                : (DateTime?)null;
        }
    }
}
