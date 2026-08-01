# Factory Service Windows Agent

This is a normal Go proof of concept. It has no installer, startup registry
entry, PowerShell scripts, or background deployment behavior.

While it is running, it listens for the current Windows session being locked
and unlocked. When you sign back in after the screen locks, it shows a visible
Windows dialog and writes the event to a local log.

## Run it

From this folder:

```powershell
go run .
```

Leave the terminal open. Lock the PC with `Win + L`, then sign in again. You
should see **“Factory Service Windows Agent: Welcome back”**.

Press `Ctrl + C` in the terminal to stop the program.

The event log is at `%LOCALAPPDATA%\FactoryService\logs\agent.log`.

## Current scope

This is only a login/unlock detection proof of concept. It does not yet start
automatically when Windows first boots. Automatic start requires an installed
executable and a startup registration, which is intentionally excluded for now.

Next, add an ESP32 serial client. The backend will give the agent a
short-lived signed QR URL/token and visible label; the agent will send that
payload to the ESP32 over USB serial. The ESP32 firmware will generate the QR
and draw it on the LCD.
