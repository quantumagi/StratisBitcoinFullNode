﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.Extensions;

namespace Stratis.Bitcoin.Features.RPC
{
    /// <summary>
    /// Configuration related to RPC interface.
    /// </summary>
    public class RpcSettings
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Indicates whether the RPC server is being used</summary>
        public bool Server { get; private set; }

        /// <summary>User name for RPC authorization.</summary>
        public string RpcUser { get; set; }

        /// <summary>Password for RPC authorization.</summary>
        public string RpcPassword { get; set; }

        /// <summary>TCP port for RPC interface.</summary>
        public int RPCPort { get; set; }

        /// <summary>Default bindings from config.</summary>
        public List<IPEndPoint> DefaultBindings => DefaultBinding(this.AllowIp.Any(), this.RPCPort);

        /// <summary>List of network endpoints that the node will listen and provide RPC on.</summary>
        public List<IPEndPoint> Bind { get; set; }

        /// <summary>List of IP addresses that are allowed to connect to RPC interfaces.</summary>
        public List<IPAddressBlock> AllowIp { get; set; }

        // TODO: Refactor static PrintHelp method.
        private static RpcSettings settingsHelp;

        private static List<IPEndPoint> DefaultBinding(bool haveAllowIps, int rpcPort)
        {
            var defaultBinding = new List<IPEndPoint>();

            if (haveAllowIps)
            {
                defaultBinding.Add(new IPEndPoint(IPAddress.Parse("::"), rpcPort));
                defaultBinding.Add(new IPEndPoint(IPAddress.Parse("0.0.0.0"), rpcPort));
            }
            else
            {
                defaultBinding.Add(new IPEndPoint(IPAddress.Parse("::1"), rpcPort));
                defaultBinding.Add(new IPEndPoint(IPAddress.Parse("127.0.0.1"), rpcPort));
            }

            return defaultBinding;
        }

        /// <summary>
        /// Initializes an instance of the object from the node configuration.
        /// </summary>
        /// <param name="nodeSettings">The node configuration.</param>
        /// <param name="defaultArgs">Allows overriding the default values before config and command line are applied.</param>
        public RpcSettings(NodeSettings nodeSettings, Action<RpcSettings> defaultArgs = null)
        {
            Guard.NotNull(nodeSettings, nameof(nodeSettings));

            this.logger = nodeSettings.LoggerFactory.CreateLogger(typeof(RpcSettings).FullName);

            this.RPCPort = nodeSettings.Network.DefaultRPCPort;
            this.Bind = new List<IPEndPoint>();
            this.AllowIp = new List<IPAddressBlock>();
            this.Server = false;
            this.RpcUser = null;
            this.RpcPassword = null;

            defaultArgs?.Invoke(this);

            // For printing help.
            // TODO: Refactor.
            settingsHelp = (RpcSettings)this.MemberwiseClone();

            // Get values from config and command line.
            this.LoadSettingsFromConfig(nodeSettings);

            // Check validity of settings
            this.CheckConfigurationValidity(nodeSettings.Logger);
        }

        /// <summary>
        /// Loads the RPC settings from the command line and application configuration.
        /// </summary>
        /// <param name="nodeSettings">Application configuration.</param>
        private void LoadSettingsFromConfig(NodeSettings nodeSettings)
        {
            TextFileConfiguration config = nodeSettings.ConfigReader;

            this.Server = config.GetOrDefault<bool>("server", this.Server, this.logger);
            this.RPCPort = config.GetOrDefault<int>("rpcport", this.RPCPort, this.logger);
            this.RpcUser = config.GetOrDefault<string>("rpcuser", this.RpcUser, this.logger);
            this.RpcPassword = config.GetOrDefault<string>("rpcpassword", this.RpcPassword); // No logging!

            try
            {
                List<IPAddressBlock> allowIp = config
                    .GetAll("rpcallowip", this.logger)
                    .Select(p => IPAddressBlock.Parse(p))
                    .ToList();

                if (allowIp.Count != 0)
                    this.AllowIp = allowIp;
            }
            catch (FormatException)
            {
                throw new ConfigurationException("Invalid rpcallowip value");
            }

            try
            {
                List<IPEndPoint> bind = config
                    .GetAll("rpcbind", this.logger)
                    .Select(p => p.ToIPEndPoint(this.RPCPort))
                    .ToList();

                if (bind.Count != 0)
                    this.Bind = bind;

                if (this.AllowIp.Count == 0)
                {
                    if (this.Bind.Count != 0)
                        this.logger.LogWarning("WARNING: RPC bind selection (-rpcbind) was ignored because allowed ip's (-rpcallowip) were not specified, refusing to allow everyone to connect");

                    this.Bind.Clear();
                }

                if (this.Bind.Count == 0)
                    this.Bind = this.DefaultBindings;
            }
            catch (FormatException)
            {
                throw new ConfigurationException("Invalid rpcbind value");
            }
        }

