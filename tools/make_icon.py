# usage: py tools/make_icon.py
# Writes the launcher icon (adaptive icon: background, foreground, monochrome) as vector drawables,
# and the same picture as docs/images/icon.svg for the README. (FF7Recomp's icon tool, new picture.)
#
# The picture (drawn here as vectors, nothing taken from the game): a propeller airship, gold
# trimmed with lit gondola windows, crossing two moons, a large pale one and a small blue one, in
# a dusk sky over clouds. The sky, moons and far clouds are the background layer, the airship
# and the near clouds the foreground (launchers move the two apart a little). 108-unit
# adaptive-icon canvas; what matters sits inside the 66-unit circle.
import math
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
RES = os.path.join(ROOT, 'android', 'app', 'src', 'main', 'res')


# ---- a tiny scene model: groups (Android's transform: scale and rotate about a pivot, then
# translate) holding paths. Fills: ('solid', '#AARRGGBB'), ('stroke', '#AARRGGBB', width),
# ('linear', x1, y1, x2, y2, stops), ('radial', cx, cy, r, stops), in the path's own units.
class G:
    def __init__(self, items, px=0, py=0, rot=0, s=1, tx=0, ty=0):
        self.items, self.px, self.py, self.rot, self.s, self.tx, self.ty = items, px, py, rot, s, tx, ty


def f2(v):
    return f'{v:.2f}'.rstrip('0').rstrip('.')


def ellipse(cx, cy, rx, ry):
    return (f'M{f2(cx - rx)},{f2(cy)} a{f2(rx)},{f2(ry)} 0 1,0 {f2(2 * rx)},0 '
            f'a{f2(rx)},{f2(ry)} 0 1,0 {f2(-2 * rx)},0 Z')


def circle(cx, cy, r):
    return ellipse(cx, cy, r, r)


# ---- the sky
SKY = ('M0,0 h108 v108 h-108 Z', ('linear', 0, 14, 0, 96,
       [(0, '#FF10142E'), (0.42, '#FF2C2C5C'), (0.68, '#FF6A4A78'), (0.86, '#FFC07A6C'), (1, '#FFEDB07A')]))
STARS = [(28, 24, .5), (80, 22, .45), (73, 34, .35), (31, 46, .35), (52, 19, .4), (86, 44, .3), (22, 36, .3), (44, 28, .25)]
STAR_PATHS = ' '.join(circle(x, y, r) for x, y, r in STARS)

# the moons: a large pale one behind the airship, a small blue one up to the left
BIG = (61, 45, 16.5)
SMALL = (37, 32, 6)
MOONS = [
    (circle(BIG[0], BIG[1], BIG[2] * 1.7), ('radial', BIG[0], BIG[1], BIG[2] * 1.7,
        [(0.55, '#55FFE9B8'), (0.75, '#22FFD9A0'), (1, '#00FFD9A0')])),                          # halo
    (circle(*BIG), ('radial', BIG[0] - 5, BIG[1] - 6, BIG[2] * 1.5,
        [(0, '#FFFFF8E4'), (0.6, '#FFF1E2B8'), (1, '#FFCDB98A')])),
    (circle(BIG[0] + 5, BIG[1] + 4, 3.2) + ' ' + circle(BIG[0] - 6, BIG[1] + 6, 2) + ' ' + circle(BIG[0] + 1, BIG[1] - 8, 1.6),
        ('solid', '#22806A40')),                                                                # craters
    (circle(SMALL[0], SMALL[1], SMALL[2] * 1.9), ('radial', SMALL[0], SMALL[1], SMALL[2] * 1.9,
        [(0.5, '#4490B8FF'), (1, '#0090B8FF')])),
    (circle(*SMALL), ('radial', SMALL[0] - 2, SMALL[1] - 2, SMALL[2] * 1.4,
        [(0, '#FFEAF4FF'), (0.6, '#FFA8C0E8'), (1, '#FF6A84B8')])),
]
FAR_CLOUDS = ('M0,78 C10,74 18,77 26,75 C34,72 42,76 50,74 C60,71 68,75 78,73 C88,71 98,75 108,73 V108 H0 Z',
              ('linear', 0, 70, 0, 88, [(0, '#FF8E5E7A'), (1, '#FF4A3458')]))
NEAR_CLOUDS = ('M0,85 C12,81 22,84 32,82 C44,79 54,83 66,81 C78,79 90,83 108,80 V108 H0 Z',
               ('linear', 0, 79, 0, 95, [(0, '#FF3A2A4E'), (1, '#FF1A1428')]))

# ---- the airship, drawn level about (0,0): envelope 42 long, gondola under it, propeller at
# the bow (left), fins at the stern
RX, RY = 21, 7.2


