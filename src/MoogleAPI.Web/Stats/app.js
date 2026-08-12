/* The request-log analytics: the charts and counters behind /stats.
 *
 * LOAD THIS AT THE END OF <body>, after the Chart.js tag in <head> - Chart is read at execution
 * to set the dark-theme defaults, before any chart is built.
 *
 * Served by an explicit route rather than by UseStaticFiles - this file sits outside wwwroot, so
 * it stays behind the same "Dashboard" authorization policy as the page that loads it.
 */
(() => {
  'use strict';

  // Chart.js global defaults for dark theme
  Chart.defaults.color         = '#90a4ae';
  Chart.defaults.borderColor   = '#455a64';
  Chart.defaults.font.family   = "'Raleway', sans-serif";
  Chart.defaults.font.size     = 11;

  const BLUE   = 'rgba(66,133,244,0.85)';
  const BLUE_B = '#4285f4';
  const GOLD   = 'rgba(255,236,179,0.85)';
  const RED    = 'rgba(239,154,154,0.85)';
  const GREEN  = 'rgba(200,230,201,0.85)';
  const YELLOW = 'rgba(255,245,157,0.85)';
  const MUTED  = 'rgba(144,164,174,0.5)';

  const STATUS_COLORS = {
    200: GREEN, 201: GREEN, 204: GREEN,
    400: YELLOW, 401: YELLOW, 404: YELLOW,
    429: GOLD,
    500: RED, 503: RED,
  };
  const statusColor = code => STATUS_COLORS[code] ?? MUTED;

  let charts = {};

  function destroyCharts() {
    Object.values(charts).forEach(c => c.destroy());
    charts = {};
  }

  function fmt(n) { return Number(n).toLocaleString(); }

  function renderDashboard(data) {
    destroyCharts();

    // Cards
    document.getElementById('c-total').textContent       = fmt(data.summary.totalRequests);
    document.getElementById('c-range').textContent       = fmt(data.summary.requestsInRange);
    document.getElementById('c-errors').textContent      = fmt(data.summary.errorsInRange);
    document.getElementById('c-ratelimited').textContent = fmt(data.summary.rateLimitedInRange);
    document.getElementById('c-ips').textContent         = fmt(data.summary.uniqueClientsInRange);
    document.getElementById('c-range-label').textContent = rangeLabel();

    // What was actually measured, which is not always what was asked for: the server trims very
    // large ranges to their most recent slice and says so.
    const r = data.range;
    const span = `${new Date(r.from).toLocaleString()} → ${new Date(r.to).toLocaleString()}`;
    document.getElementById('range-note').textContent =
      r.truncated ? `${span} · trimmed to the most recent rows` : span;
    document.getElementById('h-timeline').textContent =
      r.granularity === 'hour' ? 'Requests / Hour' : 'Requests / Day';

    // Latency
    document.getElementById('l-avg').innerHTML = `${data.latency.avgMs}<span class="latency-unit">ms</span>`;
    document.getElementById('l-p50').innerHTML = `${data.latency.p50Ms}<span class="latency-unit">ms</span>`;
    document.getElementById('l-p95').innerHTML = `${data.latency.p95Ms}<span class="latency-unit">ms</span>`;

    // Timeline. Labels follow the granularity the server picked — a daily series labelled with
    // clock times reads as a series of midnights.
    const tlLabels = data.requestsOverTime.map(b => {
      const d = new Date(b.bucket);
      return data.range.granularity === 'hour'
        ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
        : d.toLocaleDateString([], { month: 'short', day: 'numeric' });
    });
    charts.timeline = new Chart(document.getElementById('chart-timeline'), {
      type: 'line',
      data: {
        labels: tlLabels,
        datasets: [{
          label: 'Requests',
          data: data.requestsOverTime.map(b => b.count),
          borderColor: BLUE_B,
          backgroundColor: 'rgba(66,133,244,0.12)',
          tension: 0.35,
          fill: true,
          pointRadius: 3,
          pointBackgroundColor: BLUE_B,
        }],
      },
      options: {
        responsive: true,
        plugins: { legend: { display: false } },
        scales: {
          x: { grid: { color: 'rgba(69,90,100,0.5)' } },
          y: { grid: { color: 'rgba(69,90,100,0.5)' }, beginAtZero: true, ticks: { precision: 0 } },
        },
      },
    });

    // Status codes donut
    charts.status = new Chart(document.getElementById('chart-status'), {
      type: 'doughnut',
      data: {
        labels: data.statusCodes.map(s => String(s.statusCode)),
        datasets: [{
          data: data.statusCodes.map(s => s.count),
          backgroundColor: data.statusCodes.map(s => statusColor(s.statusCode)),
          borderColor: '#37474f',
          borderWidth: 2,
        }],
      },
      options: {
        responsive: true,
        cutout: '65%',
        plugins: {
          legend: { position: 'bottom', labels: { padding: 12, boxWidth: 12 } },
        },
      },
    });

    // Top endpoints
    const epData = data.topEndpoints.slice(0, 8);
    charts.endpoints = new Chart(document.getElementById('chart-endpoints'), {
      type: 'bar',
      data: {
        labels: epData.map(e => e.path),
        datasets: [{
          label: 'Requests',
          data: epData.map(e => e.count),
          backgroundColor: BLUE,
          borderRadius: 4,
        }],
      },
      options: {
        indexAxis: 'y',
        responsive: true,
        plugins: { legend: { display: false } },
        scales: {
          x: { grid: { color: 'rgba(69,90,100,0.5)' }, beginAtZero: true, ticks: { precision: 0 } },
          y: { grid: { display: false }, ticks: { font: { family: "'JetBrains Mono', monospace", size: 10 } } },
        },
      },
    });

    // Top search terms
    const srData = data.topSearchTerms.slice(0, 12);
    const resourceColors = { characters: BLUE, monsters: RED, games: GOLD };
    charts.searches = new Chart(document.getElementById('chart-searches'), {
      type: 'bar',
      data: {
        labels: srData.map(s => s.term),
        datasets: [{
          label: 'Searches',
          data: srData.map(s => s.count),
          backgroundColor: srData.map(s => resourceColors[s.resource] ?? MUTED),
          borderRadius: 4,
        }],
      },
      options: {
        indexAxis: 'y',
        responsive: true,
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              afterLabel: ctx => `  resource: ${srData[ctx.dataIndex].resource}`,
            },
          },
        },
        scales: {
          x: { grid: { color: 'rgba(69,90,100,0.5)' }, beginAtZero: true, ticks: { precision: 0 } },
          y: { grid: { display: false } },
        },
      },
    });

    // Keyed vs anonymous
    charts.traffic = new Chart(document.getElementById('chart-traffic'), {
      type: 'doughnut',
      data: {
        labels: ['Keyed', 'Anonymous'],
        datasets: [{
          data: [data.traffic.premiumRequests, data.traffic.anonymousRequests],
          backgroundColor: [GOLD, BLUE],
          borderColor: '#37474f',
          borderWidth: 2,
        }],
      },
      options: {
        responsive: true,
        cutout: '65%',
        plugins: { legend: { position: 'bottom', labels: { padding: 12, boxWidth: 12 } } },
      },
    });

    // Tables
    fillTable('t-errors', data.topErrorPaths, 'No failed requests in range.', e => [
      cell(e.path), cell(String(e.statusCode)), cell(fmt(e.count), 'num'),
    ]);

    fillTable('t-slowest', data.slowestEndpoints, 'Not enough requests in range to rank.', e => [
      cell(e.path), cell(`${fmt(e.p95Ms)}ms`, 'num'), cell(fmt(e.count), 'num'),
    ]);

    fillTable('t-clients', data.topClients, 'No traffic in range.', c => [
      cell(c.ipHash),
      cellHtml(c.isPremium ? '<span class="pill gold">keyed</span>' : '<span class="pill">anon</span>'),
      cell(fmt(c.count), 'num'),
    ]);

    document.getElementById('last-updated').textContent =
      'Updated ' + new Date().toLocaleTimeString();
  }

  // Built as DOM rather than innerHTML: paths and hashes come from request data, and one
  // crafted path would otherwise be markup on a page that is logged into.
  function cell(text, cls) {
    const td = document.createElement('td');
    td.textContent = text;
    if (cls) td.className = cls;
    return td;
  }

  function cellHtml(html) {
    const td = document.createElement('td');
    td.innerHTML = html;   // literals only — never request data
    return td;
  }

  function fillTable(id, rows, emptyText, toCells) {
    const body = document.getElementById(id);
    body.replaceChildren();

    if (!rows || rows.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 3;
      td.className = 'empty';
      td.textContent = emptyText;
      tr.append(td);
      body.append(tr);
      return;
    }

    for (const row of rows) {
      const tr = document.createElement('tr');
      tr.append(...toCells(row));
      body.append(tr);
    }
  }

  // ── Range selection ────────────────────────────────────────────────────────
  // Relative presets stay relative: the page refreshes every two minutes, and "Last 24h" that
  // silently froze at the hour it was clicked would drift out of date while being watched.
  const ALL_TIME_FROM = '2000-01-01T00:00:00Z';
  let range = { hours: 24, from: null, to: null };

  function rangeLabel() {
    if (range.from || range.to) return 'In Range';
    if (range.hours === 'all')  return 'All Time';
    if (range.hours === 24)     return 'Last 24h';
    return `Last ${range.hours / 24}d`;
  }

  function rangeQuery() {
    if (range.from || range.to) {
      const params = new URLSearchParams();
      if (range.from) params.set('from', range.from);
      if (range.to)   params.set('to', range.to);
      return `?${params}`;
    }
    if (range.hours === 'all') return `?from=${encodeURIComponent(ALL_TIME_FROM)}`;
    const from = new Date(Date.now() - range.hours * 3600 * 1000).toISOString();
    return `?from=${encodeURIComponent(from)}`;
  }

  function selectPreset(hours) {
    range = { hours: hours === 'all' ? 'all' : Number(hours), from: null, to: null };
    document.getElementById('range-from').value = '';
    document.getElementById('range-to').value = '';
    for (const btn of document.querySelectorAll('#range-presets .range-btn')) {
      btn.setAttribute('aria-pressed', String(btn.dataset.hours === String(hours)));
    }
    loadStats();
  }

  document.getElementById('range-presets').addEventListener('click', e => {
    const btn = e.target.closest('.range-btn');
    if (btn) selectPreset(btn.dataset.hours);
  });

  document.getElementById('range-apply').addEventListener('click', () => {
    const from = document.getElementById('range-from').value;
    const to   = document.getElementById('range-to').value;
    if (!from && !to) return;

    // The date inputs give a bare day. "To" covers the whole of that day rather than stopping at
    // its first instant, which would otherwise return nothing for a same-day from/to.
    range = {
      hours: null,
      from: from ? `${from}T00:00:00Z` : null,
      to:   to   ? `${to}T23:59:59Z`   : null,
    };
    for (const btn of document.querySelectorAll('#range-presets .range-btn')) {
      btn.setAttribute('aria-pressed', 'false');
    }
    loadStats();
  });

  async function loadStats() {
    const banner = document.getElementById('error-banner');
    try {
      const res  = await fetch(`/api/stats${rangeQuery()}`);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      banner.style.display = 'none';
      renderDashboard(data);
    } catch (e) {
      banner.textContent   = `Failed to load stats: ${e.message}`;
      banner.style.display = 'block';
    }
  }

  loadStats();
  setInterval(loadStats, 2 * 60 * 1000); // refresh every 2 minutes
})();
