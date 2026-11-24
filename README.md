# vatSys Pain Relief
<img width="300" height="300" alt="PainReliefPlugin" src="https://github.com/user-attachments/assets/9deff198-6bda-4ff9-9dbe-6bd7b26fef6d" />

A [vatSys](https://virtualairtrafficsystem.com/) plugin that smooths over rough edges when your neighbouring controller is on a different client (mostly EuroScope).

## What it does

### Auto-activate on incoming handoff

vatSys refuses any handoff for a flight data record that is inactive, preactive or coordinated: it immediately sends a "handoff cancelled" back to the sender. EuroScope has no concept of FDR activation, so from the other controller's side the handoff simply bounces.

With this plugin, when a handoff addressed to you arrives for such an FDR, it is activated on the spot (exactly as if you had used the *activate* menu item) and the handoff then shows in handover-in as normal. You still accept it yourself. If the track isn't coupled, autocouple or manual identification works as usual.

The departure time used for the activation is normally "now". If you are connected as a centre position whose callsign starts with a four-letter FIR code (`WAAF_BI_CTR`, `VTBB_CTR`, ...) and the flight plan remarks include an elapsed time for that FIR (`EET/WAAF0623`), it is set to now minus that elapsed time instead, since the aircraft is crossing into your FIR about now. Where a route enters the same FIR more than once, the first entry is used.

If your callsign ends in `_CTR` or `_APP`, the aircraft is then placed a fixed time before the point where its route enters the sectors you control: 10 minutes for `_CTR`, 5 minutes for `_APP`. The plugin adds up vatSys's own leg times along the route to that point and sets the ATD so the entry is that far in the future. Ground speed is whatever vatSys uses (the FDR's TAS). If the route never enters your sectors, or you are not a `_CTR`/`_APP` position, the departure time above stands. This happens once, at activation only: an ATD you type into the FDR afterwards is never overwritten.

Only the FDR is changed; nothing about track display is touched.

Handoffs for FDRs that are already active, or addressed to someone else, are not touched.

### Chimes for an incoming handoff

When a handoff addressed to you arrives, `incoming.wav` plays: a soft rising chime. If you haven't accepted it 10 seconds later, `nudge.wav` plays, then again every 10 seconds for the next 90 seconds (10 nudges, the last 100 seconds after the handoff arrived). Those timings are settings too (see below). The nudges stop as soon as you accept the handoff, it is refused (by you with Reject Handoff, or cancelled by the sender), the FDR is deactivated, or another controller takes the aircraft.

Set other WAVs with `HandoffIncomingSound` and `HandoffNudgeSound` in `PainRelief.conf`; `none` plays nothing, and `default` (or a missing file) plays a Windows system sound. The plugin watches the same handoff event described below, plus the FDR's handoff, tracking and state, and never changes the FDR or any track.

### Sound when your handoff is refused

When a controller refuses a handoff you made (the moment vatSys shows "Handoff refused by ..."), `refused.wav` from the plugin's folder plays: a G and B♭ struck together twice. Set another WAV with `HandoffRefusedSound` in `PainRelief.conf`; `default` plays vatSys's own `C:\Program Files (x86)\vatSys\wav\Tone3.wav`. If the file is missing, a Windows system sound plays instead. A sender withdrawing a handoff they made to *you* arrives as the same network message, but makes no sound. The plugin only listens; nothing about the FDR or track is changed.

### Reject an incoming handoff

vatSys only lets you accept an incoming handoff or leave it sitting there. With this plugin, *Tools → Reject Handoff* lists every handoff addressed to you that you haven't accepted, as `<callsign>  from <sender>`. Pick one to refuse it: the sender gets a "handoff cancelled" back, which both vatSys and EuroScope show them as a refused handoff, the aircraft stays with them, and the FDR leaves handover-in on your side. The nudges stop for that handoff straight away. If the sender hands it off again, that is a new handoff and chimes as normal.

The same refusal is also on the aircraft itself: the menu that opens on its label has *Reject* under *Accept* whenever that aircraft is being handed off to you.

This uses the same two steps vatSys itself takes when it refuses a handoff for an inactive plan and when a sender cancels one.

### Longer callsigns when adding a VSCS frequency

vatSys limits the callsign box on the VSCS *Add Freq* page to 10 characters, which cuts off longer callsigns. With this plugin the limit is raised to 12, VATSIM's own callsign limit, so any VATSIM callsign can be typed in full. It changes only that text box, and only its length limit.

This fix is also in the VSCS Tweaks plugin. If both plugins are installed (VSCS Tweaks 1.2.0 or later), Pain Relief leaves it to VSCS Tweaks.

### About and Sector Configuration windows under display scaling

