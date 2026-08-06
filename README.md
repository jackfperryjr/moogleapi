# <img src="src\MoogleAPI.Web\wwwroot\moogle.png" alt="moogleAPI" width="30"> moogleAPI

> *"I'm a SOLDIER, not a database." — Cloud Strife, probably*

A free, open REST API for Final Fantasy data — characters, monsters, and games across the entire mainline series. Built with modern .NET 10 and designed to stay fast and cheap to run.

<p align="center">
  <img src="https://github.com/jackfperryjr/moogleapi/actions/workflows/checks.yml/badge.svg" alt="Checks" height="20">
  <img src="https://img.shields.io/github/sponsors/jackfperryjr?style=flat-square&color=ea4aaa" alt="GitHub Sponsors">
  <img src="https://img.shields.io/badge/.NET-10-512bd4?style=flat-square&logo=dotnet" alt=".NET 10">
  <img src="https://img.shields.io/badge/FastEndpoints-black?style=flat-square&logo=fastendpoints" alt="FastEndpoints">
  <img src="https://img.shields.io/badge/PostgreSQL-316192?style=flat-square&logo=postgresql&logoColor=white" />
  <img src="https://img.shields.io/github/license/jackfperryjr/moogleapi?style=flat-square&color=black" alt="License">
</p>

---

## ✨ Features

- **Characters, Monsters & Games** across all 16 mainline Final Fantasy titles
- **Full-text search** on names and descriptions (case-insensitive, PostgreSQL `ILike`)
- **Pagination** on all list endpoints
- **HybridCache** — stampede-proof L1/L2 caching out of the box
- **Rate limiting** — 60 req/min anonymous, 600 req/min with an API key
- **Interactive docs** at `/scalar/v1` (far nicer than Swagger UI)
- **Hand-curated** — every row reviewed and edited through the dashboard, not bulk-imported

---

## 🚀 Quick Start

```http
GET https://moogleapi.com/api/characters/search?query=Aerith
GET https://moogleapi.com/api/monsters?gameId=7
GET https://moogleapi.com/api/games
```

No API key required. Pass an issued `X-Api-Key: your-key` to get 10× the rate limit.

---

## 📖 Endpoints

### Characters

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/api/characters` | List all characters (`gameId`, `page`, `pageSize`) |
| `GET` | `/api/characters/{id}` | Get a character by ID |
| `GET` | `/api/characters/search` | Search by name/description (`query`, `gameId`) |

### Monsters

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/api/monsters` | List all monsters (`gameId`, `category`, `minPopularity`, `requireImage`, `page`, `pageSize`) |
| `GET` | `/api/monsters/{id}` | Get a monster by ID — art, location, HP/MP/level/EXP/gil, elemental weaknesses |
| `GET` | `/api/monsters/search` | Search by name/description (**`query` required**, `gameId`, `category`) |

`category` is `Boss` or `Enemy`. `gameId` is the numeric id from `/api/games` (1 = Final Fantasy … 16 = Final Fantasy XVI).

Search needs a term — it looks *within* a game rather than listing one:

```http
GET /api/monsters/search?query=bomb&gameId=4   # Bomb, Bomb King, Gray Bomb, Melt Bomb
GET /api/monsters?gameId=4                     # every Final Fantasy IV monster instead
```

### Games

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/api/games` | List all games (`page`, `pageSize`) |
| `GET` | `/api/games/{id}` | Get a game by ID (includes character + monster counts) |

Both return two pictures: `imageUrl` is the full logo — the wide lockup with the title text —
and `thumbnailUrl` is the square emblem, the artwork alone. They are separate crops rather than
two sizes of one image, so pick by shape, not by resolution.

### Arena

Powers [Battle Square](https://moogleapi.com/battle-square) — one character against eight consecutive waves of their own game's monsters.

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/api/arena/roster` | Playable characters that can enter (`gameId`) |
| `GET` | `/api/arena/run` | A day's eight waves (`characterId`, `level`, `date`) |

Levels are **positions in a game's own stat distribution**, not absolute numbers — the series has no shared scale, so a Final Fantasy Goblin has 8 HP where a Final Fantasy XV Bomb has 5,600. Level 40 places a character above the same share of their game's monsters everywhere. `recommendedLevel` is solved against the day's actual waves rather than looked up.

