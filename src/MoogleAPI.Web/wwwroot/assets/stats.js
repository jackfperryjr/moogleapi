/* Shared win/loss/streak tracking for the moogleAPI games.
 *
 * Two kinds of streak live here. Kupodle is a daily puzzle, so its streak counts consecutive
 * *days* solved and breaks when a day is skipped — winning twice in one day can't extend it.
 * Triple Triad and Guess Who are replayable, so theirs simply counts consecutive wins.
 */
window.MoogleStats = (() => {
  'use strict';

  const KEY = (game) => `moogle:stats:${game}`;

  const BLANK = {
    played: 0, wins: 0, losses: 0, draws: 0,
    streak: 0, best: 0,
    lastDate: null,   // daily games only
    dist: {}          // optional histogram, e.g. Kupodle guesses-to-solve
  };

  function get(game) {
    try {
      const raw = localStorage.getItem(KEY(game));
      if (raw) return { ...BLANK, ...JSON.parse(raw) };
    } catch { /* corrupt or unavailable storage — fall through to a blank record */ }
    return { ...BLANK };
  }

  function save(game, stats) {
    try { localStorage.setItem(KEY(game), JSON.stringify(stats)); }
    catch { /* private mode: stats just won't persist */ }
  }

  const dayBefore = (iso) => {
    const d = new Date(`${iso}T00:00:00Z`);
    d.setUTCDate(d.getUTCDate() - 1);
    return d.toISOString().slice(0, 10);
  };

  /**
   * @param outcome 'win' | 'loss' | 'draw'
   * @param opts.date  ISO day for daily games. Supplying it switches to day-based streaks
   *                   and makes the call idempotent for that day.
   * @param opts.bucket optional histogram key (Kupodle: number of guesses used)
   */
  function record(game, outcome, opts = {}) {
    const stats = get(game);

    // A daily result must only ever count once, no matter how often the page is reloaded.
    if (opts.date && stats.lastDate === opts.date) return stats;

    stats.played++;
    if (outcome === 'win')       stats.wins++;
    else if (outcome === 'loss') stats.losses++;
    else                         stats.draws++;

    if (outcome === 'win') {
      const continues = opts.date ? stats.lastDate === dayBefore(opts.date) : true;
      stats.streak = continues ? stats.streak + 1 : 1;
      stats.best = Math.max(stats.best, stats.streak);
    } else {
      stats.streak = 0;
    }

    if (opts.date) stats.lastDate = opts.date;

    if (opts.bucket !== undefined && opts.bucket !== null) {
      const k = String(opts.bucket);
      stats.dist[k] = (stats.dist[k] || 0) + 1;
    }

    save(game, stats);
    return stats;
  }

  function reset(game) {
    try { localStorage.removeItem(KEY(game)); } catch { /* nothing to clear */ }
    return { ...BLANK };
  }

  const pct = (n, d) => (d === 0 ? 0 : Math.round((n / d) * 100));

  /**
   * Renders the stat strip into `el`. `fields` picks which tiles to show, so a drawless
   * game doesn't display a permanent "Draws 0".
   */
  function render(el, game, fields = ['played', 'wins', 'losses', 'winPct', 'streak', 'best']) {
    const s = get(game);
    const values = {
      played: ['Played', s.played],
      wins:   ['Won', s.wins],
      losses: ['Lost', s.losses],
      draws:  ['Drawn', s.draws],
      winPct: ['Win %', `${pct(s.wins, s.played)}%`],
      streak: ['Streak', s.streak],
      best:   ['Best', s.best]
    };

    el.replaceChildren(...fields.map((f) => {
      const [label, value] = values[f];
      const box = document.createElement('div');
      box.className = 'stat';
      const v = document.createElement('span');
      v.className = 'stat-v';
      v.textContent = value;
      const k = document.createElement('span');
      k.className = 'stat-k';
      k.textContent = label;
      box.append(v, k);
      return box;
    }));
    return s;
  }

  return { get, record, reset, render };
})();
