using System.Collections;
using UnityEngine;

namespace Wagenheimer.Tracking
{
    /// <summary>
    /// One-shot ATT runner. Created by <see cref="TrackingConsent"/> after the first scene is
    /// visible, it reads the native status, optionally prompts, retries once and then resolves the
    /// wait used by ad SDKs.
    /// </summary>
    internal sealed class TrackingConsentRunner : MonoBehaviour
    {
        private bool allowPrompt;
        private bool forceRequest;

        internal void Configure(bool allowPrompt, bool forceRequest)
        {
            this.allowPrompt = allowPrompt;
            this.forceRequest = forceRequest;
        }

        private void OnDestroy()
        {
            TrackingConsent.EndRunner();
        }

        private IEnumerator Start()
        {
            var settings = TrackingSettings.Instance;

            // Give the first scene a frame to render and the key window time to become active.
            if (settings.InitialDelaySeconds > 0f)
                yield return new WaitForSecondsRealtime(settings.InitialDelaySeconds);
            else
                yield return null;

            var status = TrackingConsent.ReadNative();
            TrackingConsent.SetStatus(status, resolved: false);

            Debug.Log($"[Tracking] ATT status at boot: {status} ({SystemInfo.operatingSystem})");
            Debug.Log($"[Tracking] NSUserTrackingUsageDescription: {(TrackingConsent.HasPurposeString ? "present" : "MISSING")}");

            // Never request without the key: iOS terminates the app in that case.
            if (!TrackingConsent.HasPurposeString)
            {
                const string reason =
                    "NSUserTrackingUsageDescription is missing from the app bundle — the ATT request was blocked.";
                TrackingConsent.SetBlockedReason(reason);
                Debug.LogError("[Tracking] " + reason);
                TrackingConsent.SetStatus(status, resolved: true);
                Finish();
                yield break;
            }

            if (TrackingConsent.IsAnswered)
            {
                if (forceRequest)
                {
                    TrackingConsent.RecordRequest();
                    Debug.LogWarning(
                        $"[Tracking] Re-sending the ATT request with status {status}. iOS will NOT show the alert again — " +
                        "reset the permission (Settings > Privacy & Security > Tracking, or reinstall) to test the prompt.");

                    TrackingConsent.RequestNativeAuthorization();
                    yield return new WaitForSecondsRealtime(0.35f);

                    status = TrackingConsent.ReadNative();
                    TrackingConsent.SetStatus(status, resolved: true);
                    Debug.Log($"[Tracking] ATT status after the re-request: {status}");
                }
                else
                {
                    Debug.Log($"[Tracking] ATT already answered: {status} — no request needed.");
                    TrackingConsent.SetStatus(status, resolved: true);
                }

                Finish();
                yield break;
            }

            if (!allowPrompt)
            {
                Debug.LogWarning(
                    $"[Tracking] ATT is {status} and requestOnBoot is off — resolving without prompting.");
                TrackingConsent.SetStatus(status, resolved: true);
                Finish();
                yield break;
            }

            TrackingConsent.SetRequesting(true);
            TrackingConsent.RecordRequest();

            Debug.Log("[Tracking] Requesting ATT authorization...");
            TrackingConsent.RequestNativeAuthorization();

            float deadline = Time.realtimeSinceStartup + settings.TimeoutSeconds;
            float retryAt = settings.RetryOnce
                ? Time.realtimeSinceStartup + settings.RetryDelaySeconds
                : float.MaxValue;

            while (TrackingConsent.ReadNative() == TrackingConsent.State.NotDetermined &&
                   Time.realtimeSinceStartup < deadline)
            {
                // Some devices ignore the very first request (window not key yet). One retry is
                // harmless when the alert is already on screen.
                if (Time.realtimeSinceStartup >= retryAt)
                {
                    retryAt = float.MaxValue;
                    Debug.LogWarning(
                        $"[Tracking] ATT still NotDetermined after {settings.RetryDelaySeconds:F0}s — retrying the request once.");
                    TrackingConsent.RequestNativeAuthorization();
                }

                yield return null;
            }

            TrackingConsent.SetRequesting(false);

            status = TrackingConsent.ReadNative();
            TrackingConsent.SetStatus(status, resolved: true);

            if (status == TrackingConsent.State.Authorized)
                Debug.Log("[Tracking] ATT authorized — the advertising identifier is available.");
            else
                Debug.LogWarning($"[Tracking] ATT {status} — ad SDKs run without the advertising identifier.");

            Finish();
        }

        private void Finish()
        {
            TrackingConsent.EndRunner();
            Destroy(gameObject);
        }
    }
}
