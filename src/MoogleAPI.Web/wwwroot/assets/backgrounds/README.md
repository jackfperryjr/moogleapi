# Page backgrounds

Full-page artwork for the game pages. Referenced from each page's `background-image`, layered
*over* the gradients beneath it and under a fixed dimming veil (`body::before`).

A missing file simply does not paint, so a page with no artwork here degrades to its gradients
with nothing to guard against — add and remove these freely.

## What to supply

| | |
|---|---|
| **Size** | **2400 × 1350** (16:9) |
| **Format** | WebP, quality ~80 |
| **Weight** | under ~400 KB |

Sized for `background-size: cover` on a fixed attachment, so it is cropped rather than letterboxed
and 16:9 is what wastes least across the phones and desktops that actually load it. 2400 wide
covers a maximised window on a 2K display without upscaling; larger buys nothing a veil this dark
would show.

## What works at this size

The page is dark and the artwork sits under a 72–95% veil, so it reads as atmosphere rather than
as a picture:

- **Mid-to-dark source images.** A bright one fights the veil and ends up flat grey.
- **Detail at the edges, calm in the middle.** The arena sits centred over roughly the middle
  third; fine detail there is covered by the fight and wasted.
- **Broad shape over fine texture.** Anything smaller than a few dozen pixels disappears.

If a supplied image still reads too strong, raise the veil in `body::before` rather than
re-exporting — that is the knob it exists for.

## `tiles/` — the same artwork on the hub

`/games/` uses these as the card backgrounds, in the default theme only. They are **separate,
smaller crops, not the files above**: the two page backgrounds are 6.8 MB and 10.3 MB, and a hub
that loaded four of those would be ~25 MB to draw four cards. The four crops together are 201 KB.

| | |
|---|---|
| **Size** | **880 × 600**, centre-cropped from the full image |
| **Format** | WebP, quality ~72 |

Rebuild them from the originals rather than exporting by hand, and **re-measure if the veil
changes** — a card is black body text over a picture, which is the easy way to make one unreadable.

The artwork **covers the whole card**, which means the card is dark and its copy is light: there
is no veil strong enough to carry black text over a full-bleed picture without becoming the wash
that idea started as. The scrim in `games/index.html` deepens downward instead — 6% at the top,
94% under the text block at the bottom — so four sources with wildly different brightness all end
up near-black under the copy, which is what lets one set of text colours work across the set.

Against the real crops that leaves the name at 10.9:1 (6.8:1 if a wrapped description pushes it
up), the description at 12.3:1 and the footer link at 5.2:1 in the worst case. Three earlier
attempts looked fine and failed the measurement, so do not trust your eye on this one.

The tile copy is one line per game on purpose: the text is bottom-aligned, so every line of copy
is a line of artwork covered up.

FFIV mode replaces the tile background wholesale with its window gradient, so none of this applies
there.

## Current files

- `battle-square.webp` — Cloud mid-swing in the arena. 2399 × 1350.
- `sphere-hunter.webp` — the roster lined up on the plain. 2738 × 1536.
- `kupodle.webp` — a wall of framed portraits, all silhouetted but Lightning. 1376 × 768.
- `triple-triad.webp` — a felt table mid-game under a lamp. 1376 × 768. **The cards on it are
  the catalogue's own `cards/{id}.webp` art**, composited onto a generated bare table rather than
  drawn by the model: a backdrop for the card game should be showing the cards the API serves.
  Each is dimmed to match the felt sampled underneath it, which is what makes the paste sit in
  the picture instead of on top of it.

The last two are **below the 2400-wide spec** and deliberately not upscaled — that is the size they
were generated at, and inventing pixels would cost weight for nothing under a veil this dark. They
are also **not yet wired to their game pages**; only the hub crops are in use. Kupodle and Triple
Triad each paint their own gradients and would need a `background-image` line adding to pick these
up, the way `battle-square/` and `sphere-hunter/` already do.
