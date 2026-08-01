# Factory Service Windows Agent

This is a normal Go proof of concept. It has no installer, startup registry
entry, PowerShell scripts, or background deployment behavior.

While it is running, it listens for the current Windows session being locked
and unlocked. When you sign back in after the screen locks, it opens a
full-screen QR code that scans to `https://www.youtube.com/` and writes the
event to a local log.

## Run it

From this folder:

```powershell
go run .
```

Leave the terminal open. Lock the PC with `Win + L`, then sign in again. The
YouTube QR code will open. Press `Alt + F4` to close that QR window.

Press `Ctrl + C` in the terminal to stop the agent.

The event log is at `%LOCALAPPDATA%\FactoryService\logs\agent.log`.

## Current scope

This is only a login/unlock detection proof of concept. It does not install
anything, add a startup entry, or run after Windows boots on its own. Run
`go run .` whenever you want it active.
