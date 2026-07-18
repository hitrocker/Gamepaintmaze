from pathlib import Path

import cv2
import numpy as np
from PIL import Image


SOURCE = Path(
    "/Users/hitrocker/.cursor/projects/"
    "Users-hitrocker-Documents-Games-PaintMaze/assets/"
    "image-22a92f6e-2c53-4373-9b79-2a306add5de1.png"
)
OUTPUT = (
    Path(__file__).resolve().parents[1]
    / "StoreAssets"
    / "AppIcons"
    / "paintmaze_logo_transparent_512.png"
)


def central_logo_mask(rgb):
    gray = cv2.cvtColor(rgb, cv2.COLOR_RGB2GRAY)
    edges = cv2.Canny(gray, 40, 100)
    contours, _ = cv2.findContours(
        edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE
    )
    contour = max(contours, key=cv2.contourArea)

    alpha = np.zeros(rgb.shape[:2], dtype=np.uint8)
    cv2.drawContours(alpha, [contour], -1, 255, thickness=cv2.FILLED)
    alpha = cv2.dilate(alpha, np.ones((5, 5), np.uint8), iterations=1)
    alpha = cv2.GaussianBlur(alpha, (9, 9), 2.0)
    return alpha, cv2.boundingRect(contour)


def main():
    source = Image.open(SOURCE).convert("RGB")
    rgb = np.asarray(source)
    alpha, (x, y, width, height) = central_logo_mask(rgb)
    transparent = Image.fromarray(np.dstack((rgb, alpha)), "RGBA")

    # Center the clean rounded-square logo with enough transparent masking room.
    side = max(width, height) + 90
    center_x = x + width / 2
    center_y = y + height / 2
    left = round(center_x - side / 2)
    top = round(center_y - side / 2)
    transparent = transparent.crop((left, top, left + side, top + side))
    transparent = transparent.resize((512, 512), Image.Resampling.LANCZOS)

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    transparent.save(OUTPUT, "PNG", optimize=True)
    print(f"Prepared {OUTPUT} ({OUTPUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
