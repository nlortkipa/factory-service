# Kartuli Cement — Factory Service Evaluation

End-to-end implementation of the Georgian technical specification:

- `backend/` — ASP.NET Core .NET 10 API with Entity Framework Core, SQL Server production configuration, SQLite development profile, signed QR sessions, direct public surveys, MFA, RBAC, audit, reports, and exports.
- `frontend/` — responsive React/Vite public survey and administration portal matching the green/navy UI concepts in the specification.
- `windows-agent/` — .NET 10 Windows Service plus tray companion. The companion requests short-lived QR sessions, detects the Turmo USB panel's serial port, and sends the QR design directly to its 320×480 display.

## Local development

Start the API, React frontend, Windows service in console mode, and Turmo QR
companion together:

```powershell
.\Start-FactoryService.ps1
```

On Windows, you can also double-click `Run-Local.bat` in the repository root.

The command waits for both web applications to become ready before starting the
client. Background output is saved under `.dev/`. To stop every process started
by this command:

```powershell
.\Stop-FactoryService.ps1
```

You can also run each component separately as described below.

Start the backend (development uses a local SQLite database and creates its schema from the EF model; production uses the included SQL Server migration):

```powershell
dotnet run --project .\backend\src\FactoryService.Api
```

Start the frontend:

```powershell
cd .\frontend
npm install
npm run dev
```

Open `http://localhost:5173/admin`. Administrator login uses the six-digit SMS
code sent through SMS Office; no username or password is required. Copy `.env.example` to
`.env.local`, set `SMS_KEY`, `SMS_SENDER`, `AUTH_SIGNING_KEY`, `ADMIN_PHONE`, and
then start the application with `Start-FactoryService.ps1`.
Set `VITE_ADMIN_BYPASS=true` only when an explicit local authentication bypass is
needed.

For the Turmo companion in development, set `FACTORY_SERVICE_DEV_OPERATOR_ID` in
`.env.local` to an operator ID that exists in the local database. The development
launcher keeps the saved production client configuration untouched and overrides
the companion process to use `http://localhost:5000`.

The Vercel deployment is available at
`https://factory-service-app.vercel.app/admin`. Vercel does not provide an
official .NET runtime, so the deployment uses `api/index.js`. Connect a Neon
Postgres database and expose its connection string as `DATABASE_URL`; the Node
adapter creates its plant/operator tables automatically. Without a database,
local development uses resettable in-memory records, while Vercel returns a 503
for directory operations instead of pretending that an unsaved record persisted.

### Test from a phone on the same network

The development setup listens on all network interfaces: Vite on port `5173` and
the API on port `5000`. The backend automatically puts the active private LAN
IPv4 address in each QR link (rather than `localhost`), so scanning it opens the
survey on a phone connected to the same Wi-Fi/Ethernet network. With the current
network it will be `http://192.168.100.148:5173`.

If Windows asks, allow both the frontend and backend through the **Private**
network firewall. Do not use the HTTPS development profile URL on a phone: its
local development certificate is only trusted by this PC. Re-scan the QR after a
network change; the next QR uses the new LAN address automatically.

Run the Turmo QR companion after the backend is ready:

```powershell
dotnet run --project .\windows-agent\src\FactoryService.Companion
```

On first launch, the companion creates a separate configuration for the current
Windows user at `%LOCALAPPDATA%\Factory Service Agent\config.json`. It contains
`apiBaseUrl` (initially `http://localhost:5000`) and a blank `operatorId`.
[`windows-agent/config.example.json`](./windows-agent/config.example.json)
documents the format. Create an operator in the administration portal and copy
its generated Operator ID into that user's configuration before starting the
companion.

## Production database

`appsettings.json` uses SQL Server. Supply a protected `ConnectionStrings__DefaultConnection` and a long random `FactoryService__QrSigningKey` through the deployment environment. The development profile intentionally overrides the provider with SQLite.

Create or update migrations with EF Core, then apply them during controlled deployment:

```powershell
dotnet ef database update --project .\backend\src\FactoryService.Api
```

## Build

```powershell
dotnet build .\backend\FactoryService.slnx
dotnet build .\windows-agent\FactoryService.sln
cd .\frontend
npm run build
```

## Build and install the Windows client

Build the dependency-free Windows x64 ZIP with:

```powershell
.\windows-agent\scripts\package-client.ps1
```

On Windows, double-click `Create-Production-Client-Zip.bat` to run the same
Release packaging flow and write the ZIP under `windows-agent\dist`.

Send `windows-agent\dist\FactoryServiceClient-win-x64.zip`. The recipient only
needs to extract it and double-click `Install.cmd`; the package contains the
self-contained .NET runtime. Each user then fills in the API URL and Operator ID
in `%LOCALAPPDATA%\Factory Service Agent\config.json`.

The installer creates a delayed-auto Windows Service with restart recovery and
registers the companion for every user logon. It disables (but does not delete)
the vendor `TURMO_ScheduleTask`, because `TURMO.exe` and the factory companion
cannot both own the panel's serial port. `Uninstall.cmd` reverses those startup
changes.
