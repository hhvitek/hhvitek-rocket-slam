"""Scenery generator -> unity/Assets/Models/Scenery_*.fbx, Scenery_catalog.json, preview renders.

Run: blender -b --factory-startup --python blender/scripts/scenery.py

Low-poly, flat-shaded set dressing for the ground landing site (our own generic designs, no names or
logos): 8 buildings, road tiles, fence, lamp post, radar dish, 4 trees, bush, rock cluster.
Every piece is one mesh object, single material 'Palette'. ORIGIN = ground centre of its footprint
(Unity y 0). Roads, trees and rocks reach up to 0.3 m below ground so they sit on uneven terrain.
Orientation (Unity): doors / fronts face +Z (Blender -Y). Road_Straight runs along Z; Road_T is open
to +Z, -Z and -X; Road_Corner is open to +Z and -X. Fence segment runs along X (10 m).
"""
import math
import os
import random
import sys

import bmesh
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common as C  # noqa: E402

PIECES = []  # (object, suggested use)


def box(p, lo, hi, sw, bevel=0.0):
    p.add(C.bm_box_minmax(lo, hi), sw, bevel=bevel)


def prism_x(profile_yz, x0, x1):
    """Polygon (y, z) extruded along X from x0 to x1."""
    m = Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1)))  # (a, b, c) -> (c, a, b)
    return C.bm_polygon_prism(profile_yz, x0, x1, matrix=m)


def prism_y(profile_xz, y0, y1):
    """Polygon (x, z) extruded along Y from y0 to y1."""
    m = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))  # (a, b, c) -> (a, c, b)
    bm = C.bm_polygon_prism(profile_xz, y0, y1, matrix=m)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def cone(r, z0, z1, seg=7, center=(0, 0), rnd=None, jitter=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=0.0,
                          depth=z1 - z0, matrix=Matrix.Translation((center[0], center[1], (z0 + z1) / 2)))
    if rnd:
        for v in bm.verts:
            v.co.x += rnd.uniform(-jitter, jitter) * r
            v.co.y += rnd.uniform(-jitter, jitter) * r
    return bm


