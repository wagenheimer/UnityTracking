using UnityEngine;

namespace Wagenheimer.Tracking
{
    /// <summary>How a build decides whether it declares tracking in App Privacy.</summary>
    public enum TrackingDeclarationMode
    {
        /// <summary>Never declares tracking: no purpose string, no request.</summary>
        Never = 0,

        /// <summary>Always declares tracking, regardless of scripting defines.</summary>
        Always = 1,

        /// <summary>Declares tracking only when <see cref="TrackingSettings.ScriptingDefine"/> is applied to the build.</summary>
        ScriptingDefine = 2
    }

    /// <summary>Runtime requirement override, mainly for QA builds.</summary>
    public enum TrackingRequirementMode
    {
        /// <summary>The build declares tracking when <c>NSUserTrackingUsageDescription</c> exists.</summary>
        Auto = 0,

        /// <summary>Require ATT even if the plist key is missing (diagnostics only; the request is still blocked).</summary>
        ForceRequired = 1,

        /// <summary>Do not request ATT even when the key exists.</summary>
        ForceNotRequired = 2
    }

    /// <summary>
    /// Configuration for the tracking package. Create the asset from
    /// <c>Tools &gt; Wagenheimer &gt; Tracking &gt; Create Settings Asset...</c>; it is loaded from
    /// <c>Resources/Wagenheimer/TrackingSettings</c>.
    ///
    /// The same setup can ship a Free build that declares tracking and a Full build that does not:
    /// <see cref="DeclarationMode"/> + <see cref="ScriptingDefine"/> are evaluated by the build
    /// processor, while the runtime uses the purpose string actually present in the app bundle.
    /// </summary>
    [CreateAssetMenu(menuName = "Wagenheimer/Tracking/Settings", fileName = "TrackingSettings")]
    public sealed class TrackingSettings : ScriptableObject
    {
        public const string ResourcesPath = "Wagenheimer/TrackingSettings";
        public const string DefaultDefine = "FTDOTD_TRACKING";

        public const string DefaultPurposeString =
            "Your advertising identifier is used to show you ads based on your interests and to measure ad performance.";

        private const string PrefsPrefix = "Wagenheimer.Tracking.OverlayScale";

        // ── Activation ───────────────────────────────────────────────────────
        [Header("Activation")]
        [Tooltip("Master switch. Off = no request, no plist write, the overlay reports N/A.")]
        [SerializeField] private bool enabled = true;

        [Tooltip("How a build declares tracking in App Privacy. Drives the Info.plist key and the request.")]
        [SerializeField] private TrackingDeclarationMode declarationMode = TrackingDeclarationMode.ScriptingDefine;

        [Tooltip("Scripting define inspected by ScriptingDefine mode (iOS Free profile applies it).")]
        [SerializeField] private string scriptingDefine = DefaultDefine;

        [Tooltip("Runtime requirement. Auto uses the purpose string present in the app bundle.")]
        [SerializeField] private TrackingRequirementMode requirementMode = TrackingRequirementMode.Auto;

        [Tooltip("Request ATT on boot when the status is NotDetermined. Off only reads the status.")]
        [SerializeField] private bool requestOnBoot = true;

        // ── iOS Info.plist ───────────────────────────────────────────────────
        [Header("iOS Info.plist")]
        [Tooltip("Write NSUserTrackingUsageDescription into the generated Info.plist when the build declares tracking.")]
        [SerializeField] private bool writePurposeString = true;

        [Tooltip("Text written to NSUserTrackingUsageDescription. Apple rejects missing or vague descriptions.")]
        [TextArea(2, 5)]
        [SerializeField] private string purposeString = DefaultPurposeString;

        [Tooltip("Remove the key from builds that do not declare tracking (Full games, etc.).")]
        [SerializeField] private bool removeWhenNotDeclared = true;

        [Tooltip("Fail the build when it declares tracking but the configuration cannot produce a valid plist key.")]
        [SerializeField] private bool failBuildOnInvalidConfiguration = true;

        // ── Timing ───────────────────────────────────────────────────────────
        [Header("Timing")]
        [Tooltip("Seconds to wait after the first scene before requesting (the window must be visible).")]
        [Min(0f)]
        [SerializeField] private float initialDelaySeconds = 0.75f;

        [Tooltip("Maximum seconds the game waits for the answer before initializing ad SDKs.")]
        [Min(1f)]
        [SerializeField] private float timeoutSeconds = 30f;

        [Tooltip("Seconds before the single retry when the alert did not appear.")]
        [Min(0f)]
        [SerializeField] private float retryDelaySeconds = 6f;

        [Tooltip("Retry the request once when the status is still NotDetermined.")]
        [SerializeField] private bool retryOnce = true;

        // ── History ──────────────────────────────────────────────────────────
        [Header("History")]
        [Tooltip("Persist attempts and timestamps in PlayerPrefs.")]
        [SerializeField] private bool persistHistory = true;

        [Tooltip("PlayerPrefs prefix for the attempt history.")]
        [SerializeField] private string historyKeyPrefix = "Wagenheimer.Tracking";

        // ── Diagnostics ──────────────────────────────────────────────────────
        [Header("Diagnostics")]
        [Tooltip("Stack description added to the AI prompt.")]
        [TextArea(2, 4)]
        [SerializeField] private string stackDescription = "Unity (IL2CPP) with an ads mediation SDK.";

