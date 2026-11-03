# idk

idk is a Windows desktop app. Select text in any app and ask a question in place.

idk Pro is $3 a month for this computer. Asking stays on while the subscription is active. Copying the app or the license file does not unlock another computer. Model access is built in. The key is sealed into the build from `.env` and is not saved in settings.

## Install

The installer is `dist-setup/idk-Setup-0.1.12.exe`. It installs idk under `Program Files\idk`, shows the license, and can add a Start Menu shortcut, a desktop shortcut, and a sign-in startup entry. The same file is at `website/download/idk-Setup.exe`.

Build it with:

```powershell
powershell -File scripts/package-windows.ps1
```

Notes for each version are generated from `brain/log.json` into `website/releases.html`. The live site is `https://idk-trymoonrises-projects.vercel.app`.

The setup includes the desktop app, so a separate .NET install is not required. Uninstall removes the program files. Settings, chat history, and the license file stay in your idk data folder.

## License keys

Copy `.env.example` to `.env` on the machine that sells licenses. Never commit `.env`. `STRIPE_SECRET_KEY` checks the $3 monthly subscription for this computer. The installer does not include `.env`.

## License

Use is governed by the license shown in setup (`installer/license.rtf`). One subscription is for one computer while it is active. The software is provided as is, and liability is limited as that agreement states.
