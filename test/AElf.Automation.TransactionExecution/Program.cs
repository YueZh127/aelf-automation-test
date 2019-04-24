﻿using System;
using System.Collections.Generic;
using System.IO;
using AElf.Automation.Common.Contracts;
using AElf.Automation.Common.Extensions;
using AElf.Automation.Common.Helpers;
using AElf.Contracts.MultiToken.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AElf.Automation.TransactionExecution
{
    class Program
    {
        #region Private Properties

        private static readonly ILogHelper Logger = LogHelper.GetLogHelper();
        private static string TokenAddress { get; set; }
        private static List<string> Users { get; set; }

        private static CliHelper CH { get; set; }

        private static TokenExecutor Executor { get; set; }

        #endregion

        public static string Endpoint { get; set; } = "http://192.168.197.13:8000/chain";

        static void Main(string[] args)
        {
            #region Basic Preparation
            //Init Logger
            string logName = "ContractTest_" + DateTime.Now.ToString("MMddHHmmss") + ".log";
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", logName);
            Logger.InitLogHelper(dir);

            CH = new CliHelper(Endpoint, AccountManager.GetDefaultDataDir());

            //Connect Chain
            var ci = new CommandInfo(ApiMethods.GetChainInformation);
            CH.ExecuteCommand(ci);
            Assert.IsTrue(ci.Result, "Connect chain got exception.");

            //Account preparation
            Users = new List<string>();

            for (int i = 0; i < 5; i++)
            {
                ci = new CommandInfo(ApiMethods.AccountNew) {Parameter = "123"};
                ci = CH.NewAccount(ci);
                if(ci.Result)
                    Users.Add(ci.InfoMsg?[0].Replace("Account address:", "").Trim());

                //unlock
                var uc = new CommandInfo(ApiMethods.AccountUnlock);
                uc.Parameter = $"{Users[i]} 123 notimeout";
                CH.UnlockAccount(uc);
            }
            #endregion

            #region Transaction Execution
            Executor = new TokenExecutor(CH, Users[0]);
            TokenAddress = Executor.Token.ContractAbi;

            //Transfer and check
            for (int i = 1; i < Users.Count; i++)
            {
                //Execute Transfer
                Executor.Token.CallContractMethod(TokenMethod.Transfer, new TransferInput
                {
                    Amount = (long)(i * 100),
                    To = Address.Parse(Users[i])
                });
                //Query Balance
                var balanceResult = Executor.Token.CallReadOnlyMethod(TokenMethod.GetBalance, 
                    new GetBalanceInput
                    {
                        Symbol = "ELF",
                        Owner = Address.Parse(Users[i]),
                    });
                var balance = Executor.Token.ConvertViewResult(balanceResult, true);
                Console.WriteLine($"User: {Users[i]}, Balance: {balance}");
            }

            #endregion

            Console.ReadLine();
        }
    }
}