"""Deterministic nine-grid shadow assets; standard library only, no UI access."""
import argparse
import math
import os
from pathlib import Path
import struct
import zlib

ROOT = Path(os.path.abspath(__file__)).parents[1]


def make_shadow(scale):
    size = 96 * scale
    pad, radius, shift = 12 * scale, 8 * scale, 3 * scale
    sigma = 3 * scale
    support = int(3 * sigma)
    weights = [math.exp(-i * i / (2.0 * sigma * sigma)) for i in range(-support, support + 1)]
    total = sum(weights)
    weights = [v / total for v in weights]

    def inside(x, y):
        cx = max(pad + radius, min(size - pad - radius, x))
        cy = max(pad + radius, min(size - pad - radius, y))
        return (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2

    mask = [[float(inside(x + .5, y + .5 - shift)) for x in range(size)] for y in range(size)]
    horizontal = [[sum(mask[y][x + i] * weights[i + support] for i in range(-support, support + 1)
                       if 0 <= x + i < size) for x in range(size)] for y in range(size)]
    alpha = []
    rows = []
    for y in range(size):
        row = bytearray([0])  # PNG filter: None
        alpha_row = []
        for x in range(size):
            value = 0 if inside(x + .5, y + .5) else round(255 * sum(
                horizontal[y + i][x] * weights[i + support] for i in range(-support, support + 1) if 0 <= y + i < size))
            row.extend((0, 0, 0, value))
            alpha_row.append(value)
        alpha.append(alpha_row)
        rows.append(bytes(row))
    # The center never paints below acrylic; every exterior side has shadow.
    assert all(alpha[y][x] == 0 for y in range(32 * scale, 64 * scale) for x in range(32 * scale, 64 * scale))
    assert alpha[11 * scale][48 * scale] > 0 and alpha[84 * scale][48 * scale] > 0
    assert alpha[48 * scale][11 * scale] > 0 and alpha[48 * scale][84 * scale] > 0

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)

    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(b''.join(rows), 9)) + chunk(b'IEND', b''))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    for scale, name in ((1, 'PopupShadow.png'), (2, 'PopupShadow.scale-200.png'), (3, 'PopupShadow.scale-300.png')):
        target = ROOT / 'FreeClipControl.Shell' / 'Assets' / name
        content = make_shadow(scale)
        if args.check:
            if not target.is_file() or target.read_bytes() != content:
                raise SystemExit('Shadow asset missing or changed: ' + str(target))
        else:
            target.write_bytes(content)
    print('Shadow assets verified: transparent center and continuous shadow on all four sides at 100/200/300% scale.')


if __name__ == '__main__':
    main()
