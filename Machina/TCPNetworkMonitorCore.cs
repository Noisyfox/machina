using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Machina
{
    /// <summary>
    /// TCPNetworkMonitorCore is configured through the following properties after it is constructed:
    ///   MonitorType: Specifies whether it should use a winsock raw socket, or use WinPCap (requires separate kernel driver installation).  Default is a raw socket.
    ///   UpdateTCPConnections: Delegate that is called during the processing loop for obtaining the connections need to be monitered.
    ///   DataReceived: Delegate that is called when data is received and successfully decoded through IP and TCP decoders.  Note that a connection identifier is 
    ///     supplied to distinguish between multiple connections from the same process.
    ///   DataSent: Delegate that is called when data is sent and successfully decoded through IP and TCP decoders.  Note that a connection identifier is 
    ///     supplied to distinguish between multiple connections from the same process.
    ///     
    /// This class uses a long-running task to monitor the network data as it is received.
    ///   
    /// The stream of data sent to the remote server is considered separate from the stream of data sent by the remote server, and processed through different events.
    /// </summary>
    public class TCPNetworkMonitorCore : ITCPNetworkMonitor
    {

        /// <summary>
        /// Specifies the type of monitor to use - Raw socket or WinPCap
        /// </summary>
        public NetworkMonitorType MonitorType
        { get; set; } = NetworkMonitorType.RawSocket;

        /// <summary>
        /// Specifies the local IP address of the network interface to monitor
        /// </summary>
        public string LocalIP
        { get; set; } = "";

        #region ConnectionUpdate Delegate Section

        public delegate void UpdateTCPConnectionsDelegate(List<TCPConnection> connections);

        /// <summary>
        /// Specifies the delegate that is called during the processing loop for obtaining the connections
        /// need to be monitered.
        /// </summary>
        public UpdateTCPConnectionsDelegate UpdateTCPConnections = null;

        #endregion

        #region Data Delegates section

        /// <summary>
        /// Specifies the delegate that is called when data is received and successfully decoded
        /// </summary>
        public event DataReceivedDelegate DataReceived;

        public void OnDataReceived(string connection, byte[] data)
        {
            DataReceived?.Invoke(connection, data);
        }

        public event DataSentDelegate DataSent;

        public void OnDataSent(string connection, byte[] data)
        {
            DataSent?.Invoke(connection, data);
        }

        #endregion

        private RawSocket _socket = null;
        private RawPCap _winpcap = null;
        private uint _localAddress = 0;
        private List<TCPConnection> _connections = new List<TCPConnection>(2);


        private Task _monitorTask = null;
        private int _Abort = 0;
        private bool Abort
        {
            get
            {
                return _Abort != 0;
            }
            set
            {
                if (value)
                    Interlocked.Exchange(ref _Abort, 1);
                else
                    Interlocked.Exchange(ref _Abort, 0);
            }
        }

        /// <summary>
        /// Validates the parameters and starts the monitor.
        /// </summary>
        public void Start()
        {
            if (UpdateTCPConnections == null)
                throw new ArgumentException("UpdateTCPConnections delegate must be specified.");

            _monitorTask = new Task(() => Run(), TaskCreationOptions.LongRunning);

            _monitorTask.Start();
        }

        /// <summary>
        /// Stops the monitor if it is active.
        /// </summary>
        public void Stop()
        {
            Abort = true;
            if (_monitorTask != null)
            {
                _monitorTask.Wait(5000);
                if (_monitorTask.Status == TaskStatus.Running)
                {
                    // todo: implement cancellation token to cancel task.
                }
                _monitorTask = null;
            }

            Cleanup();
        }

        private void Cleanup()
        {
            _connections.Clear();
            if (_socket != null)
            {
                _socket.Destroy();
                _socket = null;
            }
            if (_winpcap != null)
            {
                _winpcap.Destroy();
                _winpcap = null;
            }

            _localAddress = 0;
        }

        private void Run()
        {
            try
            {
                while (!Abort)
                {
                    UpdateProcessConnections();
                    if (_connections.Count == 0)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    CheckForIPChange();

                    ProcessNetworkData();

                    Thread.Sleep(10);
                }

                Cleanup();

                return;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("TCPNetworkMonitor Error: " + ex.ToString());
            }

            Cleanup();
        }

        private void UpdateProcessConnections()
        {
            // get any active game connections
            UpdateTCPConnections(_connections);
            if (_connections.Count == 0)
            {
                Cleanup();
                return;
            }

            for (int i = 0; i < _connections.Count; i++)
            {
                TCPConnection connection = _connections[i];
                if (string.IsNullOrWhiteSpace(connection.ID))
                {
                    connection.ID = connection.ToString(); //TODO: In the future there may be a better way to define the ID other than such a long string.

                    // Set up decoders for data sent from local machine
                    connection.IPDecoder_Send = new IPDecoder(connection.LocalIP, connection.RemoteIP, IPProtocol.TCP);
                    connection.TCPDecoder_Send = new TCPDecoder(connection.LocalPort, connection.RemotePort);

                    // set up decoders for data received by local machine
                    connection.IPDecoder_Receive = new IPDecoder(connection.RemoteIP, connection.LocalIP, IPProtocol.TCP);
                    connection.TCPDecoder_Receive = new TCPDecoder(connection.RemotePort, connection.LocalPort);

                    continue;
                }
            }
        }

        private void CheckForIPChange()
        {
            uint newLocalAddress = 0;
            if (string.IsNullOrWhiteSpace(LocalIP))
            {
                newLocalAddress = _connections.FirstOrDefault()?.LocalIP ?? 0;
                if (newLocalAddress == 0x100007F && _connections.Select(x => x.LocalIP).Distinct().Count() > 1) // 127.0.0.1 localhost
                    newLocalAddress = _connections.FirstOrDefault(x => x.LocalIP != 0x100007F)?.LocalIP ?? 0;
            }
            else
                newLocalAddress = (uint)IPAddress.Parse(LocalIP).Address;

            if (_localAddress != newLocalAddress)
            {
                Trace.WriteLine("TCPNetworkMonitor: " + ((MonitorType == NetworkMonitorType.WinPCap) ? "WinPCap " : "") + "listening on IP: " + new IPAddress(newLocalAddress).ToString());
                _localAddress = newLocalAddress;

                if (MonitorType == NetworkMonitorType.WinPCap)
                {
                    if (_winpcap != null)
                        _winpcap.Destroy();

                    _winpcap = new RawPCap();
                    _winpcap.Create(_localAddress);
                }
                else
                {
                    if (_socket != null)
                        _socket.Destroy();

                    _socket = new RawSocket();
                    _socket.Create(_localAddress);
                }
            }
        }

        private void ProcessNetworkData()
        {
            int size;
            byte[] buffer;

            if (MonitorType == NetworkMonitorType.WinPCap)
            {
                if (_winpcap == null)
                    return;
                while ((size = _winpcap.Receive(out buffer)) > 0)
                    ProcessData(buffer, size);
            }
            else
            {
                if (_socket == null)
                    return;
                while ((size = _socket.Receive(out buffer)) > 0)
                    ProcessData(buffer, size);
            }
        }


        private void ProcessData(byte[] buffer, int size)
        {
            byte[] tcpbuffer;
            byte[] payloadBuffer;
            for (int i = 0; i < _connections.Count; i++)
            {
                TCPConnection connection = _connections[i];
                connection.IPDecoder_Send.FilterAndStoreData(buffer, size);

                while ((tcpbuffer = connection.IPDecoder_Send.GetNextIPPayload()) != null)
                {
                    connection.TCPDecoder_Send.FilterAndStoreData(tcpbuffer);
                    while ((payloadBuffer = connection.TCPDecoder_Send.GetNextTCPDatagram()) != null)
                    {
                        OnDataSent(connection.ID, payloadBuffer);
                    }
                }

                connection.IPDecoder_Receive.FilterAndStoreData(buffer, size);
                while ((tcpbuffer = connection.IPDecoder_Receive.GetNextIPPayload()) != null)
                {
                    connection.TCPDecoder_Receive.FilterAndStoreData(tcpbuffer);
                    while ((payloadBuffer = connection.TCPDecoder_Receive.GetNextTCPDatagram()) != null)
                    {
                        OnDataReceived(connection.ID, payloadBuffer);
                    }
                }
            }
        }

    }
}
