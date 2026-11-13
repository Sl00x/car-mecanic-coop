#!/usr/bin/env python3
"""Load the CMS2027 open-world scene (level8) with UnityPy, build the Transform graph, and report the
root objects by how many MeshRenderers live under them — so we can locate the GARAGE subtree. Caches the
graph to a pickle for fast reuse by the extractor."""
import UnityPy, pickle, os, sys
from collections import defaultdict

LEVEL = r"C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2026 Demo\Car Mechanic Simulator 2027 Demo_Data\level8"
CACHE = "scene_graph.pkl"

def build():
    print("loading level8 (big, be patient)...")
    env = UnityPy.load(LEVEL)
    objs = env.objects
    tf = {}          # tf_pathid -> dict(go, pos, rot, scl, father)
    go_tf = {}       # go_pathid -> tf_pathid
    go_name = {}     # go_pathid -> name
    go_mesh = {}     # go_pathid -> mesh_pathid (has a MeshFilter)
    n=0
    for o in objs:
        tn=o.type.name
        if tn=="Transform" or tn=="RectTransform":
            d=o.read()
            try:
                go=d.m_GameObject.path_id
                p=d.m_LocalPosition; q=d.m_LocalRotation; s=d.m_LocalScale
                tf[o.path_id]=dict(go=go,
                    pos=(p.x,p.y,p.z), rot=(q.x,q.y,q.z,q.w), scl=(s.x,s.y,s.z),
                    father=d.m_Father.path_id)
                go_tf[go]=o.path_id
            except Exception: pass
        elif tn=="GameObject":
            try:
                d=o.read(); go_name[o.path_id]=d.m_Name
            except Exception: pass
        elif tn=="MeshFilter":
            try:
                d=o.read(); go_mesh[d.m_GameObject.path_id]=d.m_Mesh.path_id
            except Exception: pass
        n+=1
        if n%40000==0: print(f"  ...{n}/{len(objs)}")
    data=dict(tf=tf, go_tf=go_tf, go_name=go_name, go_mesh=go_mesh)
    pickle.dump(data, open(CACHE,"wb"))
    print(f"cached: {len(tf)} transforms, {len(go_mesh)} meshfilters, {len(go_name)} gameobjects")
    return data

def analyze(data):
    tf=data['tf']; go_tf=data['go_tf']; go_name=data['go_name']; go_mesh=data['go_mesh']
    # children map (tf -> [child tf])
    children=defaultdict(list)
    for tid,t in tf.items():
        children[t['father']].append(tid)
    # subtree mesh count (iterative), and name of the subtree's root GO
    def subtree_mesh_count(root_tf):
        cnt=0; stack=[root_tf]
        while stack:
            cur=stack.pop()
            go=tf[cur]['go'] if cur in tf else None
            if go in go_mesh: cnt+=1
            stack.extend(children.get(cur,[]))
        return cnt
    roots=[tid for tid,t in tf.items() if t['father']==0 or t['father'] not in tf]
    scored=[]
    for r in roots:
        go=tf[r]['go']; nm=go_name.get(go,"?")
        scored.append((subtree_mesh_count(r), nm, r))
    scored.sort(reverse=True)
    print(f"\n{len(roots)} root objects. Top roots by mesh count under them:")
    for cnt,nm,r in scored[:30]:
        if cnt>0: print(f"  {cnt:6d} meshes   root='{nm}'  tf={r}")
    # also search any GO name containing garage/hala and its subtree mesh count
    print("\nGO names containing 'garage'/'hala'/'warsztat' (CMS garage words):")
    hits=[(go,nm) for go,nm in go_name.items() if any(w in nm.lower() for w in ("garage","hala","warsztat","interior"))]
    for go,nm in hits[:40]:
        tid=go_tf.get(go)
        c=subtree_mesh_count(tid) if tid else 0
        print(f"  '{nm}'  subtreeMeshes={c}")

if __name__=="__main__":
    data = pickle.load(open(CACHE,"rb")) if (os.path.exists(CACHE) and "--recache" not in sys.argv) else build()
    analyze(data)
