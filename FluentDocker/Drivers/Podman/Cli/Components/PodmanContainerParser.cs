using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentDocker.Model.Containers;
using Newtonsoft.Json.Linq;
using Container = FluentDocker.Model.Containers.Container;
using ContainerState = FluentDocker.Model.Containers.ContainerState;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
    /// <summary>
    /// Utility class for parsing Podman CLI JSON output into container model objects.
    /// Extracted from PodmanCliContainerDriver to separate parsing concerns from driver API.
    /// </summary>
    public static class PodmanContainerParser
    {
        #region JSON Parsing

        public static IList<Container> ParseContainerList(string json)
        {
            var containers = new List<Container>();
            if (string.IsNullOrWhiteSpace(json)) return containers;

            var trimmed = json.Trim();
            if (trimmed.StartsWith("["))
            {
                var arr = JArray.Parse(trimmed);
                foreach (var token in arr)
                    containers.Add(ParseContainerFromListToken(token));
            }
            else
            {
                foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    containers.Add(ParseContainerFromListToken(JObject.Parse(line.Trim())));
            }

            return containers;
        }

        private static Container ParseContainerFromListToken(JToken token)
        {
            var names = token["Names"] ?? token["Name"];
            string name = null;
            if (names is JArray namesArr && namesArr.Count > 0)
                name = namesArr[0].Value<string>();
            else if (names != null)
                name = names.Value<string>();

            return new Container
            {
                Id = token["Id"]?.Value<string>() ?? token["ID"]?.Value<string>(),
                Image = token["Image"]?.Value<string>(),
                Name = name,
                State = new ContainerState
                {
                    Status = token["State"]?.Value<string>() ?? token["Status"]?.Value<string>()
                }
            };
        }

        public static Container ParseContainerInspect(string json)
        {
            var trimmed = json.Trim();
            JToken token;
            if (trimmed.StartsWith("["))
                token = JArray.Parse(trimmed).First;
            else
                token = JObject.Parse(trimmed);

            return new Container
            {
                Id = token["Id"]?.Value<string>() ?? token["ID"]?.Value<string>(),
                Image = token["Image"]?.Value<string>(),
                Name = token["Name"]?.Value<string>(),
                Created = ParseDateTime(token["Created"]),
                ResolvConfPath = token["ResolvConfPath"]?.Value<string>(),
                HostnamePath = token["HostnamePath"]?.Value<string>(),
                HostsPath = token["HostsPath"]?.Value<string>(),
                LogPath = token["LogPath"]?.Value<string>(),
                RestartCount = token["RestartCount"]?.Value<int>() ?? 0,
                Driver = token["Driver"]?.Value<string>(),
                Args = ParseStringArray(token["Args"]),
                State = ParseContainerState(token["State"]),
                Config = ParseContainerConfig(token["Config"]),
                Mounts = ParseMounts(token["Mounts"]),
                NetworkSettings = ParseNetworkSettings(token["NetworkSettings"])
            };
        }

        public static ContainerState ParseContainerState(JToken stateToken)
        {
            if (stateToken == null) return new ContainerState();

            return new ContainerState
            {
                Status = stateToken["Status"]?.Value<string>(),
                Running = stateToken["Running"]?.Value<bool>() ?? false,
                Paused = stateToken["Paused"]?.Value<bool>() ?? false,
                Restarting = stateToken["Restarting"]?.Value<bool>() ?? false,
                OOMKilled = stateToken["OOMKilled"]?.Value<bool>() ?? false,
                Dead = stateToken["Dead"]?.Value<bool>() ?? false,
                Pid = stateToken["Pid"]?.Value<int>() ?? 0,
                ExitCode = stateToken["ExitCode"]?.Value<int>() ?? 0,
                Error = stateToken["Error"]?.Value<string>(),
                StartedAt = ParseDateTime(stateToken["StartedAt"]),
                FinishedAt = ParseDateTime(stateToken["FinishedAt"]),
                Health = ParseHealth(stateToken["Health"] ?? stateToken["Healthcheck"])
            };
        }

        public static Health ParseHealth(JToken healthToken)
        {
            if (healthToken == null) return null;

            var statusStr = healthToken["Status"]?.Value<string>();
            HealthState status;
            if (string.IsNullOrEmpty(statusStr))
                status = HealthState.Unknown;
            else if (!Enum.TryParse(statusStr, ignoreCase: true, out status))
                status = HealthState.Unknown;

            var health = new Health
            {
                Status = status,
                FailingStreak = healthToken["FailingStreak"]?.Value<int>() ?? 0
            };

            var logArray = healthToken["Log"] as JArray;
            if (logArray != null)
            {
                health.Log = logArray.Select(entry => new HealthLog
                {
                    Start = entry["Start"]?.Value<string>(),
                    End = entry["End"]?.Value<string>(),
                    ExitCode = entry["ExitCode"]?.Value<int>() ?? 0,
                    Output = entry["Output"]?.Value<string>()
                }).ToList();
            }

            return health;
        }

        public static ContainerConfig ParseContainerConfig(JToken configToken)
        {
            if (configToken == null) return null;

            return new ContainerConfig
            {
                Hostname = configToken["Hostname"]?.Value<string>(),
                DomainName = configToken["DomainName"]?.Value<string>()
                             ?? configToken["Domainname"]?.Value<string>(),
                User = configToken["User"]?.Value<string>(),
                AttachStdin = configToken["AttachStdin"]?.Value<bool>() ?? false,
                AttachStdout = configToken["AttachStdout"]?.Value<bool>() ?? false,
                AttachStderr = configToken["AttachStderr"]?.Value<bool>() ?? false,
                Tty = configToken["Tty"]?.Value<bool>() ?? false,
                OpenStdin = configToken["OpenStdin"]?.Value<bool>() ?? false,
                StdinOnce = configToken["StdinOnce"]?.Value<bool>() ?? false,
                Image = configToken["Image"]?.Value<string>(),
                WorkingDir = configToken["WorkingDir"]?.Value<string>(),
                StopSignal = configToken["StopSignal"]?.Value<string>(),
                Env = ParseStringArray(configToken["Env"]),
                Cmd = ParseStringOrArray(configToken["Cmd"]),
                EntryPoint = ParseStringOrArray(
                    configToken["Entrypoint"] ?? configToken["EntryPoint"]),
                ExposedPorts = ParseExposedPorts(configToken["ExposedPorts"]),
                Labels = ParseStringDictionary(configToken["Labels"])
            };
        }

        public static ContainerMount[] ParseMounts(JToken mountsToken)
        {
            if (mountsToken is not JArray mountsArray || mountsArray.Count == 0)
                return Array.Empty<ContainerMount>();

            return mountsArray.Select(m => new ContainerMount
            {
                Name = m["Name"]?.Value<string>(),
                Source = m["Source"]?.Value<string>(),
                Destination = m["Destination"]?.Value<string>(),
                Driver = m["Driver"]?.Value<string>(),
                Mode = m["Mode"]?.Value<string>(),
                RW = m["RW"]?.Value<bool>() ?? false,
                Propagation = m["Propagation"]?.Value<string>()
            }).ToArray();
        }

        public static ContainerNetworkSettings ParseNetworkSettings(JToken nsToken)
        {
            if (nsToken == null) return null;

            return new ContainerNetworkSettings
            {
                Bridge = nsToken["Bridge"]?.Value<string>(),
                SandboxID = nsToken["SandboxID"]?.Value<string>(),
                HairpinMode = nsToken["HairpinMode"]?.Value<bool>() ?? false,
                LinkLocalIPv6Address = nsToken["LinkLocalIPv6Address"]?.Value<string>(),
                LinkLocalIPv6PrefixLen = nsToken["LinkLocalIPv6PrefixLen"]?.Value<string>(),
                SandboxKey = nsToken["SandboxKey"]?.Value<string>(),
                SecondaryIPAddresses = nsToken["SecondaryIPAddresses"]?.Value<string>(),
                SecondaryIPv6Addresses = nsToken["SecondaryIPv6Addresses"]?.Value<string>(),
                EndpointID = nsToken["EndpointID"]?.Value<string>(),
                Gateway = nsToken["Gateway"]?.Value<string>(),
                GlobalIPv6Address = nsToken["GlobalIPv6Address"]?.Value<string>(),
                GlobalIPv6PrefixLen = nsToken["GlobalIPv6PrefixLen"]?.Value<string>(),
                IPAddress = nsToken["IPAddress"]?.Value<string>(),
                IPPrefixLen = nsToken["IPPrefixLen"]?.Value<string>(),
                IPv6Gateway = nsToken["IPv6Gateway"]?.Value<string>(),
                MacAddress = nsToken["MacAddress"]?.Value<string>(),
                Ports = ParsePorts(nsToken["Ports"]),
                Networks = ParseNetworks(nsToken["Networks"])
            };
        }

        public static Dictionary<string, HostIpEndpoint[]> ParsePorts(JToken portsToken)
        {
            if (portsToken is not JObject portsObj) return null;

            var result = new Dictionary<string, HostIpEndpoint[]>();
            foreach (var prop in portsObj.Properties())
            {
                if (prop.Value is JArray bindings && bindings.Count > 0)
                {
                    result[prop.Name] = bindings.Select(b => new HostIpEndpoint
                    {
                        HostIp = b["HostIp"]?.Value<string>(),
                        HostPort = b["HostPort"]?.Value<string>()
                    }).ToArray();
                }
                else
                {
                    result[prop.Name] = Array.Empty<HostIpEndpoint>();
                }
            }

            return result;
        }

        public static Dictionary<string, BridgeNetwork> ParseNetworks(JToken networksToken)
        {
            if (networksToken is not JObject networksObj) return null;

            var result = new Dictionary<string, BridgeNetwork>();
            foreach (var prop in networksObj.Properties())
            {
                var n = prop.Value;
                result[prop.Name] = new BridgeNetwork
                {
                    NetworkID = n["NetworkID"]?.Value<string>(),
                    EndpointID = n["EndpointID"]?.Value<string>(),
                    Gateway = n["Gateway"]?.Value<string>(),
                    IPAddress = n["IPAddress"]?.Value<string>(),
                    IPPrefixLen = n["IPPrefixLen"]?.Value<int>() ?? 0,
                    IPv6Gateway = n["IPv6Gateway"]?.Value<string>(),
                    GlobalIPv6Address = n["GlobalIPv6Address"]?.Value<string>(),
                    GlobalIPv6PrefixLen = n["GlobalIPv6PrefixLen"]?.Value<int>() ?? 0,
                    MacAddress = n["MacAddress"]?.Value<string>(),
                    Aliases = ParseStringArray(n["Aliases"])
                };
            }

            return result;
        }

        #endregion

        #region Parsing Helpers

        /// <summary>
        /// Parses a JToken that may be a JSON array of strings or a single string value.
        /// Handles the Podman quirk where fields like EntryPoint and Cmd can be either format.
        /// </summary>
        public static string[] ParseStringOrArray(JToken token)
        {
            if (token == null) return null;

            if (token is JArray arr)
                return arr.Select(t => t.Value<string>()).ToArray();

            var str = token.Value<string>();
            return str != null ? new[] { str } : null;
        }

        internal static string[] ParseStringArray(JToken token)
        {
            if (token is not JArray arr) return null;
            return arr.Select(t => t.Value<string>()).ToArray();
        }

        internal static IDictionary<string, string> ParseStringDictionary(JToken token)
        {
            if (token is not JObject obj) return null;
            return obj.Properties().ToDictionary(p => p.Name, p => p.Value.Value<string>());
        }

        internal static IDictionary<string, object> ParseExposedPorts(JToken token)
        {
            if (token is not JObject obj) return null;
            return obj.Properties().ToDictionary(p => p.Name, p => (object)new { });
        }

        internal static DateTime ParseDateTime(JToken token)
        {
            if (token == null) return default;

            if (token.Type == JTokenType.Date)
                return ((DateTime)token).ToUniversalTime();

            var str = token.Value<string>();
            if (string.IsNullOrEmpty(str)) return default;

            return DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dto)
                ? dto.UtcDateTime
                : default;
        }

        #endregion
    }
}
