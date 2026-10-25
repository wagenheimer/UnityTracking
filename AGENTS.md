## UI Toolkit: leading-icon text

Never put an emoji/symbol inline at the start of a `Button.text` (or a lone `Label`) — on Windows the fallback glyph draws wider than it measures and the following text overlaps the icon. Use a dedicated `VisualElement` for the icon and a separate `Label` for the text, or reserve the icon box explicitly.

## Two owners rule

The package must be the only component that requests ATT and the only build processor that writes
`NSUserTrackingUsageDescription`. When integrating with an SDK that also requests ATT, disable the
SDK's request (the editor checker reports it) instead of adding a second request path.

## Diagnostics must be copy-pasteable

Every new runtime check must include enough context (status, platform, values, next step) to be
pasted into an issue. The overlay's check rows and the editor checker build their copy text from the
same source.
