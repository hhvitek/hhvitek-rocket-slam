#!/usr/bin/env python3
"""Fetch the CC0 third-party assets used by Rocket Slam (see THIRD_PARTY.md).

Downloads from official sources only (kenney.nl, polyhaven.com, ambientcg.com) into a
fresh temp dir, then copies the selected files into unity/Assets/ThirdParty/.
Usage: python3 tools/fetch_assets.py
"""
import hashlib, http.cookiejar, json, shutil, tempfile, urllib.request, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TP = ROOT / "unity/Assets/ThirdParty"
UA = "Mozilla/5.0 (X11; Linux x86_64) Firefox/130.0"

# (kenney slug, [(path inside zip, dest name under ThirdParty/Kenney/<dir>)])
KENNEY = {
    "sci-fi-sounds": ("Audio/Sci-Fi", [
        ("Audio/thrusterFire_000.ogg", "engine_loop.ogg"),
        ("Audio/spaceEngineLow_000.ogg", "engine_low_loop.ogg"),
        ("Audio/explosionCrunch_003.ogg", "explosion_01.ogg"),
        ("Audio/explosionCrunch_004.ogg", "explosion_02.ogg"),
        ("Audio/lowFrequency_explosion_000.ogg", "explosion_rumble.ogg"),
        ("Audio/forceField_000.ogg", "forcefield_01.ogg"),
    ]),
    "impact-sounds": ("Audio/Impact", [
        ("Audio/impactMetal_heavy_001.ogg", "leg_clunk.ogg"),
        ("Audio/impactPlate_heavy_000.ogg", "landing_thud.ogg"),
        ("Audio/impactMetal_medium_000.ogg", "leg_latch.ogg"),
    ]),
    "interface-sounds": ("Audio/UI", [
        ("Audio/click_001.ogg", "ui_click.ogg"),
        ("Audio/confirmation_002.ogg", "ui_confirm.ogg"),
        ("Audio/back_002.ogg", "ui_back.ogg"),
        ("Audio/error_001.ogg", "ui_error.ogg"),
    ]),
    "particle-pack": ("Particles", [
        (f"PNG (Transparent)/{n}.png", f"{n}.png") for n in
        ("smoke_01", "smoke_04", "fire_01", "flame_01", "spark_01", "circle_05", "muzzle_01")
    ]),
}
POLYHAVEN = {  # id -> (url, md5)
    "kloofendal_43d_clear_puresky_2k.hdr": (
        "https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/2k/kloofendal_43d_clear_puresky_2k.hdr",
        "62f7cb7b1dac5a90a5bbde728606773b"),
    "dikhololo_night_2k.hdr": (
        "https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/2k/dikhololo_night_2k.hdr",
        "fcc8200f34360993e938d4012e2e446a"),
}
AMBIENTCG = {  # asset -> [(suffix kept, dest)]
    "Concrete034": ["Color", "NormalGL", "Roughness"],
    "MetalPlates006": ["Color", "NormalGL", "Roughness", "Metalness"],
    # terrain: Color/Roughness only (normal maps are ~2.4 MB each, skipped to keep the repo small)
    "Grass004": ["Color", "Roughness"],
    "Gravel023": ["Color", "Roughness"],
    "Asphalt012": ["Color"],
}
AMBIENTCG_MD5 = {  # md5 of the downloaded 1K-JPG zip (checked when listed)
    "Grass004": "ede3ecdf304e4c95212d7ec4157d2e74",
    "Gravel023": "4ab69f92b5130c457501e514a75330b0",
    "Asphalt012": "8685866031facb321ef6ff6b5e212516",
}


def kenney_download(slug, out):
    cj = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))
    page = f"https://kenney.nl/assets/{slug}"
    op.addheaders = [("User-Agent", UA)]
    op.open(page).read()  # sets session cookie
    op.addheaders = [("User-Agent", UA), ("Referer", page), ("Accept", "application/json")]
    link = json.load(op.open("https://kenney.nl/download-link/" + f"assets/{slug}".encode().hex()))["url"]
    op.addheaders = [("User-Agent", UA), ("Referer", page)]
    out.write_bytes(op.open(link).read())


def get(url, out):
    req = urllib.request.Request(url, headers={"User-Agent": "rocket-slam-asset-fetch"})
    out.write_bytes(urllib.request.urlopen(req).read())


def main():
    tmp = Path(tempfile.mkdtemp(prefix="rs_assets_"))
    for slug, (sub, files) in KENNEY.items():
        z = tmp / f"{slug}.zip"
        kenney_download(slug, z)
        dest = TP / "Kenney" / sub
        dest.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(z) as zf:
            for src, name in files:
                (dest / name).write_bytes(zf.read(src))
            if sub.startswith("Audio") or slug == "particle-pack":
                (dest / f"License_{slug}.txt").write_bytes(zf.read("License.txt"))
    dest = TP / "PolyHaven"
    dest.mkdir(parents=True, exist_ok=True)
    for name, (url, md5) in POLYHAVEN.items():
        get(url, dest / name)
        assert hashlib.md5((dest / name).read_bytes()).hexdigest() == md5, name
    for asset, maps in AMBIENTCG.items():
        z = tmp / f"{asset}.zip"
        get(f"https://ambientcg.com/get?file={asset}_1K-JPG.zip", z)
        if asset in AMBIENTCG_MD5:
            assert hashlib.md5(z.read_bytes()).hexdigest() == AMBIENTCG_MD5[asset], asset
        dest = TP / "ambientCG" / asset
        dest.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(z) as zf:
            for m in maps:
                (dest / f"{asset}_{m}.jpg").write_bytes(zf.read(f"{asset}_1K-JPG_{m}.jpg"))
    shutil.rmtree(tmp)
    print("done")


if __name__ == "__main__":
    main()
