#!/usr/bin/env python3
"""Extract geometry from an AssetRipper Unity Mesh .asset (YAML, serializedVersion 12, packed
m_VertexData/_typelessdata + m_IndexBuffer) into a minimal .cmsh binary we can rebuild at runtime
in CMS2021 (Unity 2020.3) WITHOUT a Unity bundle build. No external deps.

.cmsh format (little-endian):
  'CMSH' | int version=1 | int vtxCount
  float[3*vtx] positions | float[3*vtx] normals
  int idxCount | int[idxCount] indices (triangles)
"""
import re, struct, sys, os

def find_scalar(text, key):
    m = re.search(r'\b' + re.escape(key) + r':\s*([0-9]+)', text)
    return int(m.group(1)) if m else None

def find_hex(text, key):
    # key: <hex...>  (single long line)
    m = re.search(re.escape(key) + r':\s*([0-9a-fA-F]+)', text)
    return bytes.fromhex(m.group(1)) if m else b''

def extract(path):
    txt = open(path, 'r', encoding='utf-8', errors='replace').read()
    idx_format = find_scalar(txt, 'm_IndexFormat') or 0
    data_size  = find_scalar(txt, 'm_DataSize')
    # m_VertexCount inside m_VertexData (the vertex-buffer count)
    vdm = re.search(r'm_VertexData:(.*?)_typelessdata', txt, re.S)
    vblock = vdm.group(1) if vdm else txt
    vtx = find_scalar(vblock, 'm_VertexCount')
    raw = find_hex(txt, '_typelessdata')
    idxbuf = find_hex(txt, 'm_IndexBuffer')
    if not vtx or not raw or data_size is None:
        raise RuntimeError(f"missing fields: vtx={vtx} raw={len(raw)} dsize={data_size}")
    stride = data_size // vtx
    if stride * vtx != len(raw):
        # trust data_size; some exports pad
        stride = data_size // vtx
    pos = []; nrm = []
    for i in range(vtx):
        o = i * stride
        px, py, pz = struct.unpack_from('<3f', raw, o + 0)
        nx, ny, nz = struct.unpack_from('<3f', raw, o + 12)   # normal channel @ offset 12
        pos += [px, py, pz]; nrm += [nx, ny, nz]
    if idx_format == 0:
        n = len(idxbuf) // 2
        idx = list(struct.unpack_from('<%dH' % n, idxbuf, 0))
    else:
        n = len(idxbuf) // 4
        idx = list(struct.unpack_from('<%dI' % n, idxbuf, 0))
    # drop any degenerate trailing indices beyond a multiple of 3
    idx = idx[:(len(idx) // 3) * 3]
    return vtx, pos, nrm, idx

def write_cmsh(out, vtx, pos, nrm, idx):
    with open(out, 'wb') as f:
        f.write(b'CMSH'); f.write(struct.pack('<i', 1)); f.write(struct.pack('<i', vtx))
        f.write(struct.pack('<%df' % len(pos), *pos))
        f.write(struct.pack('<%df' % len(nrm), *nrm))
        f.write(struct.pack('<i', len(idx)))
        f.write(struct.pack('<%di' % len(idx), *idx))

if __name__ == '__main__':
    outdir = sys.argv[1]
    os.makedirs(outdir, exist_ok=True)
    for src in sys.argv[2:]:
        name = os.path.splitext(os.path.basename(src))[0]
        vtx, pos, nrm, idx = extract(src)
        out = os.path.join(outdir, name + '.cmsh')
        write_cmsh(out, vtx, pos, nrm, idx)
        print(f"  {name}: {vtx} verts, {len(idx)//3} tris -> {out} ({os.path.getsize(out)} B)")
