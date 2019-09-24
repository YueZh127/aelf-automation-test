using System;
using System.Collections.Generic;
using System.Linq;
using Acs0;
using AElf.Automation.Common.Contracts;
using AElf.Automation.Common.Helpers;
using AElf.Kernel;
using AElf.Types;
using AElfChain.SDK;
using AElfChain.SDK.Models;
using Google.Protobuf;
using log4net;
using Volo.Abp.Threading;

namespace AElf.Automation.Common.Managers
{
    public class NodeManager : INodeManager
    {
        public NodeManager(string baseUrl, string keyPath = "")
        {
            _baseUrl = baseUrl;
            _keyStore = AElfKeyStore.GetKeyStore(keyPath);

            ApiService = AElfChainClient.GetClient(baseUrl);
            _chainId = GetChainId();
        }

        public string GetApiUrl()
        {
            return _baseUrl;
        }

        public void UpdateApiUrl(string url)
        {
            _baseUrl = url;
            ApiService = AElfChainClient.GetClient(url);
            _chainId = GetChainId();

            Logger.Info($"Request url updated to: {url}");
        }

        public string GetChainId()
        {
            if (_chainId != null)
                return _chainId;

            var chainStatus = AsyncHelper.RunSync(ApiService.GetChainStatusAsync);
            _chainId = chainStatus.ChainId;

            return _chainId;
        }

        public string GetGenesisContractAddress()
        {
            if (_genesisAddress != null) return _genesisAddress;

            var statusDto = AsyncHelper.RunSync(ApiService.GetChainStatusAsync);
            _genesisAddress = statusDto.GenesisContractAddress;
            
            return _genesisAddress;
        }

        private string CallTransaction(Transaction tx)
        {
            var rawTxString = TransactionManager.ConvertTransactionRawTxString(tx);
            return AsyncHelper.RunSync(() => ApiService.ExecuteTransactionAsync(rawTxString));
        }

        private TransactionManager GetTransactionManager()
        {
            if (_transactionManager != null) return _transactionManager;

            _transactionManager = new TransactionManager(_keyStore);
            return _transactionManager;
        }

        private AccountManager GetAccountManager()
        {
            if (_accountManager != null) return _accountManager;

            _accountManager = new AccountManager(_keyStore);
            return _accountManager;
        }

        #region Properties

        private string _baseUrl;
        private string _chainId;
        private readonly AElfKeyStore _keyStore;
        private static readonly ILog Logger = Log4NetHelper.GetLogger();

        private string _genesisAddress;
        public string GenesisAddress => GetGenesisContractAddress();

        private AccountManager _accountManager;
        public AccountManager AccountManager => GetAccountManager();

        private TransactionManager _transactionManager;
        public TransactionManager TransactionManager => GetTransactionManager();
        public IApiService ApiService { get; set; }

        #endregion

        #region Account methods

        public string NewAccount(string password = "")
        {
            return AccountManager.NewAccount(password);
        }

        public string GetRandomAccount()
        {
            var accounts = AccountManager.ListAccount();
            var retry = 0;
            while (retry < 5)
            {
                retry++;
                var randomId = CommonHelper.GenerateRandomNumber(0, accounts.Count);
                var result = AccountManager.UnlockAccount(accounts[randomId]);
                if (!result) continue;

                return accounts[randomId];
            }

            throw new Exception("Cannot got account with default password.");
        }

        public string GetAccountPublicKey(string account, string password = "")
        {
            return AccountManager.GetPublicKey(account, password);
        }

        public List<string> ListAccounts()
        {
            return AccountManager.ListAccount();
        }

        public bool UnlockAccount(string account, string password = "")
        {
            return AccountManager.UnlockAccount(account, password);
        }

        #endregion

        #region Web request methods

        public string DeployContract(string from, string filename)
        {
            // Read sc bytes
            var contractReader = new SmartContractReader();
            var codeArray = contractReader.Read(filename);
            var input = new ContractDeploymentInput
            {
                Category = KernelConstants.CodeCoverageRunnerCategory,
                Code = ByteString.CopyFrom(codeArray)
            };

            var tx = TransactionManager.CreateTransaction(from, GenesisAddress,
                GenesisMethod.DeploySmartContract.ToString(), input.ToByteString());
            tx = tx.AddBlockReference(_baseUrl, _chainId);
            tx = TransactionManager.SignTransaction(tx);
            var rawTxString = TransactionManager.ConvertTransactionRawTxString(tx);
            var transactionOutput = AsyncHelper.RunSync(() => ApiService.SendTransactionAsync(rawTxString));

            return transactionOutput.TransactionId;
        }

        public string SendTransaction(string from, string to, string methodName, IMessage inputParameter)
        {
            var rawTransaction = GenerateRawTransaction(from, to, methodName, inputParameter);
            var transactionOutput = AsyncHelper.RunSync(() => ApiService.SendTransactionAsync(rawTransaction));

            return transactionOutput.TransactionId;
        }

        public string SendTransaction(string rawTransaction)
        {
            var transactionOutput = AsyncHelper.RunSync(() => ApiService.SendTransactionAsync(rawTransaction));

            return transactionOutput.TransactionId;
        }

        public List<string> SendTransactions(string rawTransactions)
        {
            var transactions = AsyncHelper.RunSync(() => ApiService.SendTransactionsAsync(rawTransactions));

            return transactions.ToList();
        }

        public string GenerateRawTransaction(string from, string to, string methodName, IMessage inputParameter)
        {
            var tr = new Transaction
            {
                From = AddressHelper.Base58StringToAddress(from),
                To = AddressHelper.Base58StringToAddress(to),
                MethodName = methodName
            };

            if (tr.MethodName == null)
            {
                Logger.Error("Method not found.");
                return string.Empty;
            }

            tr.Params = inputParameter == null ? ByteString.Empty : inputParameter.ToByteString();
            tr = tr.AddBlockReference(_baseUrl, _chainId);

            TransactionManager.SignTransaction(tr);

            return tr.ToByteArray().ToHex();
        }

        public T QueryView<T>(string from, string to, string methodName, IMessage inputParameter)
            where T : IMessage<T>, new()
        {
            var transaction = new Transaction
            {
                From = AddressHelper.Base58StringToAddress(from),
                To = AddressHelper.Base58StringToAddress(to),
                MethodName = methodName,
                Params = inputParameter == null ? ByteString.Empty : inputParameter.ToByteString()
            };
            transaction = TransactionManager.SignTransaction(transaction);

            var resp = CallTransaction(transaction);

            //deserialize response
            if (resp == null)
            {
                Logger.Error("ExecuteTransaction response is null.");
                return default;
            }

            var byteArray = ByteArrayHelper.HexStringToByteArray(resp);
            var messageParser = new MessageParser<T>(() => new T());

            return messageParser.ParseFrom(byteArray);
        }

        //Net Api
        public List<PeerDto> NetGetPeers()
        {
            return AsyncHelper.RunSync(ApiService.GetPeersAsync);
        }

        public bool NetAddPeer(string address)
        {
            return AsyncHelper.RunSync(() => ApiService.AddPeerAsync(address));
        }

        public bool NetRemovePeer(string address)
        {
            return AsyncHelper.RunSync(() => ApiService.RemovePeerAsync(address));
        }

        #endregion
    }
}