// ASCOM Switch hardware class for mytjaAstroBox
//
// Description:	 ASCOM driver for Event Horizon AstroBox - a powerful OSHW astronomy power management hub
//
// Implements:	ASCOM Switch interface version: 2.0 (I think)
// Author:		Mitja Ševerkar <mitja@severkar.eu>
//

using System;
using ASCOM;
using ASCOM.DeviceInterface;
using ASCOM.Astrometry.AstroUtils;
using ASCOM.Utilities;
using System.Collections;
using System.Windows.Forms;
using ASCOM.Simulators;
using System.Collections.Generic;
using System.IO.Ports;
using System.Runtime.Remoting.Messaging;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Media;
using System.Timers;
using System.Globalization;


namespace ASCOM.mytjaAstroBox.Switch
{
    //
    // TODO Replace the not implemented exceptions with code to implement the function or throw the appropriate ASCOM exception.
    //

    /// <summary>
    /// ASCOM Switch hardware class for mytjaAstroBox.
    /// </summary>
    [HardwareClass()] // Class attribute flag this as a device hardware class that needs to be disposed by the local server when it exits.
    internal static class SwitchHardware
    {
        // Constants used for Profile persistence
        internal static string autoDetectComPortProfileName = "Auto-Detect COM Port";
        internal static string autoDetectComPortDefault = "true";
        internal static string comPortProfileName = "COM Port";
        internal static string comPortDefault = "COM1";
        internal static string traceStateProfileName = "Trace Level";
        internal static string traceStateDefault = "false";
        internal static bool showGps = true;

        const bool ENABLE_GPS = true;

        // Variables to hold the current device configuration
        internal static bool autoDetectComPort = Convert.ToBoolean(autoDetectComPortDefault);
        //internal static bool autoDetectComPort = false;
        internal static string comPortOverride = comPortDefault;

        private static string DriverProgId = ""; // ASCOM DeviceID (COM ProgID) for this driver, the value is set by the driver's class initialiser.
        private static string DriverDescription = "ASCOM driver for Event Horizon AstroBox - a powerful OSHW astronomy power management hub"; // The value is set by the driver's class initialiser.
        internal static string comPort; // COM port name (if required)
        private static bool connectedState; // Local server's connected state
        private static bool runOnce = false; // Flag to enable "one-off" activities only to run once.
        internal static Util utilities; // ASCOM Utilities object for use as required
        internal static AstroUtils astroUtilities; // ASCOM AstroUtilities object for use as required
        internal static TraceLogger tl; // Local server's trace logger object for diagnostic log with information that you specify

        //internal static Mutex mutex;
        internal static Task task;
        internal static CancellationTokenSource cts = new CancellationTokenSource();

        // The object used to communicate with the device using serial port communication.
        private static Serial objSerial;

