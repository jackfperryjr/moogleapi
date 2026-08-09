/* Which of the two site themes is on, remembered across pages.
 *
 * LOAD THIS SYNCHRONOUSLY IN <head>, before any stylesheet that reacts to it. No defer, no async,
 * not at the end of <body>. The whole point is that the class is on <html> before the first paint;
 * anything later and every navigation flashes silver for a frame before turning blue, which is
 * worse than not persisting it at all.
 *
 * The class goes on the root element rather than on <body> for the same reason: <body> does not
 * exist yet when this runs.
 *
 * Storage can throw rather than return null — Safari in private browsing, and any browser with
 * third-party storage blocked in an iframe. A theme preference is not worth a broken page, so
 * both halves swallow it and the site falls back to silver.
 */
(function () {
  'use strict';

  var KEY = 'moogleapi-theme';
  var FF = 'ff4';

  function stored() {
    try {
      return window.localStorage.getItem(KEY);
    } catch (e) {
      return null;
    }
  }

  function remember(value) {
    try {
      window.localStorage.setItem(KEY, value);
    } catch (e) {
      /* Private mode or blocked storage. The theme still applies for this page. */
    }
  }

  var fontLoaded = false;

  /* Press Start 2P, fetched only when FFIV mode is actually on — it is a whole webfont and the
     silver theme never uses a glyph of it. Injected rather than linked in each page's <head> so
     there is one copy of the URL and one rule about when it loads. */
  function loadPixelFont() {
    if (fontLoaded) return;
    fontLoaded = true;

    var link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = 'https://fonts.googleapis.com/css2?family=Press+Start+2P&display=swap';
    (document.head || document.documentElement).appendChild(link);
  }

  function apply(on) {
    document.documentElement.classList.toggle('ff-mode', on);
    if (on) loadPixelFont();
  }

  apply(stored() === FF);

  window.moogleTheme = {
    /** Whether FFIV mode is currently on. */
    isFF: function () {
      return document.documentElement.classList.contains('ff-mode');
    },

    /**
     * Turns FFIV mode on or off and remembers the choice. Returns the new state.
     *
     * Callers that animate the change want to swap the class at a specific moment in their own
     * timeline, which is why this does the swap and nothing else — no transition, no timing.
     */
    set: function (on) {
      apply(on);
      remember(on ? FF : 'silver');
      return on;
    },

    toggle: function () {
      return window.moogleTheme.set(!window.moogleTheme.isFF());
    },

    /**
     * Fetches the pixel font ahead of the class going on. An animated toggle wants it in flight
     * before the swap, or the page re-lays-out in Press Start 2P a beat after the reveal and
     * reads as a bug rather than as an effect.
     */
    preloadFont: loadPixelFont,
  };
})();
