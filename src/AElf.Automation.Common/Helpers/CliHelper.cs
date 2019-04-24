﻿using System;
using System.Collections.Generic;
using System.IO;
using AElf.Automation.Common.Extensions;
using AElf.Cryptography;
using AElf.Kernel;
using Google.Protobuf;
using Newtonsoft.Json.Linq;
using ProtoBuf;

namespace AElf.Automation.Common.Helpers
{
    public class CliHelper
    {
        #region Properties

        private string _rpcAddress;
        private string _genesisAddress;
        private string _chainId;
        private AElfKeyStore _keyStore;
        private AccountManager _accountManager;
        private TransactionManager _transactionManager;
        private RpcRequestManager _requestManager;

        private readonly ILogHelper _logger = LogHelper.GetLogHelper();
        public List<CommandInfo> CommandList { get; }

        #endregion

        public CliHelper(string rpcUrl, string keyPath = "")
        {
            _rpcAddress = rpcUrl;
            _keyStore = new AElfKeyStore(keyPath == "" ? ApplicationHelper.GetDefaultDataDir() : keyPath);
            _requestManager = new RpcRequestManager(rpcUrl);

            CommandList = new List<CommandInfo>();
        }

        public CommandInfo ExecuteCommand(CommandInfo ci)
        {
            switch (ci.Cmd)
            {
                //Account request
                case "AccountNew":
                    ci = NewAccount(ci);
                    break;
                case "AccountList":
                    ci = ListAccounts(ci);
                    break;
                case "AccountUnlock":
                    ci = UnlockAccount(ci);
                    break;
                case "GetChainInformation":
                    RpcGetChainInformation(ci);
                    break;
                case "LoadContractAbi":
                    RpcLoadContractAbi(ci);
                    break;
                case "DeploySmartContract":
                    RpcDeployContract(ci);
                    break;
                case "BroadcastTransaction":
                    RpcBroadcastTx(ci);
                    break;
                case "BroadcastTransactions":
                    RpcBroadcastTxs(ci);
                    break;
                case "GetCommands":
                    RpcGetCommands(ci);
                    break;
                case "GetContractAbi":
                    RpcGetContractAbi(ci);
                    break;
                case "GetIncrement":
                    RpcGetIncrement(ci);
                    break;
                case "GetTransactionResult":
                    RpcGetTxResult(ci);
                    break;
                case "GetBlockHeight":
                    RpcGetBlockHeight(ci);
                    break;
                case "GetBlockInfo":
                    RpcGetBlockInfo(ci);
                    break;
                case "GetMerklePath":
                    RpcGetMerklePath(ci);
                    break;
                case "QueryView":
                    RpcQueryViewInfo(ci);
                    break;
                case "SetBlockVolume":
                    RpcSetBlockVolume(ci);
                    break;
                default:
                    _logger.WriteError("Invalid command.");
                    break;
            }

            ci.PrintResultMessage();
            CommandList.Add(ci);

            return ci;
        }

        #region Account methods

        public CommandInfo NewAccount(CommandInfo ci)
        {
            ci = _accountManager.NewAccount(ci.Parameter);
            return ci;
        }

        public CommandInfo ListAccounts(CommandInfo ci)
        {
            ci = _accountManager.ListAccount();
            return ci;
        }

        public CommandInfo UnlockAccount(CommandInfo ci)
        {
            ci = _accountManager.UnlockAccount(ci.Parameter.Split(" ")?[0], ci.Parameter.Split(" ")?[1],
                ci.Parameter.Split(" ")?[2]);
            return ci;
        }

        #endregion

        #region Rpc request methods

        public void RpcGetChainInformation(CommandInfo ci)
        {
            var req = RpcRequestManager.CreateRequest(new JObject(), ci.Cmd, 1);
            var resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;

            var jObj = JObject.Parse(resp);

            var j = jObj["result"];
            if (j["error"] != null)
            {
                ci.ErrorMsg.Add(j["error"].ToString());
                return;
            }

            if (j["GenesisContractAddress"] != null)
            {
                _genesisAddress = j["GenesisContractAddress"].ToString();
            }

            if (j["ChainId"] != null)
            {
                _chainId = j["ChainId"].ToString();
                _accountManager = new AccountManager(_keyStore, _chainId);
                _transactionManager = new TransactionManager(_keyStore, _chainId);
            }

            var message = j.ToString();
            ci.InfoMsg.Add(message);
            ci.Result = true;
        }

