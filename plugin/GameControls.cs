using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.RegularExpressions;
using System.Windows.Input;
using SimHub.Plugins;

namespace AIRaceEngineer
{
    /// <summary>
    /// Réglages faits dans le jeu à la voix. SimHub ne fait que lire la télémétrie : pour écrire, il n'y a que
    /// deux chemins, qui dépendent du jeu.
    /// - iRacing : son SDK accepte des commandes du menu des stands (essence, pneus, réparation rapide).
    /// - Les autres réglages du cockpit (répartition de freinage, ABS, TC) : on « appuie » sur le rôle du Control Mapper
    ///   de SimHub (ABS+, TractionControl-, BrakeBalanceFront...) que le pilote utilise déjà pour ses boutons, sinon sur les
    ///   touches clavier associées dans le jeu. Après chaque appui, on relit la valeur dans la télémétrie jusqu'à la
    ///   cible. Ça marche dans tous les jeux qui renvoient la valeur (AMS2, ACC, iRacing, LMU, rFactor 2...).
    /// </summary>
    internal static class GameControls
    {
        public const string IRacing = "IRacing";

        // ---------- iRacing : menu des stands par le SDK ----------

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool SendNotifyMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private static uint _irsdkMsg;
        private const int BroadcastPitCommand = 9;          // irsdk_BroadcastPitCommand
        private const int PitClear = 0, PitFuel = 2, PitLF = 3, PitRF = 4, PitLR = 5, PitRR = 6,
                          PitClearTires = 7, PitFastRepair = 8, PitClearFastRepair = 10, PitClearFuel = 11;

        private static void PitCommand(int cmd, int value = 0)
        {
            if (_irsdkMsg == 0) _irsdkMsg = RegisterWindowMessage("IRSDK_BROADCASTMSG");
            // wParam = MAKELONG(message, commande), lParam = valeur ; HWND_BROADCAST, sans attendre de réponse
            var wParam = (IntPtr)((BroadcastPitCommand & 0xFFFF) | ((cmd & 0xFFFF) << 16));
            SendNotifyMessage((IntPtr)0xFFFF, _irsdkMsg, wParam, (IntPtr)value);
        }

        /// <summary>
        /// Programme d'arrêt dans le menu des stands d'iRacing. fuel en litres (null : on n'y touche pas, 0 : pas
        /// d'essence) ; tyres : changer les 4 pneus ou non (null : on n'y touche pas) ; repair : réparation rapide.
        /// </summary>
        public static void IRacingPitPlan(double? fuelLitres, bool? tyres, bool? repair)
        {
            if (fuelLitres is double f)
            {
                if (f >= 1) PitCommand(PitFuel, (int)Math.Round(f));
                else PitCommand(PitClearFuel);
            }
            if (tyres == true)
            {
                // Valeur 0 : la pression du menu ne change pas
                PitCommand(PitLF); PitCommand(PitRF); PitCommand(PitLR); PitCommand(PitRR);
            }
            else if (tyres == false) PitCommand(PitClearTires);
            if (repair == true) PitCommand(PitFastRepair);
            else if (repair == false) PitCommand(PitClearFastRepair);
        }

        // ---------- Touches du jeu ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public InputUnion u; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;  // le plus grand membre : donne la bonne taille à INPUT
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private const uint KeyUp = 0x0002, ScanCode = 0x0008, Extended = 0x0001;

        /// <summary>Une touche avec ses modificateurs, lue depuis le réglage (« Ctrl+Shift+F5 », noms WPF).</summary>
        public sealed class Binding
        {
            public ModifierKeys Modifiers;
            public Key Key;

