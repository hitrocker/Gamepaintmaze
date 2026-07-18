"""Emit original, validated starter level packs as TextAssets under
Assets/Resources/Levels/. Levels are produced by our own generator and verified
never-stuck, then written in the human-readable pack format the in-game
LevelParser consumes. Re-run to regenerate.
"""
import os
from levelgen_check import get_level, is_always_solvable, to_text

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Levels")
MODES = [("Easy", "easy_pack.txt"),
         ("Medium", "medium_pack.txt"),
         ("Hard", "hard_pack.txt"),
         ("ExtraHard", "extrahard_pack.txt")]
PER_MODE = 8


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, fname in MODES:
        blocks = []
        for i in range(1, PER_MODE + 1):
            grid, spawn, mm, _ = get_level(name, i)
            assert is_always_solvable(grid, spawn), f"{name}#{i} invalid"
            blocks.append(f"{name} {i}  (min {mm} moves)\n{to_text(grid, spawn)}")
        header = (f"# {name} starter pack - original, generated & verified never-stuck.\n"
                  f"# S = spawn, . = floor, # = wall. Blocks separated by blank lines.\n")
        path = os.path.join(OUT_DIR, fname)
        with open(path, "w") as f:
            f.write(header + "\n" + "\n\n".join(blocks) + "\n")
        print(f"wrote {os.path.relpath(path)} ({PER_MODE} levels)")


if __name__ == "__main__":
    main()
