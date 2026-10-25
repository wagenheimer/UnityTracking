using UnityEditor;
using UnityEngine;

namespace Wagenheimer.Tracking.Editor
{
    internal static class TrackingMenuItems
    {
        private const string Root = "Tools/Wagenheimer/Tracking/";

        [MenuItem(Root + "Tracking Checker...", priority = 120)]
        private static void OpenChecker()
        {
            TrackingCheckerWindow.Open();
        }

        [MenuItem(Root + "Create Settings Asset", priority = 121)]
        private static void CreateSettings()
        {
            TrackingChecker.CreateSettingsAsset();
        }

        [MenuItem(Root + "Create Settings Asset", true)]
        private static bool ValidateCreateSettings()
        {
            return AssetDatabase.FindAssets("t:TrackingSettings").Length == 0;
        }

        [MenuItem(Root + "Copy Checker Report", priority = 122)]
        private static void CopyReport()
        {
            EditorGUIUtility.systemCopyBuffer = TrackingChecker.BuildReport(TrackingChecker.Run());
            Debug.Log("[Tracking] Checker report copied to the clipboard.");
        }

        [MenuItem(Root + "Documentation", priority = 123)]
        private static void OpenDocumentation()
        {
            TrackingPackageInfo.OpenDocumentation();
        }
    }
}
