/* The home page: the ground toggle, the live counts, the Try It bar, and the FFIV easter egg
 * with its chocobo transition.
 *
 * LOAD THIS AT THE END OF <body>. No defer, no async, not in <head>. Everything below runs at
 * once against elements it expects to already exist — the Try It controls, #ff-hotspot,
 * #ff-menu, #choco-wipe — so it has to come after the markup rather than wait for an event.
 *
 * Nothing here is exported. The controls are wired with addEventListener rather than with
 * onclick attributes, which is what lets the whole file sit inside one scope: inline handlers
 * resolve against the global object and would need these functions hung off window.
 *
 * theme.js is the one dependency, and it is loaded synchronously in <head>, so both
 * window.moogleTheme and window.moogleGround are there by the time this runs.
 */
(() => {
  'use strict';

  const BASE = window.location.origin;

  /* ── Ground: light / dark ──────────────────────────────────────────────────────────────
   * theme.js owns the storage, the resolution and the OS listener — it has to, because the
   * ground must be stamped before first paint and this file runs at the end of <body>. All
   * that is left here is the button.
   *
   * The label advertises what the button will DO, not what is on: "☾ Dark" while the page is
   * light. Repainted on the media query too, because while the preference is still 'system' an
   * OS change flips the ground under us and the button would otherwise offer the wrong thing. */
  const icon  = document.getElementById('ground-icon');
  const label = document.getElementById('ground-label');

  function paintToggle() {
    const dark = window.moogleTheme.ground() === 'dark';
    icon.textContent  = dark ? '☀' : '☾';
    label.textContent = dark ? 'Light' : 'Dark';
  }

  document.getElementById('ground-toggle').addEventListener('click', () => {
    window.moogleTheme.toggleGround();
    paintToggle();
  });

  window.matchMedia('(prefers-color-scheme: dark)')
    .addEventListener('change', () => paintToggle());

  paintToggle();

  /* ── FFIV ──────────────────────────────────────────────────────────────────────────────
   * The way IN is the moogle at the head of the wordmark — a transparent hotspot over the
   * leading glyph, x 12–105 of an 1878px mark. It replaced #moogle-egg, the moogle that used
   * to be spliced into the h1, when the mark took over the hero.
   *
   * The way OUT has to be #ff-restore. FFIV hides the nav and the hero, and the moogle goes
   * with them, so from inside the menu that button is the only exit that is not the Konami
   * code. It runs the same transition. */
  function showNotify(html, duration = 3200) {
    const el = document.getElementById('ff-notify');
    el.innerHTML = html;
    el.classList.add('show');
    setTimeout(() => el.classList.remove('show'), duration);
  }

  let ffTransitioning = false;

  /* One clock, shared with the CSS. The curtain slides in from the left, holds over the
     viewport between 42% and 58% of its travel, and carries on out to the right; the theme
     swaps inside that hold, the only window where neither palette is visible. RUN and the
     keyframes on #choco-curtain have to agree.

     HERD outlasts RUN because the slowest of the herd is still crossing after the curtain has
     gone — longest --dur plus longest --delay is about 1.9s against the curtain's 1.5s. */
  const RUN  = 1500;
  const SWAP = Math.round(RUN * 0.5);
  const HERD = 2100;

  function toggleFFMode() {
    if (ffTransitioning) return;
    ffTransitioning = true;

    const wipe = document.getElementById('choco-wipe');
    const menu = document.getElementById('ff-menu');

    window.moogleTheme.preloadFont();

    wipe.classList.remove('run');
    void wipe.offsetWidth;
    wipe.classList.add('run');

    setTimeout(() => {
      const isFF = window.moogleTheme.toggle();
      menu.setAttribute('aria-hidden', String(!isFF));

      setTimeout(() => {
        if (isFF) {
          showNotify('FINAL FANTASY MODE<br><br><span style="color:#f8d030;">Kupo!</span>', 4000);
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

  document.getElementById('ff-hotspot').addEventListener('click', toggleFFMode);
  document.getElementById('ff-restore').addEventListener('click', toggleFFMode);

  /* theme.js set the class before paint, but what is not pure CSS still has to catch up on a
     page that loaded already in FFIV: the menu's aria-hidden and the pixel font. No transition
     — the chocobos announce a change, and there was none. */
  if (window.moogleTheme.isFF()) {
    window.moogleTheme.preloadFont();
    document.getElementById('ff-menu').setAttribute('aria-hidden', 'false');
  }

  const KONAMI = ['ArrowUp','ArrowUp','ArrowDown','ArrowDown','ArrowLeft','ArrowRight','ArrowLeft','ArrowRight','KeyB','KeyA'];
  let konamiIdx = 0;
  document.addEventListener('keydown', e => {
    konamiIdx = e.code === KONAMI[konamiIdx] ? konamiIdx + 1 : (e.code === KONAMI[0] ? 1 : 0);
    if (konamiIdx === KONAMI.length) { toggleFFMode(); konamiIdx = 0; }
  });

  /* ── Live counts ───────────────────────────────────────────────────────────────────────
   * One call each, pageSize=1, read for totalCount only. Fills both the band under the rail
   * and the per-collection record counts. */
  const fmt = n => Number(n).toLocaleString();

  (async () => {
    try {
      const [chars, monsters, games] = await Promise.all(
        ['characters', 'monsters', 'games'].map(r =>
          fetch(`${BASE}/api/${r}?pageSize=1`, { headers: { Accept: 'application/json' } })
            .then(res => res.json()))
      );

      const counts = {
        characters: chars?.totalCount    ?? chars?.TotalCount    ?? 0,
        monsters:   monsters?.totalCount ?? monsters?.TotalCount ?? 0,
        games:      games?.totalCount    ?? games?.TotalCount    ?? 0,
      };

      /* Three places now: the counts band, the per-collection record counts, and the FFIV
         status window. The status window is written whether or not that mode is on — it is
         behind display:none until then, and filling it on toggle instead would mean either
         holding the counts in a variable or fetching them twice. */
      for (const [name, value] of Object.entries(counts)) {
        document.getElementById(`n-${name}`).textContent = fmt(value);
        document.getElementById(`cn-${name}`).textContent = fmt(value);
        document.getElementById(`ff-${name}`).textContent = fmt(value);
      }
    } catch (e) {
      console.warn('[counts] failed:', e);
    }
  })();

  /* ── Try It ────────────────────────────────────────────────────────────────────────────
   * Which query parameters each endpoint actually accepts. Anything not listed is hidden
   * rather than disabled, so the bar only ever shows controls that affect the call. */
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

    input.placeholder = resource.startsWith('monsters')
      ? 'Search term, e.g. bomb — required'
      : 'Search term, e.g. Cloud — required';

    const url  = buildUrl();
    const hint = supports.query && !input.value.trim()
      ? ' <span class="try-hint">← add a search term, or switch to a list endpoint</span>'
      : '';

    document.getElementById('try-url-display').innerHTML = `GET <span>${url}</span>${hint}`;
  }

  async function loadGameOptions() {
    const select = document.getElementById('try-game');
    try {
      const res  = await fetch(`${BASE}/api/games?pageSize=50`, { headers: { Accept: 'application/json' } });
      const data = await res.json();
      for (const g of (data?.items ?? data?.Items ?? [])) {
        const option = document.createElement('option');
        option.value = g.id ?? g.Id;
        option.textContent = g.name ?? g.Name;
        select.appendChild(option);
      }
    } catch {
      /* Leave "All games" in place — the tester still works unfiltered. */
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
      result.className   = res.ok ? 'try-result loaded' : 'try-result error';
      result.textContent = JSON.stringify(data, null, 2);
    } catch (e) {
      result.className   = 'try-result error';
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

  loadGameOptions();
  updateTryUrl();
})();
