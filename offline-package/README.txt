MOSS — PRIVATE WRITING TOOLS FOR WINDOWS

Moss is a small writing helper that stays in the background. Use it from other apps to summarize, check spelling and grammar, explore better word choices, or rephrase selected text.

PRIVACY
- MossAI works on this laptop. Your selected writing is not sent to an online AI service or saved by Moss.
- Preferences and shortcuts are stored locally on this laptop.
- After installation, Moss works with Wi-Fi off.
- The local service listens only on this laptop (127.0.0.1).

INSTALL
1. Extract the complete ZIP into a folder.
2. Right-click Install-Moss.ps1 and choose Run with PowerShell.
   If Windows blocks the script, open PowerShell in this folder and run:
   powershell -ExecutionPolicy Bypass -File .\Install-Moss.ps1
3. Search for Moss in Start to open Preferences. Moss starts quietly when you sign in.

USING MOSS
- Select text in an app and right-click to see enabled Moss actions, or use the shortcut shown beside a feature in Preferences.
- To set a shortcut, click Set shortcut next to the feature, then press your key combination.
- Turn individual actions on or off in Preferences. Disabled actions do not appear in the Moss menu and their shortcuts are inactive.
- Review suggestions first. Replace only happens when you choose Replace selection; turn that option off to keep results copy-only.
- Moss checks selected text rather than monitoring every keystroke. Common spelling mistakes may show a quick fix; use Spelling & grammar for a broader review.
- Close Preferences to hide it. The background helper and small Moss action panel keep working; right-click the Moss tray icon and choose Exit Moss to stop it.

UNINSTALL
Right-click Uninstall-Moss.ps1 and choose Run with PowerShell. This removes the app, local engine, Start shortcut, and sign-in startup entry. Your preferences remain in LocalAppData\Moss.

OPEN-SOURCE NOTICES
Required open-source notices are included in Models\LICENSE.txt and Runtime\LICENSE. The bundled local model and inference runtime are distributed with their applicable terms.
