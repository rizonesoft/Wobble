param([string]$Drive = 'E:')
Add-Type @'
using System; using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles;
public static class Cd {
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] i, int il, byte[] o, int ol, out int ret, IntPtr ov);
  public static string Ioctl(string drive, uint code, byte[] input, int outLen) {
    using (var h = CreateFile(@"\\.\" + drive, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
      if (h.IsInvalid) return "open failed " + Marshal.GetLastWin32Error();
      var o = new byte[outLen]; int r;
      if (!DeviceIoControl(h, code, input, input == null ? 0 : input.Length, o, outLen, out r, IntPtr.Zero)) return "ioctl failed " + Marshal.GetLastWin32Error();
      return r + " bytes: " + BitConverter.ToString(o, 0, r);
    }
  }
}
'@
# IOCTL_CDROM_READ_TOC_EX = 0x24054; input CDROM_READ_TOC_EX { Format:4, Reserved:3, Msf:1; SessionTrack; Reserved2; Reserved3 }
"TOC  fmt0 msf : " + [Cd]::Ioctl($Drive, 0x24054, [byte[]](0x80,1,0,0), 804)
"TOC  fmt0 lba : " + [Cd]::Ioctl($Drive, 0x24054, [byte[]](0x00,1,0,0), 804)
"SESS fmt1 lba : " + [Cd]::Ioctl($Drive, 0x24054, [byte[]](0x01,0,0,0), 12)
"FULL fmt2 msf : " + [Cd]::Ioctl($Drive, 0x24054, [byte[]](0x82,1,0,0), 804)
"GET_LAST_SESSION: " + [Cd]::Ioctl($Drive, 0x24038, $null, 12)
"READ_TOC (old): " + [Cd]::Ioctl($Drive, 0x24000, $null, 804)
