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

## Current files

- `battle-square.webp` — the Gold Saucer. *Not yet supplied; the page runs on its gradients until
  it is.*
- `kupo-climb.webp` — the climb. *Not yet supplied; same.* Wants height in the frame — a ridgeline,
  a tower, something with a top — since the page is lit from its summit and the gradients beneath
  are built around an ascent.
