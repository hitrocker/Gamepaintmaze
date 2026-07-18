"""Dev harness that mirrors the C# Core algorithms (MovementSystem, Solver,
GeneratedLevelProvider) to validate logic without compiling Unity, and to emit
guaranteed-valid starter level packs.

The slide rules and IsAlwaysSolvable check are a 1:1 port of the C# so that any
level this accepts will also be accepted by the in-game Solver.
"""
import random
from collections import deque

FLOOR, WALL = 0, 1
DIRS = [(-1, 0), (1, 0), (0, -1), (0, 1)]  # Up, Down, Left, Right


def slide(grid, pos, d):
    rows, cols = len(grid), len(grid[0])
    dr, dc = d
    r, c = pos
    path = []
    while True:
        nr, nc = r + dr, c + dc
        if not (0 <= nr < rows and 0 <= nc < cols) or grid[nr][nc] == WALL:
            break
        r, c = nr, nc
        path.append((r, c))
    return (r, c), path


def is_always_solvable(grid, spawn):
    rows, cols = len(grid), len(grid[0])
    forward, reachable, covered = {}, {spawn}, {spawn}
    q = deque([spawn])
    while q:
        cur = q.popleft()
        for d in DIRS:
            stop, path = slide(grid, cur, d)
            if stop == cur:
                continue
            covered.update(path)
            forward.setdefault(cur, []).append(stop)
            if stop not in reachable:
                reachable.add(stop)
                q.append(stop)
    for r in range(rows):
        for c in range(cols):
            if grid[r][c] == FLOOR and (r, c) not in covered:
                return False
    reverse = {}
    for a, tos in forward.items():
        for b in tos:
            reverse.setdefault(b, []).append(a)
    can_reach = {spawn}
    q = deque([spawn])
    while q:
        cur = q.popleft()
        for prev in reverse.get(cur, []):
            if prev not in can_reach:
                can_reach.add(prev)
                q.append(prev)
    return all(p in can_reach for p in reachable)


def total_paintable(grid):
    return sum(row.count(FLOOR) for row in grid)


def solve_min_moves(grid, spawn, max_states=200000):
    target = total_paintable(grid)
    start_painted = frozenset([spawn])
    if len(start_painted) >= target:
        return 0
    start = (spawn, start_painted)
    visited = {start}
    q = deque([(spawn, start_painted, 0)])
    explored = 0
    while q and explored < max_states:
        pos, painted, depth = q.popleft()
        explored += 1
        for d in DIRS:
            stop, path = slide(grid, pos, d)
            if stop == pos:
                continue
            np = painted | set(path)
            if len(np) >= target:
                return depth + 1
            key = (stop, frozenset(np))
            if key not in visited:
                visited.add(key)
                q.append((stop, frozenset(np), depth + 1))
    return None


# ---- generation (mirrors GeneratedLevelProvider) ----
CFG = {
    "Easy":      dict(base=5, mx=7, frac=0.14, mn=2, prob_add=0.45),
    "Medium":    dict(base=7, mx=9, frac=0.20, mn=4, prob_add=0.55),
    "Hard":      dict(base=7, mx=9, frac=0.27, mn=6, prob_add=0.62),
    "ExtraHard": dict(base=9, mx=9, frac=0.32, mn=8, prob_add=0.68),
}


def board_size(cfg, index):
    g = cfg["base"] + (index - 1) // 12
    if g % 2 == 0:
        g += 1
    return max(5, min(g, cfg["mx"]))


def gen_room(rng, size, frac):
    """Open border ring kept as floor (guarantees return-navigation), obstacles
    scattered only in the interior. Obstacle count is a fraction of interior
    cells. This keeps the slide graph strongly connected far more often."""
    grid = [[FLOOR] * size for _ in range(size)]
    interior = [(r, c) for r in range(1, size - 1) for c in range(1, size - 1)]
    walls = round(frac * len(interior)) if interior else 0
    rng.shuffle(interior)
    for (r, c) in interior[:walls]:
        grid[r][c] = WALL
    floors = [(r, c) for r in range(size) for c in range(size) if grid[r][c] == FLOOR]
    if len(floors) < 3:
        return None, None
    return grid, rng.choice(floors)


