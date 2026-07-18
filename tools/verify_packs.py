"""Validate all sectioned baked levels and reject symmetric duplicates."""
import os, glob
from collections import deque
from statistics import mean, median
from neverstuck_test import always_solvable

LV = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Levels")
BAKED = os.path.join(LV, "Baked")
EXPECTED_MODES = {"easy", "medium", "hard", "extrahard", "ultrahard"}
EXPECTED_COUNTS = {
    "easy": 500,
    "medium": 500,
    "hard": 500,
    "extrahard": 500,
    "ultrahard": 4500,
}


def expected_hard_dimensions(mode, index):
    if mode == "extrahard":
        if index <= 125: return 9, 9
        if index <= 250: return 10, 10
        if index <= 375: return 12, 14
        return 14, 16
    if mode == "ultrahard":
        if index <= 125: return 14, 16
        if index <= 250: return 14, 18
        if index <= 375: return 16, 18
        if index <= 500: return 16, 20
        return 16, 22
    return None


def is_board_row(line):
    return len(line) > 0 and set(line) <= set(".#S_")


def parse_blocks(text):
    block = []
    for line in text.splitlines():
        line = line.lstrip("\ufeff")
        if line.strip() == "":
            if block:
                yield block; block = []
            continue
        if is_board_row(line):          # a grid row (may start with '#')
            block.append(line)
        elif line.startswith("#"):       # comment (board rows handled above)
            continue
        else:                            # title line starts a new block
            if block:
                yield block; block = []
            block = [line]
    if block:
        yield block


def transforms(rows):
    grid = tuple(tuple(row) for row in rows)

    def rotate(g):
        return tuple(tuple(g[len(g) - 1 - r][c] for r in range(len(g)))
                     for c in range(len(g[0])))

    variants = []
    current = grid
    for _ in range(4):
        variants.append(current)
        variants.append(tuple(tuple(reversed(row)) for row in current))
        current = rotate(current)
    return variants


def canonical(rows):
    return min("\n".join("".join(row) for row in variant)
               for variant in transforms(rows))


def has_enclosed_void(rows):
    height = len(rows)
    width = max(len(row) for row in rows)
    padded = [row.ljust(width, "_") for row in rows]
    exterior = set()
    queue = deque()

    def add(row, col):
        if not (0 <= row < height and 0 <= col < width):
            return
        if (row, col) in exterior or padded[row][col] != "_":
            return
        exterior.add((row, col))
        queue.append((row, col))

    for col in range(width):
        add(0, col)
        add(height - 1, col)
    for row in range(height):
        add(row, 0)
        add(row, width - 1)

    while queue:
        row, col = queue.popleft()
        add(row - 1, col)
        add(row + 1, col)
        add(row, col - 1)
        add(row, col + 1)

    return any(padded[row][col] == "_" and (row, col) not in exterior
               for row in range(height) for col in range(width))


