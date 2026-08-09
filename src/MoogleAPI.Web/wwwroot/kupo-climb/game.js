'use strict';

/**
 * Kupo Climb — a ladder of Final Fantasy monster battles, one game per rung.
 *
 * The server hands over the whole run in a single call: every rung, both sides, and their
 * moves. Combat then resolves entirely here, so a twelve-rung run costs one request and
 * survives a flaky connection.
 */

const API = '/api/battle';

/** Shown when a row has no artwork. The API reports that as a null imageUrl rather than
 *  substituting anything, so that "which rows still need art?" stays answerable from the
 *  catalogue itself; picking the stand-in is the client's job. */
const NO_ART = '/assets/no-image.svg';

/** The damage model arrives with the run rather than living here. The server vets every
 *  matchup as winnable using this same arithmetic, so a second copy of the constants in the
 *  browser would drift and quietly break that guarantee. These values are only a fallback for
 *  a payload from an older server. */
let rules = {
  damageShare: 0.30, weaknessMultiplier: 2, minRatio: 0.2, maxRatio: 4, ratioScale: 0.5,
  poisonShare: 0.06, blindMultiplier: 0.5, statusTurns: 3,
};

/** The square root of the advantage, not offence/(offence+guard) — that expression cannot exceed
 *  1 however large the advantage grows, so past about four-to-one extra attack bought nothing and
 *  no fight could ever end in fewer than four turns. */
const ratio = (offence, guard) =>
  Math.min(rules.maxRatio,
           Math.max(rules.minRatio, rules.ratioScale * Math.sqrt(offence / Math.max(1, guard))));

/**
 * The three conditions a battle can inflict. Each carries its own wording because "Chimera is
 * poisoned" and "Chimera is silenced" are the only part of a status the player actually reads.
 */
const STATUS = {
  Poison: {
    label: 'Poison',
    verb: 'poisoned',
    hint: () => `Loses ${Math.round(rules.poisonShare * 100)}% of max HP after acting.`,
  },
  Blind: {
    label: 'Blind',
    verb: 'blinded',
    hint: () => `Physical moves land for ${Math.round(rules.blindMultiplier * 100)}% damage.`,
  },
  Silence: {
    label: 'Silence',
    verb: 'silenced',
    hint: () => 'Magic moves are locked out. Attack is physical, so a turn is never lost.',
  },
};

/** No randomness anywhere in combat: the same choices always produce the same battle, so a
 *  shared result is honest and two players comparing runs see identical fights. */
const state = {
  run: null,
  rungIndex: 0,
  battleIndex: 0,
  results: [],      // per rung: array of true/false, indexed by battle
  retries: 0,       // second chances left for the whole run, not this rung
  retriesUsed: 0,
  player: null,     // { fighter, hp, statuses }
  opponent: null,
  busy: false,
  starters: [],
};

const $ = (id) => document.getElementById(id);

// ── Boot ───────────────────────────────────────────────────────────────────

async function boot() {
  try {
    const res = await fetch(`${API}/starters`, { headers: { Accept: 'application/json' } });
    if (!res.ok) throw new Error(`Starters unavailable (${res.status})`);

    const data = await res.json();
    const starters = data.starters ?? [];
    if (!starters.length) throw new Error('No monsters are ready to fight yet.');

    state.starters = starters;
    renderStarters(starters);
    $('boot').hidden = true;
    $('select').hidden = false;
  } catch (err) {
    fail(err.message);
  }
}

function fail(message) {
  $('boot').hidden = true;
  const box = $('error');
  box.textContent = message;
  box.hidden = false;
}