        public void RpcLoadContractAbi(CommandInfo ci)
        {
            if (string.IsNullOrEmpty(ci.Parameter))
            {
                if (_genesisAddress == null)
                {
                    ci.ErrorMsg.Add("Please GetChainInformation first.");
                    return;
                }

                ci.Parameter = _genesisAddress;
            }

            var req = RpcRequestManager.CreateRequest(new JObject
            {
                ["address"] = ci.Parameter
            }, "GetContractAbi", 1);

            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;
            JObject jObj = JObject.Parse(resp);
            var res = JObject.FromObject(jObj["result"]);

            ci.InfoMsg.Add(res.ToString());
            ci.Result = true;
        }

        public void RpcDeployContract(CommandInfo ci)
        {
            if (!ci.CheckParameterValid(3))
                return;
            string filename = ci.Parameter.Split(" ")[0];

            // Read sc bytes
            var contractReader = new SmartContractReader();
            byte[] codeArray = contractReader.Read(filename);
            var input = new ContractDeploymentInput
            {
                Category = KernelConstants.CodeCoverageRunnerCategory,
                Code = ByteString.CopyFrom(codeArray)
            };

            _transactionManager.SetCmdInfo(ci);
            var tx = _transactionManager.CreateTransaction(ci.Parameter.Split(" ")[2], _genesisAddress,
                ci.Parameter.Split(" ")[1],
                ci.Cmd, input.ToByteString());
            tx = tx.AddBlockReference(_rpcAddress);
            if (tx == null)
                return;
            tx = _transactionManager.SignTransaction(tx);
            if (tx == null)
                return;
            var rawtx = _transactionManager.ConvertTransactionRawTx(tx);
            var req = RpcRequestManager.CreateRequest(rawtx, "BroadcastTransaction", 1);
            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
            {
                ci.Result = false;
                ci.ErrorMsg.Add(returnCode);
                return;
            }

            var jObj = JObject.Parse(resp);
            var j = jObj["result"];
            if (j["error"] != null)
            {
                ci.ErrorMsg.Add(j["error"].ToString());
                ci.Result = false;
                return;
            }

            ci.InfoMsg.Add(j.ToString());

            ci.Result = true;
        }

        public void RpcBroadcastTx(CommandInfo ci)
        {
            JObject j = null;
            if (ci.Parameter != null)
            {
                if (!ci.Parameter.Contains("{"))
                {
                    RpcBroadcastWithRawTx(ci);
                    return;
                }

                j = JObject.Parse(ci.Parameter);
            }

            var tr = _transactionManager.ConvertFromJson(j) ?? _transactionManager.ConvertFromCommandInfo(ci);

            var parameter = ci.ParameterInput.ToByteString();
            tr.Params = parameter == null ? ByteString.Empty : parameter;

            tr = tr.AddBlockReference(_rpcAddress);

            _transactionManager.SignTransaction(tr);
            var rawtx = _transactionManager.ConvertTransactionRawTx(tr);
            var req = RpcRequestManager.CreateRequest(rawtx, ci.Category, 1);

            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;

            JObject rObj = JObject.Parse(resp);
            if (rObj["error"] != null)
            {
                ci.ErrorMsg.Add(rObj.ToString());
                ci.Result = false;
                return;
            }

            string hash = rObj["result"]["TransactionId"].ToString();
            var jobj = new JObject
            {
                ["TransactionId"] = hash
            };
            ci.InfoMsg.Add(jobj.ToString());
            ci.Result = true;
        }

        public void RpcBroadcastWithRawTx(CommandInfo ci)
        {
            var rawtx = new JObject
            {
                ["rawTransaction"] = ci.Parameter
            };
            var req = RpcRequestManager.CreateRequest(rawtx, ApiMethods.BroadcastTransaction.ToString(), 1);


            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;

            var rObj = JObject.Parse(resp);
            var rj = rObj["result"];
            string hash = rj["TransactionId"] == null ? rj["error"].ToString() : rj["TransactionId"].ToString();
            string res = rj["TransactionId"] == null ? "error" : "TransactionId";
            var jobj = new JObject
            {
                [res] = hash
            };
            ci.InfoMsg.Add(jobj.ToString());

            ci.Result = true;
        }

