using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.Tracking.Editor
{
    /// <summary>
    /// Batch-mode entry point for CI:
    /// <c>Unity -batchmode -quit -executeMethod Wagenheimer.Tracking.Editor.TrackingCheckerCli.Run</c>.
    /// Prints the checker report and exits with code 1 when any check fails.
    /// </summary>
    public static class TrackingCheckerCli
    {
        public static void Run()
        {
            var sections = TrackingChecker.Run();
            string report = TrackingChecker.BuildReport(sections);
            Debug.Log(report);

            int failures = sections.Sum(s => s.Failures);
            if (failures > 0)
            {
                Debug.LogError($"[Tracking] Checker found {failures} failure(s).");
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);

                return;
            }

            Debug.Log("[Tracking] Checker passed.");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        /// <summary>
        /// Batch-mode helper that disables the LevelPlay Helper's own ATT request so this package
        /// is the single owner. Safe to run when the package/prefab is absent.
        /// </summary>
        public static void FixLevelPlayAttRequest()
        {
            bool changed = TrackingChecker.DisableLevelPlayRequest();
            Debug.Log(changed
                ? "[Tracking] LevelPlay Helper ATT request disabled."
                : "[Tracking] Nothing to change (LevelPlay Helper not found or already disabled).");

            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
    }
}
