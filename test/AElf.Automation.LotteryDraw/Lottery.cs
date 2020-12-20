using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AElf.Contracts.LotteryDemoContract;
using AElf.Contracts.MultiToken;
using AElf.Types;
using AElfChain.Common;
using AElfChain.Common.Contracts;
using AElfChain.Common.DtoExtension;
using AElfChain.Common.Helpers;
using Google.Protobuf.WellKnownTypes;
using log4net;
using Shouldly;
using Volo.Abp.Threading;

namespace AElf.Automation.LotteryTest
{
    public class Lottery
    {
        private static ILog Logger { get; set; } = Log4NetHelper.GetLogger();
        public string Symbol;
        public long Price;
        public readonly string Owner;
        public readonly string Password;
        public string LotteryContract;
        public Dictionary<string, int> RewardList;
        
        public EnvironmentInfo EnvironmentInfo { get; set; }
        public readonly LotteryDemoContract LotteryService;
        public readonly LotteryDemoContractContainer.LotteryDemoContractStub LotteryStub;

        public readonly TokenContract TokenService;
        public readonly ContractServices ContractServices;

        public Lottery(string rewards,string counts)
        {
            GetRewardList(rewards, counts);
            GetConfig();
            Owner = EnvironmentInfo.Owner;
            Password = EnvironmentInfo.Password;
            ContractServices = GetContractServices(LotteryContract);
            TokenService = ContractServices.TokenService;
            LotteryService = ContractServices.LotteryService;
            LotteryStub = LotteryService.GetTestStub<LotteryDemoContractContainer.LotteryDemoContractStub>(Owner);
            if (LotteryContract == "")
                InitializeLotteryDemoContract();
            if (!TokenService.GetTokenInfo(Symbol).Symbol.Equals(Symbol))
                CreateTokenAndIssue();
        }

        private ContractServices GetContractServices(string lotteryContract)
        {
            var config = NodeInfoHelper.Config;
            var firstNode = config.Nodes.First();
            var contractService = new ContractServices(firstNode.Endpoint, firstNode.Account, firstNode.Password,
                lotteryContract);
            return contractService;
        }

        private void GetConfig()
        {
            var config = ConfigHelper.Config;
            LotteryContract = config.LotteryContract;
            Symbol = config.TokenInfo.Symbol;
            Price = config.TokenInfo.Price;

            var testEnvironment = config.TestEnvironment;
            EnvironmentInfo =
                config.EnvironmentInfos.Find(o => o.Environment.Contains(testEnvironment));
            NodeInfoHelper.SetConfig(EnvironmentInfo.ConfigFile);
        }

        private void GetRewardList(string rewards,string counts)
        {
            var rewardList = rewards.Split(",").ToList();
            var countList = counts.Split(",").ToList();
            try
            {
                rewardList.Count.ShouldBe(countList.Count);
            }
            catch (Exception e)
            {
                Console.WriteLine("Please input correct reward list");
                throw;
            }
            
            RewardList = new Dictionary<string, int>();
            for (int i = 0; i < rewardList.Count; i++)
            {
                var c = int.Parse(countList[i]);
                RewardList.Add(rewardList[i],c);
            }
        }

