using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.Tracking
{
    /// <summary>
    /// Runtime UI Toolkit overlay for ATT diagnostics.
    ///
    /// Opaque card layout with word wrap and a real scroll view (nothing is clipped), a draggable
    /// header, `A-`/`A+` zoom persisted per device and a maximize/restore button. It answers
    /// "was ATT already asked?", shows the native and managed status, the local request history,
    /// the purpose string, the build identity and every diagnostic check. Project-specific checks
    /// registered with <see cref="TrackingDiagnostics.AddCheckProvider"/> appear automatically.
    /// </summary>
    [AddComponentMenu("Tools/Wagenheimer/Tracking/Tracking Debug Overlay")]
    [DisallowMultipleComponent]
    public sealed class TrackingDebugOverlay : MonoBehaviour
    {
        private const float MinScale = 0.75f;
        private const float MaxScale = 3f;
        private const float ScaleStep = 0.25f;
        private const float RefreshInterval = 0.4f;
        private const int SortingOrder = 32759;
        private static readonly Vector2Int BaseReference = new Vector2Int(1920, 1080);

        // ── Palette (same family as the other Wagenheimer debug overlays) ─────
        private static readonly Color ColorBackground = new Color(0.106f, 0.114f, 0.157f, 0.98f);
        private static readonly Color ColorPanel = new Color(0.161f, 0.173f, 0.227f, 1f);
        private static readonly Color ColorPanelBorder = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color ColorHeaderTo = new Color(0.204f, 0.463f, 0.902f, 1f);
        private static readonly Color ColorGreen = new Color(0.298f, 0.851f, 0.392f, 1f);
        private static readonly Color ColorAmber = new Color(0.984f, 0.749f, 0.141f, 1f);
        private static readonly Color ColorRed = new Color(0.937f, 0.325f, 0.314f, 1f);
        private static readonly Color ColorCyan = new Color(0.278f, 0.827f, 0.902f, 1f);
        private static readonly Color ColorOrange = new Color(1f, 0.596f, 0.145f, 1f);
        private static readonly Color ColorText = new Color(0.92f, 0.93f, 0.95f, 1f);
        private static readonly Color ColorTextMuted = new Color(0.62f, 0.65f, 0.71f, 1f);

        // ── State ────────────────────────────────────────────────────────────
        private TrackingSettings settings;
        private bool open;
        private bool maximized;
        private float scale = 1f;
        private float nextRefresh;
        private string checksSignature = string.Empty;
        private string lastAction = string.Empty;
        private float lastActionUntil;
        private bool hadFocus = true;

        private bool dragging;
        private Vector2 dragStartPointer;
        private Vector2 dragStartWindowPos;

        private PanelSettings panelSettings;
        private UIDocument document;
        private VisualElement root;
        private VisualElement floatingButton;
        private VisualElement window;
        private Button maxButton;
        private Label zoomLabel;
        private Label lastActionLabel;

        private Pill verdictPill;
        private VisualElement diagnosisCard;
        private Label diagnosisTitle;
        private Label diagnosisDetail;
        private Label diagnosisFix;

        private Pill attStatePill;
        private Pill attAskedPill;
        private Pill attFrameworkPill;
        private Pill attPurposePill;
        private Label attNativeValue;
        private Label attManagedValue;
        private Label attResolvedValue;
        private Label attHistoryValue;
        private Label attFirstValue;
        private Label attLastValue;
        private Label attPurposeValue;
        private VisualElement attBlockedRow;
        private Label attBlockedValue;
        private Label attNote;
        private Button requestButton;
        private Button settingsButton;

        private Label envLabel;
        private Label envSubLabel;
        private VisualElement checksContainer;
        private Label checksTitle;

        private bool CanShow => settings != null && settings.Enabled && settings.IsOverlayAllowed;

        /// <summary>Creates the overlay if one does not exist. Used by the bootstrap.</summary>
        public static TrackingDebugOverlay CreateIfNeeded()
        {
            var existing = FindAnyObjectByType<TrackingDebugOverlay>();
            if (existing != null)
                return existing;

            var host = new GameObject("[WagenheimerTrackingOverlay]");
            DontDestroyOnLoad(host);
            return host.AddComponent<TrackingDebugOverlay>();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            settings = TrackingSettings.Instance;

            if (!CanShow)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);

            scale = Mathf.Clamp(
                PlayerPrefs.GetFloat(settings.OverlayScalePrefsKey, settings.DefaultOverlayScale),
                MinScale,
                MaxScale);

            BuildDocument();
            BuildUI();
            SetOpen(false);
        }

        private void OnEnable()
        {
            TrackingConsent.Changed += HandleConsentChanged;
        }

        private void OnDisable()
        {
            TrackingConsent.Changed -= HandleConsentChanged;
        }

        private void OnDestroy()
        {
            TrackingConsent.Changed -= HandleConsentChanged;

            if (document != null)
                document.panelSettings = null;

            if (panelSettings != null)
            {
                Destroy(panelSettings);
                panelSettings = null;
            }
        }

        private void Update()
        {
            if (!CanShow)
                return;

            if (Input.GetKeyDown(settings.ToggleKey))
                SetOpen(!open);

            // Coming back from Settings may have changed the permission.
            bool focused = Application.isFocused;
            if (focused && !hadFocus && open)
            {
                TrackingConsent.RefreshStatus();
                RefreshAll();
            }
            hadFocus = focused;

            if (Time.unscaledTime < nextRefresh)
                return;

            nextRefresh = Time.unscaledTime + RefreshInterval;

            if (open)
                RefreshAll();

            UpdateExpiredAction();
        }

        private void HandleConsentChanged()
        {
            if (open)
                RefreshAll();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Document / panel
        // ─────────────────────────────────────────────────────────────────────

        private void BuildDocument()
        {
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "WagenheimerTrackingPanelSettings";
            panelSettings.hideFlags = HideFlags.HideAndDontSave;
            panelSettings.themeStyleSheet = ResolveTheme();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = BaseReference;
            panelSettings.match = 0.5f;
            panelSettings.clearColor = false;
            panelSettings.sortingOrder = SortingOrder;

            ApplyScaleToPanel();

            var host = new GameObject("WagenheimerTrackingOverlayUI");
            host.SetActive(false);
            host.transform.SetParent(transform, false);

            document = host.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = SortingOrder;

            host.SetActive(true);
            root = document.rootVisualElement;
        }

        /// <summary>
        /// Resolves the theme shipped with the package. In a player build no ThemeStyleSheet is
        /// loaded by default, so a runtime PanelSettings needs one to render text and controls.
        /// </summary>
        private static ThemeStyleSheet ResolveTheme()
        {
            var theme = Resources.Load<ThemeStyleSheet>("WagenheimerTrackingTheme");
            if (theme != null)
                return theme;

            var all = Resources.FindObjectsOfTypeAll<ThemeStyleSheet>();
            return all != null && all.Length > 0 ? all[0] : null;
        }

        private void ApplyScaleToPanel()
        {
            if (panelSettings == null)
                return;

            panelSettings.referenceResolution = new Vector2Int(
                Mathf.RoundToInt(BaseReference.x / scale),
                Mathf.RoundToInt(BaseReference.y / scale));

            if (zoomLabel != null)
                zoomLabel.text = $"{scale:0.##}x";
        }

        private void SetScale(float value)
        {
            scale = Mathf.Clamp(Mathf.Round(value / ScaleStep) * ScaleStep, MinScale, MaxScale);
            PlayerPrefs.SetFloat(settings.OverlayScalePrefsKey, scale);
            PlayerPrefs.Save();
            ApplyScaleToPanel();
        }

        private void SetOpen(bool value)
        {
            open = value;

            if (floatingButton != null)
                floatingButton.style.display = open || !settings.ShowFloatingButton ? DisplayStyle.None : DisplayStyle.Flex;

            if (window != null)
                window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

            if (open)
                RefreshAll();
        }

        // ─────────────────────────────────────────────────────────────────────
        // UI
        // ─────────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            if (root == null)
                return;

            root.Clear();
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.right = 0;
            root.style.bottom = 0;
            root.pickingMode = PickingMode.Ignore;

            floatingButton = BuildFloatingButton();
            root.Add(floatingButton);

            window = BuildWindow();
            root.Add(window);
        }

        private VisualElement BuildFloatingButton()
        {
            var button = new Button(() => SetOpen(true)) { text = "TRACK DBG" };
            button.style.position = Position.Absolute;
            button.style.left = 18;
            button.style.top = 18;
            button.style.height = 34;
            button.style.paddingLeft = 12;
            button.style.paddingRight = 12;
            button.style.fontSize = 11.5f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.color = Color.white;
            SetBackground(button, new Color(0.12f, 0.12f, 0.15f, 0.94f));
            SetRadius(button, 17);
            SetBorder(button, ColorCyan, 1);
            return button;
        }

        private VisualElement BuildWindow()
        {
            var element = new VisualElement();
            element.name = "WagenheimerTrackingWindow";
            element.pickingMode = PickingMode.Position;
            element.style.position = Position.Absolute;
            element.style.backgroundColor = ColorBackground;
            element.style.overflow = Overflow.Hidden;
            SetRadius(element, 10);
            SetBorder(element, new Color(0.25f, 0.26f, 0.32f), 1);

            window = element;
            element.Add(BuildHeader());

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = 12;
            scroll.style.paddingRight = 12;
            scroll.style.paddingTop = 10;
            scroll.style.paddingBottom = 12;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            scroll.Add(BuildDiagnosisCard());
            scroll.Add(BuildAttCard());
            scroll.Add(BuildEnvironmentCard());
            scroll.Add(BuildChecksCard());

            element.Add(scroll);

            ApplyWindowLayout();
            return element;
        }

        private void ApplyWindowLayout()
        {
            if (window == null)
                return;

            var style = window.style;

            if (maximized)
            {
                style.left = 0;
                style.top = 0;
                style.right = 0;
                style.bottom = 0;
                style.width = StyleKeyword.Auto;
                style.height = StyleKeyword.Auto;
                style.maxWidth = Length.Percent(100);
                style.maxHeight = Length.Percent(100);
                return;
            }

            style.left = 24;
            style.top = 30;
            style.right = StyleKeyword.Auto;
            style.bottom = StyleKeyword.Auto;
            style.width = Length.Percent(94);
            style.maxWidth = 720;
            style.height = StyleKeyword.Auto;
            style.maxHeight = Length.Percent(90);
        }

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.height = 38;
            header.style.paddingLeft = 12;
            header.style.paddingRight = 8;
            SetBackground(header, new Color(0.13f, 0.14f, 0.18f));
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.22f, 0.23f, 0.28f);

            var title = new Label("ATT / Tracking");
            title.style.fontSize = 13.5f;
            title.style.color = Color.white;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexShrink = 1;
            header.Add(title);

            verdictPill = new Pill(10f);
            verdictPill.Element.style.marginLeft = 8;
            header.Add(verdictPill.Element);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            header.Add(spacer);

            header.Add(HeaderButton("A-", () => SetScale(scale - ScaleStep)));
            header.Add(HeaderButton("A+", () => SetScale(scale + ScaleStep)));

            zoomLabel = new Label($"{scale:0.##}x");
            zoomLabel.style.minWidth = 36;
            zoomLabel.style.fontSize = 10;
            zoomLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            zoomLabel.style.color = ColorTextMuted;
            header.Add(zoomLabel);

            maxButton = HeaderButton("Max", ToggleMaximize);
            header.Add(maxButton);

            header.Add(HeaderButton("X", () => SetOpen(false)));

            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || maximized)
                    return;

                dragging = true;
                dragStartPointer = evt.position;
                dragStartWindowPos = new Vector2(window.resolvedStyle.left, window.resolvedStyle.top);
                header.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            header.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging)
                    return;

                Vector2 delta = (Vector2)evt.position - dragStartPointer;
                float maxLeft = Mathf.Max(0f, root.resolvedStyle.width - window.resolvedStyle.width);
                float maxTop = Mathf.Max(0f, root.resolvedStyle.height - window.resolvedStyle.height);

                window.style.left = Mathf.Clamp(dragStartWindowPos.x + delta.x, 0f, maxLeft);
                window.style.top = Mathf.Clamp(dragStartWindowPos.y + delta.y, 0f, maxTop);
                evt.StopPropagation();
            });

            header.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging)
                    return;

                dragging = false;
                header.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });

            return header;
        }

        private static Button HeaderButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 22;
            button.style.minWidth = 30;
            button.style.marginLeft = 4;
            button.style.paddingLeft = 6;
            button.style.paddingRight = 6;
            button.style.paddingTop = 0;
            button.style.paddingBottom = 0;
            button.style.fontSize = 10.5f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.color = Color.white;
            SetBackground(button, new Color(1f, 1f, 1f, 0.16f));
            SetRadius(button, 4);

            // The header drag starts on PointerDown; a button press must not start it.
            button.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            return button;
        }

        private void ToggleMaximize()
        {
            maximized = !maximized;

            if (maxButton != null)
                maxButton.text = maximized ? "Rest" : "Max";

            ApplyWindowLayout();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Cards
        // ─────────────────────────────────────────────────────────────────────

        private VisualElement BuildDiagnosisCard()
        {
            diagnosisCard = Card();

            diagnosisTitle = Body();
            diagnosisTitle.style.fontSize = 13;
            diagnosisTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            diagnosisCard.Add(diagnosisTitle);

            diagnosisDetail = Body();
            diagnosisDetail.style.fontSize = 11.5f;
            diagnosisDetail.style.marginTop = 3;
            diagnosisCard.Add(diagnosisDetail);

            diagnosisFix = Muted();
            diagnosisFix.style.color = ColorAmber;
            diagnosisFix.style.marginTop = 4;
            diagnosisCard.Add(diagnosisFix);

            var actions = Row();
            actions.style.marginTop = 8;
            actions.Add(ActionButton("Copy report", ColorHeaderTo,
                () => Copy(TrackingDiagnostics.BuildReport(), "Report")));

            if (settings.IncludeAiPrompt)
            {
                actions.Add(ActionButton("Copy for AI", ColorCyan,
                    () => Copy(TrackingDiagnostics.BuildAiPrompt(), "AI prompt")));
            }

            actions.Add(ActionButton("Refresh", ColorTextMuted, RefreshFromSystem));
            diagnosisCard.Add(actions);

            lastActionLabel = Muted();
            lastActionLabel.style.color = ColorGreen;
            lastActionLabel.style.marginTop = 4;
            lastActionLabel.style.display = DisplayStyle.None;
            diagnosisCard.Add(lastActionLabel);

            return diagnosisCard;
        }

        private VisualElement BuildAttCard()
        {
            var card = Card();
            card.Add(SectionTitle("APP TRACKING TRANSPARENCY (ATT)"));

            var pills = Row(true);
            attStatePill = new Pill();
            attAskedPill = new Pill();
            attFrameworkPill = new Pill();
            attPurposePill = new Pill();
            pills.Add(attStatePill.Element);
            pills.Add(attAskedPill.Element);
            pills.Add(attFrameworkPill.Element);
            pills.Add(attPurposePill.Element);
            card.Add(pills);

            card.Add(InfoRow("Native status", out attNativeValue));
            card.Add(InfoRow("Managed status", out attManagedValue));
            card.Add(InfoRow("Boot wait", out attResolvedValue));
            card.Add(InfoRow("Attempts", out attHistoryValue));
            card.Add(InfoRow("First request", out attFirstValue));
            card.Add(InfoRow("Last request", out attLastValue));
            card.Add(InfoRow("Purpose string", out attPurposeValue));

            attBlockedRow = InfoRow("Blocked", out attBlockedValue);
            attBlockedRow.style.display = DisplayStyle.None;
            card.Add(attBlockedRow);

            var buttons = Row(true);
            buttons.style.marginTop = 8;
            requestButton = ActionButton("Request ATT again", ColorOrange, RequestAgain);
            buttons.Add(requestButton);

            settingsButton = ActionButton("Open app settings", ColorHeaderTo, OpenSettings);
            buttons.Add(settingsButton);

            buttons.Add(ActionButton("Read status", ColorCyan, RefreshFromSystem));
            card.Add(buttons);

            var copyRow = Row(true);
            copyRow.style.marginTop = 2;
            copyRow.Add(ActionButton("Copy ATT status", ColorGreen,
                () => Copy(TrackingDiagnostics.BuildAttStatusText(), "ATT status")));
            copyRow.Add(ActionButton("Clear local history", ColorTextMuted, ResetHistory));
            card.Add(copyRow);

            attNote = Muted();
            attNote.style.marginTop = 6;
            card.Add(attNote);

            return card;
        }

        private VisualElement BuildEnvironmentCard()
        {
            var card = Card();
            card.Add(SectionTitle("ENVIRONMENT / BUILD"));

            envLabel = Body();
            envLabel.style.fontSize = 11.5f;
            card.Add(envLabel);

            envSubLabel = Muted();
            envSubLabel.style.marginTop = 4;
            card.Add(envSubLabel);

            return card;
        }

        private VisualElement BuildChecksCard()
        {
            var card = Card();
            checksTitle = SectionTitle("CHECKS");
            card.Add(checksTitle);

            checksContainer = new VisualElement();
            card.Add(checksContainer);

            return card;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Refresh
        // ─────────────────────────────────────────────────────────────────────

        private void RefreshAll()
        {
            if (root == null)
                return;

            var checks = TrackingDiagnostics.GetChecks();
            var att = TrackingDiagnostics.GetAttSnapshot();

            UpdateVerdict(checks);
            UpdateAtt(att);
            UpdateEnvironment(att);
            UpdateChecks(checks);
        }

        private void UpdateVerdict(List<TrackingDiagnostics.Check> checks)
        {
            int errors = 0, warnings = 0;
            TrackingDiagnostics.Check firstProblem = null;

            foreach (var check in checks)
            {
                if (check.Status == TrackingDiagnostics.CheckStatus.Error)
                {
                    errors++;
                    if (firstProblem == null || firstProblem.Status != TrackingDiagnostics.CheckStatus.Error)
                        firstProblem = check;
                }
                else if (check.Status == TrackingDiagnostics.CheckStatus.Warning)
                {
                    warnings++;
                    if (firstProblem == null)
                        firstProblem = check;
                }
            }

            Color color;
            string label;
            string title;
            string detail;
            string fix = string.Empty;

            if (errors > 0)
            {
                color = ColorRed;
                label = "FAIL";
                title = $"{errors} problem(s) to fix" + (warnings > 0 ? $" + {warnings} warning(s)" : string.Empty);
                detail = firstProblem != null ? $"{firstProblem.Title}: {firstProblem.Detail}" : string.Empty;
                fix = firstProblem != null ? firstProblem.Fix : string.Empty;
            }
            else if (warnings > 0)
            {
                color = ColorAmber;
                label = "WARN";
                title = $"{warnings} warning(s)";
                detail = firstProblem != null ? $"{firstProblem.Title}: {firstProblem.Detail}" : string.Empty;
                fix = firstProblem != null ? firstProblem.Fix : string.Empty;
            }
            else if (!TrackingConsent.IsRequired)
            {
                color = ColorTextMuted;
                label = "N/A";
                title = "ATT does not apply to this build";
                detail = "This build does not declare tracking (no purpose string in the bundle). " +
                         "The panel still reports the build identity and the environment.";
            }
            else
            {
                color = ColorGreen;
                label = "OK";
                title = "ATT ready";
                detail = "Framework, purpose string and the player's answer are consistent.";
            }

            verdictPill.Set(label, color);
            diagnosisTitle.text = title;
            diagnosisTitle.style.color = color;
            diagnosisDetail.text = detail;
            diagnosisFix.text = string.IsNullOrEmpty(fix) ? string.Empty : $"Fix: {fix}";
            diagnosisFix.style.display = string.IsNullOrEmpty(fix) ? DisplayStyle.None : DisplayStyle.Flex;

            SetBackground(diagnosisCard, Mix(ColorPanel, color, 0.12f));
            SetBorder(diagnosisCard, Mix(ColorPanelBorder, color, 0.35f), 1);

            if (floatingButton != null)
                SetBorder(floatingButton, color, 1);
        }

        private void UpdateAtt(TrackingDiagnostics.AttSnapshot att)
        {
            if (!att.Enabled)
            {
                attStatePill.Set("DISABLED", ColorTextMuted);
                attAskedPill.Set("ASKED: N/A", ColorTextMuted);
                attFrameworkPill.Set("FRAMEWORK N/A", ColorTextMuted);
                attPurposePill.Set("PLIST N/A", ColorTextMuted);
            }
            else if (!att.Required)
            {
                attStatePill.Set("ATT N/A", ColorTextMuted);
                attAskedPill.Set("ASKED: N/A", ColorTextMuted);
                attFrameworkPill.Set(att.FrameworkAvailable ? "FRAMEWORK OK" : "FRAMEWORK N/A",
                    att.FrameworkAvailable ? ColorGreen : ColorTextMuted);
                attPurposePill.Set(att.HasPurposeString ? "PLIST OK" : "PLIST N/A",
                    att.HasPurposeString ? ColorGreen : ColorTextMuted);
            }
            else if (att.Requesting)
            {
                attStatePill.Set("REQUESTING...", ColorAmber);
                attAskedPill.Set("REQUEST IN PROGRESS", ColorAmber);
                attFrameworkPill.Set(att.FrameworkAvailable ? "FRAMEWORK OK" : "FRAMEWORK MISSING",
                    att.FrameworkAvailable ? ColorGreen : ColorRed);
                attPurposePill.Set(att.HasPurposeString ? "PLIST OK" : "PLIST MISSING",
                    att.HasPurposeString ? ColorGreen : ColorRed);
            }
            else
            {
                attStatePill.Set(StatusPillText(att.NativeStatus), StatusPillColor(att.NativeStatus));

                if (att.Answered)
                    attAskedPill.Set("ASKED: YES", ColorGreen);
                else if (att.WasRequested)
                    attAskedPill.Set($"SENT, NO ANSWER ({att.RequestCount}x)", ColorAmber);
                else if (!att.Resolved)
                    attAskedPill.Set("WAITING TO ASK", ColorTextMuted);
                else
                    attAskedPill.Set("NOT ASKED", ColorAmber);

                attFrameworkPill.Set(att.FrameworkAvailable ? "FRAMEWORK OK" : "FRAMEWORK MISSING",
                    att.FrameworkAvailable ? ColorGreen : ColorRed);
                attPurposePill.Set(att.HasPurposeString ? "PLIST OK" : "PLIST MISSING",
                    att.HasPurposeString ? ColorGreen : ColorRed);
            }

            attNativeValue.text = att.NativeStatus == TrackingDiagnostics.NativeAttStatus.Unavailable
                ? "unavailable on this platform"
                : $"{att.NativeStatus} ({(int)att.NativeStatus})";
            attManagedValue.text = att.ManagedState.ToString();
            attResolvedValue.text = att.Resolved
                ? "finished — ad SDKs may initialize"
                : "waiting for the player's answer";
            attHistoryValue.text = att.WasRequested
                ? $"{att.RequestCount} attempt(s) recorded on this device"
                : "none recorded since the install";
            attFirstValue.text = FormatLocal(att.FirstRequestUtc);
            attLastValue.text = FormatLocal(att.LastRequestUtc);
            attPurposeValue.text = att.HasPurposeString ? $"\"{att.PurposeString}\"" : "missing from Info.plist";

            attBlockedRow.style.display = att.Blocked ? DisplayStyle.Flex : DisplayStyle.None;
            attBlockedValue.text = att.BlockedReason;

            requestButton.SetEnabled(att.CanRequest && !att.Requesting);
            requestButton.text = att.Requesting ? "Requesting..." : "Request ATT again";

            settingsButton.SetEnabled(Application.platform == RuntimePlatform.IPhonePlayer);

            attNote.text = BuildAttNote(att);
        }

        private static string BuildAttNote(TrackingDiagnostics.AttSnapshot att)
        {
            if (!att.Enabled)
                return "The package is disabled in the settings (master switch off).";

            if (!att.Required)
                return "ATT is not required for this build. The overlay still reports the build and the checks.";

            if (att.NativeStatus == TrackingDiagnostics.NativeAttStatus.Unavailable)
                return "ATTrackingManager is not available in this app. Check the weak link of AppTrackingTransparency.framework.";

            if (!att.HasPurposeString)
                return "Without NSUserTrackingUsageDescription iOS terminates the app when ATT is requested — the request was blocked on purpose.";

            if (!att.Resolved && !att.WasRequested && !att.Requesting)
                return "The boot request is about to run (the game waits for the first frame + the initial delay so the alert has a window to appear).";

            if (att.Answered)
                return "iOS has a final answer and does NOT show the alert again. 'Request ATT again' re-sends the request " +
                       "(returns immediately, no alert) and re-reads the status. To see the alert again: Settings > Privacy & " +
                       "Security > Tracking (allow requests) or delete and reinstall the app.";

            if (att.WasRequested)
                return "The request was sent, but iOS is still NotDetermined. If the alert did not appear, confirm that the app " +
                       "was foreground, that the purpose string is in Info.plist, and check the [Tracking] log on the device. " +
                       "You can re-send it with the button above.";

            return "No ATT request was recorded on this device. Use 'Request ATT again' to trigger the alert now; " +
                   "the [Tracking] log shows the full sequence.";
        }

        private void UpdateEnvironment(TrackingDiagnostics.AttSnapshot att)
        {
            var builder = new StringBuilder();
            builder.Append("App: ").Append(Application.identifier);
            builder.Append("    Version: ").Append(Application.version);
            builder.Append(" (").Append(string.IsNullOrEmpty(TrackingDiagnostics.BundleBuildNumber)
                ? "?"
                : TrackingDiagnostics.BundleBuildNumber).Append(')');
            builder.Append('\n').Append("Platform: ").Append(Application.platform);
            builder.Append("    OS: ").Append(SystemInfo.operatingSystem);
            builder.Append('\n').Append("Device: ").Append(SystemInfo.deviceModel);
            builder.Append('\n').Append("Declares tracking: ").Append(att.HasPurposeString ? "yes" : "no");
            builder.Append("    Build: ").Append(Debug.isDebugBuild ? "development" : "release");
            builder.Append('\n').Append("Declaration mode: ").Append(settings.DeclarationMode);
            builder.Append("    Requirement: ").Append(settings.RequirementMode);
            envLabel.text = builder.ToString();

            envSubLabel.text =
                $"Define: {settings.ScriptingDefine}" +
                $"    Framework: {(att.FrameworkAvailable ? "loaded" : "not loaded")}" +
                $"    Purpose string: {(att.HasPurposeString ? "present" : "missing")}";
        }

        private void UpdateChecks(List<TrackingDiagnostics.Check> checks)
        {
            if (checksContainer == null)
                return;

            var signature = new StringBuilder();
            foreach (var check in checks)
            {
                signature.Append((int)check.Status)
                    .Append(check.Title)
                    .Append(check.Detail)
                    .Append(check.Fix)
                    .Append('|');
            }

            string value = signature.ToString();
            checksTitle.text = $"CHECKS ({checks.Count})";

            if (value == checksSignature)
                return;

            checksSignature = value;
            checksContainer.Clear();

            foreach (var check in checks)
                checksContainer.Add(BuildCheckRow(check));
        }

        private VisualElement BuildCheckRow(TrackingDiagnostics.Check check)
        {
            Color color = StatusColor(check.Status);

            var row = new VisualElement();
            row.style.backgroundColor = Mix(ColorPanel, color, 0.10f);
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.marginBottom = 5;
            SetRadius(row, 6);
            SetBorder(row, Mix(ColorPanelBorder, color, 0.30f), 1);

            var header = Row();
            header.style.alignItems = Align.Center;

            var badge = new Pill(10f);
            badge.Set(check.StatusLabel, color);
            badge.Element.style.minWidth = 52;
            header.Add(badge.Element);

            var title = new Label(check.Title);
            title.style.marginLeft = 6;
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = ColorText;
            title.style.flexGrow = 1;
            title.style.whiteSpace = WhiteSpace.Normal;
            header.Add(title);

            if (check.IsProblem)
            {
                var copy = TinyButton("Copy");
                copy.clicked += () => Copy(TrackingDiagnostics.BuildCheckText(check), check.Title);
                header.Add(copy);

                if (settings.IncludeAiPrompt)
                {
                    var ai = TinyButton("AI");
                    ai.clicked += () => Copy(TrackingDiagnostics.BuildAiPrompt(check), $"AI prompt ({check.Title})");
                    header.Add(ai);
                }
            }

            row.Add(header);

            var detail = Muted();
            detail.text = check.Detail;
            detail.style.marginTop = 3;
            row.Add(detail);

            if (check.IsProblem && !string.IsNullOrEmpty(check.Fix))
            {
                var fix = Muted();
                fix.text = $"Fix: {check.Fix}";
                fix.style.color = ColorAmber;
                fix.style.marginTop = 3;
                row.Add(fix);
            }

            return row;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Actions
        // ─────────────────────────────────────────────────────────────────────

        private void RefreshFromSystem()
        {
            TrackingConsent.RefreshStatus();
            RefreshAll();
            ShowAction("Status re-read from the system");
        }

        private void RequestAgain()
        {
            if (TrackingConsent.RequestAgain())
            {
                ShowAction("ATT request re-sent — see the [Tracking] log");
                RefreshAll();
            }
            else
            {
                ShowAction("Could not re-send (request in progress or ATT unavailable)");
            }
        }

        private void OpenSettings()
        {
            if (TrackingDiagnostics.OpenAppSettings())
                ShowAction("Opening the app Settings page");
            else
                ShowAction("Settings is only available on an iOS device");
        }

        private void ResetHistory()
        {
            TrackingConsent.ResetHistory();
            checksSignature = string.Empty;
            RefreshAll();
            ShowAction("Local history cleared (the iOS permission is unchanged)");
        }

        private void Copy(string text, string what)
        {
            TrackingDiagnostics.CopyToClipboard(text);
            ShowAction($"{what} copied");
        }

        private void ShowAction(string message)
        {
            lastAction = message;
            lastActionUntil = Time.unscaledTime + 3f;

            if (lastActionLabel != null)
            {
                lastActionLabel.text = message;
                lastActionLabel.style.display = DisplayStyle.Flex;
            }
        }

        private void UpdateExpiredAction()
        {
            if (lastActionLabel == null || string.IsNullOrEmpty(lastAction))
                return;

            if (Time.unscaledTime < lastActionUntil)
                return;

            lastAction = string.Empty;
            lastActionLabel.style.display = DisplayStyle.None;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Widgets
        // ─────────────────────────────────────────────────────────────────────

        private sealed class Pill
        {
            public readonly VisualElement Element;
            private readonly Label label;

            public Pill(float fontSize = 10.5f)
            {
                Element = new VisualElement();
                Element.style.paddingLeft = 7;
                Element.style.paddingRight = 7;
                Element.style.paddingTop = 3;
                Element.style.paddingBottom = 3;
                Element.style.marginRight = 6;
                Element.style.marginBottom = 4;
                Element.style.flexDirection = FlexDirection.Row;
                Element.style.alignItems = Align.Center;
                SetRadius(Element, 4);

                label = new Label();
                label.style.fontSize = fontSize;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = Color.white;
                Element.Add(label);
            }

            public void Set(string text, Color color)
            {
                label.text = text;
                label.style.color = color;
                Element.style.backgroundColor = new Color(color.r, color.g, color.b, 0.20f);
                SetBorder(Element, new Color(color.r, color.g, color.b, 0.55f), 1);
            }
        }

        private static VisualElement Card()
        {
            var card = new VisualElement();
            card.style.backgroundColor = ColorPanel;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 9;
            card.style.marginBottom = 8;
            SetRadius(card, 8);
            SetBorder(card, ColorPanelBorder, 1);
            return card;
        }

        private static Label SectionTitle(string text)
        {
            var label = new Label(text);
            label.style.color = ColorCyan;
            label.style.fontSize = 11.5f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 5;
            return label;
        }

        private static Label Body()
        {
            var label = new Label();
            label.style.color = ColorText;
            label.style.fontSize = 12;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static Label Muted()
        {
            var label = new Label();
            label.style.color = ColorTextMuted;
            label.style.fontSize = 11;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static VisualElement Row(bool wrap = false)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = wrap ? Wrap.Wrap : Wrap.NoWrap;
            row.style.alignItems = Align.Center;
            return row;
        }

        private static VisualElement InfoRow(string label, out Label value)
        {
            var row = Row();
            row.style.marginBottom = 2;
            row.style.alignItems = Align.FlexStart;

            var key = new Label(label);
            key.style.width = 118;
            key.style.flexShrink = 0;
            key.style.fontSize = 11;
            key.style.color = ColorTextMuted;
            row.Add(key);

            value = new Label("-");
            value.style.flexGrow = 1;
            value.style.fontSize = 11.5f;
            value.style.color = ColorText;
            value.style.whiteSpace = WhiteSpace.Normal;
            row.Add(value);

            return row;
        }

        private static Button ActionButton(string text, Color color, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.flexGrow = 1;
            button.style.flexBasis = 0;
            button.style.minWidth = 96;
            button.style.marginRight = 4;
            button.style.marginTop = 3;
            button.style.paddingLeft = 8;
            button.style.paddingRight = 8;
            button.style.paddingTop = 6;
            button.style.paddingBottom = 6;
            button.style.fontSize = 11;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.color = Color.white;
            SetBackground(button, color);
            SetRadius(button, 4);
            return button;
        }

        private static Button TinyButton(string text)
        {
            var button = new Button { text = text };
            button.style.marginLeft = 4;
            button.style.paddingLeft = 6;
            button.style.paddingRight = 6;
            button.style.paddingTop = 2;
            button.style.paddingBottom = 2;
            button.style.fontSize = 9.5f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.color = Color.white;
            SetBackground(button, new Color(1f, 1f, 1f, 0.14f));
            SetRadius(button, 3);
            return button;
        }

        private static void SetBackground(VisualElement element, Color color)
        {
            element.style.backgroundColor = color;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private static void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }

        private static Color Mix(Color baseColor, Color tint, float amount)
        {
            return new Color(
                Mathf.Lerp(baseColor.r, tint.r, amount),
                Mathf.Lerp(baseColor.g, tint.g, amount),
                Mathf.Lerp(baseColor.b, tint.b, amount),
                baseColor.a);
        }

        private static Color StatusColor(TrackingDiagnostics.CheckStatus status) => status switch
        {
            TrackingDiagnostics.CheckStatus.Ok => ColorGreen,
            TrackingDiagnostics.CheckStatus.Warning => ColorAmber,
            TrackingDiagnostics.CheckStatus.Error => ColorRed,
            _ => ColorTextMuted
        };

        private static string StatusPillText(TrackingDiagnostics.NativeAttStatus status) => status switch
        {
            TrackingDiagnostics.NativeAttStatus.Authorized => "AUTHORIZED",
            TrackingDiagnostics.NativeAttStatus.Denied => "DENIED",
            TrackingDiagnostics.NativeAttStatus.Restricted => "RESTRICTED",
            TrackingDiagnostics.NativeAttStatus.NotDetermined => "NOT DETERMINED",
            _ => "UNAVAILABLE"
        };

        private static Color StatusPillColor(TrackingDiagnostics.NativeAttStatus status) => status switch
        {
            TrackingDiagnostics.NativeAttStatus.Authorized => ColorGreen,
            TrackingDiagnostics.NativeAttStatus.Denied => ColorAmber,
            TrackingDiagnostics.NativeAttStatus.Restricted => ColorAmber,
            TrackingDiagnostics.NativeAttStatus.NotDetermined => ColorOrange,
            _ => ColorRed
        };

        private static string FormatLocal(DateTime? utc) =>
            utc.HasValue ? utc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "never";
    }
}
