# Builds src/FinanceApp.Desktop/Assets/FinanceApp.ico from the SVGs in Assets/IconSource: 16 px from the 16 px
# drawing, 24 and 32 from the 32 px one, the rest from 256. Needs Pillow; supports exactly what the drawings use
# (rect with rx, circles with fill or stroke, paths of M/L/V/H/A/Z), drawn 16× larger and scaled down.
# Usage: python tools/svg-to-ico.py <16.svg> <32.svg> <256.svg> <out.ico> <preview.png>
import math
import re
import sys
import xml.etree.ElementTree as ET
from PIL import Image, ImageDraw

SS = 16  # supersampling factor


def color(value, opacity=1.0):
    value = value.lstrip('#')
    r, g, b = (int(value[i:i + 2], 16) for i in (0, 2, 4))
    return (r, g, b, int(round(255 * opacity)))


def arc_points(x1, y1, rx, ry, phi, large, sweep, x2, y2, steps=48):
    # SVG endpoint → center parameterization (rotation 0 in these files).
    dx, dy = (x1 - x2) / 2, (y1 - y2) / 2
    rx, ry = abs(rx), abs(ry)
    lam = dx * dx / (rx * rx) + dy * dy / (ry * ry)
    if lam > 1:
        rx, ry = rx * math.sqrt(lam), ry * math.sqrt(lam)
    num = rx * rx * ry * ry - rx * rx * dy * dy - ry * ry * dx * dx
    den = rx * rx * dy * dy + ry * ry * dx * dx
    coef = math.sqrt(max(num / den, 0)) * (-1 if large == sweep else 1)
    cxp, cyp = coef * rx * dy / ry, -coef * ry * dx / rx
    cx, cy = cxp + (x1 + x2) / 2, cyp + (y1 + y2) / 2

    def angle(ux, uy, vx, vy):
        a = math.atan2(ux * vy - uy * vx, ux * vx + uy * vy)
        return a

    t1 = angle(1, 0, (dx - cxp) / rx, (dy - cyp) / ry)
    dt = angle((dx - cxp) / rx, (dy - cyp) / ry, (-dx - cxp) / rx, (-dy - cyp) / ry)
    if not sweep and dt > 0:
        dt -= 2 * math.pi
    elif sweep and dt < 0:
        dt += 2 * math.pi
    return [(cx + rx * math.cos(t1 + dt * i / steps), cy + ry * math.sin(t1 + dt * i / steps)) for i in range(1, steps + 1)]


def path_points(d):
    tokens = re.findall(r'[MLVHAZ]|-?\d*\.?\d+', d)
    pts, i, cmd = [], 0, None
    x = y = 0.0
    while i < len(tokens):
        t = tokens[i]
        if t.isalpha():
            cmd = t
            i += 1
            if cmd == 'Z':
                continue
        if cmd in 'ML':
            x, y = float(tokens[i]), float(tokens[i + 1])
            i += 2
            pts.append((x, y))
        elif cmd == 'V':
            y = float(tokens[i])
            i += 1
            pts.append((x, y))
        elif cmd == 'H':
            x = float(tokens[i])
            i += 1
            pts.append((x, y))
        elif cmd == 'A':
            rx, ry, phi, large, sweep, nx, ny = (float(v) for v in tokens[i:i + 7])
            i += 7
            pts += arc_points(x, y, rx, ry, phi, int(large), int(sweep), nx, ny)
            x, y = nx, ny
    return pts


def render(svg_path, size):
    root = ET.parse(svg_path).getroot()
    vb = [float(v) for v in root.get('viewBox').split()]
    k = size * SS / vb[2]
    img = Image.new('RGBA', (size * SS, size * SS), (0, 0, 0, 0))
    for el in root:
        tag = el.tag.split('}')[-1]
        if tag == 'rect':
            x, y, w, h = (float(el.get(a, 0)) * k for a in ('x', 'y', 'width', 'height'))
            layer = Image.new('RGBA', img.size, (0, 0, 0, 0))
            ImageDraw.Draw(layer).rounded_rectangle([x, y, x + w - 1, y + h - 1], radius=float(el.get('rx', 0)) * k, fill=color(el.get('fill')))
            img = Image.alpha_composite(img, layer)
        elif tag == 'circle':
            cx, cy, r = (float(el.get(a)) * k for a in ('cx', 'cy', 'r'))
            layer = Image.new('RGBA', img.size, (0, 0, 0, 0))
            draw = ImageDraw.Draw(layer)
            if el.get('fill', 'black') != 'none':
                draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color(el.get('fill')))
            if el.get('stroke'):
                sw = float(el.get('stroke-width', 1)) * k
                draw.ellipse([cx - r - sw / 2, cy - r - sw / 2, cx + r + sw / 2, cy + r + sw / 2],
                             outline=color(el.get('stroke'), float(el.get('stroke-opacity', 1))), width=int(round(sw)))
            img = Image.alpha_composite(img, layer)
        elif tag == 'path':
            pts = [(px * k, py * k) for px, py in path_points(el.get('d'))]
            layer = Image.new('RGBA', img.size, (0, 0, 0, 0))
            ImageDraw.Draw(layer).polygon(pts, fill=color(el.get('fill')))
            img = Image.alpha_composite(img, layer)
    return img.resize((size, size), Image.LANCZOS)


svg16, svg32, svg256, out_ico, preview = sys.argv[1:6]
sources = {16: svg16, 24: svg32, 32: svg32, 48: svg256, 64: svg256, 128: svg256, 256: svg256}
frames = {s: render(src, s) for s, src in sources.items()}
order = [256, 128, 64, 48, 32, 24, 16]
frames[256].save(out_ico, format='ICO', sizes=[(s, s) for s in order], append_images=[frames[s] for s in order[1:]])

sheet = Image.new('RGBA', (256 + 16 + 4 * 72, 256), (11, 11, 11, 255))
sheet.alpha_composite(frames[256], (0, 0))
x = 272
for s in (48, 32, 24, 16):
    sheet.alpha_composite(frames[s], (x, 10))
    big = frames[s].resize((s * 2, s * 2), Image.NEAREST)
    sheet.alpha_composite(big, (x, 80))
    x += 72
sheet.save(preview)
print('ok')
