﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Controllers;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Primitives;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;

namespace Stratis.Features.FederatedPeg.SourceChain
{
    public interface IMaturedBlocksProvider
    {
        /// <summary>
        /// Retrieves deposits for the indicated blocks from the block repository and throws an error if the blocks are not mature enough.
        /// </summary>
        /// <param name="blockHeight">The block height at which to start retrieving blocks.</param>
        /// <param name="maxBlocks">The number of blocks to retrieve.</param>
        /// <returns>A list of mature block deposits.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the blocks are not mature or not found.</exception>
        Task<Result<List<MaturedBlockDepositsModel>>> GetMaturedDepositsAsync(int blockHeight, int maxBlocks);
    }

    public class MaturedBlocksProvider : IMaturedBlocksProvider
    {
        private readonly IDepositExtractor depositExtractor;

        private readonly IConsensusManager consensusManager;

        private readonly ILogger logger;

        private readonly IBlockStore blockStore;

        private readonly Network network;

        public MaturedBlocksProvider(ILoggerFactory loggerFactory, IDepositExtractor depositExtractor, IConsensusManager consensusManager, Network network = null, IBlockStore blockStore = null)
        {
            this.depositExtractor = depositExtractor;
            this.consensusManager = consensusManager;
            this.blockStore = blockStore;
            this.network = network;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public async Task<Result<List<MaturedBlockDepositsModel>>> GetMaturedDepositsAsync(int blockHeight, int maxBlocks)
        {
            ChainedHeader consensusTip = this.consensusManager.Tip;

            int matureTipHeight = (consensusTip.Height - (int)this.depositExtractor.MinimumDepositConfirmations);

            if (blockHeight > matureTipHeight)
            {
                // We need to return a Result type here to explicitly indicate failure and the reason for failure.
                // This is an expected condition so we can avoid throwing an exception here.
                return Result<List<MaturedBlockDepositsModel>>.Fail($"Block height {blockHeight} submitted is not mature enough. Blocks less than a height of {matureTipHeight} can be processed.");
            }

            var maturedBlocks = new List<MaturedBlockDepositsModel>();

            // Half of the timeout. We will also need time to convert it to json.
            int maxTimeCollectionCanTakeMs = RestApiClientBase.TimeoutMs / 2;
            var cancellation = new CancellationTokenSource(maxTimeCollectionCanTakeMs);

            for (int i = blockHeight; (i <= matureTipHeight) && (i < blockHeight + maxBlocks); i++)
            {
                ChainedHeader currentHeader = consensusTip.GetAncestor(i);

                ChainedHeaderBlock block = this.consensusManager.GetBlockData(currentHeader.HashBlock);

                if (block?.Block?.Transactions == null)
                {
                    // Report unexpected results from consenus manager.
                    this.logger.LogWarning("Stop matured blocks collection due to consensus manager integrity failure. Send what we've collected.");
                    break;
                }

                MaturedBlockDepositsModel maturedBlockDeposits = this.depositExtractor.ExtractBlockDeposits(block);

                if (maturedBlockDeposits == null)
                    throw new InvalidOperationException($"Unable to get deposits for block at height {currentHeader.Height}");

                maturedBlocks.Add(maturedBlockDeposits);

                if (cancellation.IsCancellationRequested && maturedBlocks.Count > 0)
                {
                    this.logger.LogDebug("Stop matured blocks collection because it's taking too long. Send what we've collected.");
                    break;
                }
            }

            if (this.blockStore != null && this.network != null)
                this.ResolveRefundAddresses(maturedBlocks);

            return Result<List<MaturedBlockDepositsModel>>.Ok(maturedBlocks);
        }

        private void ResolveRefundAddresses(List<MaturedBlockDepositsModel> blockList)
        {
            var senderTx = new HashSet<uint256>();
            foreach (MaturedBlockDepositsModel maturedBlockDepositsModel in blockList)
            {
                foreach (IDeposit deposit in maturedBlockDepositsModel.Deposits)
                {
                    if (!senderTx.Contains(deposit.FirstTxIn.PrevOut.Hash))
                        senderTx.Add(deposit.FirstTxIn.PrevOut.Hash);
                }
            }

            var senderTxArr = new uint256[senderTx.Count];
            senderTx.CopyTo(senderTxArr);

            Transaction[] senderTransactions = this.blockStore.GetTransactionsByIds(senderTxArr);
            if (senderTransactions == null)
                throw new InvalidOperationException("Transaction indexing must be enabled!");

            var senderTxLookup = senderTransactions.ToDictionary(x => x.GetHash(), x => x);

            foreach (MaturedBlockDepositsModel maturedBlockDepositsModel in blockList)
            {
                foreach (IDeposit deposit in maturedBlockDepositsModel.Deposits)
                {
                    OutPoint prevOut = deposit.FirstTxIn.PrevOut;
                    TxOut txOut = senderTxLookup[prevOut.Hash].Outputs[prevOut.N];
                    deposit.SenderAddress = txOut.ScriptPubKey.GetDestinationAddress(this.network).ToString();
                }
            }
        }
    }
}