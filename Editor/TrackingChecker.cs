using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Wagenheimer.Tracking.Editor
{
    internal enum CheckStatus
    {
        Pass,
        Warning,
        Fail,
        Info
    }

    internal sealed class CheckResult
    {
        public string Title = string.Empty;
        public CheckStatus Status = CheckStatus.Info;
        public string Detail = string.Empty;
        public readonly List<string> Facts = new List<string>();
        public string ActionLabel;
        public Action Action;

        /// <summary>
        /// True when the item is a release requirement: a failure blocks the iOS build (pre-build gate)
        /// and makes the CLI exit with an error code.
        /// </summary>
        public bool Mandatory;

        public CheckResult WithFacts(params string[] facts)
        {
            Facts.AddRange(facts);
            return this;
        }
    }

    internal sealed class Section
    {
        public string Title = string.Empty;
        public string Description = string.Empty;
        public readonly List<CheckResult> Items = new List<CheckResult>();

        public int Failures => Items.Count(i => i.Status == CheckStatus.Fail);
        public int Warnings => Items.Count(i => i.Status == CheckStatus.Warning);
        public int Passed => Items.Count(i => i.Status == CheckStatus.Pass);
    }

    /// <summary>
    /// Automated project checker. It validates the settings, the iOS prerequisites, the last build
    /// output and the ad-SDK integration, and produces a copyable report/prompt. The runtime overlay
    /// reuses the same vocabulary (PASS/WARN/FAIL) so editor and device findings match.
    /// </summary>
    internal static class TrackingChecker
    {
        private static BuildVerification manualVerification;

        // ── Public entry points ──────────────────────────────────────────────

        internal static List<Section> Run()
        {
            var settings = FindSettings(out bool settingsExists, out string settingsPath);
            var sections = new List<Section>
            {
                BuildMandatorySection(settings, settingsExists, settingsPath),
                BuildConfigurationSection(settings, settingsExists, settingsPath),
                BuildIosSection(settings),
                BuildLastBuildSection(settings),
                BuildIntegrationSection(settings),
                BuildRuntimeSection(settings)
            };

            return sections;
        }

        /// <summary>
        /// Mandatory failures only. The build processor calls this before an iOS build so a release
        /// cannot ship with a broken ATT declaration or with a second owner of the permission.
        /// </summary>
        internal static List<CheckResult> MandatoryFailures()
        {
            var failures = new List<CheckResult>();

            foreach (var section in Run())
            {
                foreach (var item in section.Items)
                {
                    if (item.Mandatory && item.Status == CheckStatus.Fail)
                        failures.Add(item);
                }
            }

            return failures;
        }

        internal static string BuildReport(List<Section> sections)
        {
            var settings = FindSettings(out bool settingsExists, out _);
            var builder = new StringBuilder(3200);
            builder.AppendLine("===== TRACKING HELPER CHECKER =====");
            builder.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"Package: {TrackingPackageInfo.PackageName} {TrackingPackageInfo.Version}");
            builder.AppendLine($"Unity: {Application.unityVersion}");
            builder.AppendLine($"Active target: {EditorUserBuildSettings.activeBuildTarget}");
            builder.AppendLine($"ATT requirement: {(settings.RequireTracking ? "MANDATORY (build is blocked on failures)" : "advisory")}" +
                               (settingsExists ? string.Empty : " — no settings asset, using defaults"));
            builder.AppendLine();

            int pass = 0, warn = 0, fail = 0, mandatoryFail = 0;

            foreach (var section in sections)
            {
                builder.AppendLine($"[{section.Title}]");
                foreach (var item in section.Items)
                {
                    switch (item.Status)
                    {
                        case CheckStatus.Pass: pass++; break;
                        case CheckStatus.Warning: warn++; break;
                        case CheckStatus.Fail:
                            fail++;
                            if (item.Mandatory) mandatoryFail++;
                            break;
                    }

                    string tag = item.Mandatory && item.Status == CheckStatus.Fail ? " [MANDATORY]" : string.Empty;
                    builder.AppendLine($"  [{Label(item.Status)}] {item.Title}: {item.Detail}{tag}");
                    foreach (var fact in item.Facts)
                        builder.AppendLine($"      - {fact}");
                }

                builder.AppendLine();
            }

            builder.AppendLine($"SUMMARY: {pass} pass, {warn} warning(s), {fail} fail(s)" +
                               (mandatoryFail > 0 ? $" — {mandatoryFail} mandatory" : string.Empty));
            return builder.ToString();
        }

        internal static string BuildPrompt(List<Section> sections, CheckResult only = null)
        {
            var settings = FindSettings(out _, out _);
            var builder = new StringBuilder(3600);
            builder.AppendLine("You are a Unity/iOS engineer. Fix the tracking (ATT) configuration below.");
            builder.AppendLine();
            builder.AppendLine("CONTEXT");
            builder.AppendLine($"- Unity: {Application.unityVersion}");
            builder.AppendLine($"- Package: {TrackingPackageInfo.PackageName} {TrackingPackageInfo.Version} ({TrackingPackageInfo.PackagePath})");
            builder.AppendLine($"- Active target: {EditorUserBuildSettings.activeBuildTarget}");
            builder.AppendLine($"- Declaration mode: {settings.DeclarationMode} (define '{settings.ScriptingDefine}').");
            builder.AppendLine($"- Requirement mode: {settings.RequirementMode}.");
            builder.AppendLine($"- Package files: {TrackingPackageInfo.PackagePath}/Runtime/TrackingConsent.cs, " +
                               $"{TrackingPackageInfo.PackagePath}/Runtime/TrackingDiagnostics.cs, " +
                               $"{TrackingPackageInfo.PackagePath}/Editor/TrackingBuildProcessor.cs");
            builder.AppendLine();

            builder.AppendLine("PROBLEMS");
            bool any = false;

            foreach (var section in sections)
            {
                foreach (var item in section.Items)
                {
                    if (only != null && !ReferenceEquals(item, only))
                        continue;

                    if (item.Status != CheckStatus.Fail && item.Status != CheckStatus.Warning)
                        continue;

                    any = true;
                    builder.AppendLine($"- [{Label(item.Status)}] {item.Title}: {item.Detail}");
                    foreach (var fact in item.Facts)
                        builder.AppendLine($"    - {fact}");
                }
            }

            if (!any)
                builder.AppendLine("- none");

            builder.AppendLine();
            builder.AppendLine("FULL CHECKER REPORT");
            builder.AppendLine(BuildReport(sections));
            builder.AppendLine("RESPOND WITH");
            builder.AppendLine("1. Probable cause of each problem.");
            builder.AppendLine("2. Exact patch (file + diff).");
            builder.AppendLine("3. How to validate on device ([Tracking] log/overlay) and in the build (checker).");
            return builder.ToString();
        }

        // ── Sections ─────────────────────────────────────────────────────────

        /// <summary>
        /// Release requirements: the policy switch, the project-supplied requirements (build profiles)
        /// and the single-owner rules. Everything here is mandatory when the settings mark ATT as
        /// required, and blocks the iOS build through <see cref="MandatoryFailures"/>.
        /// </summary>
        private static Section BuildMandatorySection(
            TrackingSettings settings, bool settingsExists, string settingsPath)
        {
            var section = new Section
            {
                Title = "Mandatory",
                Description = settings.RequireTracking
                    ? "ATT is a release requirement: any FAIL here blocks the iOS build and the CI check."
                    : "ATT is advisory in this project (requireTracking off): the findings below are warnings."
            };

            var policy = new CheckResult
            {
                Title = "ATT is mandatory",
                Status = settings.RequireTracking ? CheckStatus.Pass : CheckStatus.Info,
                Detail = settings.RequireTracking
                    ? "requireTracking is on. The checker fails and the pre-build step blocks the iOS build while a mandatory item fails."
                    : "requireTracking is off. Turn it on to fail the checker and block the iOS build when ATT is not configured.",
                Mandatory = settings.RequireTracking
            };

            if (!settings.RequireTracking)
            {
                policy.ActionLabel = settingsExists ? "Open settings" : "Create settings";
                policy.Action = settingsExists ? () => OpenSettingsAsset(settingsPath) : CreateSettingsAsset;
            }

            section.Items.Add(policy);

            if (settings.RequireTracking && !settingsExists)
            {
                section.Items.Add(new CheckResult
                {
                    Title = "Settings asset",
                    Status = CheckStatus.Fail,
                    Detail = "ATT is mandatory but there is no TrackingSettings asset; the package is running on defaults.",
                    Mandatory = true,
                    ActionLabel = "Create",
                    Action = CreateSettingsAsset
                });
            }

            AddRequirementItems(section, settings);
            AddSingleOwnerItems(section, settings);

            return section;
        }

        private static void AddRequirementItems(Section section, TrackingSettings settings)
        {
            bool required = settings.RequireTracking;

            if (!TrackingRequirements.HasProvider)
            {
                section.Items.Add(new CheckResult
                {
                    Title = "Project requirements",
                    Status = required ? CheckStatus.Warning : CheckStatus.Info,
                    Detail = "No provider registered. Call TrackingRequirements.SetProvider(...) from an editor " +
                             "script to validate the build profiles (for example: every iOS profile that ships ads " +
                             "must declare tracking).",
                    Mandatory = false
                });
                return;
            }

            var requirements = TrackingRequirements.Evaluate();

            if (requirements.Count == 0)
            {
                section.Items.Add(new CheckResult
                {
                    Title = "Project requirements",
                    Status = CheckStatus.Info,
                    Detail = "The registered provider returned no requirements (nothing to enforce).",
                    Mandatory = false
                });
                return;
            }

            foreach (var requirement in requirements)
            {
                CheckStatus status = ToCheckStatus(requirement.Status);

                // A provider failure only blocks the build when the project marks ATT as mandatory.
                if (!required && status == CheckStatus.Fail)
                    status = CheckStatus.Warning;

                var item = new CheckResult
                {
                    Title = requirement.Title,
                    Status = status,
                    Detail = requirement.Detail,
                    Mandatory = required && requirement.Status == TrackingRequirementStatus.Fail,
                    ActionLabel = requirement.FixLabel,
                    Action = requirement.Fix
                };

                foreach (var fact in requirement.Facts)
                    item.Facts.Add(fact);

                section.Items.Add(item);
            }
        }

        /// <summary>
        /// Enforces the "two owners" rule: only the tracking package may write the purpose string or
        /// request ATT. Project scripts that do either are reported with file and line.
        /// </summary>
        private static void AddSingleOwnerItems(Section section, TrackingSettings settings)
        {
            bool required = settings.RequireTracking;
            CheckStatus problem = required ? CheckStatus.Fail : CheckStatus.Warning;

            var plistWriters = PlistWriterHits();

            var writerItem = new CheckResult
            {
                Title = "Single owner — Info.plist key",
                Status = plistWriters.Count == 0 ? CheckStatus.Pass : problem,
                Detail = plistWriters.Count == 0
                    ? "Only the tracking package writes NSUserTrackingUsageDescription."
                    : $"{plistWriters.Count} project script line(s) write or mention the key outside the package; " +
                      "the package must be the only writer.",
                Mandatory = required
            };

            if (plistWriters.Count > 0)
            {
                writerItem.ActionLabel = "Open first";
                writerItem.Action = () => OpenHit(plistWriters[0]);
                foreach (var hit in plistWriters.GetRange(0, Math.Min(3, plistWriters.Count)))
                    writerItem.Facts.Add(hit.ToString());
            }

            section.Items.Add(writerItem);

            var requesters = AttRequesterHits();

            var requesterItem = new CheckResult
            {
                Title = "Single owner — ATT request",
                Status = requesters.Count == 0 ? CheckStatus.Pass : problem,
                Detail = requesters.Count == 0
                    ? "No project script requests ATT outside the package."
                    : $"{requesters.Count} project script line(s) request ATT or use ATTrackingManager outside the package.",
                Mandatory = required
            };

            if (requesters.Count > 0)
            {
                requesterItem.ActionLabel = "Open first";
                requesterItem.Action = () => OpenHit(requesters[0]);
                foreach (var hit in requesters.GetRange(0, Math.Min(3, requesters.Count)))
                    requesterItem.Facts.Add(hit.ToString());
            }

            section.Items.Add(requesterItem);
        }

        private static Section BuildConfigurationSection(
            TrackingSettings settings, bool settingsExists, string settingsPath)
        {
            var section = new Section
            {
                Title = "Configuration",
                Description = "Settings asset, declaration rule and build safety."
            };

            if (settingsExists)
            {
                var item = new CheckResult
                {
                    Title = "Settings asset",
                    Status = CheckStatus.Pass,
                    Detail = "Assets/Resources/Wagenheimer/TrackingSettings.asset",
                    ActionLabel = "Select",
                    Action = () => PingSettings(settingsPath)
                };
                section.Items.Add(item);
            }
            else
            {
                section.Items.Add(new CheckResult
                {
                    Title = "Settings asset",
                    Status = CheckStatus.Fail,
                    Detail = "No TrackingSettings asset found. The package falls back to safe defaults without a purpose string path.",
                    ActionLabel = "Create",
                    Action = CreateSettingsAsset
                });
            }

            section.Items.Add(new CheckResult
            {
                Title = "Master switch",
                Status = settings.Enabled ? CheckStatus.Pass : CheckStatus.Info,
                Detail = settings.Enabled
                    ? "Tracking Helper is enabled."
                    : "Disabled: no request and no plist key. Enable it in the settings for tracking builds."
            });

            bool definePresent = HasDefine(IOSDefines(), settings.ScriptingDefine);
            bool declares = TrackingDeclaration.DeclaresTracking(
                settings.Enabled, settings.DeclarationMode, definePresent);

            var declaration = new CheckResult
            {
                Title = "Declaration rule",
                Status = settings.Enabled ? CheckStatus.Pass : CheckStatus.Info,
                Detail = TrackingDeclaration.Describe(
                    settings.Enabled, settings.DeclarationMode, settings.ScriptingDefine, definePresent)
            };
            declaration.WithFacts(
                $"Mode: {settings.DeclarationMode}",
                $"Define: {settings.ScriptingDefine}",
                $"iOS defines: {IOSDefines()}");

            if (settings.DeclarationMode == TrackingDeclarationMode.ScriptingDefine && !definePresent)
            {
                declaration.Status = settings.Enabled ? CheckStatus.Info : CheckStatus.Info;
                declaration.Detail += " (expected for builds that do not declare tracking)";
            }

            if (settings.RequireTracking && settings.Enabled && !declares)
            {
                // With the build-pipeline rule active the profiles own the truth (a Free profile declares
                // tracking and a Full profile does not), so the active-target define alone is not a failure.
                if (TrackingBuildProfiles.IsActive)
                {
                    declaration.Status = CheckStatus.Info;
                    declaration.Detail += " Profile-level truth comes from the project requirements above.";
                }
                else
                {
                    declaration.Status = CheckStatus.Fail;
                    declaration.Mandatory = true;
                    declaration.ActionLabel = "Apply define to iOS";
                    declaration.Action = () => ApplyIosScriptingDefine();
                }
            }

            section.Items.Add(declaration);

            string purposeIssue = ValidatePurposeString(settings.PurposeString);
            CheckStatus purposeStatus;
            if (string.IsNullOrWhiteSpace(settings.PurposeString))
                purposeStatus = CheckStatus.Fail;
            else if (purposeIssue != null)
                purposeStatus = settings.RequireTracking ? CheckStatus.Fail : CheckStatus.Warning;
            else
                purposeStatus = CheckStatus.Pass;

            section.Items.Add(new CheckResult
            {
                Title = "Purpose string",
                Status = purposeStatus,
                Detail = purposeStatus == CheckStatus.Pass
                    ? $"\"{settings.PurposeString}\""
                    : purposeIssue,
                Mandatory = settings.RequireTracking
            }.WithFacts(
                $"Chars: {settings.PurposeString.Length}",
                "Written to NSUserTrackingUsageDescription only for builds that declare tracking."));

            section.Items.Add(new CheckResult
            {
                Title = "Build safety",
                Status = settings.FailBuildOnInvalidConfiguration
                    ? CheckStatus.Pass
                    : settings.RequireTracking ? CheckStatus.Fail : CheckStatus.Warning,
                Detail = settings.FailBuildOnInvalidConfiguration
                    ? "A build that declares tracking without a valid purpose string fails before compiling."
                    : settings.RequireTracking
                        ? "Invalid configurations only log a warning, so a broken ATT build can be shipped even though ATT is mandatory here."
                        : "Invalid configurations only log a warning. A broken ATT build can be shipped.",
                Mandatory = settings.RequireTracking,
                ActionLabel = "Fix",
                Action = () => OpenSettingsAsset(settingsPath)
            });

            var timing = new CheckResult
            {
                Title = "Timing",
                Status = CheckStatus.Pass,
                Detail = $"Delay {settings.InitialDelaySeconds:0.##}s, timeout {settings.TimeoutSeconds:0}s, " +
                         $"retry {(settings.RetryOnce ? settings.RetryDelaySeconds.ToString("0.##") + "s" : "off")}."
            };

            if (settings.InitialDelaySeconds < 0.25f)
            {
                timing.Status = CheckStatus.Warning;
                timing.Detail += " The initial delay is very short; iOS may drop the alert if the window is not active.";
            }
            else if (settings.TimeoutSeconds < 5f)
            {
                timing.Status = CheckStatus.Warning;
                timing.Detail += " The timeout is short; ad SDKs may initialize before the player answers.";
            }

            section.Items.Add(timing);

            section.Items.Add(new CheckResult
            {
                Title = "Overlay scale",
                Status = CheckStatus.Pass,
                Detail = $"Mobile {settings.MobileScale:0.##}x, desktop {settings.DesktopScale:0.##}x " +
                         $"(adjustable in-game with A-/A+)."
            });

            return section;
        }

        private static Section BuildIosSection(TrackingSettings settings)
        {
            var section = new Section
            {
                Title = "iOS readiness",
                Description = "Native ATT support and the minimum OS version."
            };

            bool iosSupport = Type.GetType(
                "Unity.Advertisement.IosSupport.ATTrackingStatusBinding, Unity.Advertisement.IosSupport") != null;

            section.Items.Add(new CheckResult
            {
                Title = "iOS support binding",
                Status = iosSupport ? CheckStatus.Pass : CheckStatus.Fail,
                Detail = iosSupport
                    ? "com.unity.ads.ios-support is installed (ATTrackingStatusBinding found)."
                    : "com.unity.ads.ios-support is missing; the package cannot request ATT."
            });

            bool plugin = File.Exists(TrackingPackageInfo.NativePluginPath);
            section.Items.Add(new CheckResult
            {
                Title = "Native plugin",
                Status = plugin ? CheckStatus.Pass : CheckStatus.Fail,
                Detail = plugin
                    ? "WagenheimerTracking.mm is present."
                    : "WagenheimerTracking.mm is missing from the package.",
            }.WithFacts(TrackingPackageInfo.NativePluginPath));

            string targetVersion = PlayerSettings.iOS.targetOSVersionString;
            bool parsed = Version.TryParse(targetVersion, out var version);
            bool supported = parsed && version >= new Version(14, 0);

            section.Items.Add(new CheckResult
            {
                Title = "Minimum iOS version",
                Status = supported ? CheckStatus.Pass : CheckStatus.Warning,
                Detail = supported
                    ? $"iOS {targetVersion} (ATT requires 14.5+ at runtime)."
                    : $"iOS {targetVersion} is below 14.0; ATT is unavailable on those devices.",
                ActionLabel = "Player Settings",
                Action = () => SettingsService.OpenProjectSettings("Project/Player")
            });

            bool declare = TrackingDeclaration.DeclaresTracking(
                settings.Enabled, settings.DeclarationMode, HasDefine(IOSDefines(), settings.ScriptingDefine));

            section.Items.Add(new CheckResult
            {
                Title = "Info.plist key",
                Status = declare ? CheckStatus.Pass : CheckStatus.Info,
                Detail = declare
                    ? "The build processor will write NSUserTrackingUsageDescription with the configured text."
                    : $"The build processor will {(settings.RemoveWhenNotDeclared ? "remove" : "keep")} the key for this build."
            });

            return section;
        }

        private static Section BuildLastBuildSection(TrackingSettings settings)
        {
            var section = new Section
            {
                Title = "Last build",
                Description = "Verifies the generated Info.plist and the Xcode project."
            };

            var verification = manualVerification;
            if (verification == null)
            {
                var last = TrackingLastBuild.Load();
                if (last != null && !string.IsNullOrEmpty(last.path))
                    verification = VerifyBuild(last.path, last.declaresTracking);
            }

            if (verification == null)
            {
                section.Items.Add(new CheckResult
                {
                    Title = "Build verification",
                    Status = CheckStatus.Info,
                    Detail = "No iOS build verified yet. Build once, or point the checker at an existing Xcode project folder.",
                    ActionLabel = "Verify folder...",
                    Action = VerifyFolder
                });
                return section;
            }

            var item = new CheckResult
            {
                Title = "Info.plist purpose string",
                Status = verification.PlistKeyPresent == verification.ExpectedDeclares
                    ? CheckStatus.Pass
                    : CheckStatus.Fail,
                Detail = verification.PlistKeyPresent
                    ? "NSUserTrackingUsageDescription is present."
                    : "NSUserTrackingUsageDescription is absent."
            };
            item.WithFacts(
                $"Folder: {verification.Path}",
                $"Expected declaring tracking: {verification.ExpectedDeclares}",
                $"Purpose string: {(string.IsNullOrEmpty(verification.PurposeString) ? "(none)" : verification.PurposeString)}");

            if (verification.PlistKeyPresent != verification.ExpectedDeclares)
            {
                item.ActionLabel = "Re-verify";
                item.Action = VerifyFolder;
            }

            section.Items.Add(item);

            section.Items.Add(new CheckResult
            {
                Title = "AppTrackingTransparency.framework",
                Status = verification.FrameworkLinked ? CheckStatus.Pass : CheckStatus.Warning,
                Detail = verification.FrameworkLinked
                    ? "Linked (or weak-linked) in the generated Xcode project."
                    : "Not found in project.pbxproj. The runtime probe still dlopens it, but review the Xcode project.",
                ActionLabel = "Verify folder...",
                Action = VerifyFolder
            });

            bool declareNow = TrackingDeclaration.DeclaresTracking(
                settings.Enabled, settings.DeclarationMode, HasDefine(IOSDefines(), settings.ScriptingDefine));

            if (verification.ExpectedDeclares != declareNow)
            {
                section.Items.Add(new CheckResult
                {
                    Title = "Settings changed since the build",
                    Status = CheckStatus.Info,
                    Detail = $"The verified build declared tracking={verification.ExpectedDeclares}; the current settings would produce {declareNow}. Rebuild to validate."
                });
            }

            return section;
        }

        private static Section BuildIntegrationSection(TrackingSettings settings)
        {
            var section = new Section
            {
                Title = "Integration",
                Description = "Detects ad SDKs that also request ATT."
            };

            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(p => p.name == "com.wagenheimer.levelplayhelper");

            if (package == null)
            {
                section.Items.Add(new CheckResult
                {
                    Title = "Ads SDK",
                    Status = CheckStatus.Info,
                    Detail = "No LevelPlay Helper package detected. This package still works as the single ATT owner."
                });
                return section;
            }

            bool found = TryFindLevelPlayRequest(out bool requestOn, out string prefabPath);

            if (!found)
            {
                section.Items.Add(new CheckResult
                {
                    Title = "LevelPlay Helper",
                    Status = CheckStatus.Info,
                    Detail = $"Installed ({package.version}), but no LevelPlayHelper prefab was found. Nothing to disable."
                });
                return section;
            }

            section.Items.Add(new CheckResult
            {
                Title = "LevelPlay ATT request",
                Status = requestOn
                    ? settings.RequireTracking ? CheckStatus.Fail : CheckStatus.Warning
                    : CheckStatus.Pass,
                Detail = requestOn
                    ? "The helper is still configured to request ATT — two owners request the same permission and the second request is a no-op (or an unexpected prompt)."
                    : "The helper's own ATT request is disabled; this package is the single owner.",
                Mandatory = settings.RequireTracking,
                ActionLabel = requestOn ? "Disable on prefab" : null,
                Action = requestOn ? () => DisableLevelPlayRequest(prefabPath) : null
            }.WithFacts(prefabPath));

            return section;
        }

        private static Section BuildRuntimeSection(TrackingSettings settings)
        {
            var section = new Section
            {
                Title = "Runtime",
                Description = "How to validate on device."
            };

            section.Items.Add(new CheckResult
            {
                Title = "Consent gate",
                Status = settings.Enabled ? CheckStatus.Pass : CheckStatus.Info,
                Detail = settings.Enabled
                    ? "TrackingConsent.WaitUntilResolved() blocks ad SDK initialization until the answer is known."
                    : "The master switch is off; the gate resolves immediately."
            });

            section.Items.Add(new CheckResult
            {
                Title = "Debug overlay",
                Status = settings.ShowInReleaseBuilds ? CheckStatus.Warning : CheckStatus.Pass,
                Detail = settings.ShowInReleaseBuilds
                    ? "The overlay is enabled in release builds. Disable showInReleaseBuilds before shipping."
                    : "Available in the editor and development builds (F9 / TRACK DBG)."
            });

            section.Items.Add(new CheckResult
            {
                Title = "Device test",
                Status = CheckStatus.Info,
                Detail = "Install on a device, answer the alert, press 'Request ATT again' (no second alert is expected), " +
                         "use 'Open app settings' to change the permission and reinstall to reset the prompt."
            });

            section.Items.Add(new CheckResult
            {
                Title = "Documentation",
                Status = CheckStatus.Info,
                Detail = "Full guide: README.md in the package.",
                ActionLabel = "Open",
                Action = TrackingPackageInfo.OpenDocumentation
            });

            return section;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static CheckStatus ToCheckStatus(TrackingRequirementStatus status) => status switch
        {
            TrackingRequirementStatus.Pass => CheckStatus.Pass,
            TrackingRequirementStatus.Warning => CheckStatus.Warning,
            TrackingRequirementStatus.Fail => CheckStatus.Fail,
            _ => CheckStatus.Info
        };

        private static readonly Regex AttRequestPattern = new Regex(
            @"\bRequestAuthorizationTracking\s*\(|\bRequestTrackingAuthorization\s*\(|\bATTrackingManager\b|\bATTrackingStatusBinding\b",
            RegexOptions.Compiled);

        private static bool IsAttRequestLine(string line) =>
            !string.IsNullOrEmpty(line) && AttRequestPattern.IsMatch(line);

        private sealed class ScriptHit
        {
            public ScriptHit(string path, int line, string text)
            {
                Path = path;
                Line = line;
                Text = text;
            }

            public string Path { get; }
            public int Line { get; }
            public string Text { get; }

            public override string ToString() => $"{Path}:{Line} — {Text}";
        }

        private sealed class ProjectScan
        {
            public readonly List<ScriptHit> PlistWriters = new List<ScriptHit>();
            public readonly List<ScriptHit> AttRequesters = new List<ScriptHit>();
            public double Timestamp;
        }

        private const double ScanCacheSeconds = 10d;
        private static ProjectScan scanCache;

        /// <summary>
        /// Scans the project's own scripts (not packages, not Library) so the checker can prove that the
        /// package is the only owner of the plist key and of the ATT request. Cached for a minute because
        /// some projects carry thousands of scripts.
        /// </summary>
        private static ProjectScan GetProjectScan()
        {
            if (scanCache != null &&
                EditorApplication.timeSinceStartup - scanCache.Timestamp < ScanCacheSeconds)
            {
                return scanCache;
            }

            var scan = new ProjectScan();
            string assetsRoot = Application.dataPath;
            string projectRoot = Directory.GetParent(assetsRoot)?.FullName ?? assetsRoot;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(assetsRoot, "*.cs", SearchOption.AllDirectories);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Tracking] Could not scan the project scripts: {exception.Message}");
                scan.Timestamp = EditorApplication.timeSinceStartup;
                scanCache = scan;
                return scan;
            }

            foreach (string file in files)
            {
                if (IsInsidePackage(file))
                    continue;

                string assetPath;
                try
                {
                    assetPath = file.Substring(projectRoot.Length).TrimStart('\\', '/').Replace('\\', '/');
                }
                catch (Exception)
                {
                    assetPath = file;
                }

                try
                {
                    int lineNumber = 0;
                    foreach (string rawLine in File.ReadLines(file))
                    {
                        lineNumber++;

                        string line = rawLine.TrimStart();
                        if (line.Length == 0)
                            continue;

                        // Skip comments: the rule is about code that writes/requests, and the project's
                        // own documentation about the key must not be reported as a second owner.
                        if (line.StartsWith("//", StringComparison.Ordinal) ||
                            line.StartsWith("*", StringComparison.Ordinal) ||
                            line.StartsWith("/*", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (line.IndexOf("NSUserTrackingUsageDescription", StringComparison.Ordinal) >= 0)
                            scan.PlistWriters.Add(new ScriptHit(assetPath, lineNumber, line.Trim()));

                        if (IsAttRequestLine(line))
                            scan.AttRequesters.Add(new ScriptHit(assetPath, lineNumber, line.Trim()));
                    }
                }
                catch (Exception)
                {
                    // Unreadable/generated file: skip it, the checker must never fail because of I/O.
                }
            }

            scan.Timestamp = EditorApplication.timeSinceStartup;
            scanCache = scan;
            return scan;
        }

        private static bool IsInsidePackage(string file)
        {
            try
            {
                string packageRoot = Path.GetFullPath(TrackingPackageInfo.PackagePath);
                return Path.GetFullPath(file).StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static List<ScriptHit> PlistWriterHits() => GetProjectScan().PlistWriters;

        private static List<ScriptHit> AttRequesterHits() => GetProjectScan().AttRequesters;

        private static void OpenHit(ScriptHit hit)
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(hit.Path);
            if (script != null)
                AssetDatabase.OpenAsset(script, hit.Line);
        }

        /// <summary>Adds the configured define to the iOS target (one-click fix for ScriptingDefine mode).</summary>
        internal static bool ApplyIosScriptingDefine()
        {
            var settings = FindSettings(out _, out _);
            var target = NamedBuildTarget.iOS;

            string current = PlayerSettings.GetScriptingDefineSymbols(target);
            if (HasDefine(current, settings.ScriptingDefine))
                return false;

            string updated = string.IsNullOrWhiteSpace(current)
                ? settings.ScriptingDefine
                : current.TrimEnd(';') + ";" + settings.ScriptingDefine;

            PlayerSettings.SetScriptingDefineSymbols(target, updated);
            Debug.Log($"[Tracking] Added '{settings.ScriptingDefine}' to the iOS scripting defines.");
            return true;
        }

        internal static TrackingSettings FindSettings(out bool exists, out string path)
        {
            var guids = AssetDatabase.FindAssets("t:TrackingSettings");
            if (guids.Length > 0)
            {
                path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var asset = AssetDatabase.LoadAssetAtPath<TrackingSettings>(path);
                if (asset != null)
                {
                    exists = true;
                    return asset;
                }
            }

            exists = false;
            path = null;
            return TrackingSettings.CreateDefaults();
        }

        /// <summary>Returns null when the purpose string is acceptable, otherwise a message.</summary>
        internal static string ValidatePurposeString(string purpose)
        {
            if (string.IsNullOrWhiteSpace(purpose))
                return "The purpose string is empty. iOS terminates the app when ATT is requested without NSUserTrackingUsageDescription.";

            string trimmed = purpose.Trim();

            if (trimmed.Length < 30)
                return "The purpose string is too short to be meaningful. Apple review rejects vague descriptions.";

            if (trimmed.Length > 300)
                return "The purpose string is very long. Keep it specific and concise for the App Review team.";

            if (trimmed.IndexOf("lorem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf("TODO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf("placeholder", StringComparison.OrdinalIgnoreCase) >= 0)
                return "The purpose string looks like a placeholder.";

            return null;
        }

        internal static string IOSDefines()
        {
            try
            {
                return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.iOS);
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static bool HasDefine(string defines, string define)
        {
            if (string.IsNullOrWhiteSpace(defines) || string.IsNullOrWhiteSpace(define))
                return false;

            return defines
                .Split(';')
                .Any(d => string.Equals(d.Trim(), define.Trim(), StringComparison.Ordinal));
        }

        internal static void CreateSettingsAsset()
        {
            const string folder = "Assets/Resources/Wagenheimer";
            const string path = folder + "/TrackingSettings.asset";

            EnsureFolder("Assets/Resources");
            EnsureFolder(folder);

            if (File.Exists(path))
            {
                TrackingSettings.Reload();
                var existing = AssetDatabase.LoadAssetAtPath<TrackingSettings>(path);
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            var settings = ScriptableObject.CreateInstance<TrackingSettings>();
            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();
            TrackingSettings.Reload();

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
            Debug.Log($"[Tracking] Settings asset created at {path}");
        }

        internal static void VerifyFolder()
        {
            var last = TrackingLastBuild.Load();
            string start = last != null && Directory.Exists(last.path) ? last.path : string.Empty;
            string path = EditorUtility.OpenFolderPanel(
                "Select the folder that contains Info.plist", start, string.Empty);

            if (string.IsNullOrEmpty(path))
                return;

            var settings = FindSettings(out _, out _);
            bool expected = TrackingDeclaration.DeclaresTracking(
                settings.Enabled, settings.DeclarationMode, HasDefine(IOSDefines(), settings.ScriptingDefine));

            manualVerification = VerifyBuild(path, expected);
            Debug.Log($"[Tracking] Build verified: {path} (key={manualVerification.PlistKeyPresent}, framework={manualVerification.FrameworkLinked})");
        }

        private static BuildVerification VerifyBuild(string path, bool expectedDeclares)
        {
            var verification = new BuildVerification
            {
                Path = path,
                PathExists = Directory.Exists(path),
                ExpectedDeclares = expectedDeclares
            };

            string infoPlist = Path.Combine(path, "Info.plist");
            if (File.Exists(infoPlist))
            {
                string text = File.ReadAllText(infoPlist);
                verification.PlistKeyPresent =
                    Regex.IsMatch(text, "<key>\\s*NSUserTrackingUsageDescription\\s*</key>");

                var match = Regex.Match(
                    text,
                    "<key>\\s*NSUserTrackingUsageDescription\\s*</key>\\s*<string>(.*?)</string>",
                    RegexOptions.Singleline);

                if (match.Success)
                    verification.PurposeString = Regex.Unescape(match.Groups[1].Value.Trim());
            }

            string project = Path.Combine(path, "Unity-iPhone.xcodeproj", "project.pbxproj");
            if (!File.Exists(project))
                project = Path.Combine(path, "project.pbxproj");

            if (File.Exists(project))
                verification.FrameworkLinked = File.ReadAllText(project).Contains("AppTrackingTransparency.framework");

            return verification;
        }

        private static bool TryFindLevelPlayRequest(out bool requestOn, out string prefabPath)
        {
            requestOn = false;
            prefabPath = null;

            foreach (string guid in AssetDatabase.FindAssets("LevelPlayHelper t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                foreach (var behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null || behaviour.GetType().FullName != "Wagenheimer.LevelPlayHelper.LevelPlayHelper")
                        continue;

                    var serialized = new SerializedObject(behaviour);
                    var tracking = serialized.FindProperty("trackingConfig");
                    var request = tracking?.FindPropertyRelative("requestAuthorization");
                    if (request == null)
                        continue;

                    requestOn = request.boolValue;
                    prefabPath = path;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Disables the LevelPlay Helper ATT request when it is enabled. Returns true when changed.</summary>
        internal static bool DisableLevelPlayRequest()
        {
            if (!TryFindLevelPlayRequest(out bool requestOn, out string prefabPath) ||
                !requestOn ||
                string.IsNullOrEmpty(prefabPath))
                return false;

            DisableLevelPlayRequest(prefabPath);
            return true;
        }

        private static void DisableLevelPlayRequest(string prefabPath)
        {            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return;

            foreach (var behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().FullName != "Wagenheimer.LevelPlayHelper.LevelPlayHelper")
                    continue;

                var serialized = new SerializedObject(behaviour);
                var tracking = serialized.FindProperty("trackingConfig");
                var request = tracking?.FindPropertyRelative("requestAuthorization");
                if (request == null || !request.boolValue)
                    continue;

                request.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(behaviour);
                Debug.Log($"[Tracking] Disabled the LevelPlay Helper ATT request on {prefabPath}.");
            }

            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void PingSettings(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TrackingSettings>(path);
            if (asset == null)
                return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static void OpenSettingsAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TrackingSettings>(path);
            if (asset != null)
                Selection.activeObject = asset;
        }

        private static string Label(CheckStatus status) => status switch
        {
            CheckStatus.Pass => "PASS",
            CheckStatus.Warning => "WARN",
            CheckStatus.Fail => "FAIL",
            _ => "INFO"
        };

        private sealed class BuildVerification
        {
            public string Path;
            public bool PathExists;
            public bool PlistKeyPresent;
            public string PurposeString;
            public bool FrameworkLinked;
            public bool ExpectedDeclares;
        }
    }
}
