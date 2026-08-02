'use strict';

/**
 * Battle Square — one character against eight consecutive waves of their own game's monsters.
 *
 * The server hands over the whole run in a single call: the champion at the chosen level, all
 * eight opponents, both sides' moves, and the damage model. Combat then resolves entirely here,
 * so a full run costs one request and survives a flaky connection.
 */

const API = '/api/arena';

/** The damage model arrives with the run rather than living here. The server picks every wave
 *  using this same arithmetic, so a second copy of the constants in the browser would drift and
 *  the difficulty ramp would stop describing the fights the player actually gets. These values
 *  are only a fallback for a payload from an older server. */
let rules = {
  damageShare: 0.30, weaknessMultiplier: 2, minRatio: 0.2, maxRatio: 4, ratioScale: 0.5,
  poisonShare: 0.06, blindMultiplier: 0.5, statusTurns: 3, handicapStatPenalty: 0.66,
};

/** Health restored between waves, as a share of maximum. Mirrors ArenaBuilder.WaveRecovery —
 *  the server solves the recommended level against exactly this number. */
const WAVE_RECOVERY = 0.20;

/** The square root of the advantage, not offence/(offence+guard) — that expression cannot exceed
 *  1 however large the advantage grows, so past about four-to-one extra attack bought nothing and
 *  no fight could ever end in fewer than four turns. */
const ratio = (offence, guard) =>
  Math.min(rules.maxRatio,
           Math.max(rules.minRatio, rules.ratioScale * Math.sqrt(offence / Math.max(1, guard))));

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

/** No randomness anywhere in combat: the same choices always produce the same run, so a shared
 *  result is honest and two players comparing scores fought identical waves. */
const state = {
  roster: [],
  games: [],
  gameId: null,
  selected: null,   // roster entry
  level: 50,
  run: null,
  waveIndex: 0,
  results: [],      // true/false per wave
  points: 0,
  championHp: 0,    // carried between waves — the whole point of the format
  player: null,     // { fighter, hp, statuses }
  opponent: null,
  busy: false,
};

const $ = (id) => document.getElementById(id);

// ── Boot ───────────────────────────────────────────────────────────────────

async function boot() {
  try {
    const res = await fetch(`${API}/roster`, { headers: { Accept: 'application/json' } });
    if (!res.ok) throw new Error(`Roster unavailable (${res.status})`);

    const data = await res.json();
    state.roster = data.characters ?? [];
    state.games = data.games ?? [];

    if (state.roster.length === 0) {
      // The roster is populated by a scraper stage; before it has run there is nothing to pick.
      throw new Error('No characters are available yet. The roster has not been built.');
    }

    $('boot').hidden = true;
    $('setup').hidden = false;

    buildGameSelect();
    renderRoster();
  } catch (e) {
    fail(e.message);
  }
}

function fail(message) {
  $('boot').hidden = true;
  const box = $('error');
  box.textContent = message;
  box.hidden = false;
}

function buildGameSelect() {
  const select = $('game-select');
  select.innerHTML = '';

  for (const game of state.games) {
    const option = document.createElement('option');
    option.value = game.gameId;
    option.textContent = `${game.gameName} — ${game.characterCount} character${game.characterCount === 1 ? '' : 's'}`;
    select.appendChild(option);
  }

  state.gameId = state.games[0]?.gameId ?? null;
  select.value = state.gameId;
  select.addEventListener('change', () => {
    state.gameId = Number(select.value);
    state.selected = null;
    $('level-panel').hidden = true;
    renderRoster();
  });
}

function renderRoster() {
  const grid = $('roster');
  grid.innerHTML = '';

  for (const c of state.roster.filter((c) => c.gameId === state.gameId)) {
    const card = document.createElement('button');
    card.type = 'button';
    card.className = 'champ';
    card.setAttribute('aria-pressed', String(state.selected?.characterId === c.characterId));
    card.innerHTML =
      `<img src="${escapeAttr(c.imageUrl ?? '')}" alt="" loading="lazy" />` +
      `<div class="champ-name">${escapeHtml(c.name)}</div>` +
      `<div class="champ-role">${escapeHtml(c.job || c.archetype)}</div>`;

    card.addEventListener('click', () => selectChampion(c));
    grid.appendChild(card);
  }
}

