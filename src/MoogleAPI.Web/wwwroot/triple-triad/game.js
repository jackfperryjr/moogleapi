/* Triple Triad — the base Final Fantasy VIII ruleset.
 *
 * Rules implemented: 3x3 board, five cards each, capture by comparing the two facing numbers
 * of adjacent cards. The optional rules (Same, Plus, Combo, Elemental, Open/Random) are not
 * applied — Elemental icons are shown on the cards but do not affect scoring.
 */
(() => {
  'use strict';

  const API = '/api';
  const SIZE = 3;
  const HAND = 5;
  const PLAYER = 'player';
  const OPPONENT = 'opponent';

  const el = {
    boot:    document.getElementById('boot'),
    game:    document.getElementById('game'),
    board:   document.getElementById('board'),
    youHand: document.getElementById('you-hand'),
    cpuHand: document.getElementById('cpu-hand'),
    scoreYou: document.getElementById('score-you'),
    scoreCpu: document.getElementById('score-cpu'),
    status:  document.getElementById('status'),
    outcome: document.getElementById('outcome'),
    stats:   document.getElementById('stats'),
    newGame: document.getElementById('new-game')
  };

  let deck = [];
  let board = [];        // 9 slots: null | { card, owner }
  let hands = { player: [], opponent: [] };
  let turn = PLAYER;
  let selected = null;   // index into hands.player (click-to-place)
  let dragIndex = null;  // index into hands.player (drag-to-place)
  let over = false;

  // Adjacency: for each direction, the offset and which edge of each card meets.
  // "mine" is the placed card's edge, "theirs" is the neighbour's facing edge.
  const DIRS = [
    { dr: -1, dc:  0, mine: 'top',    theirs: 'bottom' },
    { dr:  1, dc:  0, mine: 'bottom', theirs: 'top'    },
    { dr:  0, dc: -1, mine: 'left',   theirs: 'right'  },
    { dr:  0, dc:  1, mine: 'right',  theirs: 'left'   }
  ];

  const idx = (r, c) => r * SIZE + c;
  const inBounds = (r, c) => r >= 0 && r < SIZE && c >= 0 && c < SIZE;
  const shown = (n) => (n === 10 ? 'A' : String(n));

  function shuffle(arr) {
    const a = arr.slice();
    for (let i = a.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [a[i], a[j]] = [a[j], a[i]];
    }
    return a;
  }

  // ── Rules ──────────────────────────────────────────────────────────────────
  /** Flips every adjacent enemy card this placement beats. Returns how many flipped. */
  function resolveCaptures(state, pos, owner) {
    const r = Math.floor(pos / SIZE), c = pos % SIZE;
    const placed = state[pos].card;
    const flipped = [];

    for (const d of DIRS) {
      const nr = r + d.dr, nc = c + d.dc;
      if (!inBounds(nr, nc)) continue;

      const neighbour = state[idx(nr, nc)];
      if (!neighbour || neighbour.owner === owner) continue;

      if (placed[d.mine] > neighbour.card[d.theirs]) {
        neighbour.owner = owner;
        flipped.push(idx(nr, nc));
      }
    }
    return flipped;
  }

  function scores() {
    let you = hands.player.length;
    let cpu = hands.opponent.length;
    for (const slot of board) {
      if (!slot) continue;
      if (slot.owner === PLAYER) you++; else cpu++;
    }
    return { you, cpu };
  }

  const emptyCells = (state) => state
    .map((s, i) => (s === null ? i : -1))
    .filter((i) => i >= 0);

  // ── Opponent AI ────────────────────────────────────────────────────────────
  /* Greedy with a defensive tie-break: take the most captures, and among equal options
     prefer keeping strong cards in hand and exposing the lowest edges. Good enough to
     punish careless play without being unbeatable. */
  function chooseMove() {
    const cells = emptyCells(board);
    let best = null;

    for (let h = 0; h < hands.opponent.length; h++) {
      const card = hands.opponent[h];
      for (const pos of cells) {
        // Simulate on a shallow copy: slots are replaced, never mutated in place.
        const sim = board.map((s) => (s ? { card: s.card, owner: s.owner } : null));
        sim[pos] = { card, owner: OPPONENT };
        const gained = resolveCaptures(sim, pos, OPPONENT).length;

        const exposure = exposedStrength(sim, pos, card);
        const score = gained * 100 - exposure - cardPower(card) * 0.4;

        if (!best || score > best.score) best = { hand: h, pos, score };
      }
    }
    return best;
  }

  /** Sum of the card's edges that face an empty cell — how vulnerable it is next turn. */
  function exposedStrength(state, pos, card) {
    const r = Math.floor(pos / SIZE), c = pos % SIZE;
    let total = 0;
    for (const d of DIRS) {
      const nr = r + d.dr, nc = c + d.dc;
      if (!inBounds(nr, nc)) continue;
      if (state[idx(nr, nc)] === null) total += 10 - card[d.mine];
    }
    return total;
  }

  const cardPower = (c) => c.top + c.left + c.right + c.bottom;

  // ── Rendering ──────────────────────────────────────────────────────────────
  function cardNode(card, owner, { faceDown = false, selectable = false, isSelected = false } = {}) {
    const node = document.createElement('button');
    node.type = 'button';
    node.className = 'card';
    if (owner) node.classList.add(`owner-${owner}`);
    if (selectable) node.classList.add('selectable');
    if (isSelected) node.classList.add('selected');

    if (faceDown) {
      node.classList.add('facedown');
      node.setAttribute('aria-label', 'Opponent card, face down');
      node.disabled = true;
      return node;
    }

    if (card.imageUrl) {
      const art = document.createElement('img');
      art.className = 'art';
      art.src = card.imageUrl;
      art.alt = '';
      // The wiki CDN 404s any request carrying a Referer, so send none. Kept for the copied
      // originals: the regenerated faces are served from our own bucket, which does not care.
      art.referrerPolicy = 'no-referrer';
      // The rank numbers below used to be hidden the moment this loaded, because the 1999 card
      // faces have their values printed on. The regenerated faces are drawn without them on
      // purpose, so the overlay is the real thing now and the has-art class that suppressed it
      // is gone — nothing else referenced it.
      art.addEventListener('error', () => art.remove());
      node.appendChild(art);
    }

    // Appended after the art so it paints over it.
    const tint = document.createElement('span');
    tint.className = 'tint';
    node.appendChild(tint);

    const vals = document.createElement('span');
    vals.className = 'vals';
    for (const [k, cls] of [['top', 'v-top'], ['left', 'v-left'], ['right', 'v-right'], ['bottom', 'v-bottom']]) {
      const s = document.createElement('span');
      s.className = cls;
      s.textContent = shown(card[k]);
      vals.appendChild(s);
    }
    node.appendChild(vals);

    if (card.element) {
      const e = document.createElement('span');
      e.className = 'elem';
      e.textContent = card.element;
      node.appendChild(e);
    }

    const name = document.createElement('span');
    name.className = 'cname';
    name.textContent = card.name;
    node.appendChild(name);

    node.setAttribute('aria-label',
      `${card.name}. Top ${shown(card.top)}, left ${shown(card.left)}, ` +
      `right ${shown(card.right)}, bottom ${shown(card.bottom)}.` +
      (owner ? ` Owned by ${owner === PLAYER ? 'you' : 'opponent'}.` : ''));

    return node;
  }

  function render(flashed = []) {
    // Board
    el.board.replaceChildren(...board.map((slot, i) => {
      const cell = document.createElement('div');
      cell.className = 'cell';
      if (slot) {
        const c = cardNode(slot.card, slot.owner);
        c.disabled = true;
        if (flashed.includes(i)) c.classList.add('flipping');
        cell.appendChild(c);
      } else if (!over && turn === PLAYER) {
        // An empty cell is a drop target for the whole of your turn, whether or not a card
        // is click-selected — dragging shouldn't require selecting first.
        cell.addEventListener('dragover', (e) => {
          if (dragIndex === null) return;
          e.preventDefault();                       // required to allow a drop
          e.dataTransfer.dropEffect = 'move';
          cell.classList.add('dragover');
        });
        cell.addEventListener('dragleave', () => cell.classList.remove('dragover'));
        cell.addEventListener('drop', (e) => {
          e.preventDefault();
          cell.classList.remove('dragover');
          const from = dragIndex ?? Number(e.dataTransfer.getData('text/plain'));
          if (!Number.isNaN(from)) playerPlace(i, from);
        });

        // Click and keyboard placement stay available: touch devices don't fire HTML5
        // drag events at all, so drag can't be the only way to play a card.
        if (selected !== null) {
          cell.classList.add('playable');
          cell.setAttribute('role', 'button');
          cell.tabIndex = 0;
          cell.setAttribute('aria-label', `Play here, row ${Math.floor(i / SIZE) + 1} column ${i % SIZE + 1}`);
          const play = () => playerPlace(i);
          cell.addEventListener('click', play);
          cell.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); play(); }
          });
        }
      }
      return cell;
    }));

    // Hands
    el.youHand.replaceChildren(...hands.player.map((card, i) => {
      const wrap = document.createElement('div');
      wrap.className = 'card-slot';
      const myTurn = !over && turn === PLAYER;
      const node = cardNode(card, PLAYER, {
        selectable: myTurn,
        isSelected: selected === i
      });
      node.disabled = !myTurn;
      node.addEventListener('click', () => {
        selected = selected === i ? null : i;
        render();
      });

      if (myTurn) {
        node.draggable = true;
        node.classList.add('draggable');
        node.addEventListener('dragstart', (e) => {
          dragIndex = i;
          // Carry the index in the payload too, so a drop still resolves if the
          // module-level value is lost (e.g. a re-render mid-drag).
          e.dataTransfer.setData('text/plain', String(i));
          e.dataTransfer.effectAllowed = 'move';
          node.classList.add('dragging');
        });
        node.addEventListener('dragend', () => {
          dragIndex = null;
          node.classList.remove('dragging');
          document.querySelectorAll('.cell.dragover')
            .forEach((c) => c.classList.remove('dragover'));
        });
      }

      wrap.appendChild(node);
      return wrap;
    }));

    el.cpuHand.replaceChildren(...hands.opponent.map(() => {
      const wrap = document.createElement('div');
      wrap.className = 'card-slot';
      wrap.appendChild(cardNode(null, OPPONENT, { faceDown: true }));
      return wrap;
    }));

    const s = scores();
    el.scoreYou.innerHTML = `<span class="pip"></span> You ${s.you}`;
    el.scoreCpu.innerHTML = `<span class="pip"></span> CPU ${s.cpu}`;
    el.scoreYou.classList.toggle('turn', !over && turn === PLAYER);
    el.scoreCpu.classList.toggle('turn', !over && turn === OPPONENT);

    MoogleStats.render(el.stats, 'triple-triad',
      ['played', 'wins', 'losses', 'draws', 'winPct', 'streak', 'best']);

    if (!over) {
      el.status.textContent = turn === PLAYER
        ? (selected === null
            ? 'Drag a card onto the board — or tap one to select it, then tap a square.'
            : 'Choose a square.')
        : 'Opponent is thinking…';
    }
  }

  // ── Turn flow ──────────────────────────────────────────────────────────────
  function playerPlace(pos, handIndex = selected) {
    if (over || turn !== PLAYER || handIndex === null || board[pos]) return;
    if (handIndex < 0 || handIndex >= hands.player.length) return;

    const card = hands.player.splice(handIndex, 1)[0];
    selected = null;
    dragIndex = null;
    board[pos] = { card, owner: PLAYER };
    const flipped = resolveCaptures(board, pos, PLAYER);

    turn = OPPONENT;
    render(flipped);
    setTimeout(finishOrContinue, 550);
  }

  function opponentPlace() {
    const move = chooseMove();
    if (!move) return finishOrContinue();

    const card = hands.opponent.splice(move.hand, 1)[0];
    board[move.pos] = { card, owner: OPPONENT };
    const flipped = resolveCaptures(board, move.pos, OPPONENT);

    turn = PLAYER;
    render(flipped);
    checkEnd();
  }

  function finishOrContinue() {
    if (checkEnd()) return;
    if (turn === OPPONENT) opponentPlace();
  }

  function checkEnd() {
    if (emptyCells(board).length > 0) return false;

    over = true;
    const s = scores();
    el.status.textContent = '';
    el.outcome.hidden = false;
    el.outcome.replaceChildren();

    const h = document.createElement('h2');
    h.textContent = s.you > s.cpu ? 'You win!' : s.you < s.cpu ? 'You lose' : 'Draw';
    const p = document.createElement('p');
    p.textContent = `Final score — you ${s.you}, opponent ${s.cpu}.`;
    el.outcome.append(h, p);

    MoogleStats.record('triple-triad',
      s.you > s.cpu ? 'win' : s.you < s.cpu ? 'loss' : 'draw');

    render();
    return true;
  }

  // ── Boot ───────────────────────────────────────────────────────────────────
  function deal() {
    const picked = shuffle(deck).slice(0, HAND * 2);
    hands = { player: picked.slice(0, HAND), opponent: picked.slice(HAND) };
    board = Array(SIZE * SIZE).fill(null);
    selected = null;
    dragIndex = null;
    over = false;
    el.outcome.hidden = true;

    // Coin flip for the opening move — going first is a real advantage here,
    // since whoever starts places the fifth card on the last empty square.
    turn = Math.random() < 0.5 ? PLAYER : OPPONENT;
    render();
    if (turn === OPPONENT) setTimeout(opponentPlace, 700);
  }

  async function init() {
    try {
      const res = await fetch(`${API}/cards?pageSize=200`);
      if (!res.ok) throw new Error(`Server returned ${res.status}`);
      const data = await res.json();
      deck = data.items;
      if (deck.length < HAND * 2) throw new Error('Not enough cards in the deck.');
    } catch (err) {
      el.boot.className = 'error';
      el.boot.textContent = `Could not load the card deck. ${err.message}`;
      return;
    }

    el.boot.hidden = true;
    el.game.hidden = false;
    el.newGame.addEventListener('click', deal);
    deal();
  }

  init();
})();
