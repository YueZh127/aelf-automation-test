using AElf.Types;
using AElfChain.Common.Contracts;
using AElfChain.Common.DtoExtension;
using AElfChain.Common.Managers;

namespace AElf.Automation.SideChainCreate
{
    public class ContractServices
    {
        public readonly INodeManager NodeManager;

        public ContractServices(string url, string callAddress, string password)
        {
            NodeManager = new NodeManager(url);
            CallAddress = callAddress;
            CallAccount = callAddress.ConvertAddress();

            NodeManager.UnlockAccount(CallAddress, password);
            GetContractServices();
        }

        public GenesisContract GenesisService { get; set; }
        public TokenContract TokenService { get; set; }
        public ConsensusContract ConsensusService { get; set; }
        public CrossChainContract CrossChainService { get; set; }
        public ParliamentContract ParliamentService { get; set; }

        public string CallAddress { get; set; }
        public Address CallAccount { get; set; }

        private void GetContractServices()
        {
            GenesisService = GenesisContract.GetGenesisContract(NodeManager, CallAddress);

            //Token contract
            TokenService = GenesisService.GetTokenContract();

            //Consensus contract
            ConsensusService = GenesisService.GetConsensusContract();

            //CrossChain contract
            CrossChainService = GenesisService.GetCrossChainContract();

            //Parliament contract
            ParliamentService = GenesisService.GetParliamentContract();
        }
    }
}