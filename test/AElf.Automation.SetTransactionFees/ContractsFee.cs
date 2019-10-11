using System;
using System.Linq;
using Acs1;
using AElf.Automation.Common;
using AElf.Automation.Common.Contracts;
using AElf.Automation.Common.ContractSerializer;
using AElf.Automation.Common.Helpers;
using AElf.Automation.Common.Managers;
using log4net;

namespace AElf.Automation.SetTransactionFees
{
    public class ContractsFee
    {
        private INodeManager NodeManager { get; set; }
        private ContractHandler ContractHandler { get; set; }
        
        private GenesisContract Genesis { get; set; }
        
        private string Caller { get; set; }

        private static ILog Logger = Log4NetHelper.GetLogger();

        public ContractsFee(INodeManager nodeManager)
        {
            NodeManager = nodeManager;
            ContractHandler = new ContractHandler();
            Caller = NodeOption.AllNodes.First().Account;
            Genesis = nodeManager.GetGenesisContract(Caller); 
        }

        public void SetAllContractsMethodFee(long amount)
        {
            var authority = new AuthorityManager(NodeManager);
            var genesisOwner = authority.GetGenesisOwnerAddress();
            var miners = authority.GetCurrentMiners();
            var systemContracts = Genesis.GetAllSystemContracts();
            
            foreach (var provider in GenesisContract.NameProviderInfos.Keys)
            {
                Logger.Info($"Begin set contract: {provider}");
                var contractInfo = ContractHandler.GetContractInfo(provider);
                var contractAddress = systemContracts[provider];
                
                var contractFee = new ContractMethodFee(NodeManager, authority, contractInfo, contractAddress.GetFormatted());
                contractFee.SetContractFees(NodeOption.NativeTokenSymbol, amount, genesisOwner, miners, Caller);
            }
        }

        public void QueryAllContractsMethodFee()
        {
            var systemContracts = Genesis.GetAllSystemContracts();
            foreach (var provider in GenesisContract.NameProviderInfos.Keys)
            {
                Logger.Info($"Query contract fees: {provider}");
                var contractInfo = ContractHandler.GetContractInfo(provider);
                var contractAddress = systemContracts[provider];
                foreach (var method in contractInfo.ActionMethodNames)
                {
                    var feeResult = NodeManager.QueryView<TokenAmounts>(Caller, contractAddress.GetFormatted(),
                        "GetMethodFee", new MethodName
                        {
                            Name = method
                        });
                    if (feeResult.Amounts.Count > 0)
                    {
                        var amountInfo = feeResult.Amounts.First();
                        Logger.Info($"Method: {method.PadRight(48)} Symbol: {amountInfo.Symbol}   Amount: {amountInfo.Amount}");
                    }
                    else
                    {
                        Logger.Warn($"Method: {method.PadRight(48)} Symbol: {NodeOption.NativeTokenSymbol}   Amount: 0");
                    }
                }
                Console.WriteLine();
            }
        }
    }
}