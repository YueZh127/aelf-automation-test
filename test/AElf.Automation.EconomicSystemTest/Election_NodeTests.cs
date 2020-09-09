using System.Collections.Generic;
using System.Linq;
using Acs1;
using Acs10;
using AElf.Contracts.Consensus.AEDPoS;
using AElf.Contracts.Election;
using AElf.Contracts.Profit;
using AElfChain.Common.Contracts;
using AElf.Types;
using AElfChain.Common.DtoExtension;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using log4net.Repository.Hierarchy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Shouldly;
using Volo.Abp.Threading;
using PubkeyList = AElf.Contracts.Consensus.AEDPoS.PubkeyList;

namespace AElf.Automation.EconomicSystemTest
{
    [TestClass]
    public class NodeTests : ElectionTests
    {
        [TestInitialize]
        public void InitializeNodeTests()
        {
            Initialize();
        }

        [TestCleanup]
        public void CleanUpNodeTests()
        {
            TestCleanUp();
        }

        [TestMethod]
        public void AnnouncementNode()
        {
            Behaviors.TransferToken(InitAccount, FullNodeAddress[0], 10_1000_00000000);
            var result = Behaviors.AnnouncementElection(FullNodeAddress[0]);
            result.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
        }

        [TestMethod]
        public void NodeAnnounceElectionAction()
        {
            foreach (var user in FullNodeAddress)
            {
                Behaviors.TransferToken(InitAccount, user, 10_1000_00000000);
                var election = Behaviors.ElectionService.GetNewTester(user, "123");
                var electionResult = election.ExecuteMethodWithResult(ElectionMethod.AnnounceElection, new Empty());
                electionResult.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
            }

            var candidateList = Behaviors.GetCandidates();
            Logger.Info($"{candidateList.Value.Count}");
            foreach (var publicKey in candidateList.Value)
                Logger.Info($"Candidate PublicKey: {publicKey.ToByteArray().ToHex()}");
        }

        [TestMethod]
        public void SetMaximumMinersCount()
        {
            var amount = 5;
            var maximumBlocksCount = Behaviors.ConsensusService.GetMaximumMinersCount().Value;
            Logger.Info($"{maximumBlocksCount}");
            var consensus = Behaviors.ConsensusService;
            var input = new Int32Value {Value = amount};
            var result = Behaviors.AuthorityManager.ExecuteTransactionWithAuthority(consensus.ContractAddress,
                nameof(ConsensusMethod.SetMaximumMinersCount), input, InitAccount);
            result.Status.ShouldBe(TransactionResultStatus.Mined);
            maximumBlocksCount = Behaviors.ConsensusService.GetMaximumMinersCount().Value;
            maximumBlocksCount.ShouldBe(amount);
        }
        
        [TestMethod]
        public void SetMaximumMinersCountThroughAssociation()
        {
            var amount = 5;
            var maximumBlocksCount = Behaviors.ConsensusService.GetMaximumMinersCount().Value;
            Logger.Info($"{maximumBlocksCount}");
            var consensus = Behaviors.ConsensusService;
            var association = Behaviors.ContractManager.Association;
            var maximumMinersCountController = Behaviors.ConsensusService.GetMaximumMinersCountController();
            var associationOrganization = maximumMinersCountController.OwnerAddress;
            var input = new Int32Value {Value = amount};
            var proposer = association.GetOrganization(associationOrganization).ProposerWhiteList.Proposers.First();
            var proposalId = association.CreateProposal(consensus.ContractAddress,
                nameof(ConsensusMethod.SetMaximumMinersCount), input, associationOrganization,
                proposer.ToBase58());
            association.ApproveWithAssociation(proposalId, associationOrganization);
            var release = association.ReleaseProposal(proposalId, proposer.ToBase58());
            release.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
            maximumBlocksCount = Behaviors.ConsensusService.GetMaximumMinersCount().Value;
            maximumBlocksCount.ShouldBe(amount);
        }

