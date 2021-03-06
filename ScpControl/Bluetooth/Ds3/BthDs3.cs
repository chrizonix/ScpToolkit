using System;
using System.Net.NetworkInformation;
using System.Threading;
using ScpControl.ScpCore;
using ScpControl.Shared.Core;
using ScpControl.Usb.Ds3;
using ScpControl.Database;
using ScpControl.Utilities;
using ScpControl.Shared.Utilities;

namespace ScpControl.Bluetooth.Ds3
{
    /// <summary>
    ///     Represents a DualShock 3 controller connected via Bluetooth.
    /// </summary>
    public class BthDs3 : BthDevice
    {
        #region Private fields

        private byte _counterForLeds;
        private byte _ledStatus;
		private DS3CalInstance _cal;

        #endregion

        #region Public methods

        public override bool Start()
        {
            CanStartHid = false;
            State = DsState.Connected;

			byte[] eepromContents = null, statusContents = null;
			using (var db = new ScpDb())
			{
				using (var tran = db.Engine.GetTransaction())
				{
					var row = tran.Select<byte[],byte[]>(ScpDb.TableDS3Data, DeviceAddress.GetAddressBytes());
					if (row.Exists)
					{
						eepromContents = new byte[49];
						statusContents = new byte[49];

						var rowData = row.Value;
						Array.Copy(rowData, 0, eepromContents, 0, eepromContents.Length);
						Array.Copy(rowData, eepromContents.Length, statusContents, 0, statusContents.Length);
					}
				}
			}

			if (eepromContents != null && statusContents != null)
			{
				_cal = new DS3CalInstance(DeviceAddress, eepromContents);
				_cal.InitialCal(statusContents);
			}

			if (!IsFake && _cal == null)
				Log.WarnFormat("EEPROM data for DS3 controller {0} not present, please connect it via USB first!", DeviceAddress.AsFriendlyName());

			m_Queued = 1;
            m_Blocked = true;
            m_Last = DateTime.Now;
            BluetoothDevice.HID_Command(HciHandle.Bytes, Get_SCID(L2CAP.PSM.HID_Command), _hidCommandEnable);

            return base.Start();
        }

        /// <summary>
        ///     Interprets a HID report sent by a DualShock 3 device.
        /// </summary>
        /// <param name="report">The HID report as byte array.</param>
        public override void ParseHidReport(byte[] report)
        {
            if (report[10] == 0xFF) return;
			AccurateTime currentTime = AccurateTime.Now;

            m_PlugStatus = report[38];
            Battery = (DsBattery) report[39];
            m_CableStatus = report[40];

            if (m_Packet == 0) Rumble(0, 0);
            m_Packet++;

            var inputReport = NewHidReport();
            
            inputReport.PacketCounter = m_Packet;

			// convert accel/gyro values
			if (_cal != null)
				_cal.ApplyCalToInReport(report, 9);

            // copy controller data to report packet
            Buffer.BlockCopy(report, 9, inputReport.RawBytes, 8, 49);

			// DS3 does not have timestamp in reports, so just give system time
			inputReport.Timestamp = (long)Math.Round(currentTime.ToSeconds() * 1000000); //convert from s to us

            var trigger = false;

            // Quick Disconnect
            if (inputReport[Ds3Button.L1].IsPressed
                && inputReport[Ds3Button.R1].IsPressed
                && inputReport[Ds3Button.Ps].IsPressed)
            {
                // Set Trigger
                trigger = true;

                // Unset Pressed Buttons
                inputReport.Unset(Ds3Button.L1);
                inputReport.Unset(Ds3Button.R1);
                inputReport.Unset(Ds3Button.Ps);
            }

            if (inputReport.IsPadActive)
            {
                m_IsIdle = false;
            }
            else if (!m_IsIdle)
            {
                m_IsIdle = true;
                m_Idle = DateTime.Now;
            }

            if (trigger && !m_IsDisconnect)
            {
                m_IsDisconnect = true;
                m_Disconnect = DateTime.Now;
            }
            else if (!trigger && m_IsDisconnect)
            {
                m_IsDisconnect = false;
            }

            OnHidReportReceived(inputReport);
        }

