from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont


FINAL = 512
AA = 4
SIZE = FINAL * AA
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "StoreAssets" / "AppIcons"
FONT_PATH = "/System/Library/Fonts/Supplemental/Arial Rounded Bold.ttf"

PALETTE = {
    "background_dark": "#252B32",
    "background_light": "#EEE8D6",
    "card_light": "#FBF7EC",
    "card_dark": "#2E343F",
    "slate_dark": "#45535E",
    "slate_light": "#8C98B2",
    "gold": "#F2B83D",
    "honey": "#DDA448",
    "tomato": "#BB342F",
    "celadon": "#74D3AE",
    "sky": "#39A9DB",
    "blue": "#3E8EE6",
    "green": "#37B86E",
    "orange": "#F2982F",
    "red": "#E5503A",
    "purple": "#9B4DFF",
    "ball": "#F2F2EE",
}

FLOOR = (
    (1, 1, 1, 0, 0, 0),
    (0, 0, 1, 0, 1, 1),
    (1, 1, 1, 1, 1, 0),
    (1, 0, 0, 0, 1, 1),
)

ROUTE = {
    (0, 0), (0, 1), (0, 2), (1, 2),
    (2, 2), (2, 1), (2, 0), (3, 0),
}


def s(value):
    if isinstance(value, tuple):
        return tuple(round(v * AA) for v in value)
    return round(value * AA)


def rgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def rgba(value, alpha=255):
    return rgb(value) + (alpha,)


def mix(a, b, amount):
    a = rgb(a)
    b = rgb(b)
    return tuple(round(x + (y - x) * amount) for x, y in zip(a, b))


def to_hex(value):
    return "#{:02X}{:02X}{:02X}".format(*value)


def vertical_gradient(top, bottom):
    top = rgb(top)
    bottom = rgb(bottom)
    strip = Image.new("RGBA", (1, SIZE))
    pixels = strip.load()
    for y in range(SIZE):
        t = y / (SIZE - 1)
        pixels[0, y] = tuple(
            round(a + (b - a) * t) for a, b in zip(top, bottom)
        ) + (255,)
    return strip.resize((SIZE, SIZE))


def draw_panel(image, panel_color, border_color):
    shadow = Image.new("RGBA", image.size)
    sd = ImageDraw.Draw(shadow)
    sd.rounded_rectangle(
        s((24, 22, 488, 490)), radius=s(65), fill=(0, 0, 0, 130)
    )
    image.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(s(13))), (0, s(10)))

    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle(
        s((22, 18, 490, 486)),
        radius=s(66),
        fill=rgba(border_color),
    )
    draw.rounded_rectangle(
        s((35, 31, 477, 473)),
        radius=s(54),
        fill=rgba(panel_color),
    )


def draw_tile(draw, box, top, side):
    x0, y0, x1, y1 = box
    radius = s(8)
    draw.rounded_rectangle(
        s((x0, y0 + 8, x1, y1 + 8)), radius=radius, fill=rgba(side)
    )
    draw.rounded_rectangle(s(box), radius=radius, fill=rgba(top))
    draw.rounded_rectangle(
        s((x0 + 4, y0 + 4, x1 - 4, y0 + 9)),
        radius=s(2),
        fill=(255, 255, 255, 42),
    )


def draw_ball(image, center, radius, paint):
    x, y = s(center)
    r = s(radius)
    shadow = Image.new("RGBA", image.size)
    sd = ImageDraw.Draw(shadow)
    sd.ellipse(
        (x - r, y - s(radius * 0.25), x + r, y + s(radius * 0.90)),
        fill=(0, 0, 0, 145),
    )
    image.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(s(6))))

    draw = ImageDraw.Draw(image)
    edge = mix(PALETTE["ball"], "#000000", 0.16)
    base = rgb(PALETTE["ball"])
    for current in range(r, 0, -2):
        progress = 1 - current / r
        brightness = min(1.0, progress * 0.46)
        fill = tuple(
            round(a + (b - a) * brightness) for a, b in zip(edge, base)
        ) + (255,)
        draw.ellipse((x - current, y - current, x + current, y + current), fill=fill)

    draw.ellipse(
        (x - s(radius * 0.43), y - s(radius * 0.48),
         x - s(radius * 0.16), y - s(radius * 0.21)),
        fill=(255, 255, 255, 240),
    )


