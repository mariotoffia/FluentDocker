namespace Ductus.FluentDocker.Model.Drivers
{
    /// <summary>
    /// Hierarchical error codes for FluentDocker v3.0.0.
    /// </summary>
    public static class ErrorCodes
    {
        /// <summary>
        /// General error codes
        /// </summary>
        public static class General
        {
            public const string Unknown = "GEN_000";
            public const string InvalidOperation = "GEN_001";
            public const string InvalidArgument = "GEN_002";
            public const string Timeout = "GEN_003";
            public const string Cancelled = "GEN_004";
        }

        /// <summary>
        /// Driver-related error codes
        /// </summary>
        public static class Driver
        {
            public const string NotFound = "DRV_001";
            public const string AlreadyRegistered = "DRV_002";
            public const string NotAvailable = "DRV_003";
            public const string InitializationFailed = "DRV_004";
            public const string HealthCheckFailed = "DRV_005";
            public const string InterfaceNotSupported = "DRV_006";
            public const string CapabilityNotSupported = "DRV_007";
        }

        /// <summary>
        /// Container-related error codes
        /// </summary>
        public static class Container
        {
            public const string NotFound = "CNT_001";
            public const string AlreadyExists = "CNT_002";
            public const string StartFailed = "CNT_003";
            public const string StopFailed = "CNT_004";
            public const string RemoveFailed = "CNT_005";
            public const string CreateFailed = "CNT_006";
            public const string InspectFailed = "CNT_007";
            public const string InvalidState = "CNT_008";
            public const string AttachFailed = "CNT_009";
            public const string ExecFailed = "CNT_010";
        }

        /// <summary>
        /// Image-related error codes
        /// </summary>
        public static class Image
        {
            public const string NotFound = "IMG_001";
            public const string PullFailed = "IMG_002";
            public const string PushFailed = "IMG_003";
            public const string BuildFailed = "IMG_004";
            public const string RemoveFailed = "IMG_005";
            public const string TagFailed = "IMG_006";
            public const string InspectFailed = "IMG_007";
        }

        /// <summary>
        /// Network-related error codes
        /// </summary>
        public static class Network
        {
            public const string NotFound = "NET_001";
            public const string AlreadyExists = "NET_002";
            public const string CreateFailed = "NET_003";
            public const string RemoveFailed = "NET_004";
            public const string ConnectFailed = "NET_005";
            public const string DisconnectFailed = "NET_006";
            public const string InspectFailed = "NET_007";
            public const string PruneFailed = "NET_008";
        }

        /// <summary>
        /// Volume-related error codes
        /// </summary>
        public static class Volume
        {
            public const string NotFound = "VOL_001";
            public const string AlreadyExists = "VOL_002";
            public const string CreateFailed = "VOL_003";
            public const string RemoveFailed = "VOL_004";
            public const string InspectFailed = "VOL_005";
            public const string InUse = "VOL_006";
            public const string PruneFailed = "VOL_007";
        }

        /// <summary>
        /// Compose-related error codes
        /// </summary>
        public static class Compose
        {
            public const string FileNotFound = "CMP_001";
            public const string InvalidFormat = "CMP_002";
            public const string UpFailed = "CMP_003";
            public const string DownFailed = "CMP_004";
            public const string ValidationFailed = "CMP_005";
            public const string StartFailed = "CMP_006";
            public const string StopFailed = "CMP_007";
            public const string ListFailed = "CMP_008";
            public const string LogsFailed = "CMP_009";
            public const string ExecFailed = "CMP_010";
        }

        /// <summary>
        /// Configuration error codes
        /// </summary>
        public static class Config
        {
            public const string Invalid = "CFG_001";
            public const string Missing = "CFG_002";
            public const string ValidationFailed = "CFG_003";
        }
    }
}
