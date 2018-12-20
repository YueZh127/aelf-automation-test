﻿using AElf.Automation.Common.Extensions;
using AElf.Automation.Common.Helpers;

namespace AElf.Automation.Common.Contracts
{
    public enum BenchmarkMethod
    {
        InitBalance,
        Transfer,
        GetBalance
    }
    
    public class BenchmarkContract : BaseContract
    {
        public BenchmarkContract(CliHelper ch, string account):
            base(ch, "AElf.Benchmark.TestContrat", account)
        {
        }

        public BenchmarkContract(CliHelper ch, string account, string contractAbi) :
            base(ch, "AElf.Benchmark.TestContrat", contractAbi)
        {
            Account = account;
        }

        public CommandInfo CallContractMethod(BenchmarkMethod method, params string[] paramArray)
        {
            return ExecuteContractMethodWithResult(method.ToString(), paramArray);
        }

        public void CallContractWithoutResult(BenchmarkMethod method, params string[] paramsArray)
        {
            ExecuteContractMethod(method.ToString(), paramsArray);
        }
    }
}