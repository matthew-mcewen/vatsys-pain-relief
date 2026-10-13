# vatSys Pain Relief

A [vatSys](https://virtualairtrafficsystem.com/) plugin that smooths over rough edges when your neighbouring controller is on a different client (mostly EuroScope).

## What it does

### Auto-activate on incoming handoff

vatSys refuses any handoff for a flight data record that is inactive, preactive or coordinated: it immediately sends a "handoff cancelled" back to the sender. EuroScope has no concept of FDR activation, so from the other controller's side the handoff simply bounces.

With this plugin, when a handoff addressed to you arrives for such an FDR, it is activated on the spot (exactly as if you had used the *activate* menu item) and the handoff then shows in handover-in as normal. You still accept it yourself. If the track isn't coupled, autocouple or manual identification works as usual.

The departure time used for the activation is normally "now". If you are connected as a centre position whose callsign starts with a four-letter FIR code (`WAAF_BI_CTR`, `VTBB_CTR`, ...) and the flight plan remarks include an elapsed time for that FIR (`EET/WAAF0623`), it is set to now minus that elapsed time instead, since the aircraft is crossing into your FIR about now. Where a route enters the same FIR more than once, the first entry is used.

If your callsign ends in `_CTR` or `_APP`, the aircraft is then placed a fixed time before the point where its route enters the sectors you control: 10 minutes for `_CTR`, 5 minutes for `_APP`. After activating, the plugin reads vatSys's own sector entry time for the FDR and shifts the ATD so that entry time is that far in the future. Ground speed is whatever vatSys uses (the FDR's TAS). If the route never enters your sectors, or you are not a `_CTR`/`_APP` position, the departure time above stands. This happens once, at activation only: an ATD you type into the FDR afterwards is never overwritten.

Only the FDR is changed; nothing about track display is touched.

Handoffs for FDRs that are already active, or addressed to someone else, are not touched.

### Chime for an unaccepted handoff

When a handoff addressed to you arrives and you haven't accepted it 5 seconds later, a chime plays every 2 seconds until you accept it. It uses `chime.wav` from the plugin's folder (replace it with any WAV you like), or a Windows system sound if that file is missing. The plugin watches the same handoff event described below and `fdr.IsTrackedByMe`, and never changes the FDR or any track. As a safety net it stops after 2 minutes, since a sender can cancel a handoff without vatSys telling the plugin.

## Install

Copy `PainReliefPlugin.dll` and `chime.wav` into a folder under your profile's `Plugins` directory, e.g.

```
Documents\vatSys Files\Profiles\<Profile>\Plugins\PainRelief\PainReliefPlugin.dll
```

and restart vatSys. If you downloaded the DLL rather than building it, right-click it → Properties → *Unblock* first.

## Build

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (8 or later) and vatSys installed.

```
dotnet build src/PainRelief -c Release
```

Output is `src/PainRelief/bin/Release/PainReliefPlugin.dll`.

- vatSys installed somewhere other than `C:\Program Files (x86)\vatSys\bin`? Add `-p:VatSysDir="<path to bin>"`.
- Add `-p:Deploy=true` to copy the DLL into the Australia profile's `Plugins\PainRelief` folder (change with `-p:DeployDir="<folder>"`). vatSys must be closed.

## How it works, and what can break

vatSys has no plugin hook for handoffs, and by the time its own handoff handler has finished the refusal is already sent. So the plugin uses reflection to place its handler ahead of vatSys's on the network session's `HandoffReceived` event; see [HandoffAutoActivate.cs](src/PainRelief/HandoffAutoActivate.cs).

This depends on three non-public names inside vatSys (`Network.Instance`, `Network.VATSIM`, `FSDSession.HandoffReceived`), checked against vatSys 0.4.9305. If an update renames them, the plugin raises a single "Pain Relief" error in the vatSys error window and vatSys carries on with its stock behaviour.
