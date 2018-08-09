using PropertyChanged;
using ScpControl.Utilities;

namespace ScpDriverInstaller.View_Models
{
    [ImplementPropertyChanged]
    public class InstallationOptionsViewModel
    {
        public bool IsXbox360DriverNeeded
        {
            get { return !(OsInfoHelper.OsParse(OsInfoHelper.OsInfo) >= OsType.Win8); }
        }

        public bool InstallDs3ButtonEnabled { get; set; }
        public bool InstallBthButtonEnabled { get; set; }

        public bool SelfSignedDriversEnabled { get; set; }
        public bool ForceInstallEnabled { get; set; }

        public bool SelfSignedDriversBthEnabled { get; set; }
        public bool ForceInstallBthEnabled { get; set; }

        public InstallationOptionsViewModel()
        {
            InstallDs3ButtonEnabled = false;
            InstallBthButtonEnabled = false;

            SelfSignedDriversEnabled = false;
            ForceInstallEnabled = false;

            SelfSignedDriversBthEnabled = false;
            ForceInstallBthEnabled = false;
        }
    }
}
