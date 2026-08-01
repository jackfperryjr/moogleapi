/**
 * moogleAPI maintenance worker.
 *
 * Sits on the Cloudflare edge in front of Railway. Normal traffic passes straight
 * through untouched; only when the origin is unreachable or returns a gateway-class
 * error does this serve a maintenance response instead of the browser's raw
 * ERR_QUIC_PROTOCOL_ERROR / Cloudflare 5xx interstitial.
 *
 * The page has to live out here rather than in the app because the app is the thing
 * that's restarting — anything served by ASP.NET is unavailable for exactly the
 * window we're trying to cover.
 */

// Origin failures worth masking. 502/503/504 come from Railway's edge while a
// container is cycling; 52x are Cloudflare's own "couldn't reach the origin" codes.
// Everything else — including 500 — passes through, because a genuine application
// error is not maintenance and hiding it behind a friendly page would bury real bugs.
const FAILURE_STATUSES = new Set([502, 503, 504, 521, 522, 523, 524, 525, 526]);

// A Railway handover is seconds long, so one retry converts most of the window into
// a slightly slow request rather than a visible error.
const RETRY_DELAY_MS = 1500;

export default {
  async fetch(request) {
    let response = await tryOrigin(request);

    // Only replay methods that are safe to run twice. A retried POST could double
    // a daily-guess submission, which is worse than showing the maintenance page.
    const isReplayable = request.method === 'GET' || request.method === 'HEAD';

    if (response === null || FAILURE_STATUSES.has(response.status)) {
      if (isReplayable) {
        await sleep(RETRY_DELAY_MS);
        const second = await tryOrigin(request);
        if (second !== null && !FAILURE_STATUSES.has(second.status)) return second;
        response = second;
      }
      return maintenanceResponse(request);
    }

    return response;
  },
};

async function tryOrigin(request) {
  try {
    return await fetch(request);
  } catch {
    // Connection refused / reset / TLS failure while the container is down.
    return null;
  }
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * API clients get JSON, browsers get the page. moogleAPI is consumed by scripts far
 * more than by people, and handing an HTTP client a lump of HTML it will try to
 * parse as JSON turns a clear outage into a confusing deserialization error.
 */
function maintenanceResponse(request) {
  const url = new URL(request.url);
  const wantsHtml =
    !url.pathname.startsWith('/api/') &&
    (request.headers.get('Accept') || '').includes('text/html');

  // 503 + Retry-After is the honest status: temporary, try again shortly. It also
  // keeps crawlers from indexing the maintenance page in place of the real site.
  const headers = {
    'Retry-After': '30',
    'Cache-Control': 'no-store, no-cache, must-revalidate',
    'X-Moogle-Maintenance': '1',
  };

  if (wantsHtml) {
    return new Response(MAINTENANCE_HTML, {
      status: 503,
      headers: { ...headers, 'Content-Type': 'text/html; charset=utf-8' },
    });
  }

  return new Response(
    JSON.stringify(
      {
        status: 503,
        title: 'Service Unavailable',
        detail:
          'moogleAPI is restarting and will be back shortly. Retry in about 30 seconds.',
      },
      null,
      2,
    ),
    {
      status: 503,
      // ProblemDetails, matching the app's own error shape (Errors.UseProblemDetails).
      headers: { ...headers, 'Content-Type': 'application/problem+json; charset=utf-8' },
    },
  );
}

const MAINTENANCE_HTML = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>Back shortly — moogleAPI</title>
<style>
  :root {
    color-scheme: light dark;
    --bg: #f4f1e8;
    --card: #fffdf7;
    --ink: #2b2417;
    --muted: #6b6252;
    --edge: #ddd5c2;
    --accent: #b0407a;
  }
  @media (prefers-color-scheme: dark) {
    :root {
      --bg: #14121a;
      --card: #1d1b25;
      --ink: #ece8f5;
      --muted: #9d97ae;
      --edge: #2f2c3b;
      --accent: #f19ec8;
    }
  }
  * { box-sizing: border-box; }
  body {
    margin: 0;
    min-height: 100vh;
    display: grid;
    place-items: center;
    padding: 1.5rem;
    background: var(--bg);
    color: var(--ink);
    font: 16px/1.6 ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif;
  }
  .card {
    width: min(30rem, 100%);
    background: var(--card);
    border: 1px solid var(--edge);
    border-radius: 14px;
    padding: 2.5rem 2rem;
    text-align: center;
    box-shadow: 0 12px 32px rgb(0 0 0 / 0.09);
  }
  .pom {
    width: 68px;
    height: 68px;
    margin: 0 auto 1.25rem;
    animation: bob 2.6s ease-in-out infinite;
  }
  @keyframes bob {
    0%, 100% { transform: translateY(0); }
    50%      { transform: translateY(-7px); }
  }
  @media (prefers-reduced-motion: reduce) {
    .pom { animation: none; }
  }
  h1 { margin: 0 0 .6rem; font-size: 1.4rem; letter-spacing: -0.01em; }
  p  { margin: 0 0 1.5rem; color: var(--muted); }
  .kupo { color: var(--accent); font-weight: 600; font-style: italic; }
  .foot {
    margin: 0;
    padding-top: 1.25rem;
    border-top: 1px solid var(--edge);
    font-size: .8rem;
    color: var(--muted);
  }
  code {
    font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
    font-size: .78rem;
    background: color-mix(in srgb, var(--muted) 14%, transparent);
    padding: .15em .45em;
    border-radius: 4px;
  }
</style>
</head>
<body>
  <main class="card">
    <!-- A moogle's pom-pom. Inline because a Worker has no static assets to serve. -->
    <svg class="pom" viewBox="0 0 64 64" role="img" aria-label="Moogle pom-pom">
      <line x1="32" y1="34" x2="32" y2="58" stroke="var(--edge)" stroke-width="4" stroke-linecap="round"/>
      <circle cx="32" cy="22" r="15" fill="var(--accent)"/>
      <circle cx="26" cy="17" r="4.5" fill="#fff" opacity=".45"/>
    </svg>
    <h1>Back in a moment</h1>
    <p>
      moogleAPI is restarting after a deploy.<br>
      <span class="kupo">Kupo!</span>
    </p>
    <p class="foot">
      This page refreshes itself. API clients get a <code>503</code> with
      <code>Retry-After: 30</code>.
    </p>
  </main>
  <script>
    // Poll rather than reload on a timer: a blind refresh loop during a longer
    // outage just repaints this same page over and over.
    (function () {
      var delay = 5000;
      (function check() {
        setTimeout(function () {
          fetch('/health', { cache: 'no-store' })
            .then(function (r) { if (r.ok) location.reload(); else grow(); })
            .catch(grow);
        }, delay);
        function grow() {
          delay = Math.min(delay * 1.5, 30000);
          check();
        }
      })();
    })();
  </script>
</body>
</html>`;