function selectChampion(entry) {
  state.selected = entry;
  state.level = entry.recommendedLevel;

  renderRoster();

  $('level-panel').hidden = false;
  const slider = $('level-slider');
  slider.value = String(state.level);
  updateLevelUi();

  slider.oninput = () => { state.level = Number(slider.value); updateLevelUi(); };
  $('enter-btn').onclick = startRun;
}

function updateLevelUi() {
  const entry = state.selected;
  $('level-value').textContent = String(state.level);

  const recommended = entry.recommendedLevel;
  const delta = state.level - recommended;
  const verdict =
    delta === 0 ? 'the level this run is balanced for'
    : delta > 0 ? `${delta} above the balanced level — easier, and the same points`
    : `${-delta} below the balanced level — you may not survive eight waves`;

  $('level-hint').innerHTML =
    `<b>${escapeHtml(entry.name)}</b> · ${escapeHtml(entry.archetype)}<br />` +
    `Recommended <b>${recommended}</b> — ${escapeHtml(verdict)}`;

  // The preview is fetched rather than computed: the stat curve is the server's, and
  // reimplementing it here to fill in a panel is exactly how the two would drift apart.
  previewStats();
}

let previewToken = 0;
let previewTimer = null;

/**
 * Debounced because the slider fires on every pixel of a drag, and this calls the API. The
 * anonymous rate limit is 60 requests a minute — one sweep of the level slider would spend it
 * all and lock the player out of starting the run they were configuring.
 */
function previewStats() {
  clearTimeout(previewTimer);
  previewTimer = setTimeout(fetchPreview, 350);
}

async function fetchPreview() {
  const token = ++previewToken;
  const box = $('preview-stats');
  box.innerHTML = '<div class="preview-stat"><span>loading</span></div>';

  try {
    const res = await fetch(
      `${API}/run?characterId=${state.selected.characterId}&level=${state.level}`,
      { headers: { Accept: 'application/json' } },
    );
    if (!res.ok) throw new Error();

    const run = await res.json();
    // A slower earlier request must never overwrite a newer one — the slider fires fast.
    if (token !== previewToken) return;

    const f = run.champion.fighter;
    box.innerHTML = [
      ['HP', f.hitPoints], ['ATK', f.attack], ['DEF', f.defense],
      ['MAG', f.magicAttack], ['MDEF', f.magicDefense], ['SPD', f.speed],
    ].map(([k, v]) => `<div class="preview-stat"><span>${k}</span>${v.toLocaleString()}</div>`).join('');
  } catch {
    if (token === previewToken) box.innerHTML = '';
  }
}

// ── Run ────────────────────────────────────────────────────────────────────

async function startRun() {
  $('enter-btn').disabled = true;

  try {
    const res = await fetch(
      `${API}/run?characterId=${state.selected.characterId}&level=${state.level}`,
      { headers: { Accept: 'application/json' } },
    );
    if (!res.ok) throw new Error(`Could not start the run (${res.status})`);

    state.run = await res.json();
    if (state.run.rules) rules = state.run.rules;

    state.waveIndex = 0;
    state.results = [];
    state.points = 0;
    state.championHp = state.run.champion.fighter.hitPoints;

    $('setup').hidden = true;
    $('battle').hidden = false;
    $('over').hidden = true;

    beginWave();
  } catch (e) {
    fail(e.message);
  } finally {
    $('enter-btn').disabled = false;
  }
}

const wave = () => state.run.waves[state.waveIndex];

/**
 * Applies the wave's handicap to a copy of the champion, so the penalty lasts exactly one wave.
 * Mutating the champion in place would compound the reel across the run — two "Armor broken"
 * spins would leave the player at a third of their defence for the rest of it.
 */
function championForWave(handicap) {
  const base = state.run.champion.fighter;
  const cut = rules.handicapStatPenalty;

  const fighter = { ...base };
  if (handicap.kind === 'WeakenOffence') {
    fighter.attack = Math.max(1, Math.round(base.attack * cut));
    fighter.magicAttack = Math.max(1, Math.round(base.magicAttack * cut));
  } else if (handicap.kind === 'StripDefence') {
    fighter.defense = Math.max(1, Math.round(base.defense * cut));
    fighter.magicDefense = Math.max(1, Math.round(base.magicDefense * cut));
  } else if (handicap.kind === 'SealAbilities') {
    // The basic Attack is always first in the list and always physical, so sealing everything
    // else can never leave a combatant with nothing to press.
    fighter.moves = base.moves.slice(0, 1);
  }

  return fighter;
}