        /// <summary>
        /// Initializes a new instance of the device Hardware class.
        /// </summary>
        static SwitchHardware()
        {
            try
            {
                // Create the hardware trace logger in the static initialiser.
                // All other initialisation should go in the InitialiseHardware method.
                tl = new TraceLogger("", "mytjaAstroBox.Hardware");

                // DriverProgId has to be set here because it used by ReadProfile to get the TraceState flag.
                DriverProgId = Switch.DriverProgId; // Get this device's ProgID so that it can be used to read the Profile configuration values

                // ReadProfile has to go here before anything is written to the log because it loads the TraceLogger enable / disable state.
                ReadProfile(); // Read device configuration from the ASCOM Profile store, including the trace state

                LogMessage("SwitchHardware", $"Static initialiser completed.");
            }
            catch (Exception ex)
            {
                try { LogMessage("SwitchHardware", $"Initialisation exception: {ex}"); } catch { }
                MessageBox.Show($"{ex.Message}", "Exception creating ASCOM.mytjaAstroBox.Switch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw;
            }
        }

        /// <summary>
        /// Place device initialisation code here
        /// </summary>
        /// <remarks>Called every time a new instance of the driver is created.</remarks>
        internal static void InitialiseHardware()
        {
            // This method will be called every time a new ASCOM client loads your driver
            LogMessage("InitialiseHardware", $"Start.");

            // Make sure that "one off" activities are only undertaken once
            if (runOnce == false)
            {
                LogMessage("InitialiseHardware", $"Starting one-off initialisation.");

                DriverDescription = Switch.DriverDescription; // Get this device's Chooser description

                LogMessage("InitialiseHardware", $"ProgID: {DriverProgId}, Description: {DriverDescription}");

                connectedState = false; // Initialise connected to false
                utilities = new Util(); //Initialise ASCOM Utilities object
                astroUtilities = new AstroUtils(); // Initialise ASCOM Astronomy Utilities object

                LogMessage("InitialiseHardware", "Completed basic initialisation");

                // Add your own "one off" device initialisation here e.g. validating existence of hardware and setting up communications

                LogMessage("InitialiseHardware", $"One-off initialisation complete.");
                runOnce = true; // Set the flag to ensure that this code is not run again
            }
        }

        // PUBLIC COM INTERFACE ISwitchV3 IMPLEMENTATION

        #region Common properties and methods.

        /// <summary>
        /// Displays the Setup Dialogue form.
        /// If the user clicks the OK button to dismiss the form, then
        /// the new settings are saved, otherwise the old values are reloaded.
        /// THIS IS THE ONLY PLACE WHERE SHOWING USER INTERFACE IS ALLOWED!
        /// </summary>
        public static void SetupDialog()
        {
            // Don't permit the setup dialogue if already connected
            if (IsConnected)
                MessageBox.Show("Already connected, just press OK");

            using (SetupDialogForm F = new SetupDialogForm(tl))
            {
                var result = F.ShowDialog();
                if (result == DialogResult.OK)
                {
                    WriteProfile(); // Persist device configuration values to the ASCOM Profile store
                }
            }
        }

        /// <summary>Returns the list of custom action names supported by this driver.</summary>
        /// <value>An ArrayList of strings (SafeArray collection) containing the names of supported actions.</value>
        public static ArrayList SupportedActions
        {
            get
            {
                LogMessage("SupportedActions Get", "Returning empty ArrayList");
                return new ArrayList();
            }
        }

        /// <summary>Invokes the specified device-specific custom action.</summary>
        /// <param name="ActionName">A well known name agreed by interested parties that represents the action to be carried out.</param>
        /// <param name="ActionParameters">List of required parameters or an <see cref="String.Empty">Empty String</see> if none are required.</param>
        /// <returns>A string response. The meaning of returned strings is set by the driver author.
        /// <para>Suppose filter wheels start to appear with automatic wheel changers; new actions could be <c>QueryWheels</c> and <c>SelectWheel</c>. The former returning a formatted list
        /// of wheel names and the second taking a wheel name and making the change, returning appropriate values to indicate success or failure.</para>
        /// </returns>
        public static string Action(string actionName, string actionParameters)
        {
            LogMessage("Action", $"Action {actionName}, parameters {actionParameters} is not implemented");
            throw new ActionNotImplementedException("Action " + actionName + " is not implemented by this driver");
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and does not wait for a response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        public static void CommandBlind(string command, bool raw)
        {
            CheckConnected("CommandBlind");
            // TODO The optional CommandBlind method should either be implemented OR throw a MethodNotImplementedException
            // If implemented, CommandBlind must send the supplied command to the mount and return immediately without waiting for a response

            throw new MethodNotImplementedException($"CommandBlind - Command:{command}, Raw: {raw}.");
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and waits for a boolean response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        /// <returns>
        /// Returns the interpreted boolean response received from the device.
        /// </returns>
        public static bool CommandBool(string command, bool raw)
        {
            CheckConnected("CommandBool");
            // TODO The optional CommandBool method should either be implemented OR throw a MethodNotImplementedException
            // If implemented, CommandBool must send the supplied command to the mount, wait for a response and parse this to return a True or False value

            throw new MethodNotImplementedException($"CommandBool - Command:{command}, Raw: {raw}.");
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and waits for a string response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        /// <returns>
        /// Returns the string response received from the device.
        /// </returns>
        public static string CommandString(string command, bool raw)
        {
            CheckConnected("CommandString");
            // TODO The optional CommandString method should either be implemented OR throw a MethodNotImplementedException
            // If implemented, CommandString must send the supplied command to the mount and wait for a response before returning this to the client

            throw new MethodNotImplementedException($"CommandString - Command:{command}, Raw: {raw}.");
        }

        /// <summary>
        /// Deterministically release both managed and unmanaged resources that are used by this class.
        /// </summary>
        /// <remarks>
        /// 
        /// Do not call this method from the Dispose method in your driver class.
        ///
        /// This is because this hardware class is decorated with the <see cref="HardwareClassAttribute"/> attribute and this Dispose() method will be called 
        /// automatically by the  local server executable when it is irretrievably shutting down. This gives you the opportunity to release managed and unmanaged 
        /// resources in a timely fashion and avoid any time delay between local server close down and garbage collection by the .NET runtime.
        ///
        /// For the same reason, do not call the SharedResources.Dispose() method from this method. Any resources used in the static shared resources class
        /// itself should be released in the SharedResources.Dispose() method as usual. The SharedResources.Dispose() method will be called automatically 
        /// by the local server just before it shuts down.
        /// 
        /// </remarks>
        public static void Dispose()
        {
            try { LogMessage("Dispose", $"Disposing of assets and closing down."); } catch { }

            try
            {
                // Clean up the trace logger and utility objects
                tl.Enabled = false;
                tl.Dispose();
                tl = null;
            }
            catch { }

            try
            {
                utilities.Dispose();
                utilities = null;
            }
            catch { }

            try
            {
                astroUtilities.Dispose();
                astroUtilities = null;
            }
            catch { }

            try
            {
                objSerial.Connected = false;
            }
            catch { }

            try
            {
                cts.Cancel();
            }
            catch { }

            try
            {
                task.Dispose();
            }
            catch { }
        }

        /// <summary>
        /// Set True to connect to the device hardware. Set False to disconnect from the device hardware.
        /// You can also read the property to check whether it is connected. This reports the current hardware state.
        /// </summary>
        /// <value><c>true</c> if connected to the hardware; otherwise, <c>false</c>.</value>
        public static bool Connected
        {
            get
            {
                LogMessage("Connected", $"Get {IsConnected}");
                return IsConnected;
            }
            set
            {
                LogMessage("Connected", $"Set {value}");
                if (value == IsConnected)
                    return;

                if (value)
                {
                    objSerial = DetectCOMPort();
                    if (objSerial == null)
                    {
                        connectedState = false;
                        throw new ASCOM.NotConnectedException("Failed to connect");
                    }

                    for (int id = 0; id < switches.Count; id++)
                    {
                        if (switches[id].IsCommand)
                        {
                            switches[id].Value = 0.0;
                            continue;
                        }
                        if (switches[id].InternalID == "DC1_VOLTAGE")
                        {
                            objSerial.Transmit($"VGET\n"); // TODO: vget komanda?
                            switches[id].Value = int.Parse(objSerial.ReceiveTerminated("\n").Trim());
                            switches[id].InSync = true;
                            continue;
                        }
                        if (switches[id].InternalID == "AUTODEW") continue;
                        if (!switches[id].InternalID.StartsWith("DEW") || switches[id].InternalID.Contains("_"))
                        {
                            continue;
                        }
                        objSerial.Transmit($"PWMGET;{switches[id].InternalID}\n");
                        switches[id].Value = ((double)int.Parse(objSerial.ReceiveTerminated("\n").Trim()) / 65535.0) * 100.0; // TODO: resolution of esp32-s3
                        switches[id].InSync = true;
                    }

                    for (int id = 0; id < switches.Count; id++)
                    {
                        if (switches[id].IsCommand)
                        {
                            switches[id].Value = 0.0;
                            continue;
                        }
                        if (!(switches[id].InternalID.StartsWith("DC") || switches[id].InternalID.StartsWith("UH") || switches[id].InternalID == "AUTODEW") || switches[id].InternalID.Contains("_"))
                        { // TODO: STATEGET ne podpira UH-jev
                            continue;
                        }
                        objSerial.Transmit($"STATEGET;{switches[id].InternalID}\n");
                        switches[id].Value = int.Parse(objSerial.ReceiveTerminated("\n").Trim());
                        switches[id].InSync = true;
                    }

                    /*
                    for (int id = 0; id < switches.Count; id++)
                    {
                        if (!switches[id].InternalID.StartsWith("DC") && !switches[id].InternalID.StartsWith("DEW"))
                        {
                            continue;
                        }
                        if (switches[id].InternalID.Contains("_"))
                        {
                            continue;
                        }
                        objSerial.Transmit($"NAMEGET;{switches[id].InternalID}\n");
                        String name = objSerial.ReceiveTerminated("\n").Trim();
                        if (name == "")
                        {
                            continue;
                        }

                        switches[id].Name = name;
                        switches[id].InSync = true;
                    }
                    */

                    connectedState = true;

                    //mutex = new Mutex();

                    //Task.Run(DataReceived);
                    //thread1 = new Thread(DataReceived);

                    task = Task.Run(UpdateFrequent);
                }
                else
                {
                    connectedState = false;

                    LogMessage("Connected Set", "Disconnecting from port {0}", comPort);

                    objSerial.Connected = false;
                    objSerial.Dispose();
                    objSerial = null;
                    //thread1.Abort();
                }
            }
        }

        public async static void UpdateFrequent()
        {
            while (true)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(500);
                cts.Token.ThrowIfCancellationRequested();

                // Potrebno za Gyro
                //objSerial.Transmit($"REFRESH_DATA\n");
                //objSerial.ReceiveTerminated("\n").Trim();
                //await Task.Delay(1000);

                for (int i = 0; i < switches.Count; i++)
                {
                    if (!switches[i].RegularFetch) continue;
                    //mutex.WaitOne();
                    try
                    {
                        objSerial.Transmit($"ADCGET;{switches[i].InternalID}\n");
                        int digits = 2;
                        if (switches[i].InternalID.Contains("GPS")) digits = 5;
                        // CultureInfo.InvariantCulture je potreben, ker je C# retarded
                        switches[i].Value = Math.Round(float.Parse(objSerial.ReceiveTerminated("\n").Trim(), CultureInfo.InvariantCulture), digits);
                    }
                    catch (Exception er)
                    {
                        if (!objSerial.Connected) throw new NotConnectedException();
                        LogMessage("SchedPull", $"Scheduled pull failed on {switches[i].InternalID} with exception {er}");
                    }
                    //mutex.ReleaseMutex();
                }
            }
        }

        /// <summary>
        /// Returns a description of the device, such as manufacturer and model number. Any ASCII characters may be used.
        /// </summary>
        /// <value>The description.</value>
        public static string Description
        {
            get
            {
                LogMessage("Description Get", DriverDescription);
                return DriverDescription;
            }
        }

        /// <summary>
        /// Descriptive and version information about this ASCOM driver.
        /// </summary>
        public static string DriverInfo
        {
            get
            {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                // TODO customise this driver description if required
                string driverInfo = $"Information about the driver itself. Version: {version.Major}.{version.Minor}";
                LogMessage("DriverInfo Get", driverInfo);
                return driverInfo;
            }
        }

        /// <summary>
        /// A string containing only the major and minor version of the driver formatted as 'm.n'.
        /// </summary>
        public static string DriverVersion
        {
            get
            {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string driverVersion = $"{version.Major}.{version.Minor}";
                LogMessage("DriverVersion Get", driverVersion);
                return driverVersion;
            }
        }

        /// <summary>
        /// The interface version number that this device supports.
        /// </summary>
        public static short InterfaceVersion
        {
            // set by the driver wizard
            get
            {
                LogMessage("InterfaceVersion Get", "2");
                return Convert.ToInt16("2");
            }
        }

        /// <summary>
        /// The short name of the driver, for display purposes
        /// </summary>
        public static string Name
        {
            get
            {
                string name = "AstroBox";
                LogMessage("Name Get", name);
                return name;
            }
        }

        #endregion

        internal static List<LocalSwitch> allSwitches;
        internal static List<LocalSwitch> switches;

        internal static void Send(string value)
        {
            //mut.WaitOne();
            objSerial.Transmit(value);
            //mut.ReleaseMutex();
        }

        #region ISwitchV2 Implementation

        private static short numSwitch = 16;

        /// <summary>
        /// The number of switches managed by this driver
        /// </summary>
        /// <returns>The number of devices managed by this driver.</returns>
        internal static short MaxSwitch
        {
            get
            {
                LogMessage("MaxSwitch Get", numSwitch.ToString());
                return (short)switches.Count;
            }
        }

        /// <summary>
        /// Return the name of switch device n.
        /// </summary>
        /// <param name="id">The device number (0 to <see cref="MaxSwitch"/> - 1)</param>
        /// <returns>The name of the device</returns>
        internal static string GetSwitchName(short id)
        {
            Validate("GetSwitchName", id);
            return switches[id].Name;
        }

        /// <summary>
        /// Set a switch device name to a specified value.
        /// </summary>
        /// <param name="id">The device number (0 to <see cref="MaxSwitch"/> - 1)</param>
        /// <param name="name">The name of the device</param>
        internal static void SetSwitchName(short id, string name)
        {
            Validate("SetSwitchName", id);
            if (!CanWrite(id))
            {
                var str = $"SetSwitchName({id}) - Cannot Write";
                LogMessage("SetSwitchName", str);
                throw new MethodNotImplementedException(str);
            }
            switches[id].Name = name;
        }

        /// <summary>
        /// Gets the description of the specified switch device. This is to allow a fuller description of
        /// the device to be returned, for example for a tool tip.
        /// </summary>
        /// <param name="id">The device number (0 to <see cref="MaxSwitch"/> - 1)</param>
        /// <returns>
        /// String giving the device description.
        /// </returns>
        internal static string GetSwitchDescription(short id)
        {
            Validate("GetSwitchDescription", id);
            return switches[id].Description;
        }

        /// <summary>
        /// Reports if the specified switch device can be written to, default true.
        /// This is false if the device cannot be written to, for example a limit switch or a sensor.
        /// </summary>
        /// <param name="id">The device number (0 to <see cref="MaxSwitch"/> - 1)</param>
        /// <returns>
        /// <c>true</c> if the device can be written to, otherwise <c>false</c>.
        /// </returns>
        internal static bool CanWrite(short id)
        {
            Validate("CanWrite", id);
            return switches[id].CanWrite;
        }

        #region Boolean switch members

        /// <summary>
        /// Return the state of switch device id as a boolean
        /// </summary>
        /// <param name="id">The device number (0 to <see cref="MaxSwitch"/> - 1)</param>
        /// <returns>True or false</returns>
        internal static bool GetSwitch(short id)
        {
            Validate("GetSwitch", id);
            bool t = switches[id].Maximum - switches[id].Value <= switches[id].Value - switches[id].Minimum;
            LogMessage("GetSwitch", $"Switch bool requested {switches[id].InternalID}: {switches[id].Value} ({t})");
            // Sends true if Value is closer to maximum than to minimum.
            return t;
        }

        /// <summary>
        /// Sets a switch controller device to the specified state, true or false.
        /// </summary>
        /// <param name="id">The device number (0 to <see cref="MaxSwitch"/> - 1)</param>
        /// <param name="state">The required control state</param>
        internal static void SetSwitch(short id, bool state)
        {
            Validate("SetSwitch", id);
            LogMessage("SetSwitch", $"Set switch bool called {switches[id].InternalID}: {switches[id].Value} -> {state}");
            if (!CanWrite(id))
            {
                var str = $"SetSwitch({id}) - Cannot Write";
                LogMessage("SetSwitch", str);
                throw new MethodNotImplementedException(str);
            }
            LogMessage("SetSwitch", $"Set switch state {id}");
            //mutex.WaitOne();

            if (switches[id].IsCommand)
            {
                Send($"{switches[id].InternalID}\n");
                if (objSerial.ReceiveTerminated("\n").Trim().StartsWith("OK"))
                {
                    switches[id].Value = 1;
                    Thread.Sleep(1000);
                    switches[id].Value = 0;
                }
                return;
            }

            if (state) Send($"ON;{switches[id].InternalID}\n");
            else Send($"OFF;{switches[id].InternalID}\n");
            if (objSerial.ReceiveTerminated("\n").Trim() == "OK")
            {
                switches[id].Value = state ? 1 : 0;
            }
            //mutex.ReleaseMutex();
        }

        #endregion

        #region Analogue members

        /// <summary>
        /// Returns the maximum value for this switch device, this must be greater than <see cref="MinSwitchValue"/>.
        /// </summary>
        /// <param name="id">The device number (0 to <see cref="MaxSwitch"/> - 1)</param>
        /// <returns>The maximum value to which this device can be set or which a read only sensor will return.</returns>
        internal static double MaxSwitchValue(short id)
        {
            Validate("MaxSwitchValue", id);
            return switches[id].Maximum;
        }

        /// <summary>
        /// Returns the minimum value for this switch device, this must be less than <see cref="MaxSwitchValue"/>
        /// </summary>
        /// <param name="id">The device number (0 to <see cref="MaxSwitch"/> - 1)</param>
        /// <returns>The minimum value to which this device can be set or which a read only sensor will return.</returns>
        internal static double MinSwitchValue(short id)
        {
            Validate("MinSwitchValue", id);
            return switches[id].Minimum;
        }

        /// <summary>
        /// Returns the step size that this device supports (the difference between successive values of the device).
        /// </summary>
        /// <param name="id">The device number (0 to <see cref="MaxSwitch"/> - 1)</param>
        /// <returns>The step size for this device.</returns>
        internal static double SwitchStep(short id)
        {
            Validate("SwitchStep", id);
            return switches[id].StepSize;
        }

        /// <summary>
        /// Returns the value for switch device id as a double
        /// </summary>
        /// <param name="id">The device number (0 to <see cref="MaxSwitch"/> - 1)</param>
        /// <returns>The value for this switch, this is expected to be between <see cref="MinSwitchValue"/> and
        /// <see cref="MaxSwitchValue"/>.</returns>
        internal static double GetSwitchValue(short id)
        {
            Validate("GetSwitchValue", id);
            LogMessage("GetSwitchValue", $"Switch value requested {switches[id].InternalID}: {switches[id].Value}");
            return switches[id].Value;
        }

        /// <summary>
        /// Set the value for this device as a double.
        /// </summary>
        /// <param name="id">The device number (0 to <see cref="MaxSwitch"/> - 1)</param>
        /// <param name="value">The value to be set, between <see cref="MinSwitchValue"/> and <see cref="MaxSwitchValue"/></param>
        internal static void SetSwitchValue(short id, double value)
        {
            Validate("SetSwitchValue", id, value);
            LogMessage("SetSwitchValue", $"Set switch value called {switches[id].InternalID}: {switches[id].Value} -> {value}");
            if (!CanWrite(id))
            {
                LogMessage("SetSwitchValue", $"SetSwitchValue({id}) - Cannot write");
                throw new ASCOM.MethodNotImplementedException($"SetSwitchValue({id}) - Cannot write");
            }
            if (switches[id].Maximum == 1.0)
            {
                SetSwitch(id, value == 1.0);
                return;
            }
            //mutex.WaitOne();
            string msg = $"PWMSET;{switches[id].InternalID};{Math.Round(((double)value / 100.0) * 65535)}\n";
            Send(msg);
            if (objSerial.ReceiveTerminated("\n").Trim() == "OK")
            {
                switches[id].Value = value;
            }
            for (int i = 0; i < switches.Count; i++)
            {
                if (switches[i].InternalID != "AUTODEW") continue;
                switches[i].Value = 0;
            }
            //mutex.ReleaseMutex();
        }

        #endregion

        #endregion

        #region Private methods

        /// <summary>
        /// Checks that the switch id is in range and throws an InvalidValueException if it isn't
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="id">The id.</param>
        private static void Validate(string message, short id)
        {
            if (id < 0 || id >= numSwitch)
            {
                LogMessage(message, string.Format("Switch {0} not available, range is 0 to {1}", id, numSwitch - 1));
                throw new InvalidValueException(message, id.ToString(), string.Format("0 to {0}", numSwitch - 1));
            }
        }

        /// <summary>
        /// Checks that the switch id and value are in range and throws an
        /// InvalidValueException if they are not.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="id">The id.</param>
        /// <param name="value">The value.</param>
        private static void Validate(string message, short id, double value)
        {
            Validate(message, id);
            var min = MinSwitchValue(id);
            var max = MaxSwitchValue(id);
            if (value < min || value > max)
            {
                LogMessage(message, string.Format("Value {1} for Switch {0} is out of the allowed range {2} to {3}", id, value, min, max));
                throw new InvalidValueException(message, value.ToString(), string.Format("Switch({0}) range {1} to {2}", id, min, max));
            }
        }

        #endregion

        #region Private properties and methods
        // Useful methods that can be used as required to help with driver development

        /// <summary>
        /// Returns true if there is a valid connection to the driver hardware
        /// </summary>
        private static bool IsConnected
        {
            get
            {
                // TODO check that the driver hardware connection exists and is connected to the hardware
                return connectedState;
            }
        }

        /// <summary>
        /// Use this function to throw an exception if we aren't connected to the hardware
        /// </summary>
        /// <param name="message"></param>
        private static void CheckConnected(string message)
        {
            if (!IsConnected)
            {
                throw new NotConnectedException(message);
            }
        }

        /// <summary>
        /// Read the device configuration from the ASCOM Profile store
        /// </summary>
        internal static void ReadProfile()
        {
            using (Profile driverProfile = new Profile())
            {
                LogMessage("ReadProfile", "Reading profile...");
                driverProfile.DeviceType = "Switch";
                tl.Enabled = Convert.ToBoolean(driverProfile.GetValue(DriverProgId, traceStateProfileName, string.Empty, traceStateDefault));
                comPort = driverProfile.GetValue(DriverProgId, comPortProfileName, string.Empty, comPortDefault);
                showGps = bool.Parse(driverProfile.GetValue(DriverProgId, "gps_show", string.Empty, "true"));

                switches = new List<LocalSwitch>();
                allSwitches = LoadDefaultSwitches();
                for (int i = 0; i < allSwitches.Count; i++)
                {
                    if (!showGps && allSwitches[i].InternalID.Contains("GPS"))
                    {
                        continue;
                    }
                    bool hide;
                    if (allSwitches[i].IsCommand)
                    {
                        hide = bool.Parse(driverProfile.GetValue(DriverProgId, $"{allSwitches[i].InternalID}_hide", string.Empty, "true"));
                        allSwitches[i].Hide = hide;
                        if (!hide) switches.Add(allSwitches[i]);
                        continue;
                    }
                    if (!allSwitches[i].CanSetName)
                    {
                        switches.Add(allSwitches[i]);
                        continue;
                    }
                    allSwitches[i].Name = driverProfile.GetValue(DriverProgId, $"{allSwitches[i].InternalID}_name", string.Empty, allSwitches[i].Name);
                    hide = bool.Parse(driverProfile.GetValue(DriverProgId, $"{allSwitches[i].InternalID}_hide", string.Empty, "false"));
                    allSwitches[i].Hide = hide;
                    if (!hide) switches.Add(allSwitches[i]);
                }
                numSwitch = (short)switches.Count;

            }
        }

        /// <summary>
        /// Write the device configuration to the  ASCOM  Profile store
        /// </summary>
        internal static void WriteProfile()
        {
            using (Profile driverProfile = new Profile())
            {
                driverProfile.DeviceType = "Switch";
                driverProfile.WriteValue(DriverProgId, traceStateProfileName, tl.Enabled.ToString());
                driverProfile.WriteValue(DriverProgId, comPortProfileName, comPort.ToString());
                for (int i = 0; i < allSwitches.Count; i++) {
                    if (allSwitches[i].IsCommand)
                    {
                        driverProfile.WriteValue(DriverProgId, $"{allSwitches[i].InternalID}_hide", allSwitches[i].Hide.ToString());
                        continue;
                    }
                    if (!allSwitches[i].CanSetName)
                    {
                        continue;
                    }
                    driverProfile.WriteValue(DriverProgId, $"{allSwitches[i].InternalID}_name", allSwitches[i].Name);
                    driverProfile.WriteValue(DriverProgId, $"{allSwitches[i].InternalID}_hide", allSwitches[i].Hide.ToString());
                }
                driverProfile.WriteValue(DriverProgId, "gps_show", showGps.ToString());
            }
        }

        public static List<LocalSwitch> LoadDefaultSwitches()
        {
            List<LocalSwitch > switches = new List<LocalSwitch>();
            switches.Add(new LocalSwitch("DC1", "DC1", true) { Description = "DC1 switch" });
            switches.Add(new LocalSwitch("DC1 Voltage [mV]", "DC1_VOLTAGE", false, 25_000, 2000, 100, 13_000, true, false) { Description = "Boost converter output voltage for DC1 in millivolts; from 2.000mV to 25.000mV" });
            switches.Add(new LocalSwitch("DC2", "DC2", true) { Description = "DC2 switch" });
            switches.Add(new LocalSwitch("DC3", "DC3", true) { Description = "DC3 switch" });
            switches.Add(new LocalSwitch("DC4", "DC4", true) { Description = "DC4 switch" });
            switches.Add(new LocalSwitch("DC5", "DC5", true) { Description = "DC5 switch" });
            switches.Add(new LocalSwitch("DEW1 [%]", "DEW1", true, 100, 0, 0.01, 0, true, true) { Description = "Dew heater 1; adjustable PWM output; from 0 to 100" });
            switches.Add(new LocalSwitch("DEW2 [%]", "DEW2", true, 100, 0, 0.01, 0, true, true) { Description = "Dew heater 2; adjustable PWM output; from 0 to 100" });
            switches.Add(new LocalSwitch("DEW3 [%]", "DEW3", true, 100, 0, 0.01, 0, true, true) { Description = "Dew heater 3; adjustable PWM output; from 0 to 100" });
            switches.Add(new LocalSwitch("UH1", "UH1", true) { Description = "USB hub 1" });
            switches.Add(new LocalSwitch("UH2", "UH2", true) { Description = "USB hub 2" });
            switches.Add(new LocalSwitch("UH3", "UH3", true) { Description = "USB hub 3" });
            switches.Add(new LocalSwitch("UH4", "UH4", true) { Description = "USB hub 4" });
            switches.Add(new LocalSwitch("UH5", "UH5", true) { Description = "USB hub 5" });
            switches.Add(new LocalSwitch("UH6", "UH6", true) { Description = "USB hub 6" });
            switches.Add(new LocalSwitch("Auto-dew", "AUTODEW", false) { Description = "Turns on or off the auto-dew functionality." });
            switches.Add(new LocalSwitch("Input Voltage [V]", "INPUT_VOLTAGE", false, 16.0, 0.0, 0.01, 0.0, false, true) { Description = "Generic Power switch" });
            switches.Add(new LocalSwitch("Input Current [A]", "INPUT_CURRENT", false, 30.0, 0.0, 0.01, 0.0, false, true));
            switches.Add(new LocalSwitch("Output Current (DC1) [A]", "DC1_CURRENT", false, 30.0, 0.0, 0.01, 0.0, false, true));
            switches.Add(new LocalSwitch("Output Current (DC2) [A]", "DC2_CURRENT", false, 30.0, 0.0, 0.01, 0.0, false, true));
            switches.Add(new LocalSwitch("Output Current (DC3) [A]", "DC3_CURRENT", false, 30.0, 0.0, 0.01, 0.0, false, true));
            switches.Add(new LocalSwitch("Output Current (DC4) [A]", "DC4_CURRENT", false, 30.0, 0.0, 0.01, 0.0, false, true));
            switches.Add(new LocalSwitch("Output Current (DC5) [A]", "DC5_CURRENT", false, 30.0, 0.0, 0.01, 0.0, false, true));
            switches.Add(new LocalSwitch("Output Current (DEW1) [A]", "DEW1_CURRENT", false, 5.0, 0.0, 0.01, 0.0, false, true));
            switches.Add(new LocalSwitch("Output Current (DEW2) [A]", "DEW2_CURRENT", false, 5.0, 0.0, 0.01, 0.0, false, true));
            switches.Add(new LocalSwitch("Output Current (DEW3) [A]", "DEW3_CURRENT", false, 5.0, 0.0, 0.01, 0.0, false, true));
            switches.Add(new LocalSwitch("Temperature (EXT1) [°C]", "EXT1_ANALOG_TEMP", true, 60.0, -40.0, 0.1, 0.0, false, true));
            switches.Add(new LocalSwitch("Temperature (EXT2) [°C]", "EXT2_ANALOG_TEMP", true, 60.0, -40.0, 0.1, 0.0, false, true));
            switches.Add(new LocalSwitch("Temperature (EXT3) [°C]", "EXT3_ANALOG_TEMP", true, 60.0, -40.0, 0.1, 0.0, false, true));
            switches.Add(new LocalSwitch("Temperature (SHT1) [°C]", "SHT3X1_TEMP", true, 60.0, -40.0, 0.1, 0.0, false, true));
            switches.Add(new LocalSwitch("Temperature (SHT2) [°C]", "SHT3X2_TEMP", true, 60.0, -40.0, 0.1, 0.0, false, true));
            switches.Add(new LocalSwitch("Temperature (SHT3) [°C]", "SHT3X3_TEMP", true, 60.0, -40.0, 0.1, 0.0, false, true));
            switches.Add(new LocalSwitch("CPU Temperature (ESP32-S3) [°C]", "CPU_TEMP", true, 80.0, -40.0, 0.1, 0.0, false, true));
            switches.Add(new LocalSwitch("Humidity (SHT1) [%]", "SHT3X1_HUM", true, 100.0, 0.0, 0.1, 0.0, false, true));
            switches.Add(new LocalSwitch("Humidity (SHT2) [%]", "SHT3X2_HUM", true, 100.0, 0.0, 0.1, 0.0, false, true));
            switches.Add(new LocalSwitch("Humidity (SHT3) [%]", "SHT3X3_HUM", true, 100.0, 0.0, 0.1, 0.0, false, true));
            switches.Add(new LocalSwitch("Dew Point [°C]", "DEW_POINT", true, 60.0, -40.0, 0.1, 0.0, false, true));
            switches.Add(new LocalSwitch("GPS Sleep mode", "GPS1", false) { Description = "When GPS is in sleep mode, it won't attempt to receive any coordinate data from satellites." });
            switches.Add(new LocalSwitch("Disable Fan", "FAN", false) { Description = "Beware that disabling the fan might lead to overheating, causing a shorter lifespan of components." });
            switches.Add(new LocalSwitch("GPS Latitude", "GPS1_LATITUDE", false, 90, -90, 0.0001, 0.0, false, ENABLE_GPS));
            switches.Add(new LocalSwitch("GPS Longitude", "GPS1_LONGITUDE", false, 180, -180, 0.0001, 0.0, false, ENABLE_GPS));
            switches.Add(new LocalSwitch("GPS Elevation [m]", "GPS1_ELEVATION", false, 7000, -20, 0.001, 0.0, false, ENABLE_GPS));
            switches.Add(new LocalSwitch("GPS Satellite Count", "GPS1_SATELLITE_COUNT", false, 30, 0, 1, 0, false, ENABLE_GPS));
            switches.Add(new LocalSwitch("Gyro X", "GYRO_X", true, -360, 360, 0.0001, 0.0, false, true));
            switches.Add(new LocalSwitch("Gyro Y", "GYRO_Y", true, -360, 360, 0.0001, 0.0, false, true));
            switches.Add(new LocalSwitch("Zero", "ZERO", false) { Description = "Upon clicking this switch, the power supply will zero the current sensors. Proceed with caution.", IsCommand = true });
            switches.Add(new LocalSwitch("BOOTSEL Mode", "BOOTSEL", false) { Description = "Upon clicking this switch, the power supply will reboot into BOOTSEL mode. Proceed with caution. This command WILL turn off all the connected devices.", IsCommand = true });
            switches.Add(new LocalSwitch("Reboot", "REBOOT", false) { Description = "Upon clicking this switch, the power supply will reboot (into normal mode). Proceed with caution. This command WILL turn off all the connected devices.", IsCommand = true });
            switches.Add(new LocalSwitch("Reset Core 1", "RESET_CORE", false) { Description = "Upon clicking this switch, the power supply will restart the second core (core 1). Proceed with caution. This can be useful if sensor values aren't updating.", IsCommand = true });
            return switches;
        }

        /*private static void DataReceived()
        {
            LogMessage("DataReceived", "Starting DataReceived thread");
            while (true)
            {
                //mut.WaitOne();
                var task = Task.Run(() => objSerial.ReceiveTerminated("\n"));
                if (!task.Wait(TimeSpan.FromMilliseconds(500)))
                {
                    //mut.ReleaseMutex();
                    continue;
                }
                //mut.ReleaseMutex();
                string indata = task.Result;
                LogMessage("DataReceived", $"Received data: {indata}");
                string[] msg = indata.Split(';');
                if (msg[0] == "ON")
                {
                    switches[internalIdMap[msg[1]]].Value = 1;
                }
                else if (msg[0] == "PWMSET")
                {
                    double map = (int.Parse(msg[2]) / 65535) * 100;
                    switches[internalIdMap[msg[1]]].Value = map;
                }
            }
        }*/

        internal static Serial DetectCOMPort()
        {
            LogMessage("DetectCOMPort", $"Serial Ports {String.Join(",", SerialPort.GetPortNames())}");
            foreach (string portName in SerialPort.GetPortNames())
            {
                LogMessage("DetectCOMPort", $"Trying serial port {portName}");
                Serial serial = null;

                try
                {
                    serial = new Serial
                    {
                        PortName = portName,
                        Speed = SerialSpeed.ps115200,
                        Parity = SerialParity.None,
                        DataBits = 8,
                        StopBits = SerialStopBits.One,
                        Handshake = SerialHandshake.None,
                        ReceiveTimeoutMs = 2000,
                        DTREnable = true,
                    };
                    serial.Connected = true;
                }
                catch (Exception e)
                {
                    LogMessage("DetectCOMPort", $"Exception thrown on Serial connect {e}");
                    // If trying to connect to a port that is already in use, an exception will be thrown.
                    continue;
                }

                // Wait a second for the serial connection to establish
                //System.Threading.Thread.Sleep(1000);

                serial.ClearBuffers();

                // Poll the device (with a short timeout value) until successful,
                // or until we've reached the retry count limit of 3...
                bool success = false;
                for (int retries = 3; retries > 0; retries--)
                {
                    string response = "";
                    try
                    {
                        serial.Transmit("FWINFO\n");
                        response = serial.ReceiveTerminated("\n").Trim();
                    }
                    catch (Exception e)
                    {
                        LogMessage("DetectCOMPort", $"Exception thrown on Serial transmit {e}");
                        continue;
                    }
                    LogMessage("DetectCOMPort", $"Response: {response}");
                    if (response.StartsWith("AstroBox-Pico"))
                    {
                        success = true;
                        break;
                    }
                }

                if (success)
                {
                    LogMessage("DetectCOMPort", $"Port {portName} OK!");
                    return serial;
                }

                serial.Connected = false;
                serial.Dispose();
            }

            return null;
        }

        /// <summary>
        /// Log helper function that takes identifier and message strings
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="message"></param>
        internal static void LogMessage(string identifier, string message)
        {
            tl.LogMessageCrLf(identifier, message);
        }

        /// <summary>
        /// Log helper function that takes formatted strings and arguments
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="message"></param>
        /// <param name="args"></param>
        internal static void LogMessage(string identifier, string message, params object[] args)
        {
            var msg = string.Format(message, args);
            LogMessage(identifier, msg);
        }
        #endregion
    }
}

