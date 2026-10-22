"""Makes aperture.ico (Windows) and Aperture.icns (macOS) from the app icon, icon-512.png, with the standard library only:

    python3 deploy/icons/make_icons.py src/Aperture.Web/wwwroot/icon-512.png deploy/icons/aperture.ico deploy/icons/Aperture.icns

Both formats can hold PNG images, so the work is decoding the 8-bit gray+alpha PNG, scaling it down by averaging
(premultiplied by alpha, so edges stay clean), and encoding the sizes again as RGBA.
"""
import struct, sys, zlib

def read_png(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    pos, idat, header = 8, b"", None
    while pos < len(data):
        length, kind = struct.unpack(">I4s", data[pos:pos + 8])
        chunk = data[pos + 8:pos + 8 + length]
        if kind == b"IHDR":
            header = struct.unpack(">IIBBBBB", chunk)
        elif kind == b"IDAT":
            idat += chunk
        pos += 12 + length
    width, height, depth, color, _, _, interlace = header
    assert depth == 8 and color == 4 and interlace == 0, header  # gray + alpha
    raw, bpp, stride = zlib.decompress(idat), 2, width * 2
    rows, prev, i = [], bytearray(stride), 0
    for _ in range(height):
        kind, line = raw[i], bytearray(raw[i + 1:i + 1 + stride])
        i += 1 + stride
        for x in range(stride):
            a = line[x - bpp] if x >= bpp else 0
            b = prev[x]
            c = prev[x - bpp] if x >= bpp else 0
            if kind == 1: line[x] = (line[x] + a) & 255
            elif kind == 2: line[x] = (line[x] + b) & 255
            elif kind == 3: line[x] = (line[x] + (a + b) // 2) & 255
            elif kind == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line)
        prev = line
    return width, height, rows

def scale(width, rows, size):
    """Area-average down to size x size; gray premultiplied by alpha."""
    out, f = [], width / size
    for y in range(size):
        line = bytearray()
        y0, y1 = int(y * f), max(int(y * f) + 1, int((y + 1) * f))
        for x in range(size):
            x0, x1 = int(x * f), max(int(x * f) + 1, int((x + 1) * f))
            g = a = n = 0
            for yy in range(y0, y1):
                row = rows[yy]
                for xx in range(x0, x1):
                    alpha = row[xx * 2 + 1]
                    g += row[xx * 2] * alpha
                    a += alpha
                    n += 1
            line += bytes((round(g / a) if a else 0, round(a / n)))
        out.append(line)
    return out

def write_png(size, rows):
    """RGBA (color type 6): what Windows expects of PNG images inside icons."""
    rgba = [b"".join(bytes((r[i], r[i], r[i], r[i + 1])) for i in range(0, len(r), 2)) for r in rows]
    raw = b"".join(b"\x00" + r for r in rgba)
    chunk = lambda kind, body: struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))

source, ico_path, icns_path = sys.argv[1:4]
width, height, rows = read_png(source)
assert width == height == 512
pngs = {size: write_png(size, rows if size == 512 else scale(width, rows, size)) for size in (16, 32, 48, 64, 128, 256, 512)}

# ICO: a directory of PNG images (Windows Vista and later). A size byte of 0 means 256.
sizes = (16, 32, 48, 256)
offset = 6 + 16 * len(sizes)
ico = struct.pack("<HHH", 0, 1, len(sizes))
for size in sizes:
    ico += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(pngs[size]), offset)
    offset += len(pngs[size])
ico += b"".join(pngs[size] for size in sizes)
open(ico_path, "wb").write(ico)

# ICNS: typed PNG entries (icp4 16, icp5 32, icp6 64, ic07 128, ic08 256, ic09 512).
entries = b"".join(kind + struct.pack(">I", 8 + len(pngs[size])) + pngs[size]
                   for kind, size in ((b"icp4", 16), (b"icp5", 32), (b"icp6", 64), (b"ic07", 128), (b"ic08", 256), (b"ic09", 512)))
open(icns_path, "wb").write(b"icns" + struct.pack(">I", 8 + len(entries)) + entries)
print("corner pixel (gray, alpha):", tuple(rows[0][0:2]), "| ico", len(ico), "bytes | icns", 8 + len(entries), "bytes")
