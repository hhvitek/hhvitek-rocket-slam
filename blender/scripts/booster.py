"""Booster generator -> unity/Assets/Models/Booster.fbx + Booster_rig.json, preview renders.

Run: blender -b --factory-startup --python blender/scripts/booster.py

Sizes match unity/Assets/Scripts/Rocket/RocketConfig.cs: body 40 m tall, radius 1.85 m, engine nozzle
below the body. ORIGIN = centre of the body's bottom face (= Unity rocket root, y 0); body y 0..40,
nozzle exit plane at y -1.25.

Hierarchy (Unity names): Booster (body mesh)
  GridFin_0..3  pivot at the fin hinge (bottom edge of the fin), Unity azimuth 0/90/180/270 deg
  Leg_0..3      pivot at the leg hinge (radius 1.95, y 2.0), Unity azimuth 45/135/225/315 deg
  Foot_0..3     pivot at the foot pad centre (pad 1.5 m wide, 0.4 m thick, bottom at local y -0.2).
                Feet are children of Booster, NOT of the legs: Blender 5.3's FBX exporter writes wrong
                transforms for grandchildren when baking the axis conversion. Either re-parent Foot_i
                under Leg_i in Unity (SetParent(leg, worldPositionStays: true)) or move it yourself.
Azimuth az (Unity): outward = (cos az, 0, sin az). All parts are modelled STOWED with identity local
rotation. Deploy (Unity, parent space): localRotation = AngleAxis(angle, Cross(Vector3.up, outward)),
which swings the part's "up" towards "outward":
  legs: 111.9 deg  (foot centre then sits exactly where RocketConfig's legLength 9 / 116.4 deg puts it:
        radius 10.01 m, y -2.00; the pad is offset 0.7 m outward from the hinge line so it clears the
        body when stowed, hence the smaller hinge angle). Keep the foot level: after re-parenting under the
        leg, Foot.localRotation = Inverse(leg localRotation) (as RocketLeg does now). Foot contact (pad bottom) deployed: y -2.20, radius 10.01.
  fins: 90 deg (stowed flat up the interstage, deployed horizontal, lattice facing the airflow).
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common as C  # noqa: E402

# ---- dimensions (RocketConfig) ------------------------------------------------------------------
BODY_H = 40.0
R = 1.85
SEG = 32
LEG_HINGE_H = 2.0
LEG_HINGE_R = 1.95
LEG_LEN = 9.0                     # hinge -> foot centre, as in RocketConfig.legLength
GAME_LEG_ANGLE = 116.4            # RocketConfig.legDeployAngleDeg (leg straight along the hinge line)
FOOT_OUT = 0.7                    # stowed foot centre offset outward from the hinge line
FOOT_UP = math.sqrt(LEG_LEN ** 2 - FOOT_OUT ** 2)
LEG_ANGLE = GAME_LEG_ANGLE - math.degrees(math.atan2(FOOT_OUT, FOOT_UP))   # 111.94
FOOT_R = 0.75
FOOT_T = 0.4
FIN_HINGE_H = 36.35
FIN_HINGE_R = 1.95
FIN_W = 1.4                       # tangential
FIN_L = 1.5                       # along the body when stowed (= span when deployed)
FIN_T = 0.2                       # lattice depth
FIN_ANGLE = 90.0
LEG_AZ = [45, 135, 225, 315]      # Unity azimuths
FIN_AZ = [0, 90, 180, 270]

UP = Vector((0, 0, 1))
FOOT_OFFSET = {}  # foot name -> stowed offset from the leg hinge (Blender)


def outward_b(az_unity_deg):
    a = math.radians(az_unity_deg)
    return C.u2b((math.cos(a), 0.0, math.sin(a))).normalized()


def frame(az_unity_deg):
    """Blender matrix: local x = tangent, y = outward, z = up (rotation only)."""
    o = outward_b(az_unity_deg)
    t = UP.cross(o)  # tangent; also the Blender deploy axis (rotates up towards outward)
    return Matrix((t, o, UP)).transposed().to_4x4()


def local(m, x, y, z, origin):
    return origin + m @ Vector((x, y, z))


# ---- body ---------------------------------------------------------------------------------------

def build_body():
    p = C.Part("Booster")
    bands = [  # (z0, z1, radius, swatch)
        (0.0, 3.0, R + 0.03, "carbon"),
        (3.0, 4.2, R, "soot_dark"),
        (4.2, 5.8, R, "soot_mid"),
        (5.8, 8.0, R, "soot_light"),
        (8.0, 11.0, R, "off_white"),
        (11.0, 35.4, R, "white"),
        (35.4, 35.9, R + 0.01, "orange"),
        (35.9, 40.0, R + 0.03, "carbon"),
    ]
    for z0, z1, r, sw in bands:
        p.add(C.bm_cylinder(r, z0, z1, SEG), sw, bevel=0.025)
    # top: slightly recessed dark cap ring + inner dome stub (interstage opening)
    p.add(C.bm_cylinder(R - 0.15, 39.98, 40.12, SEG), "graphite", bevel=0.02)
    # engine bay: heat shield disc + thrust structure + bell nozzle (outer skin, inner skin, rim)
    p.add(C.bm_cylinder(R - 0.1, -0.12, 0.0, SEG), "soot_dark", bevel=0.02)
    p.add(C.bm_lathe([(0.62, -0.12), (0.62, -0.32), (0.5, -0.38)], 24, cap_start=True, cap_end=True), "copper", bevel=0)
    outer = [(0.85, -1.25), (0.70, -0.98), (0.58, -0.70), (0.5, -0.45), (0.47, -0.30)]  # rising z
    t = 0.045
    p.add(C.bm_lathe(outer, 24), "nozzle_out", bevel=0)
    p.add(C.bm_lathe([(r - t, z) for r, z in reversed(outer)], 24), "nozzle_in", bevel=0)
    p.add(C.bm_lathe([(0.85 - t, -1.25), (0.85, -1.25)], 24), "copper", bevel=0)

    # raceway (cable tunnel) between a fin and a leg
    m = frame(22.5)
    o = Vector((0, 0, 0))
    p.add(C.bm_box_minmax((-0.18, R - 0.05, 3.2), (0.18, R + 0.13, 35.3), matrix=m), "off_white", bevel=0.03)
    # small ribs over the raceway
    for z in (6.0, 12.0, 18.0, 24.0, 30.0):
        p.add(C.bm_box_minmax((-0.24, R - 0.05, z), (0.24, R + 0.17, z + 0.25), matrix=m), "steel", bevel=0.02)

    # cold-gas thruster pods on the interstage (between the fins)
    for az in (45, 135, 225, 315):
        m = frame(az)
        p.add(C.bm_box_minmax((-0.35, R, 38.6), (0.35, R + 0.32, 39.4), matrix=m), "graphite", bevel=0.04)
        p.add(C.bm_box_minmax((-0.12, R + 0.32, 38.85), (0.12, R + 0.40, 39.15), matrix=m), "steel", bevel=0.01)

    # grid-fin actuator housings (below each hinge)
    for az in FIN_AZ:
        m = frame(az)
        p.add(C.bm_box_minmax((-0.5, R, FIN_HINGE_H - 0.55), (0.5, R + 0.22, FIN_HINGE_H - 0.02), matrix=m),
              "graphite", bevel=0.04)

    # leg hinge brackets + push-rod mounts
    for az in LEG_AZ:
        m = frame(az)
        p.add(C.bm_box_minmax((-0.55, R, LEG_HINGE_H - 0.55), (-0.38, LEG_HINGE_R + 0.15, LEG_HINGE_H + 0.3),
                              matrix=m), "steel", bevel=0.02)
        p.add(C.bm_box_minmax((0.38, R, LEG_HINGE_H - 0.55), (0.55, LEG_HINGE_R + 0.15, LEG_HINGE_H + 0.3),
                              matrix=m), "steel", bevel=0.02)
        # stripe of warning paint next to each leg top (stowed foot rest)
        p.add(C.bm_box_minmax((-0.4, R - 0.02, 11.4), (0.4, R + 0.03, 11.6), matrix=m), "yellow", bevel=0)
    _add_lettering(p)
    return p.to_object()


def _text_bmesh(text, size):
    import bmesh
    curve = bpy.data.curves.new("Lettering", type="FONT")
    curve.body = text
    curve.size = size
    curve.align_x = "LEFT"
    obj = bpy.data.objects.new("Lettering", curve)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
    bpy.data.objects.remove(obj)
    bpy.data.curves.remove(curve)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bpy.data.meshes.remove(mesh)
    return bm


def _add_digit_one(bm, x0, x1, y0, y1):
    """A '1' with a flag and a base serif (the default font's plain bar reads as 'I').
    x0..x1, y0..y1: the font's own '1' bar, so stroke width and cap height match the other letters."""
    w, h = x1 - x0, y1 - y0
    cx, b = (x0 + x1) / 2, 0.27 * h
    def quad(pts):
        bm.faces.new([bm.verts.new((x, y, 0.0)) for x, y in pts])
    quad([(cx - b, y0), (cx + b, y0), (cx + b, y0 + w), (cx - b, y0 + w)])              # base serif
    quad([(cx - w / 2, y0 + w), (cx + w / 2, y0 + w), (cx + w / 2, y1), (cx - w / 2, y1)])  # stem
    top = Vector((cx - w / 2, y1))
    tip = Vector((cx - 0.3 * h, y1 - 0.24 * h))                                        # flag, down-left
    d = (tip - top).normalized()
    n = Vector((-d.y, d.x))
    n = n if n.y < 0 else -n                                                           # offset towards the stem
    tip2 = tip + n * w
    join = Vector((top.x, tip2.y + d.y / d.x * (top.x - tip2.x)))
    quad([top, tip, tip2, join])                                                       # winding fixed after the wrap


def _add_lettering(part, text="SLAM-1", az_unity=112.5, z_top=31.0, size=1.5, r_text=R + 0.012):
    """Painted name running down the body, wrapped onto the cylinder (no logo, made-up name)."""
    import bmesh
    if text.endswith("1"):
        # font glyphs for everything but the trailing '1', which is built from geometry
        ref = _text_bmesh(text, size)
        bm = _text_bmesh(text[:-1], size)
        xmax = max(v.co.x for v in bm.verts)
        one = [v.co for v in ref.verts if v.co.x > xmax + 1e-4]
        _add_digit_one(bm, min(c.x for c in one), max(c.x for c in one), min(c.y for c in one), max(c.y for c in one))
        ref.free()
    else:
        bm = _text_bmesh(text, size)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    # centre letters vertically, then slice along the wrap direction so the wrap stays smooth
    ys = [v.co.y for v in bm.verts]
    ymid = (min(ys) + max(ys)) / 2
    for v in bm.verts:
        v.co.y -= ymid
    y = min(ys) - ymid
    while y < max(ys) - ymid:
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, y, 0), plane_no=(0, 1, 0))
        y += 0.15
    bmesh.ops.triangulate(bm, faces=bm.faces)
    # wrap: string x -> down the body, letter y -> around the body (+angle = viewer's right)
    o = outward_b(az_unity)
    a0 = math.atan2(o.y, o.x)
    for v in bm.verts:
        x, yy = v.co.x, v.co.y
        a = a0 + yy / r_text
        v.co = Vector((r_text * math.cos(a), r_text * math.sin(a), z_top - x))
    for f in bm.faces:
        f.normal_update()
        c = f.calc_center_median()
        if f.normal.dot(Vector((c.x, c.y, 0))) < 0:
            f.normal_flip()
    part.add(bm, "text_dark", bevel=0)


