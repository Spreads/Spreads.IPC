using Spreads.Buffers;
using Spreads.Collections.Concurrent;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Spreads.IPC.Network
{
    public class UDPConnection
    {
        private const int bufferLength = 4096;

        public long ReceiveCounter;
        public long SendCounter;
        private readonly ObjectPool<SocketAsyncEventArgs> _receiveArgsPool;

        private readonly Socket _socket;
        private readonly IPEndPoint _inEndPoint;

        public UDPConnection(IPAddress outIpAddress, int outPort,
            IPAddress intIpAddress, int inPort)
        {
            var outEndPoint = new IPEndPoint(outIpAddress, outPort);
            _inEndPoint = new IPEndPoint(intIpAddress, inPort);
            _receiveArgsPool = new ObjectPool<SocketAsyncEventArgs>(ReceiveEventArgsFactory, Environment.ProcessorCount * 2);
            //_sendArgsPool = new ObjectPool<SocketAsyncEventArgs>(SendEventArgsFactory, Environment.ProcessorCount * 2);
            _socket = new Socket(outEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _socket.EnableBroadcast = true;
            _socket.Bind(outEndPoint);
            _socket.Connect(_inEndPoint);
        }

        public void StartReceive()
        {
            SocketAsyncEventArgs args = _receiveArgsPool.Allocate();

            if (!_socket.ReceiveAsync(args))
            {
                // if false, the Complete won't be called
                OnReceiveCompleted(this, args);
            }

            Thread.Sleep(int.MaxValue - 1);
        }

        public void Send(byte[] buffer, int offset, int count)
        {
            _socket.Send(buffer, offset, count, SocketFlags.None);
            Interlocked.Increment(ref SendCounter);
        }


        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
#if NET451
            if (!ExecutionContext.IsFlowSuppressed())
            {
                ExecutionContext.SuppressFlow();
            }
#endif
            Interlocked.Increment(ref ReceiveCounter);
            SocketAsyncEventArgs args = _receiveArgsPool.Allocate();

            if (!_socket.ReceiveAsync(args))
            {
                // if false, the Complete won't be called
                OnReceiveCompleted(this, args);
            }

            // TODO Process message here

            _receiveArgsPool.Free(e);
        }

        private SocketAsyncEventArgs ReceiveEventArgsFactory()
        {
            var args = new SocketAsyncEventArgs();
            var buffer = BufferPool<byte>.Rent(bufferLength);
            args.Completed += OnReceiveCompleted;
            args.SetBuffer(buffer, 0, bufferLength);
            args.RemoteEndPoint = _inEndPoint;
            return args;
        }
    }
}