        [TestMethod]
        public void ChangeMaximumMinersCountController()
        {
            var parliament = Behaviors.ContractManager.Parliament;
            var association = Behaviors.ContractManager.Association;
            var consensus = Behaviors.ConsensusService;
            var genesisOwnerAddress = parliament.GetGenesisOwnerAddress();
            var maximumMinersCountController = Behaviors.ConsensusService.GetMaximumMinersCountController();
            var oldInput = new AuthorityInfo
            {
                ContractAddress = parliament.Contract,
                OwnerAddress = genesisOwnerAddress
            };
            var associationOrganization = Behaviors.AuthorityManager.CreateAssociationOrganization();
            var input = new AuthorityInfo
            {
                ContractAddress = association.Contract,
                OwnerAddress = associationOrganization
            };
            if (maximumMinersCountController.ContractAddress.Equals(parliament.Contract))
            {
                var result = Behaviors.AuthorityManager.ExecuteTransactionWithAuthority(consensus.ContractAddress,
                    nameof(ConsensusMethod.ChangeMaximumMinersCountController), input, InitAccount);
                result.Status.ShouldBe(TransactionResultStatus.Mined);
                maximumMinersCountController = Behaviors.ConsensusService.GetMaximumMinersCountController();
                maximumMinersCountController.ContractAddress.ShouldBe(association.Contract);
                maximumMinersCountController.OwnerAddress.ShouldBe(associationOrganization);
            }
            else if (maximumMinersCountController.ContractAddress.Equals(association.Contract))
            {
                var proposer = association.GetOrganization(associationOrganization).ProposerWhiteList.Proposers.First();
                var proposalId = association.CreateProposal(consensus.ContractAddress,
                    nameof(ConsensusMethod.ChangeMaximumMinersCountController), oldInput, associationOrganization,
                    proposer.ToBase58());
                association.ApproveWithAssociation(proposalId, associationOrganization);
                var release = association.ReleaseProposal(proposalId, proposer.ToBase58());
                release.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
                maximumMinersCountController = Behaviors.ConsensusService.GetMaximumMinersCountController();
                maximumMinersCountController.ContractAddress.ShouldBe(parliament.Contract);
                maximumMinersCountController.OwnerAddress.ShouldBe(genesisOwnerAddress);
            }
        }

        [TestMethod]
        public void GetMaximumMinersCount()
        {
            var maximumMinersCount = Behaviors.ConsensusService.GetMaximumMinersCount().Value;
            Logger.Info($"{maximumMinersCount}");
        }

        [TestMethod]
        [DataRow(0)]
        public void GetVotesInformationResult(int nodeId)
        {
            var records = Behaviors.GetElectorVoteWithAllRecords(UserList[nodeId]);
        }

        [TestMethod]
        public void GetMiners()
        {
            GetCurrentMiners();
        }

        [TestMethod]
        public void GetVoteStatus()
        {
            var termNumber =
                Behaviors.ConsensusService.CallViewMethod<SInt64Value>(ConsensusMethod.GetCurrentTermNumber,
                    new Empty()).Value;
            var candidateList = Behaviors.GetCandidates();
            var voteMessage =
                $"TermNumber={termNumber}, candidates count is {candidateList.Value.Count}, got vote keys info: \r\n";
            foreach (var fullNode in candidateList.Value)
            {
                var candidateVote = Behaviors.ElectionService.CallViewMethod<CandidateVote>(
                    ElectionMethod.GetCandidateVote,
                    new StringValue
                    {
                        Value = fullNode.ToHex()
                    });
                if (candidateVote.Equals(new CandidateVote()))
                    continue;
                voteMessage +=
                    $" {fullNode.ToHex()} All tickets: {candidateVote.AllObtainedVotedVotesAmount}, Active tickets: {candidateVote.ObtainedActiveVotedVotesAmount}\r\n";
            }

            Logger.Info(voteMessage);
        }

        [TestMethod]
        public void GetVictories()
        {
            var victories = Behaviors.GetVictories();

            var publicKeys = victories.Value.Select(o => o.ToByteArray().ToHex()).ToList();

            publicKeys.Contains(Behaviors.NodeManager.GetAccountPublicKey(FullNodeAddress[0])).ShouldBeTrue();
            publicKeys.Contains(Behaviors.NodeManager.GetAccountPublicKey(FullNodeAddress[1])).ShouldBeTrue();
            publicKeys.Contains(Behaviors.NodeManager.GetAccountPublicKey(FullNodeAddress[2])).ShouldBeTrue();
        }

