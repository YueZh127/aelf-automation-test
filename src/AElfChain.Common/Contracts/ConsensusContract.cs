﻿using System.Collections.Generic;
using System.Linq;
using AElf;
using AElf.Contracts.Consensus.AEDPoS;
using AElfChain.Common.Managers;
using Google.Protobuf.WellKnownTypes;

namespace AElfChain.Common.Contracts
{
    public enum ConsensusMethod
    {
        GetRoundInfo,
        GetCurrentTermNumber,
        IsCandidate,
        GetVotesCount,
        GetTicketsCount,
        GetCandidatesList,
        GetCandidateHistoryInformation,
        GetCurrentMinerList,
        GetCurrentRoundInformation,
        GetCurrentMinerPubkeyList,
        GetTicketsInfo,
        GetPageableElectionInfo,
        GetBlockchainAge,
        GetCurrentVictories,
        GetTermSnapshot,
        GetTermNumberByRoundNumber,
        QueryAliasesInUse,
        QueryCurrentDividends,
        QueryCurrentDividendsForVoters,
        QueryMinedBlockCountInCurrentTerm,
        GetAvailableDividends,

        AnnounceElection,
        QuitElection,
        Vote,
        ReceiveAllDividends,
        WithdrawAll,
        InitialBalance
    }

    public class ConsensusContract : BaseContract<ConsensusMethod>
    {
        public ConsensusContract(INodeManager nodeManager, string callAddress, string consensusAddress)
            : base(nodeManager, consensusAddress)
        {
            SetAccount(callAddress);
        }

        public long GetCurrentTermInformation()
        {
            var round = CallViewMethod<Round>(ConsensusMethod.GetCurrentRoundInformation, new Empty());

            return round.TermNumber;
        }

        public List<string> GetCurrentMiners()
        {
            var miners = CallViewMethod<MinerList>(ConsensusMethod.GetCurrentMinerList, new Empty());
            return miners.Pubkeys.Select(o => o.ToByteArray().ToHex()).ToList();
        }
    }
}