            public static Binding Parse(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return null;
                var b = new Binding();
                foreach (var part in text.Split('+'))
                {
                    var p = part.Trim();
                    if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) b.Modifiers |= ModifierKeys.Control;
                    else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) b.Modifiers |= ModifierKeys.Shift;
                    else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) b.Modifiers |= ModifierKeys.Alt;
                    else if (Enum.TryParse(p, true, out Key k) && k != Key.None) b.Key = k;
                    else return null;
                }
                return b.Key == Key.None ? null : b;
            }

            public static string Format(ModifierKeys modifiers, Key key)
            {
                var s = "";
                if ((modifiers & ModifierKeys.Control) != 0) s += "Ctrl+";
                if ((modifiers & ModifierKeys.Shift) != 0) s += "Shift+";
                if ((modifiers & ModifierKeys.Alt) != 0) s += "Alt+";
                return s + key;
            }
        }

        private static INPUT KeyInput(int vk, bool up)
        {
            // Code de scan plutôt que touche virtuelle : les jeux lisent le clavier en DirectInput / Raw Input
            uint scan = MapVirtualKey((uint)vk, 0);
            uint flags = ScanCode | (up ? KeyUp : 0);
            if (IsExtended(vk)) flags |= Extended;
            return new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = (ushort)scan, dwFlags = flags } } };
        }

        private static bool IsExtended(int vk) =>
            (vk >= 0x21 && vk <= 0x28)          // PageUp, PageDown, End, Home, flèches
            || vk == 0x2D || vk == 0x2E         // Insert, Delete
            || vk == 0x6F || vk == 0x90         // / du pavé numérique, NumLock
            || vk == 0xA3 || vk == 0xA5;        // Ctrl et Alt de droite

        private static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));

        /// <summary>Appui bref : assez long pour que le jeu le voie à sa prochaine lecture du clavier.</summary>
        public static void Press(Binding b)
        {
            int vk = KeyInterop.VirtualKeyFromKey(b.Key);
            if (vk == 0) return;
            var mods = new System.Collections.Generic.List<int>();
            if ((b.Modifiers & ModifierKeys.Control) != 0) mods.Add(0xA2);
            if ((b.Modifiers & ModifierKeys.Shift) != 0) mods.Add(0xA0);
            if ((b.Modifiers & ModifierKeys.Alt) != 0) mods.Add(0xA4);
            foreach (var m in mods) Send(KeyInput(m, false));
            Send(KeyInput(vk, false));
            Thread.Sleep(50);
            Send(KeyInput(vk, true));
            for (int i = mods.Count - 1; i >= 0; i--) Send(KeyInput(mods[i], true));
        }

        /// <summary>
        /// Les appuis partent vers la fenêtre au premier plan. Si c'est SimHub (le pilote règle sa page), ils
        /// taperaient dans SimHub : on refuse.
        /// </summary>
        public static bool GameInFront()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out var pid);
            return pid != 0 && pid != (uint)Process.GetCurrentProcess().Id;
        }

        public sealed class Result
        {
            public string result;      // done, already, no_value, no_effect, not_focused
            public double? value;      // valeur lue à la fin
            public double? before;
        }

        /// <summary>
        /// Appuie jusqu'à atteindre la cible (target) ou faire « steps » crans (signe = sens). read relit la valeur
        /// dans la télémétrie. Si « plus » fait baisser la valeur (certains jeux comptent la répartition vers l'arrière),
        /// les deux touches sont échangées une fois.
        /// </summary>
        public static Result Adjust(Func<double?> read, Action up, Action down, double? target, int steps, double tolerance,
                                    bool needsFocus)
        {
            var before = read();
            if (before == null) return new Result { result = "no_value" };
            // Touches clavier : elles partent vers la fenêtre au premier plan. Le Control Mapper, lui, passe par la
            // manette virtuelle, que le jeu lit même quand il n'est pas devant.
            if (needsFocus && !GameInFront()) return new Result { result = "not_focused", value = before, before = before };

            double v = before.Value;
            int dir = target is double t0 ? Math.Sign(t0 - v) : Math.Sign(steps);
            if (dir == 0 || (target is double t1 && Math.Abs(t1 - v) <= tolerance))
                return new Result { result = "already", value = v, before = before };

            var increase = up;
            var decrease = down;
            bool swapped = false;
            int changes = 0, stalls = 0;
            for (int i = 0; i < 80; i++)
            {
                (dir > 0 ? increase : decrease)();
                var nv = WaitForChange(read, v, 400);
                if (nv == null || Math.Abs(nv.Value - v) < 1e-6)
                {
                    // Rien ne bouge : touche pas associée dans le jeu, ou réglage au bout
                    if (++stalls >= 2) return new Result { result = changes == 0 ? "no_effect" : "limit", value = v, before = before };
                    continue;
                }
                stalls = 0;
                int moved = Math.Sign(nv.Value - v);
                if (moved != dir)
                {
                    if (swapped) return new Result { result = "no_effect", value = nv, before = before };
                    // Les touches font l'inverse : on les échange et on revient sur ce cran
                    swapped = true;
                    (increase, decrease) = (decrease, increase);
                    v = nv.Value;
                    continue;
                }
                v = nv.Value;
                changes++;
                if (target is double t)
                {
                    // Cible atteinte, ou dépassée d'un cran (crans plus gros que la précision demandée)
                    if (Math.Abs(t - v) <= tolerance || Math.Sign(t - v) != dir) break;
                }
                else if (changes >= Math.Abs(steps)) break;
            }
            return new Result { result = "done", value = v, before = before };
        }

        // ---------- Control Mapper de SimHub ----------

        /// <summary>
        /// Rôles du Control Mapper pour un réglage (bb, abs, tc) : (plus, moins), ou null s'il n'y en a pas.
        /// Les noms viennent de la liste de SimHub (« ABS+ », « TractionControl- », « BrakeBalanceFront »).
        /// </summary>
        public static (string Up, string Down)? MapperRoles(PluginManager pm, string kind)
        {
            var roles = AvailableRoles(pm);
            if (roles == null) return null;
            Regex pattern;
            switch (kind)
            {
                // SimHub : BrakeBalanceFront / BrakeBalanceRear (la répartition est le % à l'avant : « Front » la monte ;
                // si le jeu compte dans l'autre sens, Adjust échange les deux)
                case "bb": pattern = new Regex(@"^\s*brake\s*balance\s*(front|rear)\s*$|brake\s*bias\s*([+-])\s*$", RegexOptions.IgnoreCase); break;
                case "abs": pattern = new Regex(@"^\s*abs\s*([+-])\s*$", RegexOptions.IgnoreCase); break;
                case "tc": pattern = new Regex(@"^\s*(?:traction\s*control|tc)\s*([+-])\s*$", RegexOptions.IgnoreCase); break;
                default: return null;
            }
            string up = null, down = null;
            foreach (var role in roles)
            {
                var m = pattern.Match(role ?? "");
                if (!m.Success) continue;
                var sign = m.Groups.Cast<Group>().Skip(1).Select(g => g.Value).FirstOrDefault(v => v.Length > 0)?.ToLowerInvariant();
                if (sign == "+" || sign == "front") up = up ?? role;
                else down = down ?? role;
            }
            return up != null && down != null ? (up, down) : ((string, string)?)null;
        }

        /// <summary>Rôles de bouton du Control Mapper, ou null (SimHub trop ancien, Control Mapper absent).</summary>
        public static List<string> AvailableRoles(PluginManager pm)
        {
            if (pm == null) return null;
            try { return ReadRoles(pm); }
            catch (Exception) { return null; }   // MissingMethodException sur un SimHub sans cette API, etc.
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<string> ReadRoles(PluginManager pm) => pm.GetControlMapperInterface()?.GetAvailableButtonRoles()?.ToList();

        /// <summary>Appui bref sur un rôle du Control Mapper, comme le bouton du volant qui y est associé.</summary>
        public static void PressRole(PluginManager pm, string role)
        {
            try { StartStopRole(pm, role, 100); }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[AI Race Engineer] Control Mapper : " + ex.Message); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void StartStopRole(PluginManager pm, string role, int ms) => pm.GetControlMapperInterface()?.StartStopRole(role, ms);

        private static double? WaitForChange(Func<double?> read, double from, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            double? last = null;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                Thread.Sleep(20);
                last = read();
                if (last is double x && Math.Abs(x - from) > 1e-6)
                {
                    // Laisse la télémétrie se poser (certaines valeurs passent par un état intermédiaire)
                    Thread.Sleep(40);
                    return read() ?? x;
                }
            }
            return last;
        }
    }
}
