using System;
using System.Collections.Generic;
using ScpControl.Shared.Win32;
using System.ComponentModel;

namespace ScpControl.Shared.Utilities
{
	public class AccurateTime
	{
		private long value;
		static private double scale;

		static AccurateTime() //figure out QPF only once
		{
			long currentQpf = 0;
			if (!Kernel32Natives.QueryPerformanceFrequency(out currentQpf))
				throw new Win32Exception();

			scale = 1.0 / (double)currentQpf;
		}

		public AccurateTime(long timeSpan)
		{
			value = timeSpan;
		}

		static public AccurateTime Now
		{
			get
			{
				long currentQpc;
				if (!Kernel32Natives.QueryPerformanceCounter(out currentQpc))
					throw new Win32Exception();

				return new AccurateTime(currentQpc);
			}
		}

		public static AccurateTime operator+(AccurateTime startTime, AccurateTime span)
		{
			return new AccurateTime(startTime.value + span.value);
		}
		public static AccurateTime operator-(AccurateTime startTime, AccurateTime span)
		{
			return new AccurateTime(startTime.value - span.value);
		}

		public double ToSeconds()
		{
			return (double)(value) * scale;
		}

		public override string ToString() 
		{
			return ToSeconds().ToString();
		}
	}
}
