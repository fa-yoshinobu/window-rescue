## Download
- [Windows x64 ZIP]({{DOWNLOAD_URL}})
- [SHA-256 checksum]({{CHECKSUM_URL}})

## Getting started
Extract the ZIP file, then run `WindowRescue.exe`.

Requirements: Windows 11 (x64). The .NET runtime is included.

## Changes
### Added
- Bounded local error logs and notifications for unexpected errors.
- Editable display numbers and shortcuts for every connected display.
- Build/test CI and scheduled checks for bundled runtime updates.

### Changed
- Updated the bundled .NET and Windows Desktop runtime to 10.0.15.
- Improved display refresh caching, build packaging and maintenance documentation.

### Fixed
- Desktop context-menu and second-instance commands now reach an elevated instance for the same Windows account, with visible delivery errors.
- Initially cloaked Store app windows are corrected without moving existing windows during virtual-desktop switches.
- Default shortcuts no longer block AltGr input on supported keyboard layouts.
- Unresponsive-window moves are bounded, and automatic and manual rescue operations are serialized.
- Settings validation is consistent, newer settings formats are protected, and moved context-menu registrations are repaired at startup.

<!-- virustotal:start -->
{{VIRUSTOTAL_CONTENT}}
<!-- virustotal:end -->
