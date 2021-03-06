namespace ScpControl.Shared.Core
{
    public class DsAccelerometer
    {
        public float X { get; set; }
		public float Y { get; set; }
		public float Z { get; set; }
    }

    public class DsGyroscope
    {
		public float Pitch { get; set; }
		public float Yaw { get; set; }
		public float Roll { get; set; }
    }
}