def gen_mutate(rng, size, steps, prob_add):
    """Random walk through *valid* configurations: start from the always-valid
    serpentine, repeatedly flip a cell, and keep the flip only if the board stays
    never-stuck. prob_add biases toward adding walls (denser = harder)."""
    grid, spawn = gen_serpentine(size)
    for _ in range(steps):
        r, c = rng.randrange(size), rng.randrange(size)
        if (r, c) == spawn:
            continue
        old = grid[r][c]
        add = rng.random() < prob_add
        new = WALL if add else FLOOR
        if new == old:
            continue
        grid[r][c] = new
        if grid[spawn[0]][spawn[1]] == FLOOR and is_always_solvable(grid, spawn):
            continue  # accept
        grid[r][c] = old  # revert
    # Pick a valid spawn (prefer a fresh random one for variety).
    floors = [(r, c) for r in range(size) for c in range(size) if grid[r][c] == FLOOR]
    rng.shuffle(floors)
    for f in floors:
        if is_always_solvable(grid, f):
            return grid, f
    return grid, spawn


def gen_serpentine(size):
    grid = [[FLOOR] * size for _ in range(size)]
    for r in range(size):
        if r % 2 == 1:
            conn = size - 1 if (r // 2) % 2 == 0 else 0
            for c in range(size):
                grid[r][c] = FLOOR if c == conn else WALL
    return grid, (0, 0)


def get_level(name, index, attempts=12):
    """Mutation-based: always valid. Run a few seeds, keep the one meeting the
    min-move gate with the most walls (denser = more interesting)."""
    cfg = CFG[name]
    size = board_size(cfg, index)
    steps = size * size * 4
    best = None
    for a in range(attempts):
        rng = random.Random(hash((name, index, a)) & 0x7FFFFFFF)
        grid, spawn = gen_mutate(rng, size, steps, cfg["prob_add"])
        mm = solve_min_moves(grid, spawn)
        walls = sum(row.count(WALL) for row in grid)
        cand = (grid, spawn, mm, walls)
        if mm is not None and mm >= cfg["mn"]:
            if best is None or walls > best[3]:
                best = cand
        elif best is None:
            best = cand
    grid, spawn, mm, walls = best
    return grid, spawn, mm, False


def to_text(grid, spawn):
    out = []
    for r in range(len(grid)):
        row = []
        for c in range(len(grid[0])):
            if (r, c) == spawn:
                row.append("S")
            else:
                row.append("#" if grid[r][c] == WALL else ".")
        out.append("".join(row))
    return "\n".join(out)


if __name__ == "__main__":
    # 1. serpentine validity
    print("== serpentine validity ==")
    for size in (5, 7, 9, 11, 13):
        g, s = gen_serpentine(size)
        print(f"  size {size}: always_solvable={is_always_solvable(g, s)} minMoves={solve_min_moves(g, s)}")

    # 2. generator variety per mode (sample 1..30)
    print("== generator (levels 1..30 sampled) ==")
    for name in CFG:
        mms, wallcts, distinct = [], [], set()
        for i in range(1, 31):
            g, s, mm, _ = get_level(name, i)
            assert is_always_solvable(g, s), f"{name}#{i} invalid!"
            if mm is not None:
                mms.append(mm)
            wallcts.append(sum(row.count(WALL) for row in g))
            distinct.add(to_text(g, s))
        print(f"  {name:10s} minMoves[min/avg/max]={min(mms)}/{sum(mms)//len(mms)}/{max(mms)}"
              f"  walls[min/avg/max]={min(wallcts)}/{sum(wallcts)//len(wallcts)}/{max(wallcts)}"
              f"  distinct={len(distinct)}/30")
    # 3. sample render
    print("== sample Hard #5 ==")
    g, s, mm, _ = get_level("Hard", 5)
    print(to_text(g, s), f"\n  minMoves={mm}")
