# vatSys Pain Relief
<img width="300" height="300" alt="PainReliefPlugin" src="https://github.com/user-attachments/assets/9deff198-6bda-4ff9-9dbe-6bd7b26fef6d" />

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

### Sound when your handoff is refused

When a controller refuses a handoff you made (the moment vatSys shows "Handoff refused by ..."), `rejected.wav` from the plugin's folder plays: a G and B♭ struck together twice. Replace it with any WAV you like; if the file is missing, a Windows system sound plays instead. A sender withdrawing a handoff they made to *you* arrives as the same network message, but makes no sound. The plugin only listens; nothing about the FDR or track is changed.

### Reject an incoming handoff

vatSys only lets you accept an incoming handoff or leave it sitting there. With this plugin, *Tools → Reject Handoff* lists every handoff addressed to you that you haven't accepted, as `<callsign>  from <sender>`. Pick one to refuse it: the sender gets a "handoff cancelled" back, which both vatSys and EuroScope show them as a refused handoff, the aircraft stays with them, and the FDR leaves handover-in on your side. The chime stops for that handoff straight away. If the sender hands it off again, that is a new handoff and chimes as normal.

This uses the same two steps vatSys itself takes when it refuses a handoff for an inactive plan and when a sender cancels one.

### Unlimited callsign length when adding a VSCS frequency

vatSys limits the callsign box on the VSCS *Add Freq* page to 10 characters, which cuts off longer callsigns. With this plugin the limit is lifted, so the full callsign can be typed. It changes only that text box, and only its length limit.

This fix is also in the VSCS Tweaks plugin. If both plugins are installed (VSCS Tweaks 1.2.0 or later), Pain Relief leaves it to VSCS Tweaks.

### About and Sector Configuration windows under display scaling

With Windows display scaling above 100%, the About window collapses into a narrow, nearly empty box with a cut-off title. The Sector Configuration Window also cuts off sector names at both ends ("T - Jakarta Grou" for "JT - Jakarta Group"). The plugin re-lays out the About window so every line shows in full. It also draws each sector name from its left edge across the full width of the list, ending with "..." if it is still too long. Hover over a name to see all of it. Nothing else about either window changes.

The Display Settings window is also cut off at the bottom under display scaling, hiding the last brightness slider and the Reset button, and the checkboxes on the left run under the Brightness panel. The plugin resizes the window to fit its contents and moves the Brightness panel clear of the checkboxes.

The Audio Settings window has the same trouble: the Emulation column is cut off ("VHFEmulatio", "Realistic H"), and the slider values are drawn over by the label under them. The plugin lays the left-hand controls out again at their real sizes and fits the window, with or without the Additional Audio Devices panel shown. It redoes this each time the window opens or *Advanced* is toggled, since vatSys resets the window width then.

### VSCS clicks landing away from the drawn buttons

When vatSys does its own DPI scaling (Properties → Compatibility → High DPI → *Scaling performed by: Application*), the VSCS is drawn larger than its click areas, so a click lands away from the button you aimed at. The plugin sets the VSCS's drawing back to one unit per pixel, so the buttons and click areas line up again. Nothing changes at 100% scaling or with *System* scaling.

This fix is also in the VSCS Tweaks plugin. If both plugins are installed (VSCS Tweaks 1.2.0 or later), Pain Relief leaves it to VSCS Tweaks.

## Install

Copy `PainReliefPlugin.dll`, `chime.wav` and `rejected.wav` into a folder under your profile's `Plugins` directory, e.g.

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

The refused-handoff sound hooks `FSDSession.HandoffCancelledReceived` the same way, at the front of the event, because vatSys's own handler clears the handoff immediately. If that name changes, the plugin raises one "Pain Relief" error and there is no sound.

Reject Handoff calls vatSys's internal `Network.CancelHandoff(FDR, NetworkATC)` (plus the public `FDP2.CancelHandoff`), checked against the same vatSys build. If an update renames it, picking a handoff raises one "Pain Relief" error and the handoff is left as it was.

The VSCS callsign fix reaches two private fields (`MMI.VSCSWindow` and the window's `vscsControl`, then `VSCSControlDX.addFreqNameField`), checked against the same vatSys build. If an update renames them the plugin raises one "Pain Relief" error and vatSys keeps its 10-character limit.

The display-scaling fixes exist because vatSys starts without DPI awareness, then becomes per-monitor DPI-aware when its built-in browser (CEF) starts up. After that, WinForms draws the 16px Terminus font wider than the native controls laid it out, which breaks both windows. The fixes reach `MMI.MainForm` and `MMI.SectorsWindow`, plus private members of `AboutWindow` (`tableLayoutPanel1`, `insetPanel1`, `genericButton1`) and `SectorsWindow` (`currSectorsView`, `availSectorsView`, `SectorsView_DrawNode`), plus `MMI.DisplaySettingsWindow` and its `insetPanel1`, `tableLayoutPanel1`–`5`, and `MMI.AudioSettingsWindow` with the layout panels, sliders and labels it lays out (`tableLayoutPanel1`, `flowLayoutPanel1`, `insetPanel1`/`2`, `additionalDeviceLayoutPanel`, `additionalDevicePanel`–`3`, `advancedButton` and others), checked against the same vatSys build. If an update renames them, the plugin raises one "Pain Relief" error and the windows look as they do without the plugin.

The VSCS fix reaches `MMI.VSCSWindow`, the window's `vscsControl`, and the control's Direct2D render target (`dc`, `dc_properties`), checked against the same vatSys build. If an update renames them the plugin raises one "Pain Relief" error and the VSCS keeps vatSys's own drawing.
