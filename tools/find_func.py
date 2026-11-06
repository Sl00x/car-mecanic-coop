import sys, pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_OP_IMM
from capstone.x86 import X86_OP_IMM

PATH = r"C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021\GameAssembly.dll"
pe = pefile.PE(PATH, fast_load=True)
pe.parse_data_directories(directories=[
    pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXPORT']])
image_base = pe.OPTIONAL_HEADER.ImageBase
print("image_base = 0x%X" % image_base)

# Map RVA -> (bytes, section va range)
text = None
for s in pe.sections:
    name = s.Name.rstrip(b"\x00").decode(errors="replace")
    if name == ".text":
        text = s
sec_rva = text.VirtualAddress
sec_data = text.get_data()
sec_size = len(sec_data)
print(".text rva=0x%X size=0x%X" % (sec_rva, sec_size))

def read(rva, n):
    off = rva - sec_rva
    if off < 0 or off+n > sec_size: return b""
    return sec_data[off:off+n]

# exports
exports = {}
for exp in pe.DIRECTORY_ENTRY_EXPORT.symbols:
    if exp.name:
        exports[exp.name.decode()] = exp.address  # RVA
def exp_rva(name):
    return exports.get(name)

md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = True

def in_text(rva):
    return sec_rva <= rva < sec_rva + sec_size

def jump_targets(start_rva, max_ins=400):
    """Approximate Il2CppInterop XrefScannerLowLevel.JumpTargets: linear decode,
       collect near call/jmp immediate targets in order; stop at ret/int3/invalid
       or after an unconditional jmp."""
    out = []
    data = read(start_rva, 4096)
    if not data: return out
    va = image_base + start_rva
    count = 0
    for ins in md.disasm(data, va):
        count += 1
        if count > max_ins: break
        m = ins.mnemonic
        if m == "ret" or m.startswith("ret") or m == "int3":
            break
        if m == "call" or m == "jmp":
            # immediate near target?
            if ins.operands and ins.operands[0].type == X86_OP_IMM:
                tgt = ins.operands[0].imm
                rva = tgt - image_base
                if in_text(rva):
                    out.append((m, rva, ins.address - image_base))
            if m == "jmp":
                break  # unconditional branch ends linear flow
    return out

def show(rva, label, n=16):
    print("\n--- %s @ rva 0x%X (va 0x%X) ---" % (label, rva, image_base+rva))
    data = read(rva, 80)
    c = 0
    for ins in md.disasm(data, image_base+rva):
        print("  0x%X: %s %s" % (ins.address, ins.mnemonic, ins.op_str))
        c += 1
        if c >= n: break

igc = exp_rva("il2cpp_image_get_class")
print("il2cpp_image_get_class RVA =", hex(igc) if igc else None)
if not igc:
    # list a few il2cpp_image exports
    for k in sorted(exports):
        if "image" in k.lower(): print("  export:", k, hex(exports[k]))
    sys.exit(0)

show(igc, "il2cpp_image_get_class")
t1 = jump_targets(igc)
print("\nL1 jump targets from il2cpp_image_get_class:", [(m, hex(r)) for m,r,_ in t1])
if t1:
    image_get_type = t1[0][1]
    show(image_get_type, "Image::GetType (L1.first)")
    t2 = jump_targets(image_get_type)
    print("\nL2 jump targets from Image::GetType:", [(m, hex(r)) for m,r,_ in t2])
    if t2:
        cand = t2[0][1]
        show(cand, "GetTypeInfoFromTypeDefinitionIndex? (L2.first)")
        print("\n==> CANDIDATE GetTypeInfoFromTypeDefinitionIndex RVA = 0x%X (va 0x%X)" % (cand, image_base+cand))
