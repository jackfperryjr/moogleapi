'use strict';

/**
 * Gauntlet — a ladder of Final Fantasy monster battles, one game per rung.
 *
 * The server hands over the whole run in a single call: every rung, both sides, and their
 * moves. Combat then resolves entirely here, so a twelve-rung run costs one request and
 * survives a flaky connection.
 */

const API = '/api/battle';

/** The damage model arrives with the run rather than living here. The server vets every
 *  matchup as winnable using this same arithmetic, so a second copy of the constants in the
 *  browser would drift and quietly break that guarantee. These values are only a fallback for
 *  a payload from an older server. */
let rules = { damageShare: 0.30, weaknessMultiplier: 2, minRatio: 0.2, maxRatio: 0.8 };

const ratio = (offence, guard) =>
  Math.min(rules.maxRatio, Math.max(rules.minRatio, offence / (offence + guard || 1)));

/** No randomness anywhere in combat: the same choices always produce the same battle, so a
 *  shared result is honest and two players comparing runs see identical fights. */
const state = {
  run: null,
  rungIndex: 0,
  battleIndex: 0,
  results: [],      // per rung: array of true/false, indexed by battle
  retries: 0,       // second chances left for the whole run, not this rung
  retriesUsed: 0,
  player: null,     // { fighter, hp }
  opponent: null,
  busy: false,
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

  for (const s of starters) {
    const card = document.createElement('button');
    card.className = 'starter';
    card.type = 'button';
    card.innerHTML = `
      <img class="art-frame" alt="" loading="lazy" src="${escapeAttr(s.imageUrl ?? '')}" />
      <span class="starter-name">${escapeHtml(s.family)}</span>
      <span class="starter-games">${s.gameCount} games</span>`;
    card.addEventListener('click', () => startRun(s.family));
    grid.appendChild(card);
  }
}

