"""Droneship (autonomous landing barge) generator -> unity/Assets/Models/Droneship.fbx, preview render.

Run: blender -b --factory-startup --python blender/scripts/droneship.py

ORIGIN = deck centre at deck height (Unity y 0 = deck top surface). Deck 90 m long (Unity X) x 50 m
wide (Unity Z). Hull bottom y -7, waterline y -3 (navy hull above, red antifouling below).
Stern at Unity -X (tall blast walls, containers, two thruster pods hanging off the hull sides),
bow at Unity +X. Parapets 1.2 m tall along both long edges. Deck paint (y up to +0.02): yellow ring
26 m diameter, white plus-cross, yellow bullseye, all centred on the origin.
Single object 'Droneship', single material 'Palette'.
"""
import math
import os
import sys

from mathutils import Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common as C  # noqa: E402

L, W = 90.0, 50.0
HL, HW = L / 2, W / 2
STERN = 1.0  # Blender +X = Unity -X


def hull_section(points_yz, x0, x1):
    """Prism along X from a (y, z) cross-section."""
    m = Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1)))  # (a, b, c) -> (c, a, b)
    return C.bm_polygon_prism(points_yz, x0, x1, matrix=m)


def main():
    C.reset_scene()
    p = C.Part("Droneship")
    # hull: upper (navy) and lower (antifouling) with a chine; bow/stern slightly raked below water
    p.add(hull_section([(-HW, -3.0), (HW, -3.0), (HW, -0.06), (-HW, -0.06)], -HL, HL), "hull", bevel=0.12)
    p.add(hull_section([(-HW + 2.0, -7.0), (HW - 2.0, -7.0), (HW, -4.6), (HW, -3.0), (-HW, -3.0), (-HW, -4.6)],
                       -HL + 1.5, HL - 1.5), "antifoul", bevel=0.1)
    # deck plates: 9 strips alternating shade
    for k in range(9):
        x0 = -HL + 10 * k
        p.add(C.bm_box_minmax((x0 + 0.03, -HW + 0.05, -0.08), (x0 + 9.97, HW - 0.05, 0.0)),
              "deck" if k % 2 == 0 else "deck_dk", bevel=0.02)
    # fender strips along the long sides
    for s in (-1, 1):
        y0 = s * HW
        p.add(C.bm_box_minmax((-HL + 2, min(y0, y0 + s * 0.4), -1.9), (HL - 2, max(y0, y0 + s * 0.4), -1.3)),
              "rubber", bevel=0.08)
    # deck markings (top at y +0.02)
    z0, z1 = 0.0, 0.02
    p.add(C.bm_lathe([(11.5, z0), (13.0, z0), (13.0, z1), (11.5, z1)], 64, closed=True), "yellow", bevel=0)
    p.add(C.bm_cylinder(2.0, z0, z1, 32), "yellow", bevel=0)
    w = 0.8
    for (a, b) in (((3.0, -w / 2), (10.5, w / 2)), ((-10.5, -w / 2), (-3.0, w / 2)),
                   ((-w / 2, 3.0), (w / 2, 10.5)), ((-w / 2, -10.5), (w / 2, -3.0))):
        p.add(C.bm_box_minmax((a[0], a[1], z0), (b[0], b[1], z1)), "paint_white", bevel=0)
    # safety border on deck
    e = 2.2
    for lo, hi in (((-HL + e, -HW + e), (HL - e, -HW + e + 0.4)), ((-HL + e, HW - e - 0.4), (HL - e, HW - e)),
                   ((-HL + e, -HW + e), (-HL + e + 0.4, HW - e)), ((HL - e - 0.4, -HW + e), (HL - e, HW - e))):
        p.add(C.bm_box_minmax((lo[0], lo[1], z0), (hi[0], hi[1], z1)), "paint_white", bevel=0)

    # parapets along the long edges (1.2 m), tall blast walls at the stern quarter (3.5 m)
    for s in (-1, 1):
        ya, yb = sorted((s * HW, s * (HW - 0.5)))
        p.add(C.bm_box_minmax((-HL, ya, -0.1), (HL - 22, yb, 1.2)), "wall", bevel=0.06)
        p.add(C.bm_box_minmax((HL - 22, ya, -0.1), (HL, yb, 3.5)), "wall", bevel=0.08)
        # wall stiffeners
        for x in range(-40, 23, 8):
            p.add(C.bm_box_minmax((x - 0.15, min(yb, ya) - 0.0 if s < 0 else ya - 0.25, -0.1),
                                  (x + 0.15, yb + 0.25 if s < 0 else yb, 1.2)), "deck_dk", bevel=0.03)
        for x in (25, 31, 37, 43):
            p.add(C.bm_box_minmax((x - 0.2, ya - (0 if s < 0 else 0.3), -0.1), (x + 0.2, yb + (0.3 if s < 0 else 0), 3.5)),
                  "deck_dk", bevel=0.03)
        # yellow cap stripe on the blast walls
        p.add(C.bm_box_minmax((HL - 22, ya - 0.02, 3.2), (HL, yb + 0.02, 3.5)), "yellow", bevel=0)
    # bollards
    for s in (-1, 1):
        for x in (-38, -22, -6, 10):
            y = s * (HW - 1.3)
            p.add(C.bm_cylinder(0.3, 0.0, 0.7, 10, matrix=Matrix.Translation((x, y, 0))), "graphite", bevel=0.03)
            p.add(C.bm_cylinder(0.42, 0.7, 0.82, 10, matrix=Matrix.Translation((x, y, 0))), "graphite", bevel=0.03)
    # containers (6.1 x 2.44 x 2.6) at the stern corners, plus one stacked
    for (x, y, sw, z) in ((STERN * 36.0, -19.5, "container_a", 0.0), (STERN * 36.0, 19.5, "container_b", 0.0),
                          (STERN * 36.0, 16.6, "container_a", 0.0), (STERN * 36.0, -19.5, "container_b", 2.6)):
        p.add(C.bm_box((6.1, 2.44, 2.6), (x, y, z + 1.3)), sw, bevel=0.06)
        for k in range(-2, 3):  # corrugation ribs
            p.add(C.bm_box((0.12, 2.5, 2.4), (x + k * 1.1, y, z + 1.3)), sw, bevel=0)
    # stern machinery house + mast with a lamp
    p.add(C.bm_box_minmax((STERN * 39.0, -6.0, 0.0), (STERN * 44.0, 6.0, 3.2)), "off_white", bevel=0.08)
    p.add(C.bm_box_minmax((STERN * 39.5, -6.3, 3.2), (STERN * 43.5, 6.3, 3.5)), "graphite", bevel=0.04)
    p.add(C.bm_box_minmax((STERN * 41.3, -0.2, 3.5), (STERN * 41.7, 0.2, 9.0)), "steel", bevel=0.03)
    p.add(C.bm_box_minmax((STERN * 41.1, -0.4, 9.0), (STERN * 41.9, 0.4, 9.6)), "lamp", bevel=0.04)
    # two thruster pods hanging off the hull sides near the stern
    for s in (-1, 1):
        y0 = s * HW
        ya, yb = sorted((y0, y0 + s * 3.0))
        p.add(C.bm_box_minmax((STERN * 26.0, ya, -6.0), (STERN * 38.0, yb, 0.4)), "hull", bevel=0.15)
        p.add(C.bm_box_minmax((STERN * 26.0, ya - 0.02, -6.0), (STERN * 38.0, yb + 0.02, -3.0)), "antifoul", bevel=0)
        p.add(C.bm_box_minmax((STERN * 28.0, ya + 0.3, 0.4), (STERN * 36.0, yb - 0.3, 1.6)), "graphite", bevel=0.1)
        rot = Matrix.Translation((STERN * 32.0, y0 + s * 1.5, -7.2)) @ Matrix.Rotation(math.radians(90), 4, "Y")
        p.add(C.bm_lathe([(1.0, -3.0), (1.3, -2.0), (1.3, 2.0), (1.0, 3.0)], 16, cap_start=True, cap_end=True,
                         matrix=rot), "graphite", bevel=0.05)
        p.add(C.bm_box_minmax((STERN * 31.4, y0 + s * 1.5 - 0.3, -6.3), (STERN * 32.6, y0 + s * 1.5 + 0.3, -5.9)),
              "steel", bevel=0.03)
    # deck edge lights at the corners and mid-sides
    for (x, y) in ((-HL + 1, -HW + 1), (-HL + 1, HW - 1), (0, -HW + 1), (0, HW - 1), (-HL + 1, 0)):
        p.add(C.bm_box_minmax((x - 0.25, y - 0.25, 0.0), (x + 0.25, y + 0.25, 0.25)), "light_case", bevel=0.03)
        p.add(C.bm_box_minmax((x - 0.15, y - 0.15, 0.25), (x + 0.15, y + 0.15, 0.4)), "lamp", bevel=0.02)

    ship = p.to_object()
    print("triangles:", C.tri_count(ship))
    C.print_bbox("droneship", [ship])
    C.export_fbx(ship, "Droneship.fbx")
    C.save_blend("droneship")
    human = C.human_box((0.0, -15.0, 0.0))
    C.render_preview("droneship.png", [ship, human], view_dir=(-0.9, -1.0, 0.6), res=(1400, 900), margin=0.72)
    C.render_preview("droneship_stern.png", [ship, human], view_dir=(1.0, -0.8, 0.35), res=(1400, 900), margin=0.72)


main()
