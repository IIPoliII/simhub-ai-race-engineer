using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// AIRaceEngineer-Setup.exe : demande la langue de l'installation (celle de Windows par défaut), puis ouvre le MSI
// qu'il contient dans cette langue. Le MSI est en anglais, avec le français intégré en transformation « 1036 ».
// Les options passées à l'exe vont à msiexec : /passive ou /quiet installe sans poser la question.
internal static class Program
{
    private const int EnglishLcid = 1033, FrenchLcid = 1036;

    [STAThread]
    private static int Main(string[] args)
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Sans fenêtre (/passive, /quiet, /qn...) : pas de question, la langue de Windows
        bool silent = args.Any(a => a.StartsWith("/q", StringComparison.OrdinalIgnoreCase)
                                 || a.Equals("/passive", StringComparison.OrdinalIgnoreCase));
        bool french = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
        if (!silent)
        {
            bool? choice = AskLanguage(french);
            if (choice == null) return 1602; // annulé par l'utilisateur, comme msiexec
            french = choice.Value;
        }

        string dir = Path.Combine(Path.GetTempPath(), "AIRaceEngineer-Setup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string msi = Path.Combine(dir, "AIRaceEngineer-Setup.msi");
        try
        {
            Directory.CreateDirectory(dir);
            using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("setup.msi"))
            using (var file = File.Create(msi))
                res.CopyTo(file);

            // ProductLanguage choisit la transformation intégrée à appliquer (sans lui, c'est la langue de Windows).
            // Pas TRANSFORMS=:1036 : Windows l'appliquerait deux fois (erreur 1624), et il ne peut pas l'empêcher.
            string lang = "ProductLanguage=" + (french ? FrenchLcid : EnglishLcid);
            string extra = string.Join(" ", args.Select(Quote));
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                Arguments = "/i " + Quote(msi) + " " + lang + " " + extra,
                UseShellExecute = false,
            });
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "AI Race Engineer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1603;
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static string Quote(string s) => s.IndexOf(' ') >= 0 && !s.StartsWith("\"") ? "\"" + s + "\"" : s;

    /// <summary>Petite fenêtre « Langue de l'installation ». true = français, false = anglais, null = annulé.</summary>
    private static bool? AskLanguage(bool french)
    {
        using (var form = LanguageForm(french, out var list))
        {
            if (form.ShowDialog() != DialogResult.OK) return null;
            return list.SelectedIndex == 1;
        }
    }

    internal static Form LanguageForm(bool french, out ComboBox list)
    {
        var form = new Form
        {
            Text = "AI Race Engineer",
            Font = SystemFonts.MessageBoxFont,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowIcon = false,
            StartPosition = FormStartPosition.CenterScreen,
            AutoScaleMode = AutoScaleMode.Font,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 14, 16, 12),
        };
        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        var label = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        list = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, Margin = new Padding(0, 0, 0, 16) };
        list.Items.AddRange(new object[] { "English", "Français" });
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        var cancel = new Button { DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(88, 0), Margin = new Padding(8, 0, 0, 0) };
        var ok = new Button { DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(88, 0), Margin = Padding.Empty };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(label);
        layout.Controls.Add(list);
        layout.Controls.Add(buttons);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        // Les textes suivent la langue choisie dans la liste
        var combo = list;
        combo.SelectedIndexChanged += (s, e) =>
        {
            bool fr = combo.SelectedIndex == 1;
            label.Text = fr ? "Choisis la langue de l'installation :" : "Choose the setup language:";
            ok.Text = "OK";
            cancel.Text = fr ? "Annuler" : "Cancel";
        };
        list.SelectedIndex = french ? 1 : 0;
        return form;
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();
}
