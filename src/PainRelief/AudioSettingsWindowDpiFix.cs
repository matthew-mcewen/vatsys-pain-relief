using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using vatsys;

namespace PainRelief
{
    /// <summary>
    /// Fixes the Audio Settings window under Windows display scaling: the "Emulation" column
    /// is cut off ("VHFEmulatio", "Realistic H"), the slider values are overdrawn by the label
    /// under them, and the Additional Audio Devices panel crowds the main devices.
    ///
    /// Why it breaks: vatSys has no DPI manifest, so it starts DPI-unaware, but Cef.Initialize
    /// (the built-in browser) switches the process to per-monitor DPI awareness partway through
    /// startup. The sliders and tables then come out larger than the designer sized them, but
    /// the controls are placed at fixed positions and the container holding the three top
    /// controls (tone volumes and Emulation) is a fixed 479px wide, so the Emulation column is
    /// clipped and each slider runs into the label under it. On top of that vatSys sets the
    /// window to a fixed 980px (or 510px without the additional devices) every time it is shown
    /// or Advanced is toggled, whatever the contents need.
    ///
    /// The fix stacks the left-hand controls again from their real sizes, keeping the designed
    /// gaps, widens the tone/emulation row to fit, moves the Additional Audio Devices panel to
    /// the right of the widest left-hand control, wraps it around its device list, and then
    /// fits the window to what is shown. Nothing is scaled by a guessed factor: sizes come from
    /// the controls' own preferred sizes, so it is correct at any scale, including 100%.
    ///
    /// Because vatSys resets the window width each time it is shown or Advanced is toggled, the
    /// layout is redone after each of those. It is idempotent.
    /// </summary>
    internal sealed class AudioSettingsWindowDpiFix
    {
        private const int TickMilliseconds = 250;

        // Gaps between the left-hand controls, in pixels, as the window was designed.
        private const int MenuGap = 6;         // main devices table to the tone/emulation row
        private const int LabelOverlap = -1;   // "Input Volume Adjustment" label sits on the meter
        private const int MeterGap = 6;        // meter to the input volume slider
        private const int SliderToLabelGap = 3;
        private const int LabelToSliderGap = -13; // a slider's top is blank, so its label overlaps it
        private const int DesignedSliderHeight = 45;
        private const int BottomMargin = 14;   // under the last slider
        private const int PanelGap = 2;        // between the left column and the devices panel
        private const int ListPaddingX = 3;    // device tables to the edge of their list, as designed
        private const int ListPaddingY = 4;
        private const int PanelPadding = 3;    // devices list to its panel's edge
        private const int RightMargin = 6;     // devices panel to the window edge
        private const int NarrowMargin = 9;    // left column to the window edge without the panel
        private const int PanelBottomMargin = 2;

        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly string[] DevicePanels =
            { "additionalDevicePanel", "additionalDevicePanel1", "additionalDevicePanel2", "additionalDevicePanel3" };

        private static readonly FieldInfo AudioWindowField = typeof(MMI).GetField("AudioSettingsWindow", NonPublicStatic);

        private readonly System.Threading.Timer timer;
        private readonly ConditionalWeakTable<Form, object> hooked = new ConditionalWeakTable<Form, object>();
        private int ticking;
        private int errorReported;

        public AudioSettingsWindowDpiFix()
        {
            timer = new System.Threading.Timer(Tick, null, TickMilliseconds, TickMilliseconds);
        }

        private void Tick(object state)
        {
            if (Interlocked.Exchange(ref ticking, 1) == 1)
                return;

            try
            {
                if (AudioWindowField == null)
                    throw new MissingFieldException("MMI.AudioSettingsWindow not found");

                Form window = AudioWindowField.GetValue(null) as Form;
                if (window == null || window.IsDisposed || !window.IsHandleCreated)
                    return; // not created yet
                if (hooked.TryGetValue(window, out _))
                    return; // already hooked

                hooked.Add(window, null);
                window.BeginInvoke((MethodInvoker)(() =>
                {
                    try
                    {
                        // Both run after vatSys's own handlers, which were added first.
                        window.VisibleChanged += OnChanged;
                        GetControl(window, "advancedButton").Click += OnChanged;
                        if (window.Visible)
                            Apply(window);
                    }
                    catch (Exception ex)
                    {
                        Report(ex);
                    }
                }));
            }
            catch (Exception ex)
            {
                Report(ex);
            }
            finally
            {
                Volatile.Write(ref ticking, 0);
            }
        }

        private void OnChanged(object sender, EventArgs e)
        {
            Control source = (Control)sender;
            Form window = source as Form ?? source.FindForm();
            if (window == null || !window.IsHandleCreated)
                return;

            // Run after vatSys has finished its own show / toggle work.
            window.BeginInvoke((MethodInvoker)(() =>
            {
                try
                {
                    if (window.Visible)
                        Apply(window);
                }
                catch (Exception ex)
                {
                    Report(ex);
                }
            }));
        }

