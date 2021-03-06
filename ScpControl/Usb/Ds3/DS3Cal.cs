using System;
using System.Runtime.InteropServices;
using ScpControl.Driver;
using System.Net.NetworkInformation;
using ScpControl.Utilities;
using ScpControl.Database;
using log4net;
using System.Reflection;

namespace ScpControl.Usb.Ds3
{
	public class DS3CalData
	{
		public struct CalValue
		{
			public uint val1;
			public uint val2;
		}

		private CalValue _calX, _calY, _calZ, _calG;
		public CalValue X { get { return _calX; } }
		public CalValue Y { get { return _calY; } }
		public CalValue Z { get { return _calZ; } }
		public CalValue G { get { return _calG; } }

		public DS3CalData(byte[] eepromData)
		{
			int idx = 0x11; //starts here, has 8 big endian uint16s

			_calX.val1 = (uint)eepromData[idx + 1] + ((uint)eepromData[idx + 0] << 8); idx += 2;
			_calX.val2 = (uint)eepromData[idx + 1] + ((uint)eepromData[idx + 0] << 8); idx += 2;

			_calY.val1 = (uint)eepromData[idx + 1] + ((uint)eepromData[idx + 0] << 8); idx += 2;
			_calY.val2 = (uint)eepromData[idx + 1] + ((uint)eepromData[idx + 0] << 8); idx += 2;

			_calZ.val1 = (uint)eepromData[idx + 1] + ((uint)eepromData[idx + 0] << 8); idx += 2;
			_calZ.val2 = (uint)eepromData[idx + 1] + ((uint)eepromData[idx + 0] << 8); idx += 2;

			_calG.val1 = (uint)eepromData[idx + 1] + ((uint)eepromData[idx + 0] << 8); idx += 2;
			_calG.val2 = (uint)eepromData[idx + 1] + ((uint)eepromData[idx + 0] << 8); idx += 2;
		}
	}

	public class DS3CalLibrary : NativeLibraryWrapper<DS3CalLibrary>
	{
		private DS3CalLibrary()
		{
			LoadNativeLibrary("ds3cal", @"ds3cal\x86\ds3cal.dll", @"ds3cal\x64\ds3cal.dll");
		}

		internal int InitialCal(ushort last2, ushort last1, out byte outCalVal, IntPtr gyroStructPtr)
		{
			return InitialGyroCal(last2, last1, out outCalVal, gyroStructPtr);
		}

		internal int RuntimeCal(ushort gottenGyroVal, out ushort outGyroVal, out byte outCalVal, IntPtr gyroStructPtr)
		{
			return RuntimeGyroCal(gottenGyroVal, out outGyroVal, out outCalVal, gyroStructPtr);
		}

		internal bool CalStore(IntPtr gyroStructPtr, byte[] outBuffer, out int outLen)
		{
			return GyroCalStore(gyroStructPtr, outBuffer, out outLen) == 0;
		}

		internal bool CalLoad(IntPtr gyroStructPtr, byte[] srcBuffer, out int calWord)
		{
			return GyroCalLoad(gyroStructPtr, srcBuffer, out calWord) == 0;
		}

		internal IntPtr CalCreate()
		{
			return GyroCalCreate();
		}

		internal void CalDestroy(IntPtr gyroStructPtr)
		{
			GyroCalDestroy(gyroStructPtr);
		}

		#region P/Invoke

		[DllImport("ds3cal.dll", CallingConvention = CallingConvention.StdCall)]
		private static extern int InitialGyroCal(ushort last2, ushort last1, out byte outCalVal, IntPtr gyroStructPtr);

		[DllImport("ds3cal.dll", CallingConvention = CallingConvention.StdCall)]
		private static extern int RuntimeGyroCal(ushort gottenGyroVal, out ushort outGyroVal, out byte outCalVal, IntPtr gyroStructPtr);

		[DllImport("ds3cal.dll", CallingConvention = CallingConvention.StdCall)]
		private static extern int GyroCalStore(IntPtr gyroStructPtr, [Out] byte[] outBuffer, out int outLen);

		[DllImport("ds3cal.dll", CallingConvention = CallingConvention.StdCall)]
		private static extern int GyroCalLoad(IntPtr gyroStructPtr, [In] byte[] srcBuffer, out int calWord);

		[DllImport("ds3cal.dll", CallingConvention = CallingConvention.StdCall)]
		private static extern IntPtr GyroCalCreate();

