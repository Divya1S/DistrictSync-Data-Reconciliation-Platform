# DistrictSync.Web

Operations console for DistrictSync: a React 18 + TypeScript single-page app that drives the
`DistrictSync.Api` REST endpoints (`/api/v1`) and listens to the SignalR operations hub
(`/hubs/operations`) for live updates.

## Run

The intended way is through .NET Aspire, which starts the API, worker, infrastructure and this app
together and wires the API URL into the Vite dev server (`services__api__http__0`) along with `PORT`:

```bash
cd ../DistrictSync.AppHost
dotnet run
```

Standalone, against an API already running on `http://localhost:5080`:

```bash
npm install
npm run dev          # http://localhost:5173, proxies /api and /hubs to the API
```

Other scripts:

```bash
npm run build        # tsc -b && vite build  → dist/
npm run preview      # serve the production build locally
npm run lint         # eslint (typescript-eslint, type-aware)
```

Environment: copy `.env.example` to `.env` and set `VITE_API_BASE` only when the app should call an
API origin directly instead of going through the dev-server proxy.

## Sign-in

`/login` is a development sign-in: it calls `POST /api/v1/auth/dev-token` with a tenant slug
(default `northgate`), a user id and a set of roles (`admin`, `analyst`, `approver`, `viewer`) and
stores the returned JWT in `localStorage`. The token is decoded client-side only to display the
user and roles; the API remains the authority. A `401` from any endpoint drops the session.

## Layout

```
src/
  api/          typed fetch client (ProblemDetails → ApiError) and DTO types mirroring DistrictSync.Contracts
  auth/         token store, JWT decode, useAuth, route guard
  realtime/     SignalR connection, live-update dispatch (query invalidation + toasts), activity/progress store
  components/   ui primitives (table, modal, badges, fields, toasts…) and the app shell
  features/     one folder per area: hooks.ts (react-query) + pages/dialogs
  lib/          formatting, theme, URL-synced filters, chart palette
```