def rib(x):
    h = RY * math.sqrt(max(0.0, 1 - (x / RX) ** 2)) - 0.4
    return f'M{f2(x)},{f2(-h)} L{f2(x)},{f2(h)}'


DARK = '#FF17121F'
ENVELOPE = ellipse(0, 0, RX, RY)
FINS = 'M17,-1.6 L27,-8.6 L25.4,0 L27,8.6 L17,1.8 Z'
TOPFIN = 'M-5,-6.6 L-1.5,-13 L5.5,-6.6 Z'
GONDOLA = 'M-9,6.2 L-6.5,11.8 L9.5,11.8 L12.5,6.2 Z'
BLADES = ellipse(-22.6, 0, 1.3, 6.2)
AIRSHIP = [
    (ENVELOPE, ('linear', 0, -RY, 0, RY, [(0, '#FF463A5E'), (0.5, '#FF2A2238'), (1, '#FF0E0A16')])),
    (' '.join(rib(x) for x in (-14, -7, 0, 7, 14)), ('stroke', '#FF54466C', 0.45)),
    ('M-20,-2 C-12,-7.2 12,-7.2 20,-2', ('stroke', '#FFF0CF72', 0.85)),                     # gold trim
    ('M-20,1.6 C-12,6.6 12,6.6 20,1.6', ('stroke', '#80B98A28', 0.5)),
    (ellipse(-6, -3.4, 9, 1.6), ('linear', 0, -5, 0, -1.8, [(0, '#55FFFFFF'), (1, '#00FFFFFF')])),   # sheen
    (FINS, ('solid', DARK)),
    ('M17,-1.6 L27,-8.6 M17,1.8 L27,8.6', ('stroke', '#FFC9A24A', 0.5)),
    (TOPFIN, ('solid', DARK)),
    ('M-4,6.4 L-6.5,4.6 M4,6.4 L4,5.6 M10,6.4 L12,4.6', ('stroke', '#FF54466C', 0.4)),       # rigging
    (GONDOLA, ('solid', DARK)),
    ('M-7.6,8.4 h18.6', ('stroke', '#FFF0CF72', 0.55)),
    (circle(-3, 10, 2.6) + ' ' + circle(1.5, 10, 2.6) + ' ' + circle(6, 10, 2.6),
        ('radial', 1.5, 10, 7, [(0, '#66FFD27A'), (1, '#00FFD27A')])),                     # window glow
    (circle(-3, 10, .75) + ' ' + circle(1.5, 10, .75) + ' ' + circle(6, 10, .75), ('solid', '#FFFFE29A')),
    (BLADES, ('linear', 0, -6, 0, 6, [(0, '#00F0CF72'), (0.5, '#99F0CF72'), (1, '#00F0CF72')])),  # spinning blades
    (circle(-22.6, 0, 1.1), ('solid', '#FFC9A24A')),
    ('M-21,0 h-0.6', ('stroke', DARK, 1.6)),
]
SHIP_AT = dict(rot=-7, s=0.95, tx=52, ty=58)

BACKGROUND = [SKY, (STAR_PATHS, ('solid', '#CCFFFFFF'))] + MOONS + [FAR_CLOUDS]
FOREGROUND = [G(AIRSHIP, **SHIP_AT), NEAR_CLOUDS]
MONO = [(circle(*BIG), ('solid', '#80FFFFFF')),
        G([(f'{ENVELOPE} {FINS} {TOPFIN} {GONDOLA} {BLADES}', ('solid', '#FFFFFFFF'))], **SHIP_AT)]


# ---- Android vector drawables
HEAD = '''<?xml version="1.0" encoding="utf-8"?>
<!-- FF9Recomp launcher icon ({part}): an airship crossing two moons at dusk (drawn as
     vectors, not taken from the game). Written by tools/make_icon.py; edit that, not this. -->
<vector xmlns:android="http://schemas.android.com/apk/res/android"
    xmlns:aapt="http://schemas.android.com/aapt"
    android:width="108dp" android:height="108dp"
    android:viewportWidth="108" android:viewportHeight="108">
'''


def a_stops(items):
    return ''.join(f'\n                <item android:offset="{o:g}" android:color="{c}"/>' for o, c in items)


