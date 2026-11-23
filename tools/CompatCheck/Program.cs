// Vérifie qu'un AIRaceEngineer.dll déjà compilé se chargera dans une version donnée de SimHub, sans le recompiler :
// chaque DLL qu'il référence doit être fournie par SimHub ou .NET Framework, chaque type, méthode, propriété ou champ de
// SimHub qu'il utilise doit encore exister avec la même signature, et les interfaces de SimHub qu'il implémente ne doivent
// pas avoir gagné ni changé de membre (sinon SimHub refuse de charger le plugin).
//   dotnet run --project tools/CompatCheck -- <AIRaceEngineer.dll> <dossier des DLL de SimHub>
// Code de sortie 0 : compatible ; 1 : incompatibilités (listées) ; 2 : erreur d'utilisation.
using Mono.Cecil;

if (args.Length != 2 || !File.Exists(args[0]) || !Directory.Exists(args[1]))
{
    Console.Error.WriteLine("Usage : CompatCheck <AIRaceEngineer.dll> <dossier des DLL de SimHub>");
    return 2;
}

string simHubDir = Path.GetFullPath(args[1]);
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(simHubDir);
// .NET Framework et WPF, pour les classes de SimHub qui en héritent (présents sur tout Windows)
string framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319");
foreach (var dir in new[] { framework, Path.Combine(framework, "WPF") })
    if (Directory.Exists(dir)) resolver.AddSearchDirectory(dir);
using var module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { AssemblyResolver = resolver });

// Assemblys fournis par SimHub (ceux qu'on trouve dans son dossier) : les seuls qu'on vérifie
var hosted = new HashSet<string>(
    Directory.GetFiles(simHubDir, "*.dll").Select(f => Path.GetFileNameWithoutExtension(f)),
    StringComparer.OrdinalIgnoreCase);

string? AssemblyOf(TypeReference type)
{
    type = type.GetElementType();
    while (type.DeclaringType != null) type = type.DeclaringType;
    return type.Scope switch
    {
        AssemblyNameReference a => a.Name,
        ModuleDefinition m => m.Assembly.Name.Name,
        _ => null,
    };
}
bool FromSimHub(TypeReference type) => AssemblyOf(type) is string a && hosted.Contains(a);

var problems = new SortedSet<string>();
int checkedCount = 0;

T? Resolve<T>(Func<T?> resolve, string what) where T : class
{
    try { return resolve(); }
    catch (AssemblyResolutionException e) { problems.Add($"{what} : assembly introuvable ({e.AssemblyReference.Name})"); return null; }
}

foreach (var type in module.GetTypeReferences().Where(FromSimHub))
{
    checkedCount++;
    if (Resolve(() => type.Resolve(), type.FullName) == null)
        problems.Add($"type manquant : {type.FullName} ({AssemblyOf(type)})");
}

foreach (var member in module.GetMemberReferences().Where(m => m.DeclaringType != null && FromSimHub(m.DeclaringType)))
{
    checkedCount++;
    IMemberDefinition? resolved = member switch
    {
        MethodReference m => Resolve(() => m.Resolve(), member.FullName),
        FieldReference f => Resolve(() => f.Resolve(), member.FullName),
        _ => null,
    };
    if (resolved == null)
        problems.Add($"{(member is MethodReference ? "méthode" : "champ")} manquant(e) ou changé(e) : {member.FullName}");
}

// Chaque assembly référencé doit se trouver dans le dossier de SimHub ou dans .NET Framework : sinon le plugin ne se
// charge pas (et les références vers une DLL que SimHub ne fournit plus seraient ignorées ci-dessus)
foreach (var reference in module.AssemblyReferences)
{
    try { resolver.Resolve(reference); }
    catch (AssemblyResolutionException) { problems.Add($"assembly introuvable dans SimHub et .NET Framework : {reference.FullName}"); }
}

// Interfaces de SimHub implémentées par le plugin (IPlugin, IDataPlugin, IWPFSettingsV2...) : chaque membre abstrait
// doit avoir une implémentation dans la classe du plugin, ou dans une de ses classes de base, avec la même signature
// (type de retour et ref compris), sinon le CLR refuse de charger la classe.
string ShortName(string name) => name.Substring(name.LastIndexOf('.') + 1);
string Signature(MethodReference m) =>
    m.ReturnType.FullName + " " + ShortName(m.Name) + "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) + ")";
// Interface ou méthode générique : ses T ne se comparent pas aux types concrets du plugin, seuls le nom et le nombre de
// paramètres comptent alors
string Key(MethodDefinition m) =>
    m.DeclaringType.HasGenericParameters || m.HasGenericParameters ? ShortName(m.Name) + "/" + m.Parameters.Count : Signature(m);

IEnumerable<TypeDefinition> SimHubInterfaces(TypeDefinition iface)
{
    yield return iface;
    foreach (var parent in iface.Interfaces)
        if (FromSimHub(parent.InterfaceType) && Resolve(() => parent.InterfaceType.Resolve(), parent.InterfaceType.FullName) is TypeDefinition p)
            foreach (var t in SimHubInterfaces(p)) yield return t;
}

foreach (var type in module.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
{
    var implemented = new HashSet<string>();
    // la classe et ses classes de base du plugin lui-même ; seules comptent les méthodes d'instance virtuelles publiques
    // (implémentation implicite) ou qui déclarent l'interface qu'elles implémentent (implémentation explicite)
    for (var t = type; t != null; t = t.BaseType as TypeDefinition)
        foreach (var m in t.Methods.Where(m => !m.IsStatic && m.IsVirtual && (m.IsPublic || m.HasOverrides)))
        {
            implemented.Add(Signature(m));
            implemented.Add(ShortName(m.Name) + "/" + m.Parameters.Count);
        }

    foreach (var iface in type.Interfaces.Where(i => FromSimHub(i.InterfaceType)))
    {
        if (Resolve(() => iface.InterfaceType.Resolve(), iface.InterfaceType.FullName) is not TypeDefinition def) continue;
        foreach (var required in SimHubInterfaces(def).SelectMany(i => i.Methods).Where(m => m.IsAbstract && !m.IsStatic))
        {
            checkedCount++;
            if (!implemented.Contains(Key(required)))
                problems.Add($"{type.FullName} n'implémente pas {Signature(required)} de {required.DeclaringType.FullName}");
        }
    }
}

Console.WriteLine($"{checkedCount} références à SimHub vérifiées dans {Path.GetFileName(args[0])}.");
if (problems.Count == 0)
{
    Console.WriteLine("Compatible.");
    return 0;
}
Console.WriteLine($"{problems.Count} incompatibilité(s) :");
foreach (var p in problems) Console.WriteLine("- " + p);
return 1;