        [Tooltip("Project files added to the AI prompt (one per line).")]
        [TextArea(2, 6)]
        [SerializeField] private string contextFiles = "";

        [Tooltip("Enable the 'Copy for AI' action in the runtime overlay and the editor checker.")]
        [SerializeField] private bool includeAiPrompt = true;

        // ── Runtime overlay ──────────────────────────────────────────────────
        [Header("Runtime Debug Overlay")]
        [SerializeField] private bool enableDebugOverlay = true;

        [Tooltip("Also show the overlay in non-development builds. QA only.")]
        [SerializeField] private bool showInReleaseBuilds = false;

        [SerializeField] private KeyCode toggleKey = KeyCode.F9;
        [SerializeField] private bool showFloatingButton = true;

        [Range(1f, 3f)]
        [SerializeField] private float mobileScale = 1.6f;

        [Range(0.75f, 3f)]
        [SerializeField] private float desktopScale = 1f;

        // ── Public read API ──────────────────────────────────────────────────
        public bool Enabled => enabled;
        public TrackingDeclarationMode DeclarationMode => declarationMode;
        public string ScriptingDefine => scriptingDefine;
        public TrackingRequirementMode RequirementMode => requirementMode;
        public bool RequestOnBoot => requestOnBoot;
        public bool WritePurposeString => writePurposeString;
        public string PurposeString => purposeString;
        public bool RemoveWhenNotDeclared => removeWhenNotDeclared;
        public bool FailBuildOnInvalidConfiguration => failBuildOnInvalidConfiguration;
        public float InitialDelaySeconds => Mathf.Max(0f, initialDelaySeconds);
        public float TimeoutSeconds => Mathf.Max(1f, timeoutSeconds);
        public float RetryDelaySeconds => Mathf.Max(0f, retryDelaySeconds);
        public bool RetryOnce => retryOnce;
        public bool PersistHistory => persistHistory;
        public string HistoryKeyPrefix =>
            string.IsNullOrWhiteSpace(historyKeyPrefix) ? "Wagenheimer.Tracking" : historyKeyPrefix.Trim();
        public string StackDescription => stackDescription;
        public string ContextFiles => contextFiles;
        public bool IncludeAiPrompt => includeAiPrompt;
        public bool EnableDebugOverlay => enableDebugOverlay;
        public bool ShowInReleaseBuilds => showInReleaseBuilds;
        public KeyCode ToggleKey => toggleKey;
        public bool ShowFloatingButton => showFloatingButton;
        public float MobileScale => Mathf.Clamp(mobileScale, 0.75f, 3f);
        public float DesktopScale => Mathf.Clamp(desktopScale, 0.75f, 3f);
        public bool HasUsablePurposeString => !string.IsNullOrWhiteSpace(purposeString);

        /// <summary>True when the package is allowed to create the runtime overlay for this build.</summary>
        public bool IsOverlayAllowed =>
            enableDebugOverlay && (Application.isEditor || Debug.isDebugBuild || showInReleaseBuilds);

        public float DefaultOverlayScale =>
            Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer
                ? MobileScale
                : DesktopScale;

        public string OverlayScalePrefsKey => PrefsPrefix + "." + HistoryKeyPrefix;

        // ── Instance ─────────────────────────────────────────────────────────

        private static TrackingSettings instance;
        private static bool missingAssetLogged;

        /// <summary>
        /// The project settings asset, or a safe in-memory default when the asset does not exist.
        /// Use <see cref="Reload"/> after creating the asset during the same editor session.
        /// </summary>
        public static TrackingSettings Instance
        {
            get
            {
                if (instance != null)
                    return instance;

                instance = Resources.Load<TrackingSettings>(ResourcesPath);
                if (instance != null)
                    return instance;

                instance = CreateInstance<TrackingSettings>();
                instance.name = "TrackingSettings (defaults)";
                instance.hideFlags = HideFlags.HideAndDontSave;

                if (!missingAssetLogged)
                {
                    missingAssetLogged = true;
                    Debug.LogWarning(
                        "[Tracking] No TrackingSettings asset found at Resources/" + ResourcesPath +
                        ". Using defaults. Run Tools > Wagenheimer > Tracking > Create Settings Asset...");
                }

                return instance;
            }
        }

        /// <summary>Drops the cached instance so the next access re-reads Resources.</summary>
        public static void Reload()
        {
            instance = null;
        }

        /// <summary>Used by the editor/tests to inspect defaults without touching Resources.</summary>
        public static TrackingSettings CreateDefaults()
        {
            var settings = CreateInstance<TrackingSettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            return settings;
        }

        private void OnValidate()
        {
            initialDelaySeconds = Mathf.Max(0f, initialDelaySeconds);
            timeoutSeconds = Mathf.Max(1f, timeoutSeconds);
            retryDelaySeconds = Mathf.Max(0f, retryDelaySeconds);
            mobileScale = Mathf.Clamp(mobileScale, 0.75f, 3f);
            desktopScale = Mathf.Clamp(desktopScale, 0.75f, 3f);

            if (string.IsNullOrWhiteSpace(scriptingDefine))
                scriptingDefine = DefaultDefine;

            if (string.IsNullOrWhiteSpace(historyKeyPrefix))
                historyKeyPrefix = "Wagenheimer.Tracking";

            if (string.IsNullOrWhiteSpace(purposeString))
                purposeString = string.Empty;
        }
    }
}
