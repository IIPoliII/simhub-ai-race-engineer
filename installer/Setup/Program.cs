using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

// AIRaceEngineer-Setup.exe : demande la langue de l'installation (celle déjà installée, sinon celle de Windows), puis
// ouvre le MSI qu'il contient dans cette langue. Le MSI est en anglais, avec le français intégré en transformation « 1036 ».
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

        // Sans fenêtre (/passive, /quiet, /qn, /qb..., aussi avec -) : pas de question
        bool silent = args.Any(IsSilentSwitch);
        bool french = InstalledFrench() ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
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
            ResetClientDates();

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
            if (!silent) MessageBox.Show(e.Message, "AI Race Engineer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1603;
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static string Quote(string s) => s.IndexOf(' ') >= 0 && !s.StartsWith("\"") ? "\"" + s + "\"" : s;

    /// <summary>Option msiexec sans assistant : /quiet, /passive, /q, /qn..., /qb... (/qf et /qr gardent l'assistant).</summary>
    private static bool IsSilentSwitch(string a)
    {
        if (a.Length < 2 || (a[0] != '/' && a[0] != '-')) return false;
        string s = a.Substring(1).ToLowerInvariant();
        return s == "quiet" || s == "passive" || s == "q" || s.StartsWith("qn") || s.StartsWith("qb");
    }

    /// <summary>
    /// Langue de l'installation en place, null s'il n'y en a pas : la valeur écrite par le MSI depuis 0.5.0,
    /// sinon la langue du produit installé (les MSI 0.4.x, -fr et -en, ne l'écrivaient pas).
    /// </summary>
    private static bool? InstalledFrench()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\AIRaceEngineer"))
            {
                var lang = key?.GetValue("Language") as string;
                if (lang == "fr") return true;
                if (lang == "en") return false;
            }
        }
        catch { }
        try
        {
            var product = new StringBuilder(39);
            if (MsiEnumRelatedProducts(UpgradeCode, 0, 0, product) == 0)
            {
                var lcid = new StringBuilder(16);
                int size = lcid.Capacity;
                if (MsiGetProductInfo(product.ToString(), "Language", lcid, ref size) == 0 && int.TryParse(lcid.ToString(), out int code))
                    return code == FrenchLcid;
            }
        }
        catch { }
        return null;
    }

    private const string UpgradeCode = "{A228A2D8-BAAC-4221-99B1-239257A64C39}"; // celui d'installer/AIRaceEngineer.wxs

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern int MsiEnumRelatedProducts(string upgradeCode, int reserved, int index, StringBuilder productCode);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern int MsiGetProductInfo(string product, string property, StringBuilder value, ref int size);

    /// <summary>
    /// Fichiers du client réécrits sur place par le plugin (mise à jour) : modifiés après leur création, Windows
    /// Installer les prend pour des fichiers de l'utilisateur et garderait l'ancienne version. Le client n'a pas de
    /// fichier de réglages : on aligne la date de création sur la date de modification pour que le MSI les remplace.
    /// </summary>
    private static void ResetClientDates()
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIRaceEngineer", "client");
            if (!Directory.Exists(dir)) return;
            foreach (string f in Directory.GetFiles(dir)) // ni .venv ni les modèles
                try { File.SetCreationTimeUtc(f, File.GetLastWriteTimeUtc(f)); } catch { }
        }
        catch { }
    }

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