def draw_board(image, tile_top, paint):
    draw = ImageDraw.Draw(image)
    tile = 54
    gap = 6
    origin_x = 79
    origin_y = 55
    tile_side = to_hex(mix(tile_top, "#000000", 0.35))
    paint_side = to_hex(mix(paint, "#000000", 0.31))

    for row in range(4):
        for col in range(6):
            if not FLOOR[row][col]:
                continue
            x = origin_x + col * (tile + gap)
            y = origin_y + row * (tile + gap)
            painted = (row, col) in ROUTE
            draw_tile(
                draw,
                (x, y, x + tile, y + tile),
                paint if painted else tile_top,
                paint_side if painted else tile_side,
            )

    final_row, final_col = 3, 0
    ball_center = (
        origin_x + final_col * (tile + gap) + tile / 2,
        origin_y + final_row * (tile + gap) + tile / 2 - 4,
    )
    for dx, dy, radius in [(-27, 14, 4), (29, -10, 3), (22, 24, 2.5)]:
        draw.ellipse(
            s((ball_center[0] + dx - radius, ball_center[1] + dy - radius,
               ball_center[0] + dx + radius, ball_center[1] + dy + radius)),
            fill=rgba(paint),
        )
    draw_ball(image, ball_center, 28, paint)


def fitted_font(text, maximum_width, initial_size=88):
    for size in range(initial_size, 48, -1):
        font = ImageFont.truetype(FONT_PATH, s(size))
        if font.getlength(text) <= s(maximum_width):
            return font
    return ImageFont.truetype(FONT_PATH, s(48))


def draw_word(image, word, y, colors, outline):
    font = fitted_font(word, 384)
    advances = [font.getlength(letter) for letter in word]
    total = sum(advances) - s(1.5) * (len(word) - 1)
    x = (SIZE - total) / 2
    draw = ImageDraw.Draw(image)

    for index, letter in enumerate(word):
        fill = colors[index % len(colors)]
        dark = to_hex(mix(fill, "#000000", 0.40))
        position = (round(x), s(y))

        # Deep but restrained extrusion for readability at small store-listing sizes.
        for offset in range(s(11), s(3), -1):
            draw.text(
                (position[0], position[1] + offset),
                letter,
                font=font,
                fill=rgba(dark),
                stroke_width=s(2),
                stroke_fill=rgba(outline),
            )
        draw.text(
            position,
            letter,
            font=font,
            fill=rgba(fill),
            stroke_width=s(2),
            stroke_fill=rgba(outline),
        )
        x += advances[index] - s(1.5)


def add_paint_marks(image, paint):
    draw = ImageDraw.Draw(image)
    for x, y, radius in [
        (62, 333, 11), (49, 356, 6), (454, 350, 10),
        (466, 372, 5), (57, 434, 8), (454, 430, 7),
    ]:
        draw.ellipse(
            s((x - radius, y - radius, x + radius, y + radius)),
            fill=rgba(paint, 220),
        )


def render(filename, background, panel, border, tile, paint, dark_text_outline):
    top = to_hex(mix(background, "#FFFFFF", 0.08))
    bottom = to_hex(mix(background, "#000000", 0.08))
    image = vertical_gradient(top, bottom)
    draw_panel(image, panel, border)
    draw_board(image, tile, paint)
    add_paint_marks(image, paint)

    paint_colors = [
        PALETTE["tomato"], PALETTE["honey"], PALETTE["celadon"],
        PALETTE["sky"], PALETTE["purple"],
    ]
    maze_colors = [
        PALETTE["orange"], PALETTE["celadon"],
        PALETTE["sky"], PALETTE["red"],
    ]
    draw_word(image, "PAINT", 278, paint_colors, dark_text_outline)
    draw_word(image, "MAZE", 365, maze_colors, dark_text_outline)

    OUT.mkdir(parents=True, exist_ok=True)
    image.resize((FINAL, FINAL), Image.Resampling.LANCZOS).save(
        OUT / filename, "PNG", optimize=True
    )


def main():
    render(
        "paintmaze_icon_v6_01_title_light.png",
        PALETTE["background_light"], PALETTE["card_light"],
        PALETTE["slate_dark"], PALETTE["slate_dark"],
        PALETTE["honey"], PALETTE["slate_dark"],
    )
    render(
        "paintmaze_icon_v6_02_title_dark.png",
        PALETTE["background_dark"], PALETTE["card_dark"],
        PALETTE["slate_light"], PALETTE["slate_light"],
        PALETTE["celadon"], PALETTE["background_dark"],
    )
    render(
        "paintmaze_icon_v6_03_title_gold.png",
        PALETTE["background_dark"], PALETTE["card_light"],
        PALETTE["gold"], PALETTE["slate_dark"],
        PALETTE["tomato"], PALETTE["slate_dark"],
    )
    print(f"Generated 3 titled PaintMaze icons in {OUT}")


if __name__ == "__main__":
    main()
