"""Shared helpers for the Rocket Slam Blender generators (bpy, Blender 5.x, headless).

Geometry is built in Blender world space (Z up, metres) from small primitives. Each primitive gets
one palette swatch; its hard edges get a small bevel; then its triangles are appended to a Part.
A Part becomes one mesh object whose every face is UV-mapped onto the centre of its swatch in
unity/Assets/Textures/palette.png (see palette.py), so one URP Lit material colours everything.

Axes: Blender (x, y, z) -> Unity (-x, z, -y) with the FBX settings in export_fbx()
(forward -Z, up Y, bake_space_transform, FBX_SCALE_ALL). b2u() does that conversion.
"""
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import palette  # noqa: E402

REPO = palette.REPO
MODELS_DIR = os.path.join(REPO, "unity", "Assets", "Models")
BLEND_DIR = os.path.join(REPO, "blender", "blend")
RENDER_DIR = os.path.join(REPO, "blender", "renders")
PREVIEW_MAX_PX = 960  # long side of preview renders


# ---- coordinates --------------------------------------------------------------------------------

def b2u(v):
    """Blender vector -> Unity vector (FBX export with forward -Z, up Y; Unity mirrors X on import)."""
    return [round(-v[0], 4), round(v[2], 4), round(-v[1], 4)]


def u2b(v):
    """Unity vector -> Blender vector."""
    return Vector((-v[0], -v[2], v[1]))


# ---- scene --------------------------------------------------------------------------------------

def reset_scene():
    global _MAT
    _MAT = None
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights, bpy.data.images):
        for d in list(coll):
            coll.remove(d)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    palette.write_png(palette.PALETTE_PNG)
    return scene


_MAT = None


def palette_material():
    """The single shared material: Principled BSDF with the palette atlas (closest filtering)."""
    global _MAT
    if _MAT is not None and _MAT.name in bpy.data.materials:
        return _MAT
    mat = bpy.data.materials.new("Palette")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = 0.6
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(palette.PALETTE_PNG, check_existing=True)
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.nodes.active = tex
    _MAT = mat
    return mat


# ---- primitives (each returns a bmesh in world space) ------------------------------------------

