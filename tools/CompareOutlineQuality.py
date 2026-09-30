"""Summarize TextQualityProbe gdi-outlines fixtures; requires Pillow and numpy.

This checks geometry compatibility, not displayed FPS or perceived sharpness.
"""
import csv
import html
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image


def compare(folder):
    def rows(name):
        with (folder / name).open(encoding="utf-8-sig") as source:
            return {row["id"]: row for row in csv.DictReader(source)}

    baseline = rows("gdi-metrics.csv")
    candidate = rows("gdi-outline-metrics.csv")
    if baseline.keys() != candidate.keys() or len(baseline) != 40:
        raise ValueError("Expected the same 40 fixture IDs in both outputs")
    results = []
    cards = []
    for key, old in sorted(baseline.items()):
        new = candidate[key]
        width_error = abs(float(old["gdi_width"]) - float(new["dwrite_width"]))
        height_error = abs(float(old["gdi_height"]) - float(new["dwrite_height"]))
        # Managed CSV rounds to three decimals; native CSV to six significant
        # digits (0.005 pixels at these 4K fixture widths).
        if max(width_error, height_error) > .006:
            raise ValueError("Original geometry bounds changed: " + key)
        a = np.asarray(Image.open(folder / (key + "-gdi.png")).convert("RGB"), dtype=float)
        b = np.asarray(Image.open(folder / (key + "-gdi-outline.png")).convert("RGB"), dtype=float)
        if a.shape != b.shape:
            raise ValueError("Output dimensions changed: " + key)
        row = dict(id=key, width_error_px=width_error, height_error_px=height_error,
                   mean_rgb_difference=float(np.abs(a-b).mean()))
        results.append(row)
        cards.append('<section><h2>' + html.escape(key) + '</h2><p>Current bitmap</p><img src="' +
                     key + '-gdi.png"><p>Direct2D, same shaped outlines</p><img src="' +
                     key + '-gdi-outline.png"></section>')
    report = dict(fixtures=len(results), geometry_bounds_preserved=True,
                  note="Software WIC output. No hardware/frame-rate claim. RGB difference measures change, not quality.",
                  results=results)
    (folder / "outline-comparison.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    (folder / "outline-comparison.html").write_text(
        '<!doctype html><meta charset="utf-8"><title>Same-outline text comparison</title>'
        '<style>body{background:#161d2e;color:white;font:16px sans-serif}section{margin:32px 0}'
        'img{display:block;max-width:none}p{margin:8px 0}</style>'
        '<h1>Same-outline text comparison</h1><p>Images shown at their native pixel size. '
        'This compares rasterization only, not animation cadence.</p>' + ''.join(cards), encoding="utf-8")
    print(json.dumps(dict(fixtures=len(results), geometry_bounds_preserved=True,
                         max_mean_rgb_difference=max(r["mean_rgb_difference"] for r in results))))


if __name__ == "__main__":
    compare(Path(sys.argv[1]))
