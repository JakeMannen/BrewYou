# BrewYou

[![License: MIT](https://img.shields.io/badge/License-MIT-amber.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![SvelteKit 2](https://img.shields.io/badge/SvelteKit-2.0-ff3e00.svg)](https://kit.svelte.dev/)
[![Svelte 5](<https://img.shields.io/badge/Svelte-5.0_(Runes)-ff3e00.svg>)](https://svelte.dev/)
[![.NET Aspire](https://img.shields.io/badge/.NET_Aspire-13.5-512bd4.svg)](https://learn.microsoft.com/dotnet/aspire/)
[![PostgreSQL 18](https://img.shields.io/badge/PostgreSQL-18.6-336791.svg)](https://www.postgresql.org/)

**BrewYou** is a 100% free and open-source fullstack craft brewing platform for homebrewers and small breweries — covering recipe formulation, multi-rig brew day execution, IoT fermentation telemetry, and taproom dispensing. All features are universally accessible with no paywalls, tiers, or subscriptions.

## Core Capabilities

- **Dashboard & Cellar Operations**: Live overview of active batches, fermenters, serving taps, schedule milestones, and quick gravity/temperature logging.
- **Multi-Rig Setups & Collaboration**: Calibrated equipment profiles (BIAB, All-in-One, 2-vessel, 3-vessel) with shared brewery RBAC (`Owner`/`Co-Owner`, `Brewer`, `Viewer`).
- **Recipe Formulator & Labels**: Real-time OG, FG, ABV, IBU, and SRM calculations, BJCP style targets, BeerXML 1.0 & BeerJSON import/export, printable brew sheets, and custom bottle/tap artwork.
- **Live Brew Day & HUD**: 5-stage lifecycle (`Mash -> Boil -> Ferment -> Condition -> Package`), full-screen Brew Station HUD, offline/retroactive batch logging, and QR cellar labels.
- **Equipment, Kegs & Digital Taps**: Vessel and keg fill tracking plus standalone kiosk, rendered image, and JSON manifest displays (`/tap/{token}`).
- **BrewLink & IoT Telemetry**: ESP32 Wi-Fi/BLE gateway pairing and hardware control, iSpindle/Tilt/HTTP webhooks, Mosquitto MQTT, and real-time Server-Sent Events (SSE).
- **Pantry, Calculators & Portability**: Ingredient stock alerts, water chemistry/carbonation/hydrometer/refractometer calculators, encrypted `.brewyou.json` backup/restore, and 5 locales (`en`, `sv`, `de`, `fr`, `es`).

## Tech Stack

| Layer | Technologies |
| :--- | :--- |
| **Frontend** | SvelteKit 2, Svelte 5 (Runes), TypeScript, Tailwind CSS v4, Vite |
| **Backend API** | .NET 10, ASP.NET Core Minimal APIs, FluentValidation, Scalar OpenAPI |
| **Data & IoT** | Entity Framework Core 10, PostgreSQL 18.6, Eclipse Mosquitto MQTT |
| **Orchestration** | .NET Aspire 13.5 (`src/BrewYou.AppHost`), Docker Compose |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20 or 22 LTS](https://nodejs.org/) & `npm`
- [Docker](https://www.docker.com/products/docker-desktop/) (for PostgreSQL, Mosquitto, or full container stack)

### Option A: .NET Aspire (Recommended for Development)

```bash
cd src/BrewYou.Web && npm install && cd ../..
dotnet run --project src/BrewYou.AppHost
```

### Option B: Docker Compose

```bash
# Local development build (loopback-bound)
docker compose up --build

# Optional profiles: mqtt, pgadmin, tools
docker compose --profile mqtt --profile tools up --build

# Server / Portainer deployment (pre-built GHCR images + MQTT)
JWT_SECRET="your-32+-char-secret" docker compose -f docker-compose.server.yml up -d
```

### Option C: Manual Local Run

```bash
# 1. Start PostgreSQL
docker run --name brewyou-postgres -e POSTGRES_DB=brewyou-db -e POSTGRES_USER=brewyou -e POSTGRES_PASSWORD=brewyou_dev_password -p 5432:5432 -d postgres:18.6-alpine

# 2. Start the API (auto-migrates & seeds on startup)
cd src/BrewYou.ApiService && dotnet run

# 3. Start the frontend
cd src/BrewYou.Web && npm install && npm run dev
```

### Local Service URLs

| Service | URL / Port |
| :--- | :--- |
| **Web App** | `http://localhost:3000` |
| **Backend API** | `http://localhost:5000` |
| **Scalar OpenAPI UI** | `http://localhost:5000/scalar/v1` |
| **OpenAPI JSON** | `http://localhost:5000/openapi/v1.json` |
| **PostgreSQL** | `127.0.0.1:5432` (`brewyou-db`) |
| **Mosquitto MQTT** | `127.0.0.1:1883` *(when enabled)* |
| **pgAdmin** | `http://localhost:5050` *(when enabled)* |

## Configuration

| Scope | Variable | Purpose / Default |
| :--- | :--- | :--- |
| **Backend** | `ConnectionStrings__brewyou-db` | PostgreSQL connection string |
| **Backend** | `Jwt__SecretKey` | Signing key (>= 32 bytes; required outside `Development`) |
| **Backend** | `Cors__AllowedOrigins__0` | Allowed frontend origin (`http://localhost:3000`) |
| **Backend** | `Authentication__Google__ClientId` | Optional Google OAuth client ID |
| **Backend** | `NetworkSecurity__AllowPrivateNetworks` | Allow LAN/private MQTT brokers (`false` default; `true` in Docker Compose via `ALLOW_PRIVATE_NETWORKS`) |
| **Backend** | `DataProtection__CertificatePath` | Optional PKCS#12 certificate path to encrypt ASP.NET Core Data Protection keys at rest |
| **Frontend** | `PUBLIC_API_URL` | Browser API base URL (`http://localhost:5000`) |
| **Frontend** | `PUBLIC_APP_URL` | Public app URL for QR cellar labels (`http://localhost:3000`) |
| **Frontend** | `services__apiservice__http__0` | Internal SSR API URL (`http://apiservice:5000` in Docker) |

For Docker Compose overrides, see the defaults in [docker-compose.yml](docker-compose.yml) (`DB_NAME`, `DB_USER`, `DB_PASSWORD`, `JWT_SECRET`).

## API & Type Synchronization

Interactive endpoint docs are served in development at `http://localhost:5000/scalar/v1`. To regenerate frontend TypeScript API types from the running backend:

```bash
cd src/BrewYou.Web
npm run api:sync
```

## Testing & Linting

```bash
# Backend
dotnet format --verify-no-changes
dotnet test

# Frontend (from src/BrewYou.Web)
npm run lint
npm run check
npm run test:unit -- --run
npm run test:e2e
```

## Project Structure

```text
BrewYou/
├── src/
│   ├── BrewYou.AppHost/          # .NET Aspire orchestration
│   ├── BrewYou.ApiService/       # ASP.NET Core Minimal API, EF Core, services, MQTT/SSE
│   ├── BrewYou.ServiceDefaults/  # Shared OpenTelemetry & health checks
│   └── BrewYou.Web/              # SvelteKit 2 + Svelte 5 frontend
├── tests/
│   └── BrewYou.ApiService.Tests/ # Backend unit & integration tests
├── docker/                       # Mosquitto container config
├── docker-compose.yml            # Local container stack
├── AGENTS.md                     # AI agent rules & architectural invariants
└── CONTRIBUTING.md               # Contribution & PR workflow
```

## Contributing & License

See [CONTRIBUTING.md](CONTRIBUTING.md) for workflow and code standards, and [AGENTS.md](AGENTS.md) for architectural invariants. Licensed under the [MIT License](LICENSE).