def a_path(d, fill, ind):
    if fill[0] in ('solid', 'solid_eo'):
        eo = ' android:fillType="evenOdd"' if fill[0] == 'solid_eo' else ''
        return f'{ind}<path android:fillColor="{fill[1]}"{eo} android:pathData="{d}"/>\n'
    if fill[0] == 'stroke':
        return (f'{ind}<path android:strokeColor="{fill[1]}" android:strokeWidth="{fill[2]}" android:strokeLineCap="round" '
                f'android:strokeLineJoin="round" android:pathData="{d}"/>\n')
    if fill[0] == 'linear':
        _, x1, y1, x2, y2, st = fill
        g = (f'<gradient android:type="linear" android:startX="{f2(x1)}" android:startY="{f2(y1)}" '
             f'android:endX="{f2(x2)}" android:endY="{f2(y2)}">')
    else:
        _, cx, cy, rr, st = fill
        g = f'<gradient android:type="radial" android:centerX="{f2(cx)}" android:centerY="{f2(cy)}" android:gradientRadius="{f2(rr)}">'
    return f'''{ind}<path android:pathData="{d}">
{ind}    <aapt:attr name="android:fillColor">
{ind}        {g}{a_stops(st)}
{ind}        </gradient>
{ind}    </aapt:attr>
{ind}</path>
'''


def a_items(items, ind='    '):
    out = ''
    for it in items:
        if isinstance(it, G):
            attrs = ''
            for name, v, dflt in (('pivotX', it.px, 0), ('pivotY', it.py, 0), ('rotation', it.rot, 0), ('scaleX', it.s, 1),
                                  ('scaleY', it.s, 1), ('translateX', it.tx, 0), ('translateY', it.ty, 0)):
                if v != dflt:
                    attrs += f' android:{name}="{v:g}"'
            out += f'{ind}<group{attrs}>\n' + a_items(it.items, ind + '    ') + f'{ind}</group>\n'
        else:
            out += a_path(it[0], it[1], ind)
    return out


def write(path, text):
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)


for name, part, items in (('background', 'background: sky, moons and far clouds', BACKGROUND),
                          ('foreground', 'foreground: the airship and near clouds', FOREGROUND),
                          ('monochrome', 'themed-icon silhouette', MONO)):
    write(os.path.join(RES, 'drawable', f'ic_launcher_{name}.xml'), HEAD.format(part=part) + a_items(items) + '</vector>\n')


# ---- docs/images/icon.svg: both layers in a rounded square
defs = []


def s_color(c):
    return '#' + c[3:], int(c[1:3], 16) / 255


def s_stops(items):
    out = ''
    for o, c in items:
        rgb, a = s_color(c)
        out += f'<stop offset="{o:g}" stop-color="{rgb}" stop-opacity="{a:.3g}"/>'
    return out


def s_path(d, fill):
    if fill[0] in ('solid', 'solid_eo'):
        rgb, a = s_color(fill[1])
        return f'<path fill="{rgb}" fill-opacity="{a:.3g}" d="{d}"/>'
    if fill[0] == 'stroke':
        rgb, a = s_color(fill[1])
        return (f'<path fill="none" stroke="{rgb}" stroke-opacity="{a:.3g}" stroke-width="{fill[2]}" '
                f'stroke-linecap="round" stroke-linejoin="round" d="{d}"/>')
    n = len(defs)
    if fill[0] == 'linear':
        _, x1, y1, x2, y2, st = fill
        defs.append(f'<linearGradient id="g{n}" gradientUnits="userSpaceOnUse" x1="{f2(x1)}" y1="{f2(y1)}" '
                    f'x2="{f2(x2)}" y2="{f2(y2)}">{s_stops(st)}</linearGradient>')
    else:
        _, cx, cy, rr, st = fill
        defs.append(f'<radialGradient id="g{n}" gradientUnits="userSpaceOnUse" cx="{f2(cx)}" cy="{f2(cy)}" '
                    f'r="{f2(rr)}">{s_stops(st)}</radialGradient>')
    return f'<path fill="url(#g{n})" d="{d}"/>'


def s_items(items):
    out = []
    for it in items:
        if isinstance(it, G):
            t = f'translate({it.tx + it.px:g} {it.ty + it.py:g}) rotate({it.rot:g}) scale({it.s:g}) translate({-it.px:g} {-it.py:g})'
            out.append(f'<g transform="{t}">' + ''.join(s_items(it.items)) + '</g>')
        else:
            out.append(s_path(it[0], it[1]))
    return out


body = s_items(BACKGROUND + FOREGROUND)
svg = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="18 18 72 72" width="216" height="216">\n'
       '<clipPath id="c"><rect x="18" y="18" width="72" height="72" rx="16"/></clipPath>\n'
       f'<defs>{"".join(defs)}</defs>\n<g clip-path="url(#c)">\n' + '\n'.join(body) + '\n</g>\n</svg>\n')
write(os.path.join(ROOT, 'docs', 'images', 'icon.svg'), svg)
print('icon written')
