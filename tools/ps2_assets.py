#!/usr/bin/env python3
"""Build the PS2 theme's font and menu sounds from YOUR OWN PS2 BIOS dump.

Nothing from Sony's BIOS is stored in this repository. This tool reads a dump you made from
your own console (tested with an NTSC SCPH-39001) and writes the generated files into the
ignored launcher/assets/ps2/font/ and launcher/assets/ps2/sfx/ folders.

  python3 tools/ps2_assets.py all /path/to/your-bios.bin
  python3 tools/ps2_assets.py extract BIOS OUTDIR      # unpack TEXIMAGE/SNDIMAGE/ICOIMAGE/FNTIMAGE
  python3 tools/ps2_assets.py font OUTDIR/FNTIMAGE/FNTASCII.bin
  python3 tools/ps2_assets.py sounds OUTDIR/SNDIMAGE/SNDOSDDB.bin

Requires numpy and Pillow. The videos and the background ambience are recordings you make
yourself in PCSX2; see docs/10-ps2-theme.md.
"""
import math
import os
import struct
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "launcher/assets/ps2"

# ---------------------------------------------------------------- OSD resource unpacking
def unpack(b):
    """u32 LE unpacked size, then blocks: u32 BE control word (top 30 bits are flags, MSB first:
    0 = literal byte, 1 = 16-bit BE back-reference; low 2 bits = shift)."""
    target = struct.unpack_from("<I", b, 0)[0]
    out = bytearray(); p = 4
    while len(out) < target:
        c = struct.unpack_from(">I", b, p)[0]; p += 4
        obits = 14 - (c & 3); mask = (1 << obits) - 1
        for i in range(30):
            if len(out) >= target:
                break
            if not (c >> (31 - i)) & 1:
                out.append(b[p]); p += 1
            else:
                t = struct.unpack_from(">H", b, p)[0]; p += 2
                off = (t & mask) + 1
                for _ in range((t >> obits) + 3):
                    out.append(out[-off])
    if len(out) != target:
        raise ValueError("size mismatch")
    return bytes(out)


def romdir(buf):
    i = buf.find(b"RESET\0\0\0\0\0")
    if i < 0:
        raise ValueError("no ROMDIR found; is this a PS2 BIOS dump?")
    off = 0; res = []; k = 0
    while True:
        name, _ext, size = struct.unpack_from("<10sHI", buf, i + k * 16); k += 1
        name = name.rstrip(b"\0").decode("ascii", "replace")
        if not name:
            return res
        res.append((name, off, size)); off += (size + 15) & ~15


def extract(bios, outdir):
    data = Path(bios).read_bytes()
    top = {n: (o, s) for n, o, s in romdir(data)}
    ok = bad = 0
    for img in ("SNDIMAGE", "TEXIMAGE", "ICOIMAGE", "FNTIMAGE"):
        o, s = top[img]; sub = data[o:o + s]
        (Path(outdir) / img).mkdir(parents=True, exist_ok=True)
        for n, so, ss in romdir(sub):
            if n in ("RESET", "ROMDIR", "EXTINFO", "-"):
                continue
            try:
                (Path(outdir) / img / f"{n}.bin").write_bytes(unpack(sub[so:so + ss])); ok += 1
            except Exception as e:  # keep going; report what failed
                print("FAILED", img, n, e); bad += 1
    print(f"unpacked {ok}, failed {bad} -> {outdir}")

# ---------------------------------------------------------------- system font
# FNTASCII: 256x480 at 4 bits per pixel, 8x12 cells of 32x40, ASCII 0x20..0x7f.
# Levels: 0 empty, 1-4 the dark outline ramp, 8-15 the white body.
CW, CH, UP = 32, 40, 8
SCALES = [0.6, 0.62, 0.7, 0.8, 0.85, 1.0, 1.15, 1.3, 1.5]   # every size the launcher draws
RIM = (6, 10, 28)


