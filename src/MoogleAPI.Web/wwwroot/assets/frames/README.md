# Frame ornament

The carved detail on Kupodle's answer frame: chocobo scrollwork running each side, a moogle at
each corner.

Separate files rather than data URIs in the stylesheet. The CSS is inline in `kupodle/index.html`,
which is served `no-cache` so it revalidates on every request — 40 KB of encoded SVG would have
been paid for on every page load. As files they fall under the static-asset rule instead and are
cached for thirty days.

## Regenerating

These are generated, not hand-drawn: one geometry description emits both the SVG and a raster
preview, so what gets reviewed is the same drawing that ships rather than a sketch of it. A
missing file simply does not paint, and the frame degrades to plain bronze moulding.

| file | tile | notes |
|---|---|---|
| `moogle-corner.svg` | 20x20 | symmetric, so one file serves all four corners |
| `chocobo-top.svg` | 32x20 | repeats along the top |
| `chocobo-bottom.svg` | 32x20 | the same tile mirrored vertically |
| `chocobo-left.svg` | 20x32 | rotated for the vertical runs |
| `chocobo-right.svg` | 20x32 | rotated and mirrored |

Corners are painted over the edge runs so the repeat is never visibly cut off at a corner.