function renderStarters(starters) {
  const grid = $('starter-grid');
  grid.innerHTML = '';

  starters.forEach((s, i) => {
    const card = document.createElement('button');
    card.className = 'starter';
    card.type = 'button';
    card.dataset.index = String(i);
    // The tooltip is the card's own description rather than a separate region, so a screen
    // reader reads the stats when the card takes focus instead of announcing a stray panel.
    card.setAttribute('aria-describedby', 'starter-tip');
    card.innerHTML = `
      <img class="art-frame" alt="" loading="lazy" src="${escapeAttr(s.imageUrl ?? NO_ART)}" />
      <span class="starter-name">${escapeHtml(s.family)}</span>
      <span class="starter-games">${s.gameCount} games</span>`;
    card.addEventListener('click', () => startRun(s.family));
    grid.appendChild(card);
  });
}

// ── Starter tooltip ────────────────────────────────────────────────────────
// Hover alone would leave the whole comparison unreachable by keyboard, so focus opens it too,
// and the panel pins to the grid rather than to the pointer.

function starterTipHtml(s) {
  const f = s.startingForm;
  if (!f) return `<div class="tip-name">${escapeHtml(s.family)}</div>`;

  const stat = (label, value) => `<div class="tip-stat"><span>${label}</span><b>${value}</b></div>`;

  const affinities = [
    ...(f.weaknesses ?? []).map((w) => `<span class="tag weak">weak: ${escapeHtml(w)}</span>`),
    ...(f.absorbs ?? []).map((a) => `<span class="tag absorb">absorbs: ${escapeHtml(a)}</span>`),
  ].join('') || '<span class="tip-none">no elemental affinities</span>';

  return `
    <div class="tip-name">${escapeHtml(s.family)}</div>
    <div class="tip-sub">${escapeHtml(f.gameName)} form · ${s.gameCount} rungs</div>
    <div class="tip-stats">
      ${stat('HP', f.hitPoints)}${stat('ATK', f.attack)}${stat('DEF', f.defense)}
      ${stat('MAG', f.magicAttack)}${stat('MDF', f.magicDefense)}${stat('SPD', f.speed)}
    </div>
    <div class="tip-affinity">${affinities}</div>
    <div class="tip-label">Opening moves</div>
    <div class="tip-moves">${(f.moves ?? []).map((m) => `<span class="tip-move">${escapeHtml(m)}</span>`).join('')}</div>
    <div class="tip-label">Appears in</div>
    <div class="tip-games">${(s.games ?? []).map(escapeHtml).join(' · ')}</div>`;
}

function showStarterTip(card) {
  const starter = state.starters[Number(card.dataset.index)];
  if (!starter) return;

  const tip = $('starter-tip');
  tip.innerHTML = starterTipHtml(starter);
  tip.hidden = false;

  // Anchored to the grid's wrapper, then nudged back inside it — a card in the last column
  // would otherwise push the panel off the right edge of the page.
  const area = tip.parentElement.getBoundingClientRect();
  const box = card.getBoundingClientRect();
  const width = tip.offsetWidth;

  const left = Math.min(
    Math.max(0, box.left - area.left + box.width / 2 - width / 2),
    Math.max(0, area.width - width));

  tip.style.left = `${left}px`;
  tip.style.top = `${box.bottom - area.top + 8}px`;
}

function hideStarterTip() { $('starter-tip').hidden = true; }

function bindStarterTip() {
  const grid = $('starter-grid');
  const open = (event) => {
    const card = event.target.closest('.starter');
    if (card) showStarterTip(card);
  };

  grid.addEventListener('mouseover', open);
  grid.addEventListener('focusin', open);
  grid.addEventListener('mouseleave', hideStarterTip);
  grid.addEventListener('focusout', hideStarterTip);
  window.addEventListener('scroll', hideStarterTip, { passive: true });
}

