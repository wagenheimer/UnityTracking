using UnityEditor;
using UnityEngine;

namespace Wagenheimer.Tracking.Editor
{
    /// <summary>
    /// Inspector for <see cref="TrackingSettings"/>: keeps the default fields but surfaces the
    /// validation and the checker entry point.
    /// </summary>
    [CustomEditor(typeof(TrackingSettings))]
    internal sealed class TrackingSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var settings = (TrackingSettings)target;

            EditorGUILayout.HelpBox(
                "Tracking Helper is the single owner of ATT: it requests the permission and writes " +
                "NSUserTrackingUsageDescription. Ad SDKs must wait for TrackingConsent.WaitUntilResolved().",
                MessageType.Info);

            DrawDefaultInspector();

            EditorGUILayout.Space(6);

            bool defineOn = TrackingChecker.HasDefine(
                TrackingChecker.IOSDefines(), settings.ScriptingDefine);
            bool declares = TrackingDeclaration.DeclaresTracking(
                settings.Enabled, settings.DeclarationMode, defineOn);

            string summary = declares
                ? "This configuration declares tracking: the purpose string will be written to the iOS build."
                : "This configuration does not declare tracking: the purpose string will be removed from the iOS build.";

            EditorGUILayout.HelpBox(
                summary + "\n" +
                $"iOS define {settings.ScriptingDefine}: {(defineOn ? "applied" : "not applied")}.",
                declares ? MessageType.Info : MessageType.Warning);

            string purposeIssue = TrackingChecker.ValidatePurposeString(settings.PurposeString);
            if (purposeIssue != null)
            {
                EditorGUILayout.HelpBox(
                    purposeIssue,
                    string.IsNullOrWhiteSpace(settings.PurposeString) ? MessageType.Error : MessageType.Warning);
            }

            if (settings.RequireTracking)
            {
                EditorGUILayout.HelpBox(
                    "ATT is mandatory in this project (requireTracking). A failing mandatory item blocks the " +
                    "iOS build, and the checker reports FAIL instead of WARN.\n" +
                    "Mandatory items: purpose string, build safety, the project requirements " +
                    "(TrackingRequirements) and the single-owner rules (plist key and ATT request).",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "ATT is advisory in this project. Turn on 'Require Tracking' to block the iOS build " +
                    "while a mandatory item fails.",
                    MessageType.None);
            }

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Checker", GUILayout.Height(26)))
                    TrackingCheckerWindow.Open();

                if (GUILayout.Button("Copy Report", GUILayout.Height(26)))
                {
                    EditorGUIUtility.systemCopyBuffer = TrackingChecker.BuildReport(TrackingChecker.Run());
                    Debug.Log("[Tracking] Checker report copied to the clipboard.");
                }
            }
        }
    }
}
