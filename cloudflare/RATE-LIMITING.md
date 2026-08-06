# Rate limiting

Two layers, doing different jobs. The edge one is the one that protects costs; the app one is the
one that knows who is keyed.

## Why the app limiter alone was not enough

Railway terminates connections at its own load balancer, so `Connection.RemoteIpAddress` inside the
app is one of a small pool of internal `100.64.0.0/10` addresses — never the caller. Measured
against three months of production request logs on 2026-08-05, the ten busiest "clients" were
`100.64.0.3` through `100.64.0.9`, and the distinct-address count sat at 21–24 a day whether the day
served 56 requests or 1,730. Real visitors scale with traffic; a fixed pool of load balancers does
not.

The consequences were that no caller could be isolated (an abuser's requests scattered across ~20
buckets), legitimate callers shared buckets with abusers, and the effective anonymous ceiling was
roughly 60/min × proxy nodes × replicas — which is where the ~290/min measured in production came
from.

## What is fixed in the app

`ClientIpResolver` now resolves the caller from `CF-Connecting-IP`, but **only** on requests that
carry the shared secret the Worker injects. That condition is the whole design: Cloudflare appends
to a caller-supplied `X-Forwarded-For`, so its first entry is attacker-controlled, and Railway also
answers on its own `*.up.railway.app` hostname where `CF-Connecting-IP` can simply be typed in. A
spoofable partition key would be worse than a coarse one — it would let one caller mint unlimited
fresh windows. Without the secret, the resolver falls back to the peer address: imprecise, never in
the attacker's favour.

Rejections are also recorded now. `UseRateLimiter` short-circuits ahead of the logging middleware,
so three months of logs contain zero 429s — which was never evidence the limiter wasn't firing.

### Setup — both sides, or it silently degrades

```bash
# 1. Generate a secret
openssl rand -hex 32

# 2. Cloudflare Worker
cd cloudflare/maintenance-worker
wrangler secret put EDGE_SECRET     # paste it

# 3. Railway → Variables (same value)
Edge__Secret = <the same value>
```

If the two disagree the app stops trusting forwarded addresses and quietly goes back to limiting on
the load balancer. Nothing breaks and nothing warns, so treat rotation as a two-sided change.

**Verifying it took**, once both are deployed: hit the site through Cloudflare a few times, then
check that `/stats` shows a "Busiest Clients" list that grows with real traffic rather than sitting
at ~20 fixed hashes. Those hashes are the tell.

## What still needs doing at the edge — not applicable from this repo

The app limiter is per-process, and Railway runs several replicas, so its ceiling is still
multiplied by replica count. More importantly, a request it rejects has already cost Railway compute
and possibly a Neon query. Blocking at Cloudflare is what actually protects spend.

In the dashboard: **Security → WAF → Rate limiting rules → Create rule**

| Field | Value |
|---|---|
| Rule name | `api-anonymous` |
| If incoming requests match | `(http.request.uri.path contains "/api/")` |
| Characteristics | IP |
| Period | 1 minute |
| Requests | 120 |
| Action | Block (or Managed Challenge) |
| Duration | 1 minute |

Notes on the numbers. 120/min is deliberately looser than the app's 60 — the edge rule is the
backstop against abuse, and the app is what draws the anonymous/keyed distinction. Set the edge
below the app limit and the app's tiers stop meaning anything.

To exempt keyed callers, add to the rule expression:

```
(http.request.uri.path contains "/api/" and not any(http.request.headers["x-api-key"][*] in {"key-one" "key-two"}))
```

That places live credentials in a dashboard rule, which is a real trade — it is why this is written
as optional rather than recommended. The alternative is to leave keyed callers subject to the edge
rule too and raise its ceiling.

Free plans allow a limited number of rate-limiting rules; check the allowance on the current plan
before designing around several.

## Costs worth knowing

Images serve from R2, which has no egress fees, so image bandwidth is not the exposure. Railway
compute and Neon are — which makes the list and search endpoints the ones worth protecting. Both
are already fronted by `HybridCache`.
