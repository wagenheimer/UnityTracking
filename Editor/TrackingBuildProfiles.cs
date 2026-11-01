using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.Tracking.Editor
{
    /// <summary>
    /// Automatic integration with <c>com.wagenheimer.buildpipeline</c>.
    ///
    /// The package reads <c>ProjectBuildConfig</c> through SerializedObject (found by asset type name, so
    /// there is no compile-time dependency on the build pipeline) and reports one requirement per build
    /// profile: a profile that applies <see cref="TrackingSettings.AdsDefine"/> ships ads, so it must also
    /// apply <see cref="TrackingSettings.ScriptingDefine"/>.
    ///
    /// This is the only per-project configuration: the two define names in TrackingSettings. Games with
    /// the build pipeline need no editor script at all; games without it simply see the rule skipped.
    /// </summary>
    internal static class TrackingBuildProfiles
    {
        private const string ConfigAssetType = "t:ProjectBuildConfig";
        private const string PublishersProperty = "publishers";
        private const string IosPlatformName = "iOS";

        [InitializeOnLoadMethod]
        private static void Register()
        {
            TrackingRequirements.SetProvider(Evaluate);
        }

        /// <summary>True when the profile rule can actually run (config found and ads define configured).</summary>
        internal static bool IsActive
        {
            get
            {
                var settings = TrackingChecker.FindSettings(out _, out _);
                return !string.IsNullOrEmpty(settings.AdsDefine) && FindConfig() != null;
            }
        }

        internal static IEnumerable<TrackingRequirement> Evaluate()
        {
            var settings = TrackingChecker.FindSettings(out _, out _);
            var config = FindConfig();

            if (config == null)
            {
                yield return new TrackingRequirement(
                    "iOS build profiles",
                    TrackingRequirementStatus.Info,
                    "No ProjectBuildConfig asset found, so the profile rule is skipped (the build pipeline is optional).");
                yield break;
            }

            string adsDefine = settings.AdsDefine;
            if (string.IsNullOrEmpty(adsDefine))
            {
                yield return new TrackingRequirement(
                    "iOS build profiles",
                    TrackingRequirementStatus.Info,
                    "Profile rule disabled. Set 'ads define' in TrackingSettings (for example FTDOTD_MONETIZATION) " +
                    "so every iOS profile that ships ads is required to declare tracking.")
                    .WithFacts($"Tracking define: {settings.ScriptingDefine}");
                yield break;
            }

            var serialized = new SerializedObject(config);
            var publishers = serialized.FindProperty(PublishersProperty);

            if (publishers == null || !publishers.isArray)
            {
                yield return new TrackingRequirement(
                    "iOS build profiles",
                    TrackingRequirementStatus.Warning,
                    "ProjectBuildConfig has no 'publishers' list; the profile rule could not run.");
                yield break;
            }

            int iosProfiles = 0;

            for (int index = 0; index < publishers.arraySize; index++)
            {
                var element = publishers.GetArrayElementAtIndex(index);
                if (!IsIos(element))
                    continue;

                iosProfiles++;

                string id = ReadString(element, "id");
                string name = ReadString(element, "displayName");
                string title = (string.IsNullOrWhiteSpace(name) ? id : name) + " — ATT";

                var defines = ReadDefines(element);
                bool shipsAds = IsApplied(defines, adsDefine);
                bool declares = IsApplied(defines, settings.ScriptingDefine);

                if (shipsAds && declares)
                {
                    yield return new TrackingRequirement(
                            title,
                            TrackingRequirementStatus.Pass,
                            "Ships ads and declares tracking: the build writes the purpose string and requests ATT before the ad SDK reads the identifier.")
                        .WithFacts(
                            $"Profile: {id}",
                            $"Defines: {Join(defines)}",
                            $"Rule: {adsDefine} requires {settings.ScriptingDefine}");
                    continue;
                }

                if (shipsAds)
                {
                    var requirement = new TrackingRequirement(
                            title,
                            TrackingRequirementStatus.Fail,
                            $"Ships ads ({adsDefine}) but does not declare tracking ({settings.ScriptingDefine}). " +
                            "The purpose string is removed and the ad SDK reads the advertising identifier without consent " +
                            "(App Store guideline 5.1.2(i)).")
                        .WithFacts(
                            $"Profile: {id}",
                            $"Defines: {Join(defines)}",
                            $"Expected: {adsDefine} + {settings.ScriptingDefine}");

                    int captured = index;
                    requirement.WithFix($"Add {settings.ScriptingDefine}", () => AddDefine(config, captured, settings.ScriptingDefine));
                    yield return requirement;
                    continue;
                }

                if (declares)
                {
                    yield return new TrackingRequirement(
                            title,
                            TrackingRequirementStatus.Warning,
                            "Declares tracking but does not ship ads: the build would carry the purpose string.")
                        .WithFacts(
                            $"Profile: {id}",
                            $"Defines: {Join(defines)}");
                    continue;
                }

                yield return new TrackingRequirement(
                        title,
                        TrackingRequirementStatus.Pass,
                        "No ads and no tracking: the purpose string is removed from this build.")
                    .WithFacts(
                        $"Profile: {id}",
                        $"Defines: {Join(defines)}");
            }

            if (iosProfiles == 0)
            {
                yield return new TrackingRequirement(
                    "iOS build profiles",
                    TrackingRequirementStatus.Info,
                    "ProjectBuildConfig declares no iOS profile, so the profile rule has nothing to check.");
            }
        }

        // ── SerializedObject helpers (no build pipeline types) ────────────────

        private static ScriptableObject FindConfig()
        {
            var guids = AssetDatabase.FindAssets(ConfigAssetType);
            if (guids.Length == 0)
                return null;

            return AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static bool IsIos(SerializedProperty profile)
        {
            var platform = profile.FindPropertyRelative("platform");
            if (platform == null || platform.propertyType != SerializedPropertyType.Enum)
                return false;

            int index = platform.enumValueIndex;
            if (index < 0 || index >= platform.enumNames.Length)
                return false;

            return string.Equals(platform.enumNames[index], IosPlatformName, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadString(SerializedProperty element, string name)
        {
            var property = element.FindPropertyRelative(name);
            return property != null && property.propertyType == SerializedPropertyType.String
                ? property.stringValue
                : string.Empty;
        }

        private static List<string> ReadDefines(SerializedProperty element)
        {
            var list = new List<string>();
            var defines = element.FindPropertyRelative("scriptingDefines");
            if (defines == null || !defines.isArray)
                return list;

            for (int i = 0; i < defines.arraySize; i++)
            {
                string value = defines.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrWhiteSpace(value))
                    list.Add(value.Trim());
            }

            return list;
        }

        /// <summary>
        /// True when the profile ends up with the define. Supports the pipeline's removal syntax:
        /// a profile that lists <c>!DEFINE</c> or <c>-DEFINE</c> does not apply it.
        /// </summary>
        internal static bool IsApplied(IEnumerable<string> defines, string define)
        {
            if (defines == null || string.IsNullOrWhiteSpace(define))
                return false;

            string target = define.Trim();
            bool applied = false;

            foreach (string raw in defines)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                string value = raw.Trim();
                bool removal = value[0] == '!' || value[0] == '-';
                string name = removal ? value.Substring(1).Trim() : value;

                if (!string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (removal)
                    return false;

                applied = true;
            }

            return applied;
        }

        private static void AddDefine(ScriptableObject config, int index, string define)
        {
            if (config == null || string.IsNullOrWhiteSpace(define))
                return;

            var serialized = new SerializedObject(config);
            var publishers = serialized.FindProperty(PublishersProperty);

            if (publishers == null || index < 0 || index >= publishers.arraySize)
                return;

            var element = publishers.GetArrayElementAtIndex(index);
            var defines = element.FindPropertyRelative("scriptingDefines");

            if (IsApplied(ReadDefines(element), define))
                return;

            if (defines == null)
                return;

            defines.InsertArrayElementAtIndex(defines.arraySize);
            defines.GetArrayElementAtIndex(defines.arraySize - 1).stringValue = define;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Tracking] Added '{define}' to build profile '{ReadString(element, "displayName")}'.");
        }

        private static string Join(IEnumerable<string> defines)
        {
            if (defines == null)
                return "(none)";

            var list = new List<string>();
            foreach (string value in defines)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    list.Add(value);
            }

            return list.Count == 0 ? "(none)" : string.Join(", ", list);
        }
    }
}
