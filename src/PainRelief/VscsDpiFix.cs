using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct2D1;
using vatsys;

namespace PainRelief
{
    /// <summary>
    /// Keeps the VSCS's drawing lined up with its buttons when Windows lets vatSys do its own
    /// DPI scaling (Properties → Compatibility → High DPI → "Scaling performed by: Application"),
    /// or when vatSys becomes DPI-aware on its own. Without it the VSCS's click areas sit away
    /// from where its buttons are drawn.
    ///
    /// VSCSControlDX creates its Direct2D render target without saying which DPI to use, so
    /// Direct2D takes the screen's DPI from Windows (120 at 125%, 144 at 150%, or anything in
    /// between for a custom scale) and draws everything that much bigger. But its button grid
    /// and click areas are in plain pixels, so the drawing no longer matches where clicks land.
    /// With "System" scaling Windows reports 96 DPI to vatSys, which is why that mode works.
    ///
    /// The fix reads the DPI Direct2D actually picked, whatever the scale, and if it isn't 96
    /// sets it back to 96 so one drawing unit is one pixel again, matching the click areas. It
    /// also sets 96 in the properties vatSys reuses whenever it rebuilds the render target.
    /// At 96 DPI (System scaling, or 100%) it changes nothing.
    ///
    /// This is the same fix as in the VSCS Tweaks plugin, which takes precedence: while it is
    /// loaded (see VscsTweaksPresence) this one does nothing, even if it was already attached.
    /// </summary>
    internal sealed class VscsDpiFix
    {
        private const float PixelDpi = 96f;
        private const int TickMilliseconds = 1000;

        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo VscsWindowField = typeof(MMI).GetField("VSCSWindow", NonPublicStatic);

        private readonly System.Threading.Timer timer;
        private int ticking;
        private bool errorReported;
        private volatile Control attachedControl;
        private FieldInfo renderTargetField;
        private FieldInfo renderTargetPropertiesField;

        public VscsDpiFix()
        {
            timer = new System.Threading.Timer(Tick, null, TickMilliseconds, TickMilliseconds);
        }

        private void Tick(object state)
        {
            if (Interlocked.Exchange(ref ticking, 1) == 1)
                return;

            try
            {
                if (VscsTweaksPresence.IsLoaded)
                    return; // VSCS Tweaks handles it; keep checking in case that's all we ever see

                Control control = FindVscsControl();
                if (control != null && control != attachedControl && control.IsHandleCreated)
                    control.BeginInvoke((MethodInvoker)(() => Guard(() => Attach(control))));
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

        private static Control FindVscsControl()
        {
            if (VscsWindowField == null)
                throw new MissingFieldException("MMI.VSCSWindow not found");

            object window = VscsWindowField.GetValue(null);
            if (window == null)
                return null; // window not created yet

            FieldInfo controlField = window.GetType().GetField("vscsControl", NonPublicInstance);
            if (controlField == null)
                throw new MissingFieldException("VSCSWindow.vscsControl not found");

            return controlField.GetValue(window) as Control;
        }

        private void Attach(Control control)
        {
            if (control == attachedControl || control.IsDisposed || VscsTweaksPresence.IsLoaded)
                return;

            Type type = control.GetType();
            renderTargetField = type.GetField("dc", NonPublicInstance);
            renderTargetPropertiesField = type.GetField("dc_properties", NonPublicInstance);
            if (renderTargetField == null || renderTargetPropertiesField == null)
                throw new MissingFieldException("VSCSControlDX render target fields not found");

            // vatSys rebuilds the render target if the graphics device is lost; every repaint
            // starts with Invalidate(), so check again each time.
            control.Invalidated += (s, e) => Guard(() => Apply(control));
            attachedControl = control;
            Apply(control);
        }

        private void Apply(Control control)
        {
            // VSCS Tweaks may have loaded after this was attached; if so, leave it to that.
            if (VscsTweaksPresence.IsLoaded)
                return;

            var properties = (RenderTargetProperties)renderTargetPropertiesField.GetValue(control);
            if (properties.DpiX != PixelDpi || properties.DpiY != PixelDpi)
            {
                properties.DpiX = PixelDpi;
                properties.DpiY = PixelDpi;
                renderTargetPropertiesField.SetValue(control, properties);
            }

            if (renderTargetField.GetValue(control) is RenderTarget target && !target.IsDisposed)
            {
                Size2F dpi = target.DotsPerInch;
                if (dpi.Width != PixelDpi || dpi.Height != PixelDpi)
                {
                    target.DotsPerInch = new Size2F(PixelDpi, PixelDpi);
                    control.Invalidate(); // redraw at the corrected size
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
            // Report once, then stop: if a vatSys update renamed these private fields there is
            // nothing more to do, and vatSys keeps its own VSCS drawing.
            if (errorReported)
                return;
            errorReported = true;
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            Errors.Add(new Exception("Could not fix VSCS scaling: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
        }
    }
}