def font(src):
    import numpy as np
    from PIL import Image
    b = Path(src).read_bytes(); nib = []
    for x in b:
        nib += [x & 15, x >> 4]
    L = np.array(nib[:256 * 480], np.float32).reshape(480, 256)
    lum = Image.new("L", (256, 480)); lum.putdata([int(v) * 17 for v in nib[:256 * 480]])
    left, width = [], []
    for i in range(96):
        x, y = (i % 8) * CW, (i // 8) * CH
        bb = lum.crop((x, y, x + CW, y + CH)).getbbox()
        if i == 0 or not bb:
            left.append(0); width.append(16 if i == 0 else 10)
        else:
            left.append(bb[0]); width.append(bb[2] - bb[0])

    def hires(i):   # reconstruct the 4-bit glyph at 8x, cut body and rim at their edge midpoints
        gx, gy = (i % 8) * CW, (i // 8) * CH
        pad = np.pad(L[gy:gy + CH, gx:gx + CW], 2)
        up = np.array(Image.fromarray(pad).resize(((CW + 4) * UP, (CH + 4) * UP), Image.BICUBIC))
        up = up[2 * UP:(CH + 2) * UP, 2 * UP:(CW + 2) * UP]
        body = np.clip((up - 9.5) / 0.9 + 0.5, 0, 1)
        rim = np.clip((up - 2.0) / 0.9 + 0.5, 0, 1)
        return body, np.maximum(rim, body)

    def area(a, w, h):
        return np.array(Image.fromarray(a.astype(np.float32)).resize((w, h), Image.BOX))

    HI = [hires(i) for i in range(96)]
    out_dir = ASSETS / "font"; out_dir.mkdir(parents=True, exist_ok=True)
    atlases = {}
    for s in SCALES:
        key = int(round(s * 100)); ch = math.ceil(CH * s); cw = math.ceil(CW * s) + 2
        W, H = 8 * cw, 12 * ch
        rgba = np.zeros((H, W, 4), np.float32); rects = []
        for i in range(95):
            lft, w = left[i], width[i]
            gw = max(1, math.ceil(w * s))
            x0, y0 = (i % 8) * cw + 1, (i // 8) * ch
            if i == 0:
                rects += [x0, y0, gw, ch]; continue
            body, cover = HI[i]
            bb = body[:, lft * UP:(lft + w) * UP]; cc = cover[:, lft * UP:(lft + w) * UP]
            tw, th = gw * UP, ch * UP
            bb = np.array(Image.fromarray(bb).resize((tw, th), Image.BILINEAR))
            cc = np.array(Image.fromarray(cc).resize((tw, th), Image.BILINEAR))
            a_body, a_cov = area(bb, gw, ch), area(cc, gw, ch)
            alpha = np.clip(a_body + (a_cov - a_body) * 0.9, 0, 1)
            col = np.zeros((ch, gw, 3), np.float32)
            for k in range(3):   # premultiplied mix of white body and dark rim, then un-premultiply
                pm = a_body * 255 + (a_cov - a_body) * 0.9 * RIM[k]
                col[..., k] = np.where(alpha > 0, pm / np.maximum(alpha, 1e-6), 0)
            rgba[y0:y0 + ch, x0:x0 + gw, :3] = col; rgba[y0:y0 + ch, x0:x0 + gw, 3] = alpha * 255
            rects += [x0, y0, gw, ch]
        Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), "RGBA").save(out_dir / f"ps2font_{key}.png", optimize=True)
        atlases[key] = (W, H, rects)
    cs = ["// Generated by tools/ps2_assets.py font from the owner's own BIOS FNTASCII (ASCII 0x20..0x7E).",
          "// One atlas per on-screen size; Rects = x,y,w,h per glyph in that atlas (drawn 1:1).",
          "using System.Collections.Generic;", "static class Ps2FontMetrics {",
          " public static readonly byte[] Width = {" + ",".join(map(str, width[:95])) + "};",
          " public static readonly Dictionary<int, (int W, int H, short[] Rects)> Atlases = new Dictionary<int, (int, int, short[])> {"]
    for k, (W, H, r) in atlases.items():
        cs.append(f"  [{k}] = ({W}, {H}, new short[] {{" + ",".join(map(str, r)) + "}),")
    cs += [" };", "}"]
    (ROOT / "launcher/src/Ps2FontMetrics.cs").write_text("\n".join(cs) + "\n")
    print("font atlases:", {k: (v[0], v[1]) for k, v in atlases.items()})

# ---------------------------------------------------------------- menu sounds
COEF = [(0, 0), (60, 0), (115, -52), (98, -55), (122, -60)]
# SNDOSDDB sample index -> launcher sound, and its level relative to the BIOS original.
# These levels sit the clicks just under the menu ambience, as tuned on the S90D.
SOUNDS = {6: ("cursor", -16.5), 8: ("select", -14.5), 12: ("back", -14.5)}
RATE = 22050


def _decode(frames):
    s1 = s2 = 0; pcm = []
    for f in frames:
        shift, filt = f[0] & 0x0F, (f[0] >> 4) & 0x0F
        c1, c2 = COEF[filt if filt <= 4 else 0]
        for byte in f[2:16]:
            for nib in (byte & 0x0F, byte >> 4):
                v = nib - 16 if nib >= 8 else nib
                v = (v << 12) >> shift
                v += (s1 * c1 + s2 * c2 + 32) >> 6
                v = max(-32768, min(32767, v))
                pcm.append(v); s2, s1 = s1, v
    return pcm


def _split(body):
    samples, cur = [], []
    for i in range(0, len(body) - 15, 16):
        f = body[i:i + 16]
        if f == b"\0" * 16 and not cur:
            continue
        cur.append(f)
        if f[1] & 1:
            samples.append(cur); cur = []
    if cur:
        samples.append(cur)
    return samples


def sounds(src):
    out_dir = ASSETS / "sfx"; out_dir.mkdir(parents=True, exist_ok=True)
    samples = _split(Path(src).read_bytes())
    for idx, (name, gain_db) in SOUNDS.items():
        g = 10 ** (gain_db / 20)
        pcm = [max(-32768, min(32767, int(round(v * g)))) for v in _decode(samples[idx])]
        with wave.open(str(out_dir / f"{name}.wav"), "wb") as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
            w.writeframes(struct.pack(f"<{len(pcm)}h", *pcm))
        print(f"{name}.wav  {len(pcm) / RATE:.3f}s  {gain_db:+.1f} dB")


def main(argv):
    if len(argv) < 3:
        sys.exit(__doc__)
    cmd = argv[1]
    if cmd == "extract" and len(argv) == 4:
        extract(argv[2], argv[3])
    elif cmd == "font":
        font(argv[2])
    elif cmd == "sounds":
        sounds(argv[2])
    elif cmd == "all":
        tmp = ROOT / ".local/ps2-bios"
        extract(argv[2], tmp)
        font(tmp / "FNTIMAGE/FNTASCII.bin")
        sounds(tmp / "SNDIMAGE/SNDOSDDB.bin")
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv)
