using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

// Dumps exact Il2CppInterop member signatures for the classes the mod hooks.
// Usage: InteropDump <gameDir> <outFile> [TypeName ...]
// Default type list covers everything the co-op mod touches.

string gameDir = args.Length > 0 ? args[0]
    : @"C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021";
string outFile = args.Length > 1 ? args[1]
    : @"C:\Users\alexu\Documents\Projects\CarMecaSim\re\interop-signatures.txt";

string interopDir = Path.Combine(gameDir, "BepInEx", "interop");
string mlNet6 = Path.Combine(gameDir, "BepInEx", "core");
string runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();

string[] requested = args.Length > 2 ? args[2..] : new[]
{
    "GameManager","GameScript","GameSettings","GameMode","SceneManager","GarageLevelManager","GameDataManager",
    "PartScript","MountObject","PartGroup","PartModule","CarLoader","CarLifter","CarSimulator",
    "ProfileData","NewCarData","PartData","MountObjectData",
    "Job","OrderGenerator","ShopBuyWindow","Warehouse","Inventory","GameInventory","Item",
    "FPSCamera","CharacterMotor","FPSInputController","ToolsManager","Raycast",
    "SteamManager",
    // enums
    "gameMode","SceneType","ToolType","CarFluidType",
};

var paths = new List<string>();
foreach (var d in new[] { interopDir, mlNet6, runtimeDir })
    if (Directory.Exists(d)) paths.AddRange(Directory.GetFiles(d, "*.dll"));
// de-dup by filename, prefer interop/ml over runtime
var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
foreach (var p in paths) { var n = Path.GetFileName(p); if (!byName.ContainsKey(n)) byName[n] = p; }

var resolver = new PathAssemblyResolver(byName.Values);
using var mlc = new MetadataLoadContext(resolver);  // auto-detect real core assembly (mscorlib/System.Runtime)

var asms = new List<Assembly>();
foreach (var p in new[] { "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll", "UnityEngine.CoreModule.dll" })
{
    var full = Path.Combine(interopDir, p);
    if (File.Exists(full)) { try { asms.Add(mlc.LoadFromAssemblyPath(full)); } catch (Exception e) { Console.Error.WriteLine($"load {p}: {e.Message}"); } }
}

Type FindType(string name)
{
    foreach (var a in asms)
    {
        var t = a.GetTypes().FirstOrDefault(x => x.Name == name || x.FullName == name);
        if (t != null) return t;
    }
    return null;
}

var sb = new StringBuilder();
sb.AppendLine("# Il2CppInterop signatures for CMS2021 co-op mod");
sb.AppendLine("# Generated from MelonLoader/Il2CppAssemblies. Fields are exposed as shown below.");
sb.AppendLine();

foreach (var name in requested)
{
    var t = FindType(name);
    if (t == null) { sb.AppendLine($"==== {name}  [NOT FOUND] ===="); sb.AppendLine(); continue; }
    sb.AppendLine($"==== {t.FullName}  :  {t.BaseType?.Name} {(t.IsEnum ? "(enum)" : "")} ====");
    sb.AppendLine($"     assembly: {t.Assembly.GetName().Name}");

    if (t.IsEnum)
    {
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            object v = null; try { v = f.GetRawConstantValue(); } catch { }
            sb.AppendLine($"    {f.Name} = {v}");
        }
        sb.AppendLine(); continue;
    }

    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    var fields = t.GetFields(flags).Where(f => !f.Name.Contains("<")).ToArray();
    if (fields.Length > 0)
    {
        sb.AppendLine("  -- Fields --");
        foreach (var f in fields)
        {
            string ty; try { ty = Short(f.FieldType); } catch { ty = "?"; }
            sb.AppendLine($"    {AccF(f)}{(f.IsStatic ? "static " : "")}{ty} {f.Name}");
        }
    }

    var props = t.GetProperties(flags);
    if (props.Length > 0)
    {
        sb.AppendLine("  -- Properties --");
        foreach (var p in props)
        {
            var g = p.GetMethod != null ? "get;" : "";
            var s = p.SetMethod != null ? "set;" : "";
            var st = (p.GetMethod?.IsStatic ?? p.SetMethod?.IsStatic ?? false) ? "static " : "";
            string ty; try { ty = Short(p.PropertyType); } catch { ty = "?"; }
            sb.AppendLine($"    {st}{ty} {p.Name} {{ {g}{s} }}");
        }
    }

    var methods = t.GetMethods(flags).Where(m => !m.IsSpecialName).ToArray();
    if (methods.Length > 0)
    {
        sb.AppendLine("  -- Methods --");
        foreach (var m in methods)
        {
            string sig;
            try
            {
                var ps = string.Join(", ", m.GetParameters().Select(p => { string pt; try { pt = Short(p.ParameterType); } catch { pt = "?"; } return $"{pt} {p.Name}"; }));
                string rt; try { rt = Short(m.ReturnType); } catch { rt = "?"; }
                sig = $"{AccM(m)}{(m.IsStatic ? "static " : "")}{rt} {m.Name}({ps})";
            }
            catch { sig = $"{m.Name}(?)"; }
            sb.AppendLine("    " + sig);
        }
    }
    sb.AppendLine();
}

Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
File.WriteAllText(outFile, sb.ToString());
Console.WriteLine($"Wrote {outFile} ({sb.Length} bytes), {requested.Length} types requested.");

static string Short(Type t)
{
    if (t == null) return "void";
    var n = t.Name;
    if (t.IsGenericType) n = n.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(Short)) + ">";
    return n;
}
static string AccF(FieldInfo f) => f.IsPublic ? "" : (f.IsPrivate ? "private " : "internal ");
static string AccM(MethodInfo m) => m.IsPublic ? "" : (m.IsPrivate ? "private " : "internal ");