function beginWave() {
  const current = wave();
  const handicap = current.handicap;

  if (handicap.kind === 'HalveHitPoints') {
    // Halves what is left, not the maximum — and never to zero, so the reel maims rather
    // than kills.
    state.championHp = Math.max(1, Math.floor(state.championHp / 2));
  }

  const fighter = championForWave(handicap);

  state.player = { fighter, hp: Math.min(state.championHp, fighter.hitPoints), statuses: {} };
  state.opponent = { fighter: current.opponent, hp: current.opponent.hitPoints, statuses: {} };

  // A status handicap is applied as a condition that lasts the whole wave rather than the usual
  // few turns, because it was drawn for this wave — not landed by a move inside it.
  if (handicap.status && handicap.status !== 'None')
    state.player.statuses[handicap.status] = Number.MAX_SAFE_INTEGER;

  state.busy = false;
  $('end-row').hidden = true;
  $('log').innerHTML = '';

  say(`Wave ${current.number}: ${current.opponent.name} steps in.`);
  if (handicap.kind !== 'None') say(`The reel lands on ${handicap.name}. ${handicap.description}`, 'crit');

  render();
  setStatus(playerIsFaster() ? 'Choose a move.'
    : `Choose a move — ${state.opponent.fighter.name} acts before it lands.`);
}

const playerIsFaster = () => state.player.fighter.speed >= state.opponent.fighter.speed;
const has = (combatant, status) => (combatant.statuses[status] ?? 0) > 0;

// ── Render ─────────────────────────────────────────────────────────────────

function render() {
  const current = wave();

  $('wave-title').textContent = `Wave ${current.number} of ${state.run.waves.length}`;
  $('wave-sub').textContent =
    `${state.run.gameName} · ${state.run.champion.name} Lv ${state.run.champion.level}`;
  $('points').textContent = state.points.toLocaleString();

  renderPips();
  renderReel(current.handicap);

  $('player-card').innerHTML = fighterCard(state.player, 'Your champion', true);
  $('opponent-card').innerHTML = fighterCard(state.opponent, current.opponent.category ?? 'Enemy', false);

  for (const button of $('player-card').querySelectorAll('button[data-move]'))
    button.addEventListener('click', () => takeTurn(state.player.fighter.moves[Number(button.dataset.move)]));
}

function renderPips() {
  const pips = state.run.waves.map((w, i) => {
    const result = state.results[i];
    const cls = result === true ? 'won' : result === false ? 'lost' : i === state.waveIndex ? 'now' : '';
    const glyph = result === true ? '✓' : result === false ? '✕' : String(w.number);
    return `<span class="pip ${cls}" title="Wave ${w.number}">${glyph}</span>`;
  });

  $('wave-pips').innerHTML = pips.join('');
}

function renderReel(handicap) {
  const box = $('reel');
  const clean = handicap.kind === 'None';

  box.className = `reel${clean ? ' clean' : ' spun'}`;
  box.innerHTML =
    `<span class="reel-icon" aria-hidden="true">${clean ? '✦' : '🎰'}</span>` +
    `<div class="reel-body">` +
      `<div class="reel-name">${escapeHtml(handicap.name)}</div>` +
      `<div class="reel-desc">${escapeHtml(handicap.description)}</div>` +
    `</div>` +
    `<div class="reel-mult">×${handicap.multiplier.toFixed(2)} points</div>`;
}

