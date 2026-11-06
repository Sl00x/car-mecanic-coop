using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CarMecaSim.Launcher
{
    /// <summary>
    /// Patches BepInEx's Il2CppInterop.Runtime.dll so class injection works on CMS2021.
    /// This game is an IL2CPP "Master" build; Il2CppInterop's xref scan can't locate
    /// MetadataCache::GetTypeInfoFromTypeDefinitionIndex, so the chainloader aborts
    /// (upstream BepInEx #820). We rewrite the finder to return a verified address.
    /// </summary>
    public static class InteropPatcher
    {
        // RVA of GetTypeInfoFromTypeDefinitionIndex in this game's GameAssembly.dll.
        public const long DefaultRva = 0x2AE3A0;
        private const string HookType =
            "Il2CppInterop.Runtime.Injection.Hooks.MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook";

        // Function prologue we expect at that RVA (mov [rsp+8],rbx; mov [rsp+18],rbp; mov [rsp+20],rsi; push rdi; sub rsp,20)
        private static readonly byte[] ExpectedPrologue =
            { 0x48,0x89,0x5C,0x24,0x08, 0x48,0x89,0x6C,0x24,0x18, 0x48,0x89,0x74,0x24,0x20, 0x57 };

        public static bool EnsurePatched(string gameDir, Action<string>? log = null)
        {
            var interopDll = Path.Combine(gameDir, "BepInEx", "core", "Il2CppInterop.Runtime.dll");
            var gameAsm = Path.Combine(gameDir, "GameAssembly.dll");
            if (!File.Exists(interopDll)) { log?.Invoke("Il2CppInterop.Runtime.dll not found (install BepInEx first)."); return false; }
            if (!File.Exists(gameAsm)) { log?.Invoke("GameAssembly.dll not found."); return false; }

            long rva = DefaultRva;
            if (!VerifyPrologue(gameAsm, rva, log))
            {
                log?.Invoke($"WARNING: expected function prologue not found at RVA 0x{rva:X}. " +
                            "The game may have updated — re-run tools/find_func.py to get the new RVA. Skipping patch.");
                return false;
            }

            if (ApplyCecilPatch(interopDll, rva, log))
            {
                log?.Invoke($"Il2CppInterop patched (GetTypeInfoFromTypeDefinitionIndex @ base+0x{rva:X}).");
                return true;
            }
            return false;
        }

        /// <summary>Minimal PE walk: map the RVA to a file offset and check the prologue bytes.</summary>
        private static bool VerifyPrologue(string pePath, long rva, Action<string>? log)
        {
            try
            {
                var bytes = File.ReadAllBytes(pePath);
                int elfanew = BitConverter.ToInt32(bytes, 0x3C);
                if (BitConverter.ToUInt32(bytes, elfanew) != 0x00004550) { log?.Invoke("not a PE"); return false; }
                int coff = elfanew + 4;
                ushort numSections = BitConverter.ToUInt16(bytes, coff + 2);
                ushort optSize = BitConverter.ToUInt16(bytes, coff + 16);
                int sectionTable = coff + 20 + optSize;

                long fileOffset = -1;
                for (int i = 0; i < numSections; i++)
                {
                    int s = sectionTable + i * 40;
                    uint vsize = BitConverter.ToUInt32(bytes, s + 8);
                    uint vaddr = BitConverter.ToUInt32(bytes, s + 12);
                    uint praw  = BitConverter.ToUInt32(bytes, s + 20);
                    if (rva >= vaddr && rva < vaddr + vsize) { fileOffset = praw + (rva - vaddr); break; }
                }
                if (fileOffset < 0 || fileOffset + ExpectedPrologue.Length > bytes.Length) return false;

                for (int i = 0; i < ExpectedPrologue.Length; i++)
                    if (bytes[fileOffset + i] != ExpectedPrologue[i]) return false;
                return true;
            }
            catch (Exception e) { log?.Invoke("prologue verify error: " + e.Message); return false; }
        }

        private static bool ApplyCecilPatch(string dll, long rva, Action<string>? log)
        {
            try
            {
                var bak = dll + ".bak";
                if (!File.Exists(bak)) File.Copy(dll, bak);

                var resolver = new DefaultAssemblyResolver();
                resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(dll)));
                // Read from the backup each time so repeated setups stay idempotent.
                using var asm = AssemblyDefinition.ReadAssembly(bak, new ReaderParameters { AssemblyResolver = resolver });
                var module = asm.MainModule;

                var type = module.GetType(HookType);
                var method = type?.Methods.FirstOrDefault(m => m.Name == "FindTargetMethod");
                var helpers = module.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers");
                var field = helpers?.Fields.FirstOrDefault(f => f.Name == "Il2CppModule");
                if (method == null || field == null) { log?.Invoke("patch targets not found in Il2CppInterop.Runtime.dll"); return false; }

                var getBaseAddress = module.ImportReference(typeof(System.Diagnostics.ProcessModule).GetProperty("BaseAddress")!.GetGetMethod());
                var opAddition = module.ImportReference(typeof(IntPtr).GetMethod("op_Addition", new[] { typeof(IntPtr), typeof(int) }));

                method.Body = new MethodBody(method);
                var il = method.Body.GetILProcessor();
                il.Append(il.Create(OpCodes.Ldsfld, field));
                il.Append(il.Create(OpCodes.Callvirt, getBaseAddress));
                il.Append(il.Create(OpCodes.Ldc_I4, (int)rva));
                il.Append(il.Create(OpCodes.Call, opAddition));
                il.Append(il.Create(OpCodes.Ret));

                asm.Write(dll);
                return true;
            }
            catch (Exception e) { log?.Invoke("Cecil patch error: " + e.Message); return false; }
        }
    }
}