# ---- legs and fins ------------------------------------------------------------------------------

def build_leg(i, az, body):
    m = frame(az)
    hinge = Vector(local(m, 0, LEG_HINGE_R, LEG_HINGE_H, Vector()))
    foot_c = hinge + m @ Vector((0, FOOT_OUT, FOOT_UP))
    p = C.Part(f"Leg_{i}")
    # hinge knuckle (cylinder along the tangent)
    knuckle = C.bm_cylinder(0.2, -0.36, 0.36, 12, matrix=Matrix.Translation(hinge) @ m @ Matrix.Rotation(math.radians(90), 4, "Y"))
    p.add(knuckle, "steel", bevel=0.02)
    # main beam: tapered box flush with the body, then a short kink out to the foot joint
    kink = hinge + m @ Vector((0, 0.17, FOOT_UP - 1.2))
    p.add(C.bm_beam(hinge + m @ Vector((0, 0.17, -0.1)), kink, 1.0, 0.36, side=m @ Vector((1, 0, 0)),
                    width1=0.6, thick1=0.3), "carbon", bevel=0.03)
    p.add(C.bm_beam(kink - m @ Vector((0, 0, 0.15)), foot_c, 0.6, 0.3, side=m @ Vector((1, 0, 0)),
                    width1=0.4, thick1=0.26), "carbon", bevel=0.03)
    # centre stripe so the leg reads at a distance
    p.add(C.bm_beam(hinge + m @ Vector((0, 0.36, 0.6)), hinge + m @ Vector((0, 0.32, FOOT_UP - 1.6)),
                    0.2, 0.02, side=m @ Vector((1, 0, 0))), "off_white", bevel=0)
    leg = p.to_object(origin=hinge, parent=body)

    f = C.Part(f"Foot_{i}")
    pad = [(FOOT_R * math.cos(math.radians(22.5 + 45 * k)), FOOT_R * math.sin(math.radians(22.5 + 45 * k)))
           for k in range(8)]
    f.add(C.bm_polygon_prism(pad, -FOOT_T / 2, FOOT_T / 2 - 0.12, matrix=Matrix.Translation(foot_c)),
          "graphite", bevel=0.04)
    f.add(C.bm_cylinder(0.22, FOOT_T / 2 - 0.13, FOOT_T / 2 + 0.12, 10, matrix=Matrix.Translation(foot_c)),
          "steel", bevel=0.02)
    foot = f.to_object(origin=foot_c, parent=body)
    FOOT_OFFSET[foot.name] = foot_c - hinge
    return leg, foot, hinge, foot_c


