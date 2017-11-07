namespace Machina
{
    public enum NetworkMonitorType
    {
        RawSocket = 1,
        WinPCap = 2
    }

    public delegate void DataReceivedDelegate(string connection, byte[] data);

    public delegate void DataSentDelegate(string connection, byte[] data);

    /// <summary>
    /// ITCPNetworkMonitor is configured through the following properties after it is constructed:
    ///   MonitorType: Specifies whether it should use a winsock raw socket, or use WinPCap (requires separate kernel driver installation).  Default is a raw socket.
    ///   DataReceived: Delegate that is called when data is received and successfully decoded through IP and TCP decoders.  Note that a connection identifier is 
    ///     supplied to distinguish between multiple connections from the same process.
    ///   DataSent: Delegate that is called when data is sent and successfully decoded through IP and TCP decoders.  Note that a connection identifier is 
    ///     supplied to distinguish between multiple connections from the same process.
    /// </summary>
    public interface ITCPNetworkMonitor
    {
        /// <summary>
        /// Specifies the type of monitor to use - Raw socket or WinPCap
        /// </summary>
        NetworkMonitorType MonitorType { get; set; }

        /// <summary>
        /// Specifies the delegate that is called when data is received and successfully decoded
        /// </summary>
        event DataReceivedDelegate DataReceived;

        event DataSentDelegate DataSent;

        /// <summary>
        /// Validates the parameters and starts the monitor.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the monitor if it is active.
        /// </summary>
        void Stop();
    }
}
