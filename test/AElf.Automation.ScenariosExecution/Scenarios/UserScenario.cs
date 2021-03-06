using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AElf.Contracts.Consensus.AEDPoS;
using AElf.Contracts.Election;
using AElf.Contracts.MultiToken;
using AElf.Contracts.Profit;
using AElf.Types;
using AElfChain.Common;
using AElfChain.Common.Contracts;
using AElfChain.Common.DtoExtension;
using AElfChain.Common.Helpers;
using Google.Protobuf.WellKnownTypes;
using log4net;
using Shouldly;

namespace AElf.Automation.ScenariosExecution.Scenarios
{
    public class UserScenario : BaseScenario
    {
        private static List<string> _candidates;
        private static List<string> _candidatesExcludeMiners;

        public new static readonly ILog Logger = Log4NetHelper.GetLogger();

        public UserScenario()
        {
            InitializeScenario();
            Treasury = Services.TreasuryService;
            Election = Services.ElectionService;
            Consensus = Services.ConsensusService;
            Profit = Services.ProfitService;
            Token = Services.TokenService;

            //Get Profit items
            Profit.GetTreasurySchemes(Treasury.ContractAddress);
            Schemes = ProfitContract.Schemes;

            Testers = AllTesters.GetRange(80, 20);
            PrintTesters(nameof(UserScenario), Testers);
        }

        public TreasuryContract Treasury { get; }
        public ElectionContract Election { get; }
        public ConsensusContract Consensus { get; }
        public ProfitContract Profit { get; }
        public TokenContract Token { get; }
        public List<string> Testers { get; }
        public Dictionary<SchemeType, Scheme> Schemes { get; }

        public void RunUserScenario()
        {
            ExecuteContinuousTasks(new Action[]
            {
                UserVotesAction,
                TakeVotesProfitAction
            }, true, 30);
        }

        public void RunUserScenarioJob()
        {
            ExecuteStandaloneTask(new Action[]
            {
                UserVotesAction,
                TakeVotesProfitAction,
                () => PrepareTesterToken(Testers),
                UpdateEndpointAction
            });
        }

        private void UserVotesAction()
        {
            GetCandidates(Election);
            GetCandidatesExcludeCurrentMiners();

            if (_candidates.Count < 2)
                return;

            var times = GenerateRandomNumber(3, 5);
            for (var i = 0; i < times; i++)
            {
                var id = GenerateRandomNumber(0, Testers.Count - 1);
                UserVote(Testers[id]);

                Thread.Sleep(10);
            }
        }

        private void TakeVotesProfitAction()
        {
            var times = GenerateRandomNumber(3, 5);
            for (var i = 0; i < times; i++)
            {
                var id = GenerateRandomNumber(0, Testers.Count - 1);
                TakeUserProfit(Testers[id]);

                Thread.Sleep(10);
            }
        }

        private void TakeUserProfit(string account)
        {
            var schemeId = Schemes[SchemeType.CitizenWelfare].SchemeId;
            var voteProfit =
                Profit.GetProfitDetails(account, schemeId);
            if (voteProfit.Equals(new ProfitDetails())) return;
            $"20% user vote profit for account: {account}.\r\nDetails number: {voteProfit.Details}".WriteSuccessLine();

            //Get user profit amount
            var profitMap = Profit.GetProfitsMap(account, schemeId);
            if (profitMap.Equals(new ReceivedProfitsMap()))
                return;
            var profitAmount = profitMap.Value["ELF"];
            Logger.Info($"Profit amount: user {account} profit amount is {profitAmount}");
            var beforeBalance = Token.GetUserBalance(account);
            var profit = Profit.GetNewTester(account);
            var profitResult = profit.ExecuteMethodWithResult(ProfitMethod.ClaimProfits, new ClaimProfitsInput
            {
                SchemeId = schemeId,
                Beneficiary = account.ConvertAddress()
            }, out var existed);
            if (existed) return; //if found tx existed and return
            if (profitResult.Status.ConvertTransactionResultStatus() != TransactionResultStatus.Mined) return;

            var checkResult = true;
            var profitTransactionFee = profitResult.GetDefaultTransactionFee();
            var afterBalance = Token.GetUserBalance(account);

            if (afterBalance + profitTransactionFee != beforeBalance + profitAmount)
            {
                Logger.Error(
                    $"Check profit balance failed: {afterBalance + profitTransactionFee}/{beforeBalance + profitAmount}");
                checkResult = false;
            }

            if (checkResult)
                Logger.Info(
                    $"Profit success - user {account} get vote profit from Id: {schemeId}, value is: {profitAmount}");
        }

        private void UserVote(string account)
        {
            var lockTime = GenerateRandomNumber(3, 30) * 30;

            var amount = GenerateRandomNumber(1, 5) * 5;
            if (_candidatesExcludeMiners.Count != 0)
            {
                var id = GenerateRandomNumber(0, _candidatesExcludeMiners.Count - 1);
                UserVote(account, _candidatesExcludeMiners[id], lockTime, amount);
            }

            else
            {
                var id = GenerateRandomNumber(0, _candidates.Count - 1);
                UserVote(account, _candidates[id], lockTime, amount);
            }
        }

