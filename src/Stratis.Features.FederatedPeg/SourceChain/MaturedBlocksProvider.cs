﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Controllers;
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
        /// <param name="maxDeposits">The number of deposits to retrieve.</param>
        /// <returns>A list of mature block deposits.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the blocks are not mature or not found.</exception>
        Task<Result<List<MaturedBlockDepositsModel>>> GetMaturedDepositsAsync(int blockHeight, int maxBlocks, int maxDeposits = int.MaxValue);
    }

    public class MaturedBlocksProvider : IMaturedBlocksProvider
    {
        private readonly IDepositExtractor depositExtractor;

        private readonly IConsensusManager consensusManager;

        private readonly ChainIndexer chainIndexer;

        private readonly ILogger logger;

        public MaturedBlocksProvider(ILoggerFactory loggerFactory, IDepositExtractor depositExtractor, IConsensusManager consensusManager, ChainIndexer chainIndexer)
        {
            this.depositExtractor = depositExtractor;
            this.consensusManager = consensusManager;
            this.chainIndexer = chainIndexer;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public async Task<Result<List<MaturedBlockDepositsModel>>> GetMaturedDepositsAsync(int blockHeight, int maxBlocks, int maxDeposits = int.MaxValue)
        {
            ChainedHeader consensusTip = this.consensusManager.Tip;

            if (consensusTip == null)
            {
                return Result<List<MaturedBlockDepositsModel>>.Fail("Not ready to provide blocks.");
            }

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

            int numDeposits = 0;

            ChainedHeader prevBlock = this.chainIndexer.GetHeader(blockHeight).Previous;

            while ((prevBlock.Height < matureTipHeight) && ((maxBlocks -= 100) > 0))
            {
                List<ChainedHeader> blockHeaders = this.chainIndexer.EnumerateAfter(prevBlock).Take(Math.Min(matureTipHeight - prevBlock.Height, 100)).ToList();

                List<uint256> hashes = blockHeaders.Select(h => h.HashBlock).ToList();

                ChainedHeaderBlock[] blocks = this.consensusManager.GetBlockData(hashes);

                foreach (ChainedHeaderBlock chainedHeaderBlock in blocks)
                {
                    if (chainedHeaderBlock?.Block?.Transactions == null)
                    {
                        this.logger.LogDebug("Unexpected null data. Send what we've collected.");

                        return Result<List<MaturedBlockDepositsModel>>.Ok(maturedBlocks);
                    }

                    MaturedBlockDepositsModel maturedBlockDeposits = this.depositExtractor.ExtractBlockDeposits(chainedHeaderBlock);

                    maturedBlocks.Add(maturedBlockDeposits);

                    numDeposits += maturedBlockDeposits.Deposits?.Count ?? 0;

                    if (maturedBlocks.Count >= maxBlocks || numDeposits >= maxDeposits)
                        return Result<List<MaturedBlockDepositsModel>>.Ok(maturedBlocks);

                    if (cancellation.IsCancellationRequested)
                    {
                        this.logger.LogDebug("Stop matured blocks collection because it's taking too long. Send what we've collected.");

                        return Result<List<MaturedBlockDepositsModel>>.Ok(maturedBlocks);
                    }
                }

                prevBlock = blockHeaders.Last();
            }

            return Result<List<MaturedBlockDepositsModel>>.Ok(maturedBlocks);
        }
    }
}