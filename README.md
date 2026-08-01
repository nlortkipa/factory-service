# Kartuli Cement — Factory Service Evaluation

End-to-end implementation of the Georgian technical specification:

- `backend/` — ASP.NET Core .NET 10 API with Entity Framework Core, SQL Server production configuration, SQLite development profile, signed QR sessions, direct public surveys, MFA, RBAC, audit, reports, and exports.
- `frontend/` — responsive React/Vite public survey and administration portal matching the green/navy UI concepts in the specification.
- `windows-agent/` — .NET 10 Windows Service plus interactive tray companion. The companion requests short-lived QR sessions and shows the LCD design on the Windows screen.

## Local development

Start the API, React frontend, and on-screen QR companion together:

```powershell
.\Start-FactoryService.ps1
```

The command waits for both web applications to become ready before opening the QR display. Background output is saved under `.dev/`. To stop the API and frontend processes started by this command:

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

Open `http://localhost:5173/admin`. Demo credentials are `admin` / `Admin123!`.
After the password step, read the six-digit development MFA code from the backend terminal; no SMS is sent.

### Test from a phone on the same network

The development setup listens on all network interfaces: Vite on port `5173` and
the API on port `5106`. The backend automatically puts the active private LAN
IPv4 address in each QR link (rather than `localhost`), so scanning it opens the
survey on a phone connected to the same Wi-Fi/Ethernet network. With the current
network it will be `http://192.168.100.148:5173`.

If Windows asks, allow both the frontend and backend through the **Private**
network firewall. Do not use the HTTPS development profile URL on a phone: its
local development certificate is only trusted by this PC. Re-scan the QR after a
network change; the next QR uses the new LAN address automatically.

Run the interactive Windows QR display after the backend is ready:

```powershell
dotnet run --project .\windows-agent\src\FactoryService.Companion
```

The source-local agent configuration is [`windows-agent/config.json`](./windows-agent/config.json). It is copied beside both executables during build/publish and is never written to AppData or ProgramData.

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