        public string RpcGenerateTransactionRawTx(CommandInfo ci)
        {
            JObject j = null;
            if (ci.Parameter != null)
                j = JObject.Parse(ci.Parameter);
            var tr = _transactionManager.ConvertFromJson(j) ?? _transactionManager.ConvertFromCommandInfo(ci);

            if (tr.MethodName == null)
            {
                ci.ErrorMsg.Add("Method not found.");
                return string.Empty;
            }

            var parameter = ci.ParameterInput.ToByteString();
            tr.Params = parameter == null ? ByteString.Empty : parameter;
            tr = tr.AddBlockReference(_rpcAddress);

            _transactionManager.SignTransaction(tr);
            var rawtx = _transactionManager.ConvertTransactionRawTx(tr);

            return rawtx["rawTransaction"].ToString();
        }

        public string RpcGenerateTransactionRawTx(string from, string to, string methodName, IMessage inputParameter)
        {
            var tr = new Transaction()
            {
                From = Address.Parse(from),
                To = Address.Parse(to),
                MethodName = methodName
            };

            if (tr.MethodName == null)
            {
                _logger.WriteError("Method not found.");
                return string.Empty;
            }

            tr.Params = inputParameter == null ? ByteString.Empty : inputParameter.ToByteString();
            tr = tr.AddBlockReference(_rpcAddress);

            _transactionManager.SignTransaction(tr);
            var rawtx = _transactionManager.ConvertTransactionRawTx(tr);

            return rawtx["rawTransaction"].ToString();
        }

        public void RpcBroadcastTxs(CommandInfo ci)
        {
            var paramObject = new JObject
            {
                ["rawTransactions"] = ci.Parameter
            };
            var req = RpcRequestManager.CreateRequest(paramObject, ci.Category, 0);
            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;

            JObject jObj = JObject.Parse(resp);
            ci.InfoMsg.Add(jObj["result"].ToString());
            ci.Result = true;
        }

        public void RpcGetCommands(CommandInfo ci)
        {
            var req = RpcRequestManager.CreateRequest(new JObject(), ci.Category, 0);
            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;

            JObject jObj = JObject.Parse(resp);
            var j = jObj["result"];
            ci.InfoMsg.Add(j["result"]["commands"].ToString());
            ci.Result = true;
        }

        public void RpcGetContractAbi(CommandInfo ci)
        {
            if (ci.Parameter == "")
            {
                if (_genesisAddress == null)
                {
                    ci.ErrorMsg.Add("Please GetChainInformation first.");
                    return;
                }

                ci.Parameter = _genesisAddress;
            }

            var req = RpcRequestManager.CreateRequest(new JObject
            {
                ["address"] = ci.Parameter
            }, ci.Category, 1);


            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;

            JObject jObj = JObject.Parse(resp);
            var res = JObject.FromObject(jObj["result"]);

            ci.InfoMsg.Add(res.ToString());
            ci.Result = true;
        }

        public void RpcGetIncrement(CommandInfo ci)
        {
            if (!ci.CheckParameterValid(1))
                return;

            var req = RpcRequestManager.CreateRequest(new JObject
            {
                ["address"] = ci.Parameter
            }, ci.Category, 1);
            var resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;
            ci.InfoMsg.Add(resp);
            ci.Result = true;
        }

        public void RpcGetTxResult(CommandInfo ci)
        {
            if (!ci.CheckParameterValid(1))
                return;

            var req = RpcRequestManager.CreateRequest(new JObject
            {
                ["transactionId"] = ci.Parameter
            }, ci.Cmd, 0);
            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;
            ci.InfoMsg.Add(resp);
            ci.Result = true;
        }

        public void RpcGetBlockHeight(CommandInfo ci)
        {
            var req = RpcRequestManager.CreateRequest(new JObject(), ci.Cmd, 0);
            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;
            ci.InfoMsg.Add(resp);
            ci.Result = true;
        }

        public void RpcGetBlockInfo(CommandInfo ci)
        {
            if (!ci.CheckParameterValid(2))
                return;

            var req = RpcRequestManager.CreateRequest(new JObject
            {
                ["blockHeight"] = ci.Parameter.Split(" ")?[0],
                ["includeTransactions"] = ci.Parameter.Split(" ")?[1]
            }, ci.Cmd, 0);
            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;
            ci.InfoMsg.Add(resp);
            ci.Result = true;
        }

