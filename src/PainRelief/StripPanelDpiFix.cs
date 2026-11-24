using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct2D1;
using vatsys;

namespace PainRelief
{
    /// <summary>
    /// Keeps the strip windows' drawing lined up with their click areas under display scaling:
    /// Jurisdiction, Announced, Preactive, Handover In and Handover Out, plus the beacon and
    /// "other" strip windows.
    ///
    /// The same trouble as the VSCS (see VscsDpiFix). Each strip window draws its strips with a
    /// StripPanelDX, which creates its Direct2D render target without saying which DPI to use.
    /// Once vatSys is DPI-aware, Direct2D takes the screen's DPI (144 at 150%) and draws
    /// everything that much bigger. But the strips are laid out, and their click areas placed, in
    /// plain pixels (the Terminus fonts are sized in pixels), and the window is sized to fit that
    /// layout. So the drawn strips spill past where they are clickable and past the window.
    ///
    /// The fix sets the render target's DPI to 96, so one drawing unit is one pixel again and the
    /// strips are drawn at the size they were laid out. The panel also renders the strip
    /// templates into an offscreen bitmap made from that render target, so rather than change the
    /// DPI of a live target, the panel's device resources are dropped and vatSys rebuilds them on
    /// the next paint, now at 96 DPI. At 96 DPI already (System scaling, or 100%) nothing changes.
    /// </summary>
    internal sealed class StripPanelDpiFix
    {
        private const float PixelDpi = 96f;
        private const int TickMilliseconds = 1000;

        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        // MMI's strip windows (internal static StripWindow fields), plus the beacon list.
        private static readonly string[] WindowFieldNames =
        {
            "JurisdictionW", "AnnouncedW", "PreactiveW", "HandoverInW", "HandoverOutW", "OtherW",
        };
        private const string BeaconWindowsField = "beaconW";
        // StripWindow.stripPanelDX1 -> the StripPanelDX
        private const string PanelFieldName = "stripPanelDX1";

        private readonly System.Threading.Timer timer;
        private readonly HashSet<Control> attached = new HashSet<Control>();
        private readonly object gate = new object();
        private int ticking;
        private bool errorReported;

        // StripPanelDX members, looked up once from the first panel found.
        private FieldInfo renderTargetField;
        private FieldInfo renderTargetPropertiesField;
        private FieldInfo bitTargetField;
        private MethodInfo destroyMethod;

        public StripPanelDpiFix()
        {
            timer = new System.Threading.Timer(Tick, null, TickMilliseconds, TickMilliseconds);
        }

        private void Tick(object state)
        {
            if (Interlocked.Exchange(ref ticking, 1) == 1)
                return;

            try
            {
                foreach (Control panel in FindPanels())
                {
                    bool isNew;
                    lock (gate)
                        isNew = !attached.Contains(panel);
                    if (isNew && panel.IsHandleCreated && !panel.IsDisposed)
                        panel.BeginInvoke((MethodInvoker)(() => Guard(() => Attach(panel))));
                }
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

        /// <summary>The StripPanelDX of every strip window vatSys has created so far.</summary>
        private static IEnumerable<Control> FindPanels()
        {
            var windows = new List<object>();
            foreach (string name in WindowFieldNames)
            {
                FieldInfo field = typeof(MMI).GetField(name, NonPublicStatic)
                    ?? throw new MissingFieldException("MMI." + name + " not found");
                object window = field.GetValue(null);
                if (window != null)
                    windows.Add(window);
            }

            // Beacon windows are optional extras; a missing list is not an error. vatSys changes
            // the list on its own thread, so a walk that collides with a change is just retried
            // on the next tick.
            if (typeof(MMI).GetField(BeaconWindowsField, NonPublicStatic)?.GetValue(null) is IEnumerable beacons)
            {
                var found = new List<object>();
                try
                {
                    foreach (object window in beacons)
                        if (window != null)
                            found.Add(window);
                    windows.AddRange(found);
                }
                catch (InvalidOperationException)
                {
                }
            }

            var panels = new List<Control>();
            foreach (object window in windows)
            {
                FieldInfo panelField = window.GetType().GetField(PanelFieldName, NonPublicInstance)
                    ?? throw new MissingFieldException("StripWindow." + PanelFieldName + " not found");
                if (panelField.GetValue(window) is Control panel)
                    panels.Add(panel);
            }
            return panels;
        }

        private void Attach(Control panel)
        {
            lock (gate)
            {
                if (attached.Contains(panel) || panel.IsDisposed)
                    return;
            }

            if (renderTargetField == null)
            {
                Type type = panel.GetType();
                renderTargetField = type.GetField("dc", NonPublicInstance);
                renderTargetPropertiesField = type.GetField("dc_properties", NonPublicInstance);
                bitTargetField = type.GetField("bitTarget", NonPublicInstance);
                destroyMethod = type.GetMethod("DestroyDeviceResources", NonPublicInstance, null, Type.EmptyTypes, null);
                if (renderTargetField == null || renderTargetPropertiesField == null || bitTargetField == null || destroyMethod == null)
                    throw new MissingFieldException("StripPanelDX render target members not found");
            }

            // vatSys rebuilds the render target if the graphics device is lost, and every redraw
            // starts with Invalidate(), so check again each time. Cheap once it's right.
            panel.Invalidated += (s, e) => Guard(() => Apply(panel));
            panel.Disposed += (s, e) => { lock (gate) attached.Remove(panel); };
            lock (gate)
                attached.Add(panel);
            Apply(panel);
        }

        private void Apply(Control panel)
        {
            if (panel.IsDisposed)
                return;

            // The DPI vatSys uses whenever it (re)creates the render target.
            var properties = (RenderTargetProperties)renderTargetPropertiesField.GetValue(panel);
            if (properties.DpiX != PixelDpi || properties.DpiY != PixelDpi)
            {
                properties.DpiX = PixelDpi;
                properties.DpiY = PixelDpi;
                renderTargetPropertiesField.SetValue(panel, properties);
            }

            // A render target that already exists was made at the screen's DPI: drop it (and the
            // strip bitmap made from it) so the next paint rebuilds both at 96.
            if (renderTargetField.GetValue(panel) is RenderTarget target && !target.IsDisposed)
            {
                Size2F dpi = target.DotsPerInch;
                if (dpi.Width != PixelDpi || dpi.Height != PixelDpi)
                {
                    destroyMethod.Invoke(panel, null);
                    // vatSys's own DestroyDeviceResources leaves these set; its paint does the
                    // same clean-up after a lost device.
                    renderTargetField.SetValue(panel, null);
                    if (bitTargetField.GetValue(panel) is IDisposable bitTarget)
                        bitTarget.Dispose();
                    bitTargetField.SetValue(panel, null);
                    panel.Invalidate(); // repaint at the corrected size
                }
            }
        }

        private void Guard(Action action)
        {
            if (errorReported)
                return;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        private void Report(Exception ex)
        {
            // Report once, then stop: if a vatSys update renamed these private members there is
            // nothing more to do, and vatSys keeps its own strip drawing.
            if (errorReported)
                return;
            errorReported = true;
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            Errors.Add(new Exception("Could not fix strip window scaling: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
        }
    }
}
