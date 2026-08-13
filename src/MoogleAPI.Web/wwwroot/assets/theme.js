/* Which theme is on, remembered across pages.
 *
 * There are TWO INDEPENDENT AXES and they are stored under two different keys on purpose:
 *
 *   moogleapi-ground  'light' | 'dark' | 'system'   → data-theme on <html>
 *   moogleapi-theme   'silver' | 'ff4'              → .ff-mode on <html>
 *
 * One key for both would mean turning on dark mode silently cleared someone's FFIV mode, and
 * vice versa. It also keeps the upgrade free: anyone already carrying 'ff4' in the old key is
 * untouched, because the ground key is new and simply absent for them.
 *
 * LOAD THIS SYNCHRONOUSLY IN <head>, before any stylesheet that reacts to it. No defer, no async,
 * not at the end of <body>. The whole point is that both are on <html> before the first paint;
 * anything later and every navigation flashes the wrong theme for a frame, which is worse than
 * not persisting it at all.
 *
 * They go on the root element rather than on <body> for the same reason: <body> does not exist
 * yet when this runs.
 *
 * The ground is stamped on every page that loads this file, including the games hub and the API
 * reference. That is harmless where nothing reads it — an attribute with no matching selector
 * changes nothing — and it means those pages get dark mode for free the day their CSS grows the
 * tokens for it.
 *
 * Storage can throw rather than return null — Safari in private browsing, and any browser with
 * third-party storage blocked in an iframe. A theme preference is not worth a broken page, so
 * every access swallows it and the site falls back to silver on a light ground.
 */
(function () {
  'use strict';

  var KEY = 'moogleapi-theme';
  var FF = 'ff4';
  var GROUND_KEY = 'moogleapi-ground';

  function read(key) {
    try {
      return window.localStorage.getItem(key);
    } catch (e) {
      return null;
    }
  }

  function write(key, value) {
    try {
      window.localStorage.setItem(key, value);
    } catch (e) {
      /* Private mode or blocked storage. The choice still applies for this page. */
    }
  }

  function stored() {
    return read(KEY);
  }

  function remember(value) {
    write(KEY, value);
  }

  /* 'system' is the default, so a first visit follows the OS and the toggle only ever pins an
     override. Anything that is not an explicit 'light' or 'dark' resolves against the media
     query, which also covers a junk value left by an older build. */
  function resolveGround(pref) {
    if (pref === 'light' || pref === 'dark') return pref;
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  function applyGround(pref) {
    document.documentElement.dataset.theme = resolveGround(pref);
  }

  applyGround(read(GROUND_KEY) || 'system');

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

    /* ── The ground axis ── */

    /** The resolved ground currently painted: 'light' or 'dark'. */
    ground: function () {
      return document.documentElement.dataset.theme || resolveGround('system');
    },

    /** The stored preference, which may be 'system' where ground() never is. */
    groundPref: function () {
      return read(GROUND_KEY) || 'system';
    },

    /**
     * Pins the ground to 'light' or 'dark', or hands it back to the OS with 'system'.
     * Returns the resolved value actually applied.
     */
    setGround: function (pref) {
      applyGround(pref);
      write(GROUND_KEY, pref);
      return window.moogleTheme.ground();
    },

    toggleGround: function () {
      return window.moogleTheme.setGround(
        window.moogleTheme.ground() === 'dark' ? 'light' : 'dark'
      );
    },
  };

  /* Follow the OS while, and only while, the preference is still 'system'. Once the toggle has
     pinned a ground, an OS change must not yank it back. */
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
    if (!read(GROUND_KEY)) applyGround('system');
  });
})();
