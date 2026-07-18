#!/usr/bin/env python3
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1] / "StoreAssets" / "Screenshots"
EXPECTED = {
    "Phone": (1080, 1920),
    "7InchTablet": (1080, 1920),
    "10InchTablet": (1440, 2560),
}
MAX_BYTES = 8 * 1024 * 1024


def main():
    for folder, expected_size in EXPECTED.items():
        target = ROOT / folder
        for source in sorted(target.glob("*.bmp")):
            output = source.with_suffix(".png")
            with Image.open(source) as image:
                image = image.convert("RGB")
                if image.size != expected_size:
                    raise RuntimeError(
                        f"{source.name}: expected {expected_size}, got {image.size}"
                    )
                image.save(output, "PNG", optimize=True, compress_level=9)
            source.unlink()

        screenshots = sorted(target.glob("*.png"))
        if len(screenshots) != 6:
            raise RuntimeError(
                f"{folder}: expected 6 screenshots, found {len(screenshots)}"
            )
        for screenshot in screenshots:
            with Image.open(screenshot) as image:
                if image.size != expected_size:
                    raise RuntimeError(
                        f"{screenshot.name}: expected {expected_size}, got {image.size}"
                    )
            if screenshot.stat().st_size > MAX_BYTES:
                raise RuntimeError(f"{screenshot.name}: exceeds the 8 MB limit")
            print(
                f"{folder}/{screenshot.name}: "
                f"{expected_size[0]}x{expected_size[1]}, "
                f"{screenshot.stat().st_size / 1024:.0f} KB"
            )


if __name__ == "__main__":
    main()
