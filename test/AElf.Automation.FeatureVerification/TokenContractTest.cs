﻿using System.Collections.Generic;
using System.Threading.Tasks;
using AElfChain.Common;
using AElfChain.Common.Contracts;
using AElfChain.Common.Helpers;
using AElfChain.Common.Managers;
using AElf.Contracts.MultiToken;
using AElf.Contracts.TokenConverter;
using AElf.Kernel;
using AElf.Kernel.SmartContract.ExecutionPluginForAcs8.Tests.TestContract;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using log4net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using Connector = AElf.Contracts.TokenConverter.Connector;
using TokenSymbol = AElf.Contracts.TokenConverter.TokenSymbol;

namespace AElf.Automation.Contracts.ScenarioTest
{
    [TestClass]
    public class TokenContractTest
    {
        private ILog Logger { get; set; }
        private INodeManager NodeManager { get; set; }
        private AuthorityManager AuthorityManager { get; set; }

        private TokenContract _tokenContract;
        private GenesisContract _genesisContract;
        private TokenConverterContract _tokenConverterContract;
        private ParliamentAuthContract _parliamentAuthContract;
        private TokenContractContainer.TokenContractStub _tokenSub;
        private TokenContractContainer.TokenContractStub _bpTokenSub;
        private TokenContractContainer.TokenContractStub _testTokenSub;
        private ContractContainer.ContractStub _acs8Sub;
        private ExecutionPluginForAcs8Contract _acs8Contract;
        private TokenConverterContractContainer.TokenConverterContractStub _tokenConverterSub;
        private TokenConverterContractContainer.TokenConverterContractStub _testTokenConverterSub;

        private string InitAccount { get; } = "28Y8JA1i2cN6oHvdv7EraXJr9a1gY6D1PpJXw9QtRMRwKcBQMK";
        private string BpAccount { get; } = "28Y8JA1i2cN6oHvdv7EraXJr9a1gY6D1PpJXw9QtRMRwKcBQMK";
        private string TestAccount { get; } = "2oSMWm1tjRqVdfmrdL8dgrRvhWu1FP8wcZidjS6wPbuoVtxhEz";

//        private static string RpcUrl { get; } = "18.212.240.254:8000";

        private static string RpcUrl { get; } = "192.168.197.14:8000";
        private string Symbol { get; } = "TEST";

        private List<string> ResourceSymbol = new List<string>
            {"CPU", "NET", "DISK", "RAM", "READ", "WRITE", "STORAGE", "TRAFFIC"};

//        private List<string> SideChainSymbol = new List<string> {"EPC","EDA","EDB","EDC","EDD"};
        private List<string> SideChainSymbol = new List<string> {"STA", "STB"};

        [TestInitialize]
        public void Initialize()
        {
            Log4NetHelper.LogInit("ContractTest");
            Logger = Log4NetHelper.GetLogger();

            NodeManager = new NodeManager(RpcUrl);
            AuthorityManager = new AuthorityManager(NodeManager, InitAccount);
            _genesisContract = GenesisContract.GetGenesisContract(NodeManager, InitAccount);
            _tokenContract = _genesisContract.GetTokenContract(InitAccount);
            _parliamentAuthContract = _genesisContract.GetParliamentAuthContract(InitAccount);
            _tokenConverterContract = _genesisContract.GetTokenConverterContract(InitAccount);

            var tester = new ContractTesterFactory(NodeManager);
            _tokenSub = _genesisContract.GetTokenStub(InitAccount);
            _bpTokenSub = _genesisContract.GetTokenStub(BpAccount);
            _testTokenSub = _genesisContract.GetTokenStub(TestAccount);

            _tokenConverterSub = _genesisContract.GetTokenConverterStub(InitAccount);
            _testTokenConverterSub = _genesisContract.GetTokenConverterStub(TestAccount);
//            _acs8Contract = new ExecutionPluginForAcs8Contract(NodeManager,BpAccount,"rRf1ZbizAoWzYxHfBY9h3iMMiN3bYsXbUw81W3yF6UewripQu");
//            _acs8Contract = new ExecutionPluginForAcs8Contract(SideNode,BpAccount,"2F5C128Srw5rHCXoSY2C7uT5sAku48mkgiaTTp1Hiprhbb7ED9");
//            _acs8Sub = _acs8Contract.GetTestStub<ContractContainer.ContractStub>(BpAccount);
        }