def blob(r, center, rnd, sub=1, scale=(1, 1, 1), jitter=0.12):
    """Jittered icosphere (foliage, rocks)."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=r)
    for v in bm.verts:
        k = 1 + rnd.uniform(-jitter, jitter)
        v.co = Vector((v.co.x * scale[0] * k, v.co.y * scale[1] * k, v.co.z * scale[2] * k)) + Vector(center)
    return bm


def finish(p, use):
    """Object with origin at the footprint centre on the ground (x, y centred, z 0)."""
    xs = [v.x for v in p.verts]
    ys = [v.y for v in p.verts]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    obj = p.to_object(origin=(round(cx, 3), round(cy, 3), 0.0))
    obj.location = (0, 0, 0)
    PIECES.append((obj, use))
    return obj


# ---- buildings ----------------------------------------------------------------------------------

def hangar():
    p = C.Part("Scenery_Hangar")
    box(p, (-30, -20, 0), (30, 20, 34), "cladding", 0.15)
    box(p, (-30.15, -20.15, -0.2), (30.15, 20.15, 2.5), "concrete_dk", 0.05)
    box(p, (-30.3, -20.3, 34), (30.3, 20.3, 35.5), "cladding_dk", 0.1)        # parapet / roof edge
    box(p, (-30.1, -20.1, 30.6), (30.1, 20.1, 31.6), "orange", 0.0)           # accent band
    box(p, (-12, -20.3, 0), (12, -19.9, 29), "steel", 0.0)                     # big door
    for z in (5.5, 11.5, 17.5, 23.5):
        box(p, (-12, -20.4, z), (12, -20.25, z + 0.25), "graphite")
    box(p, (-0.15, -20.4, 0), (0.15, -20.25, 29), "graphite")
    box(p, (-13, -20.8, 29), (13, -19.9, 30.6), "graphite", 0.05)               # door header
    box(p, (-13, -20.8, 0), (-12, -19.9, 29), "graphite")                       # door jambs
    box(p, (12, -20.8, 0), (13, -19.9, 29), "graphite")
    box(p, (18, -20.3, 0), (19.6, -19.9, 2.4), "cladding_dk")                  # personnel door
    for s in (-1, 1):                                                            # side window strips
        box(p, (s * 30 - 0.15, -16, 20), (s * 30 + 0.15, 16, 22.5), "glass")
    box(p, (-26, 19.9, 20), (26, 20.15, 22.5), "glass")
    for x in (-18, -6, 6, 18):                                                  # roof vents
        box(p, (x - 2, -4, 34), (x + 2, 4, 36.5), "steel", 0.08)
    return finish(p, "Vehicle assembly hangar, landmark at 300-600 m; big door faces +Z")


def bunker():
    p = C.Part("Scenery_Bunker")
    prof = [(-7.5, -0.2), (7.5, -0.2), (7.5, 1.0), (5.5, 4.4), (-5.5, 4.4), (-7.5, 1.0)]
    p.add(prism_y(prof, -5, 5), "concrete", bevel=0.12)
    box(p, (-1.2, -5.15, 0), (1.2, -4.9, 2.4), "steel")                         # blast door
    box(p, (-1.5, -5.3, 2.4), (1.5, -4.9, 2.7), "concrete_dk")
    box(p, (2.5, -5.08, 2.6), (5.0, -4.95, 3.0), "glass")                        # view slit
    box(p, (-5.0, -5.08, 2.6), (-2.5, -4.95, 3.0), "glass")
    for x, y in ((-2.5, 1.5), (2.5, 1.5)):                                      # periscope heads
        box(p, (x - 0.4, y - 0.4, 4.4), (x + 0.4, y + 0.4, 5.0), "graphite", 0.04)
    p.add(C.bm_cylinder(0.08, 4.4, 8.0, 6, matrix=Matrix.Translation((0, 2.5, 0))), "steel", bevel=0)
    box(p, (-0.12, 2.38, 8.0), (0.12, 2.62, 8.25), "red")
    return finish(p, "Concrete blockhouse / control bunker near the pad (150-400 m); door faces +Z")


def water_tower():
    p = C.Part("Scenery_WaterTower")
    top = 22.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.add(C.bm_beam((sx * 5.5, sy * 5.5, -0.2), (sx * 3.2, sy * 3.2, top), 0.6, 0.6), "steel", bevel=0)
            box(p, (sx * 5.5 - 0.7, sy * 5.5 - 0.7, -0.2), (sx * 5.5 + 0.7, sy * 5.5 + 0.7, 0.4), "concrete_dk")
    for z0, z1 in ((0.5, 7.5), (7.5, 14.5), (14.5, 21.5)):                     # X braces per face
        t0, t1 = z0 / top, z1 / top
        r0, r1 = 5.5 - 2.3 * t0, 5.5 - 2.3 * t1
        for a, b, c, d in (((-r0, -r0), (r1, -r1), (r0, -r0), (-r1, -r1)),
                           ((-r0, r0), (r1, r1), (r0, r0), (-r1, r1)),
                           ((-r0, -r0), (-r1, r1), (-r0, r0), (-r1, -r1)),
                           ((r0, -r0), (r1, r1), (r0, r0), (r1, -r1))):
            p.add(C.bm_beam((a[0], a[1], z0), (b[0], b[1], z1), 0.2, 0.2), "steel", bevel=0)
            p.add(C.bm_beam((c[0], c[1], z0), (d[0], d[1], z1), 0.2, 0.2), "steel", bevel=0)
    p.add(C.bm_cylinder(0.5, 0, top, 8), "steel", bevel=0)                       # riser pipe
    tank = [(0.6, top - 0.2), (4.0, top + 1.6), (6.0, top + 3.0), (6.0, top + 7.0), (3.0, top + 8.6), (0.4, top + 9.0)]
    p.add(C.bm_lathe(tank, 16, cap_start=True, cap_end=True), "tank_white", bevel=0)
    p.add(C.bm_lathe([(6.1, top + 4.2), (6.1, top + 5.2)], 16, cap_start=False, cap_end=False), "red", bevel=0)
    p.add(C.bm_lathe([(6.5, top + 2.8), (6.5, top + 3.1)], 16, cap_start=True, cap_end=True), "graphite", bevel=0)
    p.add(C.bm_cylinder(0.12, top + 9.0, top + 10.0, 6), "steel", bevel=0)
    box(p, (-0.2, -0.2, top + 10.0), (0.2, 0.2, top + 10.4), "red")
    return finish(p, "Water tower ~32 m, vertical landmark for altitude/speed cues")


def tank_sphere():
    p = C.Part("Scenery_TankSphere")
    zc, r = 8.0, 6.0
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=r, matrix=Matrix.Translation((0, 0, zc)))
    p.add(bm, "tank_white", bevel=0)
    for k in range(8):
        a = 2 * math.pi * (k + 0.5) / 8
        x, y = r * math.cos(a), r * math.sin(a)
        p.add(C.bm_cylinder(0.3, 0.3, zc, 6, matrix=Matrix.Translation((x, y, 0))), "steel", bevel=0)
        p.add(C.bm_beam((x, y, 0.8), (r * math.cos(a + math.pi / 4), r * math.sin(a + math.pi / 4), zc - 1.5),
                        0.12, 0.12), "steel", bevel=0)
    p.add(C.bm_cylinder(7.2, -0.2, 0.3, 16), "concrete", bevel=0)
    p.add(C.bm_lathe([(r + 0.05, zc - 0.4), (r + 0.05, zc + 0.4)], 16), "orange", bevel=0)
    p.add(C.bm_cylinder(1.2, zc + r - 0.3, zc + r + 0.3, 8), "graphite", bevel=0)  # top platform
    p.add(C.bm_cylinder(0.25, 0.3, zc - r + 0.4, 6), "graphite", bevel=0)         # outlet pipe
    return finish(p, "Spherical propellant tank ~14.5 m (tank farm, 200-500 m)")


def tank_pair():
    p = C.Part("Scenery_TankPair")
    box(p, (-12, -6.5, -0.2), (12, 6.5, 0.2), "concrete", 0.0)                    # floor
    for (lo, hi) in (((-12, -6.5, 0), (12, -6.0, 1.2)), ((-12, 6.0, 0), (12, 6.5, 1.2)),
                     ((-12, -6.0, 0), (-11.5, 6.0, 1.2)), ((11.5, -6.0, 0), (12, 6.0, 1.2))):
        box(p, lo, hi, "concrete_dk", 0.04)                                       # bund wall
    for x in (-5.5, 5.5):
        m = Matrix.Translation((x, 0, 0))
        p.add(C.bm_lathe([(4.0, 0.2), (4.0, 12.5), (3.2, 14.0), (1.4, 14.8), (0.3, 15.0)], 14,
                         cap_start=True, cap_end=True, matrix=m), "tank_white", bevel=0)
        p.add(C.bm_lathe([(4.05, 9.5), (4.05, 10.5)], 14, matrix=m), "yellow", bevel=0)
        p.add(C.bm_beam((x + 4.0, 0, 0.2), (x + 4.0, 0, 13.0), 0.5, 0.15), "graphite", bevel=0)  # ladder
    p.add(C.bm_beam((-5.5, 0, 14.6), (5.5, 0, 14.6), 1.2, 0.2), "graphite", bevel=0)  # catwalk
    for z in (1.6, 2.6):
        p.add(C.bm_beam((-1.5, -2.0, z), (1.5, -2.0, z), 0.4, 0.4, side=Vector((0, 1, 0))), "steel", bevel=0)
    p.add(C.bm_beam((0, -2.0, 1.6), (0, -9.0, 1.6), 0.4, 0.4), "steel", bevel=0)  # pipe out of the bund
    return finish(p, "Pair of vertical fuel tanks in a bund, ~15 m (tank farm)")


def office():
    p = C.Part("Scenery_Office")
    box(p, (-12, -7, -0.2), (12, 7, 10.2), "facade", 0.08)
    for k in range(3):
        z0, z1 = 1.0 + 3.4 * k, 2.6 + 3.4 * k
        box(p, (-11, -7.12, z0), (11, -6.9, z1), "glass")
        box(p, (-11, 6.9, z0), (11, 7.12, z1), "glass")
        box(p, (-12.12, -6, z0), (-11.9, 6, z1), "glass")
        box(p, (11.9, -6, z0), (12.12, 6, z1), "glass")
    for (lo, hi) in (((-12.1, -7.1, 10.2), (12.1, -6.7, 10.9)), ((-12.1, 6.7, 10.2), (12.1, 7.1, 10.9)),
                     ((-12.1, -6.7, 10.2), (-11.7, 6.7, 10.9)), ((11.7, -6.7, 10.2), (12.1, 6.7, 10.9))):
        box(p, lo, hi, "cladding_dk")                                               # parapet
    for x in (-6, 0, 6):
        box(p, (x - 1.2, -1.5, 10.2), (x + 1.2, 1.5, 11.6), "steel", 0.05)       # AC units
    box(p, (-3, -9.5, 3.0), (3, -7.0, 3.3), "cladding_dk")                       # entrance canopy
    box(p, (-1.5, -7.15, 0), (1.5, -6.9, 2.6), "glass")
    p.add(C.bm_cylinder(0.06, 10.2, 15.0, 6, matrix=Matrix.Translation((9, 4, 0))), "steel", bevel=0)
    return finish(p, "Office / operations block, 3 floors (~11 m); entrance faces +Z")


def warehouse():
    p = C.Part("Scenery_Warehouse")
    w = 11.5
    p.add(prism_x([(-w, -0.2), (w, -0.2), (w, 8.0), (0, 10.6), (-w, 8.0)], -20, 20), "cladding", bevel=0.08)
    box(p, (-20.1, -w - 0.1, -0.2), (20.1, w + 0.1, 1.2), "concrete_dk")
    for s in (-1, 1):  # roof halves with 0.6 m overhang
        e = Vector((0, s * (w + 0.6), 8.0 - 2.6 * 0.6 / w))
        r = Vector((0, 0, 10.6))
        n = Vector((0, -s * 2.6, w)).normalized() * 0.15
        p.add(C.bm_beam(e + n, r + n, 41.0, 0.3, side=Vector((1, 0, 0))), "roof", bevel=0)
    for x in (-12, 0, 12):                                                         # roll-up doors
        box(p, (x - 2.6, -w - 0.15, 0), (x + 2.6, -w + 0.05, 5.2), "steel")
        box(p, (x - 2.8, -w - 0.25, 5.2), (x + 2.8, -w + 0.05, 5.6), "cladding_dk")
    box(p, (-20, -w - 2.5, -0.2), (20, -w, 1.2), "concrete", 0.05)                 # loading dock
    box(p, (16, -w - 0.12, 2.2), (19, -w + 0.05, 3.4), "glass")
    for s in (-1, 1):
        box(p, (s * 20 - 0.12, -6, 5.5), (s * 20 + 0.12, 6, 6.5), "glass")
    return finish(p, "Warehouse / workshop with gable roof (~11 m); doors and dock face +Z")


def observation_tower():
    p = C.Part("Scenery_ObsTower")
    h, b0, b1 = 26.0, 3.0, 2.0
    bays = 6
    for sx in (-1, 1):
        for sy in (-1, 1):
            for k in range(bays):
                z0, z1 = h * k / bays, h * (k + 1) / bays
                r0, r1 = b0 + (b1 - b0) * k / bays, b0 + (b1 - b0) * (k + 1) / bays
                p.add(C.bm_beam((sx * r0, sy * r0, z0 - (0.2 if k == 0 else 0)), (sx * r1, sy * r1, z1), 0.35, 0.35),
                      "red" if k % 2 == 0 else "paint_white", bevel=0)
    for k in range(bays):
        z0, z1 = h * k / bays, h * (k + 1) / bays
        r0, r1 = b0 + (b1 - b0) * k / bays, b0 + (b1 - b0) * (k + 1) / bays
        for (ax, ay), (bx, by) in (((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1))):
            p.add(C.bm_beam((ax * r0, ay * r0, z0), (bx * r1, by * r1, z1), 0.12, 0.12), "steel", bevel=0)
            p.add(C.bm_beam((bx * r0, by * r0, z0), (ax * r1, ay * r1, z1), 0.12, 0.12), "steel", bevel=0)
    box(p, (-4, -4, h - 0.3), (4, 4, h), "graphite", 0.03)                          # gallery floor
    box(p, (-3, -3, h), (3, 3, h + 1.0), "off_white", 0.04)                         # cab
    box(p, (-2.9, -2.9, h + 1.0), (2.9, 2.9, h + 3.0), "glass")
    box(p, (-3.4, -3.4, h + 3.0), (3.4, 3.4, h + 3.6), "graphite", 0.05)
    p.add(C.bm_cylinder(0.08, h + 3.6, h + 7.5, 6), "steel", bevel=0)
    box(p, (-0.2, -0.2, h + 7.5), (0.2, 0.2, h + 7.9), "red")
    return finish(p, "Lattice observation / range-control tower ~34 m, red-white landmark")


# ---- infrastructure -----------------------------------------------------------------------------

ROAD_TOP = 0.05
MZ0, MZ1 = ROAD_TOP - 0.005, ROAD_TOP + 0.01


def road_straight():
    p = C.Part("Scenery_Road_Straight")
    box(p, (-5, -10, -0.3), (5, 10, ROAD_TOP), "asphalt")
    for s in (-1, 1):
        box(p, (s * 4.6 - 0.075, -10, MZ0), (s * 4.6 + 0.075, 10, MZ1), "paint_white")
    for y0 in (-7, 3):
        box(p, (-0.075, y0, MZ0), (0.075, y0 + 4, MZ1), "paint_white")
    return finish(p, "Road 10 x 20 m, runs along Z; tile every 20 m (dashes 4 m on / 6 m off)")


def road_t():
    p = C.Part("Scenery_Road_T")
    box(p, (-5, -5, -0.3), (5, 5, ROAD_TOP), "asphalt")
    box(p, (-4.675, -5, MZ0), (-4.525, 5, MZ1), "paint_white")                     # closed side (Unity +X)
    box(p, (-0.075, -2, MZ0), (0.075, 2, MZ1), "paint_white")                      # through centre dash
    for y0 in (0.6, 2.6):                                                           # give-way dashes on branch
        box(p, (4.4, y0, MZ0), (4.7, y0 + 1.2, MZ1), "paint_white")
    return finish(p, "Road T-junction 10 x 10 m: through road along Z, branch to Unity -X")


def road_corner():
    p = C.Part("Scenery_Road_Corner")
    box(p, (-5, -5, -0.3), (5, 5, ROAD_TOP), "asphalt")
    box(p, (-4.675, -5, MZ0), (-4.525, 4.675, MZ1), "paint_white")                 # outer edge lines
    box(p, (-4.675, 4.525, MZ0), (5, 4.675, MZ1), "paint_white")
    box(p, (4.525, -5, MZ0), (4.675, -4.525, MZ1), "paint_white")                  # inner corner stub
    box(p, (4.525, -4.675, MZ0), (5, -4.525, MZ1), "paint_white")
    return finish(p, "Road 90-degree corner 10 x 10 m, open to Unity +Z and -X")


def fence():
    p = C.Part("Scenery_Fence")
    for x in (-5, 5):
        p.add(C.bm_cylinder(0.05, -0.3, 2.5, 6, matrix=Matrix.Translation((x, 0, 0))), "steel", bevel=0)
        p.add(C.bm_beam((x, 0, 2.5), (x, -0.45, 2.9), 0.06, 0.06), "steel", bevel=0)  # barbed-wire arm
    box(p, (-5, -0.015, 0.05), (5, 0.015, 2.4), "steel")                            # mesh panel
    for z in (2.4, 1.2):
        p.add(C.bm_beam((-5, 0.04, z), (5, 0.04, z), 0.06, 0.06), "cladding_dk", bevel=0)
    for k in range(3):
        f = 0.13 * (k + 1)
        p.add(C.bm_beam((-5, -f * 0.45 / 0.4, 2.5 + f), (5, -f * 0.45 / 0.4, 2.5 + f), 0.02, 0.02), "graphite", bevel=0)
    return finish(p, "Perimeter fence segment 10 m along X, 2.9 m (instance in rows; outside faces +Z)")


def lamp_post():
    p = C.Part("Scenery_LampPost")
    p.add(C.bm_cylinder(0.35, -0.3, 0.5, 8), "concrete_dk", bevel=0)
    p.add(C.bm_lathe([(0.14, 0.5), (0.08, 10.0)], 8, cap_end=True), "steel", bevel=0)
    p.add(C.bm_beam((0, 0, 9.8), (0, -1.8, 10.25), 0.08, 0.08), "steel", bevel=0)
    box(p, (-0.3, -2.4, 10.0), (0.3, -1.6, 10.35), "light_case", 0.03)
    box(p, (-0.22, -2.3, 9.94), (0.22, -1.7, 10.0), "lamp")
    return finish(p, "Street / apron lamp 10.4 m; lamp swatch can be emissive; arm points +Z")


def radar_dish():
    p = C.Part("Scenery_RadarDish")
    box(p, (-2.5, -2.5, -0.2), (2.5, 2.5, 1.0), "concrete", 0.06)
    p.add(C.bm_lathe([(1.1, 1.0), (0.8, 6.0)], 10, cap_end=True), "off_white", bevel=0)
    box(p, (-1.4, -0.9, 6.0), (1.4, 0.9, 6.6), "graphite", 0.04)
    for s in (-1, 1):
        box(p, (s * 1.4 - 0.25 * (1 - s) / 2 - 0.0, -0.4, 6.6), (s * 1.4 + 0.25 * (1 + s) / 2, 0.4, 8.4), "graphite")
    R, f, t = 5.0, 3.0, 0.15
    rs = [0.4, 1.4, 2.4, 3.3, 4.2, 5.0]
    outer = [(r, r * r / (4 * f)) for r in rs]
    inner = [(r, r * r / (4 * f) + t) for r in reversed(rs)]
    rot = Matrix.Translation((0, 0, 8.0)) @ Matrix.Rotation(math.radians(55), 4, "X") @ Matrix.Translation((0, 0, -0.9))
    p.add(C.bm_lathe(outer + inner, 16, closed=True, matrix=rot), "paint_white", bevel=0)
    focus = rot @ Vector((0, 0, f))
    for k in range(3):
        a = 2 * math.pi * k / 3 + math.pi / 2
        rim = rot @ Vector((R * 0.9 * math.cos(a), R * 0.9 * math.sin(a), (R * 0.9) ** 2 / (4 * f) + t))
        p.add(C.bm_beam(rim, focus, 0.08, 0.08), "steel", bevel=0)
    p.add(C.bm_beam(rot @ Vector((0, 0, f - 0.5)), rot @ Vector((0, 0, f + 0.3)), 0.5, 0.5), "graphite", bevel=0)
    p.add(C.bm_beam(rot @ Vector((0, 0, 0.2)), Vector((0, 0, 7.6)), 0.6, 0.6), "graphite", bevel=0)
    return finish(p, "Tracking radar / antenna dish ~12 m, 10 m dish tilted 35 deg toward +Z")


# ---- nature -------------------------------------------------------------------------------------

def conifer(name, height, width, seed, tiers=3):
    rnd = random.Random(seed)
    p = C.Part(name)
    p.add(C.bm_lathe([(0.06 * width + 0.1, -0.3), (0.04 * width + 0.05, height * 0.3)], 6, cap_end=True), "bark", bevel=0)
    z0 = height * 0.18
    step = (height - z0) * 0.3
    for k in range(tiers):
        r = width / 2 * (1 - 0.28 * k)
        base = z0 + step * k
        top = height if k == tiers - 1 else base + (height - z0) * 0.55
        p.add(cone(r, base, top, 7, rnd=rnd, jitter=0.08), "foliage_dk" if k % 2 == 0 else "foliage", bevel=0)
    return finish(p, f"Conifer ~{height:.0f} m; scatter in clusters, rotate/scale 0.8-1.2 randomly")


def broadleaf(name, height, width, seed, blobs):
    rnd = random.Random(seed)
    p = C.Part(name)
    trunk_top = height * 0.5
    p.add(C.bm_lathe([(0.05 * width + 0.12, -0.3), (0.035 * width + 0.06, trunk_top + 0.6)], 6, cap_end=True),
          "bark", bevel=0)
    crown_z = height - width * 0.42
    for k in range(blobs):
        a = 2 * math.pi * k / blobs + rnd.uniform(-0.4, 0.4)
        d = 0.0 if k == 0 else width * 0.22
        rr = width * (0.42 if k == 0 else 0.3)
        c = (d * math.cos(a), d * math.sin(a), crown_z - (0 if k == 0 else rnd.uniform(0.5, 1.5)))
        p.add(blob(rr, c, rnd, sub=1 if k else 2, scale=(1, 1, 0.85)), "foliage" if k % 2 == 0 else "foliage_lt",
              bevel=0)
    return finish(p, f"Broadleaf tree ~{height:.0f} m; scatter, rotate/scale randomly")


def bush():
    rnd = random.Random(31)
    p = C.Part("Scenery_Bush")
    for k, (x, y, r) in enumerate(((0, 0, 0.9), (0.8, 0.3, 0.65), (-0.6, 0.5, 0.6))):
        p.add(blob(r, (x, y, r * 0.55), rnd, scale=(1, 1, 0.8)), "foliage_dk" if k == 0 else "foliage", bevel=0)
    return finish(p, "Bush ~1.3 m; scatter along roads, fences and near buildings")


def rocks():
    rnd = random.Random(53)
    p = C.Part("Scenery_Rocks")
    for k, (x, y, r, sq) in enumerate(((0, 0, 1.2, 0.7), (1.5, 0.6, 0.8, 0.6), (-1.1, 0.9, 0.7, 0.8),
                                       (0.6, -1.2, 0.5, 0.6))):
        p.add(blob(r, (x, y, r * sq * 0.6), rnd, scale=(1.1, 0.9, sq), jitter=0.2), "rock" if k % 2 == 0 else "rock_dk",
              bevel=0)
    return finish(p, "Rock cluster ~4 m; scatter in grass, sinks into uneven terrain")


# ---- main ---------------------------------------------------------------------------------------

def place(src, loc, rot=0.0, scale=1.0, name=None):
    import bpy
    o = bpy.data.objects.new(name or src.name + "_inst", src.data)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (0, 0, rot)
    o.scale = (scale, scale, scale)
    return o


def lay_row(objs, gap, y=0.0):
    """Arrange objects left-to-right along X with `gap` metres between bounding boxes."""
    x = 0.0
    for o in objs:
        lo, hi = C.world_bbox([o])
        w = hi.x - lo.x
        o.location = (x + w / 2, y, 0)
        x += w + gap
    return x


def main():
    import bpy
    C.reset_scene()
    buildings = [hangar(), bunker(), water_tower(), tank_sphere(), tank_pair(), office(), warehouse(),
                 observation_tower()]
    infra = [road_straight(), road_t(), road_corner(), fence(), lamp_post(), radar_dish()]
    nature = [conifer("Scenery_Conifer_A", 12.0, 5.0, 3), conifer("Scenery_Conifer_B", 8.0, 2.8, 5, tiers=2),
              broadleaf("Scenery_Broadleaf_A", 10.0, 6.5, 11, 4), broadleaf("Scenery_Broadleaf_B", 6.5, 4.2, 17, 3),
              bush(), rocks()]

    # numeric checks, export, catalog
    catalog = []
    for obj, use in PIECES:
        lo, hi = C.print_bbox(obj.name, [obj])
        tris = C.tri_count(obj)
        path = C.export_fbx(obj, obj.name + ".fbx")
        kib = os.path.getsize(path) / 1024
        print(f"  {obj.name}: {tris} tris, {kib:.0f} KiB")
        assert abs(lo.x + hi.x) < 0.01 and abs(lo.y + hi.y) < 0.01, "footprint not centred"
        catalog.append({
            "name": obj.name, "fbx": obj.name + ".fbx",
            "footprint_x": round(hi.x - lo.x, 2), "footprint_z": round(hi.y - lo.y, 2),
            "height": round(hi.z, 2), "below_ground": round(-min(lo.z, 0.0), 2),
            "triangles": tris, "fbx_kib": round(kib), "use": use})
    C.write_json("Scenery_catalog.json", {
        "notes": "Generated by blender/scripts/scenery.py. Sizes in metres, Unity axes. Origin = ground centre "
                 "of footprint (y 0). Fronts/doors face +Z. Single material: palette atlas (Textures/palette.png).",
        "pieces": catalog})
    C.save_blend("scenery")

    # previews 1-4: groups in a row on a grass strip, each with 1.8 m human boxes
    def group(fname, objs, y, humans, view=(0.25, -1.0, 0.42), margin=0.62):
        end = lay_row(objs, 6.0, y=y)
        hs = [C.human_box((o.location.x + dx, y + dy, 0)) for o, dx, dy in humans(objs)]
        g = C.Part("Ground_" + fname)
        g.add(C.bm_box_minmax((-30, y - 40, -0.4), (end + 30, y + 40, -0.3)), "foliage_lt", bevel=0)
        go = g.to_object()
        C.render_preview(fname, objs + hs, view_dir=view, res=(1600, 800), margin=margin)
        for o in objs + hs + [go]:
            o.hide_render = True

    big = [buildings[0], buildings[2], buildings[7], buildings[1]]
    group("scenery_buildings_a.png", big, 0, lambda o: [(o[0], 16, -23), (o[3], 0, -7)])
    group("scenery_buildings_b.png", [buildings[3], buildings[4], buildings[5], buildings[6]], 200,
          lambda o: [(o[0], 0, -9), (o[2], 0, -11), (o[3], 0, -15)])
    group("scenery_infra.png", infra, 400, lambda o: [(o[0], 3, -3), (o[4], 1.5, -2), (o[5], 0, -5)],
          view=(0.3, -1.0, 0.55))
    group("scenery_nature.png", nature, 600, lambda o: [(o[0], 3.5, -2), (o[4], 2, -1), (o[5], 2.5, -1)],
          view=(0.3, -1.0, 0.3), margin=0.7)

    # preview 3: example layout around a 40 x 40 m pad (Unity SceneBuilder does the real placement)
    rnd = random.Random(99)
    lay = []
    gp = C.Part("LayoutGround")
    gp.add(C.bm_box_minmax((-1200, -1200, -0.4), (1200, 1200, -0.3)), "foliage_lt", bevel=0)
    gp.add(C.bm_box_minmax((-20, -20, -0.3), (20, 20, 0.5)), "concrete_lt", bevel=0)
    lay.append(gp.to_object())
    B = {o.name.replace("Scenery_", ""): o for o in buildings + infra + nature}
    for name, x, y, r in (("Hangar", -420, 260, 0.3), ("Warehouse", -330, 380, 0.3), ("Office", -480, 120, 1.87),
                          ("Bunker", 180, -260, 0.6), ("ObsTower", 330, -150, 0.2), ("WaterTower", -560, -120, 0),
                          ("TankSphere", 420, 330, 0), ("TankPair", 470, 270, 0.7), ("TankSphere", 380, 395, 0),
                          ("RadarDish", 260, 420, 2.5), ("RadarDish", -250, -420, 0.8)):
        lay.append(place(B[name], (x, y, 0), r))
    for k in range(18):  # road from the pad towards the hangar (-X)
        lay.append(place(B["Road_Straight"], (-30 - 20 * k, 0, 0), math.pi / 2))
    for k in range(60):  # fence ring at 550 m (sparse in preview)
        a = 2 * math.pi * k / 60
        lay.append(place(B["Fence"], (560 * math.cos(a), 560 * math.sin(a), 0), a + math.pi / 2, 1.0))
    trees = [B["Conifer_A"], B["Conifer_B"], B["Broadleaf_A"], B["Broadleaf_B"], B["Bush"], B["Rocks"]]
    for c in range(14):
        a, d = rnd.uniform(0, 2 * math.pi), rnd.uniform(320, 750)
        cx, cy = d * math.cos(a), d * math.sin(a)
        for _ in range(rnd.randint(8, 18)):
            lay.append(place(rnd.choice(trees), (cx + rnd.gauss(0, 25), cy + rnd.gauss(0, 25), 0),
                             rnd.uniform(0, 6.28), rnd.uniform(0.8, 1.3)))
    C.render_preview("scenery_layout.png", lay, view_dir=(0.35, -1.0, 0.8), res=(1400, 900), lens=35,
                     target=(0, 0, 0), dist=1150)
    # booster's-eye view on final approach: ~250 m above the pad, looking at it obliquely
    C.render_preview("scenery_layout_approach.png", lay, view_dir=(-0.9, -1.0, 0.45), res=(1400, 900), lens=28,
                     target=(0, 0, 0), dist=520)
    print("pieces:", len(PIECES))


main()