def build_fin(i, az, body):
    m = frame(az)
    hinge = Vector(local(m, 0, FIN_HINGE_R, FIN_HINGE_H, Vector()))
    p = C.Part(f"GridFin_{i}")
    org = Matrix.Translation(hinge) @ m.to_4x4()
    u0, u1 = 0.03, 0.03 + FIN_T
    z0, z1 = 0.15, 0.15 + FIN_L
    hw = FIN_W / 2
    bar = 0.07
    def box(lo, hi, sw="graphite", bevel=0.015):
        p.add(C.bm_box_minmax(lo, hi, matrix=org), sw, bevel=bevel)
    # frame
    box((-hw, u0, z0), (hw, u1, z0 + bar))
    box((-hw, u0, z1 - bar), (hw, u1, z1))
    box((-hw, u0, z0), (-hw + bar, u1, z1))
    box((hw - bar, u0, z0), (hw, u1, z1))
    # lattice 5 x 5 cells
    for k in range(1, 5):
        x = -hw + FIN_W * k / 5
        box((x - 0.02, u0 + 0.01, z0), (x + 0.02, u1 - 0.01, z1), bevel=0)
        z = z0 + FIN_L * k / 5
        box((-hw, u0 + 0.01, z - 0.02), (hw, u1 - 0.01, z + 0.02), bevel=0)
    # root arm + hinge barrel
    box((-0.22, 0.0, -0.08), (0.22, u1, z0 + 0.02), "steel", 0.02)
    p.add(C.bm_cylinder(0.1, -0.3, 0.3, 10, matrix=org @ Matrix.Rotation(math.radians(90), 4, "Y")), "steel", bevel=0)
    return p.to_object(origin=hinge, parent=body), hinge


