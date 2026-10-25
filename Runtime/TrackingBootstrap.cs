using UnityEngine;

namespace Wagenheimer.Tracking
{
    /// <summary>Creates the runtime components once, after the first scene exists.</summary>
    internal static class TrackingBootstrap
    {
        private static bool initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            // Reads the native status, resolves the wait and (optionally) requests the alert.
            TrackingConsent.Initialize();

            if (TrackingSettings.Instance.IsOverlayAllowed)
                TrackingDebugOverlay.CreateIfNeeded();
        }
    }
}
