# Frame ornament

The carving on Kupodle's answer frame: a baroque gilt surround with a moogle head at each corner.

`kupodle-frame.svg` is the whole overlay in one file, not a set of repeating tiles. Baroque
composition is the reason — plain rails with everything happening at the corners and the midpoint
of each side, and an outer silhouette broken by scrollwork that projects past where the frame's
edge would be. None of that survives being cut into tiles.

The file's box is 14px larger than the frame on every side, which is the room that projection
needs; the CSS gives it back with `inset: -14px` on the pseudo-element.

## What is in it

| | |
|---|---|
| beading | the small regular course on the inner lip — the detail that most says "gilt frame" |
| corner cartouches | acanthus running inboard along each rail, mirrored into all four corners |
| moogle heads | one per corner, drawn separately from the cartouches |
| centre crests | a palmette at the midpoint of all four sides, projecting outward |

## Two things that were learned the hard way

**The heads are upright in all four corners.** The foliage mirrors; the faces do not. Mirroring
them vertically leaves two moogles upside down, and carved frames never invert a face.

**The eyes are cut, not drawn.** A moogle head in flat silhouette reads as a bow or a bat — a
round shape, two points and a bobble. Carving two eyes into it in the shadow tone is what makes it
a face, and it is the only element in the frame that needs the treatment; a leaf is legible as a
leaf.

## Regenerating

Generated rather than hand-drawn, from one geometry description that emits both this SVG and a
raster preview — there is no SVG rasteriser on the machine this was made on, so without that
shared source the artwork could not be seen before it shipped. A missing file simply does not
paint, and the frame degrades to plain bronze moulding.