async function startRun(family) {
  hideStarterTip();
  $('select').hidden = true;
  $('boot').hidden = false;
  $('boot').textContent = `Summoning ${family}…`;

  try {
    const res = await fetch(`${API}/run?family=${encodeURIComponent(family)}`, {
      headers: { Accept: 'application/json' },
    });
    if (!res.ok) throw new Error(`Could not start a climb for ${family} (${res.status})`);

    state.run = await res.json();
    // Merged rather than replaced, so a payload from a server that predates statuses still
    // leaves the status constants defined instead of blanking them to undefined.
    rules = { ...rules, ...(state.run.rules ?? {}) };
    state.rungIndex = 0;
    state.battleIndex = 0;
    state.results = state.run.rungs.map(() => []);
    state.retries = state.run.retriesPerRun;
    state.retriesUsed = 0;

    $('boot').hidden = true;
    $('battle').hidden = false;
    beginBattle();
  } catch (err) {
    fail(err.message);
  }
}

// ── Battle flow ────────────────────────────────────────────────────────────

const rung = () => state.run.rungs[state.rungIndex];

/** Statuses are cleared between battles: a condition belongs to the fight it was inflicted in,
 *  not to the run, and a poison carried up the ladder would quietly decide later rungs. */
const side = (fighter) => ({ fighter, hp: fighter.hitPoints, statuses: {} });

const has = (combatant, status) => (combatant.statuses[status] ?? 0) > 0;

function beginBattle() {
  const current = rung();
  const foe = current.opponents[state.battleIndex];

  state.player = side(current.player);
  state.opponent = side(foe);
  state.busy = false;

  $('retry-row').hidden = true;
  $('log').innerHTML = '';
  say(`${current.player.name} faces ${foe.name}${foe.category === 'Boss' ? ' — a boss' : ''}.`);
  render();
  setStatus(`Battle ${state.battleIndex + 1} of ${current.opponents.length}. Choose a move.`);
}

/** Ties go to the player. Speed is the only thing that decides order, so it is fixed for the
 *  whole battle — which is exactly why it has to be announced rather than discovered. */
const playerIsFaster = () => state.player.fighter.speed >= state.opponent.fighter.speed;

function render() {
  const current = rung();

  $('rung-game').textContent = current.gameName;
  $('rung-count').textContent = `Rung ${current.number} of ${state.run.rungs.length}`;

  const pips = $('rung-progress');
  pips.innerHTML = '';
  for (let i = 0; i < current.opponents.length; i++) {
    const result = state.results[state.rungIndex][i];
    const pip = document.createElement('span');
    pip.className = `pip${result === true ? ' won' : result === false ? ' lost' : ''}`;
    pip.textContent = result === true ? '✓' : result === false ? '✗' : i + 1;
    pip.title = current.opponents[i].category === 'Boss' ? `Battle ${i + 1} — boss` : `Battle ${i + 1}`;
    pips.appendChild(pip);
  }

  renderRetries();
  renderInitiative();

  $('player-card').innerHTML = fighterCard(state.player, 'Your monster', true);
  $('opponent-card').innerHTML =
    fighterCard(state.opponent, state.opponent.fighter.category === 'Boss' ? 'Boss' : 'Enemy', false);
}

/**
 * Who acts first, said before the player commits rather than after.
 *
 * This is the whole fix for a turn that felt broken. Order has always come from Speed, so
 * against a faster enemy the player's click was answered by damage landing on them first: the
 * arithmetic was right and the framing was wrong — a click read as "act now" when it meant
 * "commit a move to this round". Announcing it up front makes a pre-emptive strike a fact about
 * the matchup instead of a glitch.
 */
function renderInitiative() {
  const bar = $('initiative');
  const mine = playerIsFaster();
  const fast = mine ? state.player.fighter : state.opponent.fighter;
  const slow = mine ? state.opponent.fighter : state.player.fighter;

  bar.className = `initiative ${mine ? 'mine' : 'theirs'}`;
  bar.innerHTML = `
    <span class="initiative-label">Turn order</span>
    <span class="initiative-seq">
      <b>${escapeHtml(fast.name)}</b>
      <span class="initiative-arrow" aria-hidden="true">→</span>
      ${escapeHtml(slow.name)}
    </span>
    <span class="initiative-why">Speed ${fast.speed} vs ${slow.speed} — ${
      mine ? 'your move lands first.' : 'it strikes before your move lands.'
    }</span>`;
}

