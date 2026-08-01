# Maintenance worker

Serves a maintenance page when the origin is unreachable, instead of leaving visitors
with `ERR_QUIC_PROTOCOL_ERROR` or a raw Cloudflare 5xx.

## Why this exists out here

A merge to `main` triggers a Railway redeploy, and the container restarts. Confirmed
on 2026-07-31: PR #32 merged at 21:55:54 and the origin's `last-modified` came back as
21:55:53, with the site briefly unreachable in between.

The maintenance page cannot live in the app, because the app is what's restarting. It
has to be served by something that stays up — the Cloudflare edge.

## Fix the cause first

The Worker is a safety net, not the fix. Set Railway's **Healthcheck Path** to
`/health` (Service → Settings → Deploy). Railway then keeps the old container serving
until the new one answers, turning a restart into a handover and shrinking the outage
window to roughly nothing. The `/health` endpoint ships in `Program.cs`.

Do this even if you never deploy the Worker.

## Deploy

```sh
npm install -g wrangler   # once
wrangler login            # once
wrangler deploy
```

Routes are declared in `wrangler.toml`. Deploying claims `moogleapi.com/*`, so the
Worker is in the path of **every** request to the site.

## Before you deploy, know the tradeoff

- **Free-plan Workers are capped at 100,000 requests/day.** Past the cap, requests on
  the route start failing — which would turn a rare blip into a real outage. Check
  current traffic against that ceiling before putting this in front of a public API.
- Everything is pass-through, and only 502/503/504/52x are intercepted. A genuine
  application `500` is deliberately **not** masked, so real bugs still surface.
- `POST`/`PUT`/`DELETE` are never retried, only `GET`/`HEAD`, so a retry can't
  double-submit a daily guess.

Cloudflare's built-in **Custom Pages** (Rules → Custom Pages) covers some of the same
ground with no Worker and no request cap, but which error classes are customizable
depends on your plan — worth checking your dashboard before committing to the Worker.

## Testing it

You cannot easily fake an origin outage from outside. To verify the page renders, run
it locally and force the failure path:

```sh
wrangler dev
```

then temporarily add `return maintenanceResponse(request);` at the top of `fetch`.

## The `www` hostname

`www.moogleapi.com` 301s to the apex at the Cloudflare edge. **Live since 2026-08-01.**

Railway has no custom domain for `www` and deliberately never gets one — without the
redirect it answers with its own fallback `404` and a `*.up.railway.app` certificate,
which is the bug this fixes. The redirect resolves at the edge, so `www` requests
never reach the origin at all.

The rule lives in Cloudflare → Rules → Redirect Rules:

| Setting | Value |
| --- | --- |
| Filter expression | `(http.host eq "www.moogleapi.com")` |
| Target URL | Dynamic — `concat("https://moogleapi.com", http.request.uri.path)` |
| Status code | 301 |
| Preserve query string | on |

**Preserve query string is not optional.** With it off the path survives but the query
does not, so `?pageSize=5` is silently dropped and a shared link lands on unfiltered
page 1 with nothing to explain why. It fails quietly, which is worse than failing.

Verify from a shell rather than a browser — a browser will have cached the old `404`:

```sh
curl -sS -o /dev/null -D - --max-redirs 0 'https://www.moogleapi.com/api/monsters?pageSize=5'
```

Expect `301` with `location: https://moogleapi.com/api/monsters?pageSize=5` and **no**
`x-railway-*` headers. Those headers disappearing is the real signal: it means the edge
answered and the origin was never touched. If you see `x-railway-fallback: true`, the
rule is not matching — check that it is enabled and on the right zone before touching
the target.

The rejected alternative was adding `www.moogleapi.com` as a second custom domain in
Railway. It works, but it splits the canonical URL across two hostnames and puts the
origin back in the path for no gain.