        [TestMethod]
        [DataRow(2)]
        public void QuitElection(int nodeId)
        {
            var beforeBalance = Behaviors.GetBalance(FullNodeAddress[nodeId]).Balance;
            var result = Behaviors.QuitElection(FullNodeAddress[nodeId]);
            result.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
            var fee = result.GetDefaultTransactionFee();
            var afterBalance = Behaviors.GetBalance(FullNodeAddress[nodeId]).Balance;
            beforeBalance.ShouldBe(afterBalance - 100000_00000000L + fee);
        }

        [TestMethod]
        public void GetCandidates()
        {
            var candidates = Behaviors.GetCandidates();
            Logger.Info($"Candidate count: {candidates.Value.Count}");
            foreach (var candidate in candidates.Value) Logger.Info($"Candidate: {candidate.ToByteArray().ToHex()}");
        }

        [TestMethod]
        public void GetTreasuryInfo()
        {
            var treasury = Behaviors.Treasury;
            var profit = Behaviors.ProfitService;
            Behaviors.ProfitService.GetTreasurySchemes(treasury.ContractAddress);
            var treasuryAmount = treasury.GetCurrentTreasuryBalance();
            Logger.Info(JsonConvert.SerializeObject(treasuryAmount));
            Logger.Info($"treasury dotnet balance : {treasuryAmount.Value["ELF"]}");
            
            var height = AsyncHelper.RunSync(() => NodeManager.ApiClient.GetBlockHeightAsync());
            var dividends = treasury.GetDividends(height);
            Logger.Info(JsonConvert.SerializeObject(dividends));
            var treasuryBalance = Behaviors.TokenService.GetUserBalance(treasury.ContractAddress);
            Logger.Info($"treasury  balance : {treasuryBalance}");
            
            var treasuryProfit =
                profit.GetScheme(Schemes[SchemeType.Treasury].SchemeId);
            Logger.Info(treasuryProfit);
            
            var minerRewardProfit =
                profit.GetScheme(Schemes[SchemeType.MinerReward].SchemeId);
            Logger.Info(minerRewardProfit);


            var dividendPoolWeightProportion = treasury.GetDividendPoolWeightProportion();
            var minerRewardWeightProportion = treasury.GetMinerRewardWeightProportion();
        }

        [TestMethod]
        public void GetCurrentRoundMinedBlockBonus()
        {
            var round = Behaviors.ConsensusService.GetCurrentTermInformation();
            var roundNumber = round.RoundNumber;
            var term = round.TermNumber;
            var blocksBonus = Behaviors.ConsensusService.GetCurrentWelfareReward().Value;
            var blockCount = blocksBonus / 12500000;
            Logger.Info($"{term} {roundNumber}: {blockCount} {blocksBonus}");
        }

        [TestMethod]
        public void GetRoundInformation()
        {
            var round = Behaviors.ConsensusService.GetRoundInformation(663);
            var blocksCount = round.RealTimeMinersInformation
                .Values.Sum(minerInRound => minerInRound.ProducedBlocks);
            var miningReward = Behaviors.ConsensusService.GetCurrentMiningRewardPerBlock().Value;
            Logger.Info(miningReward);
            var blocksBonus = blocksCount * miningReward;
            Logger.Info($"{blocksCount}: {blocksBonus}");
        }
        

        [TestMethod]
        public void GetMinedBlocksOfPreviousTerm()
        {
            var blocks = Behaviors.ConsensusService.GetMinedBlocksOfPreviousTerm();
            Logger.Info(blocks.Value);
        }


