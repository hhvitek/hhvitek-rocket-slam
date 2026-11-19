"""Landing pad generator -> unity/Assets/Models/LandingPad.fbx, preview render.

Run: blender -b --factory-startup --python blender/scripts/landing_pad.py

40 x 40 m concrete slab (SceneBuilder PadSize 40, PadThickness 0.5). ORIGIN = centre of the slab's
bottom face (ground level, Unity y 0). Slab + panels top at y 0.50 (= game pad collider top), painted
markings and scorch are thin overlays up to y 0.52, edge lights stick up to y 0.75.
Marking (our own design): yellow ring 18 m diameter (as the game's TargetRing), white plus-cross,
yellow 3 m bullseye (as TargetBullseye), white border line, dark scorch patch. 32 edge lights (lamp
swatch; usable as emission with the palette atlas as emission map).
Single object 'LandingPad', single material 'Palette'.
"""
import math
import os
import random
import sys

from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common as C  # noqa: E402

SIZE = 40.0
H = 0.5


def ring(r0, r1, z0, z1, seg=64):
    return C.bm_lathe([(r0, z0), (r1, z0), (r1, z1), (r0, z1)], seg, closed=True)


def main():
    C.reset_scene()
    p = C.Part("LandingPad")
    hs = SIZE / 2
    # slab body (bevelled concrete edge), top at 0.48 under the panels
    p.add(C.bm_box_minmax((-hs, -hs, 0.0), (hs, hs, H - 0.02)), "concrete_dk", bevel=0.18)
    # 4 x 4 panels, 10 m each, seams 8 cm
    for i in range(4):
        for j in range(4):
            x0, y0 = -hs + 10 * i + 0.04, -hs + 10 * j + 0.04
            sw = "concrete_lt" if (i + j) % 2 == 0 else "concrete"
            p.add(C.bm_box_minmax((x0 + (0.1 if i == 0 else 0), y0 + (0.1 if j == 0 else 0), H - 0.03),
                                  (x0 + 9.92 - (0.1 if i == 3 else 0), y0 + 9.92 - (0.1 if j == 3 else 0), H)),
                  sw, bevel=0.02)
    # scorch patch: irregular 24-gon
    rnd = random.Random(7)
    pts = [(r * math.cos(a), r * math.sin(a)) for a, r in
           ((2 * math.pi * k / 24, rnd.uniform(4.8, 6.0)) for k in range(24))]
    p.add(C.bm_polygon_prism(pts, H - 0.005, H + 0.005), "scorch", bevel=0)
    # markings (top at 0.52)
    z0, z1 = H + 0.005, H + 0.02
    p.add(ring(7.5, 9.0, z0, z1), "yellow", bevel=0)
    p.add(C.bm_cylinder(1.5, z0, z1, 32), "yellow", bevel=0)
    w = 0.6
    for (a, b) in (((2.2, -w / 2), (6.8, w / 2)), ((-6.8, -w / 2), (-2.2, w / 2)),
                   ((-w / 2, 2.2), (w / 2, 6.8)), ((-w / 2, -6.8), (w / 2, -2.2))):
        p.add(C.bm_box_minmax((a[0], a[1], z0), (b[0], b[1], z1)), "paint_white", bevel=0)
    # outer ticks beyond the ring
    for (a, b) in (((9.6, -w / 2), (12.0, w / 2)), ((-12.0, -w / 2), (-9.6, w / 2)),
                   ((-w / 2, 9.6), (w / 2, 12.0)), ((-w / 2, -12.0), (w / 2, -9.6))):
        p.add(C.bm_box_minmax((a[0], a[1], z0), (b[0], b[1], z1)), "paint_white", bevel=0)
    # border line 1 m inside the edge, 0.4 m wide
    e, lw = hs - 1.0, 0.4
    for lo, hi in (((-e, -e), (e, -e + lw)), ((-e, e - lw), (e, e)), ((-e, -e + lw), (-e + lw, e - lw)),
                   ((e - lw, -e + lw), (e, e - lw))):
        p.add(C.bm_box_minmax((lo[0], lo[1], z0), (hi[0], hi[1], z1)), "paint_white", bevel=0)
    # edge lights every 5 m, 0.35 m inside the edge
    inset = 0.45
    spots = []
    for k in range(8):
        t = -hs + 2.5 + 5 * k
        spots += [(t, -hs + inset), (t, hs - inset), (-hs + inset, t), (hs - inset, t)]
    for (x, y) in spots:
        p.add(C.bm_box_minmax((x - 0.2, y - 0.2, H - 0.01), (x + 0.2, y + 0.2, H + 0.13)), "light_case", bevel=0.03)
        p.add(C.bm_box_minmax((x - 0.13, y - 0.13, H + 0.13), (x + 0.13, y + 0.13, H + 0.25)), "lamp", bevel=0.02)
    pad = p.to_object()
    print("triangles:", C.tri_count(pad), " lights:", len(spots))
    C.print_bbox("landing pad", [pad])
    C.export_fbx(pad, "LandingPad.fbx")
    C.save_blend("landing_pad")
    human = C.human_box((3.0, -10.5, H))
    C.render_preview("landing_pad.png", [pad, human], view_dir=(0.8, -1.0, 0.75), res=(1400, 900), margin=0.75)
    C.render_preview("landing_pad_detail.png", [pad, human], view_dir=(0.6, -1.0, 0.35), res=(1200, 800),
                     target=(4.0, -12.0, 0.5), dist=14)


main()
