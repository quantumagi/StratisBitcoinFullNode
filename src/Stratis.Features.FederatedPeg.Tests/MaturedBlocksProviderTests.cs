using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NSubstitute;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.FederatedPeg.SourceChain;
using Stratis.Sidechains.Networks;
using Xunit;

namespace Stratis.Features.FederatedPeg.Tests
{
    public class MaturedBlocksProviderTests
    {
        private readonly Network network;

        private readonly IDepositExtractor depositExtractor;

        private readonly ILoggerFactory loggerFactory;

        private readonly ILogger logger;

        private readonly IConsensusManager consensusManager;

        public MaturedBlocksProviderTests()
        {
            this.network = CirrusNetwork.NetworksSelector.Regtest();

            this.loggerFactory = Substitute.For<ILoggerFactory>();
            this.logger = Substitute.For<ILogger>();
            this.loggerFactory.CreateLogger(null).ReturnsForAnyArgs(this.logger);
            this.depositExtractor = Substitute.For<IDepositExtractor>();
            this.consensusManager = Substitute.For<IConsensusManager>();
        }

        private MaturedBlocksProvider GetMaturedBlocksProvider()
        {
            var blockRepository = Substitute.For<IBlockRepository>();

            blockRepository.GetBlocks(Arg.Any<List<uint256>>()).ReturnsForAnyArgs((x) =>
            {
                var hashes = x.ArgAt<List<uint256>>(0);
                var blocks = new List<Block>();

                foreach (uint256 hash in hashes)
                {
                    blocks.Add(this.network.CreateBlock());
                }

                return blocks;
            });

            return new MaturedBlocksProvider(
                this.loggerFactory,
                this.depositExtractor,
                this.consensusManager,
                new ChainIndexer(this.network, this.consensusManager.Tip));
        }

        [Fact]
        public async Task GetMaturedBlocksAsyncReturnsDeposits()
        {
            ChainedHeader tip = ChainedHeadersHelper.CreateConsecutiveHeaders(10, null, true, network: this.network).Last();
            this.consensusManager.Tip.Returns(tip);

            uint zero = 0;
            this.depositExtractor.MinimumDepositConfirmations.Returns(info => zero);
            this.depositExtractor.ExtractBlockDeposits(null).ReturnsForAnyArgs(new MaturedBlockDepositsModel(new MaturedBlockInfoModel(), new List<IDeposit>()));

            // Makes every block a matured block.
            var maturedBlocksProvider = GetMaturedBlocksProvider();

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).ReturnsForAnyArgs((x) => {
                List<uint256> hashes = x.ArgAt<List<uint256>>(0);
                Block block = this.network.CreateBlock();
                return hashes.Select((hash) => new ChainedHeaderBlock(block, new ChainedHeader(block.Header, hash, null))).ToArray();
            });

            Result<List<MaturedBlockDepositsModel>> depositsResult = await maturedBlocksProvider.GetMaturedDepositsAsync(0, 10);

            // Expect the number of matured deposits to equal the number of blocks.
            Assert.Equal(10, depositsResult.Value.Count);
        }
    }
}
