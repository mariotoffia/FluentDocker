#if COREFX
using System.Runtime.InteropServices;
#else
using System;
#endif

namespace Ductus.FluentDocker.Common
{
	public static class FdOs
	{

#if COREFX
		public static bool IsWindows()
			=> RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

		public static bool IsOsx()
			=> RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

		public static bool IsLinux()
			=> RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
#else
		public static bool IsWindows() 
			=> Environment.OSVersion.Platform != PlatformID.MacOSX && 
			   Environment.OSVersion.Platform != PlatformID.Unix;

		public static bool IsOsx()
			=> Environment.OSVersion.Platform == PlatformID.MacOSX;

		public static bool IsLinux()
			=> Environment.OSVersion.Platform == PlatformID.Unix;
#endif
	}
}
