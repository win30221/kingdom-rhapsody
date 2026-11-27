#!/usr/bin/env python3
"""Build a Traditional Chinese lobby font from the original OFL source.

Optional: --source /path/to/NotoSansCJKtc-Bold.otf for an offline build.
The existing KingdomUiTC.otf remains unchanged. Requires fonttools.
"""
import argparse
from pathlib import Path
from tempfile import TemporaryDirectory
from urllib.request import urlretrieve

from fontTools import subset
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
FONTS = ROOT / "unity/Assets/Lobby/Resources/Lobby/Fonts"
SOURCE = "https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/OTF/TraditionalChinese/NotoSansCJKtc-Bold.otf"
OUTPUT = FONTS / "KingdomUiTCFull.otf"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path)
    args = parser.parse_args()
    with TemporaryDirectory() as work:
        original = args.source
        if original is None:
            original = Path(work) / "NotoSansCJKtc-Bold.otf"
            urlretrieve(SOURCE, original)
        old_font = TTFont(FONTS / "KingdomUiTC.otf")
        codepoints = set(old_font.getBestCmap())
        old_font.close()
        codepoints.update(range(32, 127))
        for folder in ("Lobby", "Battle"):
            for path in (ROOT / f"unity/Assets/{folder}/Scripts").rglob("*.cs"):
                codepoints.update(ord(ch) for ch in path.read_text(encoding="utf-8") if not ch.isspace())
        font = TTFont(original)
        missing = sorted(codepoints - set(font.getBestCmap()))
        if missing:
            raise RuntimeError("Source font missing codepoints: " + ", ".join(f"U+{cp:04X}" for cp in missing))
        options = subset.Options()
        options.name_IDs = ["*"]
        options.name_languages = ["*"]
        subsetter = subset.Subsetter(options=options)
        subsetter.populate(unicodes=codepoints)
        subsetter.subset(font)
        names = font["name"]
        family = "Kingdom UI TC Extended"
        replacements = {1: family, 2: "Bold", 3: "KingdomUiTCExtended-Bold",
                        4: family + " Bold", 6: "KingdomUiTCExtended-Bold",
                        16: family, 17: "Bold", 18: family + " Bold", 21: family, 22: "Bold"}
        for record in names.names:
            if record.nameID in replacements:
                record.string = replacements[record.nameID].encode(record.getEncoding())
        font.save(OUTPUT)
        font.close()
        check = TTFont(OUTPUT)
        absent = codepoints - set(check.getBestCmap())
        check.close()
        if absent:
            raise RuntimeError("Generated font is missing required glyphs")
        print(f"Generated {OUTPUT} ({len(codepoints)} codepoints)")


if __name__ == "__main__":
    main()