        /// <summary>
        ///     Send a rumble request to the controller.
        /// </summary>
        /// <param name="large">Rumble with large (left) motor.</param>
        /// <param name="small">Rumble with small (right) motor.</param>
        /// <returns></returns>
        public override bool Rumble(byte large, byte small)
        {
            lock (_hidOutputReport)
            {
				//zero out marker and value bytes for rumble first
				_hidOutputReport[3] = 0;
				_hidOutputReport[4] = 0;
				_hidOutputReport[5] = 0;
				_hidOutputReport[6] = 0;

				if (_cal != null)
					_cal.ApplyCalToOutReport(_hidOutputReport, 2);

				if (_hidOutputReport[5] != 0xFF) //if not already used for cal
				{
					//set marker bytes for rumble
					_hidOutputReport[3] = 0xFF;
					_hidOutputReport[5] = 0xFF;

					if (GlobalConfiguration.Instance.DisableRumble)
					{
						_hidOutputReport[4] = 0;
						_hidOutputReport[6] = 0;
					}
					else
					{
						_hidOutputReport[4] = (byte)(small > 0 ? 0x01 : 0x00);
						_hidOutputReport[6] = large;
					}
				}

				_hidOutputReport[11] = _ledStatus;

                if (!m_Blocked && GlobalConfiguration.Instance.Latency == 0)
                {
                    m_Last = DateTime.Now;
                    m_Blocked = true;

                    BluetoothDevice.HID_Command(HciHandle.Bytes, Get_SCID(L2CAP.PSM.HID_Command), _hidOutputReport);
                }
                else
                {
                    m_Queued = 1;
                }
            }
            return true;
        }

        public override bool InitHidReport(byte[] report)
        {
            var retVal = false;

            if (m_Init < _hidInitReport.Length)
            {
                BluetoothDevice.HID_Command(HciHandle.Bytes, Get_SCID(L2CAP.PSM.HID_Service), _hidInitReport[m_Init++]);
            }
            else if (m_Init == _hidInitReport.Length)
            {
                m_Init++;
                retVal = true;
            }

            return retVal;
        }

        #endregion

        #region Protected methods

        protected override void Process(DateTime now)
        {
            if (!Monitor.TryEnter(_hidOutputReport) || State != DsState.Connected) return;

            try
            {
                #region LED manipulation

                if ((now - m_Tick).TotalMilliseconds >= 500 
                    && m_Packet > 0
                    && XInputSlot.HasValue)
                {
                    m_Tick = now;

                    if (m_Queued == 0) m_Queued = 1;

                    _ledStatus = 0;

                    switch (GlobalConfiguration.Instance.Ds3LEDsFunc)
                    {
                        case 0:
                            _ledStatus = 0;
                            break;
                        case 1:
                            if (GlobalConfiguration.Instance.Ds3PadIDLEDsFlashCharging && Battery == DsBattery.Low)
                            {
                                _counterForLeds++;
                                _counterForLeds %= 2;
                                if (_counterForLeds == 1)
                                    _ledStatus = _ledOffsets[(int) XInputSlot];
                            }
                            else _ledStatus = _ledOffsets[(int) XInputSlot];
                            break;
                        case 2:
                            switch (Battery)
                            {
                                case DsBattery.None:
                                    _ledStatus = (byte) (_ledOffsets[0] | _ledOffsets[3]);
                                    break;
                                case DsBattery.Dying:
                                    _ledStatus = (byte) (_ledOffsets[1] | _ledOffsets[2]);
                                    break;
                                case DsBattery.Low:
                                    _counterForLeds++;
                                    _counterForLeds %= 2;
                                    if (_counterForLeds == 1)
                                        _ledStatus = _ledOffsets[0];
                                    break;
                                case DsBattery.Medium:
                                    _ledStatus = (byte) (_ledOffsets[0] | _ledOffsets[1]);
                                    break;
                                case DsBattery.High:
                                    _ledStatus = (byte) (_ledOffsets[0] | _ledOffsets[1] | _ledOffsets[2]);
                                    break;
                                case DsBattery.Full:
                                case DsBattery.Ultra:
                                    _ledStatus =
                                        (byte) (_ledOffsets[0] | _ledOffsets[1] | _ledOffsets[2] | _ledOffsets[3]);
                                    break;
                                default:
                                    ;
                                    break;
                            }
                            break;
                        case 3:
                            if (GlobalConfiguration.Instance.Ds3LEDsCustom1) _ledStatus |= _ledOffsets[0];
                            if (GlobalConfiguration.Instance.Ds3LEDsCustom2) _ledStatus |= _ledOffsets[1];
                            if (GlobalConfiguration.Instance.Ds3LEDsCustom3) _ledStatus |= _ledOffsets[2];
                            if (GlobalConfiguration.Instance.Ds3LEDsCustom4) _ledStatus |= _ledOffsets[3];
                            break;
                        default:
                            _ledStatus = 0;
                            break;
                    }

                    _hidOutputReport[11] = _ledStatus;
                }

                #endregion

                #region Fake DS3 workaround

                // TODO: this works for some but breaks others, so... dafuq >_<
                if (IsFake)
                {
                    //_hidOutputReport[0] = 0xA2;
                    //_hidOutputReport[3] = 0xFF;
                    //_hidOutputReport[5] = 0x00;
                }

                #endregion

				if (_cal != null)
					_cal.ApplyCalToOutReport(_hidOutputReport, 2);

                if (m_Blocked || m_Queued <= 0) return;

                if (!((now - m_Last).TotalMilliseconds >= GlobalConfiguration.Instance.Latency)) return;

                m_Last = now;
                m_Blocked = true;
                m_Queued--;

                BluetoothDevice.HID_Command(HciHandle.Bytes, Get_SCID(L2CAP.PSM.HID_Command), _hidOutputReport);
            }
            finally
            {
                Monitor.Exit(_hidOutputReport);
            }
        }

