# idk

idk is a Windows desktop app. Select text in any app and ask a question in place.

A license is $5.99 once for this computer. Copying the app or the license file does not unlock another computer. You also bring your own OpenRouter API key and pay that provider for model usage.

## Install

The installer is `dist-setup/idk-Setup-0.1.0.exe`. It installs idk under `Program Files\idk`, shows the license, and can add a Start Menu shortcut, a desktop shortcut, and a sign-in startup entry. The same file is at `website/download/idk-Setup.exe`.

Build it with:

```powershell
powershell -File scripts/package-windows.ps1
```

The setup includes the desktop app, so a separate .NET install is not required. Uninstall removes the program files. Settings, chat history, and the license file stay in your idk data folder.

## License keys

Copy `.env.example` to `.env` on the machine that sells licenses. Never commit `.env`. `STRIPE_SECRET_KEY` signs the one-time license for this computer. The installer does not include `.env`.

## License

Use is governed by the license shown in setup (`installer/license.rtf`). One paid license is for one computer. The software is provided as is, and liability is limited as that agreement states.
