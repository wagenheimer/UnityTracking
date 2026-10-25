using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.Tracking.Editor
{
    /// <summary>
    /// UI Toolkit window that renders <see cref="TrackingChecker"/> findings. Each row carries the
    /// evidence, an optional fix action and a ready-to-paste AI prompt for failures.
    /// </summary>
    internal sealed class TrackingCheckerWindow : EditorWindow
    {
        internal const string MenuPath = "Tools/Wagenheimer/Tracking/Tracking Checker...";

        private static readonly Color ColorPass = new Color(0.298f, 0.851f, 0.392f);
        private static readonly Color ColorWarn = new Color(0.984f, 0.749f, 0.141f);
        private static readonly Color ColorFail = new Color(0.937f, 0.325f, 0.314f);
        private static readonly Color ColorInfo = new Color(0.278f, 0.827f, 0.902f);
        private static readonly Color ColorText = new Color(0.88f, 0.89f, 0.92f);
        private static readonly Color ColorMuted = new Color(0.62f, 0.65f, 0.71f);
        private static readonly Color ColorCard = new Color(1f, 1f, 1f, 0.055f);

        private List<Section> sections;
        private VisualElement body;
        private Label summary;

        [MenuItem(MenuPath, priority = 120)]
        internal static void Open()
        {
            var window = GetWindow<TrackingCheckerWindow>();
            window.titleContent = new GUIContent("Tracking Checker");
            window.minSize = new Vector2(780, 620);
            window.Show();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexGrow = 1;
            root.style.paddingLeft = 12;
            root.style.paddingRight = 12;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;

            root.Add(BuildHeader());

            summary = new Label();
            summary.style.fontSize = 12;
            summary.style.marginTop = 2;
            summary.style.marginBottom = 8;
            summary.style.whiteSpace = WhiteSpace.Normal;
            root.Add(summary);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            body = new VisualElement();
            scroll.Add(body);

            Run();
        }

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            var title = new Label("Tracking Helper — Checker");
            title.style.fontSize = 17;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = ColorText;
            header.Add(title);

            var version = new Label(TrackingPackageInfo.Version);
            version.style.marginLeft = 8;
            version.style.fontSize = 10;
            version.style.color = ColorMuted;
            header.Add(version);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            header.Add(spacer);

            header.Add(ToolbarButton("Refresh", Run, ColorInfo));
            header.Add(ToolbarButton("Copy report", CopyReport));
            header.Add(ToolbarButton("Copy for AI", CopyPrompt));
            header.Add(ToolbarButton("Docs", TrackingPackageInfo.OpenDocumentation));

            return header;
        }

        private static Button ToolbarButton(string text, Action action, Color? accent = null)
        {
            var button = new Button(action) { text = text };
            button.style.height = 24;
            button.style.marginLeft = 4;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
            button.style.fontSize = 11;

            if (accent.HasValue)
            {
                button.style.backgroundColor = new Color(accent.Value.r, accent.Value.g, accent.Value.b, 0.35f);
                button.style.color = ColorText;
            }

            return button;
        }

        private void Run()
        {
            sections = TrackingChecker.Run();
            Rebuild();
        }

        private void Rebuild()
        {
            if (body == null || sections == null)
                return;

            body.Clear();

            int pass = 0, warn = 0, fail = 0;
            foreach (var section in sections)
            {
                pass += section.Passed;
                warn += section.Warnings;
                fail += section.Failures;
            }

            summary.text = fail > 0
                ? $"{fail} failure(s) and {warn} warning(s) — fix the failures before shipping an ATT build."
                : warn > 0
                    ? $"{warn} warning(s) — the configuration works but review the items below."
                    : $"All automated checks passed ({pass}).";

            summary.style.color = fail > 0 ? ColorFail : warn > 0 ? ColorWarn : ColorPass;

            foreach (var section in sections)
                body.Add(BuildSection(section));
        }

        private VisualElement BuildSection(Section section)
        {
            var card = new VisualElement();
            card.style.backgroundColor = ColorCard;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 10;
            card.style.marginBottom = 10;
            SetRadius(card, 8);

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            card.Add(titleRow);

            var title = new Label(section.Title);
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = ColorText;
            titleRow.Add(title);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            titleRow.Add(spacer);

            var status = new Label(section.Failures > 0
                ? $"{section.Failures} FAIL"
                : section.Warnings > 0 ? $"{section.Warnings} WARN" : $"{section.Passed} PASS");
            status.style.fontSize = 10;
            status.style.unityFontStyleAndWeight = FontStyle.Bold;
            status.style.color = section.Failures > 0 ? ColorFail : section.Warnings > 0 ? ColorWarn : ColorPass;
            titleRow.Add(status);

            if (!string.IsNullOrEmpty(section.Description))
            {
                var description = new Label(section.Description);
                description.style.fontSize = 10.5f;
                description.style.color = ColorMuted;
                description.style.marginTop = 2;
                description.style.marginBottom = 6;
                card.Add(description);
            }

            foreach (var item in section.Items)
                card.Add(BuildRow(item));

            return card;
        }

        private VisualElement BuildRow(CheckResult item)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(1f, 1f, 1f, 0.06f);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            row.Add(header);

            var color = StatusColor(item.Status);
            var badge = new Label(StatusLabel(item.Status));
            badge.style.fontSize = 9.5f;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.color = color;
            badge.style.backgroundColor = new Color(color.r, color.g, color.b, 0.18f);
            badge.style.paddingLeft = 6;
            badge.style.paddingRight = 6;
            badge.style.paddingTop = 2;
            badge.style.paddingBottom = 2;
            badge.style.marginRight = 8;
            badge.style.minWidth = 42;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            SetRadius(badge, 3);
            header.Add(badge);

            var title = new Label(item.Title);
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = ColorText;
            title.style.flexGrow = 1;
            title.style.whiteSpace = WhiteSpace.Normal;
            header.Add(title);

            if (item.Action != null && !string.IsNullOrEmpty(item.ActionLabel))
            {
                var actionButton = new Button(() =>
                {
                    item.Action();
                    Run();
                })
                {
                    text = item.ActionLabel
                };
                actionButton.style.height = 20;
                actionButton.style.marginLeft = 6;
                actionButton.style.paddingLeft = 8;
                actionButton.style.paddingRight = 8;
                actionButton.style.fontSize = 10;
                header.Add(actionButton);
            }

            if (item.Status == CheckStatus.Fail || item.Status == CheckStatus.Warning)
            {
                var ai = new Button(() =>
                {
                    EditorGUIUtility.systemCopyBuffer = TrackingChecker.BuildPrompt(sections, item);
                    ShowNotification(new GUIContent("AI prompt copied"));
                })
                {
                    text = "Copy prompt"
                };
                ai.style.height = 20;
                ai.style.marginLeft = 4;
                ai.style.paddingLeft = 8;
                ai.style.paddingRight = 8;
                ai.style.fontSize = 10;
                header.Add(ai);
            }

            var detail = new Label(item.Detail);
            detail.style.fontSize = 11;
            detail.style.color = ColorMuted;
            detail.style.marginTop = 3;
            detail.style.whiteSpace = WhiteSpace.Normal;
            row.Add(detail);

            foreach (var fact in item.Facts)
            {
                var factLabel = new Label("- " + fact);
                factLabel.style.fontSize = 10;
                factLabel.style.color = new Color(0.55f, 0.58f, 0.64f);
                factLabel.style.marginTop = 1;
                factLabel.style.whiteSpace = WhiteSpace.Normal;
                row.Add(factLabel);
            }

            return row;
        }

        private void CopyReport()
        {
            EditorGUIUtility.systemCopyBuffer = TrackingChecker.BuildReport(sections);
            ShowNotification(new GUIContent("Checker report copied"));
        }

        private void CopyPrompt()
        {
            EditorGUIUtility.systemCopyBuffer = TrackingChecker.BuildPrompt(sections);
            ShowNotification(new GUIContent("AI prompt copied"));
        }

        private static Color StatusColor(CheckStatus status) => status switch
        {
            CheckStatus.Pass => ColorPass,
            CheckStatus.Warning => ColorWarn,
            CheckStatus.Fail => ColorFail,
            _ => ColorInfo
        };

        private static string StatusLabel(CheckStatus status) => status switch
        {
            CheckStatus.Pass => "PASS",
            CheckStatus.Warning => "WARN",
            CheckStatus.Fail => "FAIL",
            _ => "INFO"
        };

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
