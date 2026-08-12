/* The games hub: the fallback for a game tile whose logo has not been drawn yet.
 *
 * LOAD THIS AT THE END OF <body>. It walks the tiles once on execution rather than waiting for
 * an event, so the markup has to be there already.
 */
(() => {
  'use strict';

  // Logos are dropped in per game and may not exist yet. What replaces a missing one depends
  // on whether the tile has artwork behind it:
  //
  //   with artwork    the slot goes. The picture covers the whole card, so 88px of reserved
  //                   space is 88px of artwork hidden, and the game's name is already the line
  //                   at the bottom — printing a wordmark here put the same word on the card
  //                   twice, which is what every tile did while /assets/logos/ did not exist.
  //   without         the name in the display face, as before, so a bare tile is not blank.
  for (const slot of document.querySelectorAll('.tile-art')) {
    const img = slot.querySelector('img');
    if (!img) continue;

    const fallback = () => {
      slot.replaceChildren();

      const tile = slot.closest('.tile');
      const art = tile && getComputedStyle(tile).getPropertyValue('--tile-bg').trim();
      if (art && art !== 'none') { slot.hidden = true; return; }

      const mark = document.createElement('div');
      mark.className = 'tile-wordmark';
      mark.textContent = slot.dataset.wordmark ?? '';
      slot.appendChild(mark);
    };

    img.addEventListener('error', fallback);
    // A cached failure can land before the listener is attached, so check the decoded state too.
    if (img.complete && img.naturalWidth === 0) fallback();
  }
})();
