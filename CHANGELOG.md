# Changelog

**English** · [Français](CHANGELOG.fr.md)

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [0.4.8] - 2026-11-23

### Added
- Welcome window on first start: what the engineer does, which AI and voice to pick, how to set up push-to-talk.
  Reopen it any time with the ? button at the top of the plugin page.
- ? buttons next to each AI provider and next to ElevenLabs: step-by-step guides (where to sign up,
  what to copy into the plugin, what it costs).
- Release notes: this changelog, in English and French. GitHub release notes are taken from it.

### Changed
- The Claude key link now points to the Claude Console (platform.claude.com).

## [0.4.7] - 2026-11-22

### Added
- Windows installer (MSI) in English and French, without admin rights. It asks you to close SimHub if it is open
  and offers to launch it at the end. Uninstalling from Windows Settings removes the plugin, the voice client
  and its models, and keeps your settings.
- On first start, the plugin sets up the voice client by itself (uv, Python, dependencies, voice models),
  with the progress shown on its page. An interrupted setup resumes at the next start.
- README in English and French.

### Fixed
- The voice client could fail to start when SimHub reloaded the plugin.

## [0.4.6] - 2026-11-22

### Security
- Updates no longer give your account write access to the plugin file. A prepared update is only copied
  if its SHA-256 fingerprint matches the downloaded file.

## [0.4.5] - 2026-11-22

### Changed
- Updates install without an admin prompt when SimHub's folder is writable (the usual case): the new version
  is prepared in your profile and copied when SimHub closes.
- Lighter on the machine: the voice client waits for events instead of polling, telemetry is only formatted
  when needed, the client runs at below-normal priority and speech recognition uses at most half of the cores.
- Track noise: traffic is random, sometimes one car, sometimes a group, sometimes nothing.

## [0.4.4] - 2026-11-22

### Changed
- Quieter, smoother track noise.

## [0.4.3] - 2026-11-22

### Added
- Track noise: F1 cars sometimes pass behind the engineer's voice. Each car is synthesized for each message,
  so it never sounds the same twice, and the voice stays in front.

## [0.4.2] - 2026-11-22

### Added
- "Écouter" (Listen) button to hear the chosen voice. Speech buttons wait for the end of the sentence.
- "Comment l'associer" (How to map it) and "Configurer" (Set up) bring the right card into view.

### Changed
- Infomaniak's default model is listed under its real name (`mistralai/Ministral-3-14B-Instruct-2512`).

## [0.4.1] - 2026-11-22

### Added
- The model list is fetched from the provider, recommended models first.

## [0.4.0] - 2026-11-22

### Added
- Microsoft neural voices (online, free), including French voices.

### Changed
- Plugin page laid out in columns with a status strip.

## [0.3.0] - 2026-11-22

### Added
- Offer to restart SimHub after an update.

### Changed
- New plugin page.

## [0.2.0] - 2026-11-22

### Added
- All settings inside SimHub: AI provider, voice, callouts.
- Automatic callouts: lap time, best lap, gaps, positions, fuel.
- The voice client starts and stops with SimHub.

### Security
- Telemetry is escaped before being sent to the AI.

## [0.1.0] - 2026-11-22

### Added
- First version: SimHub plugin with JSON telemetry, Python voice client (local Whisper, AI, voice), push-to-talk.
- One-line installer and updates from SimHub.

[0.4.8]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.7...v0.4.8
[0.4.7]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.6...v0.4.7
[0.4.6]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.5...v0.4.6
[0.4.5]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.4...v0.4.5
[0.4.4]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.3...v0.4.4
[0.4.3]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.2...v0.4.3
[0.4.2]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.1...v0.4.2
[0.4.1]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/IIPoliII/simhub-ai-race-engineer/releases/tag/v0.1.0