        private void UserVote(string account, string candidatePublicKey, int lockTime, long amount)
        {
            var beforeElfBalance = Token.GetUserBalance(account);
            var beforeVoteBalance = Token.GetUserBalance(account, "VOTE");
            var beforeVoteShareBalance = Token.GetUserBalance(account, "SHARE");

            var beforeCandidateVote = Election.GetCandidateVoteCount(candidatePublicKey);
            if (beforeElfBalance < amount) // balance not enough, bp transfer again
            {
                const long transferAmount = 1_0000_00000000L;
                var token = Token.GetNewTester(AllNodes.First().Account);
                var transferTxResult = token.ExecuteMethodWithResult(TokenMethod.Transfer, new TransferInput
                {
                    Symbol = NodeOption.NativeTokenSymbol,
                    Amount = transferAmount,
                    To = account.ConvertAddress(),
                    Memo = $"Transfer={Guid.NewGuid()}"
                });
                if (transferTxResult.Status.ConvertTransactionResultStatus() != TransactionResultStatus.Mined) return;
                beforeElfBalance = Token.GetUserBalance(account);
            }

            var election = Election.GetNewTester(account);

            var voteResult = election.ExecuteMethodWithResult(ElectionMethod.Vote, new VoteMinerInput
            {
                CandidatePubkey = candidatePublicKey,
                Amount = amount,
                EndTimestamp = DateTime.UtcNow.Add(TimeSpan.FromDays(lockTime))
                    .Add(TimeSpan.FromHours(1))
                    .ToTimestamp()
            }, out var existed);
            if (existed) return;
            if (voteResult.Status.ConvertTransactionResultStatus() != TransactionResultStatus.Mined) return;
            var voteId =  Hash.Parser.ParseFrom(
                ByteArrayHelper.HexStringToByteArray(voteResult.ReturnValue));
            var accountPubkey = Services.NodeManager.GetAccountPublicKey(account);
            var electorResult = election.CallViewMethod<ElectorVote>(ElectionMethod.GetElectorVote, new StringValue {Value = accountPubkey});
            electorResult.ActiveVotingRecordIds.ShouldContain(voteId);
            
            var afterElfBalance = Token.GetUserBalance(account);
            var afterVoteBalance = Token.GetUserBalance(account, "VOTE");
            var afterVoteShareBalance = Token.GetUserBalance(account, "SHARE");
            var transactionFee = voteResult.GetDefaultTransactionFee();
            var afterCandidateVote = Election.GetCandidateVoteCount(candidatePublicKey);

            var checkResult = true;

            //check vote result process
            if (beforeElfBalance != afterElfBalance + amount + transactionFee)
            {
                Logger.Error(
                    $"User vote cost balance check failed. ELF: {beforeElfBalance}/{afterElfBalance + amount + transactionFee}");
                checkResult = false;
            }

            if (afterVoteBalance != beforeVoteBalance + amount)
            {
                Logger.Error(
                    $"User vote receive VOTE token balance check failed. VOTE: {beforeVoteBalance}/{afterVoteBalance - amount}");
                checkResult = false;
            }
            
            if (afterVoteShareBalance != beforeVoteShareBalance + amount)
            {
                Logger.Error(
                    $"User vote receive SHARE token balance check failed. SHARE: {beforeVoteShareBalance}/{afterVoteShareBalance - amount}");
                checkResult = false;
            }

            if (beforeCandidateVote != afterCandidateVote - amount)
            {
                Logger.Error(
                    $"Candidate vote count check failed. Ticket: {beforeCandidateVote + amount}/{afterCandidateVote}");
                checkResult = false;
            }

            if (checkResult)
                Logger.Info(
                    $"Vote success - {account} vote candidate: {candidatePublicKey} with amount: {amount} lock time: {lockTime} days."
                );
        }

        public static List<string> GetCandidates(ElectionContract election)
        {
            var candidatePublicKeys = election.CallViewMethod<Candidates>(ElectionMethod.GetCandidates, new Empty());
            _candidates = candidatePublicKeys.Pubkeys.Select(o => o.ToByteArray().ToHex()).ToList();
            return _candidates;
        }

        private void GetCandidatesExcludeCurrentMiners()
        {
            //query current miners
            var miners = Consensus.CallViewMethod<MinerList>(ConsensusMethod.GetCurrentMinerList, new Empty());
            var minersPublicKeys = miners.Pubkeys.Select(o => o.ToByteArray().ToHex()).ToList();

            //query current candidates
            _candidatesExcludeMiners = new List<string>();
            _candidates.ForEach(o =>
            {
                if (!minersPublicKeys.Contains(o)) _candidatesExcludeMiners.Add(o);
            });
        }
    }
}