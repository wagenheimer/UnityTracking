# Tracking Helper

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com)
[![Platform](https://img.shields.io/badge/platform-iOS-blue?logo=apple)](https://developer.apple.com/documentation/apptrackingtransparency)
[![Version](https://img.shields.io/github/v/tag/wagenheimer/UnityTracking?label=version&color=brightgreen)](https://github.com/wagenheimer/UnityTracking/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Install](https://img.shields.io/badge/install-Git%20URL-orange)](#installation)

A single, reusable owner for **App Tracking Transparency (ATT)** in Unity iOS builds:

- Runtime consent gate with a request history and a safe re-request path.
- Native iOS probe (`ATTrackingManager`, `NSUserTrackingUsageDescription`).
- Build processor that writes/removes the purpose string from the generated `Info.plist`.
- Automated editor **checker** with pass/warn/fail findings, fix actions and a copyable report.
- Runtime **UI Toolkit debug overlay** (`F9` / `TRACK DBG`): status, history, checks, re-request, Settings deep link, zoom and maximize.
- Ads-agnostic: any SDK that needs the advertising identifier waits for the answer through `TrackingConsent.WaitUntilResolved`.

---

## Table of Contents

- [Why this package](#why-this-package)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Settings](#settings)
- [Runtime API](#runtime-api)
- [Build Processor](#build-processor)
- [Editor Checker](#editor-checker)
- [Runtime Debug Overlay](#runtime-debug-overlay)
- [Integrating with an ad SDK](#integrating-with-an-ad-sdk)
- [Testing / QA](#testing--qa)
- [Troubleshooting](#troubleshooting)
- [Architecture](#architecture)
- [License](#license)

---

## Why this package

ATT is a legal requirement tied to the App Privacy label, not to an ad SDK. In practice the
answer is consumed by ad SDKs, so the request must happen **before** any SDK initializes and
the `Info.plist` key must exist exactly when the build declares tracking.

Doing this in game code tends to drift: a key written by the project, a request made by the SDK,
two different triggers and no way to prove what the running app sees. This package centralizes:

1. **One owner for the purpose string** — the build processor writes or removes
   `NSUserTrackingUsageDescription` based on a declaration rule (always / never / scripting define).
2. **One owner for the request** — a runner that waits for the first frame, requests once, retries
   once if the alert did not appear, and resolves the status for every SDK.
3. **One diagnostic surface** — editor checker + in-game overlay that answer
   "was ATT already asked?", "why is the alert not showing?" and "what does the device see?".

---

## Installation

Unity Package Manager → **+** → **Add package from git URL...**

```
https://github.com/wagenheimer/UnityTracking.git
```

Pin a release:

```
https://github.com/wagenheimer/UnityTracking.git#vX.Y.Z
```

Requirements:

| Requirement | Version |
|---|---|
| Unity | 2022.3 LTS or newer (tested on Unity 6) |
| iOS | 14.5+ for the ATT API |
| Package | `com.unity.ads.ios-support` (declared as a dependency) |

---

## Quick Start

1. Install the package.
2. `Tools > Wagenheimer > Tracking > Create Settings Asset...` (creates
   `Assets/Resources/Wagenheimer/TrackingSettings.asset`).
3. In the settings:
   - leave **Declaration Mode** on `ScriptingDefine` and set the define your iOS build profile
     applies (for example `FTDOTD_TRACKING`);
   - review the **Purpose String** — Apple rejects vague descriptions;
   - keep **Fail Build On Invalid Configuration** enabled.
4. Open `Tools > Wagenheimer > Tracking > Tracking Checker...` and fix every **FAIL**.
5. Gate your ad SDK:

```csharp
using Wagenheimer.Tracking;

IEnumerator Start()
{
    yield return TrackingConsent.WaitUntilResolved();
    Ads.Initialize();
}
```

6. Build iOS Free (the profile with the define), run on device and press `F9` (or tap
   `TRACK DBG`).

---

## Settings

Asset path: `Assets/Resources/Wagenheimer/TrackingSettings.asset`
(`Resources.Load<TrackingSettings>("Wagenheimer/TrackingSettings")`).
Without an asset the package uses safe defaults and logs a warning.

### Activation

| Field | Meaning |
|---|---|
| `enabled` | Master switch. Off = no request, no plist write, overlay reports N/A. |
| `declarationMode` | `Never`, `Always` or `ScriptingDefine`. Decides whether a build declares tracking (and therefore whether the key is written). |
| `scriptingDefine` | Define inspected by `ScriptingDefine` mode (e.g. `FTDOTD_TRACKING`). |
| `requirementMode` | Runtime requirement: `Auto` (plist key present), `ForceRequired` (QA), `ForceNotRequired`. |
| `requestOnBoot` | `true`: request when the status is `NotDetermined`. `false`: only read the status. |

### iOS Info.plist

| Field | Meaning |
|---|---|
| `writePurposeString` | Write `NSUserTrackingUsageDescription` when the build declares tracking. |
| `purposeString` | The text written to the key. Never ship the placeholder. |
| `removeWhenNotDeclared` | Remove the key from builds that do not declare tracking (Full games, etc.). |
| `failBuildOnInvalidConfiguration` | Fail the build instead of shipping a broken ATT declaration. |

### Timing

| Field | Meaning |
|---|---|
| `initialDelaySeconds` | Wait after the first scene before requesting (default 0.75 s). |
| `timeoutSeconds` | Maximum wait for an answer (default 30 s). |
| `retryDelaySeconds` / `retryOnce` | One retry when the alert did not appear (default 6 s). |

### History

| Field | Meaning |
|---|---|
| `persistHistory` | Persist attempts and timestamps in `PlayerPrefs`. Off = session-only. |
| `historyKeyPrefix` | PlayerPrefs prefix. Change it to reset history between QA runs. |

### Diagnostics / Overlay

| Field | Meaning |
|---|---|
| `stackDescription` | Added to the AI prompt. |
| `contextFiles` | Project files added to the AI prompt (one per line). |
| `includeAiPrompt` | Enables the "Copy for AI" action. |
| `enableDebugOverlay` | Creates the runtime panel. |
| `showInReleaseBuilds` | Also show it in non-development builds (QA only). |
| `toggleKey` / `showFloatingButton` | F9 / `TRACK DBG`. |
| `mobileScale` / `desktopScale` | Initial overlay zoom (adjustable at runtime). |

Change any value while playing and press **Refresh** in the overlay (or reload Play mode).

---

## Runtime API

```csharp
using Wagenheimer.Tracking;

// Gate any SDK that reads the advertising identifier.
yield return TrackingConsent.WaitUntilResolved();

// Observe state.
bool required   = TrackingConsent.IsRequired;
bool answered   = TrackingConsent.IsAnswered;
bool resolved   = TrackingConsent.IsResolved;   // boot wait finished
var  status     = TrackingConsent.Current;      // Authorized, Denied, Restricted, ...
bool requesting = TrackingConsent.IsRequesting;

// History ("was ATT already asked on this device?").
int attempts          = TrackingConsent.RequestCount;
DateTime? first       = TrackingConsent.FirstRequestUtc;
DateTime? last        = TrackingConsent.LastRequestUtc;
bool wasRequested     = TrackingConsent.WasRequested;

// QA actions.
bool started = TrackingConsent.RequestAgain();  // re-sends the request even with a final answer
TrackingConsent.RefreshStatus();                // re-read the native status
TrackingConsent.ResetHistory();                 // clear local history only
TrackingDiagnostics.OpenAppSettings();          // opens this app's Settings page

// Diagnostics.
var snapshot = TrackingDiagnostics.GetAttSnapshot();
var checks   = TrackingDiagnostics.GetChecks();
string report = TrackingDiagnostics.BuildReport();
string ai     = TrackingDiagnostics.BuildAiPrompt();
```

### Events

| API | When |
|---|---|
| `TrackingConsent.Changed` | Status, request state or history changed. |
| `TrackingDiagnostics.AddCheckProvider(...)` | Register project-specific checks (for example the ad SDK state). |
| `TrackingDiagnostics.RemoveCheckProvider(...)` | Unregister. |

### Re-request semantics (iOS)

iOS presents the ATT alert only while the status is `NotDetermined`. Calling
`RequestAgain()` with a final status still sends the request (it returns immediately), records
the attempt and re-reads the status — useful to prove the behavior — but the alert does not
reappear. To see it again the permission must be reset:

- **Settings → Privacy & Security → Tracking** (allow apps to request tracking), or
- delete and reinstall the app.

The overlay explains this and offers **Open app settings**.

---

## Build Processor

- **Pre-build** (`callbackOrder 1001`): when the target is iOS and the build declares tracking,
  validates the purpose string, the iOS support package and the native plugin. With
  `failBuildOnInvalidConfiguration` a missing purpose string fails the build instead of shipping
  an ATT request that iOS silently ignores.
- **Post-build** (`callbackOrder 1001`, after SDK processors): writes or removes
  `NSUserTrackingUsageDescription` from the generated `Info.plist`, verifies the result, and
  records `Library/Wagenheimer.Tracking/last-build.json` for the checker.

The declaration rule is evaluated against the scripting defines of the build target, so the same
project can ship a Free build that declares tracking and a Full build that does not.

---

## Editor Checker

`Tools > Wagenheimer > Tracking > Tracking Checker...`

Sections:

1. **Configuration** — settings asset, master switch, declaration mode/define, purpose string,
   build safety, timing and overlay values.
2. **iOS readiness** — `com.unity.ads.ios-support`, native plugin, minimum iOS version.
3. **Last build** — reads `last-build.json` and verifies the generated `Info.plist` key and the
   `AppTrackingTransparency.framework` link. You can also point it at any build folder.
4. **Integration** — detects the LevelPlay Helper prefab and warns when its own ATT request is
   still enabled (double owner), with a one-click fix.
5. **Runtime** — how to validate on device.

Each finding can be copied (report) and, when it fails, comes with a ready-to-paste AI prompt.

### CI

The checker also runs headless and exits with code 1 on failures:

```bash
Unity -batchmode -nographics -quit -projectPath "<project>" \
  -executeMethod Wagenheimer.Tracking.Editor.TrackingCheckerCli.Run
```

`TrackingCheckerCli.FixLevelPlayAttRequest` disables the LevelPlay Helper's own ATT request
(the double-owner warning) when that package is installed.

---

## Runtime Debug Overlay

- Open with `F9` or the floating `TRACK DBG` button (top-left, border colored by health).
- Header: drag, `A-`/`A+` zoom (persisted per device), `Max`/`Rest`, close.
- Cards: diagnosis, ATT (status/answered/history/purpose string), environment/build, checks.
- Actions: request again, open Settings, read status, clear history, copy report, copy for AI.

The overlay is available in the editor and development builds; `showInReleaseBuilds` enables it in
a store-identical QA build.

---

## Integrating with an ad SDK

The gate is the only integration point:

```csharp
yield return TrackingConsent.WaitUntilResolved();
sdk.Initialize();
```

For **LevelPlay Helper**, keep the helper's own `requestAuthorization` **off** — the package owns
the request. The checker detects it and offers a one-click fix (or run
`TrackingCheckerCli.FixLevelPlayAttRequest` in batch mode). Register the SDK state as extra overlay checks:

```csharp
TrackingDiagnostics.AddCheckProvider(() => new[]
{
    new TrackingDiagnostics.Check(
        "Ads SDK",
        Ads.IsInitialized ? TrackingDiagnostics.CheckStatus.Ok : TrackingDiagnostics.CheckStatus.Warning,
        Ads.IsInitialized ? "SDK initialized." : "SDK not initialized yet.")
});
```

---

## Testing / QA

1. Build with the define applied (iOS Free).
2. First install → alert appears; overlay shows `NotDetermined` → answer → the status updates.
3. Press **Request ATT again** → the log shows `Re-sending the ATT request with status ...` and
   the status does not change (expected).
4. **Open app settings** → toggle tracking → return → the overlay refreshes on focus.
5. Reset: delete/reinstall the app or reset the permission in Settings.

The package ships EditMode tests under `Tests/Editor` (add the package to `testables` in
`manifest.json` to run them).

---

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Alert never appears | Key missing, build does not declare tracking, or a previous answer exists. Check the checker and the overlay history. |
| `NotDetermined` after the request | The alert was not presented (app not foreground, key missing, or the system "Allow Apps to Request to Track" is off). |
| Build fails on the purpose string | Fill a specific description in the settings. |
| Full game shows the key | `removeWhenNotDeclared` off, or another SDK post-processor wrote it. The checker verifies the generated plist. |
| Two alerts / request from another SDK | Disable the SDK's own ATT request; use this package as the single owner. |

---

## Architecture

```
Runtime/
  TrackingSettings.cs      ScriptableObject configuration (Resources)
  TrackingDeclaration.cs   Pure declaration rules (tested)
  TrackingConsent.cs       Runtime gate, request runner, history, events
  TrackingDiagnostics.cs   Snapshot, checks, reports, native probe
  UI/TrackingDebugOverlay.cs
  Plugins/iOS/WagenheimerTracking.mm
Editor/
  TrackingChecker.cs        Checks model + report/prompt (shared with CI)
  TrackingCheckerCli.cs     Batch-mode entry point (exit 1 on failures)
  TrackingCheckerWindow.cs  UI Toolkit window
  TrackingBuildProcessor.cs Pre/post build (Info.plist)
  TrackingLastBuild.cs      Last-build manifest for the checker
  TrackingSettingsEditor.cs Inspector
  TrackingPackageInfo.cs    Version/path
  TrackingMenuItems.cs      Tools menu
Tests/Editor/
  TrackingDeclarationTests.cs
  TrackingCheckerTests.cs
```

---

## License

MIT — see [LICENSE](LICENSE).

---

### Wagenheimer Unity ecosystem

[Package Hub](https://github.com/wagenheimer/UnityPackageHub) ·
[Unity Utils](https://github.com/wagenheimer/UnityUtils) ·
[Build Pipeline](https://github.com/wagenheimer/UnityBuildPipeline) ·
[Cloud Save](https://github.com/wagenheimer/UnityCloudSave) ·
[Rate Control](https://github.com/wagenheimer/UnityRateControl) ·
[IAP Helper](https://github.com/wagenheimer/UnityIAPHelper) ·
[LevelPlay Helper](https://github.com/wagenheimer/UnityLevelPlayHelper) ·
[Native Social](https://github.com/wagenheimer/UnityNativeSocial) ·
[Rewired Helper](https://github.com/wagenheimer/RewiredHelper) ·
[Timeline Typewriter](https://github.com/wagenheimer/UnityTimelineTypewriter) ·
[Tk2d Porter](https://github.com/wagenheimer/UnityTk2dPorter) ·
[Tracking](https://github.com/wagenheimer/UnityTracking) ·
[I2 Localization](https://github.com/wagenheimer/UnityI2Localization)

Explore every project at **[wagenheimer.com/opensource](https://wagenheimer.com/opensource)**.
