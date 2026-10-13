using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using vatsys;

namespace PainRelief
{
    /// <summary>
    /// Removes vatSys's 10-character limit on the callsign box of the VSCS "Add Freq" page, so
    /// long callsigns (e.g. "WAAF_BI_CTR", "WAAA_ATIS", multi-part names) can be typed in full
    /// when adding a frequency.
    ///
    /// vatSys sets addFreqNameField.MaxLength = 10 when it builds the VSCS control. The field is
    /// private, and the VSCS window is created after plugins load, so a short timer looks for it
    /// and raises the limit once it exists (and again if the window is ever rebuilt).
    ///
    /// The limit is set to a 12 to match with VATSIM's 12-character callsign limit, but can be changed to any value.
    ///
    /// Nothing else about the VSCS window is touched.
    /// </summary>
 
    internal sealed class VscsCallsignLimit
    {
        private const int TickMilliseconds = 1000;
        private const int NewLimit = 12;

        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo VscsWindowField = typeof(MMI).GetField("VSCSWindow", NonPublicStatic);

        private readonly System.Threading.Timer timer;
        private int ticking;
        private bool errorReported;

        public VscsCallsignLimit()
        {
            timer = new System.Threading.Timer(Tick, null, TickMilliseconds, TickMilliseconds);
        }

        private void Tick(object state)
        {
            if (Interlocked.Exchange(ref ticking, 1) == 1)
                return;

            try
            {
                Apply();
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

        private void Apply()
        {
            if (VscsWindowField == null)
                throw new MissingFieldException("MMI.VSCSWindow not found");

            object window = VscsWindowField.GetValue(null);
            if (window == null)
                return; // window not created yet

            FieldInfo controlField = window.GetType().GetField("vscsControl", NonPublicInstance);
            object control = controlField?.GetValue(window);
            if (control == null)
                throw new MissingFieldException("VSCSWindow.vscsControl not found");

            FieldInfo nameField = control.GetType().GetField("addFreqNameField", NonPublicInstance);
            ComboField combo = nameField?.GetValue(control) as ComboField;
            if (combo == null)
                throw new MissingFieldException("VSCSControlDX.addFreqNameField not found");

            if (combo.MaxLength >= NewLimit)
                return;

            // Touch the control on its own UI thread.
            if (combo.IsHandleCreated && combo.InvokeRequired)
                combo.BeginInvoke((MethodInvoker)(() => combo.MaxLength = NewLimit));
            else
                combo.MaxLength = NewLimit;
        }

        private void Report(Exception ex)
        {
            // Report once, then stop polling: if a vatSys update renamed these private fields
            // there is nothing more to do, and vatSys keeps its stock 10-character limit.
            if (errorReported)
                return;
            errorReported = true;
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            Errors.Add(new Exception("Could not remove the VSCS callsign length limit: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
        }
    }
}