With Windows display scaling above 100%, the About window collapses into a narrow, nearly empty box with a cut-off title. The Sector Configuration Window also cuts off sector names at both ends ("T - Jakarta Grou" for "JT - Jakarta Group"). The plugin re-lays out the About window so every line shows in full. It also draws each sector name from its left edge across the full width of the list, ending with "..." if it is still too long. Hover over a name to see all of it. Nothing else about either window changes.

The Display Settings window is also cut off at the bottom under display scaling, hiding the last brightness slider and the Reset button, and the checkboxes on the left run under the Brightness panel. The plugin resizes the window to fit its contents and moves the Brightness panel clear of the checkboxes.

The Audio Settings window has the same trouble: the Emulation column is cut off ("VHFEmulatio", "Realistic H"), and the slider values are drawn over by the label under them. The plugin lays the left-hand controls out again at their real sizes and fits the window, with or without the Additional Audio Devices panel shown. It redoes this each time the window opens or *Advanced* is toggled, since vatSys resets the window width then.

### FSD log of every connection

Every connection is logged, in EuroScope's log format, to a text file in the plugin's folder named `<Position>_<UTC date>_<UTC start time>.txt` (e.g. `WIII_APP_20261108_151640.txt`):

```
[15:16:40 >>>> WIII_APP]
%WILL_ATIS:27700:4:50:5:-5.24056:105.17556:0

[15:16:41 <<<2 WIII_APP]
#DAWIII_APP:SERVER
```

`>>>>` is a packet received, `<<<2` one sent; times are UTC. The log starts with the connection and is saved when you disconnect. When a new log starts, the oldest are deleted so that at most 10 remain. Change that with `FsdLogMaxFiles` in `PainRelief.conf`; `FsdLog=false` (or `FsdLogMaxFiles=0`) turns logging off. Both apply from your next connection. Only files named like a log are ever deleted. The ATIS connection isn't logged.

### VSCS clicks landing away from the drawn buttons

When vatSys does its own DPI scaling (Properties → Compatibility → High DPI → *Scaling performed by: Application*), the VSCS is drawn larger than its click areas, so a click lands away from the button you aimed at. The plugin sets the VSCS's drawing back to one unit per pixel, so the buttons and click areas line up again. Nothing changes at 100% scaling or with *System* scaling.

This fix is also in the VSCS Tweaks plugin. If both plugins are installed (VSCS Tweaks 1.2.0 or later), Pain Relief leaves it to VSCS Tweaks.

### Strip windows under display scaling

The strip windows (Jurisdiction, Announced, Preactive, Handover In and Handover Out, plus beacon strip windows) have the same trouble as the VSCS under display scaling: the strips are drawn larger than they are laid out, so they spill past their click areas and the window. The plugin sets their drawing back to one unit per pixel, so each strip is drawn at the size vatSys laid it out and lines up with where it can be clicked. Nothing changes at 100% scaling or with *System* scaling.

## Settings

`PainRelief.conf` in the plugin's folder switches features on and off and picks the sounds. If it's missing, the plugin writes out the default one the first time vatSys starts. Restart vatSys after changing it (the FSD logging settings apply from your next connection instead).

| Setting | Default | What it does |
|---|---|---|
| `ScalingFixes` | `true` | All the display-scaling fixes: VSCS click areas, the strip windows, and the Audio Settings, Display Settings, About and Sector Configuration windows. |
| `HandoffAutoActivate` | `true` | Auto-activate on incoming handoff. |
| `HandoffChime` | `true` | Chimes for an incoming handoff (incoming and nudge). Works whether or not `HandoffAutoActivate` is on. |
| `HandoffNudgeAfter` | `10` | Seconds from the handoff arriving to the first nudge. |
| `HandoffNudgeEvery` | `10` | Seconds between nudges (at least 1). |
| `HandoffNudgeFor` | `90` | Seconds after the first nudge to keep nudging; `0` gives a single nudge. |
| `HandoffIncomingSound` | `incoming.wav` | Played as a handoff to you arrives. |
| `HandoffNudgeSound` | `nudge.wav` | Played while the handoff waits unaccepted. |
| `HandoffRefusedSound` | `refused.wav` | Played when your handoff is refused. `default` plays `C:\Program Files (x86)\vatSys\wav\Tone3.wav`. |
| `FsdLog` | `true` | FSD log of every connection. |
| `FsdLogMaxFiles` | `10` | How many FSD logs to keep. |

For the three sounds, a file name is looked for in the plugin's folder and a full path works too; `none` plays nothing, and `default` plays a Windows system sound (for `HandoffRefusedSound`, vatSys's Tone3). A file that is missing or won't load plays the Windows system sound instead. Timings take decimals (`2.5`).

On/off settings take `true` or `false`. A setting that's missing or can't be read uses its default. The refused-handoff sound, Reject Handoff and the VSCS callsign limit are always on.

Updating the plugin by copying the whole folder over replaces `PainRelief.conf` with the default; keep a copy of yours if you've changed it.

