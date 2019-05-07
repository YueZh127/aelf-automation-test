using System.Threading;
using AElf.Automation.Common.Helpers;

namespace AElf.Automation.ContractsTesting
{
    public class NodesState
    {
        private static readonly ILogHelper Log = LogHelper.GetLogHelper();

        public static void NodeStateCheck(string name, string rpcUrl)
        {
            var apiHelper = new WebApiHelper(rpcUrl);
            var nodeStatus = new NodeStatus(apiHelper);
            long height = 1;
            while (true)
            {
                string message;
                var currentHeight = nodeStatus.GetBlockHeight();
                var txPoolCount = nodeStatus.GetTransactionPoolStatus();
                if (currentHeight == height)
                {
                    message = $"Node: {name}, TxPool Count: {txPoolCount}";
                    Log.WriteInfo(message);
                    Thread.Sleep(250);
                }
                else
                {
                    height = currentHeight;
                    message = $"Node: {name}, Height: {currentHeight}, TxPool Count: {txPoolCount}";
                    Log.WriteInfo(message);
                    Thread.Sleep(500);
                }
            }
        }
    }
}