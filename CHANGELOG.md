# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.2] - 2026-11-12

### Added
- declare scripting defines for the Build Pipeline 2.0 registry

### Changed
- chore: ignore Git Bash crash dumps (*.stackdump)

## [0.5.1] - 2026-11-01

### Changed
- test: clear the requirement provider before each test

## [0.5.0] - 2026-11-01

### Added
- block iOS builds with an unresolved CLI profile

## [0.4.0] - 2026-11-01

### Added
- validate build profiles automatically (no game code)

## [0.3.0] - 2026-11-01

### Added
- Automatic **build-pipeline integration**: the checker now reads `ProjectBuildConfig` by asset type
  (SerializedObject, no compile-time dependency) and reports one requirement per build profile. The
  only configuration is `TrackingSettings.adsDefine`: a profile that applies that define ships ads, so
  it must also apply `scriptingDefine`. Games no longer need a per-project editor script; projects
  without the build pipeline simply see the rule skipped.
- Honour the pipeline's removal syntax: a profile that lists `!DEFINE` / `-DEFINE` does not apply it.
- Tests for the define matching.

### Changed
- The active-target declaration check defers to the profile rule only when the rule is actually active
  (ads define set and a config asset present), instead of whenever a provider is registered.

## [0.2.0] - 2026-11-01

### Added
- `TrackingSettings.requireTracking`: marks ATT as a release requirement. The checker reports **FAIL**
  instead of **WARN** for the mandatory items and the iOS pre-build step blocks the build while any of
  them fails (it ignores `failBuildOnInvalidConfiguration`, because this is the release gate).
- `TrackingRequirements` editor API: the game registers a provider with
  `TrackingRequirements.SetProvider(...)` and returns one `TrackingRequirement` per rule (for example
  "every iOS build profile that ships ads must apply `FTDOTD_TRACKING`"). The provider result feeds the
  checker, the report, the AI prompt and the build gate, so the package stays ad-SDK agnostic.
- Checker section **Mandatory**: policy switch, project requirements and the two single-owner scans.
- Single-owner scans: the checker now reads the project's own scripts (`Assets/**/*.cs`, comments
  ignored) and fails when anything outside the package writes `NSUserTrackingUsageDescription` or calls
  the ATT request API (`ATTrackingStatusBinding`, `ATTrackingManager`, `RequestAuthorizationTracking`).
- `TrackingChecker.ApplyIosScriptingDefine()` / one-click fix for `ScriptingDefine` mode.
- Runtime: `TrackingDiagnostics` adds a **Mandatory policy** error when the project requires ATT but the
  app bundle has no purpose string, and the reports print `policy=MANDATORY|advisory`.
- Tests for the provider API.

### Changed
- Checker report prints `ATT requirement: MANDATORY|advisory` and counts mandatory failures.
- Settings inspector explains the mandatory policy and its consequences.

## [0.1.5] - 2026-10-29

### Changed
- docs: update install pin and version badge to the current release

## [0.1.4] - 2026-10-27

### Fixed
- keep the debug pill out of UI Toolkit navigation

## [0.1.3] - 2026-10-26

## [0.1.2] - 2026-10-25

### Fixed
- remove the duplicate `[MenuItem]` on `TrackingCheckerWindow.Open`; the menu entry now lives only in
  `TrackingMenuItems`, which stops Unity logging "Cannot add menu item
  'Tools/Wagenheimer/Tracking/Tracking Checker...' because a menu item with the same name already exists".

## [0.1.1] - 2026-10-25

### Fixed
- verify the plist value without the removed PlistElement.type API

## [0.1.0] - 2026-10-25

### Added
- initial Tracking Helper package

## [Unreleased]

### Added
- Initial release: ATT consent gate, request history/re-request, native iOS probe,
  `Info.plist` build processor, editor checker and UI Toolkit runtime overlay.