## Install

Copy `PainReliefPlugin.dll`, `PainRelief.conf`, `incoming.wav`, `nudge.wav` and `refused.wav` into a folder under your profile's `Plugins` directory, e.g.

```
Documents\vatSys Files\Profiles\<Profile>\Plugins\PainRelief\PainReliefPlugin.dll
```

and restart vatSys. If you downloaded the DLL rather than building it, right-click it → Properties → *Unblock* first.

## Build

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (8 or later) and vatSys installed.

```
dotnet build src/PainRelief -c Release
```

A Release build puts everything in `src/PainRelief/bin/Release/`:

- `PainRelief\`: the folder to copy into a profile's `Plugins` directory (`PainReliefPlugin.dll`, `PainRelief.conf`, `incoming.wav`, `nudge.wav`, `refused.wav`).
- `PainRelief-v<version>.zip`: the same folder, zipped. Not committed.

The build never copies anything into a vatSys profile; install it by hand as above.

vatSys installed somewhere other than `C:\Program Files (x86)\vatSys\bin`? Add `-p:VatSysDir="<path to bin>"`.

## How it works, and what can break

vatSys has no plugin hook for handoffs, and by the time its own handoff handler has finished the refusal is already sent. So the plugin uses reflection to place its handler ahead of vatSys's on the network session's `HandoffReceived` event; see [HandoffAutoActivate.cs](src/PainRelief/HandoffAutoActivate.cs).

This depends on three non-public names inside vatSys (`Network.Instance`, `Network.VATSIM`, `FSDSession.HandoffReceived`), checked against vatSys 0.4.9305. If an update renames them, the plugin raises a single "Pain Relief" error in the vatSys error window and vatSys carries on with its stock behaviour.

The refused-handoff sound hooks `FSDSession.HandoffCancelledReceived` the same way, at the front of the event, because vatSys's own handler clears the handoff immediately. If that name changes, the plugin raises one "Pain Relief" error and there is no sound.

Reject Handoff calls vatSys's internal `Network.CancelHandoff(FDR, NetworkATC)` (plus the public `FDP2.CancelHandoff`), checked against the same vatSys build. If an update renames it, picking a handoff raises one "Pain Relief" error and the handoff is left as it was. The *Reject* item on the aircraft menu is inserted into vatSys's private `MMI.aircraftMenu`, after its *Accept* item; if that menu can't be found, one "Pain Relief" error is raised and *Tools → Reject Handoff* still works.

The VSCS callsign fix reaches two private fields (`MMI.VSCSWindow` and the window's `vscsControl`, then `VSCSControlDX.addFreqNameField`), checked against the same vatSys build. If an update renames them the plugin raises one "Pain Relief" error and vatSys keeps its 10-character limit.

The display-scaling fixes exist because vatSys starts without DPI awareness, then becomes per-monitor DPI-aware when its built-in browser (CEF) starts up. After that, WinForms draws the 16px Terminus font wider than the native controls laid it out, which breaks both windows. The fixes reach `MMI.MainForm` and `MMI.SectorsWindow`, plus private members of `AboutWindow` (`tableLayoutPanel1`, `insetPanel1`, `genericButton1`) and `SectorsWindow` (`currSectorsView`, `availSectorsView`, `SectorsView_DrawNode`), plus `MMI.DisplaySettingsWindow` and its `insetPanel1`, `tableLayoutPanel1`–`5`, and `MMI.AudioSettingsWindow` with the layout panels, sliders and labels it lays out (`tableLayoutPanel1`, `flowLayoutPanel1`, `insetPanel1`/`2`, `additionalDeviceLayoutPanel`, `additionalDevicePanel`–`3`, `advancedButton` and others), checked against the same vatSys build. If an update renames them, the plugin raises one "Pain Relief" error and the windows look as they do without the plugin.

The strip window fix reaches MMI's strip windows (`JurisdictionW`, `AnnouncedW`, `PreactiveW`, `HandoverInW`, `HandoverOutW`, `OtherW`, `beaconW`), each window's `stripPanelDX1`, and that panel's Direct2D render target (`dc`, `dc_properties`, `bitTarget`, `DestroyDeviceResources`), checked against the same vatSys build. If an update renames them the plugin raises one "Pain Relief" error and the strips keep vatSys's own drawing.

FSD logging subscribes to the FSD session's public `RawDataSent` and `RawDataReceived` events. The session is the same private `Network.VATSIM` as above, checked every 250 ms so a new connection is hooked before its login packets go out. If that name changes, the plugin raises one "Pain Relief" error and nothing is logged.

The VSCS fix reaches `MMI.VSCSWindow`, the window's `vscsControl`, and the control's Direct2D render target (`dc`, `dc_properties`), checked against the same vatSys build. If an update renames them the plugin raises one "Pain Relief" error and the VSCS keeps vatSys's own drawing.
