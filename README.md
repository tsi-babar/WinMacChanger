# WinMacChanger

A tiny Windows GUI tool to change (spoof) or restore a network adapter's MAC address.

Single small `.exe`, no installer, no runtime download — targets **.NET Framework 4.8**, which ships with Windows 10 and 11.

## Features

- Lists your physical network adapters and shows each one's current MAC.
- Set a new MAC, or **Randomize** a valid locally-administered address.
- **Apply** writes the change and restarts the adapter so it takes effect immediately.
- **Restore original** clears the override and returns the adapter to its hardware MAC.

## ⚠️ Authorized use only

Only change the MAC address on adapters of a device **you own or are authorized to administer**.
Spoofing a MAC on a network you do not control may violate that network's acceptable-use policy
or local rules. This tool changes your own machine's adapter and nothing else.

## Download

1. Go to the **[Releases](../../releases)** page and download `WinMacChanger.exe` from the latest release, **or**
2. Open the latest run on the **[Actions](../../actions)** tab and download the `WinMacChanger-exe` artifact.

## Run

1. **Right-click `WinMacChanger.exe` → Run as administrator** (changing a MAC edits `HKLM` and restarts the adapter, which needs admin rights — the app also prompts for elevation automatically).
2. Pick your adapter.
3. Type a MAC (e.g. `02:1A:2B:3C:4D:5E`) or click **Randomize**, then **Apply**.
4. Click **Restore original** any time to revert.

> Tip: use a MAC whose first octet has the `0x02` bit set (locally administered), e.g. starts with `02`, `06`, `0A`, `0E`. **Randomize** does this for you. Some adapters reject certain values; if a change doesn't stick, try another address.

## Build locally (on Windows)

```powershell
dotnet build WinMacChanger.csproj -c Release
# output: bin\Release\net48\WinMacChanger.exe
```

## How it works

Windows stores a per-adapter MAC override in the registry under
`HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\<NNNN>`
as the `NetworkAddress` value. The app writes that value (Apply) or deletes it (Restore), then
disables and re-enables the adapter via `netsh` so the NIC re-reads it.

## License

[MIT](LICENSE)
