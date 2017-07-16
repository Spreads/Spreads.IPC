using Spreads.Buffers;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Spreads.IPC.Network
{
    public class UDPConnection : IDisposable
    {
        private const int bufferLength = 4096;

        public long ReceiveCounter;
        public long SendCounter;

        private readonly Socket _socket;
        private readonly IPEndPoint _inEndPoint;
        private CancellationTokenSource _cts;
        private Task _receiveTask;

        public UDPConnection(IPAddress outIpAddress, int outPort,
            IPAddress intIpAddress, int inPort)
        {
            var outEndPoint = new IPEndPoint(outIpAddress, outPort);
            _inEndPoint = new IPEndPoint(intIpAddress, inPort);
            _socket = new Socket(outEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _socket.Blocking = true;
            _socket.EnableBroadcast = true;
            _socket.Bind(outEndPoint);
            _socket.Connect(_inEndPoint);
        }

        public bool IsReceiving => _receiveTask != null;

        public void StartReceive()
        {
            if (_receiveTask != null)
            {
                throw new InvalidOperationException("Already receiving");
            }
            _cts = new CancellationTokenSource();
            _receiveTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        try
                        {
                            var buffer = BufferPool<byte>.Rent(bufferLength);
                            var received = _socket.Receive(buffer);
                            if (received > 0)
                            {
                                Interlocked.Increment(ref ReceiveCounter);
                                var segment = new ArraySegment<byte>(buffer, 0, received);
                                OnReceiveCompleted(segment);
                            }
                            BufferPool<byte>.Return(buffer);
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError(ex.Message + Environment.NewLine + ex);
                        }
                    }
                }
                finally
                {
                    _receiveTask = null;
                }
            }, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void StopReceive()
        {
            var t = _receiveTask;
            if (t == null)
            {
                Trace.TraceWarning("Calling StopReceive on non-receiving connection.");
                return;
            }
            _cts.Cancel();
            t.Wait();
        }

        public void Send(byte[] buffer, int offset, int count)
        {
            _socket.Send(buffer, offset, count, SocketFlags.None);
            Interlocked.Increment(ref SendCounter);
        }

        public virtual void OnReceiveCompleted(ArraySegment<byte> buffer)
        {
            // TODO write to AppendLog with a stream id supplied in ctor
            // Order restoration by sequence number and missed packets should be in a separate 
            // process, which then writes data in-order into an AppendLog.
            // Some decoding could be done in that process as well since it is a shared cost
            // E.g. if a message could be represented by a blittable structure
            // or a sequence of such structs. Then reading of structs is just pointer deref.
        }

        public void Dispose()
        {
            StopReceive();
            _socket?.Dispose();
        }
    }
}