        public void Buy()
        {
            var accounts = NodeInfoHelper.Config.Nodes;
            foreach (var account in accounts)
            {
                var buyer = account.Account;
                var amount = CommonHelper.GenerateRandomNumber(1, 10);
                var balance = TokenService.GetUserBalance(LotteryService.ContractAddress, Symbol);
                var userBeforeBalance = TokenService.GetUserBalance(buyer, Symbol);
                var userElfBalance = TokenService.GetUserBalance(buyer);
                if (userBeforeBalance < 10000_00000000)
                {
                    TokenService.SetAccount(Owner, Password);
                    TokenService.TransferBalance(Owner, buyer, 20000_00000000, Symbol, buyer);
                    userBeforeBalance = TokenService.GetUserBalance(buyer, Symbol);
                }
                if (userElfBalance < 1000_00000000)
                {
                    TokenService.SetAccount(accounts.First().Account);
                    TokenService.TransferBalance(accounts.First().Account, buyer, 10000_00000000);
                }
                
                TokenService.SetAccount(buyer);
                var approveResult = TokenService.ApproveToken(buyer, LotteryService.ContractAddress,
                    amount * Price, Symbol);
                approveResult.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
                var allowance = TokenService.GetAllowance(buyer, LotteryService.ContractAddress, Symbol);
                allowance.ShouldBeGreaterThanOrEqualTo(amount * Price);

                LotteryService.SetAccount(buyer);
                var result = LotteryService.ExecuteMethodWithResult(LotteryDemoMethod.Buy, new Int64Value
                {
                    Value = amount
                });
                result.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
                var resultValue =
                    BoughtLotteriesInformation.Parser.ParseFrom(ByteArrayHelper.HexStringToByteArray(result.ReturnValue));
                resultValue.Amount.ShouldBe(amount);
                Logger.Info($"start id is: {resultValue.StartId}");
                var userBalance = TokenService.GetUserBalance(buyer, Symbol);
                userBalance.ShouldBe(userBeforeBalance - amount * Price);
                var contractBalance = TokenService.GetUserBalance(LotteryService.ContractAddress, Symbol);
                contractBalance.ShouldBe(balance + amount * Price);
            }
        }

        public void Draw()
        {
            var amount = AsyncHelper.RunSync(()=> LotteryStub.GetMaximumBuyAmount.CallAsync(new Empty()));
            var period =  AsyncHelper.RunSync(()=> LotteryStub.GetCurrentPeriodNumber.CallAsync(new Empty()));
            Logger.Info($"Before draw period number is :{period.Value}");            
            
            LotteryService.SetAccount(Owner, Password);
            var result = LotteryService.ExecuteMethodWithResult(LotteryDemoMethod.PrepareDraw, new Empty());
            result.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
            SetRewardListForOnePeriod(period.Value);
            var block = result.BlockNumber;
            var currentBlock = AsyncHelper.RunSync(() => ContractServices.NodeManager.ApiClient.GetBlockHeightAsync());
            while (currentBlock < block + amount.Value)
            {
                Thread.Sleep(5000);
                Logger.Info("Waiting block");
                currentBlock = AsyncHelper.RunSync(() => ContractServices.NodeManager.ApiClient.GetBlockHeightAsync());
            }

            LotteryService.SetAccount(Owner, Password);
            var drawResult = LotteryService.ExecuteMethodWithResult(LotteryDemoMethod.Draw, new Int64Value
            {
                Value = period.Value
            });
            drawResult.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
            
            var afterPeriod =  AsyncHelper.RunSync(()=> LotteryStub.GetCurrentPeriodNumber.CallAsync(new Empty()));
            afterPeriod.Value.ShouldBe(period.Value + 1);
            Logger.Info($"After draw period number is :{afterPeriod.Value}");            
        }
        
        private void SetRewardListForOnePeriod(long period)
        {
            var result = LotteryService.ExecuteMethodWithResult(LotteryDemoMethod.SetRewardListForOnePeriod, new RewardsInfo
            {
                Period = period,
                Rewards =
                {
                    RewardList
                }
            });
            result.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
        }

        private void InitializeLotteryDemoContract()
        {
            LotteryService.SetAccount(Owner, Password);
            var result =
                LotteryService.ExecuteMethodWithResult(LotteryDemoMethod.Initialize,
                    new InitializeInput
                    {
                        TokenSymbol = Symbol,
                        Price = Price
                    });
            result.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
        }

        private void CreateTokenAndIssue()
        {
            var result = TokenService.ExecuteMethodWithResult(TokenMethod.Create, new CreateInput
            {
                Symbol = Symbol,
                TotalSupply = 10_00000000_00000000,
                Decimals = 8,
                Issuer = Owner.ConvertAddress(),
                IsBurnable = true,
                TokenName = "LOT"
            });
            result.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
            TokenService.IssueBalance(Owner, Owner, 10_00000000_00000000, Symbol);
        }
    }
}