		[DllImport("ds3cal.dll", CallingConvention = CallingConvention.StdCall)]
		private static extern void GyroCalDestroy(IntPtr gyroStructPtr);

		#endregion
	}

	public class DS3CalInstance
	{
		protected static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		private IntPtr _gyroStruct = IntPtr.Zero;
		private DS3CalData _calValues;
		private int _setReportFlags = 0;
		private byte _setReportCalByte = 0;
		private PhysicalAddress _deviceAddr;
		private byte _lastCalByteSaved;
		
		public DS3CalInstance(PhysicalAddress macAddr, byte[] eepromContents)
		{
			_gyroStruct = DS3CalLibrary.Instance.CalCreate();

			_calValues = new DS3CalData(eepromContents);
			_deviceAddr = macAddr;
			_lastCalByteSaved = 0;
		}

		~DS3CalInstance()
		{
			_calValues = null;

			if (_gyroStruct != IntPtr.Zero)
			{
				DS3CalLibrary.Instance.CalDestroy(_gyroStruct);
				_gyroStruct = IntPtr.Zero;
			}
		}

		public byte[] StateToBytes()
		{
			int reqLen = 0;
			DS3CalLibrary.Instance.CalStore(_gyroStruct,null,out reqLen);
			if (reqLen == 0)
				return null;

			var outBytes = new byte[reqLen];
			int storedLen = 0;
			if (!DS3CalLibrary.Instance.CalStore(_gyroStruct, outBytes, out storedLen) || storedLen != reqLen)
				return null;

			return outBytes;
		}

		public bool StateFromBytes(byte[] srcBuf, out byte loadedCalByte)
		{
			int tempCalWord = 0;
			var retVal = DS3CalLibrary.Instance.CalLoad(_gyroStruct, srcBuf, out tempCalWord);
			loadedCalByte = (byte)tempCalWord;
			return retVal;
		}

		public int InitialCal(byte[] buffer)
		{
			_setReportFlags = 0;
			if ((buffer[8] == 0x18 && buffer[9] == 0x18 && buffer[10] == 0x18 && buffer[11] == 0x18) ||
				(buffer[8] == 0x17 && buffer[9] == 0x17 && buffer[10] == 0x17 && buffer[11] == 0x17))
			{
				_setReportFlags |= 0x8;
			}

			const int idxCalibBytes = 0x26;
			if ((buffer[idxCalibBytes + 0] != 1 || buffer[idxCalibBytes + 1] != 2) &&
				(buffer[idxCalibBytes + 1] != 1 || buffer[idxCalibBytes + 2] != 2))
			{
				var retVal = DS3CalLibrary.Instance.InitialCal((ushort)_calValues.G.val2, (ushort)_calValues.G.val1, out _setReportCalByte, _gyroStruct);
				_lastCalByteSaved = _setReportCalByte;
				if (retVal != 0)
					return retVal;
			}
			else
			{
				_setReportFlags |= 0x10;
			}

			int numCalibFields = buffer[idxCalibBytes - 1];
			for (int i = 0; i < numCalibFields; i++)
			{
				int bufIdx = i + idxCalibBytes;
				if (bufIdx >= 49) //dont go outside buffer bounds
					break;

				if (buffer[bufIdx] == 7)
				{
					_setReportFlags |= 0x30;
					var retVal = DS3CalLibrary.Instance.InitialCal((ushort)_calValues.G.val2, (ushort)_calValues.G.val1, out _setReportCalByte, _gyroStruct);
					_lastCalByteSaved = _setReportCalByte;
					if (retVal != 0)
						return retVal;

					break;
				}
			}

			//speed up calibration by loading existing cal state if it exists
			byte[] calBytes = null;
			using (var db = new ScpDb())		
			{
				using (var tran = db.Engine.GetTransaction())
				{
					var dataRow = tran.Select<byte[], byte[]>(ScpDb.TableDS3Cal, _deviceAddr.GetAddressBytes());
					if (dataRow.Exists)
						calBytes = dataRow.Value;
				}
			}
			if (calBytes != null)
			{
				byte loadedCalByte = 0;
				if (StateFromBytes(calBytes,out loadedCalByte))
				{
					_setReportCalByte = loadedCalByte;
					_lastCalByteSaved = loadedCalByte;
					
					Log.InfoFormat("Loaded cal data for {0} cal byte now 0x{1}",_deviceAddr.AsFriendlyName(),loadedCalByte.ToString("X"));
				}
			}

			return 0;
		}

