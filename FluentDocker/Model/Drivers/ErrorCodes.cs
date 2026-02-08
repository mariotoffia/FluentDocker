namespace FluentDocker.Model.Drivers
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
            public const string RestartFailed = "CNT_011";
            public const string PauseFailed = "CNT_012";
            public const string UnpauseFailed = "CNT_013";
            public const string KillFailed = "CNT_014";
            public const string WaitFailed = "CNT_015";
            public const string CopyFailed = "CNT_016";
            public const string ExportFailed = "CNT_017";
            public const string DiffFailed = "CNT_018";
            public const string TopFailed = "CNT_019";
            public const string RenameFailed = "CNT_020";
            public const string UpdateFailed = "CNT_021";
            public const string StatsFailed = "CNT_022";
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
            public const string HistoryFailed = "IMG_008";
            public const string SaveFailed = "IMG_009";
            public const string LoadFailed = "IMG_010";
            public const string ImportFailed = "IMG_011";
            public const string PruneFailed = "IMG_012";
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
            public const string Timeout = "NET_009";
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
            public const string RestartFailed = "CMP_011";
            public const string PauseFailed = "CMP_012";
            public const string UnpauseFailed = "CMP_013";
            public const string KillFailed = "CMP_014";
            public const string RemoveFailed = "CMP_015";
            public const string TopFailed = "CMP_016";
            public const string ConfigFailed = "CMP_017";
            public const string ImagesFailed = "CMP_018";
            public const string PortFailed = "CMP_019";
            public const string BuildFailed = "CMP_020";
            public const string PullFailed = "CMP_021";
            public const string PushFailed = "CMP_022";
            public const string RunFailed = "CMP_023";
            public const string ScaleFailed = "CMP_024";
            public const string CopyFailed = "CMP_025";
            public const string CreateFailed = "CMP_026";
        }

        /// <summary>
        /// Pod-related error codes (Podman-specific)
        /// </summary>
        public static class Pod
        {
            public const string NotFound = "POD_001";
            public const string CreateFailed = "POD_002";
            public const string RemoveFailed = "POD_003";
            public const string StartFailed = "POD_004";
            public const string StopFailed = "POD_005";
            public const string RestartFailed = "POD_006";
            public const string KillFailed = "POD_007";
            public const string PauseFailed = "POD_008";
            public const string UnpauseFailed = "POD_009";
            public const string InspectFailed = "POD_010";
            public const string ListFailed = "POD_011";
        }

        /// <summary>
        /// Kubernetes-related error codes (Podman kube play/down/generate)
        /// </summary>
        public static class Kubernetes
        {
            public const string PlayFailed = "K8S_001";
            public const string DownFailed = "K8S_002";
            public const string GenerateFailed = "K8S_003";
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

        /// <summary>
        /// Authentication-related error codes
        /// </summary>
        public static class Auth
        {
            public const string LoginFailed = "AUTH_001";
            public const string LogoutFailed = "AUTH_002";
            public const string InvalidCredentials = "AUTH_003";
            public const string RegistryNotFound = "AUTH_004";
        }

        /// <summary>
        /// Stack-related error codes (Docker Swarm / Kubernetes)
        /// </summary>
        public static class Stack
        {
            public const string NotFound = "STK_001";
            public const string ListFailed = "STK_002";
            public const string TasksFailed = "STK_003";
            public const string DeployFailed = "STK_004";
            public const string RemoveFailed = "STK_005";
            public const string ServicesFailed = "STK_006";
        }

        /// <summary>
        /// Service-related error codes (Docker Swarm / Kubernetes)
        /// </summary>
        public static class Service
        {
            public const string NotFound = "SVC_001";
            public const string CreateFailed = "SVC_002";
            public const string RemoveFailed = "SVC_003";
            public const string UpdateFailed = "SVC_004";
            public const string RollbackFailed = "SVC_005";
            public const string ListFailed = "SVC_006";
            public const string InspectFailed = "SVC_007";
            public const string TasksFailed = "SVC_008";
            public const string LogsFailed = "SVC_009";
            public const string ScaleFailed = "SVC_010";
        }
    }
}
