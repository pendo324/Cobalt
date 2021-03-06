﻿using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cobalt.Core.Network
{
	#region SocksException class
	[Serializable]
	public class SocksException : Exception
	{
		public SocksException(string message)
			: base(message)
		{
		}
	}
	#endregion

	public class SocksTcpClient
	{
		private const int SocksTimeout = 30000;

		private class AsyncResult : IAsyncResult
		{
			public object AsyncState { get; private set; }
			public WaitHandle AsyncWaitHandle { get { return this.Event; } }
			public bool CompletedSynchronously { get { return false; } }
			public bool IsCompleted { get { return this.AsyncWaitHandle.WaitOne(0); } }
			public EventWaitHandle Event { get; private set; }
			public AsyncCallback Callback { get; private set; }
			public string Hostname { get; set; }
			public int Port { get; set; }
			public Exception Exception { get; set; }
			public TcpClient Client { get; set; }

			public AsyncResult(AsyncCallback callback, object state)
			{
				this.Event = new ManualResetEvent(false);
				this.AsyncState = state;
			}
		}

		private readonly ProxyInfo _info;

		public SocksTcpClient(ProxyInfo proxy)
		{
			_info = proxy;
		}

		public IAsyncResult BeginConnect(string hostName, int port, AsyncCallback callback = null, object state = null)
		{
			var ar = new AsyncResult(callback, state) { Hostname = hostName, Port = port };
			ar.Client = new TcpClient();
			ar.Client.BeginConnect(_info.ProxyHostname, _info.ProxyPort, OnConnected, ar);
			return ar;
		}

		public TcpClient EndConnect(IAsyncResult ar)
		{
			var result = ar as AsyncResult;
			if (result == null)
			{
				throw new InvalidOperationException("IAsyncResult is not from this operation.");
			}
			ar.AsyncWaitHandle.WaitOne();
			if (result.Exception != null)
			{
				throw result.Exception;
			}
			return result.Client;
		}

	    public async Task<TcpClient> ConnectAsync(string hostname, int port)
	    {
	        TcpClient client = new TcpClient();
	        await client.ConnectAsync(hostname, port);
	        DoHandshake(client, hostname, port);
	        return client;
	    } 

		private void OnConnected(IAsyncResult ar)
		{
			var result = ar.AsyncState as AsyncResult;

			try
			{
				result.Client.EndConnect(ar);
				this.DoHandshake(result);
			}
			catch (Exception ex)
			{
				result.Client.Close();
				result.Exception = ex;
			}
			finally
			{
				result.Event.Set();
			    result.Callback?.Invoke(result);
			}
		}

	    private async Task DoHandshakeAsync(TcpClient client, string hostname, int port)
	    {
            bool useAuth = _info.ProxyUsername != null && _info.ProxyPassword != null;

            var stream = client.GetStream();
            await stream.WriteByteAsync(0x05); // SOCKS v5
            await stream.WriteByteAsync(useAuth ? (byte)0x2 : (byte)0x1); // auth protocols supported
            await stream.WriteByteAsync(0x00); // no auth
            if (useAuth)
            {
                await stream.WriteByteAsync(0x02); // user/pass
            }

            var buffer = new byte[256];
            this.Read(stream, buffer, 2);

            byte method = buffer[1];
            if (method == 0x2)
            {
                stream.WriteByte(0x1);
                this.WriteString(stream, _info.ProxyUsername);
                this.WriteString(stream, _info.ProxyPassword);
                this.Read(stream, buffer, 2);
                if (buffer[1] != 0x0)
                {
                    throw new SocksException($"Proxy authentication failed with code {buffer[1].ToString()}.");
                }
            }

            stream.WriteByte(0x5); // SOCKS v5
            stream.WriteByte(0x1); // TCP stream
            stream.WriteByte(0x0); // reserved
            stream.WriteByte(0x3); // domain name
            this.WriteString(stream, hostname); // hostname
            stream.WriteByte((byte)((port >> 8) & 0xFF)); // port high byte
            stream.WriteByte((byte)(port & 0xff)); // port low byte

            this.Read(stream, buffer, 4);
            switch (buffer[1])
            {
                case 0x1:
                    throw new SocksException("General failure.");
                case 0x2:
                    throw new SocksException("Connection not allowed by ruleset.");
                case 0x3:
                    throw new SocksException("Network unreachable.");
                case 0x4:
                    throw new SocksException("Host unreachable.");
                case 0x5:
                    throw new SocksException("Connection refused by destination host.");
                case 0x6:
                    throw new SocksException("TTL expired.");
                case 0x7:
                    throw new SocksException("Command not supported / protocol error.");
                case 0x8:
                    throw new SocksException("Address type not supported.");
            }
            switch (buffer[3])
            {
                case 0x1:
                    this.Read(stream, buffer, 4);
                    break;
                case 0x3:
                    this.ReadString(stream, buffer);
                    break;
                case 0x4:
                    this.Read(stream, buffer, 16);
                    break;
            }
            this.Read(stream, buffer, 2);
        }

	    private void DoHandshake(TcpClient client, string hostname, int port)
	    {
            bool useAuth = _info.ProxyUsername != null && _info.ProxyPassword != null;

	        var stream = client.GetStream();
            stream.WriteByte(0x05); // SOCKS v5
            stream.WriteByte(useAuth ? (byte)0x2 : (byte)0x1); // auth protocols supported
            stream.WriteByte(0x00); // no auth
            if (useAuth)
            {
                stream.WriteByte(0x02); // user/pass
            }

            var buffer = new byte[256];
            this.Read(stream, buffer, 2);

            byte method = buffer[1];
            if (method == 0x2)
            {
                stream.WriteByte(0x1);
                this.WriteString(stream, _info.ProxyUsername);
                this.WriteString(stream, _info.ProxyPassword);
                this.Read(stream, buffer, 2);
                if (buffer[1] != 0x0)
                {
                    throw new SocksException($"Proxy authentication failed with code {buffer[1].ToString()}.");
                }
            }

            stream.WriteByte(0x5); // SOCKS v5
            stream.WriteByte(0x1); // TCP stream
            stream.WriteByte(0x0); // reserved
            stream.WriteByte(0x3); // domain name
            this.WriteString(stream, hostname); // hostname
            stream.WriteByte((byte)((port >> 8) & 0xFF)); // port high byte
            stream.WriteByte((byte)(port & 0xff)); // port low byte

            this.Read(stream, buffer, 4);
            switch (buffer[1])
            {
                case 0x1:
                    throw new SocksException("General failure.");
                case 0x2:
                    throw new SocksException("Connection not allowed by ruleset.");
                case 0x3:
                    throw new SocksException("Network unreachable.");
                case 0x4:
                    throw new SocksException("Host unreachable.");
                case 0x5:
                    throw new SocksException("Connection refused by destination host.");
                case 0x6:
                    throw new SocksException("TTL expired.");
                case 0x7:
                    throw new SocksException("Command not supported / protocol error.");
                case 0x8:
                    throw new SocksException("Address type not supported.");
            }
            switch (buffer[3])
            {
                case 0x1:
                    this.Read(stream, buffer, 4);
                    break;
                case 0x3:
                    this.ReadString(stream, buffer);
                    break;
                case 0x4:
                    this.Read(stream, buffer, 16);
                    break;
            }
            this.Read(stream, buffer, 2);
        }

		private void DoHandshake(AsyncResult result)
		{
		    DoHandshake(result.Client, result.Hostname, result.Port);
		}

		private void WriteString(NetworkStream stream, string str)
		{
			if (str.Length > 255)
			{
				str = str.Substring(0, 255);
			}
			stream.WriteByte((byte)str.Length);
			var buffer = Encoding.ASCII.GetBytes(str);
			stream.Write(buffer, 0, buffer.Length);
		}

	    private async Task WriteStringAsync(NetworkStream stream, string str)
	    {
	        if (str.Length > 255)
	        {
	            str = str.Substring(0, 255);
	        }
	        await stream.WriteByteAsync((byte) str.Length);
	        var buffer = Encoding.ASCII.GetBytes(str);
	        await stream.WriteAsync(buffer, 0, buffer.Length);
	    }

		private string ReadString(NetworkStream stream, byte[] buffer)
		{
			this.Read(stream, buffer, 1);
			int length = buffer[0];
			this.Read(stream, buffer, length);
			return Encoding.ASCII.GetString(buffer, 0, length);
		}

	    private async Task<string> ReadStringAsync(NetworkStream stream, byte[] buffer)
	    {
	        await ReadAsync(stream, buffer, 1);
	        int length = buffer[0];
	        await ReadAsync(stream, buffer, length);
	        return Encoding.ASCII.GetString(buffer, 0, length);
	    }

	    private async Task<int> ReadAsync(NetworkStream stream, byte[] buffer, int count)
	    {
	        var timeout = Task.Delay(SocksTimeout);
	        var readTask = stream.ReadAsync(buffer, 0, count);
	        var completed = await Task.WhenAny(timeout, readTask).ConfigureAwait(false);
	        if (completed == readTask)
	        {
	            int read = await readTask; // so we can throw exceptions
	            if (read < 2)
	            {
	                throw new SocksException("Unable to negotiate with the proxy.");
	            }
	            else
	            {
	                return read;
	            }
	        }
	        else
	        {
	            throw new SocksException("The proxy did not respond in a timely manner.");
	        }
	    }

		private int Read(NetworkStream stream, byte[] buffer, int count)
		{
			var ar = stream.BeginRead(buffer, 0, count, null, null);
			if (!ar.AsyncWaitHandle.WaitOne(SocksTimeout))
			{
				throw new SocksException("The proxy did not respond in a timely manner.");
			}
			count = stream.EndRead(ar);
			if (count < 2)
			{
				throw new SocksException("Unable to negotiate with the proxy.");
			}
			return count;
		}
	}
}
