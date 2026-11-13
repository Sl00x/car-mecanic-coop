#!/usr/bin/env python3
"""Textured scene extractor: like scene_extract but also pulls each instance's MATERIAL (albedo texture +
color) and the mesh UVs, within a radius of the subtree origin. Outputs:
  meshes/<id>.cmsh     v3: pos + uv + triangles
  textures/<id>.png    unique albedo textures (downscaled to <=512)
  materials.txt        matId r g b a texIdx   (texIdx=-1 => flat color)
  scene.manifest       meshId matId px py pz qx qy qz qw sx sy sz   (root-relative TRS)
Usage: python scene_tex.py <RootGOName> <out> <radius>
"""
import UnityPy, pickle, os, sys, struct, math, re
from collections import defaultdict
try:
    from PIL import Image
except Exception:
    Image=None

LEVEL=r"C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2026 Demo\Car Mechanic Simulator 2027 Demo_Data\level8"
CACHE="scene_graph.pkl"; TEXCAP=512

def mat_mul(a,b): return [[sum(a[i][k]*b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]
def ident(): return [[1,0,0,0],[0,1,0,0],[0,0,1,0],[0,0,0,1]]
def trs(p,q,s):
    x,y,z,w=q
    r=[[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]]
    return [[r[0][0]*s[0],r[0][1]*s[1],r[0][2]*s[2],p[0]],[r[1][0]*s[0],r[1][1]*s[1],r[1][2]*s[2],p[1]],[r[2][0]*s[0],r[2][1]*s[1],r[2][2]*s[2],p[2]],[0,0,0,1]]
def decompose(m):
    tx,ty,tz=m[0][3],m[1][3],m[2][3]
    cx=(m[0][0],m[1][0],m[2][0]);cy=(m[0][1],m[1][1],m[2][1]);cz=(m[0][2],m[1][2],m[2][2])
    sx=math.sqrt(sum(c*c for c in cx))or 1;sy=math.sqrt(sum(c*c for c in cy))or 1;sz=math.sqrt(sum(c*c for c in cz))or 1
    det=cx[0]*(cy[1]*cz[2]-cy[2]*cz[1])-cy[0]*(cx[1]*cz[2]-cx[2]*cz[1])+cz[0]*(cx[1]*cy[2]-cx[2]*cy[1])
    if det<0: sx=-sx
    r=[[cx[0]/sx,cy[0]/sy,cz[0]/sz],[cx[1]/sx,cy[1]/sy,cz[1]/sz],[cx[2]/sx,cy[2]/sy,cz[2]/sz]]
    tr=r[0][0]+r[1][1]+r[2][2]
    if tr>0: S=math.sqrt(tr+1)*2;qw=.25*S;qx=(r[2][1]-r[1][2])/S;qy=(r[0][2]-r[2][0])/S;qz=(r[1][0]-r[0][1])/S
    elif r[0][0]>r[1][1] and r[0][0]>r[2][2]: S=math.sqrt(1+r[0][0]-r[1][1]-r[2][2])*2;qw=(r[2][1]-r[1][2])/S;qx=.25*S;qy=(r[0][1]+r[1][0])/S;qz=(r[0][2]+r[2][0])/S
    elif r[1][1]>r[2][2]: S=math.sqrt(1+r[1][1]-r[0][0]-r[2][2])*2;qw=(r[0][2]-r[2][0])/S;qx=(r[0][1]+r[1][0])/S;qy=.25*S;qz=(r[1][2]+r[2][1])/S
    else: S=math.sqrt(1+r[2][2]-r[0][0]-r[1][1])*2;qw=(r[1][0]-r[0][1])/S;qx=(r[0][2]+r[2][0])/S;qy=(r[1][2]+r[2][1])/S;qz=.25*S
    return (tx,ty,tz),(qx,qy,qz,qw),(sx,sy,sz)

def parse_obj_uv(s):
    V=[];VT=[];vmap={};verts=[];uvs=[];faces=[]
    for line in s.splitlines():
        if line.startswith('v '):
            a=line.split();V.append((float(a[1]),float(a[2]),float(a[3])))
        elif line.startswith('vt '):
            a=line.split();VT.append((float(a[1]),float(a[2])))
        elif line.startswith('f '):
            idx=[]
            for t in line.split()[1:]:
                a=t.split('/');vi=int(a[0])-1;ti=int(a[1])-1 if len(a)>1 and a[1] else -1
                key=(vi,ti)
                if key not in vmap:
                    vmap[key]=len(verts);verts.append(V[vi]);uvs.append(VT[ti] if 0<=ti<len(VT) else (0.0,0.0))
                idx.append(vmap[key])
            for k in range(1,len(idx)-1): faces+=[idx[0],idx[k],idx[k+1]]
    return verts,uvs,faces

def main():
    root_name=sys.argv[1]; out=sys.argv[2]; R=float(sys.argv[3]) if len(sys.argv)>3 else 1e9
    for sub in ("meshes","textures"): os.makedirs(os.path.join(out,sub),exist_ok=True)
    g=pickle.load(open(CACHE,"rb")); tf=g['tf'];go_tf=g['go_tf'];go_name=g['go_name'];go_mesh=g['go_mesh']
    children=defaultdict(list)
    for tid,t in tf.items(): children[t['father']].append(tid)
    cand=[go_tf[go] for go,nm in go_name.items() if nm==root_name and go in go_tf]
    def cnt(r):
        c=0;st=[r]
        while st:
            x=st.pop()
            if tf[x]['go'] in go_mesh:c+=1
            st+=children.get(x,[])
        return c
    root=max(cand,key=cnt)
    insts=[];stack=[root]
    while stack:
        x=stack.pop();go=tf[x]['go']
        if go in go_mesh and not re.search(r'_LOD[123]\b',go_name.get(go,"")): insts.append((x,go))
        stack+=children.get(x,[])
    def rel(node):
        chain=[];cur=node
        while cur!=root and cur in tf:
            chain.append(cur);cur=tf[cur]['father']
        m=ident()
        for tid in reversed(chain): t=tf[tid];m=mat_mul(m,trs(t['pos'],t['rot'],t['scl']))
        return m
    print(f"root '{root_name}' instances(LOD0)={len(insts)}  loading level8...")
    env=UnityPy.load(LEVEL)
    go2mesh={};go2mat={}
    for o in env.objects:
        n=o.type.name
        if n=="MeshFilter":
            try:d=o.read();go2mesh[d.m_GameObject.path_id]=d.m_Mesh
            except:pass
        elif n=="MeshRenderer":
            try:
                d=o.read();ms=d.m_Materials
                if ms:go2mat[d.m_GameObject.path_id]=ms[0]
            except:pass
    wr_mesh={};wr_mat={};wr_tex={};wr_texA={}
    man=open(os.path.join(out,"scene.manifest"),"w");mats=open(os.path.join(out,"materials.txt"),"w")
    ninst=0
    for (node,go) in insts:
        M=rel(node)
        tx,tz=M[0][3],M[2][3]
        if math.hypot(tx,tz)>R: continue
        mp=go2mesh.get(go)
        if mp is None: continue
        try: mesh=mp.read();mkey=(getattr(mp,'path_id',0),getattr(mesh,'m_Name',''))
        except: continue
        if mkey not in wr_mesh:
            mid=len(wr_mesh)
            try: v,uv,fa=parse_obj_uv(mesh.export())
            except: wr_mesh[mkey]=None;continue
            if not v or not fa: wr_mesh[mkey]=None;continue
            with open(os.path.join(out,"meshes",f"{mid}.cmsh"),"wb") as f:
                f.write(b'CMSH');f.write(struct.pack('<ii',3,len(v)))
                for p in v:f.write(struct.pack('<3f',*p))
                for u in uv:f.write(struct.pack('<2f',*u))
                f.write(struct.pack('<i',len(fa)));f.write(struct.pack('<%di'%len(fa),*fa))
            wr_mesh[mkey]=mid
        mid=wr_mesh[mkey]
        if mid is None: continue
        # material
        matId=-1;col=(1,1,1,1)
        mpptr=go2mat.get(go)
        if mpptr is not None:
            mkey2=getattr(mpptr,'path_id',0)
            if mkey2 not in wr_mat:
                nm=len(wr_mat);texidx=-1;color=(1,1,1,1);scale=(1.0,1.0);offset=(0.0,0.0);transp=0;alpha=0
                try:
                    mat=mpptr.read();sp=mat.m_SavedProperties
                    for name,c in sp.m_Colors:
                        if name in("_BaseColor","_Color"): color=(round(c.r,4),round(c.g,4),round(c.b,4),round(c.a,4));break
                    # pick the albedo texenv: prefer known slots, else the first non-data (not normal/mask/etc) texture
                    def valid(e2): return getattr(getattr(e2,'m_Texture',None),'path_id',0)!=0
                    EXCL=("bump","normal","packed","mask","metal","spec","occl","emiss","detail","height","_ao","lightmap","shadow")
                    chosen=None
                    tenv=[(name,e2) for name,e2 in sp.m_TexEnvs]
                    td={name:e2 for name,e2 in tenv}
                    for slot in("_MainTex","_BaseColorMap","_BaseMap","_MainTexture","_Albedo","_Diffuse","_Texture","_Color"):
                        if slot in td and valid(td[slot]): chosen=td[slot];break
                    if chosen is None:
                        for name,e2 in tenv:
                            if valid(e2) and not any(x in name.lower() for x in EXCL): chosen=e2;break
                    if chosen is not None:
                        sc=chosen.m_Scale; of=chosen.m_Offset
                        scale=(round(sc.x,4),round(sc.y,4)); offset=(round(of.x,4),round(of.y,4))
                        tpptr=chosen.m_Texture; tkey=getattr(tpptr,'path_id',0)
                        if tkey not in wr_tex:
                            ti=len(wr_tex); au=0
                            try:
                                tex=tpptr.read();img=tex.image
                                if img is not None:
                                    img=img.convert("RGBA")
                                    if Image and max(img.size)>TEXCAP:
                                        s=TEXCAP/max(img.size);img=img.resize((max(1,int(img.size[0]*s)),max(1,int(img.size[1]*s))))
                                    lo=img.getchannel('A').getextrema()[0]
                                    au=1 if lo<235 else 0
                                    img.save(os.path.join(out,"textures",f"{ti}.png"))
                                    wr_tex[tkey]=ti; wr_texA[ti]=au
                                else: wr_tex[tkey]=-1
                            except: wr_tex[tkey]=-1
                        texidx=wr_tex[tkey]
                        if texidx>=0: alpha=wr_texA.get(texidx,0)
                    fl={name:v for name,v in sp.m_Floats}
                    if fl.get('_SurfaceType',0)>=0.5 or fl.get('_Mode',0)>=2 or fl.get('_Blend',0)>=1 or getattr(mat,'m_CustomRenderQueue',-1)>=3000:
                        transp=1
                except: pass
                mats.write(f"{nm} {color[0]} {color[1]} {color[2]} {color[3]} {texidx} {scale[0]} {scale[1]} {offset[0]} {offset[1]} {transp} {alpha}\n")
                wr_mat[mkey2]=nm
            matId=wr_mat[mkey2]
        row=" ".join(f"{M[r][c]:.5f}" for r in range(3) for c in range(4))   # full 4x4 affine (3 rows)
        man.write(f"{mid} {matId} {row}\n")
        ninst+=1
        if ninst%400==0: print(f"  ...{ninst}")
    man.close();mats.close()
    print(f"DONE: {ninst} instances, {len([v for v in wr_mesh.values() if v is not None])} meshes, {len(wr_mat)} materials, {len([v for v in wr_tex.values() if v>=0])} textures -> {out}")

if __name__=="__main__": main()