        /// <summary>
        /// Checks the validity of the RPC settings or forces them to be valid.
        /// </summary>
        /// <param name="logger">Logger to use.</param>
        private void CheckConfigurationValidity(ILogger logger)
        {
            // Check that the settings are valid.
            // (Note that these values will not be set if server = false in the config)
            if (this.Server)
            {
                if (this.RpcPassword == null)
                    throw new ConfigurationException("rpcpassword should be provided");

                if (this.RpcUser == null)
                    throw new ConfigurationException("rpcuser should be provided");
            }
        }

        /// <summary> Prints the help information on how to configure the rpc settings to the logger.</summary>
        /// <param name="network">The network to use.</param>
        public static void PrintHelp(Network network)
        {
            NodeSettings defaults = NodeSettings.Default(network);
            var builder = new StringBuilder();
            var allowIp = string.Join(" and ", settingsHelp.AllowIp.Select(ipab => $"'{ipab}'"));
            var bind = string.Join(" and ", DefaultBinding(settingsHelp.AllowIp.Any(), settingsHelp.RPCPort).Select(ep => $"'{ep}'"));

            builder.AppendLine($"-server=<0 or 1>          Accept command line and JSON-RPC commands. Default: {settingsHelp.Server}.");
            builder.AppendLine($"-rpcuser=<string>         Username for JSON-RPC connections. Default: '{settingsHelp.RpcUser}'.");
            builder.AppendLine($"-rpcpassword=<string>     Password for JSON-RPC connections");
            builder.AppendLine($"-rpcport=<0-65535>        Listen for JSON-RPC connections on <port>. Default: {settingsHelp.RPCPort}");
            builder.AppendLine($"-rpcbind=<ip:port>        Bind to given address to listen for JSON-RPC connections. This option can be specified multiple times. Default: {bind}");
            builder.AppendLine($"-rpcallowip=<ip>          Allow JSON-RPC connections from specified source. This option can be specified multiple times. Default: {allowIp}");

            defaults.Logger.LogInformation(builder.ToString());
        }

        /// <summary>
        /// Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            var allowIp = settingsHelp.AllowIp.Select(ipab => ipab.ToString());
            var bind = DefaultBinding(settingsHelp.AllowIp.Any(), settingsHelp.RPCPort).Select(ep => ep.ToString());

            var allowIpStr = string.Join(" and ", $"'{allowIp}'");
            var bindStr = string.Join(" and ", $"'{bind}'");

            builder.AppendLine($"####RPC Settings####");
            builder.AppendLine($"#Activate RPC Server (default: {settingsHelp.Server})");
            builder.AppendLine($"#server={(settingsHelp.Server ? 1 : 0)}");
            builder.AppendLine($"#Where the RPC Server binds (default: {bindStr})");
            foreach (var str in bind)
                builder.AppendLine($"#rpcbind={str}");
            builder.AppendLine($"#Ip address allowed to connect to RPC (default: {allowIpStr})");
            foreach (var str in allowIp)
                builder.AppendLine($"#rpcallowip={str}");
        }

        /// <summary>Obtains a list of HTTP URLs to RPC interfaces.</summary>
        /// <returns>List of HTTP URLs to RPC interfaces.</returns>
        public string[] GetUrls()
        {
            return this.Bind.Select(b => "http://" + b + "/").ToArray();
        }
    }
}
