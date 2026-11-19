"""Shared palette atlas for every Rocket Slam model.

Writes unity/Assets/Textures/palette.png: 256x256 px, an 8x8 grid of 32x32 px flat swatches.
Every model UV-maps each face onto the centre of one swatch, so one URP Lit material with this
texture as Base Map colours everything. Unity import: sRGB on, Filter Point (or Bilinear),
Compression None, mipmaps fine (UVs sit in swatch centres).

Swatch index = row * 8 + col, row 0 is the TOP row of the image.
Swatch -> colour mapping: SWATCHES below (index, name, sRGB hex, usage). Unused cells are magenta.
"""
import os

GRID = 8          # swatches per row/column
CELL = 32         # px per swatch
SIZE = GRID * CELL

# name -> sRGB hex. Order defines the swatch index (row-major, top-left first).
SWATCHES = [
    # row 0: booster livery
    ("white",        "#E2E4E6"),  # 0  main body (max 0.79 linear: brighter clips to pure white under ACES in full sun)
    ("off_white",    "#D3D6DA"),  # 1  panel accents, leg fairing
    ("soot_light",   "#A9A9A6"),  # 2  soot gradient, top band
    ("soot_mid",     "#6E6B66"),  # 3  soot gradient
    ("soot_dark",    "#3A3835"),  # 4  soot gradient, bottom band
    ("carbon",       "#1E2022"),  # 5  interstage, legs, engine section
    ("graphite",     "#2E3236"),  # 6  grid fins, mechanisms
    ("steel",        "#8C949C"),  # 7  bare metal, hinges
    # row 1: engine + accents
    ("nozzle_out",   "#4A3F38"),  # 8  nozzle exterior (heat tinted)
    ("nozzle_in",    "#15110F"),  # 9  nozzle interior
    ("copper",       "#A0603A"),  # 10 engine manifold, throat
    ("orange",       "#F06A1A"),  # 11 accent stripe
    ("yellow",       "#F4C21B"),  # 12 accent, pad marking
    ("text_dark",    "#24272B"),  # 13 painted lettering
    ("red",          "#C42A22"),  # 14 warning paint
    ("lamp",         "#FFD27A"),  # 15 lights (use as emission too)
    # row 2: concrete / pad
    ("concrete_lt",  "#B8B5AE"),  # 16 pad panels
    ("concrete",     "#A29F98"),  # 17 pad panels
    ("concrete_dk",  "#7D7A74"),  # 18 pad edge / skirt
    ("scorch",       "#4C4844"),  # 19 burn marks
    ("paint_white",  "#F2F2EE"),  # 20 pad marking
    ("asphalt",      "#3F4144"),  # 21 aprons
    ("light_case",   "#2A2C2F"),  # 22 light housings
    ("lamp_green",   "#7CFF8A"),  # 23 green lights
    # row 3: ship
    ("deck",         "#5B5F63"),  # 24 deck steel
    ("deck_dk",      "#44484C"),  # 25 deck plates alt
    ("hull",         "#2F3A45"),  # 26 hull sides (navy grey)
    ("antifoul",     "#8E2A22"),  # 27 below-waterline paint
    ("wall",         "#6F757B"),  # 28 blast walls
    ("container_a",  "#2F6FA8"),  # 29 container blue
    ("container_b",  "#C8582A"),  # 30 container orange
    ("rubber",       "#141516"),  # 31 fenders, tyres
    # row 4: scenery (nature)
    ("foliage_dk",   "#34502A"),  # 32 conifers, bushes
    ("foliage",      "#4F7334"),  # 33 broadleaf crowns
    ("foliage_lt",   "#7A9447"),  # 34 light crowns, grass
    ("bark",         "#5A4130"),  # 35 tree trunks
    ("rock",         "#8B867D"),  # 36 rocks
    ("rock_dk",      "#605B54"),  # 37 rocks, shaded
    ("cladding",     "#9EA9B3"),  # 38 metal wall siding (light blue-grey)
    ("cladding_dk",  "#5D6B78"),  # 39 siding accents, parapets
    # row 5: scenery (buildings)
    ("glass",        "#2D4B60"),  # 40 windows
    ("roof",         "#4A4E53"),  # 41 roofs
    ("facade",       "#C8BEAD"),  # 42 office render / sand facade
    ("tank_white",   "#E4E6E2"),  # 43 storage tanks
]
INDEX = {name: i for i, (name, _) in enumerate(SWATCHES)}


def hex_to_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def uv_of(name):
    """UV (u, v) of the centre of a swatch; v=1 is the top of the image (Blender/Unity convention)."""
    i = INDEX[name]
    row, col = divmod(i, GRID)
    u = (col + 0.5) / GRID
    v = 1.0 - (row + 0.5) / GRID
    return (u, v)


def write_png(path):
    """Write the atlas without bpy (pure Python PNG writer) so it also runs outside Blender."""
    import struct
    import zlib
    rows = []
    fill = hex_to_rgb("#FF00FF")  # unused cells: magenta, so a wrong UV is obvious
    for y in range(SIZE):
        row = bytearray([0])
        r = y // CELL
        for x in range(SIZE):
            i = r * GRID + x // CELL
            rgb = hex_to_rgb(SWATCHES[i][1]) if i < len(SWATCHES) else fill
            row += bytes(int(round(c * 255)) for c in rgb)
        rows.append(bytes(row))
    raw = b"".join(rows)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(png)
    return path


REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PALETTE_PNG = os.path.join(REPO, "unity", "Assets", "Textures", "palette.png")

if __name__ == "__main__":
    print("palette written:", write_png(PALETTE_PNG))
