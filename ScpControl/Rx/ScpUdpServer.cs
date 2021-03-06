using ScpControl.ScpCore;
using ScpControl.Shared.Core;
using ScpControl.Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ScpControl.Rx
{
	class ScpUdpServer
	{
		private Socket udpSock;
		private uint serverId;
		private bool running;
		private byte[] recvBuffer = new byte[1024];

		public delegate DualShockPadMeta GetPadDetail(DsPadId pad);

		private GetPadDetail portInfoGet;

		public ScpUdpServer(GetPadDetail getPadDetailDel)
		{
			portInfoGet = getPadDetailDel;
		}

		enum MessageType
		{
			DSUC_VersionReq = 0x100000,
			DSUS_VersionRsp = 0x100000,
			DSUC_ListPorts	= 0x100001,
			DSUS_PortInfo	= 0x100001,
			DSUC_PadDataReq = 0x100002,
			DSUS_PadDataRsp = 0x100002,
		};

		private const ushort MaxProtocolVersion = 1001;

		class ClientRequestTimes
		{
			DateTime allPads;
			DateTime[] padIds;
			Dictionary<PhysicalAddress, DateTime> padMacs;

			public DateTime AllPadsTime { get { return allPads; } }
			public DateTime[] PadIdsTime { get { return padIds; } }
			public Dictionary<PhysicalAddress, DateTime> PadMacsTime { get { return padMacs; } }

			public ClientRequestTimes()
			{
				allPads = DateTime.MinValue;
				padIds = new DateTime[4];

				for (int i = 0; i < padIds.Length; i++)
					padIds[i] = DateTime.MinValue;

				padMacs = new Dictionary<PhysicalAddress,DateTime>();
			}

			public void RequestPadInfo(byte regFlags, byte idToReg, PhysicalAddress macToReg)
			{
				if (regFlags == 0)
					allPads = DateTime.UtcNow;
				else
				{
					if ((regFlags & 0x01) != 0) //id valid
					{
						if (idToReg < padIds.Length)
							padIds[idToReg] = DateTime.UtcNow;
					}
					if ((regFlags & 0x02) != 0) //mac valid
					{
						padMacs[macToReg] = DateTime.UtcNow;
					}
				}
			}
		}

		private Dictionary<IPEndPoint, ClientRequestTimes> clients = new Dictionary<IPEndPoint, ClientRequestTimes>();

		private int BeginPacket(byte[] packetBuf, ushort reqProtocolVersion = MaxProtocolVersion)
		{
			int currIdx = 0;
			packetBuf[currIdx++] = (byte)'D';
			packetBuf[currIdx++] = (byte)'S';
			packetBuf[currIdx++] = (byte)'U';
			packetBuf[currIdx++] = (byte)'S';

			Array.Copy(BitConverter.GetBytes((ushort)reqProtocolVersion), 0, packetBuf, currIdx, 2);
			currIdx += 2;

			Array.Copy(BitConverter.GetBytes((ushort)packetBuf.Length - 16), 0, packetBuf, currIdx, 2);
			currIdx += 2;

			Array.Clear(packetBuf, currIdx, 4); //place for crc
			currIdx += 4;

			Array.Copy(BitConverter.GetBytes((uint)serverId), 0, packetBuf, currIdx, 4);
			currIdx += 4;

			return currIdx;
		}

		private void FinishPacket(byte[] packetBuf)
		{
			Array.Clear(packetBuf, 8, 4);

			uint crcCalc = Crc32.Compute(packetBuf);
			Array.Copy(BitConverter.GetBytes((uint)crcCalc), 0, packetBuf, 8, 4);
		}

		private void SendPacket(IPEndPoint clientEP, byte[] usefulData, ushort reqProtocolVersion = MaxProtocolVersion)
		{
			byte[] packetData = new byte[usefulData.Length + 16];
			int currIdx = BeginPacket(packetData, reqProtocolVersion);
			Array.Copy(usefulData, 0, packetData, currIdx, usefulData.Length);
			FinishPacket(packetData);

			try { udpSock.SendTo(packetData, clientEP); }
			catch (Exception e) { }
		}

		private void ProcessIncoming(byte[] localMsg, IPEndPoint clientEP)
		{
			try
			{
				int currIdx = 0;
				if (localMsg[0] != 'D' || localMsg[1] != 'S' || localMsg[2] != 'U' || localMsg[3] != 'C')
					return;
				else
					currIdx += 4;

				uint protocolVer = BitConverter.ToUInt16(localMsg, currIdx);
				currIdx += 2;

				if (protocolVer > MaxProtocolVersion)
					return;

				uint packetSize = BitConverter.ToUInt16(localMsg, currIdx);
				currIdx += 2;

				if (packetSize < 0)
					return;

				packetSize += 16; //size of header
				if (packetSize > localMsg.Length)
					return;
				else if (packetSize < localMsg.Length)
				{
					byte[] newMsg = new byte[packetSize];
					Array.Copy(localMsg, newMsg, packetSize);
					localMsg = newMsg;
				}

				uint crcValue = BitConverter.ToUInt32(localMsg, currIdx);
				//zero out the crc32 in the packet once we got it since that's whats needed for calculation
				localMsg[currIdx++] = 0;
				localMsg[currIdx++] = 0;
				localMsg[currIdx++] = 0;
				localMsg[currIdx++] = 0;

				uint crcCalc = ScpControl.Shared.Utilities.Crc32.Compute(localMsg);
				if (crcValue != crcCalc)
					return;

				uint clientId = BitConverter.ToUInt32(localMsg, currIdx);
				currIdx += 4;

				uint messageType = BitConverter.ToUInt32(localMsg, currIdx);
				currIdx += 4;

				if (messageType == (uint)MessageType.DSUC_VersionReq)
				{
					byte[] outputData = new byte[8];
					int outIdx = 0;
					Array.Copy(BitConverter.GetBytes((uint)MessageType.DSUS_VersionRsp), 0, outputData, outIdx, 4);
					outIdx += 4;
					Array.Copy(BitConverter.GetBytes((ushort)MaxProtocolVersion), 0, outputData, outIdx, 2);
					outIdx += 2;
					outputData[outIdx++] = 0;
					outputData[outIdx++] = 0;

					SendPacket(clientEP, outputData, 1001);
				}
				else if (messageType == (uint)MessageType.DSUC_ListPorts)
				{
					int numPadRequests = BitConverter.ToInt32(localMsg, currIdx);
					currIdx += 4;
					if (numPadRequests < 0 || numPadRequests > 4)
						return;

					int requestsIdx = currIdx;
					for (int i = 0; i < numPadRequests; i++)
					{
						byte currRequest = localMsg[requestsIdx+i];
						if (currRequest < (byte)DsPadId.One || currRequest > (byte)DsPadId.Four)
							return;
					}

					byte[] outputData = new byte[16];
					for (byte i = 0; i < numPadRequests; i++)
					{
						byte currRequest = localMsg[requestsIdx + i];
						var padData = portInfoGet((DsPadId)currRequest);

						int outIdx = 0;
						Array.Copy(BitConverter.GetBytes((uint)MessageType.DSUS_PortInfo), 0, outputData, outIdx, 4);
						outIdx += 4;

						outputData[outIdx++] = (byte)padData.PadId;
						outputData[outIdx++] = (byte)padData.PadState;
						outputData[outIdx++] = (byte)padData.Model;
						outputData[outIdx++] = (byte)padData.ConnectionType;

						var addressBytes = padData.PadMacAddress.GetAddressBytes();
						if (addressBytes.Length == 6)
						{
							outputData[outIdx++] = addressBytes[0];
							outputData[outIdx++] = addressBytes[1];
							outputData[outIdx++] = addressBytes[2];
							outputData[outIdx++] = addressBytes[3];
							outputData[outIdx++] = addressBytes[4];
							outputData[outIdx++] = addressBytes[5];
						}
						else
						{
							outputData[outIdx++] = 0;
							outputData[outIdx++] = 0;
							outputData[outIdx++] = 0;
							outputData[outIdx++] = 0;
							outputData[outIdx++] = 0;
							outputData[outIdx++] = 0;
						}

						outputData[outIdx++] = (byte)padData.BatteryStatus;
						outputData[outIdx++] = 0;

						SendPacket(clientEP, outputData, 1001);
					}
				}
				else if (messageType == (uint)MessageType.DSUC_PadDataReq)
				{
					byte regFlags = localMsg[currIdx++];
					byte idToReg = localMsg[currIdx++];
					PhysicalAddress macToReg = null;
					{
						byte[] macBytes = new byte[6];
						Array.Copy(localMsg, currIdx, macBytes, 0, macBytes.Length);
						currIdx += macBytes.Length;
						macToReg = new PhysicalAddress(macBytes);
					}

					lock(clients)
					{
						if (clients.ContainsKey(clientEP))
							clients[clientEP].RequestPadInfo(regFlags, idToReg, macToReg);
						else
						{
							var clientTimes = new ClientRequestTimes();
							clientTimes.RequestPadInfo(regFlags, idToReg, macToReg);
							clients[clientEP] = clientTimes;
						}
					}
				}
			}
			catch (Exception e) { }
		}

		private void ReceiveCallback(IAsyncResult iar)
		{
			byte[] localMsg = null;
			EndPoint clientEP = new IPEndPoint(IPAddress.Any, 0);

			try
			{
				//Get the received message.
				Socket recvSock = (Socket)iar.AsyncState;
				int msgLen = recvSock.EndReceiveFrom(iar, ref clientEP);

				localMsg = new byte[msgLen];
				Array.Copy(recvBuffer, localMsg, msgLen);
			}
			catch (Exception e) { }

			//Start another receive as soon as we copied the data
			StartReceive();

			//Process the data if its valid
			if (localMsg != null)
				ProcessIncoming(localMsg, (IPEndPoint)clientEP);
		}
		private void StartReceive()
		{
			try
			{
				if (running)
				{
					//Start listening for a new message.
					EndPoint newClientEP = new IPEndPoint(IPAddress.Any, 0);
					udpSock.BeginReceiveFrom(recvBuffer, 0, recvBuffer.Length, SocketFlags.None, ref newClientEP, ReceiveCallback, udpSock);
				}			
			}
			catch (SocketException ex) 
			{
				uint IOC_IN = 0x80000000;
				uint IOC_VENDOR = 0x18000000;
				uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
				udpSock.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);

				StartReceive(); 
			}
		}

		public void Start(int port)
		{
			if (running)
			{
				if (udpSock != null)
				{
					udpSock.Close();
					udpSock = null;
				}				
				running = false;
			}

			udpSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			try { udpSock.Bind(new IPEndPoint(IPAddress.Loopback, port)); }
			catch (SocketException ex) 
			{
				udpSock.Close();
				udpSock = null;

				throw ex;
			}

			byte[] randomBuf = new byte[4];
			new Random().NextBytes(randomBuf);
			serverId = BitConverter.ToUInt32(randomBuf,0);

			running = true;
			StartReceive();
		}

		public void Stop()
		{
			running = false;
			if (udpSock != null)
			{
				udpSock.Close();
				udpSock = null;
			}
		}

		private bool ReportToBuffer(ScpHidReport hidReport, byte[] outputData, ref int outIdx)
		{
			switch (hidReport.Model)
			{
				case DsModel.DS3:
					{
						outputData[outIdx] = 0;

						if (hidReport[Ds3Button.Left].IsPressed)	outputData[outIdx] |= 0x80;
						if (hidReport[Ds3Button.Down].IsPressed)	outputData[outIdx] |= 0x40;
						if (hidReport[Ds3Button.Right].IsPressed)	outputData[outIdx] |= 0x20;
						if (hidReport[Ds3Button.Up].IsPressed)		outputData[outIdx] |= 0x10;

						if (hidReport[Ds3Button.Start].IsPressed)	outputData[outIdx] |= 0x08;
						if (hidReport[Ds3Button.R3].IsPressed)		outputData[outIdx] |= 0x04;
						if (hidReport[Ds3Button.L3].IsPressed)		outputData[outIdx] |= 0x02;
						if (hidReport[Ds3Button.Select].IsPressed)	outputData[outIdx] |= 0x01;

						outputData[++outIdx] = 0;

						if (hidReport[Ds3Button.Square].IsPressed)	outputData[outIdx] |= 0x80;
						if (hidReport[Ds3Button.Cross].IsPressed)	outputData[outIdx] |= 0x40;
						if (hidReport[Ds3Button.Circle].IsPressed)	outputData[outIdx] |= 0x20;
						if (hidReport[Ds3Button.Triangle].IsPressed) outputData[outIdx] |= 0x10;

						if (hidReport[Ds3Button.R1].IsPressed)		outputData[outIdx] |= 0x08;
						if (hidReport[Ds3Button.L1].IsPressed)		outputData[outIdx] |= 0x04;
						if (!GlobalConfiguration.Instance.SwapTriggers)
						{
							if (hidReport[Ds3Button.R2].IsPressed)	outputData[outIdx] |= 0x02;
							if (hidReport[Ds3Button.L2].IsPressed)	outputData[outIdx] |= 0x01;
						}
						else
						{
							if (hidReport[Ds3Button.L2].IsPressed)	outputData[outIdx] |= 0x01;
							if (hidReport[Ds3Button.R2].IsPressed)	outputData[outIdx] |= 0x02;
						}

						outputData[++outIdx] = (hidReport[Ds3Button.Ps].IsPressed) ? (byte)1 : (byte)0;
						outputData[++outIdx] = 0; //no Touchpad click on ds3					

						if (!DsMath.DeadZone(GlobalConfiguration.Instance.DeadZoneL, hidReport[Ds3Axis.Lx].Value, hidReport[Ds3Axis.Ly].Value))
						{
							outputData[++outIdx] = hidReport[Ds3Axis.Lx].Value;
							if (GlobalConfiguration.Instance.FlipLX) outputData[outIdx] = (byte)(255 - outputData[outIdx]);

							outputData[++outIdx] = hidReport[Ds3Axis.Ly].Value;
							if (!GlobalConfiguration.Instance.FlipLY) outputData[outIdx] = (byte)(255 - outputData[outIdx]);
						}
						else
						{
							outputData[++outIdx] = 0x7F;
							outputData[++outIdx] = 0x7F;
						}

						if (!DsMath.DeadZone(GlobalConfiguration.Instance.DeadZoneR, hidReport[Ds3Axis.Rx].Value, hidReport[Ds3Axis.Ry].Value))
						{
							outputData[++outIdx] = hidReport[Ds3Axis.Rx].Value;
							if (GlobalConfiguration.Instance.FlipRX) outputData[outIdx] = (byte)(255 - outputData[outIdx]);

							outputData[++outIdx] = hidReport[Ds3Axis.Ry].Value;
							if (!GlobalConfiguration.Instance.FlipRY) outputData[outIdx] = (byte)(255 - outputData[outIdx]);
						}
						else
						{
							outputData[++outIdx] = 0x7F;
							outputData[++outIdx] = 0x7F;
						}

						//Analog buttons
						outputData[++outIdx] = hidReport[Ds3Axis.Left].Value;
						outputData[++outIdx] = hidReport[Ds3Axis.Down].Value;
						outputData[++outIdx] = hidReport[Ds3Axis.Right].Value;
						outputData[++outIdx] = hidReport[Ds3Axis.Up].Value;

						outputData[++outIdx] = hidReport[Ds3Axis.Square].Value;
						outputData[++outIdx] = hidReport[Ds3Axis.Cross].Value;
						outputData[++outIdx] = hidReport[Ds3Axis.Circle].Value;
						outputData[++outIdx] = hidReport[Ds3Axis.Triangle].Value;

						outputData[++outIdx] = hidReport[Ds3Axis.R1].Value;
						outputData[++outIdx] = hidReport[Ds3Axis.L1].Value;

						if (!GlobalConfiguration.Instance.SwapTriggers)
						{
							outputData[++outIdx] = hidReport[Ds3Axis.R2].Value;
							outputData[++outIdx] = hidReport[Ds3Axis.L2].Value;
						}
						else
						{
							outputData[++outIdx] = hidReport[Ds3Axis.L2].Value;
							outputData[++outIdx] = hidReport[Ds3Axis.R2].Value;
						}

						outIdx++;
					}
					break;

				case DsModel.DS4:
					{
						outputData[outIdx] = 0;

						if (hidReport[Ds4Button.Left].IsPressed)	outputData[outIdx] |= 0x80;
						if (hidReport[Ds4Button.Down].IsPressed)	outputData[outIdx] |= 0x40;
						if (hidReport[Ds4Button.Right].IsPressed)	outputData[outIdx] |= 0x20;
						if (hidReport[Ds4Button.Up].IsPressed)		outputData[outIdx] |= 0x10;

						if (hidReport[Ds4Button.Options].IsPressed)	outputData[outIdx] |= 0x08;
						if (hidReport[Ds4Button.R3].IsPressed)		outputData[outIdx] |= 0x04;
						if (hidReport[Ds4Button.L3].IsPressed)		outputData[outIdx] |= 0x02;
						if (hidReport[Ds4Button.Share].IsPressed)	outputData[outIdx] |= 0x01;

						outputData[++outIdx] = 0;

						if (hidReport[Ds4Button.Square].IsPressed)	outputData[outIdx] |= 0x80;
						if (hidReport[Ds4Button.Cross].IsPressed)	outputData[outIdx] |= 0x40;
						if (hidReport[Ds4Button.Circle].IsPressed)	outputData[outIdx] |= 0x20;
						if (hidReport[Ds4Button.Triangle].IsPressed) outputData[outIdx] |= 0x10;

						if (hidReport[Ds4Button.R1].IsPressed)		outputData[outIdx] |= 0x08;
						if (hidReport[Ds4Button.L1].IsPressed)		outputData[outIdx] |= 0x04;
						if (!GlobalConfiguration.Instance.SwapTriggers)
						{
							if (hidReport[Ds4Button.R2].IsPressed)	outputData[outIdx] |= 0x02;
							if (hidReport[Ds4Button.L2].IsPressed)	outputData[outIdx] |= 0x01;
						}
						else
						{
							if (hidReport[Ds4Button.L2].IsPressed)	outputData[outIdx] |= 0x01;
							if (hidReport[Ds4Button.R2].IsPressed)	outputData[outIdx] |= 0x02;
						}

						outputData[++outIdx] = (hidReport[Ds4Button.Ps].IsPressed) ? (byte)1 : (byte)0;
						outputData[++outIdx] = (hidReport[Ds4Button.TouchPad].IsPressed) ? (byte)1 : (byte)0;

						if (!DsMath.DeadZone(GlobalConfiguration.Instance.DeadZoneL, hidReport[Ds4Axis.Lx].Value, hidReport[Ds4Axis.Ly].Value))
						{
							outputData[++outIdx] = hidReport[Ds4Axis.Lx].Value;
							if (GlobalConfiguration.Instance.FlipLX) outputData[outIdx] = (byte)(255 - outputData[outIdx]);

							outputData[++outIdx] = hidReport[Ds4Axis.Ly].Value;
							if (!GlobalConfiguration.Instance.FlipLY) outputData[outIdx] = (byte)(255 - outputData[outIdx]);
						}
						else
						{
							outputData[++outIdx] = 0x7F;
							outputData[++outIdx] = 0x7F;
						}

						if (!DsMath.DeadZone(GlobalConfiguration.Instance.DeadZoneR, hidReport[Ds4Axis.Rx].Value, hidReport[Ds4Axis.Ry].Value))
						{
							outputData[++outIdx] = hidReport[Ds4Axis.Rx].Value;
							if (GlobalConfiguration.Instance.FlipRX) outputData[outIdx] = (byte)(255 - outputData[outIdx]);

							outputData[++outIdx] = hidReport[Ds4Axis.Ry].Value;
							if (!GlobalConfiguration.Instance.FlipRY) outputData[outIdx] = (byte)(255 - outputData[outIdx]);
						}
						else
						{
							outputData[++outIdx] = 0x7F;
							outputData[++outIdx] = 0x7F;
						}

						//we don't have analog buttons so just use the Button enums (which give either 0 or 0xFF)
						outputData[++outIdx] = hidReport[Ds4Button.Left].Value;
						outputData[++outIdx] = hidReport[Ds4Button.Down].Value;
						outputData[++outIdx] = hidReport[Ds4Button.Right].Value;
						outputData[++outIdx] = hidReport[Ds4Button.Up].Value;

						outputData[++outIdx] = hidReport[Ds4Button.Square].Value;
						outputData[++outIdx] = hidReport[Ds4Button.Cross].Value;
						outputData[++outIdx] = hidReport[Ds4Button.Circle].Value;
						outputData[++outIdx] = hidReport[Ds4Button.Triangle].Value;

						outputData[++outIdx] = hidReport[Ds4Button.R1].Value;
						outputData[++outIdx] = hidReport[Ds4Button.L1].Value;

						if (!GlobalConfiguration.Instance.SwapTriggers)
						{
							outputData[++outIdx] = hidReport[Ds4Axis.R2].Value;
							outputData[++outIdx] = hidReport[Ds4Axis.L2].Value;
						}
						else
						{
							outputData[++outIdx] = hidReport[Ds4Axis.L2].Value;
							outputData[++outIdx] = hidReport[Ds4Axis.R2].Value;
						}

						outIdx++;
					}
					break;
				default:
					return false; //we only support DS3 and DS4
			}

			//DS4 only: touchpad points
			for (int i = 0; i < 2; i++)
			{
				var tpad = hidReport.TrackPadTouch0;
				if (tpad != null && i > 0)
					tpad = hidReport.TrackPadTouch1;

				if (tpad != null)
				{
					outputData[outIdx++] = tpad.IsActive ? (byte)1 : (byte)0;
					outputData[outIdx++] = (byte)tpad.Id;
					Array.Copy(BitConverter.GetBytes((ushort)tpad.X), 0, outputData, outIdx, 2);
					outIdx += 2;
					Array.Copy(BitConverter.GetBytes((ushort)tpad.Y), 0, outputData, outIdx, 2);
					outIdx += 2;
				}
				else
					outIdx += 6;
			}

			//motion timestamp
			Array.Copy(BitConverter.GetBytes((ulong)hidReport.Timestamp), 0, outputData, outIdx, 8);
			outIdx += 8;

			//accelerometer
			{
				var accel = hidReport.Accelerometer;
				if (accel != null)
				{
					Array.Copy(BitConverter.GetBytes((float)accel.X), 0, outputData, outIdx, 4);
					outIdx += 4;
					Array.Copy(BitConverter.GetBytes((float)accel.Y), 0, outputData, outIdx, 4);
					outIdx += 4;
					Array.Copy(BitConverter.GetBytes((float)accel.Z), 0, outputData, outIdx, 4);
					outIdx += 4;
				}
				else
					outIdx += 12;
			}			

			//gyroscope
			{
				var gyro = hidReport.Gyroscope;
				if (gyro != null)
				{
					Array.Copy(BitConverter.GetBytes((float)gyro.Pitch), 0, outputData, outIdx, 4);
					outIdx += 4;
					Array.Copy(BitConverter.GetBytes((float)gyro.Yaw), 0, outputData, outIdx, 4);
					outIdx += 4;
					Array.Copy(BitConverter.GetBytes((float)gyro.Roll), 0, outputData, outIdx, 4);
					outIdx += 4;
				}
				else
					outIdx += 12;
			}

			return true;
		}

		public void NewReportIncoming(ScpHidReport hidReport)
		{
			if (!running)
				return;

			var clientsList = new List<IPEndPoint>();
			var now = DateTime.UtcNow;
			lock (clients)
			{
				var clientsToDelete = new List<IPEndPoint>();

				foreach (var cl in clients)
				{
					const double TimeoutLimit = 5;

					if ((now - cl.Value.AllPadsTime).TotalSeconds < TimeoutLimit)
						clientsList.Add(cl.Key);
					else if ((hidReport.PadId >= DsPadId.One && hidReport.PadId <= DsPadId.Four) &&
							 (now - cl.Value.PadIdsTime[(byte)hidReport.PadId]).TotalSeconds < TimeoutLimit)
						clientsList.Add(cl.Key);
					else if (cl.Value.PadMacsTime.ContainsKey(hidReport.PadMacAddress) &&
							 (now - cl.Value.PadMacsTime[hidReport.PadMacAddress]).TotalSeconds < TimeoutLimit)
						clientsList.Add(cl.Key);
					else //check if this client is totally dead, and remove it if so
					{
						bool clientOk = false;
						for (int i=0; i<cl.Value.PadIdsTime.Length; i++)
						{
							var dur = (now - cl.Value.PadIdsTime[i]).TotalSeconds;
							if (dur < TimeoutLimit)
							{
								clientOk = true;
								break;
							}
						}
						if (!clientOk)
						{
							foreach (var dict in cl.Value.PadMacsTime)
							{
								var dur = (now - dict.Value).TotalSeconds;
								if (dur < TimeoutLimit)
								{
									clientOk = true;
									break;
								}
							}

							if (!clientOk)
								clientsToDelete.Add(cl.Key);
						}
					}
				}

				foreach (var delCl in clientsToDelete)
				{
					clients.Remove(delCl);
				}
				clientsToDelete.Clear();
				clientsToDelete = null;
			}

			if (clientsList.Count <= 0)
				return;

			byte[] outputData = new byte[100];
			int outIdx = BeginPacket(outputData, 1001);
			Array.Copy(BitConverter.GetBytes((uint)MessageType.DSUS_PadDataRsp), 0, outputData, outIdx, 4);
			outIdx += 4;

			outputData[outIdx++] = (byte)hidReport.PadId;
			outputData[outIdx++] = (byte)hidReport.PadState;
			outputData[outIdx++] = (byte)hidReport.Model;
			outputData[outIdx++] = (byte)hidReport.ConnectionType;
			{
				byte[] padMac = hidReport.PadMacAddress.GetAddressBytes();
				outputData[outIdx++] = padMac[0];
				outputData[outIdx++] = padMac[1];
				outputData[outIdx++] = padMac[2];
				outputData[outIdx++] = padMac[3];
				outputData[outIdx++] = padMac[4];
				outputData[outIdx++] = padMac[5];
			}			
			outputData[outIdx++] = (byte)hidReport.BatteryStatus;
			outputData[outIdx++] = hidReport.IsPadActive ? (byte)1 : (byte)0;

			Array.Copy(BitConverter.GetBytes((uint)hidReport.PacketCounter), 0, outputData, outIdx, 4);
			outIdx += 4;

			if (!ReportToBuffer(hidReport, outputData, ref outIdx))
				return;
			else
				FinishPacket(outputData);

			foreach (var cl in clientsList)
			{
				try { udpSock.SendTo(outputData, cl); }
				catch (SocketException ex) { }
			}
			clientsList.Clear();
			clientsList = null;
		}
	}
}
