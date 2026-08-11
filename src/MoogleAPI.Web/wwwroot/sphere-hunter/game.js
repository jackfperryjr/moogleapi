/* Sphere Hunter — draft three fiends, take them through eleven hunts.
 *
 * Battles resolve here, but the rules do not live here: every constant comes from the `rules`
 * object in the run payload, which the server built from its own SphereMath. That matters because
 * the server used the same arithmetic to decide each hunt was winnable, and a client carrying its
 * own copy of the numbers drifts until the vetting stops describing the fight the player gets.
 * The one thing hard-coded below is the *shape* of the formula; every number in it is served.
 */
(() => {
  'use strict';

  const API = '/api';
  const PARTY_SIZE = 3;

  const el = (id) => document.getElementById(id);
  const ui = {
    boot: el('boot'), error: el('error'),
    draft: el('draft'), draftGrid: el('draft-grid'), draftPicked: el('draft-picked'),
    draftCount: el('draft-count'), draftGo: el('draft-go'),
    battle: el('battle'), huntNo: el('hunt-no'), huntGame: el('hunt-game'), huntMeta: el('hunt-meta'),
    restart: el('restart'), newHunt: el('new-hunt'),
    ally: el('ally'), foe: el('foe'), bench: el('bench'), moves: el('moves'), log: el('log'),
    capture: el('capture'), result: el('result'),
    statsLabel: el('stats-label'), stats: el('stats')
  };

  /* Not a daily. A run is identified by a token this client invents, and the server derives the
     hand and the expedition from it — so refreshing mid-hunt rebuilds the same run, and starting a new
     one is just a new token. Losing costs a token, not a day. */
  const STORE = 'spherehunter:v2';
  const newRun = () =>
    (crypto.randomUUID?.() ?? `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`)
      .replace(/-/g, '').slice(0, 32);

  let rules = null;
  let run = null;      // { date, party[], hunts[] } from the server
  let state = null;    // the run in progress, persisted

  // ── Rules-driven maths, mirroring SphereMath ───────────────────────────────
  // Opposed pairs. The grid is symmetric — opposites hurt each other — because Final Fantasy's
  // elements are oppositions rather than a directed cycle, and an element is resisted by itself.
  const OPPOSITE = {
    Fire: 'Ice', Ice: 'Fire', Thunder: 'Water', Water: 'Thunder',
    Earth: 'Wind', Wind: 'Earth', Holy: 'Dark', Dark: 'Holy'
  };

  const healthAt = (sphere, level) =>
    Math.max(1, Math.round(sphere.hitPoints * rules.healthPerRating * level / rules.referenceLevel));

  /** Published data wins outright; the grid only fills its silence. Never both, or a real weakness
   *  and the grid's opinion of the same pairing compound to quadruple damage. */
  function effectiveness(defender, element) {
    if (!element) return 1;
    if (defender.absorbs.includes(element)) return 0;
    if (defender.weaknesses.includes(element)) return rules.superEffective;
    if (!defender.affinity) return 1;
    if (element === defender.affinity) return rules.notVeryEffective;
    return OPPOSITE[element] === defender.affinity ? rules.superEffective : 1;
  }

  function baseDamage(attacker, defender, move, level) {
    const magic = move.category === 'Magic';
    const offence = magic ? attacker.magicAttack : attacker.attack;
    const guard = Math.max(1, magic ? defender.magicDefense : defender.defense);
    return (2 * level / 5 + 2) * move.power * offence / guard / 50 + 2;
  }

  /** Everything but the dice. Kept separate so the move buttons can quote a number. */
  function deterministic(attacker, defender, move, level, status) {
    let damage = baseDamage(attacker, defender, move, level)
      * (move.element && move.element === attacker.affinity ? rules.affinityBonus : 1)
      * effectiveness(defender, move.element);

    if (move.category !== 'Physical') return damage;
    if (status === 'Blind') damage *= rules.blindMultiplier;
    if (status === 'Sap') damage *= rules.sapAttackMultiplier;
    return damage;
  }

  /* Rolled in the browser rather than from a shared seed. Opponent selection is seeded and
     identical for everyone; the dice inside a battle are not, and do not need to be — nothing is
     scored server-side, and a shared result grid records which hunt you reached, not how. */
  function resolve(attacker, defender, move, level, status) {
    const effect = effectiveness(defender, move.element);
    if (effect === 0) return { damage: 0, missed: false, critical: false, effect };
    if (Math.random() * 100 >= move.accuracy) return { damage: 0, missed: true, critical: false, effect };

    const critical = Math.random() < rules.criticalChance;
    const variance = rules.minVariance + (rules.maxVariance - rules.minVariance) * Math.random();
    const damage = deterministic(attacker, defender, move, level, status)
      * (critical ? rules.criticalMultiplier : 1) * variance;

    return { damage: Math.max(1, Math.round(damage)), missed: false, critical, effect };
  }

  const tickDamage = (unit, level) => {
    if (unit.status === 'Poison') return Math.max(1, Math.round(healthAt(unit.sphere, level) * rules.poisonShare * unit.statusTurns));
    if (unit.status === 'Sap') return Math.max(1, Math.round(healthAt(unit.sphere, level) * rules.sapShare));
    return 0;
  };

  const speedOf = (unit) =>
    unit.status === 'Paralyze'
      ? Math.round(unit.sphere.speed * rules.paralyzeSpeedMultiplier)
      : unit.sphere.speed;

  // ── State ──────────────────────────────────────────────────────────────────
  function save() {
    try { localStorage.setItem(STORE, JSON.stringify(state)); }
    catch { /* private mode: playable, just not resumable */ }
  }

  /** Only ever one run in storage — a new one replaces the last, finished or not. */
  function load() {
    try {
      const raw = localStorage.getItem(STORE);
      if (raw) return JSON.parse(raw);
    } catch { /* corrupt — start fresh */ }
    return null;
  }

  /** A sphere as it stands right now: current health, magic, gauge and condition. */
  function unitOf(sphere, level) {
    return {
      id: sphere.id, sphere,
      hp: healthAt(sphere, level), mp: sphere.magic,
      limit: 0, limitSpent: false, status: 'None', statusTurns: 0, sleepFor: 0
    };
  }

  /** Rebuilds the live units for a hunt. Health carries between hunts as a fraction, so a
   *  sphere that ended the last hunt at a third stays at a third of a bigger pool. */
  function enterHunt(hunt) {
    const level = hunt.level;

    state.party = state.party.map((prev) => {
      const sphere = run.party.concat(state.captured || []).find((s) => s.id === prev.id) || prev.sphere;
      const max = healthAt(sphere, level);
      const fraction = prev.max ? prev.hp / prev.max : 1;

      // Recovery is partial on purpose: below what a hunt costs, so the run trends downward.
      const restored = Math.min(1, fraction + rules.recoveryBetweenHunts);
      return {
        id: sphere.id, sphere, max,
        hp: prev.hp <= 0 ? Math.max(1, Math.round(max * rules.recoveryBetweenHunts)) : Math.max(1, Math.round(max * restored)),
        mp: Math.min(sphere.magic, Math.round(prev.mp + sphere.magic * rules.recoveryBetweenHunts)),
        limit: prev.limit || 0, limitSpent: false, status: 'None', statusTurns: 0, sleepFor: 0
      };
    });

    state.active = state.party.findIndex((u) => u.hp > 0);
    startBattle();
  }

  function startBattle() {
    const hunt = run.hunts[state.hunt];
    const sphere = hunt.opponents[state.battle];
    const level = hunt.level;

    state.foe = {
      id: sphere.id, sphere, max: healthAt(sphere, level),
      hp: healthAt(sphere, level), mp: sphere.magic,
      limit: 0, limitSpent: false, status: 'None', statusTurns: 0, sleepFor: 0
    };
    state.busy = false;
    log(`Hunt ${hunt.number} — ${sphere.name} stands in the way.`, 'foe');
    save();
    render();
  }

  // ── Turn resolution ────────────────────────────────────────────────────────
  const active = () => state.party[state.active];
  const alive = () => state.party.filter((u) => u.hp > 0);

  function log(text, cls) {
    const p = document.createElement('p');
    if (cls) p.className = cls;
    p.textContent = text;
    ui.log.prepend(p);
    while (ui.log.children.length > 60) ui.log.lastChild.remove();
  }

  const effectWord = (effect) =>
    effect === 0 ? ' It is absorbed!'
      : effect >= rules.superEffective ? " It's super effective!"
        : effect <= rules.notVeryEffective ? " It's not very effective…" : '';

  /** One exchange: the player's chosen move, then the opponent's, in speed order. */
  async function takeTurn(move) {
    if (state.busy || state.done) return;
    state.busy = true;
    render();

    const hunt = run.hunts[state.hunt];
    const level = hunt.level;
    const playerFirst = speedOf(active()) >= speedOf(state.foe);

    const order = playerFirst ? ['player', 'foe'] : ['foe', 'player'];
    for (const side of order) {
      if (state.foe.hp <= 0 || active().hp <= 0) break;
      if (side === 'player') act(active(), state.foe, move, level, true);
      else act(state.foe, active(), pickFoeMove(state.foe, active(), level), level, false);
    }

    // End of turn: conditions bleed after both sides have acted.
    for (const unit of [active(), state.foe]) {
      if (unit.hp <= 0) continue;
      const bleed = tickDamage(unit, level);
      if (bleed > 0) {
        unit.hp = Math.max(0, unit.hp - bleed);
        log(`${unit.sphere.name} loses ${bleed} to ${unit.status.toLowerCase()}.`);
      }
      if (unit.statusTurns > 0 && --unit.statusTurns === 0 && unit.status !== 'None') {
        log(`${unit.sphere.name} shakes off ${unit.status.toLowerCase()}.`);
        unit.status = 'None';
      }
    }

    state.busy = false;
    settle();
  }

  function act(attacker, defender, move, level, isPlayer) {
    if (!move) return;

    if (attacker.sleepFor > 0) {
      attacker.sleepFor--;
      if (attacker.sleepFor === 0) { attacker.status = 'None'; log(`${attacker.sphere.name} wakes up.`); }
      else log(`${attacker.sphere.name} is fast asleep.`);
      return;
    }
    if (attacker.status === 'Paralyze' && Math.random() < rules.paralyzeSkipChance) {
      log(`${attacker.sphere.name} is paralysed and cannot move.`);
      return;
    }

    if (move.isLimit) { attacker.limit = 0; attacker.limitSpent = true; }
    else if (move.magicCost > 0) attacker.mp -= move.magicCost;

    const strike = resolve(attacker.sphere, defender.sphere, move, level, attacker.status);

    if (strike.missed) {
      log(`${attacker.sphere.name} uses ${move.name} — and misses.`, isPlayer ? '' : 'foe');
    } else {
      defender.hp = Math.max(0, defender.hp - strike.damage);
      const cls = move.isLimit ? 'big' : strike.critical ? 'crit' : isPlayer ? 'hit' : 'foe';
      log(`${attacker.sphere.name} uses ${move.name} for ${strike.damage}.` +
        (strike.critical ? ' Critical hit!' : '') + effectWord(strike.effect), cls);

      // The gauge fills on damage taken, so the sphere carrying the party earns the payoff — and
      // it survives a switch, which is what makes a battered sphere on the bench worth something.
      //
      // Only for something that can spend it. Opponents are served without a Limit, so filling
      // theirs would light a "Limit ready" badge over a move they do not have.
      if (strike.damage > 0 && !defender.limitSpent && defender.sphere.moves.some((m) => m.isLimit)) {
        defender.limit = Math.min(rules.limitFull,
          defender.limit + Math.round(rules.limitFull * rules.limitFillRate * strike.damage / defender.max));
      }

      if (move.status && move.status !== 'None' && defender.status === 'None' && defender.hp > 0) {
        defender.status = move.status;
        defender.statusTurns = rules.statusTurns;
        if (move.status === 'Sleep') {
          defender.sleepFor = rules.minSleepTurns +
            Math.hunt(Math.random() * (rules.maxSleepTurns - rules.minSleepTurns + 1));
        }
        log(`${defender.sphere.name} is ${move.status.toLowerCase()}.`);
      }
    }

    // Self-destruct takes its user with it, which is the whole point of the button.
    if (move.recoil > 0) {
      const cost = Math.round(attacker.max * move.recoil);
      attacker.hp = Math.max(0, attacker.hp - cost);
      log(`${attacker.sphere.name} takes ${cost} from the blast.`);
    }
  }

  /** The opponent plays greedily: its best affordable move by expected damage. */
  function pickFoeMove(foe, target, level) {
    const usable = foe.sphere.moves.filter((m) =>
      (!m.isLimit || (foe.limit >= rules.limitFull && !foe.limitSpent)) &&
      (m.magicCost === 0 || foe.mp >= m.magicCost) &&
      !(foe.status === 'Silence' && m.category === 'Magic') &&
      // It will spend its own life to win, but not to lose.
      !(m.recoil > 0 && foe.hp > target.hp));

    if (usable.length === 0) return foe.sphere.moves[0];

    return usable.reduce((best, m) => {
      const value = deterministic(foe.sphere, target.sphere, m, level, foe.status) * (m.accuracy / 100);
      const bestValue = deterministic(foe.sphere, target.sphere, best, level, foe.status) * (best.accuracy / 100);
      return value > bestValue ? m : best;
    });
  }

  /** Switching is free and resolves before the opponent acts — so it costs a turn, not a hit. */
  function switchTo(index) {
    if (state.busy || state.done || index === state.active) return;
    if (state.party[index].hp <= 0) return;

    log(`${state.party[index].sphere.name} takes the field.`);
    state.active = index;

    // A forced switch after a faint is not a turn; a voluntary one is.
    if (state.foe.hp > 0 && !state.forced) takeTurn(null);
    else { state.forced = false; save(); render(); }
  }

  /** After every exchange: did anything die, and what happens next? */
  function settle() {
    const hunt = run.hunts[state.hunt];

    if (state.foe.hp <= 0) {
      log(`${state.foe.sphere.name} is defeated.`, 'hit');
      state.battle++;

      if (state.battle >= hunt.opponents.length) {
        state.battle = 0;
        offerCapture(hunt);
        return;
      }
      startBattle();
      return;
    }

    if (active().hp <= 0) {
      log(`${active().sphere.name} is out of the fight.`, 'foe');
      const next = state.party.findIndex((u) => u.hp > 0);
      if (next === -1) return finish(false);

      state.forced = true;
      switchTo(next);
      return;
    }

    save();
    render();
  }

  // ── Capture ────────────────────────────────────────────────────────────────
  function offerCapture(hunt) {
    save();
    render();

    const fiend = hunt.capture;
    ui.capture.innerHTML = '';

    const box = document.createElement('div');
    box.className = 'capture';
    box.innerHTML =
      `<h3>Seal ${escapeHtml(fiend.name)}?</h3>
       <div class="capture-body">
         <figure class="sphere">${fiend.imageUrl ? `<img src="${fiend.imageUrl}" alt="" />` : ''}</figure>
         <div>
           <p style="margin:0 0 0.3rem">${escapeHtml(fiend.name)} — ${escapeHtml(fiend.gameName)}</p>
           <div class="draft-stats" style="max-width:280px">
             <span>HP <b>${fiend.hitPoints}</b></span><span>ATK <b>${fiend.attack}</b></span><span>DEF <b>${fiend.defense}</b></span>
             <span>MAG <b>${fiend.magicAttack}</b></span><span>MDF <b>${fiend.magicDefense}</b></span><span>SPD <b>${fiend.speed}</b></span>
           </div>
         </div>
       </div>
       <p class="page-sub" style="margin:0.6rem 0 0; font-size:0.8rem">
         A sphere holds three. Taking it means giving one up — and the one you give up does not come back.
       </p>
       <div class="capture-actions" id="capture-actions"></div>`;
    ui.capture.appendChild(box);

    const actions = box.querySelector('#capture-actions');
    state.party.forEach((unit, i) => {
      const swap = document.createElement('button');
      swap.className = 'btn';
      swap.textContent = `Release ${unit.sphere.name}`;
      swap.addEventListener('click', () => {
        (state.captured = state.captured || []).push(fiend);
        state.party[i] = { ...unitOf(fiend, hunt.level), max: healthAt(fiend, hunt.level) };
        log(`${fiend.name} is sealed. ${unit.sphere.name} is released.`, 'big');
        nextHunt();
      });
      actions.appendChild(swap);
    });

    const decline = document.createElement('button');
    decline.className = 'btn btn-ghost';
    decline.textContent = 'Leave it';
    decline.addEventListener('click', nextHunt);
    actions.appendChild(decline);
  }

  function nextHunt() {
    ui.capture.innerHTML = '';
    state.hunt++;
    if (state.hunt >= run.hunts.length) return finish(true);

    if (state.active === -1 || state.party[state.active].hp <= 0) {
      state.active = state.party.findIndex((u) => u.hp > 0);
    }
    enterHunt(run.hunts[state.hunt]);
  }

  function finish(won) {
    state.done = true;
    state.won = won;
    save();
    render();

    // No date: a streak counts consecutive wins rather than consecutive days, which is what a
    // streak means in a game you can retry. The flag is what stops a refresh of a finished run
    // counting it twice — supplying a date used to do that job.
    if (!state.recorded) {
      state.recorded = true;
      save();
      MoogleStats.record('sphere-hunter', won ? 'win' : 'loss', { bucket: won ? 'clear' : `F${state.hunt + 1}` });
    }

    ui.result.hidden = false;
    const reached = won ? run.hunts.length : state.hunt + 1;
    ui.result.innerHTML =
      `<h2>${won ? 'Every mark taken' : `The hunt ended on ${reached}`}</h2>
       <p>${won
        ? `All ${run.hunts.length} hunts cleared.`
        : `${escapeHtml(run.hunts[state.hunt].gameName)} ended the run.`}</p>
       <pre class="share-grid">${shareGrid(reached)}</pre>
       <div class="actions">
         <button class="btn" id="again">${won ? 'Hunt again' : 'New hunt'}</button>
         <button class="btn btn-ghost" id="retry">Same monsters</button>
         <button class="btn btn-ghost" id="share">Copy result</button>
       </div>`;

    el('again').addEventListener('click', restart);
    el('retry').addEventListener('click', retry);

    el('share').addEventListener('click', async (e) => {
      const text = `Sphere Hunter — ${won ? 'cleared' : `hunt ${reached}/${run.hunts.length}`}\n\n${shareGrid(reached)}\n\nmoogleapi.com/sphere-hunter`;
      try {
        await navigator.clipboard.writeText(text);
        e.target.textContent = 'Copied!';
        setTimeout(() => { e.target.textContent = 'Copy result'; }, 1800);
      } catch { e.target.textContent = 'Copy failed'; }
    });

    MoogleStats.render(ui.stats, 'sphere-hunter', ['played', 'wins', 'winPct', 'streak', 'best']);
    ui.statsLabel.hidden = false;
  }

  /** One square per hunt — the shape of the run, not which fiends were in it. */
  const shareGrid = (reached) =>
    run.hunts.map((_, i) => (i < reached - (state.won ? 0 : 1) ? '🟩' : i === reached - 1 && !state.won ? '🟥' : '⬛'))
      .join('');

  // ── Rendering ──────────────────────────────────────────────────────────────
  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, (c) =>
      ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }

  const elemTag = (e) => `<span class="elem elem-${e || 'none'}">${e || 'Neutral'}</span>`;

  function sphereArt(sphere, fainted) {
    return `<figure class="sphere${fainted ? ' fainted' : ''}">${
      sphere.imageUrl ? `<img src="${sphere.imageUrl}" alt="" />` : ''}</figure>`;
  }

  /** The fight shows the whole picture rather than the sphere. A sphere is what a fiend is kept
   *  in — right for the draft, the bench and the seal — but a circle crops the artwork to a face,
   *  and in the one place two monsters are actually facing each other you want to see them. */
  function cardArt(sphere, fainted) {
    return `<figure class="art-card${fainted ? ' fainted' : ''}">${
      sphere.imageUrl ? `<img src="${sphere.imageUrl}" alt="" />` : ''}</figure>`;
  }

  function bar(cls, value, max) {
    const pct = Math.max(0, Math.min(100, (value / Math.max(1, max)) * 100));
    const tone = cls === 'hp' ? (pct <= 25 ? ' critical' : pct <= 55 ? ' hurt' : '') : '';
    return `<div class="bar ${cls}${tone}"><i style="width:${pct}%"></i></div>`;
  }

  function combatant(unit, isFoe) {
    const badges = [];
    if (unit.status !== 'None') badges.push(unit.status);
    if (unit.limit >= rules.limitFull && !unit.limitSpent) badges.push('Limit ready');

    return `${cardArt(unit.sphere, unit.hp <= 0)}
      <div class="combatant-name">${escapeHtml(unit.sphere.name)}</div>
      <div class="combatant-game">${escapeHtml(unit.sphere.gameName)} · ${elemTag(unit.sphere.affinity)}</div>
      <div class="gauge-row"><span>HP</span>${bar(isFoe ? 'hp foe' : 'hp', unit.hp, unit.max)}</div>
      <div class="gauge-row"><span>${unit.hp}/${unit.max}</span><span></span></div>
      ${isFoe ? '' : `<div class="gauge-row"><span>MP</span>${bar('mp', unit.mp, unit.sphere.magic)}</div>
                      <div class="gauge-row"><span>LIM</span>${bar('limit', unit.limitSpent ? 0 : unit.limit, rules.limitFull)}</div>`}
      <div class="badges">${badges.map((b) => `<span class="badge">${b}</span>`).join('')}</div>`;
  }

  function render() {
    if (state.done) { ui.moves.innerHTML = ''; return; }

    const hunt = run.hunts[state.hunt];
    ui.huntNo.textContent = `Hunt ${hunt.number} / ${run.hunts.length}`;
    ui.huntGame.textContent = hunt.gameName;
    ui.huntMeta.textContent = `Level ${hunt.level} · battle ${state.battle + 1}/${hunt.opponents.length}`;

    ui.ally.innerHTML = combatant(active(), false);
    ui.foe.innerHTML = combatant(state.foe, true);

    // Bench
    ui.bench.innerHTML = '';
    state.party.forEach((unit, i) => {
      const slot = document.createElement('button');
      slot.className = `bench-slot${i === state.active ? ' active' : ''}`;
      slot.disabled = unit.hp <= 0 || i === state.active || state.busy;
      slot.innerHTML = `${sphereArt(unit.sphere, unit.hp <= 0)}
        <div>
          <div class="bench-name">${escapeHtml(unit.sphere.name)}</div>
          <div class="bench-bars">${bar('hp', unit.hp, unit.max)}${bar('limit', unit.limitSpent ? 0 : unit.limit, rules.limitFull)}</div>
        </div>`;
      slot.addEventListener('click', () => switchTo(i));
      ui.bench.appendChild(slot);
    });

    // Moves
    ui.moves.innerHTML = '';
    const me = active();
    me.sphere.moves.forEach((move) => {
      const ready = move.isLimit && me.limit >= rules.limitFull && !me.limitSpent;
      if (move.isLimit && !ready) return;

      const button = document.createElement('button');
      button.className = `move${move.isLimit ? ' limit' : ''}`;
      button.disabled = state.busy ||
        (move.magicCost > 0 && me.mp < move.magicCost) ||
        (me.status === 'Silence' && move.category === 'Magic');

      const expected = Math.round(deterministic(me.sphere, state.foe.sphere, move, hunt.level, me.status));
      button.innerHTML = `<span class="move-name">${escapeHtml(move.name)}</span>
        <span class="move-meta">${move.element || 'Neutral'} · ${move.category} · ~${expected} dmg</span>
        <span class="move-meta">${move.accuracy}% acc${move.magicCost ? ` · ${move.magicCost} MP` : ''}${move.recoil ? ' · recoil' : ''}</span>`;
      button.addEventListener('click', () => takeTurn(move));
      ui.moves.appendChild(button);
    });
  }

  // ── Draft ──────────────────────────────────────────────────────────────────
  function renderDraft(spheres, token) {
    const picked = [];

    const refresh = () => {
      ui.draftPicked.innerHTML = '';
      for (let i = 0; i < PARTY_SIZE; i++) {
        const slot = document.createElement('div');
        slot.className = 'draft-slot';
        const s = picked[i];
        slot.innerHTML = s ? (s.imageUrl ? `<img src="${s.imageUrl}" alt="${escapeHtml(s.name)}" />` : escapeHtml(s.name[0])) : `${i + 1}`;
        ui.draftPicked.appendChild(slot);
      }
      ui.draftCount.textContent = `${picked.length} of ${PARTY_SIZE} sealed`;
      ui.draftGo.disabled = picked.length !== PARTY_SIZE;
      [...ui.draftGrid.children].forEach((card) =>
        card.setAttribute('aria-pressed', String(picked.some((p) => String(p.id) === card.dataset.id))));
    };

    ui.draftGrid.innerHTML = '';
    spheres.forEach((s) => {
      const card = document.createElement('button');
      card.className = 'draft-card';
      card.dataset.id = s.id;
      card.setAttribute('aria-pressed', 'false');
      card.innerHTML = `${sphereArt(s, false)}
        <div>
          <div class="draft-name">${escapeHtml(s.name)}</div>
          <div class="draft-game">${escapeHtml(s.gameName)}</div>
        </div>
        <div>${elemTag(s.affinity)}</div>
        <div class="draft-stats">
          <span>HP <b>${s.hitPoints}</b></span><span>ATK <b>${s.attack}</b></span><span>DEF <b>${s.defense}</b></span>
          <span>MAG <b>${s.magicAttack}</b></span><span>MDF <b>${s.magicDefense}</b></span><span>SPD <b>${s.speed}</b></span>
        </div>
        <div class="draft-game">${s.moves.length} moves${s.weaknesses.length ? ` · weak to ${s.weaknesses.join(', ')}` : ''}</div>`;

      card.addEventListener('click', () => {
        const at = picked.findIndex((p) => p.id === s.id);
        if (at >= 0) picked.splice(at, 1);
        else if (picked.length < PARTY_SIZE) picked.push(s);
        refresh();
      });
      ui.draftGrid.appendChild(card);
    });

    refresh();
    // The button is replaced rather than added to: dealing a second hand into the same element
    // would otherwise leave the first hand's listener attached and start the previous run.
    const go = ui.draftGo.cloneNode(true);
    ui.draftGo.replaceWith(go);
    ui.draftGo = go;
    ui.draftGo.disabled = picked.length !== PARTY_SIZE;
    ui.draftGo.addEventListener('click', () => begin(picked.map((p) => p.id), token));
  }

  async function begin(ids, token) {
    ui.draft.hidden = true;
    ui.boot.hidden = false;
    ui.boot.textContent = 'Building the expedition…';

    try {
      const res = await fetch(`${API}/sphere-hunter/run?${new URLSearchParams({ spheres: ids.join(','), run: token })}`);
      if (!res.ok) throw new Error(`Server returned ${res.status}`);
      run = await res.json();
      rules = run.rules;
    } catch (err) {
      return fail(`Could not build the run. ${err.message}`);
    }

    state = {
      run: token, party: run.party.map((s) => ({ id: s.id, sphere: s, max: 0, hp: 1, mp: s.magic, limit: 0 })),
      hunt: 0, battle: 0, active: 0, captured: [], done: false, won: false, recorded: false
    };

    ui.boot.hidden = true;
    ui.battle.hidden = false;
    enterHunt(run.hunts[0]);
  }

  async function resume(saved) {
    try {
      const ids = saved.party.map((u) => u.id).join(',');
      const res = await fetch(`${API}/sphere-hunter/run?${new URLSearchParams({ spheres: ids, run: saved.run })}`);
      if (!res.ok) throw new Error(`Server returned ${res.status}`);
      run = await res.json();
      rules = run.rules;
    } catch {
      // A run we cannot rebuild is a run we cannot continue — start over rather than half-load it.
      return false;
    }

    state = saved;
    // Units were persisted with their sphere data; reattach the served copies so a change to the
    // catalogue does not leave the board holding a stale one.
    const known = run.party.concat(state.captured || []);
    state.party.forEach((u) => { u.sphere = known.find((s) => s.id === u.id) || u.sphere; });

    ui.boot.hidden = true;
    ui.battle.hidden = false;
    if (state.done) { render(); finish(state.won); return true; }
    startBattle();
    return true;
  }

  function fail(message) {
    ui.boot.hidden = true;
    ui.error.hidden = false;
    ui.error.textContent = message;
  }

  // ── Boot ───────────────────────────────────────────────────────────────────
  /** Runs the same expedition again from the first hunt — same party, same fiends, same order.
   *  No refetch: the run payload already holds everything, and re-requesting it with the same
   *  token would rebuild exactly what is in memory. */
  function retry() {
    if (!run) return;

    ui.result.hidden = true;
    ui.capture.innerHTML = '';
    ui.log.replaceChildren();
    ui.battle.hidden = false;

    state = {
      run: state.run,
      party: run.party.map((s) => ({ id: s.id, sphere: s, max: 0, hp: 1, mp: s.magic, limit: 0 })),
      hunt: 0, battle: 0, active: 0, captured: [], done: false, won: false, recorded: false
    };

    enterHunt(run.hunts[0]);
  }

  /** Abandons whatever is on screen and deals a fresh hand. The old run is simply forgotten. */
  async function restart() {
    ui.result.hidden = true;
    ui.battle.hidden = true;
    ui.capture.innerHTML = '';
    ui.log.replaceChildren();
    run = null;
    state = null;
    try { localStorage.removeItem(STORE); } catch { /* nothing to clear */ }
    await deal();
  }

  async function deal() {
    const token = newRun();
    ui.boot.hidden = false;
    ui.boot.className = 'loading';
    ui.boot.textContent = 'Opening a fresh set of spheres…';

    try {
      const res = await fetch(`${API}/sphere-hunter/draft?${new URLSearchParams({ run: token })}`);
      if (!res.ok) throw new Error(`Server returned ${res.status}`);
      const data = await res.json();
      rules = data.rules;

      ui.boot.hidden = true;
      ui.draft.hidden = false;
      renderDraft(data.spheres, token);
    } catch (err) {
      fail(`Could not open the spheres. ${err.message}`);
    }
  }

  /** A destructive button that asks first, in place, rather than through a modal. */
  function arm(button, prompt, action) {
    let armed = false;
    const label = button.textContent;

    button.addEventListener('click', () => {
      if (!armed) { armed = true; button.textContent = prompt; return; }
      armed = false;
      button.textContent = label;
      action();
    });

    button.addEventListener('blur', () => {
      if (armed) { armed = false; button.textContent = label; }
    });
  }

  async function init() {
    arm(ui.restart, 'Start over?', retry);
    arm(ui.newHunt, 'New monsters?', restart);

    const saved = load();
    if (saved && await resume(saved)) return;

    await deal();
  }

  init();
})();
