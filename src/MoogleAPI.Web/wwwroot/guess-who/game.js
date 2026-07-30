/* Guess Who — deduction over the Final Fantasy character set.
 *
 * Single-player, so the mystery character is chosen in the browser. That's deliberate: unlike
 * Kupodle there's no shared daily answer to protect, and keeping it local means a round costs
 * exactly one API call.
 */
(() => {
  'use strict';

  const API = '/api';
  const BOARD_SIZE = 24;
  const POOL = { minPopularity: 85, requireImage: true };

  // Excluded for the same reason its cards were dropped from Triple Triad: it's an MMO, and
  // its roster is both enormous and unfamiliar to most single-player-series players.
  const EXCLUDED_GAMES = ['Final Fantasy XIV'];

  const el = {
    boot:    document.getElementById('boot'),
    game:    document.getElementById('game'),
    attr:    document.getElementById('attr'),
    value:   document.getElementById('value'),
    ask:     document.getElementById('ask'),
    grid:    document.getElementById('grid'),
    log:     document.getElementById('log'),
    status:  document.getElementById('status'),
    outcome: document.getElementById('outcome'),
    qCount:  document.getElementById('q-count'),
    leftCount: document.getElementById('left-count'),
    stats:   document.getElementById('stats'),
    newGame: document.getElementById('new-game')
  };

  // Attribute -> how to read it off a character. Release decade is derived rather than
  // stored, because asking about a single year almost never eliminates anyone.
  const ATTRS = [
    { key: 'gameName',    label: 'Game',        get: (c) => c.gameName },
    { key: 'releaseDecade', label: 'Release decade', get: (c) => `${Math.floor(c.releaseYear / 10) * 10}s` },
    { key: 'race',        label: 'Race',        get: (c) => c.race },
    { key: 'hometown',    label: 'Hometown',    get: (c) => c.hometown },
    { key: 'role',        label: 'Role',        get: (c) => c.role },
    { key: 'affiliation', label: 'Affiliation', get: (c) => c.affiliation }
  ];

  let pool = [];
  let board = [];
  let secret = null;
  let eliminated = new Set();
  let questions = 0;
  let over = false;

  const qs = (o) => Object.entries(o).map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join('&');

  function shuffle(arr) {
    const a = arr.slice();
    for (let i = a.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [a[i], a[j]] = [a[j], a[i]];
    }
    return a;
  }

  const remaining = () => board.filter((c) => !eliminated.has(c.id));

  // ── Question options ───────────────────────────────────────────────────────
  /* Only offer values held by at least two characters still standing. A value unique to one
     person turns the question into a free win, and a value nobody holds wastes a turn. */
  function valuesFor(attrKey) {
    const attr = ATTRS.find((a) => a.key === attrKey);
    const counts = new Map();
    for (const c of remaining()) {
      const v = attr.get(c);
      if (v === null || v === undefined || v === '') continue;
      counts.set(v, (counts.get(v) || 0) + 1);
    }
    return [...counts.entries()]
      .filter(([, n]) => n >= 2)
      .map(([v, n]) => ({ value: v, count: n }))
      .sort((a, b) => b.count - a.count || String(a.value).localeCompare(String(b.value)));
  }

  function refreshValueOptions() {
    const opts = valuesFor(el.attr.value);
    el.value.replaceChildren(...opts.map(({ value, count }) => {
      const o = document.createElement('option');
      o.value = value;
      o.textContent = `${value} — ${count} left`;
      return o;
    }));

    const none = opts.length === 0;
    el.value.disabled = none;
    el.ask.disabled = none || over;
    if (none) {
      const o = document.createElement('option');
      o.textContent = 'No useful questions left';
      el.value.replaceChildren(o);
    }
  }

  // ── Rendering ──────────────────────────────────────────────────────────────
  function renderBoard() {
    el.grid.replaceChildren(...board.map((c) => {
      const out = eliminated.has(c.id);
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = `face art-frame${out ? ' out' : ''}`;
      if (over && c.id === secret.id) btn.classList.add('answer');
      btn.disabled = out || over;

      const img = document.createElement('img');
      img.src = c.imageUrl;
      img.alt = '';
      img.loading = 'lazy';

      const n = document.createElement('span');
      n.className = 'fname';
      n.textContent = c.name;

      btn.append(img, n);
      btn.setAttribute('aria-label',
        out ? `${c.name}, eliminated` : `Accuse ${c.name} (${c.gameName})`);
      btn.addEventListener('click', () => accuse(c));
      return btn;
    }));

    el.qCount.textContent = String(questions);
    el.leftCount.textContent = String(remaining().length);
  }

  function addLog(text, yes) {
    const li = document.createElement('li');
    li.className = yes ? 'yes' : 'no';
    li.textContent = `${text} — ${yes ? 'Yes' : 'No'}`;
    el.log.prepend(li);
  }

  // ── Actions ────────────────────────────────────────────────────────────────
  function askQuestion() {
    if (over) return;
    const attr = ATTRS.find((a) => a.key === el.attr.value);
    const value = el.value.value;
    if (!value) return;

    const answerIsYes = String(attr.get(secret) ?? '') === value;
    questions++;

    // Eliminate everyone the answer rules out — keeps the board honest and saves
    // the player from tracking it by hand.
    for (const c of remaining()) {
      const matches = String(attr.get(c) ?? '') === value;
      if (matches !== answerIsYes) eliminated.add(c.id);
    }

    addLog(`${attr.label} is ${value}?`, answerIsYes);
    renderBoard();
    refreshValueOptions();

    const left = remaining();
    if (left.length === 1) {
      el.status.textContent = 'Only one left — click their face to finish.';
    } else {
      el.status.textContent = `${left.length} characters still standing.`;
    }
  }

  /* Full attribute set for the revealed character. Shows the exact release year rather than
     the decade used for questions — by this point there's nothing left to protect, and the
     year is the more useful detail. */
  function infoPanel(character) {
    const fields = [
      ['Game',        character.gameName],
      ['Released',    character.releaseYear],
      ['Race',        character.race],
      ['Hometown',    character.hometown],
      ['Role',        character.role],
      ['Affiliation', character.affiliation]
    ];

    const grid = document.createElement('div');
    grid.className = 'info';

    for (const [label, value] of fields) {
      const cell = document.createElement('div');
      const k = document.createElement('span');
      k.className = 'k';
      k.textContent = label;
      const v = document.createElement('span');
      v.className = 'v';
      // Missing attributes are common in the source data; an em dash reads better than a gap.
      v.textContent = (value === null || value === undefined || value === '') ? '—' : value;
      cell.append(k, v);
      cell.title = `${label}: ${v.textContent}`;
      grid.appendChild(cell);
    }
    return grid;
  }

  function accuse(c) {
    if (over) return;
    over = true;
    const won = c.id === secret.id;

    el.outcome.hidden = false;
    el.outcome.replaceChildren();

    const h = document.createElement('h2');
    h.textContent = won ? 'Correct!' : 'Wrong!';
    el.outcome.appendChild(h);

    const img = document.createElement('img');
    img.className = 'art-frame';
    img.src = secret.imageUrl;
    img.alt = secret.name;
    el.outcome.appendChild(img);

    const p = document.createElement('p');
    p.textContent = won
      ? `You found ${secret.name} in ${questions} ${questions === 1 ? 'question' : 'questions'}.`
      : `You accused ${c.name}. It was ${secret.name}.`;
    el.outcome.appendChild(p);

    el.outcome.appendChild(infoPanel(secret));

    el.ask.disabled = true;
    el.status.textContent = '';

    MoogleStats.record('guess-who', won ? 'win' : 'loss', { bucket: questions });
    renderStats();
    renderBoard();
  }

  function renderStats() {
    MoogleStats.render(el.stats, 'guess-who', ['played', 'wins', 'losses', 'winPct', 'streak', 'best']);
  }

  // ── Boot ───────────────────────────────────────────────────────────────────
  /* Names are only unique per game ("Dancing girl" and "Cid" recur across the series), and two
     identically-labelled faces make an accusation ambiguous. One per name. */
  function pickBoard() {
    const seen = new Set();
    const chosen = [];
    for (const c of shuffle(pool)) {
      const key = c.name.toLowerCase();
      if (seen.has(key)) continue;
      seen.add(key);
      chosen.push(c);
      if (chosen.length === BOARD_SIZE) break;
    }
    return chosen;
  }

  function newRound() {
    board = pickBoard();
    secret = board[Math.floor(Math.random() * board.length)];
    eliminated = new Set();
    questions = 0;
    over = false;
    el.outcome.hidden = true;
    el.log.replaceChildren();
    el.status.textContent = `${board.length} characters. Ask a question to start narrowing down.`;
    renderBoard();
    refreshValueOptions();
    renderStats();
  }

  async function init() {
    try {
      const res = await fetch(`${API}/characters?${qs({ ...POOL, pageSize: 500 })}`);
      if (!res.ok) throw new Error(`Server returned ${res.status}`);
      const data = await res.json();
      pool = data.items.filter((c) => c.imageUrl && !EXCLUDED_GAMES.includes(c.gameName));
      if (pool.length < BOARD_SIZE) throw new Error('Not enough characters with portraits.');
    } catch (err) {
      el.boot.className = 'error';
      el.boot.textContent = `Could not load the line-up. ${err.message}`;
      return;
    }

    el.boot.hidden = true;
    el.game.hidden = false;

    el.attr.replaceChildren(...ATTRS.map((a) => {
      const o = document.createElement('option');
      o.value = a.key;
      o.textContent = a.label;
      return o;
    }));

    el.attr.addEventListener('change', refreshValueOptions);
    el.ask.addEventListener('click', askQuestion);
    el.newGame.addEventListener('click', newRound);

    newRound();
  }

  init();
})();
