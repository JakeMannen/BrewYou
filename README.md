# BrewYou — Craft Brewing Management Platform

[![License: MIT](https://img.shields.io/badge/License-MIT-amber.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![SvelteKit 2](https://img.shields.io/badge/SvelteKit-2.0-ff3e00.svg)](https://kit.svelte.dev/)
[![Svelte 5](<https://img.shields.io/badge/Svelte-5.0_(Runes)-ff3e00.svg>)](https://svelte.dev/)
[![Tailwind CSS v4](https://img.shields.io/badge/Tailwind_CSS-v4.0-38bdf8.svg)](https://tailwindcss.com/)
[![.NET Aspire](https://img.shields.io/badge/.NET_Aspire-13.5-512bd4.svg)](https://learn.microsoft.com/dotnet/aspire/)
[![PostgreSQL 18](https://img.shields.io/badge/PostgreSQL-18.6-336791.svg)](https://www.postgresql.org/)
[![MQTT](https://img.shields.io/badge/MQTT-Mosquitto-660066.svg)](https://mosquitto.org/)

**BrewYou** is a modern, 100% free and open-source fullstack craft brewing platform built for homebrewers and craft breweries. It provides an end-to-end brewing experience — from multi-rig brewery configuration and recipe formulation to live brew day execution, IoT fermentation telemetry monitoring, and inventory tracking.

BrewYou is strictly open-source craft brewing software: **all features are universally accessible to every user with no paywalls, tiers, or subscriptions.**

---

## Table of Contents

1. [Features](#features)
   - [Interactive Dashboard & Cellar Operations](#interactive-dashboard--cellar-operations)
   - [Multi-Rig Brewery Setup & Loss Profiles](#multi-rig-brewery-setup--loss-profiles)
   - [Recipe Management & Recipe Formulator](#recipe-management--recipe-formulator)
   - [Batch Tracking & Live Brew Day Execution](#batch-tracking--live-brew-day-execution)
   - [Equipment & Vessel Inventory](#equipment--vessel-inventory)
   - [Ingredient Stock & Inventory](#ingredient-stock--inventory)
   - [IoT Telemetry & Real-Time Monitoring](#iot-telemetry--real-time-monitoring)
   - [Brewing Calculators Suite](#brewing-calculators-suite)
   - [Security & Authentication](#security--authentication)
   - [Internationalization (i18n) & Personalization](#internationalization-i18n--personalization)
2. [Technology Stack](#technology-stack)
3. [Prerequisites](#prerequisites)
4. [Getting Started & How to Run](#getting-started--how-to-run)
   - [Option A: With .NET Aspire (Recommended for Development)](#option-a-with-net-aspire-recommended-for-development)
   - [Option B: With Docker Compose](#option-b-with-docker-compose)
   - [Option C: Manual / Standalone Development](#option-c-manual--standalone-development)
5. [Configuration & Environment Variables](#configuration--environment-variables)
6. [API Documentation](#api-documentation)
7. [Testing & Quality Assurance](#testing--quality-assurance)
8. [Project Structure](#project-structure)
9. [Development & Agent Conventions](#development--agent-conventions)
10. [License](#license)

---

## Features

### Interactive Dashboard & Cellar Operations

The Interactive Dashboard serves as the central command center for your brewery. It provides a real-time overview of your entire operation, aggregating key performance indicators such as active batches, currently fermenting vessels, upcoming schedule milestones, and equipment readiness in one place. Fermenting batches are displayed as active cellar cards showing their current stage, gravity, temperature, and days in vessel. Live telemetry streams from connected IoT sensors feed directly into the dashboard widgets, and modal shortcuts allow quick stage advancements or gravity and temperature logging without navigating away.

#### How to Use

1. **View Brewery KPIs**: Navigate to the **Dashboard** (`/`) to check active batch counts, fermenter utilization, and system status at a glance.
2. **Monitor Active Fermentations**: Inspect cellar cards to see fermentation stages, wort temperature, specific gravity, and days in vessel.
3. **Log Measurements Quickly**: Click **Quick Log** (or the hydrometer icon) on any cellar card to record a new gravity or temperature reading.
4. **Advance Batch Stages**: Use the stage quick-action button on a cellar card to advance a batch (e.g., from _Fermenting_ to _Conditioning_) with a single click.
5. **Follow the Operations Schedule**: Review the calendar feed for upcoming brew days, dry hopping windows, and packaging milestones.

### Multi-Rig Brewery Setup & Loss Profiles

BrewYou allows brewers to configure multiple distinct brewing rigs (such as a 5-liter stovetop BIAB pot, a 20-liter electric all-in-one system, or a traditional 3-vessel HERMS/RIMS setup). Each setup maintains its own physical loss profile—calibrating grain absorption, boil-off rate, kettle dead space, trub losses, and wort contraction during cooling. When you switch active rigs, recipes and brew sessions automatically recalculate water requirements, strike temperatures, and gravity targets to match the selected equipment profile.

#### How to Use

1. **Access Brewery Settings**: Navigate to **Settings** (`/settings`) and select **Brewery Profiles**.
2. **Configure a Brewing Rig**: Click **Add Brewery Setup** (or edit an existing profile) and enter your kettle type, boil capacity, and calibrated physical parameters (boil-off L/hr, grain absorption L/kg, kettle trub loss, and cooling shrinkage %).
3. **Switch the Active Rig**: Select a rig and set it as **Active** using the brewery switcher in the application header or settings view.
4. **Apply to Recipes**: When designing a recipe or scheduling a brew, your active rig's parameters automatically calculate strike water volumes, sparge requirements, and target boil volumes.

### Recipe Management & Recipe Formulator

The Recipe Formulator is an interactive brewing formulation tool that calculates Original Gravity (OG), Final Gravity (FG), ABV, Bitterness (Tinseth IBU), Color (SRM/EBC), and BU:GU balance in real time. It features a dynamic craft beer glass visualization that reflects your wort's authentic SRM color and style-adaptive foam head. Built-in BJCP style guidelines provide visual gauges comparing your recipe against target ranges. You can manage base and specialty malts, physical ingredient forms (Pellet, Leaf, Plug for hops; Dry, Liquid, Slant, Culture for yeasts with selectable units in g, pkg, or ml), timed hop additions (Mash, First Wort, Boil, Aroma, Whirlpool, Dry Hop), yeasts, and water salts. It includes dedicated staged fermentation schedules (_Primary_, _Secondary_, _Ramp / Diacetyl Rest_, _Free Rise_, _Cold Crash_, _Conditioning_) with pre-configured style presets (_Standard Ale_, _Lager with D-Rest_, _Saison Free-Rise_, _NEIPA Juicy_), with full bidirectional BeerXML 1.0 and BeerJSON import and export support preserving ingredient forms and precise yeast amounts.

#### How to Use

1. **Browse Recipes**: Open **Recipes** (`/recipes`) to view your saved recipes, explore community formulations, or search by beer style.
2. **Create or Edit a Recipe**: Click **New Recipe** to open the formulator cockpit.
3. **Target a Style**: Pick a BJCP style from the dropdown to display reference parameter gauges for OG, FG, IBU, SRM, and ABV.
4. **Build the Grain Bill & Hop Schedule**: Add fermentables, hops (with addition timings and temperatures), yeasts, and water salts. Watch the glass graphic and recipe metrics update in real time.
5. **Configure Mash & Fermentation Profiles**: Set up single-infusion or multi-step mash schedules, and configure staged fermentation temperature targets and durations. BrewYou computes strike water temperatures, infusion volumes, and schedule milestones automatically.
6. **Import & Export**:
   - Click **Import** to load existing `.xml` (BeerXML) or `.json` (BeerJSON) recipe files directly into your catalog, preserving fermentation profiles and temperatures. If a recipe with the same name already exists in your library, BrewYou validates for name conflicts upfront before creating any missing ingredients, popping up a rename modal that lets you easily rename the imported recipe or cancel without side effects.
   - Click **Export** on any recipe to download standard BeerXML or BeerJSON files with complete fermentation profiles for backup or sharing.
7. **Start a Brew**: Click **Brew This Recipe** to turn the formulation into an active brew day batch.

### Batch Tracking & Live Brew Day Execution

Batch Tracking guides you through a structured 10-stage brewing lifecycle: _Planning_, _Preparation_, _Mashing_, _Lautering_, _Boiling_, _Cooling_, _Fermenting_, _Conditioning_, _Bottling / Kegging_, and _Completed_. The live Brew Day Cockpit provides interactive timers for mash steps and boil additions, real-time temperature tracking, and a pre-boil correction calculator that automatically adjusts boil times or water additions if gravity or volume drifts from targets. During fermentation, batches inherit the recipe's staged fermentation profile for checklist tracking, while interactive attenuation curves dynamically plot live density and temperature readings against active step temperature targets.

#### How to Use

1. **Create a Batch**: Navigate to **Batches** (`/batches`), click **New Batch**, select a recipe (which automatically previews and inherits the fermentation profile and pitch temperature), and assign available equipment.
2. **Run Brew Day**:
   - Open the batch to launch the **Brew Day Cockpit** (`/batches/[id]`).
   - Follow the stage pipeline, starting timers for mash rests and boil additions.
   - Record pre-boil gravity and volume; if discrepancies occur, use the dynamic boil calculator to adjust boil duration or water additions.
3. **Track Fermentation & Staged Profile**:
   - Once wort is transferred and chilled, advance the batch to **Fermenting**.
   - Assign physical hydrometers or IoT sensors to the fermenting vessel for automatic telemetry, or manually record gravity and temperature samples.
   - Follow the fermentation schedule checklist, marking steps (e.g. Primary, Diacetyl Rest, Cold Crash) as completed to track progress and automatically update target temperature reference lines on the Attenuation Chart.
4. **Package & Complete**:
   - Transition through **Conditioning** to **Bottling / Kegging**.
   - Log packaged bottle counts, keg volumes, and priming sugar amounts to calculate final yield efficiency, then mark the batch **Completed**.

### Equipment & Vessel Inventory

The Equipment Inventory provides digital twin management for your brewery's hardware, organized into Boilers (HLTs, electric all-in-one kettles), Fermenters (carboys, conical fermenters, unitanks), Kegs (Cornelius, minikegs), and Sensors. Equipment cards display interactive liquid-level animations based on volume capacity, operational status, and assigned batch locks. For sensor-equipped vessels, cards display real-time telemetry badges (temperature, gravity, pressure, battery, angle) and include an expandable telemetry inspector for viewing structured attributes and raw JSON payloads.

#### How to Use

1. **View Equipment**: Go to **Inventory ➔ Equipment** (`/inventory/equipment`) to see all registered hardware grouped by category.
2. **Add or Duplicate Vessels**:
   - Click **Add Equipment** to enter vessel specifications (category, name, total capacity, dead space, and sensor assignments).
   - Click **Duplicate** on any equipment card to quickly clone vessel profiles (e.g. for sets of identical kegs or fermenters).
3. **Check Operational Status & Batch Locks**: See which vessels are currently assigned to active batches (preventing double-booking) and view liquid fill levels.
4. **Inspect Telemetry**: For vessels linked to IoT sensors, view live metric badges and click the **Attributes Inspector** to inspect raw JSON payloads, battery levels, signal RSSI, and sensor tilt angles.

### Ingredient Stock & Inventory

Ingredient Inventory tracks on-hand brewery pantry supplies across fermentables, hops, yeasts, and miscellaneous brewing additives. It keeps track of stock quantities, physical forms (such as hop pellets/leaf/plug and yeast dry/liquid/slant/culture), lot numbers, alpha acid percentages, and expiration dates. Threshold-based low-stock warnings alert you when ingredients are running low, and inventory levels can be filtered directly within the recipe designer to ensure you formulate recipes with on-hand ingredients. Custom or imported ingredients can be deleted safely; if an ingredient is currently in use by any recipe, a warning modal lists the affected recipes and upon confirmation automatically removes the ingredient while recalculating recipe metrics (ABV, IBU, SRM, OG, FG) to keep formulations mathematically sound. Default system catalog items are immutable and cannot be deleted.

#### How to Use

1. **Manage Pantry Stock**: Navigate to **Ingredients** (`/ingredients`) and switch between the **Fermentables**, **Hops**, **Yeasts**, and **Additives** tabs.
2. **Add New Inventory**: Click **Add Ingredient** to log received stock, setting the name, quantity (kg/g or lb/oz), purchase date, and ingredient-specific properties (such as hop alpha acids or grain potential).
3. **Delete Custom Ingredients**: Click the **Delete** icon on any custom or imported ingredient. If the ingredient is in use by recipes, review the warning modal and confirm to remove it from those recipes and recalculate their brewing metrics. Default catalog ingredients do not show a delete button and cannot be deleted.
4. **Configure Low-Stock Alerts**: Set a minimum threshold quantity on any ingredient to trigger visual badges and warnings when stock drops below safe limits.
5. **Recipe Integration**: When designing recipes or preparing a batch, filter ingredients by "In Stock" to build brews exclusively from available inventory.

### IoT Telemetry & Real-Time Monitoring

BrewYou features an extensible IoT telemetry architecture that connects physical brewing sensors directly to your digital brewery. It natively supports iSpindle, Tilt hydrometers, generic HTTP webhooks, scheduled HTTP polling, and Mosquitto MQTT broker connections. The system normalizes physical metrics (temperature in °C/°F, gravity in SG/Plato/Brix, pressure in bar/psi, tilt angle, battery voltage, and RSSI) while storing arbitrary sensor attributes in an open JSON payload store. Telemetry streams in real time via Server-Sent Events (SSE) directly to equipment cards and batch fermentation graphs without page reloads.

#### How to Use

1. **Get Device Credentials**: In **Inventory ➔ Equipment**, open your equipment or sensor settings to view its unique device token and ingestion endpoints.
2. **Set Up Ingestion**:
   - **HTTP Push (iSpindle / Tilt / Webhooks)**: Configure your device to post JSON to `http://<host>:5000/api/v1/telemetry/equipment` with header `X-BrewYou-Device-Token: <token>`, or use the route `POST /api/v1/telemetry/equipment/<token>`.
   - **MQTT Connection**: Navigate to **Settings ➔ Integrations / MQTT**, provide your MQTT broker host (e.g., `localhost` or container name) and port (`1883`), and configure your IoT device to publish to the auto-generated topic (e.g., `brewyou/equipment/<equipment-id>/telemetry`).
3. **Verify Connection**: Check the live MQTT and device connection status badges in user settings and equipment cards.
4. **Observe Real-Time Updates**: Watch live temperature, gravity, and pressure readings update automatically on your dashboard, equipment cards, and batch charts without manual page refreshes.

### Brewing Calculators Suite

The Brewing Calculators Suite provides dedicated, standalone utilities for quick calculations and recipe verification. It features calculators for Water Chemistry & Mash pH, Strike Water Volume & Temperature, ABV & Attenuation, Hydrometer Temperature Correction, Refractometer Brix Correction (with custom Wort Correction Factors), Dilution & Boil-Off adjustments, and Carbonation & Priming Sugar calculations. All calculators support instant bidirectional conversion between Metric and Imperial units.

#### How to Use

1. **Open Calculators**: Navigate to **Calculators** (`/calculations`) from the main navigation bar.
2. **Select a Calculator**: Choose the appropriate tool tab for your brewing task:
   - **Strike Water**: Enter grain weight, target mash temperature, grain temperature, and water-to-grain ratio to get required strike water volume and heating temperature.
   - **Water Chemistry & Mash pH**: Input source water profile, target profile, and grain bill to calculate mineral salt additions (gypsum, calcium chloride, Epsom salt) and acid additions.
   - **Hydrometer Correction**: Enter observed specific gravity and sample temperature to correct for hydrometer calibration offsets.
   - **Refractometer Correction**: Input Original Gravity and current Brix reading (with Wort Correction Factor) to calculate accurate Final Gravity and ABV during or after fermentation.
   - **Carbonation & Priming**: Select target CO₂ volumes and beer temperature to calculate priming sugar weights (table sugar, dextrose, DME) or regulator PSI for kegging.
3. **Toggle Units**: Switch between Metric and Imperial units at any time with instantaneous conversion of all inputs and results.

### Security & Authentication

BrewYou provides secure identity and session management built on ASP.NET Core security best practices. It supports standard email/password authentication with JWT access and refresh token lifecycles, as well as one-click Google OAuth authentication. Protected user actions use non-disruptive authentication modals, allowing unauthenticated or expired sessions to sign in or register without losing unsaved recipe formulations or current page contexts. The backend enforces strict Content Security Policies (CSP), HTTP Strict Transport Security (HSTS), rate limiting, and CORS isolation.

#### How to Use

1. **Create an Account or Sign In**: Click **Sign In** or **Register** in the navigation header, or use **Continue with Google** for single-click OAuth.
2. **In-Flow Authentication**: If you attempt to save a recipe, start a batch, or modify settings while signed out, the authentication modal appears automatically. Complete login to continue your action without losing draft data or navigating away.
3. **Manage Security**: Under **Settings ➔ Security**, update your password, review active token sessions, or link external authentication providers.

### Internationalization (i18n) & Personalization

BrewYou offers comprehensive internationalization with 100% key parity between English (`en`) and Swedish (`sv`) across all UI views, notifications, and error envelopes. It also supports seamless switching between regional unit presets (**European Metric**, **US Craft / Imperial**, and **UK Traditional**) and custom unit scales across all calculations, batch logs, and equipment volumes, along with a persistent theme system supporting **Modern Amber (Dark)**, **Pilsner Clean (Light)**, **Botanical Moss (Green)**, and **System OS Match**.

#### How to Use

1. **First-Login Setup**: When creating an account or signing in for the first time, an onboarding popup prompts you to name your brewery and select your measurement standard preset (_European Metric_, _US Craft_, or _UK Traditional_).
2. **Switch Language**: Click the language selector (EN / SV) in the header or in **Settings ➔ Preferences** to toggle between English and Swedish instantly.
3. **Switch Unit System & Presets**: Apply pre-configured regional presets (_European Metric_, _US Craft_, _UK Traditional_) or fine-tune individual units in **Settings ➔ Units**; all recipe formulator values, batch logs, and calculator displays convert dynamically.
4. **Change Theme**: Select your preferred appearance in **Settings ➔ Appearance** between _Modern Amber (Dark)_, _Pilsner Clean (Light)_, _Botanical Moss (Green)_, or _System Match_.

---

## Technology Stack

```
                               ┌────────────────────────┐
                               │  SvelteKit 2 Frontend  │
                               │   (Svelte 5 + Tailwind)│
                               └───────────┬────────────┘
                                           │
                        HTTP / JSON (REST) │ Server-Sent Events / Telemetry
                                           ▼
                               ┌────────────────────────┐
                               │  ASP.NET Core Web API  │
                               │   (.NET 10 Minimal)    │
                               └─────┬────────────┬─────┘
                                     │            │
            PostgreSQL Provider (EF) │            │ MQTT Protocol
                                     ▼            ▼
                           ┌────────────┐   ┌────────────┐
                           │ PostgreSQL │   │ Mosquitto  │
                           │ 18.6 DB    │   │ MQTT Broker│
                           └────────────┘   └────────────┘
```

| Layer               | Technologies                                                                             |
| :------------------ | :--------------------------------------------------------------------------------------- |
| **Frontend**        | SvelteKit 2, Svelte 5 (Runes), TypeScript, Vite, Tailwind CSS v4, Lucide Svelte, Bits UI |
| **Backend API**     | .NET 10.0, ASP.NET Core Minimal APIs, C# 14, FluentValidation, Scalar OpenAPI            |
| **Data Layer**      | Entity Framework Core 10, Npgsql PostgreSQL Provider                                     |
| **Database**        | PostgreSQL 18.6 (Alpine)                                                                 |
| **Orchestration**   | .NET Aspire 13.5 AppHost, Docker Compose                                                 |
| **Messaging & IoT** | Eclipse Mosquitto MQTT v2                                                                |
| **Observability**   | OpenTelemetry, ASP.NET Core Health Checks, Scalar Interactive API UI                     |

---

## Prerequisites

Before running BrewYou locally, ensure you have the following installed:

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Node.js (v20.x or v22.x LTS)](https://nodejs.org/) & `npm`
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) or Docker Engine (for PostgreSQL and container orchestration)
- _Optional_: [.NET Aspire Workload / CLI](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling) (`dotnet workload install aspire`)

---

## Getting Started & How to Run

### Option A: With .NET Aspire (Recommended for Development)

Aspire automatically orchestrates the PostgreSQL database, Mosquitto MQTT broker, .NET API service, and SvelteKit frontend in a single development session.

1. **Clone the repository**:

   ```bash
   git clone https://github.com/your-org/BrewYou.git
   cd BrewYou
   ```

2. **Install frontend dependencies**:

   ```bash
   cd src/BrewYou.Web
   npm install
   cd ../..
   ```

3. **Start the Aspire AppHost**:

   ```bash
   dotnet run --project src/BrewYou.AppHost
   ```

4. **Access the application**:
   - **Aspire Dashboard**: URL displayed in the console (e.g., `https://localhost:17000`)
   - **BrewYou Web Application**: [http://localhost:3000](http://localhost:3000)
   - **Interactive API Documentation (Scalar)**: [http://localhost:5000/scalar/v1](http://localhost:5000/scalar/v1)
   - **pgAdmin**: Direct link accessible from the Aspire Dashboard (`postgres-pgadmin`)
   - **Mosquitto MQTT Broker**: `tcp://localhost:1883` (persisted via `brewyou_mqttdata` volume)

---

### Option B: With Docker Compose

You can spin up the full containerized stack using Docker Compose:

1. **Build and start the services**:

   ```bash
   docker compose up --build
   ```

2. **Start with the optional MQTT broker and/or pgAdmin**:

   ```bash
   # Start with MQTT broker
   docker compose --profile mqtt up --build

   # Start with pgAdmin web dashboard
   docker compose --profile pgadmin up --build

   # Start with all optional tools
   docker compose --profile tools --profile mqtt up --build
   ```

3. **Access the application**:
   - **Web Application**: [http://localhost:3000](http://localhost:3000)
   - **Backend API**: [http://localhost:5000](http://localhost:5000)
   - **Database**: PostgreSQL exposed on `127.0.0.1:5432`
   - **pgAdmin** _(if profile enabled)_: [http://localhost:5050](http://localhost:5050)
   - **MQTT Broker** _(if profile enabled)_: Port `127.0.0.1:1883`

4. **Shut down the stack**:
   ```bash
   docker compose down
   # Or remove volumes as well:
   docker compose down -v
   ```

---

### Option C: Manual / Standalone Development

If you prefer to run services individually without Aspire or full Docker:

1. **Start PostgreSQL**:

   ```bash
   docker run --name brewyou-postgres -e POSTGRES_DB=brewyou-db -e POSTGRES_USER=brewyou -e POSTGRES_PASSWORD=brewyou_dev_password -p 5432:5432 -d postgres:18.6-alpine
   ```

2. **Run the Backend API**:

   ```bash
   cd src/BrewYou.ApiService
   dotnet run
   ```

   _The API will start on port 5000, automatically migrate, and seed reference ingredients._

3. **Run the SvelteKit Frontend**:
   ```bash
   cd src/BrewYou.Web
   npm install
   npm run dev
   ```
   _The frontend will run on [http://localhost:3000](http://localhost:3000) and proxy requests to the API._

---

## Configuration & Environment Variables

### Backend (`src/BrewYou.ApiService`)

Configured via `appsettings.json`, `appsettings.Development.json`, or environment variables:

| Variable                        | Description                                                    | Default / Development Value                                                  |
| :------------------------------ | :------------------------------------------------------------- | :--------------------------------------------------------------------------- |
| `ASPNETCORE_ENVIRONMENT`        | Hosting environment (`Development` / `Production`)             | `Development`                                                                |
| `ASPNETCORE_HTTP_PORTS`         | Listening HTTP port                                            | `5000`                                                                       |
| `ConnectionStrings__brewyou-db` | Npgsql PostgreSQL connection string                            | `Host=localhost;Port=5432;Database=brewyou-db;Username=brewyou;Password=...` |
| `Jwt__SecretKey`                | Cryptographic secret key for signing JWT tokens (min 32 chars) | Configured in dev secrets                                                    |
| `Jwt__Issuer`                   | Token issuer identifier                                        | `BrewYou`                                                                    |
| `Jwt__Audience`                 | Token audience identifier                                      | `BrewYouApp`                                                                 |
| `GoogleAuth__ClientId`          | Google OAuth client ID (optional)                              | `""`                                                                         |
| `Cors__AllowedOrigins__0`       | Allowed CORS origins for the frontend client                   | `http://localhost:3000`                                                      |

### Frontend (`src/BrewYou.Web`)

Configured via `.env` or container environment variables:

| Variable                        | Description                                       | Default Value            |
| :------------------------------ | :------------------------------------------------ | :----------------------- |
| `PUBLIC_API_URL`                | Base URL used by browser clients to query the API | `http://localhost:5000`  |
| `services__apiservice__http__0` | Internal network URL used by SvelteKit SSR server | `http://apiservice:5000` |
| `PORT`                          | Listening port for the SvelteKit node server      | `3000`                   |
| `HOST`                          | Interface binding for SvelteKit server            | `0.0.0.0`                |

---

## API Documentation

BrewYou features an integrated **Scalar** OpenAPI explorer available during development:

- **Interactive Scalar UI**: [http://localhost:5000/scalar/v1](http://localhost:5000/scalar/v1)
- **Raw OpenAPI JSON Spec**: [http://localhost:5000/openapi/v1.json](http://localhost:5000/openapi/v1.json)

### Generating Frontend API Types

To keep frontend TypeScript interfaces synchronized with backend DTOs:

```bash
cd src/BrewYou.Web
npm run api:sync
```

This regenerates `src/lib/types/api.generated.ts` directly from the running backend OpenAPI contract.

---

## Testing & Quality Assurance

BrewYou maintains comprehensive test coverage across both frontend and backend layers:

### Backend Tests

```bash
# Run all .NET unit and integration tests
dotnet test
```

### Frontend Tests

```bash
cd src/BrewYou.Web

# Run Vitest unit tests
npm run test:unit

# Run full Vitest suite once
npm run test:unit -- --run

# Run unit tests with V8 code coverage report and quality gates
npm run test:coverage

# Run Playwright End-to-End (E2E) tests
npm run test:e2e

# Run all frontend tests (unit + e2e)
npm test
```

### Linting & Formatting

```bash
# Backend code format check
dotnet format --verify-no-changes

# Frontend Prettier and ESLint verification
cd src/BrewYou.Web
npm run lint

# Auto-format frontend files
npm run format

# Svelte TypeScript type check
npm run check
```

---

## Project Structure

```
BrewYou/
├── .agents/                      # Specialized agent definitions, rules, and skills
│   ├── rules/                    # Architecture, security, QA, and git workflow rules
│   └── skills/                   # Workspace automation skills
├── .editorconfig                 # Strict formatting and style enforcement rules
├── AGENTS.md                     # Central instructions and invariants for AI agents
├── docker/                       # Supporting container configurations (e.g. Mosquitto)
├── docker-compose.yml            # Multi-container local stack definition
├── src/
│   ├── BrewYou.AppHost/          # .NET Aspire 13.5 AppHost orchestration project
│   ├── BrewYou.ApiService/       # ASP.NET Core Minimal API backend & domain logic
│   │   ├── Auth/                 # JWT & Google OAuth authentication services
│   │   ├── Common/               # Uniform API envelopes & pagination models
│   │   ├── Data/                 # EF Core DbContext, entities, and DB seed catalog
│   │   ├── Endpoints/            # Route groups (Auth, Batch, Equipment, Recipe, etc.)
│   │   ├── Middleware/           # Security headers, rate limiters, global exception handler
│   │   ├── Services/             # Domain services (Batch, Recipe, Telemetry, etc.)
│   │   └── Validation/           # FluentValidation request validators
│   ├── BrewYou.ServiceDefaults/  # Shared Aspire service defaults, metrics, and health checks
│   └── BrewYou.Web/              # SvelteKit 2 + Svelte 5 frontend application
│       └── src/
│           ├── lib/
│           │   ├── api/          # Strongly-typed API client wrapper
│           │   ├── calculators/  # Pure brewing math (Tinseth IBU, SRM, strike water, etc.)
│           │   ├── components/   # Modular Svelte 5 UI components (runes-based)
│           │   ├── exporters/    # BeerXML & BeerJSON export engines
│           │   ├── i18n/         # Internationalization dictionary (en.json, sv.json)
│           │   ├── parsers/      # BeerXML & BeerJSON import parsers
│           │   ├── stores/       # Reactive Svelte stores (auth, brewery, theme, settings)
│           │   └── types/        # API DTO contracts and domain types
│           └── routes/           # Application pages (dashboard, batches, recipes, inventory)
└── tests/
    └── BrewYou.ApiService.Tests/ # Backend unit and integration test suite
```

---

## Development & Agent Conventions

This repository adheres to strict architectural and engineering standards detailed in [AGENTS.md](AGENTS.md):

1. **Zero Paywalls or Gating**: BrewYou is permanently 100% free and open software. No paid or subscription tiers.
2. **Tier Separation**: Frontend handles presentation and client UI state; backend enforces domain rules, transactions, and data access.
3. **Strict Formatting Compliance**: Zero warnings or errors on `dotnet format --verify-no-changes` and `npm run lint`.
4. **Mandatory Test Coverage**: All new features and endpoints must include automated tests.
5. **Full i18n Key Parity**: User-visible strings must be localized using `t(...)` with identical keys in both `en.json` and `sv.json`.
6. **Documentation Synchronization**: This [README.md](README.md) must be updated whenever new features, endpoints, or setup instructions are introduced.

---

## License

This project is licensed under the [MIT License](LICENSE) — free and open for personal and commercial craft brewing use.

We welcome community contributions! Please review our [Contributing Guidelines](CONTRIBUTING.md) for details on development workflows, architectural invariants, code quality standards, and inbound licensing terms.