/** One chocobo per retry. The strip carries its own label and aria-label, so each sprite is
 *  decorative and stays out of the accessibility tree. */
const chocobo = (spent) =>
  `<img class="chocobo${spent ? ' spent' : ''}" src="/assets/chocobo.png" alt="" aria-hidden="true" />`;

function renderRetries() {
  const box = $('retries');
  const total = state.run.retriesPerRun;

  box.innerHTML = state.retries === 0
    ? `<span class="retries-none">none left</span>`
    : Array.from({ length: total }, (_, i) => chocobo(i >= state.retries)).join('');

  box.setAttribute('aria-label', `${state.retries} of ${total} retries left`);
}

function fighterCard(combatant, role, isPlayer) {
  const f = combatant.fighter;
  const pct = Math.max(0, (combatant.hp / f.hitPoints) * 100);
  const level = pct <= 20 ? ' critical' : pct <= 50 ? ' low' : '';
  const first = isPlayer === playerIsFaster();

  const tags = [
    ...f.weaknesses.map((w) => `<span class="tag weak">weak: ${escapeHtml(w)}</span>`),
    ...f.absorbs.map((a) => `<span class="tag absorb">absorbs: ${escapeHtml(a)}</span>`),
  ].join('');

  return `
    <img class="art-frame" alt="${escapeAttr(f.name)}" src="${escapeAttr(f.imageUrl ?? NO_ART)}" />
    <div class="fighter-head">
      <div>
        <div class="fighter-name">${escapeHtml(f.name)}</div>
        <div class="fighter-role">${escapeHtml(role)}</div>
      </div>
      ${first ? '<span class="first-badge" title="Acts first each round">1st</span>' : ''}
    </div>
    <div class="hp-track"><div class="hp-fill${level}" style="width:${pct}%"></div></div>
    <div class="hp-text">${Math.max(0, Math.round(combatant.hp))} / ${f.hitPoints} HP</div>
    <div class="stat-row">
      <span>ATK ${f.attack}</span><span>DEF ${f.defense}</span>
      <span>MAG ${f.magicAttack}</span><span>SPD ${f.speed}</span>
    </div>
    <div class="affinity">${tags}</div>
    ${statusStrip(combatant)}
    ${movePanel(combatant, isPlayer)}`;
}

/** Always rendered, even when empty. A strip that appears and disappears shifts the moves
 *  underneath it mid-battle, and the button the player was aiming at moves out from under them. */
function statusStrip(combatant) {
  const active = Object.keys(STATUS)
    .filter((key) => has(combatant, key))
    .map((key) => `
      <span class="status ${key.toLowerCase()}" title="${escapeAttr(STATUS[key].hint())}">
        ${STATUS[key].label} · ${combatant.statuses[key]}
      </span>`)
    .join('');

  return `<div class="statuses">${active || '<span class="status-none">no conditions</span>'}</div>`;
}

/**
 * Both sides show their moves, and the enemy's read exactly like the player's — element, kind,
 * and whether it is super effective against whoever it is pointed at. Hiding the enemy's kit
 * made every fight an exchange of surprises; showing it turns "which button" into a read of the
 * matchup, which is the only decision a deterministic battle has.
 */
function movePanel(combatant, isPlayer) {
  const foe = isPlayer ? state.opponent : state.player;
  const silenced = has(combatant, 'Silence');
  const blinded = has(combatant, 'Blind');

  const rows = combatant.fighter.moves.map((move, index) => {
    const locked = silenced && move.kind === 'Magic';
    const notes = [
      move.element,
      move.kind,
      effectAgainst(move, foe.fighter),
      move.status && move.status !== 'None' ? `inflicts ${STATUS[move.status].label.toLowerCase()}` : '',
      blinded && move.kind === 'Physical' ? 'weakened by blind' : '',
    ].filter(Boolean).join(' · ');

    const body = `
      <span class="move-name">${escapeHtml(move.name)}</span>
      <span class="move-meta">${escapeHtml(notes)}</span>
      ${move.recoil > 0 ? `<span class="move-warn">costs ${Math.round(move.recoil * 100)}% of max HP</span>` : ''}
      ${locked ? '<span class="move-warn">silenced — unavailable</span>' : ''}`;

    return isPlayer
      ? `<button class="btn move" type="button" data-index="${index}" ${state.busy || locked ? 'disabled' : ''}>${body}</button>`
      : `<div class="move foe-move${locked ? ' locked' : ''}">${body}</div>`;
  }).join('');

  return `
    <div class="move-label">${isPlayer ? 'Your moves' : 'Its moves'}</div>
    <div class="moves">${rows}</div>`;
}