        [TestMethod]
        public void CheckProfitCandidates()
        {
            var profit = Behaviors.ProfitService;
            var MinerBasicReward = Behaviors.Schemes[SchemeType.MinerBasicReward].SchemeId;
            var ReElectionReward = Behaviors.Schemes[SchemeType.ReElectionReward].SchemeId;
            var VotesWeightReward = Behaviors.Schemes[SchemeType.VotesWeightReward].SchemeId;
            var CitizenWelfare = Behaviors.Schemes[SchemeType.CitizenWelfare].SchemeId;
            var BackupSubsidy = Behaviors.Schemes[SchemeType.BackupSubsidy].SchemeId;
            
            long amount = 0;
            long testAmount = 0;
            
            long backupAmount = 0;
            long sumBasicRewardAmount = 0;
            long sumReElectionRewardAmount = 0;
            long sumVoteWeightRewardAmount = 0;
            
            long sumTestBasicRewardAmount = 0;
            long sumTestReElectionRewardAmount = 0;
            long sumTestVoteWeightRewardAmount = 0;
            
            var miners = GetCurrentMiners();
            var term = Behaviors.ConsensusService.GetCurrentTermInformation();

            foreach (var miner in miners)
            {
                var minerBasicReward = profit.GetProfitsMap(miner, MinerBasicReward);
                long profitAmount = 0;
                long testProfitAmount = 0;
                if (!minerBasicReward.Equals(new ReceivedProfitsMap()))
                {
                    profitAmount = minerBasicReward.Value["ELF"];
//                    testProfitAmount = minerBasicReward.Value["TEST"];
                    Logger.Info($"MinerBasicReward amount: user {miner} profit amount is {profitAmount}, TEST amount is {testProfitAmount}");
                }
                sumBasicRewardAmount += profitAmount;
                sumTestBasicRewardAmount += testProfitAmount;
                amount += profitAmount;
                testAmount += testProfitAmount;
                
                long reElectionRewardAmount = 0;
                long testReElectionRewardAmount = 0;
                var reElectionReward = profit.GetProfitsMap(miner, ReElectionReward);
                if (!reElectionReward.Equals(new ReceivedProfitsMap()))
                {
                    reElectionRewardAmount = reElectionReward.Value["ELF"];
//                    testReElectionRewardAmount = reElectionReward.Value["TEST"];
                    Logger.Info($"ReElectionReward amount: user {miner} profit amount is {reElectionRewardAmount}, TEST amount is {testReElectionRewardAmount}");
                }
                sumReElectionRewardAmount += reElectionRewardAmount;
                sumTestReElectionRewardAmount += testReElectionRewardAmount;

                amount += reElectionRewardAmount;
                testAmount += testReElectionRewardAmount;

                long testVotesWeighRewardAmount = 0;
                long votesWeightRewardAmount = 0;
                
                var votesWeightReward = profit.GetProfitsMap(miner, VotesWeightReward);
                if (!votesWeightReward.Equals(new ReceivedProfitsMap()))
                {
                    votesWeightRewardAmount = votesWeightReward.Value["ELF"];
//                    testVotesWeighRewardAmount = votesWeightReward.Value["TEST"];
                    Logger.Info($"VotesWeightReward amount: user {miner} profit amount is {votesWeightRewardAmount}");
                }

                sumTestVoteWeightRewardAmount += testVotesWeighRewardAmount;
                sumVoteWeightRewardAmount += votesWeightRewardAmount;
                amount += votesWeightRewardAmount;
                testAmount += testVotesWeighRewardAmount;
            }
            Logger.Info($"{term.TermNumber} {amount} MinerBasicReward (10%):{sumBasicRewardAmount}; ReElectionReward(5%):{sumReElectionRewardAmount}; VotesWeightReward(5%):{sumVoteWeightRewardAmount}");
            Logger.Info($"{term.TermNumber} {testAmount} MinerBasicReward (10%):{sumTestBasicRewardAmount}; ReElectionReward(5%):{sumTestReElectionRewardAmount}; VotesWeightReward(5%):{sumTestVoteWeightRewardAmount}");


            var candidates = Behaviors.GetCandidatesAddress();
            foreach (var candidate in FullNodeAddress)
            {
                var backupSubsidy = profit.GetProfitsMap(candidate, BackupSubsidy);
                long backupSubsidyAmount = 0;
                if (!backupSubsidy.Equals(new ReceivedProfitsMap()))
                {
                    backupSubsidyAmount = backupSubsidy.Value["ELF"];
                    Logger.Info($"BackupSubsidy amount: user {candidate} profit amount is {backupSubsidyAmount}");
                }
                backupAmount += backupSubsidyAmount;
            }
            Logger.Info($"{term.TermNumber} BackupSubsidy (5%):{backupAmount}");

            var info = Behaviors.TokenService.GetTokenInfo("ELF");
            Logger.Info(info);
        }

