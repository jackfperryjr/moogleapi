# Frame ornament

`kupodle-frame.svg` — the carving on Kupodle's answer frame: a baroque gilt surround, built as a
replica of a reference frame.

One file for the whole thing, not repeating tiles. Baroque composition cannot be tiled: it is
plain rails with everything concentrated at the corners and the midpoint of each side, and an
outer silhouette broken by carving that projects past where the frame's edge would be. The file's
box is 14px larger than the frame on every side to give that projection somewhere to go, and the
CSS hands it back with `inset: -14px`.

## What is in it

| | |
|---|---|
| two bead courses | the reference steps down twice on the way in, the inner course finer than the outer |
| corner clusters | a fan of acanthus thrown out along the diagonal, a volute answering it, leaves running inboard down each rail and diminishing, berries beside them |
| centre crests | a fan projecting outward at the midpoint of all four sides, two scrolls turning back under it |
| plain flats | the long runs between, left bare — this is what makes the clusters read |

## Things worth knowing before editing it

**Everything must stay on the moulding.** Anything further from a corner than the moulding's width
in *both* axes is over the picture, not the frame. An early version put berry clusters at (27, 27)
on a 20px moulding and they sat on the artwork. The generator now asserts this: it counts points
falling inside the picture rectangle and the answer has to be zero.

**Piercing is free.** The gaps between blades are the whole look and they come from not filling,
not from cutting.

**Size is easy to lose.** The first emission was 116 KB, because ~100 beads were ten-point polygons
drawn once per relief layer. Beads are `<circle>` elements now and relief is two layers: 29 KB,
no visible difference.

## Tone

Pitched deliberately below the silhouette's own contrast. The first version was a bright gilt and
read as a lamp on a dark page — the frame was the first thing the eye went to, ahead of the puzzle
it surrounds.

The lit layer is filled with a `linearGradient` rather than being split into a second and third
copy of every polygon. That buys the same tonal range across the carving for a couple of hundred
bytes instead of another twenty-five kilobytes of points, and it is what gives the frame a
lit-from-the-top-left reading.

## Bump the ?v= stamp when you change it

This file is served with a thirty-day `max-age`, so replacing it at a stable name leaves everyone
who has already loaded the page looking at the previous carving for a month. That is not
hypothetical — it happened on the first replacement, and Cloudflare reported `HIT` with an age of
twenty-four minutes while the new file sat one cache-busted request away. The reference in
`kupodle/index.html` carries a `?v=` for this reason, the same as `game.js` does.

## Regenerating

Generated rather than hand-drawn, from one geometry description that emits both this SVG and a
raster preview. There is no SVG rasteriser on the machine this was made on, so without that shared
source the artwork could not be seen before it shipped. A missing file simply does not paint, and
the frame degrades to plain bronze moulding.
