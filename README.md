# <img src="src\MoogleAPI.Web\wwwroot\moogle.png" alt="moogleAPI" width="30"> moogleAPI

> *"I'm a SOLDIER, not a database." — Cloud Strife, probably*

A free, open REST API for Final Fantasy data — characters, monsters, and games across the entire mainline series. Built with modern .NET 10 and designed to stay fast and cheap to run.

---

## ✨ Features

- **Characters, Monsters & Games** across all 16 mainline Final Fantasy titles
- **Full-text search** on names and descriptions (case-insensitive, PostgreSQL `ILike`)
- **Pagination** on all list endpoints
- **HybridCache** — stampede-proof L1/L2 caching out of the box
- **Rate limiting** — 60 req/min anonymous, 600 req/min with an API key
- **Interactive docs** at `/scalar/v1` (far nicer than Swagger UI)
- **Auto-updating** — a GitHub Action scrapes the Final Fantasy Wiki every Sunday

---

## 🚀 Quick Start

```http
GET https://api.moogleapi.com/api/characters/search?query=Aerith
GET https://api.moogleapi.com/api/monsters?gameId=7
GET https://api.moogleapi.com/api/games
```

No API key required. Pass `X-Api-Key: your-key` to get 10× the rate limit.

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
| `GET` | `/api/monsters` | List all monsters (`gameId`, `category`, `page`, `pageSize`) |
| `GET` | `/api/monsters/{id}` | Get a monster by ID |
| `GET` | `/api/monsters/search` | Search by name/description (`query`, `gameId`, `category`) |

### Games

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/api/games` | List all games (`page`, `pageSize`) |
| `GET` | `/api/games/{id}` | Get a game by ID (includes character + monster counts) |

Full interactive docs at [`/scalar/v1`](https://api.moogleapi.com/scalar/v1).

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
| Data pipeline | GitHub Actions scraper → Final Fantasy Wiki |

### Project Structure

```
MoogleApi.sln
├── src/
│   └── MoogleAPI.Web/
│       ├── Features/
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
│       │   ├── Data/             ← AppDbContext
│       │   ├── Models/           ← Game, Character, Monster
│       │   └── RateLimiting/
│       ├── wwwroot/              ← Landing page
│       └── Program.cs
├── scripts/
│   └── MoogleAPI.Scraper/        ← Console app, runs in GitHub Actions
└── tests/
    └── MoogleAPI.Tests/
```

---

## 🏃 Running Locally

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A PostgreSQL database ([Neon free tier](https://neon.tech) works great)
- `dotnet-ef` CLI: `dotnet tool install --global dotnet-ef`

### Setup

```bash
# 1. Clone
git clone https://github.com/jackfperryjr/moogleapi.git
cd moogleapi

# 2. Set your connection string (user secrets keeps it out of git)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=your-host;Database=neondb;Username=user;Password=pass;SSL Mode=Require;Trust Server Certificate=true" \
  --project src/MoogleAPI.Web

# 3. Create the schema
dotnet ef migrations add InitialCreate --project src/MoogleAPI.Web --startup-project src/MoogleAPI.Web
dotnet ef database update --project src/MoogleAPI.Web --startup-project src/MoogleAPI.Web

# 4. Seed data
CONNECTION_STRING="your-connection-string" dotnet run --project scripts/MoogleAPI.Scraper

# 5. Run the API
dotnet run --project src/MoogleAPI.Web
```

The API starts at `https://localhost:5001`. Landing page at `/`, docs at `/scalar/v1`.

---

## 🤖 Data Pipeline

A GitHub Action runs every Sunday at 2 AM UTC and scrapes the [Final Fantasy Wiki](https://finalfantasy.fandom.com) via the MediaWiki API. It upserts characters and monsters per game — no duplicates, no full reloads.

To trigger it manually: **Actions → Scrape Final Fantasy Data → Run workflow**.

To run the scraper locally:

```bash
# Windows (PowerShell)
$env:CONNECTION_STRING="Host=..."
dotnet run --project scripts/MoogleAPI.Scraper

# macOS / Linux
CONNECTION_STRING="Host=..." dotnet run --project scripts/MoogleAPI.Scraper
```

---

## ⚖️ Rate Limits

| Tier | Limit | How |
|------|-------|-----|
| Anonymous | 60 req / min | Per IP, no setup needed |
| Premium | 600 req / min | Pass `X-Api-Key: your-key` header |

Responses over the limit return `429 Too Many Requests`.

---

## 📜 Disclaimer

MoogleAPI is a fan project and is not affiliated with or endorsed by Square Enix. All Final Fantasy names, characters, and related marks are trademarks of Square Enix Co., Ltd. Data is sourced from the community-maintained [Final Fantasy Wiki](https://finalfantasy.fandom.com).

---

<p align="center">Made with ♥ and too many Phoenix Downs <img src="src\MoogleAPI.Web\wwwroot\moogle.png" alt="moogleAPI" width="12"> <em>kupo!</em></p>