        [TestMethod]
        public async Task NewStubTest_Call()
        {
            var tokenContractAddress =
                AddressHelper.Base58StringToAddress("WnV9Gv3gioSh3Vgaw8SSB96nV8fWUNxuVozCf6Y14e7RXyGaM");
            var tester = new ContractTesterFactory(NodeManager);
            var tokenStub = tester.Create<TokenContractContainer.TokenContractStub>(tokenContractAddress, InitAccount);
            var tokenInfo = await tokenStub.GetTokenInfo.CallAsync(new GetTokenInfoInput
            {
                Symbol = NodeOption.NativeTokenSymbol
            });
            tokenInfo.ShouldNotBeNull();
        }

        [TestMethod]
        public async Task NewStubTest_Execution()
        {
            var tokenContractAddress =
                AddressHelper.Base58StringToAddress("WnV9Gv3gioSh3Vgaw8SSB96nV8fWUNxuVozCf6Y14e7RXyGaM");
            var tester = new ContractTesterFactory(NodeManager);
            var tokenStub = tester.Create<TokenContractContainer.TokenContractStub>(tokenContractAddress, InitAccount);
            var transactionResult = await tokenStub.Transfer.SendAsync(new TransferInput
            {
                Amount = 100,
                Symbol = NodeOption.NativeTokenSymbol,
                To = AddressHelper.Base58StringToAddress(TestAccount),
                Memo = "Test transfer with new sdk"
            });
            transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            //query balance
            var result = await tokenStub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = AddressHelper.Base58StringToAddress(TestAccount),
                Symbol = NodeOption.NativeTokenSymbol
            });
            result.Balance.ShouldBeGreaterThanOrEqualTo(100);
        }

        [TestMethod]
        public void DeployContractWithAuthority_Test()
        {
            var authority = new AuthorityManager(NodeManager, TestAccount);
            var contractAddress = authority.DeployContractWithAuthority(TestAccount, "AElf.Contracts.MultiToken.dll");
            contractAddress.ShouldNotBeNull();
        }

        [TestMethod]
        public async Task BuySideChainToken()
        {
            var balance = await _testTokenSub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = AddressHelper.Base58StringToAddress(TestAccount),
                Symbol = NodeManager.GetNativeTokenSymbol()
            });
            var otherTokenBalance = await _testTokenSub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = AddressHelper.Base58StringToAddress(TestAccount),
                Symbol = "EPC"
            });

            Logger.Info($"user ELF balance is {balance} user EPC balance is {otherTokenBalance}");

            var result = await _testTokenConverterSub.Buy.SendAsync(new BuyInput
            {
                Amount = 1_00000000,
                Symbol = "EPC"
            });
            var size = result.Transaction.Size();
            Logger.Info($"transfer size is: {size}");

            var afterBalance = await _testTokenSub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = AddressHelper.Base58StringToAddress(TestAccount),
                Symbol = NodeManager.GetNativeTokenSymbol()
            });

            var afterOtherTokenBalance = await _testTokenSub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = AddressHelper.Base58StringToAddress(TestAccount),
                Symbol = "EPC"
            });

            Logger.Info(
                $"After buy token, user ELF balance is {afterBalance} user EPC balance is {afterOtherTokenBalance}");
        }

        [TestMethod]
        public async Task AddConnector()
        {
            var amount = 9000000_00000000;
            await CreateToken(amount);
            var input = new PairConnectorParam()
            {
                NativeWeight = "0.05",
                ResourceWeight = "0.05",
                ResourceConnectorSymbol = Symbol,
                NativeVirtualBalance = 100_0000_00000000
            };
            var organization = _parliamentAuthContract.GetGenesisOwnerAddress();
            var proposal = _parliamentAuthContract.CreateProposal(_tokenConverterContract.ContractAddress,
                nameof(TokenConverterMethod.AddPairConnector), input, organization, BpAccount);
            var miners = AuthorityManager.GetCurrentMiners();
            _parliamentAuthContract.MinersApproveProposal(proposal, miners);
            var result = _parliamentAuthContract.ReleaseProposal(proposal, BpAccount);
            result.Status.ShouldBe(TransactionResultStatus.Mined);

            var ELFamout = await GetNeededDeposit(amount);
            Logger.Info($"Need ELF : {ELFamout}");
            (await _bpTokenSub.Approve.SendAsync(new ApproveInput
            {
                Spender = _tokenConverterContract.Contract,
                Symbol = "ELF",
                Amount = ELFamout
            })).TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            (await _bpTokenSub.Approve.SendAsync(new ApproveInput
            {
                Spender = _tokenConverterContract.Contract,
                Symbol = Symbol,
                Amount = amount
            })).TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var buildInput = new ToBeConnectedTokenInfo()
            {
                TokenSymbol = Symbol,
                AmountToTokenConvert = amount
            };

            var enableConnector = await _tokenConverterSub.EnableConnector.SendAsync(buildInput);
            enableConnector.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var tokenConverterBalance = await _tokenSub.GetBalance.CallAsync(new GetBalanceInput
            {
                Symbol = Symbol,
                Owner = _tokenConverterContract.Contract
            });
            tokenConverterBalance.Balance.ShouldBe(amount);
        }

        [TestMethod]
        public void UpdateConnector()
        {
            var input = new Connector
            {
                Symbol = "STA",
                VirtualBalance = 100_0000_00000000
            };

            var organization = _parliamentAuthContract.GetGenesisOwnerAddress();
            var proposal = _parliamentAuthContract.CreateProposal(_tokenConverterContract.ContractAddress,
                nameof(TokenConverterMethod.UpdateConnector), input, organization, BpAccount);
            var miners = AuthorityManager.GetCurrentMiners();
            _parliamentAuthContract.MinersApproveProposal(proposal, miners);
            var result = _parliamentAuthContract.ReleaseProposal(proposal, BpAccount);
            result.Status.ShouldBe(TransactionResultStatus.Mined);
        }

        [TestMethod]
        public async Task EnableConnector()
        {
            var buildInput = new ToBeConnectedTokenInfo()
            {
                TokenSymbol = Symbol,
                AmountToTokenConvert = 0
            };

            var enableConnector = await _tokenConverterSub.EnableConnector.SendAsync(buildInput);
            enableConnector.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        }

        /*
        UpdateCoefficientFormContract,
        UpdateCoefficientFormSender,
        UpdateLinerAlgorithm,
        UpdatePowerAlgorithm,
        ChangeFeePieceKey,
        */

        [TestMethod]
        public void UpdateCoefficientFormContract()
        {
            var input = new CoefficientFromContract
            {
                FeeType = FeeTypeEnum.Storage,
                Coefficient = new CoefficientFromSender
                {
                    PieceKey = 1000000,
                    IsChangePieceKey = false,
                    IsLiner = true,
                    LinerCoefficient = new LinerCoefficient
                    {
                        ConstantValue = 1000,
                        Denominator = 2,
                        Numerator = 1
                    }
                }
            };
            var organization = _parliamentAuthContract.GetGenesisOwnerAddress();
            var proposal = _parliamentAuthContract.CreateProposal(_tokenContract.ContractAddress,
                nameof(TokenMethod.UpdateCoefficientFromContract), input, organization, InitAccount);
            var miners = AuthorityManager.GetCurrentMiners();
            _parliamentAuthContract.MinersApproveProposal(proposal, miners);
            var result = _parliamentAuthContract.ReleaseProposal(proposal, InitAccount);
            result.Status.ShouldBe(TransactionResultStatus.Mined);
        }

        [TestMethod]
        public void UpdateCoefficientFromSender()
        {
            var input = new CoefficientFromSender
            {
                PieceKey = 1000000,
                IsChangePieceKey = false,
                IsLiner = true,
                LinerCoefficient = new LinerCoefficient
                {
                    ConstantValue = 10000,
                    Numerator = 1,
                    Denominator = 400
                }
            };
            var organization = _parliamentAuthContract.GetGenesisOwnerAddress();
            var proposal = _parliamentAuthContract.CreateProposal(_tokenContract.ContractAddress,
                nameof(TokenMethod.UpdateCoefficientFromSender), input, organization, InitAccount);
            var miners = AuthorityManager.GetCurrentMiners();
            _parliamentAuthContract.MinersApproveProposal(proposal, miners);
            var result = _parliamentAuthContract.ReleaseProposal(proposal, InitAccount);
            result.Status.ShouldBe(TransactionResultStatus.Mined);
        }

        [TestMethod]
        public async Task GetCalculateFeeCoefficientOfDeveloper()
        {
            /*
            [pbr::OriginalName("Cpu")] Cpu = 0,
            [pbr::OriginalName("Sto")] Sto = 1,
            [pbr::OriginalName("Ram")] Ram = 2,
            [pbr::OriginalName("Net")] Net = 3,
             */
            var result = await _tokenSub.GetCalculateFeeCoefficientOfContract.CallAsync(new SInt32Value {Value = 0});
            var cpu = result.Coefficients;
            Logger.Info($"{cpu}");

            var result1 = await _tokenSub.GetCalculateFeeCoefficientOfContract.CallAsync(new SInt32Value {Value = 1});
            var sto = result1.Coefficients;
            Logger.Info($"{sto}");

            var result2 = await _tokenSub.GetCalculateFeeCoefficientOfContract.CallAsync(new SInt32Value {Value = 2});
            var ram = result2.Coefficients;
            Logger.Info($"{ram}");

            var result3 = await _tokenSub.GetCalculateFeeCoefficientOfContract.CallAsync(new SInt32Value {Value = 3});
            var net = result3.Coefficients;
            Logger.Info($"{net}");

            var result4 = await _tokenSub.GetCalculateFeeCoefficientOfSender.CallAsync(new Empty());
            Logger.Info($"{result4.Coefficients}");
        }

        [TestMethod]
        public async Task GetCalculateFeeCoefficientOfUser()
        {
            var result = await _tokenSub.GetCalculateFeeCoefficientOfSender.CallAsync(new Empty());
            Logger.Info($"{result.Coefficients}");
        }

        [TestMethod]
        public async Task GetBasicToken()
        {
            var result = await _tokenConverterSub.GetBaseTokenSymbol.CallAsync(new Empty());
            Logger.Info($"{result.Symbol}");
        }

        [TestMethod]
        public async Task GetManagerAddress()
        {
            var manager = await _tokenConverterSub.GetControllerForManageConnector.CallAsync(new Empty());
            Logger.Info($"manager is {manager.OwnerAddress}");
            var organization = _parliamentAuthContract.GetGenesisOwnerAddress();
            Logger.Info($"organization is {organization}");
        }

        [TestMethod]
        public async Task GetConnector()
        {
            var result = await _tokenConverterSub.GetPairConnector.CallAsync(new TokenSymbol {Symbol = "STA"});
            Logger.Info($"{result}");
        }

        [TestMethod]
        public async Task Acs8ContractTest()
        {
            var acs8Contract = _acs8Contract.ContractAddress;

            foreach (var s in ResourceSymbol)
            {
                var balance = await _tokenSub.GetBalance.CallAsync(new GetBalanceInput
                    {Owner = AddressHelper.Base58StringToAddress(acs8Contract), Symbol = s});
                Logger.Info($"{s} balance is {balance.Balance}");
            }

            var cpuResult = await _acs8Sub.CpuConsumingMethod.SendAsync(new Empty());
            Logger.Info(cpuResult.Transaction.Size());
            Logger.Info(cpuResult.TransactionResult);

            foreach (var s in ResourceSymbol)
            {
                var balance = await _tokenSub.GetBalance.CallAsync(new GetBalanceInput
                    {Owner = AddressHelper.Base58StringToAddress(acs8Contract), Symbol = s});
                Logger.Info($"{s} balance is {balance.Balance}");
            }

//            //net
//            var randomBytes = CommonHelper.GenerateRandombytes(1000);
//            var netResult = await _acs8Sub.NetConsumingMethod.SendAsync(new NetConsumingMethodInput
//            {
//                Blob = ByteString.CopyFrom(randomBytes)
//            });
//            Logger.Info(netResult.Transaction.Size());
//            Logger.Info(netResult.TransactionResult);
//
//            //sto
//            var stoResult = await _acs8Sub.StoConsumingMethod.SendAsync(new Empty());
//            Logger.Info(stoResult.Transaction.Size());
//            Logger.Info(stoResult.TransactionResult);
//
//            //few
//            var fewResult = await _acs8Sub.FewConsumingMethod.SendAsync(new Empty());
//            Logger.Info(fewResult.Transaction.Size());
//            Logger.Info(fewResult.TransactionResult);
        }

        private async Task CreateToken(long amount)
        {
            var result = await _bpTokenSub.Create.SendAsync(new CreateInput
            {
                Issuer = AddressHelper.Base58StringToAddress(BpAccount),
                Symbol = Symbol,
                Decimals = 8,
                IsBurnable = true,
                TokenName = "TEST symbol",
                TotalSupply = 100000000_00000000
            });

            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var issueResult = await _bpTokenSub.Issue.SendAsync(new IssueInput
            {
                Amount = amount,
                Symbol = Symbol,
                To = AddressHelper.Base58StringToAddress(BpAccount)
            });
            issueResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
            var balance = _tokenContract.GetUserBalance(BpAccount, Symbol);
            balance.ShouldBe(amount);
        }

        private async Task<long> GetNeededDeposit(long amount)
        {
            var result = await _tokenConverterSub.GetNeededDeposit.CallAsync(new ToBeConnectedTokenInfo
            {
                TokenSymbol = Symbol,
                AmountToTokenConvert = amount
            });
            return result.NeedAmount;
        }
    }
}