/** Shown on the move so the elemental read is available before committing, not after. */
function effectAgainst(move, defender) {
  if (!move.element) return '';
  if (defender.absorbs.includes(move.element)) return 'absorbed';
  if (defender.weaknesses.includes(move.element)) return 'super effective';
  return '';
}

async function takeTurn(playerMove) {
  if (state.busy) return;
  state.busy = true;
  render();

  const order = playerIsFaster()
    ? [() => act(state.player, state.opponent, playerMove, true),
       () => act(state.opponent, state.player, chooseFoeMove(), false)]
    : [() => act(state.opponent, state.player, chooseFoeMove(), false),
       () => act(state.player, state.opponent, playerMove, true)];

  for (const step of order) {
    if (state.player.hp <= 0 || state.opponent.hp <= 0) break;
    step();
    render();
    await pause(500);
  }

  if (state.player.hp <= 0 || state.opponent.hp <= 0) {
    finishBattle(state.opponent.hp <= 0 && state.player.hp > 0);
    return;
  }

  state.busy = false;
  render();
  setStatus(playerIsFaster()
    ? 'Choose a move.'
    : `Choose a move — ${state.opponent.fighter.name} acts before it lands.`);
}

function act(attacker, defender, move, isPlayer) {
  const who = isPlayer ? 'Your' : 'Enemy';
  const blinded = has(attacker, 'Blind') && move.kind === 'Physical';

  const offence = move.kind === 'Magic' ? attacker.fighter.magicAttack : attacker.fighter.attack;
  const guard = move.kind === 'Magic' ? defender.fighter.magicDefense : defender.fighter.defense;

  let raw = defender.fighter.hitPoints * rules.damageShare * move.power * ratio(offence, guard);
  if (blinded) raw *= rules.blindMultiplier;

  if (move.element && defender.fighter.absorbs.includes(move.element)) {
    const healed = Math.max(1, Math.round(raw / 2));
    defender.hp = Math.min(defender.fighter.hitPoints, defender.hp + healed);
    say(`${who} ${move.name} is absorbed — ${defender.fighter.name} heals ${healed} HP.`, 'bad');
  } else {
    const weak = move.element && defender.fighter.weaknesses.includes(move.element);
    const damage = Math.max(1, Math.round(raw * (weak ? rules.weaknessMultiplier : 1)));
    defender.hp -= damage;
    say(
      `${who} ${move.name} hits ${defender.fighter.name} for ${damage}` +
        `${weak ? ' — super effective!' : ''}${blinded ? ' (blinded — weakened)' : ''}`,
      weak ? 'crit' : isPlayer ? 'hit' : 'bad',
    );

    // A condition rides on the hit, so an absorbed move never lands one. Otherwise the single
    // worst move available would also be the one that poisons, and the elemental read the whole
    // battle is built on would stop meaning anything.
    if (move.status && move.status !== 'None' && defender.hp > 0)
      inflict(defender, move.status, isPlayer);
  }

  if (move.recoil > 0) {
    const cost = Math.max(1, Math.round(attacker.fighter.hitPoints * move.recoil));
    attacker.hp -= cost;
    say(`${attacker.fighter.name} takes ${cost} recoil from ${move.name}.`, 'crit');
  }

  tickStatuses(attacker, isPlayer);
}