        #endregion

        #region HID Reports

        private readonly byte[] _hidCommandEnable = {0x53, 0xF4, 0x42, 0x03, 0x00, 0x00};

        private readonly byte[][] _hidInitReport =
        {
            new byte[] {0x02, 0x00, 0x0F, 0x00, 0x08, 0x35, 0x03, 0x19, 0x12, 0x00, 0x00, 0x03, 0x00},
            new byte[]
            {
                0x04, 0x00, 0x10, 0x00, 0x0F, 0x00, 0x01, 0x00, 0x01, 0x00, 0x10, 0x35, 0x06, 0x09, 0x02, 0x01, 0x09,
                0x02,
                0x02, 0x00
            },
            new byte[]
            {0x06, 0x00, 0x11, 0x00, 0x0D, 0x35, 0x03, 0x19, 0x11, 0x24, 0x01, 0x90, 0x35, 0x03, 0x09, 0x02, 0x06, 0x00},
            new byte[]
            {
                0x06, 0x00, 0x12, 0x00, 0x0F, 0x35, 0x03, 0x19, 0x11, 0x24, 0x01, 0x90, 0x35, 0x03, 0x09, 0x02, 0x06,
                0x02,
                0x00, 0x7F
            },
            new byte[]
            {
                0x06, 0x00, 0x13, 0x00, 0x0F, 0x35, 0x03, 0x19, 0x11, 0x24, 0x01, 0x90, 0x35, 0x03, 0x09, 0x02, 0x06,
                0x02,
                0x00, 0x59
            },
            new byte[]
            {
                0x06, 0x00, 0x14, 0x00, 0x0F, 0x35, 0x03, 0x19, 0x11, 0x24, 0x01, 0x80, 0x35, 0x03, 0x09, 0x02, 0x06,
                0x02,
                0x00, 0x33
            },
            new byte[]
            {
                0x06, 0x00, 0x15, 0x00, 0x0F, 0x35, 0x03, 0x19, 0x11, 0x24, 0x01, 0x90, 0x35, 0x03, 0x09, 0x02, 0x06,
                0x02,
                0x00, 0x0D
            }
        };

        private readonly byte[] _ledOffsets = {0x02, 0x04, 0x08, 0x10};

        private readonly byte[] _hidOutputReport =
        {
            0x52, 0x01,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0x27, 0x10, 0x00, 0x32,
            0xFF, 0x27, 0x10, 0x00, 0x32,
            0xFF, 0x27, 0x10, 0x00, 0x32,
            0xFF, 0x27, 0x10, 0x00, 0x32,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00
        };

        #endregion

        #region Ctors

        public BthDs3(IBthDevice device, PhysicalAddress master, byte lsb, byte msb)
            : base(device, master, lsb, msb)
        {
        }

        #endregion
    }
}
