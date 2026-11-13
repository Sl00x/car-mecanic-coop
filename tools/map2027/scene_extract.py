#!/usr/bin/env python3
"""Extract a subtree of the CMS2027 level8 scene into a reusable form for the mod:
  meshes/<id>.cmsh   - unique mesh geometry (positions + triangles), one per source mesh (deduped)
  <out>/scene.manifest - one line per instance: meshId px py pz qx qy qz qw sx sy sz  (ROOT-relative TRS)
The mod loads the unique meshes once and instantiates each manifest line -> the subtree rebuilds exactly.
Transforms are relative to the chosen subtree root, so the whole thing can be dropped at the player.
LOD1/2/3 duplicates are skipped (keep LOD0 / non-LOD only).

Usage: python scene_extract.py <RootGOName> <out_dir> [maxMeshes]
"""
import UnityPy, pickle, os, sys, struct, math, re
from collections import defaultdict

LEVEL = r"C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2026 Demo\Car Mechanic Simulator 2027 Demo_Data\level8"
CACHE = "scene_graph.pkl"

def mat_mul(a,b): return [[sum(a[i][k]*b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]
def ident(): return [[1,0,0,0],[0,1,0,0],[0,0,1,0],[0,0,0,1]]
def trs(p,q,s):
    x,y,z,w=q
    r=[[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]]
    return [[r[0][0]*s[0],r[0][1]*s[1],r[0][2]*s[2],p[0]],
            [r[1][0]*s[0],r[1][1]*s[1],r[1][2]*s[2],p[1]],
            [r[2][0]*s[0],r[2][1]*s[1],r[2][2]*s[2],p[2]],[0,0,0,1]]
def decompose(m):
    tx,ty,tz=m[0][3],m[1][3],m[2][3]
    cx=(m[0][0],m[1][0],m[2][0]); cy=(m[0][1],m[1][1],m[2][1]); cz=(m[0][2],m[1][2],m[2][2])
    sx=math.sqrt(cx[0]**2+cx[1]**2+cx[2]**2) or 1; sy=math.sqrt(cy[0]**2+cy[1]**2+cy[2]**2) or 1; sz=math.sqrt(cz[0]**2+cz[1]**2+cz[2]**2) or 1
    # handedness
    det=(cx[0]*(cy[1]*cz[2]-cy[2]*cz[1])-cy[0]*(cx[1]*cz[2]-cx[2]*cz[1])+cz[0]*(cx[1]*cy[2]-cx[2]*cy[1]))
    if det<0: sx=-sx
    r=[[cx[0]/sx,cy[0]/sy,cz[0]/sz],[cx[1]/sx,cy[1]/sy,cz[1]/sz],[cx[2]/sx,cy[2]/sy,cz[2]/sz]]
    tr=r[0][0]+r[1][1]+r[2][2]
    if tr>0:
        S=math.sqrt(tr+1)*2; qw=0.25*S; qx=(r[2][1]-r[1][2])/S; qy=(r[0][2]-r[2][0])/S; qz=(r[1][0]-r[0][1])/S
    elif r[0][0]>r[1][1] and r[0][0]>r[2][2]:
        S=math.sqrt(1+r[0][0]-r[1][1]-r[2][2])*2; qw=(r[2][1]-r[1][2])/S; qx=0.25*S; qy=(r[0][1]+r[1][0])/S; qz=(r[0][2]+r[2][0])/S
    elif r[1][1]>r[2][2]:
        S=math.sqrt(1+r[1][1]-r[0][0]-r[2][2])*2; qw=(r[0][2]-r[2][0])/S; qx=(r[0][1]+r[1][0])/S; qy=0.25*S; qz=(r[1][2]+r[2][1])/S
    else:
        S=math.sqrt(1+r[2][2]-r[0][0]-r[1][1])*2; qw=(r[1][0]-r[0][1])/S; qx=(r[0][2]+r[2][0])/S; qy=(r[1][2]+r[2][1])/S; qz=0.25*S
    return (tx,ty,tz),(qx,qy,qz,qw),(sx,sy,sz)

def parse_obj(s):
    verts=[]; faces=[]
    for line in s.splitlines():
        if line.startswith('v '):
            _,x,y,z=line.split()[:4]; verts.append((float(x),float(y),float(z)))
        elif line.startswith('f '):
            idx=[int(p.split('/')[0])-1 for p in line.split()[1:]]
            for k in range(1,len(idx)-1): faces += [idx[0],idx[k],idx[k+1]]
    return verts, faces

def main():
    root_name=sys.argv[1]; out=sys.argv[2]; cap=int(sys.argv[3]) if len(sys.argv)>3 else 10**9
    os.makedirs(out,exist_ok=True); os.makedirs(os.path.join(out,"meshes"),exist_ok=True)
    g=pickle.load(open(CACHE,"rb")); tf=g['tf']; go_tf=g['go_tf']; go_name=g['go_name']; go_mesh=g['go_mesh']
    children=defaultdict(list)
    for tid,t in tf.items(): children[t['father']].append(tid)
    # choose root tf = the GO with this exact name whose subtree has most meshes
    cand=[go_tf[go] for go,nm in go_name.items() if nm==root_name and go in go_tf]
    if not cand: print("root not found:",root_name); return
    def cnt(r):
        c=0; st=[r]
        while st:
            x=st.pop();
            if tf[x]['go'] in go_mesh: c+=1
            st+=children.get(x,[])
        return c
    root=max(cand,key=cnt)
    print(f"root '{root_name}' tf={root} subtreeMeshes={cnt(root)}")
    # gather instances (tf, go) under root, skip LOD1/2/3
    insts=[]; stack=[root]
    while stack:
        x=stack.pop(); go=tf[x]['go']
        if go in go_mesh:
            nm=go_name.get(go,"")
            if not re.search(r'_LOD[123]\b', nm):   # keep LOD0 / non-LOD
                insts.append((x, go))
        stack+=children.get(x,[])
    print(f"instances (LOD0): {len(insts)}")
    # relative world matrix (root = identity)
    def rel(node):
        chain=[]; cur=node
        while cur!=root and cur in tf and tf[cur]['father'] in tf:
            chain.append(cur); cur=tf[cur]['father']
        if cur!=root:  # safety: node not under root via father chain
            pass
        m=ident()
        for tid in reversed(chain):
            t=tf[tid]; m=mat_mul(m,trs(t['pos'],t['rot'],t['scl']))
        return m
    # load UnityPy; resolve each GO's MeshFilter -> m_Mesh PPtr (handles cross-file sharedassets)
    print("loading level8 for geometry...")
    env=UnityPy.load(LEVEL)
    go2pptr={}
    for o in env.objects:
        if o.type.name=="MeshFilter":
            try:
                mf=o.read(); go2pptr[mf.m_GameObject.path_id]=mf.m_Mesh
            except Exception: pass
    written={}  # (path_id,name) -> mesh_id
    man=open(os.path.join(out,"scene.manifest"),"w")
    ninst=0
    for (node,go) in insts:
        if ninst>=cap: break
        pptr=go2pptr.get(go)
        if pptr is None: continue
        try:
            mesh=pptr.read(); key=(getattr(pptr,'path_id',0), getattr(mesh,'m_Name',''))
        except Exception: continue
        if key not in written:
            mid=len(written)
            try:
                verts,faces=parse_obj(mesh.export())
            except Exception:
                written[key]=None; continue
            if not verts or not faces: written[key]=None; continue
            with open(os.path.join(out,"meshes",f"{mid}.cmsh"),"wb") as f:
                f.write(b'CMSH'); f.write(struct.pack('<ii',2,len(verts)))
                for v in verts: f.write(struct.pack('<3f',*v))
                f.write(struct.pack('<i',len(faces)))
                f.write(struct.pack('<%di'%len(faces),*faces))
            written[key]=mid
        mid=written[key]
        if mid is None: continue
        (tx,ty,tz),(qx,qy,qz,qw),(sx,sy,sz)=decompose(rel(node))
        man.write(f"{mid} {tx:.4f} {ty:.4f} {tz:.4f} {qx:.5f} {qy:.5f} {qz:.5f} {qw:.5f} {sx:.4f} {sy:.4f} {sz:.4f}\n")
        ninst+=1
        if ninst%200==0: print(f"  ...{ninst} instances")
    man.close()
    uniq=len([v for v in written.values() if v is not None])
    print(f"DONE: {ninst} instances, {uniq} unique meshes -> {out}")

if __name__=="__main__": main()
