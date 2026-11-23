# Changelog

**English** · [Français](CHANGELOG.fr.md)

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [0.5.1] - 2026-11-23

### Changed
- Updates: the "Voice client and updates" section at the bottom of the page now shows "Install X.Y.Z" when an
  update is waiting, then "Restart SimHub" once it is installed, like the banner at the top. Before, it only
  offered "Check for updates", even with an update waiting. The button appears from this version on, so for
  the update to 0.5.1 itself, use "Install" in the banner at the top.

## [0.5.0] - 2026-11-23

### Added
- The plugin page, the welcome window and the ? guides in English and French. They follow the Language setting,
  and so do the voice client's messages: switching the language redraws the page.
- `AIRaceEngineer-Setup.exe` asks for the setup language, English or French (the language of your current install
  is preselected, otherwise your Windows language), then installs. With `/passive` or `/quiet`, it installs
  without asking.
- A single `AIRaceEngineer-Setup.msi` with both languages, for silent deployment (`ProductLanguage=1033` for English,
  `1036` for French). It replaces the separate English and French MSIs, and upgrading works whatever language
  was installed before.
- The one-line installer speaks English or French, following Windows.
- GitHub Actions builds and publishes every release, and checks every day that the plugin still loads in
  the latest SimHub.
- Screen readers read the name of each toggle and field on the plugin page.

### Changed
- The plugin page now uses the engineer's language (Language and voice). Settings from 0.4.x keep their language;
  the language picked in Setup.exe applies to new installs.
- OpenAI: the recommended model is now gpt-6-luna, and the update switches you to it if you used gpt-4.1-nano
  (retired), gpt-4.1-mini or gpt-4o-mini (both retired on January 14, 2027). You can pick those two again in the
  "Model" list until then.
- AI on your PC (Ollama): the guide now recommends qwen3:4b-instruct; qwen3:4b now downloads a thinking model,
  too slow for the radio. gemma3:4b replaces mistral-small3.2 in the recommended models.
- Cloudflare: Gemma 4 26B replaces Llama 3.1 8B in the recommended models. Llama 3.1 8B is no longer offered:
  if you used it, the update switches you to Llama 3.3 70B.
- The voice client installs the exact dependency versions tested for the release.
- Lighter on the machine: the page stops refreshing when SimHub is minimized and slows down when SimHub is in
  the background, and text fields are saved when you stop typing.
- First-time setup: the "Voice client" box shows "Installing 1/2", then "2/2", with the current step and the size
  of the speech recognition downloads (the full text on hover). If the offline voice can't work on the PC, the box
  says so once setup is done.
- The update banner's "What's new" button is now "Release notes" and opens this changelog in your language.
- Repair: run `AIRaceEngineer-Setup.exe` again and choose Repair (Programs and Features no longer offers it).
- The French installer uses "tu" on every screen, and its licence page starts with a note in French.
- Infomaniak: "Get a key" opens the API tokens page used in the guide.

### Fixed
- Claude: questions no longer fail with error 400, and "Test connection" now sends the same request as the engineer
  (no temperature, room left for thinking).
- A retired or unknown model is no longer mistaken for a wrong Account ID or Product ID: "Test connection" shows
  the provider's message and offers the recommended models.
- Settings are saved safely: a crash during a save no longer loses them (settings.json.bak), and an unreadable file
  is kept as settings.json.bad instead of being replaced by defaults. The page tells you when this happens.
- The offline voice works with a Windows profile name that has accents, and uses at most half of the CPU cores,
  like speech recognition.
- ElevenLabs: if the key, quota or Voice ID is refused, the offline voice takes over instead of staying silent.
  An empty Voice ID uses the default voice.
- When the online voice fails, the offline voice speaks right away for a minute instead of retrying before each
  sentence, and callouts that are too old are dropped instead of read late.
- An empty answer from the AI (a model that thinks before it speaks) no longer looks like a voice failure:
  the engineer tells you, and the page names the model.
- The "Voice client" box keeps showing errors and warnings instead of "Ready to answer" (a microphone problem
  included), and "Radio check" and "Listen" stay locked while the engineer answers.
- The voice client no longer crashes when there is no audio output, and a Windows proxy no longer cuts it off
  from SimHub.
- Speech recognition really warms up at startup, so the first question is no slower than the next ones, and
  changing the language no longer reloads it.
- Multiclass: your position, the gap to the leader and the cars ahead and behind are now all within your class
  (they were mixed with the overall order); the engineer also knows your overall position.
- Personal best: cut laps no longer count, and starting SimHub mid-session no longer gives a false personal best.
- Fuel: the warning no longer says one lap less than the setting and comes back after every refuel; average
  consumption leaves out pit laps and incomplete laps; the engineer uses SimHub's fuel unit (litres or gallons).
- When the game doesn't provide the engine map, brake bias, tank size, position or air and track temperatures,
  the engineer says it doesn't know instead of quoting 0. The leader is no longer given as the car behind when
  your position is unknown.
- Changing games in SimHub no longer restarts the voice client, interrupts its first-time setup or clears its log.
  An update that was already prepared is no longer lost, and the restart prompt shows only once.
- A voice client left behind by a SimHub crash now exits instead of answering alongside the new one.
- First-time setup no longer gets stuck on some PATH settings or when uv lands in an unusual folder, no longer
  restarts in a loop, and its error message tells you what to do.
- The plugin page no longer freezes while the voice client starts, and the "Voice client" box says when port 8889
  is taken instead of promising an automatic start.
- Updates: a failed copy no longer leaves SimHub without the plugin; the banner says "ready" for an update that
  installs at restart and no longer shows "Installing" after a later check; running `AIRaceEngineer-Setup.exe`
  over a version updated from the plugin replaces every voice client file. A Windows user name with an apostrophe
  no longer breaks updates or the one-line installer.
- "Model" list: refreshing it no longer overwrites the model you are typing. The "Test connection" result goes away
  when you change the key, account or model.
- The welcome window no longer comes back at the next start, and a guide or the welcome window no longer opens twice.
- The installer no longer promises to relaunch SimHub when you repair or uninstall, and "Launch SimHub" on its last
  page no longer shows a grey box.
- Microsoft renamed the William voice: your choice is kept.

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

[0.5.1]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.8...v0.5.0
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
