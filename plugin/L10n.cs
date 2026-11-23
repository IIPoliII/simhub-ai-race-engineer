namespace AIRaceEngineer
{
    /// <summary>
    /// Langue de l'interface (français ou anglais), celle choisie dans la page : <see cref="EngineerSettings.Language"/>.
    /// Chaque texte est écrit dans les deux langues à l'endroit où il sert : <c>T("Démarrer", "Start")</c>.
    /// </summary>
    public static class L10n
    {
        /// <summary>« fr » ou « en ». Mis à jour au chargement des réglages et à chaque changement de langue.</summary>
        public static string Language { get; set; } = "fr";

        public static bool Fr => Language == "fr";

        public static string T(string fr, string en) => Fr ? fr : en;
    }

    /// <summary>
    /// Texte gardé dans les deux langues, affiché dans la langue du moment. Pour les états produits en arrière-plan
    /// (mise à jour, mise en route du client) : ils changent de langue avec la page, sans attendre le prochain état.
    /// </summary>
    public sealed class Msg
    {
        public readonly string FrText, EnText;

        public Msg(string fr, string en) { FrText = fr; EnText = en; }

        public override string ToString() => L10n.Fr ? FrText : EnText;

        /// <summary>Même texte dans les deux langues (un nom propre, un identifiant).</summary>
        public static implicit operator Msg(string same) => same == null ? null : new Msg(same, same);

        public static implicit operator string(Msg m) => m?.ToString();
    }
}