# ---- posing -------------------------------------------------------------------------------------

def deploy_quat_b(az, angle_deg):
    return Quaternion(UP.cross(outward_b(az)), math.radians(angle_deg))


def pose(legs, fins, t):
    for (leg, foot, az) in legs:
        q = deploy_quat_b(az, LEG_ANGLE * t)
        leg.rotation_mode = "QUATERNION"
        leg.rotation_quaternion = q
        foot.location = leg.location + q @ FOOT_OFFSET[foot.name]  # stays level (identity rotation)
    for (fin, az) in fins:
        fin.rotation_mode = "QUATERNION"
        fin.rotation_quaternion = deploy_quat_b(az, FIN_ANGLE * t)
    bpy.context.view_layer.update()


def unity_rotate(v, axis, angle_deg):
    """Rodrigues rotation with Unity's AngleAxis convention (same quaternion math as standard)."""
    v, k = Vector(v), Vector(axis).normalized()
    a = math.radians(angle_deg)
    return v * math.cos(a) + k.cross(v) * math.sin(a) + k * k.dot(v) * (1 - math.cos(a))


def main():
    C.reset_scene()
    body = build_body()
    legs, fins, rig_legs, rig_fins = [], [], [], []
    for i, az in enumerate(LEG_AZ):
        leg, foot, hinge, foot_c = build_leg(i, az, body)
        legs.append((leg, foot, az))
        o_u = Vector(C.b2u(outward_b(az)))
        axis_u = Vector((0, 1, 0)).cross(o_u)
        foot_local_u = Vector(C.b2u(foot_c - hinge))
        deployed_foot_u = Vector(C.b2u(hinge)) + unity_rotate(foot_local_u, axis_u, LEG_ANGLE)
        rig_legs.append({
            "name": f"Leg_{i}", "parent": "Booster", "azimuth_deg": az,
            "pivot_local": C.b2u(hinge), "outward": [round(c, 4) for c in o_u],
            "deploy_axis_parent_space": [round(c, 4) for c in axis_u], "deploy_angle_deg": round(LEG_ANGLE, 2),
            "foot": {"name": f"Foot_{i}", "parent_in_fbx": "Booster", "pivot_local_stowed": C.b2u(foot_c),
                     "pivot_local_in_leg": C.b2u(foot_c - hinge),
                     "keep_level": "re-parent under the leg (worldPositionStays) then Foot.localRotation = "
                                   "Inverse(Leg.localRotation); or, kept under Booster: localPosition = "
                                   "Leg.localPosition + Leg.localRotation * pivot_local_in_leg, localRotation identity",
                     "pad_radius": FOOT_R, "pad_thickness": FOOT_T, "contact_local_offset": [0, -FOOT_T / 2, 0],
                     "deployed_centre_in_booster": [round(c, 3) for c in deployed_foot_u],
                     "deployed_contact_in_booster": [round(deployed_foot_u.x, 3), round(deployed_foot_u.y - FOOT_T / 2, 3),
                                                     round(deployed_foot_u.z, 3)]},
        })
    for i, az in enumerate(FIN_AZ):
        fin, hinge = build_fin(i, az, body)
        fins.append((fin, az))
        o_u = Vector(C.b2u(outward_b(az)))
        rig_fins.append({
            "name": f"GridFin_{i}", "parent": "Booster", "azimuth_deg": az, "pivot_local": C.b2u(hinge),
            "outward": [round(c, 4) for c in o_u],
            "deploy_axis_parent_space": [round(c, 4) for c in Vector((0, 1, 0)).cross(o_u)],
            "deploy_angle_deg": FIN_ANGLE, "size_m": {"width": FIN_W, "span": FIN_L + 0.15, "depth": FIN_T}})

    allobjs = C.hierarchy(body)
    print("triangles:", {o.name: C.tri_count(o) for o in allobjs}, "total", sum(C.tri_count(o) for o in allobjs))
    C.print_bbox("booster stowed", allobjs)

    # numeric check of the deployed pose in Blender against the Unity-side JSON numbers
    pose(legs, fins, 1.0)
    lo, hi = C.print_bbox("booster deployed", allobjs)
    for (leg, foot, az), rl in zip(legs, rig_legs):
        fb = foot.matrix_world.translation
        print(f"  {leg.name}: Blender foot centre -> Unity {C.b2u(fb)}, JSON {rl['foot']['deployed_centre_in_booster']},"
              f" radius {math.hypot(fb.x, fb.y):.3f}")
    print(f"  lowest point (Unity y) deployed: {lo.z:.3f}  (game foot box bottom: {LEG_HINGE_H - LEG_LEN * math.cos(math.radians(180 - GAME_LEG_ANGLE)) - 0.2:.3f})")
    pose(legs, fins, 0.0)

    C.export_fbx(body, "Booster.fbx")
    C.write_json("Booster_rig.json", {
        "model": "Booster.fbx",
        "units": "metres; Unity coordinates (x right, y up, z forward), relative to the parent named",
        "origin": "centre of the body's bottom face (Unity rocket root). Body y 0..40, radius 1.85; nozzle exit plane y -1.25",
        "pose_in_file": "stowed, every rigged part has identity local rotation",
        "deploy_rule": "localRotation = Quaternion.AngleAxis(deploy_angle_deg * t, deploy_axis_parent_space); "
                       "axis = Vector3.Cross(Vector3.up, outward) swings the part's up towards outward",
        "material": "single material 'Palette'; assign one URP Lit material with Assets/Textures/palette.png",
        "legs": rig_legs, "grid_fins": rig_fins,
        "game_config_note": f"foot centres match RocketConfig legLength 9 / angle 116.4 deg; with this model use "
                            f"legDeployAngleDeg = {LEG_ANGLE:.2f} on the hinge (pad offset {FOOT_OUT} m outward)",
    })
    C.save_blend("booster")

    human = C.human_box(C.u2b((4.0, -2.2, 0.0)))
    C.render_preview("booster_stowed.png", allobjs + [human], view_dir=(1.0, -1.6, 0.35), res=(700, 1300), margin=0.62)
    C.render_preview("booster_lettering.png", allobjs, view_dir=(1.0, -1.6, 0.1), res=(900, 900), target=(0, 0, 26), dist=22)
    pose(legs, fins, 1.0)
    C.render_preview("booster_deployed.png", allobjs + [human], view_dir=(1.0, -1.6, 0.35), res=(900, 1300), margin=0.62)
    C.render_preview("booster_deployed_bottom.png", allobjs + [human], view_dir=(1.0, -1.3, -0.12), res=(1300, 900),
                     target=(0, 0, 1.0), lens=35, dist=30)
    C.render_preview("booster_top_detail.png", allobjs, view_dir=(1.0, -1.2, 0.5), res=(1200, 900),
                     target=(0, 0, 37.0), lens=50, dist=13)


main()
