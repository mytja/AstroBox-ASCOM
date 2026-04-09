using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using ASCOM.Utilities.Interfaces;

namespace ASCOM.Simulators
{
    /// <summary>
    /// Local switch class
    /// </summary>
    [ComVisible(false)]
    public class LocalSwitch
    {
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double StepSize { get; set; }
        public string Name { get; set; }
        public string InternalID { get; set; }
        public bool CanWrite { get; set; }
        public double Value { get; set; }
        public string Description { get; set; }
        public bool InSync { get; set; }
        public bool RegularFetch { get; set; }
        public bool CanSetName { get; set; }
        public bool Hide {  get; set; }
        public bool IsCommand { get; set; }

        #region ISwitchV3 members

        public bool CanAsync { get; set; } // True if this switch can operate asynchronously

        public double Duration { get; set; } // Duration of the switch change

        public bool StateChangeComplete { get; set; } = true; // True when an asynchronous operation completes

        public Exception AsyncException { get; set; } = null; // Exception to return, if any, when the Connecting property is polled

        public CancellationTokenSource CancellationTokenSource { get; set; } = new CancellationTokenSource(); // Cancellation token source to cancel an in-progress asynchronous operation. Null when no operation is in progress

        public CancellationToken CancellationToken { get; set; } = CancellationToken.None; // Cancellation token to cancel an in-progress asynchronous operation. Null when no operation is in progress

        public Task Task { get; set; } // Current task

        #endregion



        #region constructors

