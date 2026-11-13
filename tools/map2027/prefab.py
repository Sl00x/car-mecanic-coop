#!/usr/bin/env python3
"""Reconstruct a CMS2027 prefab into pre-positioned .cmsh meshes (no Unity needed).
Parses the AssetRipper prefab YAML: builds the Transform tree, composes each MeshFilter's
world-relative TRS matrix, BAKES the referenced mesh's verts/normals by that matrix, and writes
one .cmsh per mesh instance (already positioned relative to the prefab root). The mod then loads
them all under one root and the structure assembles correctly.

Usage: python prefab.py <prefab> <mesh_dir> <out_dir>
"""
import re, struct, sys, os, glob, math

# ---------- mesh geometry (same packed-vertex decode as extract.py) ----------
def load_mesh(asset_path):
    txt = open(asset_path, encoding='utf-8', errors='replace').read()
    def scalar(k, blk=txt):
        m = re.search(r'\b'+re.escape(k)+r':\s*([0-9]+)', blk); return int(m.group(1)) if m else None
    def hexof(k):
        m = re.search(re.escape(k)+r':\s*([0-9a-fA-F]+)', txt); return bytes.fromhex(m.group(1)) if m else b''
    idxfmt = scalar('m_IndexFormat') or 0
    dsize = scalar('m_DataSize')
    vdm = re.search(r'm_VertexData:(.*?)_typelessdata', txt, re.S)
    vtx = scalar('m_VertexCount', vdm.group(1) if vdm else txt)
    raw = hexof('_typelessdata'); idxbuf = hexof('m_IndexBuffer')
    if not vtx or not raw or not dsize: raise RuntimeError("bad mesh "+asset_path)
    stride = dsize // vtx
    pos=[]; nrm=[]
    for i in range(vtx):
        o=i*stride
        pos.append(struct.unpack_from('<3f', raw, o)); nrm.append(struct.unpack_from('<3f', raw, o+12))
    n = len(idxbuf)//(2 if idxfmt==0 else 4)
    idx = list(struct.unpack_from('<%d%s'%(n,'H' if idxfmt==0 else 'I'), idxbuf, 0))
    idx = idx[:(len(idx)//3)*3]
    return pos, nrm, idx

# ---------- tiny 4x4 matrix math ----------
def mat_mul(a,b):
    return [[sum(a[i][k]*b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]
def trs(p,q,s):
    x,y,z,w=q
    # rotation 3x3 from quaternion
    r=[[1-2*(y*y+z*z), 2*(x*y-z*w),   2*(x*z+y*w)],
       [2*(x*y+z*w),   1-2*(x*x+z*z), 2*(y*z-x*w)],
       [2*(x*z-y*w),   2*(y*z+x*w),   1-2*(x*x+y*y)]]
    m=[[r[0][0]*s[0], r[0][1]*s[1], r[0][2]*s[2], p[0]],
       [r[1][0]*s[0], r[1][1]*s[1], r[1][2]*s[2], p[1]],
       [r[2][0]*s[0], r[2][1]*s[1], r[2][2]*s[2], p[2]],
       [0,0,0,1]]
    return m
def apply_pt(m,v):
    return (m[0][0]*v[0]+m[0][1]*v[1]+m[0][2]*v[2]+m[0][3],
            m[1][0]*v[0]+m[1][1]*v[1]+m[1][2]*v[2]+m[1][3],
            m[2][0]*v[0]+m[2][1]*v[1]+m[2][2]*v[2]+m[2][3])
def apply_dir(m,v):  # rotation+scale only (normals); normalize after
    x=m[0][0]*v[0]+m[0][1]*v[1]+m[0][2]*v[2]
    y=m[1][0]*v[0]+m[1][1]*v[1]+m[1][2]*v[2]
    z=m[2][0]*v[0]+m[2][1]*v[1]+m[2][2]*v[2]
    L=math.sqrt(x*x+y*y+z*z) or 1.0
    return (x/L,y/L,z/L)

# ---------- prefab parse ----------
def guid_index(mesh_dir):
    g={}
    for meta in glob.glob(os.path.join(mesh_dir,"*.asset.meta")):
        t=open(meta).read(); m=re.search(r'guid:\s*([0-9a-f]{32})',t)
        if m: g[m.group(1)]=meta[:-len(".meta")]   # path to .asset
    return g

def parse_docs(path):
    txt=open(path,encoding='utf-8',errors='replace').read()
    parts=re.split(r'^--- !u!(\d+) &(\d+).*$',txt,flags=re.M)
    objs={}
    for i in range(1,len(parts),3):
        objs[int(parts[i+1])]=(int(parts[i]),parts[i+2])
    return objs

def vec(body,name,keys):
    m=re.search(re.escape(name)+r':\s*\{([^}]*)\}',body)
    if not m: return None
    d=dict(re.findall(r'([xyzw]):\s*([-0-9.eE]+)',m.group(1)))
    return tuple(float(d.get(k,0)) for k in keys)
def fid(body,name):
    m=re.search(re.escape(name)+r':\s*\{fileID:\s*(-?\d+)',body); return int(m.group(1)) if m else 0
def guid_of(body,name):
    m=re.search(re.escape(name)+r':\s*\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-f]{32})',body); return m.group(1) if m else None

def main():
    prefab, mesh_dir, out = sys.argv[1], sys.argv[2], sys.argv[3]
    os.makedirs(out, exist_ok=True)
    g2asset = guid_index(mesh_dir)
    objs = parse_docs(prefab)
    CLS_TRANSFORM, CLS_MESHFILTER, CLS_GO = 4, 33, 1
    transforms={}; go2tf={}; gonames={}
    for fileid,(cls,body) in objs.items():
        if cls==CLS_TRANSFORM:
            go=fid(body,'m_GameObject')
            transforms[fileid]=dict(
                go=go,
                pos=vec(body,'m_LocalPosition',('x','y','z')) or (0,0,0),
                rot=vec(body,'m_LocalRotation',('x','y','z','w')) or (0,0,0,1),
                scl=vec(body,'m_LocalScale',('x','y','z')) or (1,1,1),
                father=fid(body,'m_Father'))
            go2tf[go]=fileid
        elif cls==CLS_GO:
            m=re.search(r'm_Name:\s*(.+)',body); gonames[fileid]=m.group(1).strip() if m else str(fileid)
    def world(tf_id):
        m=[[1,0,0,0],[0,1,0,0],[0,0,1,0],[0,0,0,1]]
        chain=[]
        cur=tf_id
        while cur and cur in transforms:
            chain.append(cur); cur=transforms[cur]['father']
        for tfid in reversed(chain):
            t=transforms[tfid]; m=mat_mul(m, trs(t['pos'],t['rot'],t['scl']))
        return m
    count=0
    for fileid,(cls,body) in objs.items():
        if cls!=CLS_MESHFILTER: continue
        go=fid(body,'m_GameObject'); guid=guid_of(body,'m_Mesh')
        if not guid or guid not in g2asset: continue
        tf=go2tf.get(go)
        if tf is None: continue
        M=world(tf)
        try: pos,nrm,idx = load_mesh(g2asset[guid])
        except Exception as e: print("  skip (mesh load):", e); continue
        bp=[apply_pt(M,v) for v in pos]; bn=[apply_dir(M,v) for v in nrm]
        name=gonames.get(go,str(go)); name=re.sub(r'[^A-Za-z0-9_]+','_',name)[:48]
        fn=os.path.join(out, f"{count:02d}_{name}.cmsh")
        with open(fn,'wb') as f:
            f.write(b'CMSH'); f.write(struct.pack('<ii',1,len(bp)))
            for v in bp: f.write(struct.pack('<3f',*v))
            for v in bn: f.write(struct.pack('<3f',*v))
            f.write(struct.pack('<i',len(idx)))
            f.write(struct.pack('<%di'%len(idx),*idx))
        count+=1
        print(f"  {name}: {len(bp)} verts, {len(idx)//3} tris (baked)")
    print(f"TOTAL {count} baked meshes -> {out}")

if __name__ == '__main__':
    main()