function inflict(combatant, status, byPlayer) {
  const renewed = has(combatant, status);
  combatant.statuses[status] = rules.statusTurns;

  say(
    `${combatant.fighter.name} is ${renewed ? 'still ' : ''}${STATUS[status].verb}.`,
    byPlayer ? 'hit' : 'bad',
  );
}

/**
 * Conditions burn down on their holder's own turn, so one inflicted by the faster side still
 * gets its full run of turns rather than losing one to the round it landed in.
 */
function tickStatuses(combatant, isPlayer) {
  if (has(combatant, 'Poison')) {
    const bleed = Math.max(1, Math.round(combatant.fighter.hitPoints * rules.poisonShare));
    combatant.hp -= bleed;
    say(`${combatant.fighter.name} takes ${bleed} from poison.`, isPlayer ? 'bad' : 'hit');
  }

  for (const key of Object.keys(STATUS)) {
    if (has(combatant, key) && --combatant.statuses[key] === 0)
      say(`${combatant.fighter.name} shakes off ${STATUS[key].label.toLowerCase()}.`);
  }
}

/** Deterministic and simple: the foe takes whichever move does the most damage right now,
 *  and won't self-destruct unless that finishes the fight. */
function chooseFoeMove() {
  const silenced = has(state.opponent, 'Silence');
  const blinded = has(state.opponent, 'Blind');

  // Silence can empty the list of magic, never the list itself — the basic Attack every
  // combatant is given is Physical, so there is always something left to press.
  const available = state.opponent.fighter.moves.filter((m) => !(silenced && m.kind === 'Magic'));
  const moves = available.length ? available : [state.opponent.fighter.moves[0]];

  let best = moves[0];
  let bestScore = -1;

  for (const move of moves) {
    const offence = move.kind === 'Magic' ? state.opponent.fighter.magicAttack : state.opponent.fighter.attack;
    const guard = move.kind === 'Magic' ? state.player.fighter.magicDefense : state.player.fighter.defense;

    let damage = state.player.fighter.hitPoints * rules.damageShare * move.power * ratio(offence, guard);
    if (blinded && move.kind === 'Physical') damage *= rules.blindMultiplier;

    if (move.element && state.player.fighter.absorbs.includes(move.element)) damage = -1;
    else if (move.element && state.player.fighter.weaknesses.includes(move.element)) damage *= rules.weaknessMultiplier;

    if (move.recoil > 0 && damage < state.player.hp) continue;

    // Status moves give up power to inflict, so raw damage alone would mean the enemy never
    // presses one and half the feature is invisible from the receiving end. The bonus is
    // withheld when the hit would win outright, so this never talks the foe out of a kill.
    let score = damage;
    if (move.status && move.status !== 'None' && !has(state.player, move.status) && damage < state.player.hp)
      score *= 1.3;

    if (score > bestScore) { bestScore = score; best = move; }
  }

  return best;
}

function finishBattle(won) {
  state.results[state.rungIndex][state.battleIndex] = won;
  render();

  const current = rung();
  say(won ? `${state.opponent.fighter.name} is defeated.` : `${state.player.fighter.name} falls.`, won ? 'hit' : 'bad');

  if (!won) return offerRetry();

  // Every battle on the rung has to be won, boss included.
  if (state.battleIndex + 1 >= current.opponents.length) return advance();

  state.battleIndex++;
  setStatus('Next battle…');
  setTimeout(beginBattle, 900);
}

/** A loss ends the run unless a retry is spent. Retries span the whole ladder, so this is a
 *  decision about the run rather than the current game. */
function offerRetry() {
  if (state.retries <= 0) {
    // Hiding matters: leaving the button on screen let a run spend a fourth retry.
    $('retry-row').hidden = true;
    setStatus('No retries left.');
    return setTimeout(() => endRun(false), 900);
  }

  state.busy = true;
  render();

  setStatus(`${state.player.fighter.name} was defeated. Spend a retry to fight ${state.opponent.fighter.name} again?`);
  $('retry-btn').textContent = state.retries === 1 ? 'Use last retry' : `Use a retry (${state.retries} left)`;
  $('retry-row').hidden = false;
}

