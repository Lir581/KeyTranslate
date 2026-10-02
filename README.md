# KeyTranslate

A small Windows app for translating selected text.

## Download and run

1. Install the **.NET 10 Desktop Runtime for Windows x64** from the [official Microsoft download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Choose **.NET Desktop Runtime** 
2. Download the `KeyTranslate` ZIP from Releases and extract it.
3. Keep all files together and run `KeyTranslate.exe`.
4. Choose the source and target languages, then click **Save settings**.
5. Select text in any app and press `F8`.

Automatic translation is off by default. Toggle it with `F7`, the button in the app, or the tray menu. Set the delay from 1 to 30 seconds in the app.

The release ZIP contains the application files, but not the .NET Runtime. Do not download only `KeyTranslate.exe`; keep the extracted files together.

## Build

Install the .NET 10 SDK, open PowerShell in the project folder, and run:

```powershell
dotnet publish .\KeyTranslate.Native.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The standalone app will be at `bin\Release\net10.0-windows\win-x64\publish\KeyTranslate.exe`.

## Translation limit

KeyTranslate uses the free MyMemory translation service. Anonymous use is limited to 5,000 characters per day. This limit is set by the service and may change. See [MyMemory usage limits](https://mymemory.translated.net/doc/usagelimits.php).