        /// <summary>Re-lays out one AudioSettingsWindow. Must run on its UI thread.</summary>
        internal static void Apply(Form window)
        {
            Control devicesTable = GetControl(window, "tableLayoutPanel1");
            Control toneRow = GetControl(window, "flowLayoutPanel1");
            Control inputLabel = GetControl(window, "textLabel5");
            Control meterPanel = GetControl(window, "insetPanel1");
            Control inputSlider = GetControl(window, "inputVolSlider");
            Control afvLabel = GetControl(window, "textLabel6");
            Control afvSlider = GetControl(window, "afvOutputSlider");
            Control opusLabel = GetControl(window, "textLabel8");
            Control opusSlider = GetControl(window, "opusVolumeSlider");
            Control devicesPanel = GetControl(window, "insetPanel2");
            Control devicesLabel = GetControl(window, "additionalLabel");
            Control devicesList = GetControl(window, "additionalDeviceLayoutPanel");

            window.SuspendLayout();

            // A slider's blank top margin grows with its height, so scale the label overlap.
            double sliderScale = Math.Max(1.0, (double)inputSlider.GetPreferredSize(Size.Empty).Height / DesignedSliderHeight);
            int labelToSlider = (int)Math.Round(LabelToSliderGap * sliderScale);

            // Left column, top to bottom, each at its real size and the designed gap.
            Fit(devicesTable);
            Fit(toneRow);
            Fit(inputLabel);
            Fit(inputSlider);
            Fit(afvLabel);
            Fit(afvSlider);
            Fit(opusLabel);
            Fit(opusSlider);

            toneRow.Top = devicesTable.Bottom + MenuGap;
            inputLabel.Top = toneRow.Bottom + LabelOverlap;
            meterPanel.Top = inputLabel.Bottom + LabelOverlap;
            inputSlider.Top = meterPanel.Bottom + MeterGap;
            afvLabel.Top = inputSlider.Bottom + SliderToLabelGap;
            afvSlider.Top = afvLabel.Bottom + labelToSlider;
            opusLabel.Top = afvSlider.Bottom + SliderToLabelGap;
            opusSlider.Top = opusLabel.Bottom + labelToSlider;

            int columnRight = Math.Max(devicesTable.Right, toneRow.Right);
            columnRight = Math.Max(columnRight, Math.Max(inputSlider.Right, Math.Max(afvSlider.Right, opusSlider.Right)));
            meterPanel.Width = columnRight - meterPanel.Left; // the meter spans the column

            int clientWidth = columnRight + NarrowMargin;
            int clientHeight = opusSlider.Bottom + BottomMargin;

            // Additional Audio Devices panel: wrap it around the device list, to the right of
            // the left column. Only measured while shown; showing it runs this again.
            Size? panelSize = null;
            if (devicesPanel.Visible)
            {
                int right = 0;
                int bottom = 0;
                foreach (string name in DevicePanels)
                {
                    Control panel = GetControl(window, name);
                    right = Math.Max(right, panel.Right);
                    bottom = Math.Max(bottom, panel.Bottom);
                }
                devicesList.Size = new Size(right + ListPaddingX, bottom + ListPaddingY);
                panelSize = new Size(devicesList.Right + PanelPadding, devicesList.Bottom + PanelPadding);
                devicesPanel.Size = panelSize.Value;
                devicesPanel.Left = columnRight + PanelGap;
                devicesLabel.Left = devicesPanel.Left - 1;

                clientWidth = devicesPanel.Right + RightMargin;
                clientHeight = Math.Max(clientHeight, devicesPanel.Bottom + PanelBottomMargin);
            }

            window.ResumeLayout(true);
            window.ClientSize = new Size(clientWidth, clientHeight);

            // The panel is anchored to the bottom, so the resize above stretched it; put it
            // back to the size that wraps its device list.
            if (panelSize.HasValue)
                devicesPanel.Size = panelSize.Value;
            window.Invalidate(true);
        }

        private static void Fit(Control control)
        {
            control.Size = control.GetPreferredSize(Size.Empty);
        }

        private static Control GetControl(Form window, string name)
        {
            Control control = window.GetType().GetField(name, NonPublicInstance)?.GetValue(window) as Control;
            if (control == null)
                throw new MissingFieldException(window.GetType().Name + "." + name + " not found");
            return control;
        }

        private void Report(Exception ex)
        {
            // Report once, then stop polling: if a vatSys update renamed these private members
            // there is nothing more to do, and the window keeps vatSys's own layout.
            if (Interlocked.Exchange(ref errorReported, 1) == 1)
                return;
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            Errors.Add(new Exception("Could not fix the Audio Settings window for display scaling: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
        }
    }
}
