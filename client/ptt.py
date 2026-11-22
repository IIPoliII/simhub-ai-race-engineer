"""Push-to-talk : bouton du volant (pygame) et/ou touche clavier (pynput)."""
import os
import time

os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")
# Lire les boutons même quand le jeu a le focus
os.environ.setdefault("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1")

import pygame  # noqa: E402
from pynput import keyboard  # noqa: E402


def _parse_key(name: str):
    name = name.strip().lower()
    if not name:
        return None
    if hasattr(keyboard.Key, name):
        return getattr(keyboard.Key, name)
    return keyboard.KeyCode.from_char(name)


class PushToTalk:
    def __init__(self, cfg: dict):
        self.button = int(cfg.get("joystick_button", -1))
        self.device_filter = (cfg.get("joystick_name") or "").lower()
        self.key = _parse_key(cfg.get("keyboard_key", ""))
        self._key_down = False
        self._joysticks = {}

        if self.button >= 0:
            pygame.init()
            pygame.joystick.init()
            self._rescan()

        if self.key is not None:
            keyboard.Listener(on_press=self._on_press, on_release=self._on_release, daemon=True).start()

    def describe(self) -> str:
        parts = []
        if self.key is not None:
            parts.append(f"touche {self.key}")
        if self.button >= 0:
            names = ", ".join(j.get_name() for j in self._joysticks.values()) or "aucun périphérique trouvé"
            parts.append(f"bouton {self.button} ({names})")
        return " ou ".join(parts) or "rien (configure [ptt])"

    def _rescan(self):
        self._joysticks = {}
        for i in range(pygame.joystick.get_count()):
            js = pygame.joystick.Joystick(i)
            if self.device_filter and self.device_filter not in js.get_name().lower():
                continue
            if self.button < js.get_numbuttons():
                self._joysticks[js.get_instance_id()] = js

    def _same_key(self, key) -> bool:
        if isinstance(self.key, keyboard.KeyCode) and isinstance(key, keyboard.KeyCode):
            return (key.char or "").lower() == (self.key.char or "")
        return key == self.key

    def _on_press(self, key):
        if self._same_key(key):
            self._key_down = True

    def _on_release(self, key):
        if self._same_key(key):
            self._key_down = False

    def pressed(self) -> bool:
        joy_down = False
        if self.button >= 0:
            for event in pygame.event.get():
                if event.type in (pygame.JOYDEVICEADDED, pygame.JOYDEVICEREMOVED):
                    self._rescan()
            joy_down = any(js.get_button(self.button) for js in self._joysticks.values())
        return self._key_down or joy_down


def list_controls(seconds: int = 60):
    """Affiche les périphériques et les boutons pressés, pour remplir [ptt]."""
    pygame.init()
    pygame.joystick.init()
    # Garder une référence : un Joystick ramassé par le GC n'envoie plus d'événements
    devices = {}
    for i in range(pygame.joystick.get_count()):
        js = pygame.joystick.Joystick(i)
        devices[js.get_instance_id()] = js
        print(f"[{i}] {js.get_name()}  ({js.get_numbuttons()} boutons)")
    if not devices:
        print("Aucun volant / joystick détecté.")

    def on_press(key):
        name = key.name if isinstance(key, keyboard.Key) else key.char
        print(f"  clavier : keyboard_key = \"{name}\"")

    keyboard.Listener(on_press=on_press, daemon=True).start()
    print(f"\nAppuie sur le bouton que tu veux utiliser ({seconds} s, Ctrl+C pour quitter)...")
    end = time.time() + seconds
    try:
        while time.time() < end:
            for event in pygame.event.get():
                if event.type == pygame.JOYDEVICEADDED:
                    js = pygame.joystick.Joystick(event.device_index)
                    devices[js.get_instance_id()] = js
                elif event.type == pygame.JOYBUTTONDOWN:
                    js = devices.get(event.instance_id)
                    name = js.get_name() if js else "?"
                    print(f"  {name} : joystick_button = {event.button}   (joystick_name = \"{name}\")")
            time.sleep(0.01)
    except KeyboardInterrupt:
        pass
