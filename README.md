# KeyTranslate

A small Windows app for translating selected text.

## Use

1. Run `KeyTranslate.exe`.
2. Choose the source and target languages, then click **Save settings**.
3. Select text in any app and press `F8`.

Automatic translation is off by default. Toggle it with `F7`, the button in the app, or the tray menu. Set the delay from 1 to 30 seconds in the app.

## Build

Install the .NET 10 SDK, open PowerShell in the project folder, and run:

```powershell
dotnet publish .\KeyTranslate.Native.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The standalone app will be at `bin\Release\net10.0-windows\win-x64\publish\KeyTranslate.exe`.

## Translation limit

KeyTranslate uses the free MyMemory translation service. Anonymous use is limited to 5,000 characters per day. This limit is set by the service and may change. See [MyMemory usage limits](https://mymemory.translated.net/doc/usagelimits.php).
