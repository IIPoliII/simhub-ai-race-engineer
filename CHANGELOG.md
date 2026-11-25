# Changelog

**English** · [Français](CHANGELOG.fr.md)

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [0.7.0] - 2026-11-25

### Added
- Voice adjustments (new "Race engineer" card, off by default): "brake bias 56", "brake balance 60 front 40 rear",
  "ABS 3", "one click less TC". The engineer presses the SimHub Control Mapper roles your buttons already use (ABS+,
  TractionControl-, Brake Bias +...) and reads the value back from the telemetry after each press, until it reaches
  the value asked for: nothing more to bind, in every game linked to the Control Mapper that gives the value to SimHub
  (Automobilista 2, ACC, iRacing, LMU, rFactor 2...). Without the Control Mapper, game keyboard keys can be used instead.
- Unprompted advice, like a real race engineer (never, rarely or sometimes, your choice): when crossing the line, if
  the front tyres are much hotter than the rears (or the other way round) or pressures are very uneven, the engineer
  says so and suggests a change. Never the same topic twice in a row.
- "Fuel for ten laps": the engineer works out how much to add from the last laps' consumption.
- "Boxing in two laps": the pit plan waits for the right lap.
- iRacing: during "box box", fuel, tyres and fast repair go straight into the pit menu. Other games have no reliable
  way to fill that menu: the engineer tells you what to set.
- Setup advice: "I've got understeer, what do you suggest?" gives one or two ideas, first while driving (brake bias,
  ABS, TC), then in the garage.

- Automatic "Damage, puncture, wheel lost" callout (on by default): one sentence per hit, once the car has settled
  ("The front left suspension took a small hit.", "Careful, the front right tyre is down."), worded like a real radio call and varied. In Automobilista 2 and Project CARS 2,
  suspension wheel by wheel, engine, aero, punctures and lost wheels come from the game; elsewhere, body damage and a
  puncture spotted from a tyre much lower than the others. Nothing in the pits, nor for damage already there at start.

### Fixed
- Hotlap with a ghost (AMS2 time trial, hotlap, hotstint, superpole): the ghost is no longer taken for an opponent.
  The engineer no longer gives a gap to "the driver behind" or a position when you drive alone.