        private LocalSwitch()
        {
            this.Maximum = 1.0;
            this.StepSize = 1.0;
            this.CanWrite = true;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalSwitch"/> class.
        /// default values are for a binary switch.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="internalId">The ID that is used to communicate with the microcontroller.</param>
        internal LocalSwitch(string name, string internalId, bool canSetName)
            : this(name, internalId, canSetName, 1, 0, 1, 0)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalSwitch"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="internalId">The ID that is used to communicate with the microcontroller.</param>
        /// <param name="maximum">The maximum.</param>
        /// <param name="minimum">The minimum.</param>
        /// <param name="stepSize">step Size</param>
        /// <param name="value">The value.</param>
        internal LocalSwitch(string name, string internalId, bool canSetName, double maximum, double minimum, double stepSize, double value)
            : this(name, internalId, canSetName, maximum, minimum, stepSize, value, true, false)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalSwitch"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="internalId">The ID that is used to communicate with the microcontroller.</param>
        /// <param name="max">The max.</param>
        /// <param name="min">The min.</param>
        /// <param name="step">The step.</param>
        /// <param name="canWrite">if set to <c>true</c> [read only].</param>
        /// <param name="value">The value.</param>
        public LocalSwitch(string name, string internalId, bool canSetName, double max, double min, double step, double value, bool canWrite, bool regularFetch)
        {
            this.Name = name;
            this.InternalID = internalId;
            this.CanSetName = canSetName;
            this.Maximum = max;
            this.Minimum = min;
            this.StepSize = step;
            this.CanWrite = canWrite;
            this.Value = value;
            this.Description = name;
            this.RegularFetch = regularFetch;
            //this.IsCommand = false;
        }

        #endregion constructors

        /// <summary>
        /// Sets the value with a check that the value is correct.
        /// Throws a MethodNotImplementedException if the switch is read only
        /// Throws an InvalidValueException if the value is out of range
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="message">The message.</param>
        internal void SetValue(double value, string message)
        {
            if (!this.CanWrite)
            {
                throw new ASCOM.MethodNotImplementedException(string.Format("{0} cannot be written and", this.Name));
            }
            if (value < Minimum || value > Maximum)
            {
                throw new ASCOM.InvalidValueException("Switch " + this.Name, value.ToString(), string.Format("{0} to {1}", Minimum, Maximum));
            }

            // Wait for the switch change duration if necessary
            if (Duration > 0.0)
            {
                // Assign variables
                Stopwatch sw = Stopwatch.StartNew();

                // Wait for any delay period, finishing early if cancelled
                do
                {
                    Thread.Sleep(100);
                } while ((sw.Elapsed.TotalSeconds < Duration) & !CancellationToken.IsCancellationRequested);

                // Return if the operation was cancelled before completion
                if (CancellationToken.IsCancellationRequested)
                    return;
            }

            // set the value to the closest switch step value.
            var val = Math.Round((value - Minimum) / StepSize);
            val = StepSize * val + Minimum;
            this.Value = val;
        }

        /// <summary>
        /// Saves the switch to the ASCOM profile
        /// </summary>
        /// <param name="profile">The profile.</param>
        /// <param name="driverId">The driver id.</param>
        /// <param name="id">The id.</param>
        internal void Save(IProfile profile, String driverId, int id)
        {
            var subKey = "Switch " + id.ToString();
            profile.WriteValue(driverId, "Name", subKey, this.Name);
            profile.WriteValue(driverId, "Description", subKey, this.Description);
            profile.WriteValue(driverId, "Minimum", subKey, this.Minimum.ToString(CultureInfo.InvariantCulture));
            profile.WriteValue(driverId, "Maximum", subKey, this.Maximum.ToString(CultureInfo.InvariantCulture));
            profile.WriteValue(driverId, "StepSize", subKey, this.StepSize.ToString(CultureInfo.InvariantCulture));
            profile.WriteValue(driverId, "CanWrite", subKey, this.CanWrite.ToString(CultureInfo.InvariantCulture));
            profile.WriteValue(driverId, "Value", subKey, this.Value.ToString(CultureInfo.InvariantCulture));
            profile.WriteValue(driverId, "CanAsync", subKey, this.CanAsync.ToString(CultureInfo.InvariantCulture));
            profile.WriteValue(driverId, "Duration", subKey, this.Duration.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Determines if the switch parameters are valid, returns false if not with the reason.
        /// </summary>
        /// <param name="reason">The reason this switch is invalid</param>
        /// <returns>
        ///   <c>true</c> if the switch parameters are valid; otherwise, <c>false</c>.
        /// </returns>
        public bool IsValid(out string reason)
        {
            return IsValid(this.Name, this.Maximum, this.Minimum, this.StepSize, this.Value, out reason);
        }

        ///// <summary>
        ///// Determines whether the specified row of cells contains a valid LocalSwitch definition.
        ///// </summary>
        ///// <param name="cells">The cells.</param>
        ///// <param name="reason">The reason.</param>
        ///// <returns>
        /////   <c>true</c> if the specified cells is valid; otherwise, <c>false</c>.
        ///// </returns>
        //internal static bool IsValid(System.Windows.Forms.DataGridViewCellCollection cells, out string reason)
        //{
        //    var name = (string)cells["switchName"].Value;
        //    var minimum = Convert.ToDouble(cells["colMin"].Value);
        //    var maximum = Convert.ToDouble(cells["colMax"].Value);
        //    var stepSize = Convert.ToDouble(cells["colStep"].Value);
        //    var value = Convert.ToDouble(cells["colValue"].Value);
        //    if (!IsValid(name, maximum, minimum, stepSize, value, out reason))
        //    {
        //        return false;
        //    }
        //    reason = string.Empty;
        //    return true;
        //}

        private static bool IsValid(string name, double max, double min, double step, double value, out string reason)
        {
            if (string.IsNullOrEmpty(name))
            {
                reason = "no switch device name is defined";
                return false;
            }
            if (min >= max)
            {
                reason = $"the Maximum ({max}) not greater than Minimum ({min})";
                return false;
            }
            if (step <= 0)
            {
                reason = "the Step size must be greater than zero";
                return false;
            }
            if ((max - min) / step < 1)
            {
                reason = "the Step size gives less than two states";
                return false;
            }
            if (Math.Abs(Math.IEEERemainder((max - min) / step, 1.0)) > step / 10)
            {
                reason = "the number of states is not an integer.";
                return false;
            }
            if (value < min || value > max)
            {
                reason = $"the Value ({value}) is not between Minimum ({min}) and Maximum ({max})";
                return false;
            }
            reason = string.Empty;
            return true;
        }
    }
}