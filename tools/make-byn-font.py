"""Builds src/FinanceApp.Mobile/Resources/Fonts/BynSign.ttf: one glyph, the Belarusian ruble sign.

The National Bank of Belarus approved the sign in January 2026 — the capital «Б» with a horizontal stroke through
its middle, parallel to the top one — but Unicode has no code point for it yet. The app maps it to U+E0B0 (Private
Use Area) and puts this font first on its labels; every other character falls back to the system font, so the
metrics below copy Android's Roboto (2048 units per em, ascent 1900, descent 500) to keep line heights unchanged.

Run with fontTools (pip install fonttools): python3 tools/make-byn-font.py
"""

import pathlib

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

CODE_POINT = 0xE0B0
UPM = 2048
CAP = 1456  # Roboto's cap height
STROKE = 184
WIDTH = 1280  # advance width, close to Roboto's «Б»


def rect(pen, x0, y0, x1, y1):
    """A clockwise rectangle (TrueType outer contour)."""
    pen.moveTo((x0, y0))
    pen.lineTo((x0, y1))
    pen.lineTo((x1, y1))
    pen.lineTo((x1, y0))
    pen.closePath()


def glyph():
    pen = TTGlyphPen(None)
    stem_left = 300
    stem_right = stem_left + STROKE

    # The stem and the top stroke of «Б».
    rect(pen, stem_left, 0, stem_right, CAP)
    rect(pen, stem_right, CAP - STROKE, 1150, CAP)

    # The stroke through the middle, parallel to the top one; it crosses the stem, as in other currency signs.
    middle = 924  # centered between the bowl (760) and the top stroke (1272)
    rect(pen, 90, middle, 900, middle + STROKE)

    # The bowl: the lower part of «Б», with a hole.
    top = 760
    pen.moveTo((stem_right, 0))
    pen.lineTo((stem_right, top))
    pen.lineTo((760, top))
    pen.qCurveTo((1160, top), (1160, top / 2))
    pen.qCurveTo((1160, 0), (760, 0))
    pen.closePath()

    inner_top = top - STROKE
    pen.moveTo((stem_right, STROKE))
    pen.lineTo((760, STROKE))
    pen.qCurveTo((970, STROKE), (970, top / 2))
    pen.qCurveTo((970, inner_top), (760, inner_top))
    pen.lineTo((stem_right, inner_top))
    pen.closePath()
    return pen.glyph()


def empty():
    return TTGlyphPen(None).glyph()


def main():
    builder = FontBuilder(UPM, isTTF=True)
    names = [".notdef", "bynsign"]
    builder.setupGlyphOrder(names)
    builder.setupCharacterMap({CODE_POINT: "bynsign"})
    builder.setupGlyf({".notdef": empty(), "bynsign": glyph()})
    builder.setupHorizontalMetrics({".notdef": (0, 0), "bynsign": (WIDTH, 90)})
    builder.setupHorizontalHeader(ascent=1900, descent=-500, lineGap=0)
    builder.setupNameTable({"familyName": "BynSign", "styleName": "Regular"})
    builder.setupOS2(
        sTypoAscender=1536,
        sTypoDescender=-512,
        sTypoLineGap=102,
        usWinAscent=1946,
        usWinDescent=512,
        sCapHeight=CAP,
        sxHeight=1082,
    )
    builder.setupPost()
    out = pathlib.Path(__file__).resolve().parent.parent / "src/FinanceApp.Mobile/Resources/Fonts/BynSign.ttf"
    out.parent.mkdir(parents=True, exist_ok=True)
    builder.save(str(out))
    print(out)


if __name__ == "__main__":
    main()