def bm_box(size, center=(0, 0, 0), matrix=None):
    """Axis-aligned box of `size` centred on `center`, then transformed by optional `matrix`."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    if matrix is not None:
        bmesh.ops.transform(bm, matrix=matrix, verts=bm.verts)
    return bm


def bm_box_minmax(lo, hi, matrix=None):
    lo, hi = Vector(lo), Vector(hi)
    return bm_box(hi - lo, (lo + hi) / 2, matrix)


def bm_lathe(profile, segments=32, cap_start=False, cap_end=False, closed=False, phase=0.0, matrix=None):
    """Revolve a (radius, z) polyline around Z. `closed` joins the last profile point to the first."""
    bm = bmesh.new()
    rings = []
    for (r, z) in profile:
        ring = []
        for s in range(segments):
            a = phase + 2 * math.pi * s / segments
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
        rings.append(ring)
    n = len(rings)
    pairs = [(i, i + 1) for i in range(n - 1)] + ([(n - 1, 0)] if closed else [])
    for i, j in pairs:
        a, b = rings[i], rings[j]
        for s in range(segments):
            t = (s + 1) % segments
            bm.faces.new((a[s], a[t], b[t], b[s]))
    if cap_start:
        bm.faces.new(list(reversed(rings[0])) if profile[0][1] < profile[-1][1] else rings[0])
    if cap_end:
        bm.faces.new(rings[-1] if profile[0][1] < profile[-1][1] else list(reversed(rings[-1])))
    if cap_start or cap_end or closed:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # open surfaces: normals face outward when the profile rises in z (or grows in r at constant z: down)
    if matrix is not None:
        bmesh.ops.transform(bm, matrix=matrix, verts=bm.verts)
    return bm


def bm_cylinder(r, z0, z1, segments=32, phase=0.0, matrix=None):
    return bm_lathe([(r, z0), (r, z1)], segments, cap_start=True, cap_end=True, phase=phase, matrix=matrix)


def bm_beam(p0, p1, width, thick, side=Vector((0, 0, 1)), width1=None, thick1=None):
    """Box from p0 to p1; `width` along `side` (projected), `thick` along the third axis; optional taper."""
    p0, p1 = Vector(p0), Vector(p1)
    d = (p1 - p0).normalized()
    w = (Vector(side) - d * Vector(side).dot(d)).normalized()
    t = d.cross(w)
    width1 = width if width1 is None else width1
    thick1 = thick if thick1 is None else thick1
    bm = bmesh.new()
    vs = []
    for p, ww, tt in ((p0, width, thick), (p1, width1, thick1)):
        for sw, st in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            vs.append(bm.verts.new(p + w * (sw * ww / 2) + t * (st * tt / 2)))
    quads = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    for q in quads:
        bm.faces.new([vs[i] for i in q])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def bm_polygon_prism(points2d, z0, z1, matrix=None):
    """Extrude a convex/concave 2D polygon (x, y) between z0 and z1 (bmesh triangulates on export)."""
    bm = bmesh.new()
    bot = [bm.verts.new((x, y, z0)) for x, y in points2d]
    top = [bm.verts.new((x, y, z1)) for x, y in points2d]
    bm.faces.new(list(reversed(bot)))
    bm.faces.new(top)
    n = len(points2d)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if matrix is not None:
        bmesh.ops.transform(bm, matrix=matrix, verts=bm.verts)
    return bm


def bevel_hard_edges(bm, width, angle_deg=40.0):
    """Small single-segment chamfer on edges sharper than angle_deg (keeps faceted cylinders unbevelled)."""
    if width <= 0:
        return bm
    lim = math.radians(angle_deg)
    edges = [e for e in bm.edges if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > lim]
    if edges:
        bmesh.ops.bevel(bm, geom=edges, offset=width, offset_type="OFFSET", segments=1,
                        profile=0.5, affect="EDGES", clamp_overlap=True)
    return bm


# ---- parts --------------------------------------------------------------------------------------

class Part:
    """Accumulates triangles-or-ngons with a swatch per face; becomes one mesh object."""

    def __init__(self, name):
        self.name = name
        self.verts = []
        self.faces = []
        self.swatches = []

    def add(self, bm, swatch, bevel=0.03, angle=40.0):
        bevel_hard_edges(bm, bevel, angle)
        base = len(self.verts)
        bm.verts.index_update()
        self.verts.extend(v.co.copy() for v in bm.verts)
        for f in bm.faces:
            self.faces.append([base + v.index for v in f.verts])
            self.swatches.append(swatch)
        bm.free()
        return self

    def to_object(self, origin=(0, 0, 0), parent=None, collection=None):
        """Mesh object with its origin (pivot) at `origin` (world space, Blender)."""
        origin = Vector(origin)
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata([v - origin for v in self.verts], [], self.faces)
        mesh.validate(clean_customdata=False)
        uv = mesh.uv_layers.new(name="UVMap")
        for poly, sw in zip(mesh.polygons, self.swatches):
            u, v = palette.uv_of(sw)
            for li in poly.loop_indices:
                uv.data[li].uv = (u, v)
            poly.use_smooth = False
        mesh.materials.append(palette_material())
        mesh.update()
        obj = bpy.data.objects.new(self.name, mesh)
        (collection or bpy.context.scene.collection).objects.link(obj)
        obj.location = origin
        if parent is not None:
            # parent is unrotated at build time: local position = world origin - parent's world origin
            obj.parent = parent
            obj.location = origin - _world_origin(parent)
        return obj


def _world_origin(obj):
    p = Vector(obj.location)
    while obj.parent is not None:
        obj = obj.parent
        p += obj.location
    return p


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def hierarchy(root):
    out = [root]
    for c in root.children:
        out.extend(hierarchy(c))
    return out


def world_bbox(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    bpy.context.view_layer.update()
    for o in objs:
        if o.type != "MESH":
            continue
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


def print_bbox(label, objs):
    lo, hi = world_bbox(objs)
    s = hi - lo
    print(f"[{label}] bbox Blender min {tuple(round(c, 3) for c in lo)} max {tuple(round(c, 3) for c in hi)}"
          f" size {tuple(round(c, 3) for c in s)}  (Unity size x={s.x:.2f} y={s.z:.2f} z={s.y:.2f})")
    return lo, hi


# ---- output -------------------------------------------------------------------------------------

def export_fbx(root, filename):
    bpy.ops.object.select_all(action="DESELECT")
    for o in hierarchy(root):
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    path = os.path.join(MODELS_DIR, filename)
    os.makedirs(MODELS_DIR, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
        axis_forward="-Z", axis_up="Y", apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
    print(f"FBX written: {path} ({os.path.getsize(path) / 1024:.0f} KiB)")
    return path


def save_blend(name):
    os.makedirs(BLEND_DIR, exist_ok=True)
    path = os.path.join(BLEND_DIR, name + ".blend")
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)
    print("blend saved:", path)


def write_json(filename, data):
    path = os.path.join(MODELS_DIR, filename)
    with open(path, "w") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    print("json written:", path)


def human_box(location=(0, 0, 0)):
    """1.8 m tall reference person (0.5 x 0.3 x 1.8), red, for preview renders only (never exported)."""
    p = Part("HumanRef_1p8m")
    x, y, z = location
    p.add(bm_box_minmax((x - 0.25, y - 0.15, z), (x + 0.25, y + 0.15, z + 1.8)), "red", bevel=0.02)
    return p.to_object()


def render_preview(filename, objs, view_dir=(1.0, -1.4, 0.55), res=(1280, 900), margin=1.08, lens=50,
                   target=None, ortho=False, dist=None):
    """Workbench render with palette texture colours; camera framed on the bbox of `objs`."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    sh = scene.display.shading
    sh.light = "STUDIO"
    sh.color_type = "TEXTURE"
    sh.show_cavity = True
    sh.cavity_type = "WORLD"
    sh.show_shadows = True
    sh.shadow_intensity = 0.35
    sh.show_object_outline = False
    scene.display.light_direction = (0.4, -0.3, 0.85)
    try:
        scene.view_settings.view_transform = "Standard"
    except TypeError:
        pass
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World")
    scene.world.color = (0.62, 0.70, 0.78)
    scene.render.resolution_x, scene.render.resolution_y = res
    # keep git-tracked previews small: long side <= PREVIEW_MAX_PX, 8-bit RGB PNG, max compression
    scene.render.resolution_percentage = min(100, int(100 * PREVIEW_MAX_PX / max(res)))
    fmt = scene.render.image_settings
    fmt.file_format = "PNG"
    fmt.color_mode = "RGB"
    fmt.color_depth = "8"
    fmt.compression = 100
    scene.render.dither_intensity = 0.0  # dither noise bloats PNGs
    scene.render.film_transparent = False

    lo, hi = world_bbox(objs)
    center = (lo + hi) / 2 if target is None else Vector(target)
    radius = (hi - lo).length / 2
    cam_data = bpy.data.cameras.get("PreviewCam") or bpy.data.cameras.new("PreviewCam")
    cam_data.lens = lens
    cam_data.clip_end = 5000
    cam = bpy.data.objects.get("PreviewCam") or bpy.data.objects.new("PreviewCam", cam_data)
    if cam.name not in scene.collection.objects:
        scene.collection.objects.link(cam)
    dist_arg = dist
    d = Vector(view_dir).normalized()
    if ortho:
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = 2 * radius * margin
        dist = radius * 4
    else:
        cam_data.type = "PERSP"
        fov = 2 * math.atan(18 / lens * min(res) / max(res))
        dist = radius * margin / math.sin(fov / 2)
    if dist_override := dist_arg:
        dist = dist_override
    cam.location = center + d * dist
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    os.makedirs(RENDER_DIR, exist_ok=True)
    scene.render.filepath = os.path.join(RENDER_DIR, filename)
    bpy.ops.render.render(write_still=True)
    print("render written:", scene.render.filepath)
