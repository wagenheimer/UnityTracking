using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace Wagenheimer.Tracking.Editor
{
    /// <summary>
    /// Single owner of <c>NSUserTrackingUsageDescription</c>.
    ///
    /// Pre-build: fails a build that declares tracking but cannot produce a valid key (missing
    /// purpose string, missing iOS binding or missing native plugin).
    ///
    /// Post-build (order 1001, after ad SDK processors): writes or removes the key, verifies the
    /// generated plist and records the result for <see cref="TrackingChecker"/>.
    /// </summary>
    internal sealed class TrackingBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string PurposeKey = "NSUserTrackingUsageDescription";

        public int callbackOrder => 1001;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;

            var settings = TrackingSettings.Instance;
            var target = NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(report.summary.platform));

            bool definePresent = TrackingChecker.HasDefine(
                PlayerSettings.GetScriptingDefineSymbols(target), settings.ScriptingDefine);

            bool declares = TrackingDeclaration.DeclaresTracking(
                settings.Enabled, settings.DeclarationMode, definePresent);

            if (!declares)
            {
                Debug.Log(
                    $"[Tracking] This iOS build does not declare tracking ({settings.DeclarationMode}" +
                    (settings.DeclarationMode == TrackingDeclarationMode.ScriptingDefine
                        ? $", define '{settings.ScriptingDefine}' absent"
                        : string.Empty) +
                    "). The purpose string will be " +
                    (settings.RemoveWhenNotDeclared ? "removed." : "kept."));
                return;
            }

            string issue = TrackingChecker.ValidatePurposeString(settings.PurposeString);
            if (issue != null)
                Report($"This iOS build declares tracking but the purpose string is invalid: {issue}", settings);

            if (Type.GetType("Unity.Advertisement.IosSupport.ATTrackingStatusBinding, Unity.Advertisement.IosSupport") == null)
            {
                Report(
                    "This iOS build declares tracking but com.unity.ads.ios-support is not installed; the ATT request cannot be made.",
                    settings);
            }

            if (!File.Exists(TrackingPackageInfo.NativePluginPath))
            {
                Report(
                    $"This iOS build declares tracking but the native diagnostics plugin is missing ({TrackingPackageInfo.NativePluginPath}).",
                    settings);
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;

            var settings = TrackingSettings.Instance;
            var target = NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(report.summary.platform));

            bool definePresent = TrackingChecker.HasDefine(
                PlayerSettings.GetScriptingDefineSymbols(target), settings.ScriptingDefine);

            bool declares = TrackingDeclaration.DeclaresTracking(
                settings.Enabled, settings.DeclarationMode, definePresent);

            bool shouldWrite = TrackingDeclaration.ShouldWritePurposeString(
                settings.Enabled, settings.DeclarationMode, definePresent, settings.WritePurposeString);

            bool shouldRemove = TrackingDeclaration.ShouldRemovePurposeString(
                settings.Enabled, settings.DeclarationMode, definePresent, settings.RemoveWhenNotDeclared);

            string path = report.summary.outputPath;
            bool written = false;

#if UNITY_IOS
            written = ApplyPurposeString(path, settings, shouldWrite, shouldRemove);
#else
            Debug.LogWarning(
                "[Tracking] iOS post-process skipped: the Xcode project can only be edited from an iOS build on macOS.");
#endif

            TrackingLastBuild.Save(new TrackingLastBuild
            {
                path = path,
                platform = report.summary.platform.ToString(),
                declaresTracking = declares,
                purposeStringWritten = written,
                timestampUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                packageVersion = TrackingPackageInfo.Version
            });
        }

#if UNITY_IOS
        private static bool ApplyPurposeString(
            string path, TrackingSettings settings, bool shouldWrite, bool shouldRemove)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string plistPath = Path.Combine(path, "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogWarning($"[Tracking] Info.plist not found at {plistPath}; the purpose string was not applied.");
                return false;
            }

            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            bool hasKey = plist.root.values != null && plist.root.values.ContainsKey(PurposeKey);

            if (shouldWrite)
            {
                string purpose = (settings.PurposeString ?? string.Empty).Trim();
                if (purpose.Length == 0)
                {
                    ReportOnPostBuild(
                        "The build declares tracking but the purpose string is empty; the key was not written.", settings);
                    return false;
                }

                plist.root.SetString(PurposeKey, purpose);
                plist.WriteToFile(plistPath);

                // Read it back: a later processor or a plist write failure must not slip through.
                var written = new PlistDocument();
                written.ReadFromFile(plistPath);

                bool ok = written.root.values != null &&
                          written.root.values.TryGetValue(PurposeKey, out var value) &&
                          value != null &&
                          value.type == PlistElementType.String &&
                          value.AsString() == purpose;

                if (!ok)
                {
                    ReportOnPostBuild(
                        "NSUserTrackingUsageDescription could not be verified in the generated Info.plist.", settings);
                    return false;
                }

                Debug.Log($"[Tracking] NSUserTrackingUsageDescription written to Info.plist: \"{purpose}\"");
                return true;
            }

            if (shouldRemove && hasKey)
            {
                plist.root.values.Remove(PurposeKey);
                plist.WriteToFile(plistPath);
                Debug.Log("[Tracking] NSUserTrackingUsageDescription removed: this build does not declare tracking.");
            }

            return false;
        }

        private static void ReportOnPostBuild(string message, TrackingSettings settings)
        {
            if (settings.FailBuildOnInvalidConfiguration)
                throw new BuildFailedException("[Tracking] " + message);

            Debug.LogWarning("[Tracking] " + message);
        }
#endif

        private static void Report(string message, TrackingSettings settings)
        {
            if (settings.FailBuildOnInvalidConfiguration)
                throw new BuildFailedException("[Tracking] " + message);

            Debug.LogWarning("[Tracking] " + message);
        }
    }
}
