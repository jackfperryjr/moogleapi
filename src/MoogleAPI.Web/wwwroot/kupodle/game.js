/* Kupodle — daily Final Fantasy character guessing.
 *
 * The answer is never fetched. Guesses go to POST /api/characters/daily/guess and come back
 * scored, so the only way this client learns the character is by solving it or running out of
 * attempts. That is why the puzzle parameters below must match what the server is asked for:
 * they select which puzzle series is being played.
 */
(() => {
  'use strict';

  const PUZZLE = { minPopularity: 90, requireImage: true };
  const MAX_GUESSES = 6;
  const API = '/api';

  const el = {
    boot:    document.getElementById('boot'),
    game:    document.getElementById('game'),
    input:   document.getElementById('guess-input'),
    submit:  document.getElementById('submit'),
    sugg:    document.getElementById('suggestions'),
    status:  document.getElementById('status'),
    guesses:   document.getElementById('guesses'),
    result:    document.getElementById('result'),
    stats:     document.getElementById('stats'),
    countdown: document.getElementById('countdown'),
    yesterday: document.getElementById('yesterday')
  };

  let pool = [];         // characters available to guess, for autocomplete
  let state = null;      // { date, guesses: [response], done, won }
  let highlighted = -1;
  let filtered = [];

  const qs = (o) => Object.entries(o).map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join('&');

  /* Versioned twice over: once because the day boundary moved from UTC to Central (an evening
     game was filed under the *next* UTC date), and again because the server's seed was
     reshuffled, so any board saved earlier was scored against a different answer. Neither can
     be migrated — the old entries have to be dropped. Must be bumped whenever
     PuzzleFilters.SeedVersion changes. */
  const STORE_VERSION = 'v3';
  const storeKey = (d) => `kupodle:${STORE_VERSION}:${d}`;

  /** Drops every Kupodle entry not written by the current store version. */
  function purgeLegacyState() {
    try {
      const prefix = `kupodle:${STORE_VERSION}:`;
      const stale = [];
      for (let i = 0; i < localStorage.length; i++) {
        const k = localStorage.key(i);
        if (k && k.startsWith('kupodle:') && !k.startsWith(prefix)) stale.push(k);
      }
      if (stale.length === 0) return;

      stale.forEach((k) => localStorage.removeItem(k));

      /* The stats record is keyed by date and refuses to count a day twice. Last night's
         session already stamped today's date, so without clearing that stamp the replayed
         puzzle would silently fail to record. Wins and streak are left untouched. */
      const raw = localStorage.getItem('moogle:stats:kupodle');
      if (raw) {
        const stats = JSON.parse(raw);
        stats.lastDate = null;
        localStorage.setItem('moogle:stats:kupodle', JSON.stringify(stats));
      }
    } catch { /* storage unavailable or corrupt — nothing to migrate */ }
  }

  /* The puzzle day runs midnight-to-midnight in Central time, not UTC.
   *
   * This is safe against the server's "no future puzzles" rule without any server change:
   * Central is always behind UTC, so a Central calendar date is never ahead of the server's
   * UTC date — it is either the same day or one behind. It does mean every request has to
   * carry the date explicitly, because the server's own default is UTC, and between 7pm and
   * midnight Central the two disagree. */
  const PUZZLE_TZ = 'America/Chicago';

  // 'en-CA' formats as YYYY-MM-DD, which is exactly the wire format the API expects.
  const puzzleDate = () => new Intl.DateTimeFormat('en-CA', {
    timeZone: PUZZLE_TZ, year: 'numeric', month: '2-digit', day: '2-digit'
  }).format(new Date());

  /** Milliseconds until the next Central midnight, DST included (Intl resolves the offset). */
  function msUntilReset() {
    const parts = Object.fromEntries(
      new Intl.DateTimeFormat('en-CA', {
        timeZone: PUZZLE_TZ, hour12: false,
        hour: '2-digit', minute: '2-digit', second: '2-digit'
      }).formatToParts(new Date())
       .filter((p) => p.type !== 'literal')
       .map((p) => [p.type, p.value]));

    // Some engines report midnight as hour 24 rather than 0.
    const hour = Number(parts.hour) % 24;
    const secondsIntoDay = hour * 3600 + Number(parts.minute) * 60 + Number(parts.second);
    return (86400 - secondsIntoDay) * 1000;
  }

  const shiftDay = (iso, days) => {
    const d = new Date(`${iso}T00:00:00Z`);
    d.setUTCDate(d.getUTCDate() + days);
    return d.toISOString().slice(0, 10);
  };

  // ── Reset clock ────────────────────────────────────────────────────────────
  function renderCountdown() {
    const remaining = msUntilReset();
    const mins = Math.max(0, Math.round(remaining / 60000));
    const h = Math.floor(mins / 60), m = mins % 60;

    // Shown in the viewer's own clock too, since "midnight Central" means a different hour
    // to anyone outside that zone.
    const localReset = new Date(Date.now() + remaining)
      .toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });

    el.countdown.innerHTML =
      `Next puzzle in <strong>${h}h ${m}m</strong> — resets at midnight Central (${localReset} your time).`;

    // If the day turned over while the tab sat open, pick up the new puzzle immediately
    // rather than leaving yesterday's board on screen.
    if (state && state.date !== puzzleDate()) location.reload();
  }

  async function renderYesterday(date) {
    const prev = shiftDay(date, -1);
    try {
      const res = await fetch(`${API}/characters/daily?${qs({ date: prev, ...PUZZLE })}`);
      if (!res.ok) return;
      const d = await res.json();
      el.yesterday.innerHTML =
        `Yesterday (${prev}) was <strong>${escapeHtml(d.name)}</strong> — ${escapeHtml(d.gameName)}.`;
    } catch { /* a missing previous day just leaves the line blank */ }
  }

  // ── State persistence ──────────────────────────────────────────────────────
  // Keyed by date so a new day starts clean without needing an expiry sweep.
  function load(date) {
    try {
      const raw = localStorage.getItem(storeKey(date));
      if (raw) return JSON.parse(raw);
    } catch { /* corrupt or unavailable storage — start fresh */ }
    return { date, guesses: [], done: false, won: false };
  }

  function save() {
    try { localStorage.setItem(storeKey(state.date), JSON.stringify(state)); }
    catch { /* private mode: the game still works, it just won't survive a refresh */ }
  }

  // ── Rendering ──────────────────────────────────────────────────────────────
  const ATTRS = [
    ['gameName',    'Game',        (g) => g.gameName],
    ['releaseYear', 'Year',        (g) => g.releaseYear],
    ['race',        'Race',        (g) => g.race],
    ['hometown',    'Hometown',    (g) => g.hometown],
    ['role',        'Role',        (g) => g.role],
    ['affiliation', 'Affiliation', (g) => g.affiliation]
  ];

  // Arrows are additive, not a substitute for the colour: "Higher" already reads as a miss.
  function tileClass(verdict) {
    if (verdict === 'Correct') return 'hit';
    if (verdict === 'Higher' || verdict === 'Lower') return 'near';
    return 'miss';
  }

  function tileValue(verdict, value) {
    const shown = (value === null || value === undefined || value === '') ? '—' : value;
    if (verdict === 'Higher') return `${shown} ↑`;
    if (verdict === 'Lower')  return `${shown} ↓`;
    return shown;
  }

  function renderGuess(res) {
    const g = res.guess;
    const node = document.createElement('div');
    node.className = 'guess';

    const head = document.createElement('div');
    head.className = 'guess-head';
    if (g.imageUrl) {
      const img = document.createElement('img');
      img.className = 'art-frame';
      img.src = g.imageUrl;
      img.alt = '';
      img.loading = 'lazy';
      head.appendChild(img);
    }
    const name = document.createElement('span');
    name.className = 'guess-name';
    name.textContent = g.name;
    head.appendChild(name);

    const no = document.createElement('span');
    no.className = 'guess-no';
    no.textContent = `${res.guessNumber}/${MAX_GUESSES}`;
    head.appendChild(no);
    node.appendChild(head);

    const tiles = document.createElement('div');
    tiles.className = 'tiles';
    for (const [key, label, get] of ATTRS) {
      const verdict = res.comparison[key];
      const t = document.createElement('div');
      t.className = `tile ${tileClass(verdict)}`;
      const k = document.createElement('span');
      k.className = 'k';
      k.textContent = label;
      const v = document.createElement('span');
      v.className = 'v';
      v.textContent = tileValue(verdict, get(g));
      // Colour alone can't carry the verdict for screen readers.
      t.append(k, v);
      t.setAttribute('aria-label', `${label}: ${v.textContent}, ${verdict}`);
      t.title = `${label}: ${v.textContent}`;   // full text when the value is clamped
      tiles.appendChild(t);
    }
    node.appendChild(tiles);
    return node;
  }

  function renderAll() {
    el.guesses.replaceChildren(...state.guesses.map(renderGuess));

    const left = MAX_GUESSES - state.guesses.length;
    el.status.textContent = state.done
      ? ''
      : `${left} ${left === 1 ? 'guess' : 'guesses'} remaining.`;

    el.input.disabled = state.done;
    el.submit.disabled = state.done || !matchedCharacter();

    MoogleStats.render(el.stats, 'kupodle', ['played', 'wins', 'winPct', 'streak', 'best']);

    if (state.done) renderResult();
  }

  function shareGrid() {
    // One row per guess, one square per attribute — the shape of the solve, not the answer.
    return state.guesses.map((res) =>
      ATTRS.map(([key]) => {
        const v = res.comparison[key];
        if (v === 'Correct') return '🟩';
        if (v === 'Higher' || v === 'Lower') return '🟨';
        return '⬛';
      }).join('')
    ).join('\n');
  }

  function renderResult() {
    const answer = state.answer;
    el.result.hidden = false;
    el.result.replaceChildren();

    const h = document.createElement('h2');
    h.textContent = state.won
      ? `Solved in ${state.guesses.length}!`
      : 'Out of guesses';
    el.result.appendChild(h);

    if (answer) {
      if (answer.imageUrl) {
        const img = document.createElement('img');
        img.className = 'art-frame';
        img.src = answer.imageUrl;
        img.alt = answer.name;
        el.result.appendChild(img);
      }
      const p = document.createElement('p');
      p.innerHTML = `The character was <strong>${escapeHtml(answer.name)}</strong> — ${escapeHtml(answer.gameName)} (${answer.releaseYear}).`;
      el.result.appendChild(p);
    }

    const grid = document.createElement('pre');
    grid.className = 'share-grid';
    grid.textContent = shareGrid();
    el.result.appendChild(grid);

    const actions = document.createElement('div');
    actions.className = 'actions';

    const share = document.createElement('button');
    share.className = 'btn';
    share.textContent = 'Copy result';
    share.addEventListener('click', async () => {
      const text = `Kupodle ${state.date} ${state.won ? state.guesses.length : 'X'}/${MAX_GUESSES}\n\n${shareGrid()}\n\nmoogleapi.com/kupodle`;
      try {
        await navigator.clipboard.writeText(text);
        share.textContent = 'Copied!';
        setTimeout(() => { share.textContent = 'Copy result'; }, 1800);
      } catch {
        share.textContent = 'Copy failed';
      }
    });
    actions.appendChild(share);
    el.result.appendChild(actions);
  }

  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, (c) =>
      ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }

  // ── Autocomplete ───────────────────────────────────────────────────────────
  function alreadyGuessed(id) {
    return state.guesses.some((res) => res.guess.id === id);
  }

  function matchedCharacter() {
    const typed = el.input.value.trim().toLowerCase();
    if (!typed) return null;
    return pool.find((c) => c.name.toLowerCase() === typed && !alreadyGuessed(c.id)) || null;
  }

  function closeSuggestions() {
    el.sugg.hidden = true;
    el.sugg.replaceChildren();
    el.input.setAttribute('aria-expanded', 'false');
    highlighted = -1;
    filtered = [];
  }

  function openSuggestions() {
    const typed = el.input.value.trim().toLowerCase();
    if (!typed) return closeSuggestions();

    filtered = pool
      .filter((c) => c.name.toLowerCase().includes(typed) && !alreadyGuessed(c.id))
      .slice(0, 8);

    if (filtered.length === 0) return closeSuggestions();

    el.sugg.replaceChildren(...filtered.map((c, i) => {
      const li = document.createElement('li');
      li.role = 'option';
      li.id = `sugg-${i}`;
      li.setAttribute('aria-selected', String(i === highlighted));
      if (c.imageUrl) {
        const img = document.createElement('img');
        img.className = 'art-frame';
        img.src = c.imageUrl; img.alt = ''; img.loading = 'lazy';
        li.appendChild(img);
      }
      const n = document.createElement('span');
      n.textContent = c.name;
      const g = document.createElement('span');
      g.className = 'sg';
      g.textContent = c.gameName;
      li.append(n, g);
      li.addEventListener('mousedown', (e) => { e.preventDefault(); pick(c); });
      return li;
    }));
    el.sugg.hidden = false;
    el.input.setAttribute('aria-expanded', 'true');
  }

  function pick(c) {
    el.input.value = c.name;
    closeSuggestions();
    el.submit.disabled = false;
    el.submit.focus();
  }

  function moveHighlight(delta) {
    if (filtered.length === 0) return;
    highlighted = (highlighted + delta + filtered.length) % filtered.length;
    [...el.sugg.children].forEach((li, i) =>
      li.setAttribute('aria-selected', String(i === highlighted)));
    el.input.setAttribute('aria-activedescendant', `sugg-${highlighted}`);
  }

  // ── Submitting ─────────────────────────────────────────────────────────────
  async function submitGuess() {
    const character = matchedCharacter();
    if (!character || state.done) return;

    el.submit.disabled = true;
    el.status.textContent = 'Checking…';

    try {
      const res = await fetch(`${API}/characters/daily/guess`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          guessId: character.id,
          guessNumber: state.guesses.length + 1,
          // Required, not optional: the server would otherwise fall back to the UTC date,
          // which disagrees with the Central puzzle day every evening after 7pm.
          date: state.date,
          ...PUZZLE
        })
      });

      if (!res.ok) throw new Error(`Server returned ${res.status}`);
      const data = await res.json();

      state.guesses.push(data);
      if (data.correct) { state.done = true; state.won = true; }
      else if (data.guessNumber >= MAX_GUESSES) { state.done = true; }
      if (data.answer) state.answer = data.answer;

      // Passing the puzzle date makes this idempotent — reloading a finished day
      // can't inflate the record, and the streak counts days rather than wins.
      if (state.done) {
        MoogleStats.record('kupodle', state.won ? 'win' : 'loss', {
          date: state.date,
          bucket: state.won ? state.guesses.length : 'X'
        });
      }

      el.input.value = '';
      closeSuggestions();
      save();
      renderAll();
    } catch (err) {
      el.status.textContent = '';
      showError(`Could not check that guess. ${err.message}`);
      el.submit.disabled = false;
    }
  }

  function showError(message) {
    const box = document.createElement('div');
    box.className = 'error';
    box.textContent = message;
    el.game.prepend(box);
    setTimeout(() => box.remove(), 6000);
  }

  // ── Boot ───────────────────────────────────────────────────────────────────
  async function init() {
    purgeLegacyState();
    const date = puzzleDate();
    state = load(date);

    try {
      // One bulk load powers the autocomplete for the whole session.
      const res = await fetch(`${API}/characters?${qs({ ...PUZZLE, pageSize: 500 })}`);
      if (!res.ok) throw new Error(`Server returned ${res.status}`);
      const data = await res.json();
      pool = data.items.sort((a, b) => a.name.localeCompare(b.name));
    } catch (err) {
      el.boot.className = 'error';
      el.boot.textContent = `Could not load the character list. ${err.message}`;
      return;
    }

    el.boot.hidden = true;
    el.game.hidden = false;

    el.input.addEventListener('input', () => {
      highlighted = -1;
      openSuggestions();
      el.submit.disabled = !matchedCharacter();
    });

    el.input.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowDown')      { e.preventDefault(); moveHighlight(1); }
      else if (e.key === 'ArrowUp')   { e.preventDefault(); moveHighlight(-1); }
      else if (e.key === 'Escape')    { closeSuggestions(); }
      else if (e.key === 'Enter') {
        e.preventDefault();
        if (highlighted >= 0 && filtered[highlighted]) pick(filtered[highlighted]);
        else if (matchedCharacter()) submitGuess();
      }
    });

    el.input.addEventListener('blur', () => setTimeout(closeSuggestions, 120));
    el.submit.addEventListener('click', submitGuess);

    renderAll();
    renderCountdown();
    setInterval(renderCountdown, 30000);
    renderYesterday(date);
  }

  init();
})();