async function startRun(family) {
  $('select').hidden = true;
  $('boot').hidden = false;
  $('boot').textContent = `Summoning ${family}…`;

  try {
    const res = await fetch(`${API}/run?family=${encodeURIComponent(family)}`, {
      headers: { Accept: 'application/json' },
    });
    if (!res.ok) throw new Error(`Could not start a run for ${family} (${res.status})`);

    state.run = await res.json();
    rules = state.run.rules ?? rules;
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

function beginBattle() {
  const current = rung();
  const foe = current.opponents[state.battleIndex];

  state.player = { fighter: current.player, hp: current.player.hitPoints };
  state.opponent = { fighter: foe, hp: foe.hitPoints };
  state.busy = false;

  $('retry-row').hidden = true;
  $('log').innerHTML = '';
  say(`${current.player.name} faces ${foe.name}${foe.category === 'Boss' ? ' — a boss' : ''}.`);
  setStatus(`Battle ${state.battleIndex + 1} of ${current.opponents.length}. Choose a move.`);
  render();
}

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

  $('player-card').innerHTML = fighterCard(state.player, 'Your monster');
  $('opponent-card').innerHTML = fighterCard(state.opponent, state.opponent.fighter.category === 'Boss' ? 'Boss' : 'Enemy');
  renderMoves();
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

function fighterCard(side, role) {
  const f = side.fighter;
  const pct = Math.max(0, (side.hp / f.hitPoints) * 100);
  const level = pct <= 20 ? ' critical' : pct <= 50 ? ' low' : '';

  const tags = [
    ...f.weaknesses.map((w) => `<span class="tag weak">weak: ${escapeHtml(w)}</span>`),
    ...f.absorbs.map((a) => `<span class="tag absorb">absorbs: ${escapeHtml(a)}</span>`),
  ].join('');

  return `
    <img class="art-frame" alt="${escapeAttr(f.name)}" src="${escapeAttr(f.imageUrl ?? '')}" />
    <div class="fighter-name">${escapeHtml(f.name)}</div>
    <div class="fighter-role">${escapeHtml(role)}</div>
    <div class="hp-track"><div class="hp-fill${level}" style="width:${pct}%"></div></div>
    <div class="hp-text">${Math.max(0, Math.round(side.hp))} / ${f.hitPoints} HP</div>
    <div class="affinity">${tags}</div>`;
}

function renderMoves() {
  const box = $('moves');
  box.innerHTML = '';

  for (const move of state.player.fighter.moves) {
    const btn = document.createElement('button');
    btn.className = 'btn move';
    btn.type = 'button';
    btn.disabled = state.busy;

    const element = move.element ? `${move.element} · ` : '';
    const effect = effectAgainst(move, state.opponent.fighter);
    btn.innerHTML = `
      <span class="move-name">${escapeHtml(move.name)}</span>
      <span class="move-meta">${element}${move.kind}${effect ? ' · ' + effect : ''}</span>
      ${move.recoil > 0 ? `<span class="move-warn">costs ${Math.round(move.recoil * 100)}% of your max HP</span>` : ''}`;

    btn.addEventListener('click', () => takeTurn(move));
    box.appendChild(btn);
  }
}

/** Shown on the button so the elemental read is available before committing, not after. */
function effectAgainst(move, defender) {
  if (!move.element) return '';
  if (defender.absorbs.includes(move.element)) return 'absorbed';
  if (defender.weaknesses.includes(move.element)) return 'super effective';
  return '';
}

async function takeTurn(playerMove) {
  if (state.busy) return;
  state.busy = true;
  renderMoves();

  const playerFirst = state.player.fighter.speed >= state.opponent.fighter.speed;
  const order = playerFirst
    ? [() => attack(state.player, state.opponent, playerMove, true),
       () => attack(state.opponent, state.player, chooseFoeMove(), false)]
    : [() => attack(state.opponent, state.player, chooseFoeMove(), false),
       () => attack(state.player, state.opponent, playerMove, true)];

  for (const act of order) {
    if (state.player.hp <= 0 || state.opponent.hp <= 0) break;
    act();
    render();
    await pause(450);
  }

  if (state.player.hp <= 0 || state.opponent.hp <= 0) {
    finishBattle(state.opponent.hp <= 0 && state.player.hp > 0);
    return;
  }

  state.busy = false;
  setStatus('Choose a move.');
  renderMoves();
}

function attack(attacker, defender, move, isPlayer) {
  const offence = move.kind === 'Magic' ? attacker.fighter.magicAttack : attacker.fighter.attack;
  const guard = move.kind === 'Magic' ? defender.fighter.magicDefense : defender.fighter.defense;

  const who = isPlayer ? 'Your' : 'Enemy';
  const raw = defender.fighter.hitPoints * rules.damageShare * move.power * ratio(offence, guard);

  if (move.element && defender.fighter.absorbs.includes(move.element)) {
    const healed = Math.max(1, Math.round(raw / 2));
    defender.hp = Math.min(defender.fighter.hitPoints, defender.hp + healed);
    say(`${who} ${move.name} is absorbed — ${defender.fighter.name} heals ${healed} HP.`, 'bad');
  } else {
    const weak = move.element && defender.fighter.weaknesses.includes(move.element);
    const damage = Math.max(1, Math.round(raw * (weak ? rules.weaknessMultiplier : 1)));
    defender.hp -= damage;
    say(
      `${who} ${move.name} hits ${defender.fighter.name} for ${damage}${weak ? ' — super effective!' : ''}`,
      weak ? 'crit' : isPlayer ? 'hit' : 'bad',
    );
  }

  if (move.recoil > 0) {
    const cost = Math.max(1, Math.round(attacker.fighter.hitPoints * move.recoil));
    attacker.hp -= cost;
    say(`${attacker.fighter.name} takes ${cost} recoil from ${move.name}.`, 'crit');
  }
}

/** Deterministic and simple: the foe takes whichever move does the most damage right now,
 *  and won't self-destruct unless that finishes the fight. */
function chooseFoeMove() {
  const moves = state.opponent.fighter.moves;
  let best = moves[0];
  let bestDamage = -1;

  for (const move of moves) {
    const offence = move.kind === 'Magic' ? state.opponent.fighter.magicAttack : state.opponent.fighter.attack;
    const guard = move.kind === 'Magic' ? state.player.fighter.magicDefense : state.player.fighter.defense;
    const raw = state.player.fighter.hitPoints * rules.damageShare * move.power * ratio(offence, guard);

    let damage = raw;
    if (move.element && state.player.fighter.absorbs.includes(move.element)) damage = -1;
    else if (move.element && state.player.fighter.weaknesses.includes(move.element)) damage *= rules.weaknessMultiplier;

    if (move.recoil > 0 && damage < state.player.hp) continue;

    if (damage > bestDamage) { bestDamage = damage; best = move; }
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
  renderMoves();

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

  $('over-title').textContent = cleared ? 'Gauntlet cleared!' : `Defeated in ${reached.gameName}`;
  $('over-sub').textContent = cleared
    ? `${state.run.family} fought through all ${state.run.rungs.length} games${retriesNote}.`
    : `${state.run.family} made it to rung ${reached.number} of ${state.run.rungs.length}${retriesNote}.`;

  $('share').textContent = buildShare(cleared);
}

function buildShare(cleared) {
  const lines = [`moogleAPI Gauntlet — ${state.run.family} — ${state.run.date}`];

  state.run.rungs.forEach((r, i) => {
    const results = state.results[i];
    if (!results.some((x) => x !== undefined)) return;
    // A retried battle shows as the loss it was plus the rematch, so the grid stays honest.
    lines.push(`${r.gameName}  ${results.map((w) => (w === true ? '🟩' : w === false ? '🟥' : '⬛')).join('')}`);
  });

  const reached = state.run.rungs[state.rungIndex];
  lines.push(cleared
    ? `Cleared all ${state.run.rungs.length} games!`
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

boot();
