from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont


SCALE = 2
WIDTH = 1024
HEIGHT = 500
ROOT = Path(__file__).resolve().parents[1]
ICON = ROOT / "StoreAssets" / "AppIcons" / "paintmaze_logo_transparent_512.png"
OUTPUT = (
    ROOT
    / "StoreAssets"
    / "FeatureGraphics"
    / "paintmaze_feature_graphic_1024x500.png"
)
FONT = "/System/Library/Fonts/Supplemental/Arial Rounded Bold.ttf"


def s(value):
    if isinstance(value, tuple):
        return tuple(round(v * SCALE) for v in value)
    return round(value * SCALE)


def rgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def rgba(value, alpha=255):
    return rgb(value) + (alpha,)


def gradient(top, bottom):
    top = rgb(top)
    bottom = rgb(bottom)
    strip = Image.new("RGBA", (1, s(HEIGHT)))
    pixels = strip.load()
    for y in range(s(HEIGHT)):
        amount = y / (s(HEIGHT) - 1)
        pixels[0, y] = tuple(
            round(a + (b - a) * amount) for a, b in zip(top, bottom)
        ) + (255,)
    return strip.resize(s((WIDTH, HEIGHT)))


def glow(image, center, radius, color, opacity):
    layer = Image.new("RGBA", image.size)
    draw = ImageDraw.Draw(layer)
    x, y = s(center)
    r = s(radius)
    draw.ellipse((x - r, y - r, x + r, y + r), fill=rgba(color, opacity))
    image.alpha_composite(layer.filter(ImageFilter.GaussianBlur(s(radius * 0.65))))


def add_icon(image):
    icon = Image.open(ICON).convert("RGBA").resize(s((390, 390)), Image.Resampling.LANCZOS)
    x, y = s((42, 55))

    shadow = Image.new("RGBA", image.size)
    shadow_alpha = icon.getchannel("A").filter(ImageFilter.GaussianBlur(s(15)))
    shadow_shape = Image.new("RGBA", icon.size, (0, 0, 0, 150))
    shadow_shape.putalpha(shadow_alpha)
    shadow.alpha_composite(shadow_shape, (x + s(12), y + s(17)))
    image.alpha_composite(shadow)
    image.alpha_composite(icon, (x, y))


def add_title(image):
    draw = ImageDraw.Draw(image)
    title_font = ImageFont.truetype(FONT, s(94))
    maze_font = ImageFont.truetype(FONT, s(108))
    tagline_font = ImageFont.truetype(FONT, s(31))

    x = s(476)
    draw.text(
        (x + s(7), s(95) + s(11)),
        "PAINT",
        font=title_font,
        fill=(11, 15, 20, 180),
    )
    draw.text(
        (x, s(95)),
        "PAINT",
        font=title_font,
        fill=rgba("#F2F2EE"),
    )

    draw.text(
        (x + s(7), s(184) + s(12)),
        "MAZE",
        font=maze_font,
        fill=(11, 15, 20, 180),
    )
    draw.text(
        (x, s(184)),
        "MAZE",
        font=maze_font,
        fill=rgba("#F2B83D"),
    )

    # A short painted route acts as a separator and repeats the gameplay motif.
    route_y = 331
    tile = 24
    gap = 7
    route_colors = ["#DDA448", "#DDA448", "#BB342F", "#74D3AE", "#74D3AE"]
    for index, color in enumerate(route_colors):
        left = 480 + index * (tile + gap)
        draw.rounded_rectangle(
            s((left, route_y + 5, left + tile, route_y + tile + 5)),
            radius=s(5),
            fill=rgba("#171C23"),
        )
        draw.rounded_rectangle(
            s((left, route_y, left + tile, route_y + tile)),
            radius=s(5),
            fill=rgba(color),
        )

    draw.text(
        s((476, 373)),
        "PAINT EVERY TILE",
        font=tagline_font,
        fill=rgba("#D7DCE4"),
        spacing=s(2),
    )


def add_accents(image):
    draw = ImageDraw.Draw(image)
    for x, y, radius, color, alpha in [
        (930, 76, 8, "#74D3AE", 220),
        (958, 105, 4, "#74D3AE", 180),
        (915, 425, 7, "#DDA448", 210),
        (946, 444, 3, "#DDA448", 180),
        (445, 64, 5, "#BB342F", 180),
    ]:
        draw.ellipse(
            s((x - radius, y - radius, x + radius, y + radius)),
            fill=rgba(color, alpha),
        )


def main():
    image = gradient("#303844", "#171C23")
    glow(image, (285, 245), 270, "#DDA448", 35)
    glow(image, (105, 80), 190, "#74D3AE", 32)
    glow(image, (900, 90), 180, "#3E8EE6", 24)

    add_icon(image)
    add_title(image)
    add_accents(image)

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    image.resize((WIDTH, HEIGHT), Image.Resampling.LANCZOS).convert("RGB").save(
        OUTPUT, "PNG", optimize=True
    )
    print(f"Generated {OUTPUT} ({OUTPUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