function fighterCard(combatant, role, isPlayer) {
  const f = combatant.fighter;
  const base = isPlayer ? state.run.champion.fighter : f;
  const pct = Math.max(0, (combatant.hp / f.hitPoints) * 100);
  const fill = pct <= 20 ? 'critical' : pct <= 45 ? 'low' : '';
  const first = playerIsFaster() === isPlayer;

  // A stat the reel cut is marked, so the player can see the handicap in the numbers rather
  // than having to remember what it said.
  const stat = (label, value, baseValue) =>
    `<span class="${value < baseValue ? 'cut' : ''}">${label} ${value.toLocaleString()}</span>`;

  return (
    `<img src="${escapeAttr(f.imageUrl ?? '')}" alt="" />` +
    `<div class="fighter-head">` +
      `<div>` +
        `<div class="fighter-name">${escapeHtml(f.name)}</div>` +
        `<div class="fighter-role">${escapeHtml(role)}</div>` +
      `</div>` +
      (first ? '<span class="first-badge">FIRST</span>' : '') +
    `</div>` +
    `<div class="hp-track"><div class="hp-fill ${fill}" style="width:${pct}%"></div></div>` +
    `<div class="hp-text">${Math.max(0, combatant.hp).toLocaleString()} / ${f.hitPoints.toLocaleString()} HP</div>` +
    `<div class="stat-row">` +
      stat('ATK', f.attack, base.attack) +
      stat('DEF', f.defense, base.defense) +
      stat('MAG', f.magicAttack, base.magicAttack) +
      stat('MDEF', f.magicDefense, base.magicDefense) +
      `<span>SPD ${f.speed.toLocaleString()}</span>` +
    `</div>` +
    affinityStrip(f) +
    statusStrip(combatant) +
    `<div class="move-label">${isPlayer ? 'Moves' : 'Its moves'}</div>` +
    movePanel(combatant, isPlayer)
  );
}

function affinityStrip(f) {
  const tags = [
    ...f.weaknesses.map((e) => `<span class="tag weak">weak: ${escapeHtml(e)}</span>`),
    ...f.absorbs.map((e) => `<span class="tag absorb">absorbs: ${escapeHtml(e)}</span>`),
  ];

  return `<div class="affinity">${tags.join('') || '<span class="tag">no elemental affinity</span>'}</div>`;
}

function statusStrip(combatant) {
  const chips = Object.keys(STATUS)
    .filter((key) => has(combatant, key))
    .map((key) => {
      const turns = combatant.statuses[key];
      // A handicap-inflicted status runs the whole wave, so counting its turns down would be a
      // lie. It says so instead.
      const left = turns === Number.MAX_SAFE_INTEGER ? 'this wave' : `${turns} turn${turns === 1 ? '' : 's'}`;
      return `<span class="status-chip ${key.toLowerCase()}" title="${escapeAttr(STATUS[key].hint())}">` +
        `${STATUS[key].label} · ${left}</span>`;
    });

  return `<div class="statuses">${chips.join('') || '<span class="status-none">no conditions</span>'}</div>`;
}

function movePanel(combatant, isPlayer) {
  const silenced = has(combatant, 'Silence');

  const buttons = combatant.fighter.moves.map((move, index) => {
    const locked = silenced && move.kind === 'Magic';
    const effect = effectAgainst(move, isPlayer ? state.opponent.fighter : state.player.fighter);

    const meta = [
      move.element ?? 'non-elemental',
      move.kind,
      effect && `— ${effect}`,
    ].filter(Boolean).join(' · ');

    const warn = [
      move.recoil > 0 && `costs ${Math.round(move.recoil * 100)}% of your own HP`,
      move.status && move.status !== 'None' && `inflicts ${move.status}`,
      locked && 'locked by Silence',
    ].filter(Boolean).join(' · ');

    const body =
      `<span class="move-name">${escapeHtml(move.name)}</span>` +
      `<span class="move-meta">${escapeHtml(meta)}</span>` +
      (warn ? `<span class="move-warn">${escapeHtml(warn)}</span>` : '');

    return isPlayer
      ? `<button class="btn move" data-move="${index}" ${locked || state.busy ? 'disabled' : ''}>${body}</button>`
      : `<div class="move foe-move${locked ? ' locked' : ''}">${body}</div>`;
  });

  return `<div class="moves">${buttons.join('')}</div>`;
}

function effectAgainst(move, defender) {
  if (!move.element) return '';
  if (defender.absorbs.includes(move.element)) return 'absorbed';
  if (defender.weaknesses.includes(move.element)) return 'super effective';
  return '';
}