function useRetry() {
  if (state.retries <= 0) return;

  state.retries--;
  state.retriesUsed++;
  state.results[state.rungIndex][state.battleIndex] = undefined;

  $('retry-row').hidden = true;
  say(`Retry spent — ${state.retries} left.`, 'crit');
  beginBattle();
}

function advance() {
  const next = state.rungIndex + 1;

  if (next >= state.run.rungs.length) return endRun(true);

  const evolved = state.run.rungs[next];
  setStatus(`Cleared ${rung().gameName}. ${state.run.family} evolves into its ${evolved.gameName} form.`);

  state.rungIndex = next;
  state.battleIndex = 0;
  setTimeout(beginBattle, 1400);
}

function endRun(cleared) {
  $('battle').hidden = true;
  $('over').hidden = false;

  const reached = state.run.rungs[state.rungIndex];
  const retriesNote = state.retriesUsed === 0
    ? ' — no retries used'
    : ` — ${state.retriesUsed} ${state.retriesUsed === 1 ? 'retry' : 'retries'} used`;

  $('over-title').textContent = cleared ? 'Climb complete!' : `Defeated in ${reached.gameName}`;
  $('over-sub').textContent = cleared
    ? `${state.run.family} fought through all ${state.run.rungs.length} games${retriesNote}.`
    : `${state.run.family} made it to rung ${reached.number} of ${state.run.rungs.length}${retriesNote}.`;

  $('share').textContent = buildShare(cleared);
}

function buildShare(cleared) {
  const lines = [`moogleAPI Kupo Climb — ${state.run.family} — ${state.run.date}`];

  state.run.rungs.forEach((r, i) => {
    const results = state.results[i];
    if (!results.some((x) => x !== undefined)) return;
    // A retried battle shows as the loss it was plus the rematch, so the grid stays honest.
    lines.push(`${r.gameName}  ${results.map((w) => (w === true ? '🟩' : w === false ? '🟥' : '⬛')).join('')}`);
  });

  const reached = state.run.rungs[state.rungIndex];
  lines.push(cleared
    ? `Climbed all ${state.run.rungs.length} games!`
    : `Reached ${reached.gameName} (${reached.number}/${state.run.rungs.length})`);
  lines.push(`Retries used: ${state.retriesUsed}/${state.run.retriesPerRun}`);

  return lines.join('\n');
}

// ── Helpers ────────────────────────────────────────────────────────────────

function say(text, tone) {
  const log = $('log');
  const line = document.createElement('p');
  if (tone) line.className = tone;
  line.textContent = text;
  log.appendChild(line);
  log.scrollTop = log.scrollHeight;
}

function setStatus(text) { $('status').textContent = text; }
const pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}
const escapeAttr = escapeHtml;

// Delegated, because the move buttons now live inside a card that is rewritten every render —
// rebinding each one per frame would leak a listener per turn.
$('arena').addEventListener('click', (event) => {
  const btn = event.target.closest('button.move');
  if (!btn || btn.disabled) return;
  takeTurn(state.player.fighter.moves[Number(btn.dataset.index)]);
});

$('retry-btn').addEventListener('click', useRetry);

$('concede-btn').addEventListener('click', () => {
  $('retry-row').hidden = true;
  endRun(false);
});

$('again-btn').addEventListener('click', () => {
  $('over').hidden = true;
  $('select').hidden = false;
});

$('copy-btn').addEventListener('click', async () => {
  try {
    await navigator.clipboard.writeText($('share').textContent);
    $('copy-btn').textContent = 'Copied';
    setTimeout(() => ($('copy-btn').textContent = 'Copy result'), 1600);
  } catch {
    $('copy-btn').textContent = 'Copy failed';
  }
});

bindStarterTip();
boot();