Full interactive docs at [`/scalar/v1`](https://moogleapi.com/scalar/v1).

---

## 🛠 Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | [FastEndpoints v8](https://fast-endpoints.com) — REPR pattern, one folder per operation |
| Language | C# 14 / .NET 10 |
| Database | EF Core 10 + PostgreSQL ([Neon](https://neon.tech) serverless) |
| Validation | FluentValidation (built into FastEndpoints) |
| Caching | `HybridCache` — L1 in-process + optional L2 Redis |
| Docs | [Scalar](https://scalar.com) — replaces Swagger UI |
| Rate Limiting | `PartitionedRateLimiter` (native .NET 10) |
| Artwork pipeline | GitHub Actions → Gemini → Cloudflare R2 |

### Project Structure

```
MoogleApi.sln
├── src/
│   └── MoogleAPI.Web/
│       ├── Features/
│       │   ├── Arena/
│       │   │   ├── GetRoster/
│       │   │   └── GetRun/       ← Endpoint + Models + Validator
│       │   ├── Battle/
│       │   │   ├── GetStarters/
│       │   │   └── GetRun/
│       │   ├── Characters/
│       │   │   ├── Get/          ← Endpoint + Models
│       │   │   ├── GetAll/
│       │   │   └── Search/       ← Endpoint + Models + Validator
│       │   ├── Games/
│       │   │   ├── Get/
│       │   │   └── GetAll/
│       │   └── Monsters/
│       │       ├── Get/
│       │       ├── GetAll/
│       │       └── Search/
│       ├── Infrastructure/
│       │   ├── Arena/            ← Level curve, stat scale, waves, handicaps
│       │   ├── Battle/           ← Shared damage model + monster pool
│       │   ├── Data/             ← AppDbContext
│       │   ├── Models/           ← Game, Character, Monster
│       │   └── RateLimiting/
│       ├── wwwroot/              ← Landing page + /games hub + four games
│       └── Program.cs
├── scripts/
│   └── MoogleAPI.Scraper/        ← Artwork tool, runs in GitHub Actions
└── tests/
    └── MoogleAPI.Tests/
```
---

## 🤖 Data & Artwork

The catalogue is curated by hand. Rows are added and edited through the private dashboard, one at
a time, with a person deciding what belongs — there is no unattended job that rewrites the data on
a timer, and no bulk import behind the current contents.

What still runs on request is the artwork tool, dispatched manually from the **Artwork** workflow.
Its stages are `images` (copy artwork into our own bucket and repoint the row), `audit` (classify
what each image actually is), `generate` (replace it with an illustration in one house style) and
`unpromote` (withdraw generated art and restore the original). `generate` costs money per image, so
it takes an explicit ceiling with `--max` and never runs as part of an unnamed "all stages" pass.

Artwork is served from Cloudflare R2 at `images.moogleapi.com`. Keys derive from the row id, which
is what makes a move between domains a database pass rather than a re-upload.

---

## ⚖️ Rate Limits

| Tier | Limit | How |
|------|-------|-----|
| Anonymous | 60 req / min | Per IP, no setup needed |
| Premium | 600 req / min | Pass an issued `X-Api-Key: your-key` header |

Responses over the limit return `429 Too Many Requests`.

Premium keys have to be issued — an unrecognized key isn't rejected, it just falls back to the
anonymous limit, so the API stays usable if you send a stale one. Self-hosting? Set the
allowlist with `ApiKeys__Keys__0`, `ApiKeys__Keys__1`, … With none set, everything is anonymous.

---

## 📜 Disclaimer

MoogleAPI is a fan project and is not affiliated with or endorsed by Square Enix. All Final Fantasy names, characters, and related marks are trademarks of Square Enix Co., Ltd.

The catalogue was originally seeded from the community-maintained [Final Fantasy Wiki](https://finalfantasy.fandom.com) and is maintained by hand from there on. That attribution stays while any of it remains: the wiki's text is CC BY-SA, which requires credit regardless of how much editing has happened since.

---

<p align="center">Made with ♥ and too many Phoenix Downs <img src="src\MoogleAPI.Web\wwwroot\moogle.png" alt="moogleAPI" width="12"> <em>kupo!</em></p>
