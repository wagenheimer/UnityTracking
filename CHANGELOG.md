# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
