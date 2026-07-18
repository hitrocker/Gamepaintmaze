"""Feasibility sweep: how dense can a NEVER-STUCK board be?

Never-stuck == the exact property Solver.IsAlwaysSolvable checks:
  (1) every floor cell is covered by some slide path reachable from spawn, and
  (2) the reachable stop-graph is strongly connected (every reachable stop can
      roll back to spawn) -> move order can never strand the ball.

This script generates random dense boards and reports, per (size, hole_frac),
how often a never-stuck board is found and the resulting floor density.
"""
import random, time
from collections import deque

DIRS = [(-1, 0), (1, 0), (0, -1), (0, 1)]


def slide(grid, R, C, pos, d):
    dr, dc = d; r, c = pos; path = []
    while True:
        nr, nc = r + dr, c + dc
        if not (0 <= nr < R and 0 <= nc < C) or grid[nr][nc] != 1:
            break
        r, c = nr, nc; path.append((r, c))
    return (r, c), path


def floors(grid, R, C):
    return [(r, c) for r in range(R) for c in range(C) if grid[r][c] == 1]


def connected(grid, R, C):
    fl = set(floors(grid, R, C))
    if not fl:
        return False
    s = next(iter(fl)); seen = {s}; q = deque([s])
    while q:
        r, c = q.popleft()
        for dr, dc in DIRS:
            n = (r + dr, c + dc)
            if n in fl and n not in seen:
                seen.add(n); q.append(n)
    return len(seen) == len(fl)


def always_solvable(grid, R, C, start):
    """Port of Solver.IsAlwaysSolvable."""
    forward = {}
    reachable = {start}
    covered = {start}
    q = deque([start])
    while q:
        cur = q.popleft()
        for d in DIRS:
            stop, path = slide(grid, R, C, cur, d)
            if stop == cur:
                continue
            for p in path:
                covered.add(p)
            forward.setdefault(cur, []).append(stop)
            if stop not in reachable:
                reachable.add(stop); q.append(stop)

    # (1) coverage
    for cell in floors(grid, R, C):
        if cell not in covered:
            return False

    # (2) strong connectivity: spawn reachable FROM every reachable node
    reverse = {}
    for a, tos in forward.items():
        for b in tos:
            reverse.setdefault(b, []).append(a)
    can_reach_start = {start}
    rq = deque([start])
    while rq:
        cur = rq.popleft()
        for prev in reverse.get(cur, ()):
            if prev not in can_reach_start:
                can_reach_start.add(prev); rq.append(prev)
    for p in reachable:
        if p not in can_reach_start:
            return False
    return True


def gen(R, C, rng, hf):
    grid = [[1] * C for _ in range(R)]
    cells = [(r, c) for r in range(R) for c in range(C)]
    rng.shuffle(cells)
    for (r, c) in cells[:int(round(R * C * hf))]:
        grid[r][c] = 0
    if not connected(grid, R, C):
        return None
    return grid


def best_never_stuck(seed, R, C, hf, tries):
    rng = random.Random(seed & 0x7FFFFFFF)
    best = None
    for _ in range(tries):
        grid = gen(R, C, rng, hf)
        if grid is None:
            continue
        fl = floors(grid, R, C)
        # try each floor cell as spawn until one works (cheap-ish); shuffle order
        rng.shuffle(fl)
        for start in fl:
            if always_solvable(grid, R, C, start):
                density = len(floors(grid, R, C)) / (R * C)
                if best is None or density > best[0]:
                    best = (density, grid, start)
                break
    return best


if __name__ == "__main__":
    sizes = [(6, 7), (7, 8), (7, 9), (8, 10)]
    hfs = [0.10, 0.15, 0.20, 0.25, 0.30, 0.35]
    TRIES = 400
    for (C, R) in sizes:
        print(f"\n=== board {C}x{R} (W x H), tries={TRIES} ===")
        for hf in hfs:
            t0 = time.time()
            hits = 0; dens = []
            for s in range(40):
                b = best_never_stuck(1000 + s, R, C, hf, TRIES // 40 + 1)
                if b:
                    hits += 1; dens.append(b[0])
            avg = sum(dens) / len(dens) if dens else 0
            print(f"  hf={hf:.2f}: found {hits}/40  avg_density={avg:.2f}  "
                  f"({time.time()-t0:.1f}s)")