        [TestMethod]
        public void ClaimMinerBasicReward()
        {
            var profit = Behaviors.ProfitService;
            var MinerBasicReward = Behaviors.Schemes[SchemeType.MinerBasicReward].SchemeId;
            var ReElectionReward = Behaviors.Schemes[SchemeType.ReElectionReward].SchemeId;
            var VotesWeightReward = Behaviors.Schemes[SchemeType.VotesWeightReward].SchemeId;
            var CitizenWelfare = Behaviors.Schemes[SchemeType.CitizenWelfare].SchemeId;
            var miners = GetCurrentMiners();
            var term = Behaviors.ConsensusService.GetCurrentTermInformation();
            long feeAmount = 0;
            foreach (var miner in miners)
            {
                var profitMap = profit.GetProfitsMap(miner, ReElectionReward);
                if (profitMap.Equals(new ReceivedProfitsMap()))
                    continue;
                var profitAmountFull = profitMap.Value["ELF"];
                Logger.Info($"Profit amount: user {miner} profit amount is {profitAmountFull}");
                var beforeBalance = Behaviors.TokenService.GetUserBalance(miner);
                var newProfit = profit.GetNewTester(miner);
                var profitResult = newProfit.ExecuteMethodWithResult(ProfitMethod.ClaimProfits, new ClaimProfitsInput
                {
                    SchemeId = ReElectionReward,
                    Beneficiary = miner.ConvertAddress()
                });
                profitResult.Status.ConvertTransactionResultStatus().ShouldBe(TransactionResultStatus.Mined);
                var fee = profitResult.GetDefaultTransactionFee();
                var afterBalance =  Behaviors.TokenService.GetUserBalance(miner);
                feeAmount += fee;
//                afterBalance.ShouldBe(beforeBalance + profitAmountFull - fee);
            }
            Logger.Info($"{term}: fee {feeAmount}");
        }
        

        [TestMethod]
        public void GetCurrentMiningRewardPerBlock()
        {
            var miningReward = Behaviors.ConsensusService.GetCurrentMiningRewardPerBlock();
            Logger.Info(miningReward.Value);
        }

        [TestMethod]
        public void GetCandidateHistory()
        {
            foreach (var candidate in FullNodeAddress)
            {
                var candidateResult = Behaviors.GetCandidateInformation(candidate);
                Logger.Info("Candidate: ");
                Logger.Info($"PublicKey: {candidateResult.Pubkey}");
                Logger.Info($"Terms: {candidateResult.Terms}");
                Logger.Info($"ContinualAppointmentCount: {candidateResult.ContinualAppointmentCount}");
                Logger.Info($"ProducedBlocks: {candidateResult.ProducedBlocks}");
                Logger.Info($"MissedTimeSlots: {candidateResult.MissedTimeSlots}");
                Logger.Info($"AnnouncementTransactionId: {candidateResult.AnnouncementTransactionId}");
            }
        }
        
        private List<string> GetCurrentMiners()
        {
            var minerList = new List<string>();
            var miners =
                Behaviors.ConsensusService.CallViewMethod<MinerList>(ConsensusMethod.GetCurrentMinerList, new Empty());
            Logger.Info($"Miners count is : {miners.Pubkeys.Count}");
            foreach (var minersPubkey in miners.Pubkeys)
            {
                var miner = Address.FromPublicKey(minersPubkey.ToByteArray());
                minerList.Add(miner.ToBase58());
                Logger.Info($"Miner is : {miner} \n PublicKey: {minersPubkey.ToHex()}");
            }
            return minerList;
        }
    }
}