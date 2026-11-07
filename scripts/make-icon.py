#!/usr/bin/env python3
"""Draws MyFlow's app icon (white sound-wave bars on a blue-violet rounded square)
and writes assets/MyFlow.png at 1024x1024. Pure standard library, no Pillow needed.
scripts/package-macos.sh turns it into MyFlow.icns with sips + iconutil."""
import os
import struct
import zlib

SIZE = 1024
MARGIN = 100           # macOS icons leave transparent space around the tile
RADIUS = 185
TOP = (0x5B, 0x8C, 0xFF)     # gradient colours
BOTTOM = (0x7A, 0x4D, 0xF0)
BARS = [0.30, 0.62, 1.0, 0.62, 0.30]


def inside_round_rect(x, y, x0, y0, x1, y1, r):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def main():
    tile0, tile1 = MARGIN, SIZE - MARGIN
    bar_w = 70
    gap = 52
    total = len(BARS) * bar_w + (len(BARS) - 1) * gap
    bars = []
    x = (SIZE - total) / 2
    for h in BARS:
        height = 520 * h
        bars.append((x, (SIZE - height) / 2, x + bar_w, (SIZE + height) / 2))
        x += bar_w + gap

    rows = []
    for y in range(SIZE):
        t = (y - tile0) / (tile1 - tile0)
        t = min(max(t, 0.0), 1.0)
        bg = tuple(int(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3))
        row = bytearray([0])  # PNG filter type 0
        for x in range(SIZE):
            px, py = x + 0.5, y + 0.5
            if not inside_round_rect(px, py, tile0, tile0, tile1, tile1, RADIUS):
                row += b"\x00\x00\x00\x00"
                continue
            on_bar = any(inside_round_rect(px, py, b[0], b[1], b[2], b[3], bar_w / 2) for b in bars)
            row += bytes((255, 255, 255, 255)) if on_bar else bytes((*bg, 255))
        rows.append(bytes(row))

    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(b"".join(rows), 9))
    png += chunk(b"IEND", b"")

    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "MyFlow.png")
    with open(out, "wb") as f:
        f.write(png)
    print("wrote", os.path.normpath(out))


if __name__ == "__main__":
    main()