		public void ApplyCalToOutReport(byte[] outBuffer, int startOffs=0)
		{
			if ((_setReportFlags & 0x10) == 0) //sixaxis ? because these are used for motors in ds3
			{
				outBuffer[startOffs + 3] = 0xFF;
				outBuffer[startOffs + 4] = _setReportCalByte;
			}

			if ((_setReportFlags & 0x20) != 0)
			{
				outBuffer[startOffs + 5] = 0xFF;
				outBuffer[startOffs + 6] = _setReportCalByte;
			}
		}

		public void ApplyCalToInReport(byte[] inputReport, int startOffs = 0)
		{
			int idx = startOffs + 0x29;
			for (int i=0; i<3; i++) //accelerometer vals
			{
				uint axisVal = (uint)inputReport[idx + 1] + ((uint)inputReport[idx + 0] << 8); //its big endian in the report
				uint val1, val2;
				if (i == 0)
				{
					val1 = _calValues.X.val1;
					val2 = _calValues.X.val2;
				}
				else if (i == 1)
				{
					val1 = _calValues.Y.val1;
					val2 = _calValues.Y.val2;
				}
				else if (i == 2)
				{
					val1 = _calValues.Z.val1;
					val2 = _calValues.Z.val2;
				}
				else
				{
					val1 = 0;
					val2 = 0;
				}

				if (val1 != val2)
				{
					int valDiff = (int)val1 - (int)val2;
					int axisDiff = (int)axisVal - (int)val1;
					int acc = 113 * ((axisDiff * 1024) / valDiff) / 1024 + 512;
					axisVal = (uint)acc;
				}

				//put it back into the input report, also as big endian
				inputReport[idx + 0] = (byte)((axisVal >> 8) & 0x3F);
				inputReport[idx + 1] = (byte)((axisVal >> 0) & 0xFF);				
				idx += 2;
			}

			uint gyroVal = (uint)inputReport[idx + 1] + ((uint)inputReport[idx + 0] << 8); //its big endian in the report
			if ((_setReportFlags & 0x10) != 0 && (_setReportFlags & 0x20) == 0)
			{
				int gVal = (int)_calValues.G.val1 - (int)gyroVal;
				gVal += 512; //back to unsigned

				//clamp
				if (gVal < 0)
					gVal = 0;
				else if (gVal > 1023)
					gVal = 1023;

				gyroVal = (uint)gVal;
			}
			if ((_setReportFlags & 0x10) == 0 || (_setReportFlags & 0x20) != 0)
			{
				ushort outGyroVal = (ushort)gyroVal;
				byte outCalByte = _setReportCalByte;
				if (DS3CalLibrary.Instance.RuntimeCal((ushort)gyroVal,out outGyroVal,out outCalByte,_gyroStruct) == 0)
				{
					_setReportCalByte = outCalByte;
					if (outCalByte != _lastCalByteSaved) //the calculated value isnt the same as the last stored one
					{
						bool calDataUpdated = false;
						using (var db = new ScpDb())
						{
							var addrBytes = _deviceAddr.GetAddressBytes();
							using (var tran = db.Engine.GetTransaction())
							{
								var dataRow = tran.Select<byte[], byte[]>(ScpDb.TableDS3Data, addrBytes);
								if (dataRow.Exists) //dont save cal data if we dont have eeprom data, so we dont resume with false info
								{
									var calBytes = StateToBytes();
									if (calBytes != null)
									{
										tran.Insert(ScpDb.TableDS3Cal, _deviceAddr.GetAddressBytes(), calBytes);
										tran.Commit();
										calDataUpdated = true;
									}
								}
							}
						}
						if (calDataUpdated)
							Log.InfoFormat("Stored cal data for {0} new cal byte is 0x{1}",_deviceAddr.AsFriendlyName(),outCalByte.ToString("X"));

						_lastCalByteSaved = outCalByte;
					}
				}
			}
			if ((_setReportFlags & 0x20) != 0 || (_setReportFlags == 0x08)) //pre-dualshock sixaxis seem to need it inverted ?
			{
				//G needs sign inversion
				int gVal = 1023 - (int)gyroVal;
				
				//clamp
				if (gVal < 0)
					gVal = 0;
				else if (gVal > 1023)
					gVal = 1023;

				gyroVal = (uint)gVal;
			}

			//put it back into the input report, also as big endian
			inputReport[idx + 0] = (byte)((gyroVal >> 8) & 0x3F);
			inputReport[idx + 1] = (byte)((gyroVal >> 0) & 0xFF);			
			idx += 2;
		}
	}
}
