FACTORY SERVICE CLIENT - WINDOWS X64
====================================

No .NET, Node.js, npm, or other development tools are required.

Requirements
------------
- 64-bit Windows 10 or Windows 11
- Internet connection
- Turmo display connected by USB (its COM port is detected automatically)

Install
-------
1. Extract this ZIP to a normal folder.
2. Double-click Install.cmd.
3. Choose Yes on the Windows administrator/UAC prompt.
4. Keep the extracted ZIP folder until installation reports success.

The installer:
- copies the self-contained client to Program Files;
- installs the FactoryServiceAgent Windows Service;
- starts the service automatically when Windows starts;
- starts the display companion whenever a user logs in;
- creates a separate configuration under each user's Local AppData folder;
- disables the official TURMO login task because both applications cannot use
  the panel's serial port at the same time.

Configuration
-------------
The companion creates this file the first time each Windows user logs in:

%LOCALAPPDATA%\Factory Service Agent\config.json

That user must fill in apiBaseUrl and operatorId. The package contains no shared
configuration file.

Uninstall
---------
Double-click Uninstall.cmd and approve the UAC prompt. The official TURMO login
task is re-enabled if it exists.
