/* The home page: the Try It bar, the live counts in the hero, and the chocobo theme wipe.
 *
 * LOAD THIS AT THE END OF <body>. No defer, no async, not in <head>. Everything below runs at
 * once against elements it expects to already exist — the Try It controls, #moogle-egg, #ff-menu
 * — so it has to come after the markup rather than wait for an event.
 *
 * Nothing here is exported. The Try It controls are wired with addEventListener at the bottom
 * instead of with onclick attributes in the markup, which is what lets the whole file sit inside
 * one scope: inline handlers resolve against the global object and would need these functions
 * hung off window to keep working.
 *
 * theme.js is the one dependency, and it is loaded synchronously in <head>, so window.moogleTheme
 * is always there by the time this runs.
 */
(() => {
  'use strict';

  const BASE = window.location.origin;

  // Which query parameters each endpoint actually accepts. Anything not listed here is
  // hidden rather than disabled, so the bar only ever shows controls that affect the call.
  const SUPPORTS = {
    'characters/search': { query: true,  game: true,  category: false },
    'monsters/search':   { query: true,  game: true,  category: true  },
    'games':             { query: false, game: false, category: false },
    'characters':        { query: false, game: true,  category: false },
    'monsters':          { query: false, game: true,  category: true  },
  };

  function buildUrl() {
    const resource = document.getElementById('try-resource').value;
    const supports = SUPPORTS[resource] ?? { query: false, game: false, category: false };
    const query    = document.getElementById('try-query').value.trim();
    const gameId   = document.getElementById('try-game').value;
    const category = document.getElementById('try-category').value;

    const params = new URLSearchParams();
    if (supports.query && query)       params.set('query', query);
    if (supports.game && gameId)       params.set('gameId', gameId);
    if (supports.category && category) params.set('category', category);

    const qs = params.toString();
    return `${BASE}/api/${resource}${qs ? '?' + qs : ''}`;
  }

  function updateTryUrl() {
    const resource = document.getElementById('try-resource').value;
    const supports = SUPPORTS[resource] ?? { query: false, game: false, category: false };
    const input    = document.getElementById('try-query');

    input.style.display = supports.query ? '' : 'none';
    document.getElementById('try-game').style.display     = supports.game ? '' : 'none';
    document.getElementById('try-category').style.display = supports.category ? '' : 'none';

    // Search needs a term to return anything, so say so before the call is made rather
    // than leaving an empty result array to be read as "this game has no monsters".
    input.placeholder = resource.startsWith('monsters')
      ? 'Search term, e.g. bomb — required'
      : 'Search term, e.g. Cloud — required';

    const url  = buildUrl();
    const hint = supports.query && !input.value.trim()
      ? ' <span class="try-hint">← add a search term, or switch to a list endpoint</span>'
      : '';

    document.getElementById('try-url-display').innerHTML = `GET <span>${url}</span>${hint}`;
  }

  // Populated from the API itself, so the ids in the dropdown are always the real ones.
  async function loadGameOptions() {
    const select = document.getElementById('try-game');
    try {
      const res  = await fetch(`${BASE}/api/games?pageSize=50`, { headers: { Accept: 'application/json' } });
      const data = await res.json();
      const games = data?.items ?? data?.Items ?? [];

      for (const g of games) {
        const option = document.createElement('option');
        option.value = g.id ?? g.Id;
        option.textContent = g.name ?? g.Name;
        select.appendChild(option);
      }
    } catch {
      // Leave the "All games" default in place — the tester still works unfiltered.
    }
  }

  async function runQuery() {
    const btn    = document.getElementById('try-btn');
    const result = document.getElementById('try-result');
    const url    = buildUrl();

    btn.disabled    = true;
    btn.textContent = '...';
    result.className = 'try-result';
    result.textContent = 'Fetching…';

    try {
      const res  = await fetch(url, { headers: { Accept: 'application/json' } });
      const data = await res.json();
      result.className  = res.ok ? 'try-result loaded' : 'try-result error';
      result.textContent = JSON.stringify(data, null, 2);
    } catch (e) {
      result.className  = 'try-result error';
      result.textContent = `Error: ${e.message}`;
    } finally {
      btn.disabled    = false;
      btn.textContent = 'Run →';
    }
  }

  for (const id of ['try-resource', 'try-game', 'try-category']) {
    document.getElementById(id).addEventListener('change', updateTryUrl);
  }
  document.getElementById('try-query').addEventListener('input', updateTryUrl);
  document.getElementById('try-query').addEventListener('keydown', e => {
    if (e.key === 'Enter') runQuery();
  });
  document.getElementById('try-btn').addEventListener('click', runQuery);

  updateTryUrl();
  loadGameOptions();

  // ── Final Fantasy Easter Egg ──
  function showNotify(html, duration = 3200) {
    const el = document.getElementById('ff-notify');
    el.innerHTML = html;
    el.classList.add('show');
    setTimeout(() => el.classList.remove('show'), duration);
  }

  let ffTransitioning = false;

  /* The chocobo run.
   *
   * One clock, shared with the CSS. The curtain is off-screen left, slides across, holds over the
   * viewport between 42% and 58% of its travel, and carries on out to the right; the theme swaps
   * inside that hold, which is the only window where nothing of either palette is visible. RUN and
   * the keyframes on #choco-curtain have to agree — change one and change the other.
   *
   * aria-hidden comes off #ff-menu at the same time as the class goes on, so the command list is
   * only in the accessibility tree while it is actually on screen. */
  const RUN  = 1500;
  const SWAP = Math.round(RUN * 0.5);

  /* The stage stays up after the curtain has left, because the slowest of the herd is still on
     it: the longest --dur plus the longest --delay comes to about 1.9s against the curtain's
     1.5s. Tearing the stage down at RUN chopped those stragglers off mid-stride in the middle of
     the screen. Letting them finish is also the better picture — the curtain goes, the new theme
     is there, and the last few chocobos are still crossing it. Keep this above the slowest lane
     in the markup. */
  const HERD = 2100;

  function toggleFFMode() {
    if (ffTransitioning) return;
    ffTransitioning = true;

    const wipe = document.getElementById('choco-wipe');
    const menu = document.getElementById('ff-menu');

    // Fetched before the run rather than at the swap: arriving late would re-lay-out the page in
    // Press Start 2P after the reveal, which looks like a bug rather than like an effect.
    window.moogleTheme.preloadFont();

    // Restarted from a clean slate, so a second toggle replays the animation instead of finding
    // it already finished.
    wipe.classList.remove('run');
    void wipe.offsetWidth;
    wipe.classList.add('run');

    setTimeout(() => {
      // moogleTheme owns the class and the localStorage write; this only picks the moment. The
      // games hub and the API reference read the same key on load, so the choice follows you off
      // this page — the four game pages deliberately do not, they keep their own palettes.
      const isFF = window.moogleTheme.toggle();
      menu.setAttribute('aria-hidden', String(!isFF));

      // The notify lands when the curtain clears, not when the last straggler does — waiting for
      // the herd would leave the page silent for half a second after it already looks finished.
      setTimeout(() => {
        if (isFF) {
          showNotify(
            'FINAL FANTASY MODE<br><br>' +
            '<span style="color:#f8d030;">Kupo!</span>',
            4000
          );
        } else {
          showNotify('Kweh!', 1800);
        }
      }, RUN - SWAP);

      setTimeout(() => {
        wipe.classList.remove('run');
        ffTransitioning = false;
      }, HERD - SWAP);
    }, SWAP);
  }

  document.getElementById('moogle-egg').addEventListener('click', toggleFFMode);

  // The way back out. FFIV mode hides the nav and the hero, and the moogle that turns the mode
  // on lives in the hero — so from inside the menu this button is the only exit that is not the
  // Konami code. It runs the full chocobo transition, same as the moogle does.
  document.getElementById('ff-restore').addEventListener('click', toggleFFMode);

  // theme.js set the class before paint, but what is not pure CSS still has to catch up on a page
  // that loaded already in FFIV mode: the menu's aria-hidden and the pixel font. No transition
  // here — the chocobos announce a change, and there was none.
  if (window.moogleTheme.isFF()) {
    window.moogleTheme.preloadFont();
    document.getElementById('ff-menu').setAttribute('aria-hidden', 'false');
  }

  // Konami code bonus trigger
  const KONAMI = ['ArrowUp','ArrowUp','ArrowDown','ArrowDown','ArrowLeft','ArrowRight','ArrowLeft','ArrowRight','KeyB','KeyA'];
  let konamiIdx = 0;
  document.addEventListener('keydown', e => {
    konamiIdx = e.code === KONAMI[konamiIdx] ? konamiIdx + 1 : (e.code === KONAMI[0] ? 1 : 0);
    if (konamiIdx === KONAMI.length) { toggleFFMode(); konamiIdx = 0; }
  });

  (async () => {
    try {
      const [chars, monsters, games] = await Promise.all([
        fetch(`${BASE}/api/characters?pageSize=1`, { headers: { Accept: 'application/json' } }).then(r => r.json()),
        fetch(`${BASE}/api/monsters?pageSize=1`,   { headers: { Accept: 'application/json' } }).then(r => r.json()),
        fetch(`${BASE}/api/games?pageSize=1`,       { headers: { Accept: 'application/json' } }).then(r => r.json()),
      ]);
      console.log('[data-summary]', { chars, monsters, games });
      const charCount    = chars?.totalCount    ?? chars?.TotalCount    ?? 0;
      const monsterCount = monsters?.totalCount ?? monsters?.TotalCount ?? 0;
      const gameCount    = games?.totalCount    ?? games?.TotalCount    ?? 0;
      const fmt = n => Number(n).toLocaleString();
      document.getElementById('data-summary').innerHTML =
        `<strong>${fmt(charCount)}</strong> characters &amp; <strong>${fmt(monsterCount)}</strong> monsters across <strong>${fmt(gameCount)}</strong> mainline games`;

      // The FFIV status window, where the original puts gil and time. Written whether or not
      // that mode is on — the menu is behind display:none until then, and filling it on toggle
      // instead would mean either holding the counts in a variable or fetching them twice.
      document.getElementById('ff-characters').textContent = fmt(charCount);
      document.getElementById('ff-monsters').textContent = fmt(monsterCount);
      document.getElementById('ff-games').textContent = fmt(gameCount);
    } catch (e) {
      console.warn('[data-summary] failed:', e);
      document.getElementById('data-summary').textContent = '';
    }
  })();
})();
