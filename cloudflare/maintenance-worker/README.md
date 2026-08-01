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

`www.moogleapi.com` already resolves and is proxied by Cloudflare, and Cloudflare's
certificate covers `*.moogleapi.com`. What's missing is that **Railway has no custom
domain for `www`**, so it answers with its own fallback `404` and a
`*.up.railway.app` certificate.

Pick one:

1. **Redirect (recommended).** Cloudflare → Rules → Redirect Rules. If hostname
   equals `www.moogleapi.com`, dynamic 301 to
   `concat("https://moogleapi.com", http.request.uri.path)`. Never touches Railway,
   keeps the apex canonical, and costs nothing.
2. **Serve both.** Add `www.moogleapi.com` as a second custom domain in Railway. The
   site then answers on both hostnames, which splits your canonical URL and is worth
   avoiding unless you have a reason.
