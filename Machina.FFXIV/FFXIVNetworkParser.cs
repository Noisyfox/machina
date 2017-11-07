using System;
using System.Collections.Generic;
using System.Threading;

namespace Machina.FFXIV
{
    public delegate void MessageReceivedDelegate(long epoch, byte[] message);
    public delegate void MessageSentDelegate(long epoch, byte[] message);

    public class FFXIVNetworkParser
    {
        #region Message Delegates section

        /// <summary>
        /// Specifies the delegate that is called when data is received and successfully decoded/
        /// </summary>
        public event MessageReceivedDelegate MessageReceived;

        private void OnMessageReceived(long epoch, byte[] message)
        {
            MessageReceived?.Invoke(epoch, message);
        }


        public event MessageSentDelegate MessageSent;

        private void OnMessageSent(long epoch, byte[] message)
        {
            MessageSent?.Invoke(epoch, message);
        }

        #endregion

        private ITCPNetworkMonitor _monitor;
        private readonly Dictionary<string, FFXIVBundleDecoder> _sentDecoders = new Dictionary<string, FFXIVBundleDecoder>();
        private readonly Dictionary<string, FFXIVBundleDecoder> _receivedDecoders = new Dictionary<string, FFXIVBundleDecoder>();

        private void ProcessSentMessage(string connection, byte[] data)
        {
            Tuple<long, byte[]> message;
            if (!_sentDecoders.ContainsKey(connection))
                _sentDecoders.Add(connection, new FFXIVBundleDecoder());

            _sentDecoders[connection].StoreData(data);
            while ((message = _sentDecoders[connection].GetNextFFXIVMessage()) != null)
            {
                OnMessageSent(message.Item1, message.Item2);
            }
        }

        private void ProcessReceivedMessage(string connection, byte[] data)
        {
            Tuple<long, byte[]> message;
            if (!_receivedDecoders.ContainsKey(connection))
                _receivedDecoders.Add(connection, new FFXIVBundleDecoder());

            _receivedDecoders[connection].StoreData(data);
            while ((message = _receivedDecoders[connection].GetNextFFXIVMessage()) != null)
            {
                OnMessageReceived(message.Item1, message.Item2);
            }

        }

        public void WorkOn(ITCPNetworkMonitor monitor)
        {
            Stop();

            _monitor = monitor;
            _monitor.DataReceived += ProcessReceivedMessage;
            _monitor.DataSent += ProcessSentMessage;
        }

        public void Stop()
        {
            var m = Interlocked.Exchange(ref _monitor, null);
            if (m != null)
            {
                m.DataReceived -= ProcessReceivedMessage;
                m.DataSent -= ProcessSentMessage;
            }
            _sentDecoders.Clear();
            _receivedDecoders.Clear();
        }
    }
}
