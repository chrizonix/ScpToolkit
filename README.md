# ScpToolkit for Sony DualShock 3/4 Controllers
Windows Driver and XInput Wrapper for Sony DualShock 3/4 Controllers

## Changelog
Changes to Official Release v1.7.277.16103-BETA ([nefarius/ScpToolkit](https://github.com/nefarius/ScpToolkit))
 * Added Support for DualShock 4 v2 (USB\VID_054C&PID_09CC)
 * Added Custom Color Support for Battery Status (DualShock 4)
 * Using `ScpControl.ini` for DualShock Controller Detection
 * Updated Clean Wipe Utility to Remove Hidden Devices

## Credits
### Community
 * Previous Author of ScpToolkit "Nefarius". Follow his [Recent Projects](https://vigem.org/) and [Support him on Patreon](https://www.patreon.com/nefarius)!
 * Major props to [original author Scarlet.Crush](http://forums.pcsx2.net/User-Scarlet-Crush) for developing this awesome toolkit!
 * [Original PCSX2 forums thread](http://forums.pcsx2.net/Thread-XInput-Wrapper-for-DS3-and-Play-com-USB-Dual-DS2-Controller)

### Used libraries and other 3rd party code
 * [NSIS Theme by Rodrigo "RRUZ" Ruz](https://github.com/RRUZ/vcl-styles-plugins)
 * [MadMilkman.Ini parsing library by Mario Z.](https://github.com/MarioZ/MadMilkman.Ini)
 * [reactivesockets library by Daniel Cazzulino](https://github.com/clariuslabs/reactivesockets)
 * [Windows Driver Installer library for USB devices](https://github.com/pbatard/libwdi)
 * [PortableSettingsProvider](https://github.com/crdx/PortableSettingsProvider)
 * [log4net logging library](https://logging.apache.org/log4net/)
 * [Libarius .NET library](https://github.com/nefarius/Libarius)
 * [Rx networking library](https://github.com/clariuslabs/reactivesockets)
 * [libusbK driver package](https://code.google.com/p/usb-travis/)
 * [irrKlang cross platform sound library](http://www.ambiera.com/irrklang/index.html)
 * [Metro Light and Dark Themes for WPF](http://brianlagunas.com/free-metro-light-and-dark-themes-for-wpf-and-silverlight-microsoft-controls/)
 * [Fody/PropertyChanged](https://github.com/Fody/PropertyChanged)
 * `ScpCleanWipe` uses code from [DriverStore Explorer](https://driverstoreexplorer.codeplex.com/)
 * [AutoDependencyProperty.Fody](http://blog.angeloflogic.com/2014/12/no-more-dependencyproperty-with.html)
 * [HIDSharp library](http://www.zer7.com/software/hidsharp)
 * [Windows Input Simulator](http://inputsimulator.codeplex.com/)
 * [AutoDependencyProperty.Fody](http://blog.angeloflogic.com/2014/12/no-more-dependencyproperty-with.html)
 * [LoadAssembliesOnStartup](https://github.com/Fody/LoadAssembliesOnStartup)
 * [Costura](https://github.com/Fody/Costura/)
 * [DBreeze NoSql embedded object DBMS](https://dbreeze.codeplex.com/)
 * [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)

## Installation requirements
 * Microsoft Windows Vista/7/8/8.1/10 x86 or amd64
 * [Microsoft .NET Framework 4.5](https://www.microsoft.com/en-US/download/details.aspx?id=42642)
 * [Microsoft Visual C++ 2010 Redistributable Package](http://www.microsoft.com/en-US/download/details.aspx?id=5555)
 * [Microsoft Visual C++ 2013 Runtime](https://www.microsoft.com/en-US/download/details.aspx?id=40784)
 * [DirectX Runtime](https://www.microsoft.com/en-us/download/details.aspx?DisplayLang=en&id=35)
 * [Xbox 360 Controller driver](https://www.microsoft.com/accessories/en-us/products/gaming/xbox-360-controller-for-windows/52a-00004#techspecs-connect)
 * Already integrated in Windows 8.x or greater
 * At least one supported Sony DualShock 3/4 controller (see **Compatible Controllers**)
 * Administrator rights *during driver setup*

### Compatible Controllers
 * **Original Sony DualShock 3 Controller**
   * `USB\VID_054C&PID_0268`
   * `USB\VID_054C&PID_042F`
 * **Original Sony DualShock 4 Controller**
   * `USB\VID_054C&PID_05C4`
   * `USB\VID_054C&PID_09CC`
 * See [**Compatible Controllers**](https://github.com/chrizonix/ScpToolkit/blob/master/ScpControl/ScpControl.ini)

### Compatible Bluetooth Devices (Optional)
 * A supported Bluetooth 2.0 (or higher) compatible USB dongle **with EDR** (**E**nhanced **D**ata **R**ate)
 * See [**Compatible Bluetooth Devices**](https://github.com/nefarius/ScpToolkit/wiki/Compatible-Bluetooth-Devices)
 * For DS4s to be useable the minimal supported Bluetooth version is **2.1**!

## Installation How-To
1. Connect your Dongle (only needed if you want to use it wirelessly) and controllers (yes, *all* of them) via USB and let Windows install its default drivers. **Leave them plugged in during the entire installation process!**
2. Download the [latest release of the ScpToolkit Setup](https://github.com/chrizonix/ScpToolkit/releases) to an arbitrary location on your PC.
3. Run the Setup and follow it's instructions. Should be fairly straight-forward.
4. Wait for the Setup to finish.
  * If you're performing a fresh installation, run the Driver Installer afterwards.
  * If you're upgrading from an older installation you may skip the Driver Installer.
5. When running the Driver Installer, choose your Bluetooth and controller devices you like to use with ScpToolkit.
6. The next step depends on your operating system:
 - Vista: check the Force Install option.
 - Win 7/8/8.1/10: leave it unchecked (or check if you're facing installation troubles, might help).
7. Click Install.
8. You're done!

![ScpToolkit Driver Installer](https://camo.githubusercontent.com/6dddb290ffa4db72e3bf5518cb4c5213e1bc64f9/687474703a2f2f6e656661726975732e61742f77702d636f6e74656e742f75706c6f6164732f323031332f31322f33312d31302d5f323031355f31332d32372d35352e706e67)
