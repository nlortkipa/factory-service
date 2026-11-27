# Kartuli Cement — Factory Service Evaluation

End-to-end implementation of the Georgian technical specification:

- `backend/` — ASP.NET Core .NET 10 API with Entity Framework Core, SQL Server production configuration, SQLite development profile, signed QR sessions, direct public surveys, MFA, RBAC, audit, reports, and exports.
- `frontend/` — responsive React/Vite public survey and administration portal matching the green/navy UI concepts in the specification.
- `windows-agent/` — .NET 10 Windows Service plus tray companion. The companion requests short-lived QR sessions, detects the Turmo USB panel's serial port, and sends the QR design directly to its 320×480 display.

System administrators can change the rating cooldown in **პარამეტრები → შეფასების სიხშირე**.
Enter a whole number from 0 to 3650 days (default: 7); 0 disables the waiting period.
The saved setting applies immediately to the last rating from the same browser/device,
across all plants and operators, and is checked when opening and submitting a survey.
Production stores it in the database; the Node adapter without a database uses memory.
The overview supports combined date, plant, and operator filters for all three services.
QR codes expire after five minutes. The Windows companion checks every five seconds,
keeps unused codes unchanged, and replaces a code after a successful rating or expiry.
Deploy the API/frontend and update installed Windows companions to apply both parts.

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
`https://factory-service.vercel.app/admin`. Vercel does not provide an
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

For development only, the companion can still create a default configuration at
`%LOCALAPPDATA%\Factory Service Agent\config.json`. Production users do not edit
that file: the administration portal generates a personalized launcher that
writes the backend URL and Operator ID into the current Windows user's profile.

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

## Build and distribute the Windows client

Build the self-contained Windows x64 launcher and downloadable payload with:

```powershell
.\windows-agent\scripts\package-client.ps1
```

The command writes `FactoryService.Setup.exe` and
`FactoryServiceClient-win-x64.zip` to both `windows-agent\dist` and
`frontend\public\downloads`. Deploy both web assets with the portal.

In the portal, open **Operators** and click **Download EXE** on the intended
operator. The browser attaches the current backend URL, payload URL, Operator ID,
operator name, and plant name to a copy of the launcher. Send that one EXE to the
recipient. When they run it, the launcher:

1. shows the assigned operator and plant;
2. installs or updates the Windows service and companion if needed, with
   Windows administrator approval for machine-wide changes;
3. creates branded desktop and Start menu shortcuts for all Windows users;
4. saves the assignment to `%LOCALAPPDATA%\Factory Service Agent\config.json`
   for the Windows user who opened the launcher.

Once the machine-wide client is ready, assigning an operator to another
Windows user requires no elevation. The shortcuts open the client window.
Administrators can deploy [the taskbar layout](windows-agent/taskbar-layout.xml)
through Windows device policy to pin the client automatically on managed PCs.

The same launcher also offers **Uninstall**, which removes the current user's
configuration and the machine-wide client. No CMD or PowerShell script is part
of the recipient workflow.
