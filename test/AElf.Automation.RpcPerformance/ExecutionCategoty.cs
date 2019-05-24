﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AElf.Automation.Common.Helpers;
using AElf.Automation.Common.WebApi.Dto;
using AElf.Contracts.MultiToken.Messages;
using AElf.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace AElf.Automation.RpcPerformance
{
    public class AccountInfo
    {
        public string Account { get; }
        public int Increment { get; set; }

        public AccountInfo(string account)
        {
            Account = account;
            Increment = 0;
        }
    }

    public class Contract
    {
        public string ContractPath { get; }
        public string Symbol { get; set; }
        public int AccountId { get; }

        public Contract(int accId, string contractPath)
        {
            AccountId = accId;
            ContractPath = contractPath;
        }
    }

    public class ExecutionCategory
    {
        #region Public Property

        public IApiHelper ApiHelper { get; private set; }
        private ExecutionSummary Summary { get; }
        public string BaseUrl { get; }
        private List<AccountInfo> AccountList { get; }
        private string KeyStorePath { get; }
        private long BlockHeight { get; set; }
        private List<Contract> ContractList { get; }
        private List<string> TxIdList { get; }
        public int ThreadCount { get; }
        public int ExeTimes { get; }
        private ConcurrentQueue<string> DeployContractList { get; }
        private readonly ILogHelper _logger = LogHelper.GetLogHelper();

        #endregion
        
        public ExecutionCategory(int threadCount,
            int exeTimes,
            string baseUrl = "http://127.0.0.1:8000",
            string keyStorePath = "")
        {
            if (keyStorePath == "")
                keyStorePath = GetDefaultDataDir();

            AccountList = new List<AccountInfo>();
            ContractList = new List<Contract>();
            DeployContractList = new ConcurrentQueue<string>();
            TxIdList = new List<string>();
            ThreadCount = threadCount;
            BlockHeight = 1;
            ExeTimes = exeTimes;
            KeyStorePath = keyStorePath;
            BaseUrl = baseUrl;
            Summary = new ExecutionSummary(baseUrl);
        }

        public void InitExecCommand()
        {
            _logger.WriteInfo("Rpc Url: {0}", BaseUrl);
            _logger.WriteInfo("Key Store Path: {0}", Path.Combine(KeyStorePath, "keys"));
            _logger.WriteInfo("Prepare new and unlock accounts.");
            ApiHelper = new WebApiHelper(BaseUrl, KeyStorePath);

            //Connect Chain
            var ci = new CommandInfo(ApiMethods.GetChainInformation);
            ApiHelper.ExecuteCommand(ci);
            Assert.IsTrue(ci.Result, "Connect chain got exception.");

            //New
            NewAccounts(200);

            //Unlock Account
            UnlockAllAccounts(ThreadCount);
        }

        public void DeployContracts()
        {
            var contractList = new List<object>();
            for (var i = 0; i < ThreadCount; i++)
            {
                dynamic info = new System.Dynamic.ExpandoObject();
                info.Id = i;
                info.Account = AccountList[i].Account;

                var ci = new CommandInfo(ApiMethods.DeploySmartContract)
                {
                    Parameter = $"AElf.Contracts.MultiToken {AccountList[i].Account}"
                };
                ApiHelper.ExecuteCommand(ci);
                Assert.IsTrue(ci.Result);
                var transactionResult = ci.InfoMsg as BroadcastTransactionOutput;
                var txId = transactionResult?.TransactionId;
                Assert.IsFalse(string.IsNullOrEmpty(txId), "Transaction Id is null or empty");
                info.TxId = txId;
                info.Result = false;
                contractList.Add(info);
            }

            var count = 0;
            var checkTimes = 30;

            while (checkTimes>0)
            {
                checkTimes--;
                Thread.Sleep(2000);
                foreach (dynamic item in contractList)
                {
                    if (item.Result != false) continue;
                    
                    var ci = new CommandInfo(ApiMethods.GetTransactionResult) {Parameter = item.TxId};
                    ApiHelper.ExecuteCommand(ci);
                    Assert.IsTrue(ci.Result);
                    var transactionResult = ci.InfoMsg as TransactionResultDto;
                    var deployResult = transactionResult?.Status;
                    switch (deployResult)
                    {
                        case "Mined":
                        {
                            count++;
                            item.Result = true;
                            var contractPath= transactionResult.ReadableReturnValue.Replace("\"","");
                            ContractList.Add(new Contract(item.Id, contractPath));
                            AccountList[item.Id].Increment = 1;
                            break;
                        }
                        case "Failed":
                            _logger.WriteError("Transaction failed.");
                            _logger.WriteError(transactionResult.Error);
                            break;
                        default:
                            continue;
                    }

                    Thread.Sleep(10);
                }

                if (count == contractList.Count)
                    return;
            }
            Assert.IsFalse(true, "Deployed contract not executed successfully.");
        }

        public void InitializeContracts()
        {
            //create all token
            foreach (var contract in ContractList)
            {
                var account = AccountList[contract.AccountId].Account;
                var contractPath = contract.ContractPath;

                var symbol = $"ELF{RandomString(4, false)}";
                contract.Symbol = symbol;
                var ci = new CommandInfo(ApiMethods.BroadcastTransaction, account, contractPath, "Create")
                {
                    ParameterInput = new CreateInput
                    {
                        Symbol = symbol,
                        TokenName = $"elf token {GetRandomIncrementId()}",
                        TotalSupply = long.MaxValue,
                        Decimals = 2,
                        Issuer = Address.Parse(account),
                        IsBurnable = true
                    }
                };
                ApiHelper.ExecuteCommand(ci);
                Assert.IsTrue(ci.Result);
                var transactionResult = ci.InfoMsg as BroadcastTransactionOutput;
                var transactionId = transactionResult?.TransactionId;
                Assert.IsFalse(string.IsNullOrEmpty(transactionId), "Transaction Id is null or empty");
                TxIdList.Add(transactionId);
            }

            CheckResultStatus(TxIdList);
            
            //issue all token
            foreach (var contract in ContractList)
            {
                var account = AccountList[contract.AccountId].Account;
                var contractPath = contract.ContractPath;
                var symbol = contract.Symbol;

                var ci = new CommandInfo(ApiMethods.BroadcastTransaction, account, contractPath, "Issue")
                {
                    ParameterInput = new IssueInput()
                    {
                        Amount = long.MaxValue,
                        Memo = $"Issue all balance to owner - {Guid.NewGuid()}",
                        Symbol = symbol,
                        To = Address.Parse(account)
                    }
                };
                ApiHelper.ExecuteCommand(ci);
                Assert.IsTrue(ci.Result);
                var transactionResult = ci.InfoMsg as BroadcastTransactionOutput;
                var transactionId = transactionResult?.TransactionId;
                Assert.IsFalse(string.IsNullOrEmpty(transactionId), "Transaction Id is null or empty");
                TxIdList.Add(transactionId);
            }
            
            CheckResultStatus(TxIdList);
        }
        
        public void ExecuteOneRoundTransactionTask()
        {
            _logger.WriteInfo("Start transaction execution at: {0}", DateTime.Now.ToString(CultureInfo.InvariantCulture));
            var exec = new Stopwatch();
            exec.Start();
            var contractTasks = new List<Task>();
            for (var i = 0; i < ThreadCount; i++)
            {
                var j = i;
                contractTasks.Add(Task.Run(() => ExecuteTransactionTask(j, ExeTimes)));
            }

            Task.WaitAll(contractTasks.ToArray<Task>());
            
            exec.Stop();
            _logger.WriteInfo("End transaction execution at: {0}", DateTime.Now.ToString(CultureInfo.InvariantCulture));
            _logger.WriteInfo("Execution time: {0}", exec.ElapsedMilliseconds);
            GetExecutedAccount();
        }

        public void ExecuteOneRoundTransactionsTask()
        {
            _logger.WriteInfo("Start all generate rpc request at: {0}", DateTime.Now.ToString(CultureInfo.InvariantCulture));
            var exec = new Stopwatch();
            exec.Start();
            var contractTasks = new List<Task>();
            for (var i = 0; i < ThreadCount; i++)
            {
                var j = i;
                contractTasks.Add(Task.Run(() =>
                { 
                    try
                    {
                        ExecuteBatchTransactionTask(j, ExeTimes);
                    }
                    catch (Exception e)
                    {
                        _logger.WriteInfo($"Execute batch transaction got exception, message details are: {e.Message}");
                    }
                }));
            }

            Task.WaitAll(contractTasks.ToArray<Task>());
            exec.Stop();
            _logger.WriteInfo("All rpc requests completed at: {0}", DateTime.Now.ToString(CultureInfo.InvariantCulture));
            _logger.WriteInfo("Execution time: {0}", exec.ElapsedMilliseconds);
        }
        
        public void ExecuteContinuousRoundsTransactionsTask(bool useTxs = false)
        {
            //add transaction performance check process
            var taskList = new List<Task>
            {
                Task.Run(() => { Summary.ContinuousCheckTransactionPerformance(); }),
                Task.Run(() => 
                {
                    _logger.WriteInfo("Begin generate multi rpc requests.");
                    try
                    {
                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        for (var r = 1; r > 0; r++) //continuous running
                        {
                            _logger.WriteInfo("Execution transaction request round: {0}", r);
                            if (useTxs)
                            {
                                //multi task for BroadcastTransactions query
                                var txsTasks = new List<Task>();
                                for (var i = 0; i < ThreadCount; i++)
                                {
                                    var j = i;
                                    txsTasks.Add(Task.Run(() => ExecuteBatchTransactionTask(j, ExeTimes)));
                                }

                                Task.WaitAll(txsTasks.ToArray<Task>());
                            }
                            else
                            {
                                //multi task for BroadcastTransaction query
                                for (var i = 0; i < ThreadCount; i++)
                                {
                                    var j = i;
                                    //Generate Rpc contracts
                                    GenerateRawTransactionQueue(j, ExeTimes);
                                    //Send Rpc contracts request
                                    _logger.WriteInfo("Begin execute group {0} transactions with 4 threads.", j + 1);
                                    var txTasks = new List<Task>();
                                    for (var k = 0; k < ThreadCount; k++)
                                    {
                                        txTasks.Add(Task.Run(() => ExecuteAloneTransactionTask(j)));
                                    }

                                    Task.WaitAll(txTasks.ToArray<Task>());
                                }
                            }

                            if (r % 3 != 0) continue;
                            
                            CheckNodeStatus();
                            
                            stopwatch.Stop();
                            TransactionSentPerSecond(ThreadCount * ExeTimes * 2 * 3, stopwatch.ElapsedMilliseconds);
                            stopwatch.Restart();
                        }
                    }
                    catch (Exception e)
                    {
                        var message = "Execute continuous transaction got exception." +
                                      $"\r\nMessage: {e.Message}" +
                                      $"\r\nStackTrace: {e.StackTrace}";
                        _logger.WriteError(message);
                    }
                })
            };

            Task.WaitAll(taskList.ToArray<Task>());
        }

        public void DeleteAccounts()
        {
            foreach (var item in AccountList)
            {
                var file = Path.Combine(KeyStorePath, $"{item.Account}.ak");
                File.Delete(file);
            }
        }
        
        public void PrintContractInfo()
        {
            _logger.WriteInfo("Execution account and contract address information:");
            var count = 0;
            foreach (var item in ContractList)
            {
                count++;
                _logger.WriteInfo("{0:00}. Account: {1}, ContractAddress:{2}", count, AccountList[item.AccountId].Account,
                    item.ContractPath);
            }
        }

        #region Private Method
        
        //Without conflict group category
        private void ExecuteTransactionTask(int threadNo, int times)
        {
            var account = AccountList[ContractList[threadNo].AccountId].Account;
            var abiPath = ContractList[threadNo].ContractPath;

            var set = new HashSet<int>();
            var txIdList = new List<string>();
            var passCount = 0;
            for (var i = 0; i < times; i++)
            {
                var rd = new Random(DateTime.Now.Millisecond);
                var randNumber = rd.Next(ThreadCount, AccountList.Count);
                var countNo = randNumber;
                set.Add(countNo);
                var account1 = AccountList[countNo].Account;
                AccountList[countNo].Increment++;

                //Execute Transfer
                var ci = new CommandInfo(ApiMethods.BroadcastTransaction, account, abiPath, "Transfer")
                {
                    ParameterInput = new TransferInput
                    {
                        Symbol = ContractList[threadNo].Symbol,
                        Amount = (i + 1) % 4 + 1,
                        Memo = $"transfer test - {Guid.NewGuid()}",
                        To = Address.Parse(account1)
                    }
                };
                ApiHelper.ExecuteCommand(ci);

                if (ci.Result)
                {
                    var transactionResult = ci.InfoMsg as BroadcastTransactionOutput;
                    txIdList.Add(transactionResult?.TransactionId);
                    passCount++;
                }

                Thread.Sleep(10);
                //Get Balance Info
                ci = new CommandInfo(ApiMethods.BroadcastTransaction, account, abiPath, "GetBalance")
                {
                    ParameterInput = new GetBalanceInput
                    {
                        Symbol = ContractList[threadNo].Symbol, Owner = Address.Parse(account)
                    }
                };
                ApiHelper.ExecuteCommand(ci);

                if (ci.Result)
                {
                    Assert.IsTrue(ci.Result);
                    _logger.WriteInfo(JsonConvert.SerializeObject(ci.InfoMsg));
                    passCount++;
                }

                Thread.Sleep(10);
            }

            _logger.WriteInfo("Total contract sent: {0}, passed number: {1}", 2 * times, passCount);
            txIdList.Reverse();
            CheckResultStatus(txIdList);
            _logger.WriteInfo("{0} Transfer from Address {1}", set.Count, account);
        }

        private void ExecuteBatchTransactionTask(int threadNo, int times)
        {
            var account = AccountList[ContractList[threadNo].AccountId].Account;
            var contractPath = ContractList[threadNo].ContractPath;

            var set = new HashSet<int>();

            var rawTransactions = new List<string>();
            for (var i = 0; i < times; i++)
            {
                var rd = new Random(DateTime.Now.Millisecond);
                var randNumber = rd.Next(ThreadCount, AccountList.Count);
                var countNo = randNumber;
                set.Add(countNo);
                var account1 = AccountList[countNo].Account;
                AccountList[countNo].Increment++;

                //Execute Transfer
                var ci = new CommandInfo(ApiMethods.BroadcastTransaction, account, contractPath, "Transfer")
                {
                    ParameterInput = new TransferInput
                    {
                        Symbol = ContractList[threadNo].Symbol,
                        To = Address.Parse(account1),
                        Amount = (i + 1) % 4 + 1,
                        Memo = $"transfer test - {Guid.NewGuid()}"
                    }
                };
                var requestInfo = ApiHelper.GenerateTransactionRawTx(ci);
                rawTransactions.Add(requestInfo);

                //Get Balance Info
                ci = new CommandInfo(ApiMethods.BroadcastTransaction, account, contractPath, "GetBalance")
                {
                    ParameterInput = new GetBalanceInput
                    {
                        Symbol = ContractList[threadNo].Symbol, Owner = Address.Parse(account)
                    }
                };
                requestInfo = ApiHelper.GenerateTransactionRawTx(ci);
                rawTransactions.Add(requestInfo);
            }

            _logger.WriteInfo(
                "Thread [{0}] from account: {1} and contract address: {2} raw transactions generated completed.",
                threadNo, account, contractPath);
            //Send RPC Requests
            var ci1 = new CommandInfo(ApiMethods.BroadcastTransactions);
            foreach (var rawTransaction in rawTransactions)
            {
                ci1.Parameter += "," + rawTransaction;
            }

            ci1.Parameter = ci1.Parameter.Substring(1);
            ApiHelper.ExecuteCommand(ci1);
            Assert.IsTrue(ci1.Result);
            var transactions = (string[])ci1.InfoMsg;
            _logger.WriteInfo("Batch request count: {0}, passed transaction count: {1}", rawTransactions.Count, transactions.Length);
            _logger.WriteInfo("Thread [{0}] completed executed {1} times contracts work.", threadNo, times);
            Thread.Sleep(100);
        }

        private void GenerateRawTransactionQueue(int threadNo, int times)
        {
            var account = AccountList[ContractList[threadNo].AccountId].Account;
            var abiPath = ContractList[threadNo].ContractPath;

            var set = new HashSet<int>();
            for (var i = 0; i < times; i++)
            {
                var rd = new Random(DateTime.Now.Millisecond);
                var randNumber = rd.Next(ThreadCount, AccountList.Count);
                var countNo = randNumber;
                set.Add(countNo);
                var account1 = AccountList[countNo].Account;
                AccountList[countNo].Increment++;

                //Execute Transfer
                var ci = new CommandInfo(ApiMethods.BroadcastTransaction, account, abiPath, "Transfer")
                {
                    ParameterInput = new TransferInput
                    {
                        Symbol = ContractList[threadNo].Symbol,
                        To = Address.Parse(account1),
                        Amount = (i + 1) % 4 + 1,
                        Memo = $"transfer test - {Guid.NewGuid()}"
                    }
                };
                var requestInfo = ApiHelper.GenerateTransactionRawTx(ci);
                DeployContractList.Enqueue(requestInfo);

                //Get Balance Info
                ci = new CommandInfo(ApiMethods.BroadcastTransaction, account, abiPath, "GetBalance")
                {
                    ParameterInput = new GetBalanceInput
                    {
                        Symbol = ContractList[threadNo].Symbol, 
                        Owner = Address.Parse(account)
                    }
                };
                requestInfo = ApiHelper.GenerateTransactionRawTx(ci);
                DeployContractList.Enqueue(requestInfo);
            }
        }

        private void ExecuteAloneTransactionTask(int group)
        {
            while (true)
            {
                if (!DeployContractList.TryDequeue(out var rpcMsg))
                    break;
                _logger.WriteInfo("Transaction group: {0}, execution left: {1}", group+1, DeployContractList.Count);
                var ci = new CommandInfo(ApiMethods.BroadcastTransaction) {Parameter = rpcMsg};
                ApiHelper.ExecuteCommand(ci);
                Thread.Sleep(100);
            }
        }
        
        private void CheckNodeStatus()
        {
            var checkTimes = 0;
            while(true)
            {
                var ci = new CommandInfo(ApiMethods.GetBlockHeight);
                ApiHelper.GetBlockHeight(ci);
                var currentHeight = (long)ci.InfoMsg;

                if (BlockHeight != currentHeight)
                {
                    BlockHeight = currentHeight;
                    return;
                }

                checkTimes++;
                Thread.Sleep(500);
                if(checkTimes % 10 == 0)
                    _logger.WriteWarn($"Current block height {currentHeight}, not changed in {checkTimes/2} seconds.");
                
                if(checkTimes == 600)
                    Assert.IsTrue(false, "Node block exception, block height not changed 5 minutes later.");
            }
        }

        private void CheckResultStatus(IList<string> idList, int checkTimes = 60)
        {
            if(checkTimes<0)
                Assert.IsTrue(false, "Transaction status check is over time.");
            checkTimes--;
            var listCount = idList.Count;
            Thread.Sleep(2000);
            var length = idList.Count;
            for (var i = length - 1; i >= 0; i--)
            {
                var ci = new CommandInfo(ApiMethods.GetTransactionResult) {Parameter = idList[i]};
                ApiHelper.ExecuteCommand(ci);

                if (ci.Result)
                {
                    var transactionResult = ci.InfoMsg as TransactionResultDto;
                    var deployResult = transactionResult?.Status;
                    if (deployResult == "Mined")
                        idList.Remove(idList[i]);
                }

                Thread.Sleep(50);
            }

            if (idList.Count > 0 && idList.Count != 1)
            {
                _logger.WriteInfo("***************** {0} ******************", idList.Count);
                if (listCount == idList.Count && checkTimes == 0)
                    Assert.IsTrue(false, "Transaction not executed successfully.");
                CheckResultStatus(idList, checkTimes);
            }

            if (idList.Count == 1)
            {
                _logger.WriteInfo("Last one: {0}", idList[0]);
                var ci = new CommandInfo(ApiMethods.GetTransactionResult) {Parameter = idList[0]};
                ApiHelper.ExecuteCommand(ci);

                if (ci.Result)
                {
                    var transactionResult = ci.InfoMsg as TransactionResultDto;
                    var deployResult = transactionResult?.Status;
                    if (deployResult != "Mined")
                    {
                        Thread.Sleep(50);
                        CheckResultStatus(idList, checkTimes);
                    }
                }
            }

            Thread.Sleep(50);
        }

        private void UnlockAllAccounts(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var ci = new CommandInfo(ApiMethods.AccountUnlock)
                {
                    Parameter = $"{AccountList[i].Account} 123 notimeout"
                };
                ci = ApiHelper.ExecuteCommand(ci);
                Assert.IsTrue(ci.Result);
            }
        }

        private void NewAccounts(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var ci = new CommandInfo(ApiMethods.AccountNew) {Parameter = "123"};
                ci = ApiHelper.ExecuteCommand(ci);
                Assert.IsTrue(ci.Result);
                AccountList.Add(new AccountInfo(ci.InfoMsg.ToString()));
            }
        }

        private void GetExecutedAccount()
        {
            var accounts = AccountList.FindAll(x => x.Increment != 0);
            var count = 0;
            foreach (var item in accounts)
            {
                count++;
                _logger.WriteInfo("{0:000} Account: {1}, Execution times: {2}", count, item.Account, item.Increment);
            }
        }

        private static string GetDefaultDataDir()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aelf");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                var keyPath = Path.Combine(path, "keys");
                if (!Directory.Exists(keyPath))
                    Directory.CreateDirectory(keyPath);

                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string GetRandomIncrementId()
        {
            var random = new Random(DateTime.Now.Millisecond);
            return random.Next().ToString();
        }

        private static string RandomString(int size, bool lowerCase)
        {
            var random = new Random(DateTime.Now.Millisecond);
            var builder = new StringBuilder(size);
            var startChar = lowerCase ? 97 : 65;//65 = A / 97 = a
            for (var i = 0; i < size; i++)
                builder.Append((char)(26 * random.NextDouble() + startChar));
            return builder.ToString();
        }

        private void TransactionSentPerSecond(int transactionCount, long milliseconds)
        {
            var tx = (float) transactionCount;
            var time = (float) milliseconds;

            var result = tx * 1000 / time;
            
            _logger.WriteInfo($"Summary analyze: One round transaction request {transactionCount} transactions in {time/1000:0.000} seconds, average {result:0.00} tx/second.");
        }
        
        #endregion
    }
}