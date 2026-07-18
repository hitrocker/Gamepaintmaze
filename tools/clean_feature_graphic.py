from pathlib import Path

import cv2
import numpy as np


SOURCE = Path(
    "/Users/hitrocker/.cursor/projects/"
    "Users-hitrocker-Documents-Games-PaintMaze/assets/"
    "image-3616dfdc-ac5c-44f5-8db6-cbdc77155ab6.png"
)
OUTPUT = (
    Path(__file__).resolve().parents[1]
    / "StoreAssets"
    / "FeatureGraphics"
    / "paintmaze_feature_graphic_clean_1024x500.png"
)


def main():
    image = cv2.imread(str(SOURCE), cv2.IMREAD_COLOR)
    if image is None:
        raise FileNotFoundError(SOURCE)

    mask = np.zeros(image.shape[:2], dtype=np.uint8)

    # Remove the two faint repeated-logo watermarks while preserving the frame.
    cv2.rectangle(mask, (748, 28), (995, 235), 255, thickness=cv2.FILLED)
    cv2.rectangle(mask, (28, 270), (252, 469), 255, thickness=cv2.FILLED)
    mask = cv2.GaussianBlur(mask, (31, 31), 8.0)

    # Rebuild the nearly uniform cream canvas with a subtle vertical tone shift.
    height, width = image.shape[:2]
    top = np.array([218, 235, 242], dtype=np.float32)
    bottom = np.array([213, 231, 239], dtype=np.float32)
    rebuilt = np.empty_like(image, dtype=np.float32)
    for row in range(height):
        amount = row / max(1, height - 1)
        rebuilt[row, :, :] = top + (bottom - top) * amount

    amount = mask.astype(np.float32)[:, :, None] / 255.0
    cleaned = np.round(
        image.astype(np.float32) * (1.0 - amount) + rebuilt * amount
    ).astype(np.uint8)
    cleaned = cv2.resize(cleaned, (1024, 500), interpolation=cv2.INTER_LANCZOS4)

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(OUTPUT), cleaned, [cv2.IMWRITE_PNG_COMPRESSION, 9])
    print(f"Prepared {OUTPUT} ({OUTPUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
