# Moss

**Private writing tools for Windows.** Moss runs quietly in the background and lets you work with selected text from other apps: check spelling and grammar, explore better word choices, summarize, and rephrase. The Preferences window is for setup; it does not need to stay open while Moss is working.

## Privacy, in plain terms

Moss processes the text you choose on this laptop using MossAI. It does not send your writing to an AI service or save the text. Your feature choices and shortcuts stay in local Windows settings. Install the full package once, then the writing tools continue to work with Wi-Fi off. Moss does not need an account or an online connection to process text.

Moss works on text you select when you use an action; it does not continuously read everything you type. Enabled actions appear in Moss's selection menu, and you can assign keyboard shortcuts from Preferences. The app can start quietly when you sign in and stays available from the tray.

## Download and install

The complete offline Windows bundle is included in [`offline-package`](offline-package/). It contains the Moss app, MossAI model, local inference runtime, license notices, and install/uninstall scripts. You can also download the ready-to-extract ZIP from [GitHub Releases](https://github.com/businessadikamath-hue/moss-offline-assistant/releases/latest).

1. Download and extract `Moss-Offline-Windows.zip`, or use the included `offline-package` folder.
2. Run `Install-Moss.ps1` from that folder. If Windows blocks the script, open PowerShell there and run `powershell -ExecutionPolicy Bypass -File .\Install-Moss.ps1`.
3. Search for **Moss** in Start to open Preferences. It starts quietly at sign-in.

To uninstall, run `Uninstall-Moss.ps1` from the same folder.

## Build the app

The native app is a WPF project. Build `Moss.csproj` on Windows with the .NET 8 SDK and Windows desktop workload. The source project does not download the model automatically; the full bundled runtime and model are in `offline-package/`.

## Repository map

- `App.xaml.cs`, `MainWindow.*`, `QuickActionsWindow.*`, and `Moss.csproj` — Windows app source.
- `Assets/` — Moss app icon.
- `offline-package/` — full, ready-to-install offline app bundle.
- `website/` — Vercel download site and license information.

## Third-party notices

The bundle includes license notices in `offline-package/Models/LICENSE.txt` and `offline-package/Runtime/`. MossAI is the product name shown for the local writing engine. The third-party notices remain with the bundle. The Moss application source has no separate project license grant in this repository.