        public void RpcGetMerklePath(CommandInfo ci)
        {
            if (!ci.CheckParameterValid(1))
                return;

            var req = RpcRequestManager.CreateRequest(new JObject
            {
                ["txid"] = ci.Parameter
            }, ci.Category, 1);


            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;
            ci.InfoMsg.Add(resp);
            ci.Result = true;
        }

        public void RpcSetBlockVolume(CommandInfo ci)
        {
            if (!ci.CheckParameterValid(2))
                return;

            var req = RpcRequestManager.CreateRequest(new JObject
            {
                ["minimal"] = ci.Parameter.Split(" ")?[0],
                ["maximal"] = ci.Parameter.Split(" ")?[1]
            }, ci.Category, 1);


            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;
            ci.InfoMsg.Add(resp);
            ci.Result = true;
        }

        public string RpcQueryResult(string from, string to, string methodName, IMessage inputParameter)
        {
            var tr = new Transaction()
            {
                From = Address.Parse(from),
                To = Address.Parse(to),
                MethodName = methodName
            };

            if (tr.MethodName == null)
            {
                _logger.WriteError("Method not found.");
                return string.Empty;
            }

            tr.Params = inputParameter == null ? ByteString.Empty : inputParameter.ToByteString();

            var resp = CallTransaction(tr, "Call");

            return resp;
        }

        public void RpcQueryViewInfo(CommandInfo ci)
        {
            var requestData = new JObject
            {
                ["rawTransaction"] = ci.Parameter
            };
            var req = RpcRequestManager.CreateRequest(requestData, "Call", 1);

            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;

            var rObj = JObject.Parse(resp);
            ci.InfoMsg.Add(rObj.ToString());

            ci.Result = true;
        }

        private string CallTransaction(Transaction tx, string api)
        {
            MemoryStream ms = new MemoryStream();
            Serializer.Serialize(ms, tx);

            byte[] b = ms.ToArray();
            string payload = b.ToHex();
            var reqParams = new JObject {["rawTransaction"] = payload};
            var req = RpcRequestManager.CreateRequest(reqParams, api, 1);

            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);

            return resp;
        }

        public string GetPublicKeyFromAddress(string account, string password = "123")
        {
            _accountManager.UnlockAccount(account, password, "notimeout");
            return _accountManager.GetPublicKey(account);
        }

        //Net Api
        public void NetGetPeers(CommandInfo ci)
        {
            var req = RpcRequestManager.CreateRequest(new JObject(), "GetPeers", 1);
            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;

            ci.InfoMsg.Add(resp);
            ci.Result = true;
        }

        public void NetAddPeer(CommandInfo ci)
        {
            var req = RpcRequestManager.CreateRequest(new JObject
            {
                ["address"] = ci.Parameter
            }, "AddPeer", 1);
            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;

            ci.InfoMsg.Add(resp);
            ci.Result = true;
        }

        public void NetRemovePeer(CommandInfo ci)
        {
            var req = RpcRequestManager.CreateRequest(new JObject
            {
                ["address"] = ci.Parameter
            }, "RemovePeer", 1);
            string resp = _requestManager.PostRequest(req.ToString(), out var returnCode, out var timeSpan);
            ci.TimeSpan = timeSpan;
            if (!CheckResponse(ci, returnCode, resp))
                return;

            ci.InfoMsg.Add(resp);
            ci.Result = true;
        }

        #endregion

        private bool CheckResponse(CommandInfo ci, string returnCode, string response)
        {
            if (response == null)
            {
                ci.ErrorMsg.Add("Could not connect to server.");
                return false;
            }

            if (returnCode != "OK")
            {
                ci.ErrorMsg.Add("Http request failed, status: " + returnCode);
                return false;
            }

            if (response.IsNullOrEmpty())
            {
                ci.ErrorMsg.Add("Failed. Pleas check input.");
                return false;
            }

            return true;
        }

        private ulong GetRandomIncrId()
        {
            return Convert.ToUInt64(DateTime.Now.ToString("MMddHHmmss") + DateTime.Now.Millisecond.ToString());
        }
    }
}