// ── Combat ─────────────────────────────────────────────────────────────────

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
    finishWave(state.opponent.hp <= 0 && state.player.hp > 0);
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
    // worst move available would also be the one that poisons.
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
  // A handicap status outlasts anything a move can apply, so a move must never shorten it.
  if (combatant.statuses[status] === Number.MAX_SAFE_INTEGER) return;

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
    // A handicap runs the whole wave and is never counted down.
    if (combatant.statuses[key] === Number.MAX_SAFE_INTEGER) continue;

    if (has(combatant, key) && --combatant.statuses[key] === 0)
      say(`${combatant.fighter.name} shakes off ${STATUS[key].label.toLowerCase()}.`);
  }
}

/** Deterministic and simple: the foe takes whichever move does the most damage right now,
 *  and won't self-destruct unless that finishes the fight. */
function chooseFoeMove() {
  const silenced = has(state.opponent, 'Silence');
  const blinded = has(state.opponent, 'Blind');

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
    // presses one. Withheld when the hit would win outright, so this never talks it out of a kill.
    let score = damage;
    if (move.status && move.status !== 'None' && !has(state.player, move.status) && damage < state.player.hp)
      score *= 1.3;

    if (score > bestScore) { bestScore = score; best = move; }
  }

  return best;
}

function finishWave(won) {
  const current = wave();
  state.results[state.waveIndex] = won;
  state.busy = true;

  say(won ? `${state.opponent.fighter.name} is defeated.` : `${state.run.champion.name} falls.`,
      won ? 'hit' : 'bad');

  if (won) {
    state.points += current.battlePoints;
    say(`+${current.battlePoints.toLocaleString()} battle points.`, 'crit');

    // Carry the damage forward. The partial restore is what makes eight consecutive waves
    // possible at all, and keeping it below what a wave costs is what keeps them a run.
    const max = state.run.champion.fighter.hitPoints;
    const before = state.player.hp;
    state.championHp = Math.min(max, before + Math.round(max * WAVE_RECOVERY));

    if (state.waveIndex < state.run.waves.length - 1)
      say(`You recover ${(state.championHp - before).toLocaleString()} HP between waves.`);
  }

  render();

  const last = state.waveIndex === state.run.waves.length - 1;
  if (!won || last) return endRun(won && last);

  $('end-row').hidden = false;
  const button = $('continue-btn');
  button.textContent = `Face wave ${state.run.waves[state.waveIndex + 1].number} →`;
  button.onclick = () => { state.waveIndex++; beginWave(); };
  setStatus('Wave cleared.');
}

function endRun(cleared) {
  $('battle').hidden = true;
  $('over').hidden = false;

  const waves = state.results.filter((r) => r === true).length;

  $('over-title').textContent = cleared ? 'Champion of the Battle Square' : 'Run over';
  $('over-sub').textContent = cleared
    ? `${state.run.champion.name} cleared all ${state.run.waves.length} waves for ${state.points.toLocaleString()} battle points.`
    : `${state.run.champion.name} fell on wave ${waves + 1} of ${state.run.waves.length}, with ${state.points.toLocaleString()} battle points.`;

  $('share').textContent = buildShare(cleared);
  $('copy-btn').onclick = async () => {
    try {
      await navigator.clipboard.writeText(buildShare(cleared));
      $('copy-btn').textContent = 'Copied';
      setTimeout(() => ($('copy-btn').textContent = 'Copy result'), 1500);
    } catch {
      $('copy-btn').textContent = 'Copy failed';
    }
  };
  $('again-btn').onclick = () => {
    $('over').hidden = true;
    $('setup').hidden = false;
    state.selected = null;
    $('level-panel').hidden = true;
    renderRoster();
  };
}

function buildShare(cleared) {
  const squares = state.run.waves
    .map((_, i) => (state.results[i] === true ? '🟩' : state.results[i] === false ? '🟥' : '⬛'))
    .join('');

  return [
    `Battle Square — ${state.run.date}`,
    `${state.run.champion.name} Lv ${state.run.champion.level} · ${state.run.gameName}`,
    squares,
    `${state.points.toLocaleString()} BP${cleared ? ' · cleared' : ''}`,
    'moogleapi.com/battle-square',
  ].join('\n');
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

boot();
