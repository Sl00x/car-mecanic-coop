# CMS2027 map reconstruction pipeline (archived)

Reconstructs the Car Mechanic Simulator **2027** world inside CMS**2021** at runtime — no Unity/AssetBundle
(2027 is Unity 6, 2021 is Unity 2020.3, so bundles are incompatible). Proven working in-game (textured city),
but the **full town (~9,700 objects / 32M verts) is too heavy** for CMS2021, so the feature is **disabled**.
The runtime loader lives in `mod/Game/GarageProp.cs` (compiled but no longer invoked — no F8 button, no Tick).

## How it works
1. **Extract** the 2027 scene with [UnityPy](https://pypi.org/project/UnityPy/) from the game's `level8`
   (the main scene). `level8` is at
   `…/steamapps/common/Car Mechanic Simulator 2026 Demo/Car Mechanic Simulator 2027 Demo_Data/level8`.
   **Use the native Windows path** (`C:\…`) — UnityPy can't open the Git-Bash `/c/…` form (returns 0 objects).
2. `scene_scan.py` — load level8, build the Transform graph, cache `scene_graph.pkl`, print the subtree mesh
   counts so you can find the root (`!Garage` = whole map 18.5k, `Interior` = a shop, `Garage` building = 81…).
3. `scene_tex.py <RootGOName> <out> <radius>` — the main extractor. Walks the subtree, for each MeshFilter:
   world-relative 4x4 matrix (NO decompose — reflected instances broke under pos/rot/scale), mesh geometry via
   `mesh.export()` OBJ parse (pos + UV + tris, deduped), material albedo (`_MainTex`/`_BaseColorMap`/… , PNG,
   capped at TEXCAP), UV tiling, alpha/transparency flags, LOD0 only. Outputs `meshes/*.cmsh` (v3: pos+uv+tris),
   `textures/*.png`, `materials.txt` (matId r g b a texIdx sclX sclY offX offY transp alpha), `scene.manifest`
   (meshId matId + 12 affine floats, root-relative).
4. The mod (`GarageProp.cs`) spread-loads it over frames (no freeze): load meshes → textures → materials
   (HDRP/Lit + albedo + alpha-clip + double-sided) → combine instances per-material (Mesh.CombineMeshes) →
   hide the 2021 world (renderers+colliders+terrain, re-asserted each frame) → flat ground collider to walk on.

## To revive
Re-extract a SMALL radius (e.g. `scene_tex.py "!Garage" out 80`) with TEXCAP≈512 for a performant garage
district (R≈100m / ~2,700 objects ran fine). Deploy to `<plugin>/garage2027/`, re-add the F8 buttons + the
`GarageProp.Tick()` call in `Plugin.cs`. The full town needs real LOD/occlusion work to be playable.

Other scripts: `extract.py` (single mesh .asset → .cmsh), `prefab.py` (reconstruct a prefab's hierarchy →
pre-baked .cmsh, e.g. the main gate), `scene_extract.py` (geometry-only, no textures — superseded by scene_tex).
Needs `pip install UnityPy Pillow`.
