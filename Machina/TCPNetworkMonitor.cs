using System;

namespace Machina
{
    /// <summary>
    /// TCPNetworkMonitor is configured through the following properties after it is constructed:
    ///   MonitorType: Specifies whether it should use a winsock raw socket, or use WinPCap (requires separate kernel driver installation).  Default is a raw socket.
    ///   ProcessID: Specifies the process ID to record traffic from
    ///   WindowName: Specifies the window name to record traffic from, where process ID is unavailable
    ///   DataReceived: Delegate that is called when data is received and successfully decoded through IP and TCP decoders.  Note that a connection identifier is 
    ///     supplied to distinguish between multiple connections from the same process.
    ///   DataSent: Delegate that is called when data is sent and successfully decoded through IP and TCP decoders.  Note that a connection identifier is 
    ///     supplied to distinguish between multiple connections from the same process.
    ///     
    /// This class uses a long-running task to monitor the network data as it is received.  It also monitors the specified process for changes to its active
    ///   TCPIP connections and filters out all traffic not related to these connections
    ///   
    /// The stream of data sent to the remote server is considered separate from the stream of data sent by the remote server, and processed through different events.
    /// </summary>
    public class TCPNetworkMonitor : ITCPNetworkMonitor
    {
        private readonly TCPNetworkMonitorCore _monitorCore = new TCPNetworkMonitorCore();

        /// <summary>
        /// Specifies the type of monitor to use - Raw socket or WinPCap
        /// </summary>
        public NetworkMonitorType MonitorType
        {
            get => _monitorCore.MonitorType;
            set => _monitorCore.MonitorType = value;
        }

        /// <summary>
        /// Specifies the Process ID that is generating or receiving the traffic.  Either ProcessID or WindowName must be specified.
        /// </summary>
        public uint ProcessID
        { get; set; } = 0;

        /// <summary>
        /// Specifies the local IP address of the network interface to monitor
        /// </summary>
        public string LocalIP
        {
            get => _monitorCore.LocalIP;
            set => _monitorCore.LocalIP = value;
        }

        /// <summary>
        /// Specifies the Window Name of the application that is generating or receiving the traffic.  Either ProcessID or WindowName must be specified.
        /// </summary>
        public string WindowName
        { get; set; } = "";

        #region Data Delegates section

        public event DataReceivedDelegate DataReceived
        {
            add => _monitorCore.DataReceived += value;
            remove => _monitorCore.DataReceived -= value;
        }

        public event DataSentDelegate DataSent
        {
            add => _monitorCore.DataSent += value;
            remove => _monitorCore.DataSent -= value;
        }

        #endregion

        private readonly ProcessTCPInfo _processTCPInfo = new ProcessTCPInfo();

        /// <summary>
        /// Validates the parameters and starts the monitor.
        /// </summary>
        public void Start()
        {
            if (ProcessID == 0 && string.IsNullOrWhiteSpace(WindowName))
                throw new ArgumentException("Either Process ID or Window Name must be specified");

            _processTCPInfo.ProcessID = ProcessID;
            _processTCPInfo.ProcessWindowName = WindowName;

            _monitorCore.UpdateTCPConnections = _processTCPInfo.UpdateTCPIPConnections;
            _monitorCore.Start();
        }

        /// <summary>
        /// Stops the monitor if it is active.
        /// </summary>
        public void Stop()
        {
            _monitorCore.Stop();
        }
    }
}
