namespace Ductus.FluentDocker.Extensions
{
	public static class OsExtensions
	{
		public static string ToPlatformPath(this string path)
		{
			if (!Common.OperatingSystem.IsWindows())
			{
				return path;
			}

			if (path.Length > 2 && path[1] == ':' && path[2] == '\\')
			{
				return path.Replace('/', '\\');
			}

			return string.IsNullOrEmpty(path) ? path : $"{path[2]}:{path.Substring(3).Replace('/', '\\')}";
		}

		public static string ToMsysPath(this string path)
		{
			if (!Common.OperatingSystem.IsWindows())
			{
				return path;
			}

			return "//" + char.ToLower(path[0]) + path.Substring(2).Replace('\\', '/');
		}
	}
}