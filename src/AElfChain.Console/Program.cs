﻿using System;
using System.Linq;
using AElfChain.Common;
using AElfChain.Common.Helpers;
using AElfChain.Common.Managers;
using AElfChain.SDK;
using log4net;
using Sharprompt;
using McMaster.Extensions.CommandLineUtils;
using Volo.Abp.Threading;
using Prompt = Sharprompt.Prompt;

namespace AElfChain.Console
{
    [Command(Name = "AElf CLI Tool", Description = "AElf console client test tool for transaction testing.")]
    [HelpOption("-?")]
    internal class Program
    {
        private static INodeManager NodeManager;

        [Option("-e|--endpoint", Description = "Service endpoint url of node. It's required parameter.")]
        private static string Endpoint { get; set; }

        [Option("-c|--config", Description = "Config file about bp nodes setting")]
        private static string ConfigFile { get; set; }

        private static IApiService ApiService => NodeManager.ApiService;
        private static ILog Logger { get; set; }

        public static int Main(string[] args)
        {
            return CommandLineApplication.Execute<Program>(args);
        }

        private void OnExecute(CommandLineApplication app)
        {
            Log4NetHelper.LogInit();
            Logger = Log4NetHelper.GetLogger("Program");

            if (ConfigFile != null) NodeInfoHelper.SetConfig(ConfigFile);

            if (Endpoint == null)
            {
                var nodes = NodeInfoHelper.Config.Nodes.Select(o => $"{o.Name} [{o.Endpoint}]").ToList();
                var command = Prompt.Select("Select Endpoint", nodes);
                Endpoint = command.Split("[").Last().Replace("]", "");
            }
            
            try
            {
                NodeManager = new NodeManager(Endpoint);
                var chainStatusDto = AsyncHelper.RunSync(ApiService.GetChainStatusAsync);
                Logger.Info(
                    $"ChainId: {chainStatusDto.ChainId}, LongestChainHeight: {chainStatusDto.LongestChainHeight}, LastIrreversibleBlockHeight: {chainStatusDto.LastIrreversibleBlockHeight}");

                var cliCommand = new CliCommand(NodeManager);
                cliCommand.ExecuteTransactionCommand();
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
            }
        }
    }
}