- Engineer silent after switching games in SimHub: the voice client was given a copy of the port 8889 socket, so
  when SimHub recreated the plugin the port stayed taken with nobody answering ("Only one usage of each socket
  address" in the log) until SimHub was restarted. The port now moves from one plugin instance to the next without
  being closed, and is retried every 5 seconds if it is taken.

- Damage: the engineer says where the damage is instead of a single average. In Automobilista 2 (and Project CARS
  2), it gets aero, engine, suspension and brakes wheel by wheel: "Front right suspension at forty percent" rather
  than "average damage at seven percent, nothing critical". Damaged suspension explains an off-centre steering
  wheel, and on "box box" the engineer suggests fixing it even when the average is low. Other games only give the
  average: the engineer says so.

## [0.6.2] - 2026-11-25

### Fixed
- Places gained or lost: no more flood of callouts at the start or in a battle. Nothing during the first lap, then
  a single summary since the grid ("P7, you gained 5 places."); after that at most one callout every 20 seconds,
  with the summary since the previous one. A place lost then won back isn't announced.

## [0.6.1] - 2026-11-25

### Added
- "Press to talk, press again to send" push-to-talk, instead of holding the button: a setting in the Push-to-talk
  card. It works with every SimHub press type, and the radio closes on its own after 30 seconds.
- Push-to-talk test in the page, without starting a game: hold your button, say something and release. The
  Push-to-talk card shows the microphone used, its level and the sentence understood, or what's in the way: button
  released too quickly, silent microphone, voice client stopped.
- A push-to-talk mapping set to "ShortPress" is detected: SimHub then sends the press and the release at once and
  nothing is recorded. The page says to pick "During" (or the press / press again mode).

### Fixed
- Sector times: a gap of a few thousandths is no longer called "same time". The engineer says "four thousandths"
  or "two hundredths up", and "same time" only for a tie to the thousandth. Same for the gap to the previous lap.

## [0.6.0] - 2026-11-24

### Added
- Voice volume and track noise volume: two sliders in "Language and voice". The voice goes up to 200% without
  distorting (peaks are rounded off), the background from 0 to 200%. Double-click: back to 100%. Changing a volume
  doesn't reload the voice.
- "Lap invalidated." callout as soon as the game invalidates your lap (track limits, cut...), and the lap time
  says so too: "Lap time 1 47 3, invalid."
- Sector splits outside races (practice, qualifying, hotlap): at the end of each sector, the engineer tells you how
  far up or down you are on your best lap, for example "Sector 2: three tenths up." Nothing in races, nor for a lap
  already invalidated or started from the pits.

### Fixed
- "Other" AI (Ollama, LM Studio): a URL without `/v1` (`http://10.0.0.5:11434/`) answered "Address not found (404)"
  and the model list stayed empty. `/v1` is now added when missing.
- "Other" AI: models that think before answering (Qwen3...) gave empty answers, their thinking using up all the
  room. The engineer now asks them to answer directly; a server that doesn't know this setting is asked again
  without it.
- After the voice client stopped unexpectedly, its automatic restart no longer says "Radio check, loud and clear"
  mid-race (instead of the lap time). The page shows in orange that it was restarted and why, and the log keeps
  what happened before the stop.
- The voice client no longer exits when SimHub takes more than 20 seconds to answer: it waits as long as SimHub is
  running. A one-off problem in its loop is written to the log instead of stopping it.

## [0.5.5] - 2026-11-23

### Fixed
- "Box box": tyre names are better understood ("hard" said in French was heard as "arbre", "mets des hards" as
  "Mediards"), and a tyre choice that makes no sense gets asked again instead of being accepted. The engineer no
  longer picks the tyres for you.
- "Box box": the fuel and tyres you say are kept even when the AI forgets to note them, and your own words win over
  its summary.
- The engineer's answers no longer contain markdown asterisks, and an answer that came back empty during a stop is
  replaced by a summary of the plan.

## [0.5.4] - 2026-11-23

### Fixed
- Speech recognition now hears "box box" and "pit pit" (it heard "Bix, Bix" or "Pete, Pete"), and the engineer
  recognises the call even when it is misheard.
- When the engineer didn't understand the question, it says so and asks you to repeat, instead of answering beside
  the point. It no longer gives orders you didn't ask for (such as "stay in the pits").
- "Box box" early in the race, before the fuel can be worked out: the engineer asks how much fuel you want instead
  of suggesting a full tank.

### Changed
- The log shows whether the voice came from the online service or the local voice, and how long it took.

## [0.5.3] - 2026-11-23

### Added
- "Box box": say it on the radio and the engineer prepares your stop. It gives the fuel to add to finish with one
  lap of margin, asks which tyres you want, sums up the plan, then reminds you of it as you enter the pit lane.
  "Cancel the box" cancels it. Works with every game; you still set the pit menu yourself.
- Release notes inside SimHub: the "Release notes" button in "Voice client and updates" shows them without a
  browser, and after an update a "What's new" window lists what changed since your previous version.

## [0.5.2] - 2026-11-23

### Added
- Swiss numbers (French only, in Language and voice): the engineer says septante, huitante, nonante, in its answers
  and in the callouts, whatever the voice. Ticked by default when Windows is set to Switzerland.
- Two Swiss voices among the online voices: Fabrice and Ariane. When Windows is set to Switzerland, Ariane
  becomes the default French voice (the update switches you to her if you still had Henri).

### Fixed
- In the pits, the engineer no longer gives the running lap clock as a lap time.
- In practice and qualifying, the engineer no longer says you are leading before you have set a time.
- The engineer answers only the question asked, instead of adding figures that are sometimes wrong, and calls
  practice "practice".
- "No telemetry" is now "Nothing from the game yet" when SimHub is running but you are not on track.

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

[0.6.2]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.6.1...v0.6.2
[0.6.1]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.5.5...v0.6.0
[0.5.5]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.5.4...v0.5.5
[0.5.4]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.5.3...v0.5.4
[0.5.3]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.5.2...v0.5.3
[0.5.2]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.5.1...v0.5.2
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
