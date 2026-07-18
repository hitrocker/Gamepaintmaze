import unittest

from verify_packs import meets_open_layout, open_layout_metrics


REFERENCE_ARENA = [
    "..._....",
    "........",
    ".....##.",
    "....###.",
    "..#.....",
    "__......",
    "__S._...",
]

SAFE_RIBBON = [
    "S........",
    ".........",
    "########.",
    ".........",
    ".........",
    ".########",
    ".........",
    ".........",
]


class ArenaMetricParityTests(unittest.TestCase):
    def test_reference_fixture_matches_csharp_golden_values(self):
        metrics = open_layout_metrics(REFERENCE_ARENA)

        self.assertEqual(4, metrics["open_core_count"])
        self.assertEqual(24, metrics["open_core_cells"])
        self.assertAlmostEqual(24 / 44, metrics["open_core_ratio"])
        self.assertEqual(1, metrics["maximum_clearance"])
        self.assertAlmostEqual(10 / 44, metrics["degree4_ratio"])
        self.assertAlmostEqual(15 / 44, metrics["largest_open_core_ratio"])
        self.assertEqual(2, metrics["interior_wall_islands"])
        self.assertEqual(1.0, metrics["wall_island_ratio"])
        self.assertEqual(0.0, metrics["parallel_separator_ratio"])
        self.assertEqual(1, metrics["exterior_cut_depth"])
        self.assertEqual(5, metrics["concave_corners"])

    def test_reference_passes_and_safe_ribbon_fails(self):
        self.assertTrue(meets_open_layout(REFERENCE_ARENA)[0])
        self.assertFalse(meets_open_layout(SAFE_RIBBON)[0])


if __name__ == "__main__":
    unittest.main()