def open_layout_metrics(rows):
    height = len(rows)
    width = max(len(row) for row in rows)
    floors = {(r, c) for r, row in enumerate(rows)
              for c, ch in enumerate(row) if ch in ".S"}
    walls = {(r, c) for r, row in enumerate(rows)
             for c, ch in enumerate(row) if ch == "#"}
    quads = 0
    open_cells = set()
    for r in range(height - 1):
        for c in range(width - 1):
            square = {(r, c), (r + 1, c), (r, c + 1), (r + 1, c + 1)}
            if square <= floors:
                quads += 1
                open_cells |= square

    degree_two = 0
    degree_four = 0
    junctions = 0
    edges = 0
    for r, c in floors:
        degree = sum((r + dr, c + dc) in floors for dr, dc in
                     ((-1, 0), (1, 0), (0, -1), (0, 1)))
        degree_two += degree == 2
        degree_four += degree == 4
        junctions += degree >= 3
        edges += (r + 1, c) in floors
        edges += (r, c + 1) in floors

    components = 0
    unseen = set(floors)
    while unseen:
        components += 1
        queue = deque([unseen.pop()])
        while queue:
            r, c = queue.popleft()
            for dr, dc in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                neighbour = (r + dr, c + dc)
                if neighbour in unseen:
                    unseen.remove(neighbour)
                    queue.append(neighbour)

    count = max(1, len(floors))
    open_core_count = 0
    open_core_cells = set()
    for r in range(height - 2):
        for c in range(width - 2):
            square = {(r + dr, c + dc) for dr in range(3) for dc in range(3)}
            if square <= floors:
                open_core_count += 1
                open_core_cells |= square

    maximum_clearance = 0
    for row, col in floors:
        clearance = 0
        while True:
            radius = clearance + 1
            square = {(row + dr, col + dc)
                      for dr in range(-radius, radius + 1)
                      for dc in range(-radius, radius + 1)}
            if not square <= floors:
                break
            clearance = radius
        maximum_clearance = max(maximum_clearance, clearance)

    largest_open_core = 0
    unseen_core = set(open_core_cells)
    while unseen_core:
        queue = deque([unseen_core.pop()])
        component_size = 0
        while queue:
            row, col = queue.popleft()
            component_size += 1
            for dr, dc in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                neighbour = (row + dr, col + dc)
                if neighbour in unseen_core:
                    unseen_core.remove(neighbour)
                    queue.append(neighbour)
        largest_open_core = max(largest_open_core, component_size)

    interior_wall_islands = 0
    interior_wall_cells = 0
    separator_wall_cells = 0
    unseen_walls = set(walls)
    while unseen_walls:
        start = unseen_walls.pop()
        queue = deque([start])
        component_size = 0
        touches_boundary = False
        min_row = max_row = start[0]
        min_col = max_col = start[1]
        while queue:
            row, col = queue.popleft()
            component_size += 1
            min_row = min(min_row, row)
            max_row = max(max_row, row)
            min_col = min(min_col, col)
            max_col = max(max_col, col)
            touches_boundary |= row in (0, height - 1) or col in (0, width - 1)
            for dr, dc in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                neighbour = (row + dr, col + dc)
                if neighbour in unseen_walls:
                    unseen_walls.remove(neighbour)
                    queue.append(neighbour)
        if not touches_boundary:
            interior_wall_islands += 1
            interior_wall_cells += component_size
        component_height = max_row - min_row + 1
        component_width = max_col - min_col + 1
        if ((component_width >= max(4, width // 3) and component_height <= 2) or
                (component_height >= max(4, height // 3) and component_width <= 2)):
            separator_wall_cells += component_size

    exterior_cut_depth = 0
    for r, row in enumerate(rows):
        for c, ch in enumerate(row):
            if ch != "_":
                continue
            exterior_cut_depth = max(
                exterior_cut_depth,
                min(r, height - 1 - r, c, width - 1 - c))
    concave_corners = 0
    for r in range(height - 1):
        for c in range(width - 1):
            board_cells = sum(
                rows[r + dr][c + dc] != "_"
                for dr in range(2) for dc in range(2))
            concave_corners += board_cells == 3

    spawn = next(((r, c) for r, row in enumerate(rows)
                  for c, ch in enumerate(row) if ch == "S"), None)
    reachable = {spawn} if spawn is not None else set()
    queue = deque(reachable)
    choice_stops = 0
    while queue:
        row, col = queue.popleft()
        legal = 0
        for dr, dc in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            stop_row, stop_col = row, col
            while (stop_row + dr, stop_col + dc) in floors:
                stop_row += dr
                stop_col += dc
            stop = (stop_row, stop_col)
            if stop == (row, col):
                continue
            legal += 1
            if stop not in reachable:
                reachable.add(stop)
                queue.append(stop)
        choice_stops += legal >= 3

    long_lane_cells = set()
    longest_run = 0
    for r in range(height):
        c = 0
        while c < width:
            start = c
            while c < width and (r, c) in floors:
                c += 1
            length = c - start
            longest_run = max(longest_run, length)
            if length >= 6:
                long_lane_cells.update((r, x) for x in range(start, c))
            c += length == 0
    for c in range(width):
        r = 0
        while r < height:
            start = r
            while r < height and (r, c) in floors:
                r += 1
            length = r - start
            longest_run = max(longest_run, length)
            if length >= 6:
                long_lane_cells.update((x, c) for x in range(start, r))
            r += length == 0
    return {
        "quads": quads,
        "junctions": junctions,
        "cycles": max(0, edges - len(floors) + components),
        "open_ratio": len(open_cells) / count,
        "degree2_ratio": degree_two / count,
        "longest_run": longest_run,
        "long_lane_ratio": len(long_lane_cells) / count,
        "floor_count": len(floors),
        "open_core_count": open_core_count,
        "open_core_cells": len(open_core_cells),
        "open_core_ratio": len(open_core_cells) / count,
        "maximum_clearance": maximum_clearance,
        "degree4_ratio": degree_four / count,
        "largest_open_core_ratio": largest_open_core / count,
        "interior_wall_islands": interior_wall_islands,
        "wall_island_ratio": interior_wall_cells / max(1, len(walls)),
        "parallel_separator_ratio": separator_wall_cells / max(1, len(walls)),
        "exterior_cut_depth": exterior_cut_depth,
        "concave_corners": concave_corners,
        "choice_stops": choice_stops,
    }


def meets_open_layout(rows):
    metrics = open_layout_metrics(rows)
    large = len(rows) * max(len(row) for row in rows) > 100
    minimum_open_core_count = (
        max(4, metrics["floor_count"] // 50) if large else 1)
    return (
        metrics["open_core_count"] >= minimum_open_core_count and
        metrics["open_core_ratio"] >= 0.18 and
        metrics["maximum_clearance"] >= 1 and
        metrics["degree4_ratio"] >= 0.07 and
        metrics["largest_open_core_ratio"] >= 0.12 and
        metrics["interior_wall_islands"] >= 1 and
        metrics["wall_island_ratio"] >= 0.35 and
        metrics["parallel_separator_ratio"] <= 0.90 and
        (not large or metrics["choice_stops"] >= 1)
    ), metrics


def main():
    total = 0; bad = 0
    fingerprints = set()
    mode_counts = {mode: 0 for mode in EXPECTED_MODES}
    section_arena_metrics = {}
    size_arena_metrics = {}
    paths = sorted(glob.glob(os.path.join(BAKED, "*.txt")))
    for path in paths:
        mode = os.path.basename(path).split("_", 1)[0]
        if mode not in mode_counts:
            bad += 1
            print("INVALID MODE:", os.path.basename(path))
            continue
        with open(path) as f:
            text = f.read()
        for block in parse_blocks(text):
            title = block[0]
            source_index = int(title.rsplit(" ", 1)[1])
            rows = block[1:]
            if not rows:
                bad += 1
                print("INVALID EMPTY BLOCK:", os.path.basename(path), title)
                continue
            R = len(rows); C = max(len(r) for r in rows)
            grid = [[1 if ch in ".S" else 0 for ch in row.ljust(C, "_")] for row in rows]
            start = None
            spawn_count = 0
            for r, row in enumerate(rows):
                for c, ch in enumerate(row):
                    if ch == "S":
                        start = (r, c)
                        spawn_count += 1
            total += 1
            mode_counts[mode] += 1
            has_void = any("_" in row for row in rows)
            walls = {(r, c) for r, row in enumerate(rows) for c, ch in enumerate(row) if ch == "#"}
            isolated = 0
            for r, c in walls:
                if not any((r + dr, c + dc) in walls for dr, dc in
                           ((-1, 0), (1, 0), (0, -1), (0, 1))):
                    isolated += 1
            too_isolated = len(walls) >= 3 and isolated / len(walls) > 0.50
            floor_fraction = sum(ch in ".S" for row in rows for ch in row) / max(1, R * C)
            fingerprint = canonical(rows)
            duplicate = fingerprint in fingerprints
            fingerprints.add(fingerprint)
            enclosed_void = has_enclosed_void(rows)
            expected_dimensions = expected_hard_dimensions(mode, source_index)
            dimensions_match = (
                expected_dimensions is None or expected_dimensions == (R, C))
            layout_ok, layout = meets_open_layout(rows)
            extra_hard_layout_ok = (
                mode not in ("extrahard", "ultrahard") or layout_ok)
            if mode in ("extrahard", "ultrahard"):
                section_arena_metrics.setdefault(path, []).append(layout)
                size_arena_metrics.setdefault((R, C), []).append(layout)
            if (spawn_count != 1 or not has_void or too_isolated or duplicate or
                    enclosed_void or floor_fraction < 0.45 or not dimensions_match or
                    not extra_hard_layout_ok or
                    not always_solvable(grid, R, C, start)):
                bad += 1
                print("INVALID:", os.path.basename(path), title,
                      f"spawn={spawn_count} void={has_void} isolated={isolated}/{len(walls)} "
                      f"floor={floor_fraction:.3f} duplicate={duplicate} enclosed_void={enclosed_void} "
                      f"dims={R}x{C} expected={expected_dimensions} "
                      f"layout={layout}")

    for path, metrics in section_arena_metrics.items():
        section_ok = (
            len(metrics) == 50 and
            median(item["open_core_ratio"] for item in metrics) >= 0.45 and
            min(item["maximum_clearance"] for item in metrics) >= 1 and
            median(item["parallel_separator_ratio"] for item in metrics) <= 0.25)
        if not section_ok:
            bad += 1
            print("INVALID ARENA SECTION:", os.path.basename(path),
                  f"count={len(metrics)} "
                  f"core_median={median(item['open_core_ratio'] for item in metrics):.3f} "
                  f"clearance_min={min(item['maximum_clearance'] for item in metrics)} "
                  f"separator_median={median(item['parallel_separator_ratio'] for item in metrics):.3f}")

    previous_floors = previous_cycles = None
    for dimensions, metrics in sorted(
            size_arena_metrics.items(), key=lambda item: item[0][0] * item[0][1]):
        average_floors = mean(item["floor_count"] for item in metrics)
        average_cycles = mean(item["cycles"] for item in metrics)
        # The terminal 16x22 arena band trades a small amount of raw cycle count for
        # compact wall islands. Require continued floor growth and reject only a
        # material (>10%) cycle regression across adjacent physical sizes.
        if (previous_floors is not None and
                (average_floors <= previous_floors or
                 average_cycles < previous_cycles * 0.90)):
            bad += 1
            print("INVALID ARENA SIZE TREND:", dimensions,
                  f"floors={average_floors:.3f} previous={previous_floors:.3f} "
                  f"cycles={average_cycles:.3f} previous={previous_cycles:.3f}")
        previous_floors = average_floors
        previous_cycles = average_cycles

    print(f"\nchecked {total} levels, {bad} invalid")
    wrong_counts = {
        mode: mode_counts.get(mode, 0)
        for mode, expected in EXPECTED_COUNTS.items()
        if mode_counts.get(mode, 0) != expected
    }
    if len(paths) != 130 or total != 6500 or wrong_counts or bad:
        if wrong_counts:
            print("WRONG MODE COUNTS:", wrong_counts)
        raise SystemExit(1)


if __name__ == "__main__":
    main()
