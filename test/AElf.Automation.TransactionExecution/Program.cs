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

        private static WebApiHelper ApiHelper { get; set; }

        private static TokenExecutor Executor { get; set; }

        #endregion

        public static string Endpoint { get; set; } = "http://192.168.197.13:8100";

        static void Main(string[] args)
        {
            #region Basic Preparation
            //Init Logger
            string logName = "ContractTest_" + DateTime.Now.ToString("MMddHHmmss") + ".log";
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", logName);
            Logger.InitLogHelper(dir);

            ApiHelper = new WebApiHelper(Endpoint, AccountManager.GetDefaultDataDir());

            //Connect Chain
            var ci = new CommandInfo(ApiMethods.GetChainInformation);
            ApiHelper.ExecuteCommand(ci);
            Assert.IsTrue(ci.Result, "Connect chain got exception.");

            //Account preparation
            Users = new List<string>();

            for (var i = 0; i < 5; i++)
            {
                ci = new CommandInfo(ApiMethods.AccountNew) {Parameter = "123"};
                ci = ApiHelper.NewAccount(ci);
                if(ci.Result)
                    Users.Add(ci.InfoMsg.ToString().Replace("Account address:", "").Trim());

                //unlock
                var uc = new CommandInfo(ApiMethods.AccountUnlock)
                {
                    Parameter = $"{Users[i]} 123 notimeout"
                };
                ApiHelper.UnlockAccount(uc);
            }
            #endregion

            #region Transaction Execution
            Executor = new TokenExecutor(ApiHelper, Users[0]);
            TokenAddress = Executor.Token.ContractAddress;

            //Transfer and check
            for (int i = 1; i < Users.Count; i++)
            {
                //Execute Transfer
                Executor.Token.ExecuteMethodWithResult(TokenMethod.Transfer, new TransferInput
                {
                    Symbol = "ELF",
                    Amount = i * 100,
                    To = Address.Parse(Users[i])
                });
                
                //Query Balance
                var balanceResult = Executor.Token.CallViewMethod<GetBalanceOutput>(TokenMethod.GetBalance, 
                    new GetBalanceInput
                    {
                        Symbol = "ELF",
                        Owner = Address.Parse(Users[i]),
                    });
                Console.WriteLine($"User: {Users[i]}, Balance: {balanceResult.Balance}");
            }

            #endregion

            Console.ReadLine();
        }
    }
}