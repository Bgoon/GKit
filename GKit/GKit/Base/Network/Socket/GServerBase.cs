
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

#if OnUnity
namespace GKitForUnity
#elif OnWPF
namespace GKitForWPF
#else
namespace GKit
#endif
.Network {
	public abstract class GServerBase {
		public GServerState ServerState {
			get {
				lock (stateLock) {
					return state;
				}
			}
		}
		public bool IsAcceptConnecting {
			get {
				return acceptConnection;
			}
		}
		public int Port {
			get {
				return port;
			}
		}
		public int BackLogNum {
			get {
				return backLogNum;
			}
		}
		public int ClientCount {
			get {
				lock (ClientSet) {
					return ClientSet.Count;
				}
			}
		}

		#region Locks

		private object stateLock = new object();
		private object globalLock = new object();

		#endregion

		private GServerState state = GServerState.Stopped;
		private NetProtocol protocol;
		private bool acceptConnection;
		private int port;
		private int backLogNum;
		private bool noDelay;
		private bool usekeepAlive;
		private bool useLinger;
		private int keepAliveTime;
		private int keepAliveInternal;
		private int lingerTime;
		private Socket socket;
		private Thread acceptThread;
		public HashSet<Socket> ClientSet {
			get; private set;
		}
		private Dictionary<Socket, GClientData> clientDataDict;

		protected abstract void OnStarted();
		protected abstract void OnStartFailed(Exception ex);
		protected abstract void OnClosed();
		protected abstract void OnClosedByError(Exception ex);
		protected abstract void OnClientConnected(Socket client);
		protected abstract void OnClientDisconnected(Socket client);
		protected abstract void OnClientDisconnectedByError(Socket client, Exception ex);
		protected abstract void OnHeaderReceived(Socket client, byte[] header);
		protected abstract void OnPacketReceived(Socket client, byte[] packet);

		/// <param name="protocol">����� �� ���Ǵ� ��Ģ (null : �⺻��)</param>
		/// <param name="noDelay">��Ŷ�� ���� �� ��� ������ �� ����</param>
		/// <param name="useKeepAlive">���� ���� ���</param>
		/// <param name="keepAliveTime">���� ���� �ð����� (�и���)</param>
		/// <param name="keepAliveInternal">���� ���� ���� (�и���)</param>
		/// <param name="useLinger">���� ���� �� ���� ���� ���� ����</param>
		/// <param name="lingerTime">���� ���� ���� ���ð� (��)</param>
		public GServerBase(NetProtocol protocol = null, bool noDelay = false, bool useKeepAlive = true, int keepAliveTime = 3000, int keepAliveInternal = 1000, bool useLinger = false, int lingerTime = 3) {
			if (protocol == null) {
				protocol = new NetProtocol();
			}

			this.protocol = protocol;
			this.noDelay = noDelay;
			this.usekeepAlive = useKeepAlive;
			this.useLinger = useLinger;
			this.keepAliveTime = keepAliveTime;
			this.keepAliveInternal = keepAliveInternal;
			this.lingerTime = lingerTime;

			Init();
		}
		private void Init() {
			ClientSet = new HashSet<Socket>();
			clientDataDict = new Dictionary<Socket, GClientData>();

			acceptConnection = true;
		}

		public void Start(int port, int backLogNum) {
			lock (stateLock) {
				if (state != GServerState.Stopped)
					return;

				state = GServerState.Starting;
			}


			lock (globalLock) {
				try {
					//���� ����
					this.port = port;
					this.backLogNum = backLogNum;
					socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
					socket.Bind(new IPEndPoint(IPAddress.Any, port));
					socket.Listen(backLogNum);
					socket.NoDelay = !noDelay;

					byte[] optionBuffer = new byte[12];
					Array.Copy(BitConverter.GetBytes(usekeepAlive ? 1 : 0), 0, optionBuffer, 0, sizeof(int));
					Array.Copy(BitConverter.GetBytes(keepAliveTime), 0, optionBuffer, sizeof(int), sizeof(int));
					Array.Copy(BitConverter.GetBytes(keepAliveInternal), 0, optionBuffer, sizeof(int) * 2, sizeof(int));

					try {
						socket.IOControl(IOControlCode.KeepAliveValues, optionBuffer, null);
					} catch {
						usekeepAlive = false;
					}

					acceptThread = new Thread(AcceptClient);
					acceptThread.Start();

					lock (stateLock)
						state = GServerState.Running;

					OnStarted();
				} catch (Exception ex) {
					port = -1;
					backLogNum = -1;

					if (socket != null) {
						socket.Close();
						socket = null;
					}

					lock (stateLock)
						state = GServerState.Stopped;

					OnStartFailed(ex);
				}
			}
		}
		public void Close() {
			CloseWork();

			OnClosed();
		}
		public void SetAcceptConnection(bool accept) {
			acceptConnection = accept;
		}
		public void DisconnectClient(Socket client) {
			if (DisconnectClientWork(client)) {

				OnClientDisconnected(client);
			}
		}

		public void SendAll(byte[] packet) {
			Socket[] sockets = ClientSet.ToArray();
			foreach (var client in sockets) {
				SendTo(client, packet);
			}
		}
		public void SendTo(Socket client, byte[] data) {
			GClientData clientData;
			lock (ClientSet) {
				clientData = clientDataDict[client];
			}
			byte[] packet = protocol.Header2Bytes(data.Length).Concat(
				data).ToArray();

			lock (clientData.sendEventArgs) {
				if (clientData.isSending) {
					clientData.sendQueue.Enqueue(packet);
					return;
				} else {
					clientData.isSending = true;
				}
			}

			try {
				clientData.sendEventArgs.SetBuffer(packet, 0, packet.Length);
				if (!clientData.SendAsync())
					OnSendPacket(client, clientData.sendEventArgs);
			} catch (Exception ex) {
				DisconnectClientByError(clientData.socket, ex);
			}
		}


		private void CloseByError(Exception ex) {
			CloseWork();

			OnClosedByError(ex);
		}
		private void CloseWork() {
			lock (stateLock) {
				if (state != GServerState.Running)
					return;

				state = GServerState.Stopping;
			}

			int nClientNum;

			lock (ClientSet)
				nClientNum = ClientSet.Count;

			lock (globalLock) {
				lock (ClientSet) {
					foreach (var sClient in ClientSet) {
						try {
							sClient.Shutdown(SocketShutdown.Both);
						} finally {
							sClient.Close();
						}
					}

					ClientSet.Clear();
					clientDataDict.Clear();
				}

				port = -1;
				backLogNum = -1;

				if (socket != null) {
					socket.Close();
					socket = null;
				}

				lock (stateLock)
					state = GServerState.Stopped;
			}
		}
		private void DisconnectClientByError(Socket client, Exception ex) {
			if (ex is ObjectDisposedException) {
				//���� ����
				DisconnectClient(client);
			} else {
				//ó������ ���� ����
				if (DisconnectClientWork(client)) {
					OnClientDisconnectedByError(client, ex);
				}
			}
		}
		private bool DisconnectClientWork(Socket client) {
			lock (stateLock)
				if (state != GServerState.Running) {
					return false;
				}

			lock (ClientSet) {
				ClientSet.Remove(client);
				clientDataDict.Remove(client);
			}

			try {
				client.Shutdown(SocketShutdown.Both);
			} catch {
			}
			client.Close();
			client = null;
			return true;
		}
		private void AcceptClient() {
			try {
				for (; ; ) {
					if (state != GServerState.Starting && state != GServerState.Running)
						break;

					Socket client = null;
					try {
						client = socket.Accept();
					} catch (SocketException ex) {
					}

					if (client == null)
						continue;
					if (!acceptConnection) {
						try {
							client.Shutdown(SocketShutdown.Both);
						} catch {
							client.Close();
						}
						client = null;
						continue;
					}

					client.NoDelay = !noDelay;
					client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(useLinger, lingerTime));

					//Ŭ���̾�Ʈ ������ ����
					GClientData data;
					lock (ClientSet) {
						data = new GClientData(client, protocol.HeaderSize);
						ClientSet.Add(client);
						clientDataDict.Add(client, data);
					}

					//�̺�Ʈ ����
					data.sendEventArgs.Completed += OnSendPacket;

					OnClientConnected(client);

					StartReceiveClient(data);
				}
			} catch (Exception sServerException) {
				CloseByError(sServerException);
			}
		}
		private void StartReceiveClient(GClientData clientData) {
			try {
				clientData.receiveEventArgs.SetBuffer(clientData.receivedHeaderBuffer, 0, clientData.receivedHeaderBuffer.Length);
				clientData.receiveEventArgs.Completed += OnHeaderReceived;

				if (!clientData.socket.ReceiveAsync(clientData.receiveEventArgs))
					OnHeaderReceived(clientData.socket, clientData.receiveEventArgs);
			} catch (Exception sException) {
				DisconnectClientByError(clientData.socket, sException);
			}
		}
		private bool CheckAvailable(Socket client, SocketAsyncEventArgs e, NetProtocol.SocketAsyncWorkDelegate tryMethod, NetProtocol.SocketAsyncEventDelegate repeatMethod) {
			if (e.SocketError != SocketError.TimedOut &&
			   e.SocketError != SocketError.ConnectionReset &&
			   e.SocketError != SocketError.Success) {
				DisconnectClientByError(client, new SocketException((int)e.SocketError));
			} else if (e.SocketError == SocketError.TimedOut) {
				DisconnectClient(client);
			} else if (e.SocketError == SocketError.ConnectionReset || e.BytesTransferred == 0) {
				DisconnectClient(client);
			} else if (e.Count - e.BytesTransferred != 0) {
				try {
					e.SetBuffer(e.Buffer, e.Offset + e.BytesTransferred, e.Count - e.BytesTransferred);
					if (!tryMethod(e))
						repeatMethod(client, e);
				} catch (Exception ex) {
					DisconnectClientByError(client, ex);
				}
			} else {
				return true;
			}
			return false;
		}
		//Event
		private void OnSendPacket(object sender, SocketAsyncEventArgs e) {
			Socket client = (Socket)sender;

			if (CheckAvailable(client, e, client.SendAsync, OnSendPacket)) {
				GClientData data;

				lock (ClientSet) {
					if (!clientDataDict.TryGetValue(client, out data)) {
						return;
					}
				}

				byte[] packet;

				lock (e) {
					if (data.sendQueue.Count == 0) {
						data.isSending = false;
						return;
					} else {
						for (; ; ) {
							packet = data.sendQueue.Dequeue();
							if (packet != null)
								break;
							if (data.sendQueue.Count == 0) {
								data.isSending = false;
								return;
							}
						}
					}
				}

				try {
					e.SetBuffer(packet, 0, packet.Length);
					if (!client.SendAsync(e))
						OnSendPacket(client, e);
				} catch (Exception ex) {
					DisconnectClientByError(client, ex);
				}
			}
		}
		private void OnHeaderReceived(object sender, SocketAsyncEventArgs e) {
			Socket client = (Socket)sender;

			if (CheckAvailable(client, e, client.ReceiveAsync, OnHeaderReceived)) {
				int packetLength = protocol.Bytes2Header(e.Buffer);

				OnHeaderReceived(client, e.Buffer);

				if (packetLength <= 0)
					DisconnectClientByError(client, new ArgumentOutOfRangeException("�ùٸ��� ���� ��Ŷ �����Դϴ�."));
				else {
					e.SetBuffer(new byte[packetLength], 0, packetLength);

					e.Completed -= OnHeaderReceived;
					e.Completed += OnPacketReceived;

					try {
						if (!client.ReceiveAsync(e))
							OnPacketReceived(client, e);
					} catch (Exception ex) {
						DisconnectClientByError(client, ex);
					}
				}
			}
		}
		private void OnPacketReceived(object sender, SocketAsyncEventArgs e) {
			Socket client = (Socket)sender;

			if (CheckAvailable(client, e, client.ReceiveAsync, OnPacketReceived)) {
				GClientData data;

				lock (ClientSet)
					if (!clientDataDict.TryGetValue(client, out data))
						return;

				OnPacketReceived(client, e.Buffer);

				e.SetBuffer(data.receivedHeaderBuffer, 0, data.receivedHeaderBuffer.Length);
				e.Completed -= OnPacketReceived;
				e.Completed += OnHeaderReceived;

				try {
					if (!client.ReceiveAsync(e))
						OnHeaderReceived(client, e);
				} catch (Exception sException) {
					DisconnectClientByError(client, sException);
				}
			}
		}
	}
}