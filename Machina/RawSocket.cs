﻿// Machina ~ RawSocket.cs
// 
// Copyright © 2017 Ravahn - All Rights Reserved
// 
//This program is free software: you can redistribute it and/or modify
//it under the terms of the GNU General Public License as published by
//the Free Software Foundation, either version 3 of the License, or
//(at your option) any later version.

//This program is distributed in the hope that it will be useful,
//but WITHOUT ANY WARRANTY; without even the implied warranty of
//MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
//GNU General Public License for more details.

//You should have received a copy of the GNU General Public License
//along with this program.If not, see<http://www.gnu.org/licenses/>.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Win32;

namespace Machina
{
    public class RawSocket : IRawSocket
    {
        private class SocketState
        {
            public NetworkBufferFactory.Buffer buffer = null;
            public Socket socket = null;
            public object socketLock = new object();
            public NetworkBufferFactory bufferFactory = new NetworkBufferFactory(20, 0);
        }

        private SocketState _socketState = new SocketState();

        public SocketDataAvailableDelegate OnDataAvailable { get; set; }

        public uint LocalIP
        { get; private set; }
        public uint RemoteIP
        { get; private set; }

        public void Create(uint localAddress, uint remoteAddress = 0)
        {
            LocalIP = localAddress;
            RemoteIP = remoteAddress;
            _socketState.bufferFactory.AllocatedBufferAvailable = () => OnDataAvailable?.Invoke();

            // set buffer
            _socketState.buffer = _socketState.bufferFactory.GetNextFreeBuffer(); 

            lock (_socketState.socketLock)
            {
                // create the socket
                _socketState.socket = CreateRawSocket(localAddress, remoteAddress);

                // start receiving data asynchronously
                _socketState.socket.BeginReceive(_socketState.buffer.Data, 0, _socketState.buffer.Data.Length, SocketFlags.None, new AsyncCallback(OnReceive), (object)_socketState);
            }
        }

        public int Receive(out byte[] buffer)
        {
            // retrieve data from allocated buffer.
            NetworkBufferFactory.Buffer data = _socketState.bufferFactory.GetNextAllocatedBuffer();
            buffer = data?.Data;
            return data?.AllocatedSize ?? 0;
        }

        public void FreeBuffer(ref byte[] buffer)
        {
            NetworkBufferFactory.Buffer data = new NetworkBufferFactory.Buffer() { Data = buffer, AllocatedSize = 0 };
            _socketState.bufferFactory.AddFreeBuffer(data);
        }

        public void Destroy()
        {
            _socketState.bufferFactory.AllocatedBufferAvailable = null;

            lock (_socketState.socketLock)
            {
                if (_socketState.socket != null)
                {
                    try
                    {
                        _socketState.socket.Shutdown(SocketShutdown.Both);
                        _socketState.socket.Close();
                        _socketState.socket.Dispose();
                    }
                    finally
                    {
                        _socketState.socket = null;
                        _socketState.buffer = null;
                    }
                }
            }
        }

        private static Socket CreateRawSocket(uint localAddress, uint remoteAddress)
        {
            const string ENV_VAR = "FORCE_MACHINA_RAW_SOCKET_WINE_COMPAT";
            bool Windows = true;
            Process[] processes = Process.GetProcessesByName("Idle");
            if (processes.Length == 0)
            {
                Trace.WriteLine("RawSocket: Did not detect Idle process, using TCP socket instead of IP socket for wine compatability.", "DEBUG-MACHINA");
                Windows = false;
            }

            if (Environment.GetEnvironmentVariable(ENV_VAR) == "1")
            {
                Trace.WriteLine($"RawSocket: Environment variable {ENV_VAR} set, forcing wine compatability.", "DEBUG-MACHINA");
                Windows = false;
            }

            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, Windows ? ProtocolType.IP : ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(localAddress, 0));

            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);

            if (Windows)
            {
                byte[] trueBytes = new byte[4] { 3, 0, 0, 0 }; // 3 == RCVALL_IPLEVEL, so it only intercepts the target interface
                byte[] outBytes = new byte[4];
                socket.IOControl(IOControlCode.ReceiveAll, trueBytes, outBytes);
            }

            if (remoteAddress > 0)
                socket.Connect(new IPEndPoint(remoteAddress, 0));
            
            return socket;
        }

        private static void OnReceive(IAsyncResult ar)
        {
            try
            {
                SocketState state = ar.AsyncState as SocketState;
                if (state == null)
                    return;

                NetworkBufferFactory.Buffer buffer = state?.buffer;

                lock (state.socketLock)
                {
                    if (state.socket == null)
                        return;

                    int received = state.socket.EndReceive(ar);
                    state.buffer = state.bufferFactory.GetNextFreeBuffer();
                    state.socket.BeginReceive(state.buffer.Data, 0, state.buffer.Data.Length, SocketFlags.None, new System.AsyncCallback(OnReceive), (object)state);

                    if (received > 0)
                    {
                        buffer.AllocatedSize = received;
                        state.bufferFactory.AddAllocatedBuffer(buffer);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // do nothing - teardown occurring.
            }
            catch (Exception ex)
            {
                Trace.WriteLine("RawSocket: Error while receiving socket data.  Network capture aborted, please restart application." + ex.ToString(), "DEBUG-MACHINA");
            }
        }
    }

}
