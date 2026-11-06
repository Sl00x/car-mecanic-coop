using System.Diagnostics;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Surgically patch Il2CppInterop.Runtime.dll so the metadata-function finder returns a known
// address for this game's GameAssembly.dll, where the xref scan fails (IL2CPP Master build).
//
// Usage: PatchInterop <Il2CppInterop.Runtime.dll> <RVAhex, e.g. 2AE3A0>
// Writes <dll>.bak (once) and overwrites the dll in place.

string dll = args.Length > 0 ? args[0]
    : @"C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021\BepInEx\core\Il2CppInterop.Runtime.dll";
long rva = Convert.ToInt64(args.Length > 1 ? args[1] : "2AE3A0", 16);

const string HookType = "Il2CppInterop.Runtime.Injection.Hooks.MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook";

var bak = dll + ".bak";
if (!File.Exists(bak)) { File.Copy(dll, bak); Console.WriteLine("Backup -> " + bak); }

// Read from the backup each run so the patch is idempotent.
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(dll)));
var asm = AssemblyDefinition.ReadAssembly(bak, new ReaderParameters { AssemblyResolver = resolver });
var module = asm.MainModule;

var type = module.GetType(HookType) ?? throw new Exception("type not found: " + HookType);
var method = type.Methods.FirstOrDefault(m => m.Name == "FindTargetMethod")
    ?? throw new Exception("FindTargetMethod not found");

var injectorHelpers = module.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers")
    ?? throw new Exception("InjectorHelpers not found");
var il2cppModuleField = injectorHelpers.Fields.FirstOrDefault(f => f.Name == "Il2CppModule")
    ?? throw new Exception("Il2CppModule field not found");

// References from the host runtime (net8 has these; shapes match net6 target).
var getBaseAddress = module.ImportReference(typeof(ProcessModule).GetProperty("BaseAddress")!.GetGetMethod());
var opAddition = module.ImportReference(typeof(IntPtr).GetMethod("op_Addition", new[] { typeof(IntPtr), typeof(int) }));

method.Body = new MethodBody(method);
var il = method.Body.GetILProcessor();
il.Append(il.Create(OpCodes.Ldsfld, il2cppModuleField));         // InjectorHelpers.Il2CppModule
il.Append(il.Create(OpCodes.Callvirt, getBaseAddress));          // .BaseAddress  (IntPtr)
il.Append(il.Create(OpCodes.Ldc_I4, (int)rva));                  // rva
il.Append(il.Create(OpCodes.Call, opAddition));                  // IntPtr + int -> IntPtr
il.Append(il.Create(OpCodes.Ret));

asm.Write(dll);
Console.WriteLine($"Patched {HookType}.FindTargetMethod -> Il2CppModule.BaseAddress + 0x{rva:X}");
Console.WriteLine("Wrote " + dll);
