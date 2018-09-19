﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;

[assembly: InternalsVisibleTo("Stratis.Bitcoin.Features.ColdStaking.Tests")]

namespace Stratis.Bitcoin.Features.ColdStaking
{
    /// <summary>
    /// The manager class for implementing cold staking as covered in more detail in the remarks of
    /// the <see cref="ColdStakingFeature"/> class.
    /// This class provides the methods used by the <see cref="Controllers.ColdStakingController"/>
    /// which in turn provides the API methods for accessing this functionality.
    /// </summary>
    /// <remarks>
    /// The following functionality is implemented in this class:
    /// <list type="bullet">
    /// <item><description>Generating cold staking address via the <see cref="GetFirstUnusedColdStakingAddress"/> method. These
    /// addresses are used for generating the cold staking setup.</description></item>
    /// <item><description>Creating a build context for generating the cold staking setup via the <see
    /// cref="GetColdStakingSetupTransaction"/> method.</description></item>
    /// </list>
    /// </remarks>
    public class ColdStakingManager : WalletManager, IWalletManager
    {
        private static Func<HdAccount, bool> coldStakingAccounts = a => a.Index >= Wallet.Wallet.SpecialPurposeAccountIndexesStart;

        /// <summary>The account index of the cold wallet account.</summary>
        private const int ColdWalletAccountIndex = Wallet.Wallet.SpecialPurposeAccountIndexesStart + 0;

        /// <summary>The account name of the cold wallet account.</summary>
        private const string ColdWalletAccountName = "coldStakingColdAddresses";

        /// <summary>The account index of the hot wallet account.</summary>
        private const int HotWalletAccountIndex = Wallet.Wallet.SpecialPurposeAccountIndexesStart + 1;

        /// <summary>The account name of the hot wallet account.</summary>
        private const string HotWalletAccountName = "coldStakingHotAddresses";

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Provider of time functions.</summary>
        private readonly IDateTimeProvider dateTimeProvider;

        /// <summary>
        /// Constructs the cold staking manager which is used by the cold staking controller.
        /// </summary>
        /// <param name="network">The network that the manager is running on.</param>
        /// <param name="chain">Thread safe class representing a chain of headers from genesis.</param>
        /// <param name="walletSettings">The wallet settings.</param>
        /// <param name="dataFolder">Contains path locations to folders and files on disk.</param>
        /// <param name="walletFeePolicy">The wallet fee policy.</param>
        /// <param name="asyncLoopFactory">Factory for creating and also possibly starting application defined tasks inside async loop.</param>
        /// <param name="nodeLifeTime">Allows consumers to perform cleanup during a graceful shutdown.</param>
        /// <param name="scriptAddressReader">A reader for extracting an address from a <see cref="Script"/>.</param>
        /// <param name="loggerFactory">The logger factory to use to create the custom logger.</param>
        /// <param name="dateTimeProvider">Provider of time functions.</param>
        /// <param name="broadcasterManager">The broadcaster manager.</param>
        public ColdStakingManager(
            Network network,
            ConcurrentChain chain,
            WalletSettings walletSettings,
            DataFolder dataFolder,
            IWalletFeePolicy walletFeePolicy,
            IAsyncLoopFactory asyncLoopFactory,
            INodeLifetime nodeLifeTime,
            IScriptAddressReader scriptAddressReader,
            ILoggerFactory loggerFactory,
            IDateTimeProvider dateTimeProvider,
            IBroadcasterManager broadcasterManager = null) : base(
                loggerFactory,
                network,
                chain,
                // Not used by wallet manager. Pass a non-null dummy value.
                NodeSettings.Default(network),
                walletSettings,
                dataFolder,
                walletFeePolicy,
                asyncLoopFactory,
                nodeLifeTime,
                dateTimeProvider,
                scriptAddressReader,
                broadcasterManager
                )
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(dateTimeProvider, nameof(dateTimeProvider));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.dateTimeProvider = dateTimeProvider;
        }

        /// <summary>
        /// Returns information related to cold staking.
        /// </summary>
        /// <param name="walletName">The wallet to return the information for.</param>
        /// <returns>A <see cref="Models.GetColdStakingInfoResponse"/> object containing the information.</returns>
        internal Models.GetColdStakingInfoResponse GetColdStakingInfo(string walletName)
        {
            this.logger.LogTrace("({0}:'{1}')", nameof(walletName), walletName);

            Wallet.Wallet wallet = this.GetWalletByName(walletName);

            var response = new Models.GetColdStakingInfoResponse()
            {
                ColdWalletAccountExists = this.GetColdStakingAccount(wallet, true) != null,
                HotWalletAccountExists = this.GetColdStakingAccount(wallet, false) != null
            };

            this.logger.LogTrace("(-):'{0}'", response);
            return response;
        }

        /// <summary>
        /// Gets a cold staking account.
        /// </summary>
        /// <remarks>
        /// <para>In order to keep track of cold staking addresses and balances we are using <see cref="HdAccount"/>'s
        /// with indexes starting from the value defined in <see cref="Wallet.Wallet.SpecialPurposeAccountIndexesStart"/>.
        /// </para><para>
        /// We are using two such accounts, one when the wallet is in the role of cold wallet, and another one when
        /// the wallet is in the role of hot wallet. For this reason we specify the required account when calling this
        /// method.
        /// </para></remarks>
        /// <param name="wallet">The wallet where we wish to create the account.</param>
        /// <param name="isColdWalletAccount">Indicates whether we need the cold wallet account (versus the hot wallet account).</param>
        /// <returns>The cold staking account or <c>null</c> if the account does not exist.</returns>
        internal HdAccount GetColdStakingAccount(Wallet.Wallet wallet, bool isColdWalletAccount)
        {
            this.logger.LogTrace("({0}:'{1}',{2}:{3})", nameof(wallet), wallet.Name, nameof(isColdWalletAccount), isColdWalletAccount);

            var coinType = (CoinType)wallet.Network.Consensus.CoinType;
            HdAccount account = null;
            try
            {
                account = wallet.GetAccountByCoinType(isColdWalletAccount ? ColdWalletAccountName : HotWalletAccountName, coinType);
            }
            catch (Exception) { }

            if (account == null)
            {
                this.logger.LogTrace("(-)[ACCOUNT_DOES_NOT_EXIST]:null");
                return null;
            }

            this.logger.LogTrace("(-):'{0}'", account.Name);
            return account;
        }

        /// <summary>
        /// Creates a cold staking account and ensures that it has at least one address.
        /// If the account already exists then the existing account is returned.
        /// </summary>
        /// <remarks>
        /// <para>In order to keep track of cold staking addresses and balances we are using <see cref="HdAccount"/>'s
        /// with indexes starting from the value defined in <see cref="Wallet.Wallet.SpecialPurposeAccountIndexesStart"/>.
        /// </para><para>
        /// We are using two such accounts, one when the wallet is in the role of cold wallet, and another one when
        /// the wallet is in the role of hot wallet. For this reason we specify the required account when calling this
        /// method.
        /// </para></remarks>
        /// <param name="walletName">The name of the wallet where we wish to create the account.</param>
        /// <param name="isColdWalletAccount">Indicates whether we need the cold wallet account (versus the hot wallet account).</param>
        /// <param name="walletPassword">The wallet password which will be used to create the account.</param>
        /// <returns>The new or existing cold staking account.</returns>
        internal HdAccount GetOrCreateColdStakingAccount(string walletName, bool isColdWalletAccount, string walletPassword)
        {
            this.logger.LogTrace("({0}:'{1}',{2}:{3})", nameof(walletName), walletName, nameof(isColdWalletAccount), isColdWalletAccount);

            Wallet.Wallet wallet = this.GetWalletByName(walletName);

            HdAccount account = this.GetColdStakingAccount(wallet, isColdWalletAccount);
            if (account != null)
            {
                this.logger.LogTrace("(-)[ACCOUNT_ALREADY_EXIST]:'{0}'", account.Name);
                return account;
            }

            int accountIndex = isColdWalletAccount ? ColdWalletAccountIndex : HotWalletAccountIndex;
            var coinType = (CoinType)wallet.Network.Consensus.CoinType;

            this.logger.LogTrace("The {0} wallet account for '{1}' does not exist and will now be created.", isColdWalletAccount ? "cold" : "hot", wallet.Name);

            AccountRoot accountRoot = wallet.AccountsRoot.Single(a => a.CoinType == coinType);

            account = accountRoot.CreateAccount(walletPassword, wallet.EncryptedSeed,
                wallet.ChainCode, wallet.Network, this.dateTimeProvider.GetTimeOffset(), accountIndex,
                isColdWalletAccount ? ColdWalletAccountName: HotWalletAccountName);

            // Maintain at least one unused address at all times. This will ensure that wallet recovery will also work.
            account.CreateAddresses(wallet.Network, 1, false);

            ICollection<HdAccount> hdAccounts = accountRoot.Accounts.ToList();
            hdAccounts.Add(account);
            accountRoot.Accounts = hdAccounts;

            this.logger.LogTrace("(-):'{0}'", account.Name);
            return account;
        }

        /// <summary>
        /// Gets the first unused cold staking address. Creates a new address if required.
        /// </summary>
        /// <param name="walletName">The name of the wallet providing the cold staking address.</param>
        /// <param name="isColdWalletAddress">Indicates whether we need the cold wallet address (versus the hot wallet address).</param>
        /// <returns>The cold staking address or <c>null</c> if the required account does not exist.</returns>
        internal HdAddress GetFirstUnusedColdStakingAddress(string walletName, bool isColdWalletAddress)
        {
            Guard.NotNull(walletName, nameof(walletName));

            this.logger.LogTrace("({0}:'{1}',{2}:{3})", nameof(walletName), walletName, nameof(isColdWalletAddress), isColdWalletAddress);

            Wallet.Wallet wallet = this.GetWalletByName(walletName);
            HdAccount account = this.GetColdStakingAccount(wallet, isColdWalletAddress);
            if (account == null)
            {
                this.logger.LogTrace("(-)[ACCOUNT_DOES_NOT_EXIST]:null");
                return null;
            }

            HdAddress address = account.GetFirstUnusedReceivingAddress();
            if (address == null)
            {
                this.logger.LogTrace("No unused address exists on account '{0}'. Adding new address.", account.Name);
                address = account.CreateAddresses(wallet.Network, 1).First();
            }

            this.logger.LogTrace("(-):'{0}'", address.Address);
            return address;
        }

        /// <summary>
        /// Creates cold staking setup <see cref="Transaction"/>.
        /// </summary>
        /// <remarks>
        /// The <paramref name="coldWalletAddress"/> and <paramref name="hotWalletAddress"/> would be expected to be
        /// from different wallets and typically also different physical machines under normal circumstances. The following
        /// rules are enforced by this method and would lead to a <see cref="WalletException"/> otherwise:
        /// <list type="bullet">
        /// <item><description>The cold and hot wallet addresses are expected to belong to different wallets.</description></item>
        /// <item><description>Either the cold or hot wallet address must belong to a cold staking account in the wallet identified
        /// by <paramref name="walletName"/></description></item>
        /// <item><description>The account specified in <paramref name="walletAccount"/> can't be a cold staking account.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="walletTransactionHandler">The wallet transaction handler. Contains the <see cref="WalletTransactionHandler.BuildTransaction"/> method.</param>
        /// <param name="coldWalletAddress">The cold wallet address generated by <see cref="GetColdStakingAddress"/>.</param>
        /// <param name="hotWalletAddress">The hot wallet address generated by <see cref="GetColdStakingAddress"/>.</param>
        /// <param name="walletName">The name of the wallet.</param>
        /// <param name="walletAccount">The wallet account.</param>
        /// <param name="walletPassword">The wallet password.</param>
        /// <param name="amount">The amount to cold stake.</param>
        /// <param name="feeAmount">The fee to pay for the cold staking setup transaction.</param>
        /// <returns>The <see cref="Transaction"/> for setting up cold staking.</returns>
        /// <exception cref="WalletException">Thrown if any of the rules listed in the remarks section of this method are broken.</exception>
        internal Transaction GetColdStakingSetupTransaction(IWalletTransactionHandler walletTransactionHandler,
            string coldWalletAddress, string hotWalletAddress, string walletName, string walletAccount,
            string walletPassword, Money amount, Money feeAmount)
        {
            Guard.NotNull(walletTransactionHandler, nameof(walletTransactionHandler));
            Guard.NotEmpty(coldWalletAddress, nameof(coldWalletAddress));
            Guard.NotEmpty(hotWalletAddress, nameof(hotWalletAddress));
            Guard.NotEmpty(walletName, nameof(walletName));
            Guard.NotEmpty(walletAccount, nameof(walletAccount));
            Guard.NotNull(amount, nameof(amount));
            Guard.NotNull(feeAmount, nameof(feeAmount));

            this.logger.LogTrace("({0}:'{1}',{2}:'{3}',{4}:'{5}',{6}:'{7}',{8}:{9},{10}:{11})",
                nameof(coldWalletAddress), coldWalletAddress,
                nameof(hotWalletAddress), hotWalletAddress,
                nameof(walletName), walletName,
                nameof(walletAccount), walletAccount,
                nameof(amount), amount,
                nameof(feeAmount), feeAmount
                );

            Wallet.Wallet wallet = this.GetWalletByName(walletName);

            // Get/create the cold staking accounts.
            HdAccount coldAccount = this.GetOrCreateColdStakingAccount(walletName, true, walletPassword);
            HdAccount hotAccount = this.GetOrCreateColdStakingAccount(walletName, false, walletPassword);

            bool thisIsColdWallet = coldAccount?.ExternalAddresses.Select(a => a.Address).Contains(coldWalletAddress) ?? false;
            bool thisIsHotWallet = hotAccount?.ExternalAddresses.Select(a => a.Address).Contains(hotWalletAddress) ?? false;

            this.logger.LogTrace("Local wallet '{0}' does{1} contain cold wallet address '{2}' and does{3} contain hot wallet address '{4}'.",
                walletName, thisIsColdWallet ? "" : " NOT", coldWalletAddress, thisIsHotWallet ? "" : " NOT", hotWalletAddress);

            if (thisIsColdWallet && thisIsHotWallet)
            {
                this.logger.LogTrace("(-)[COLDSTAKE_BOTH_HOT_AND_COLD]");
                throw new WalletException("You can't use this wallet as both hot wallet and cold wallet.");
            }

            if (!thisIsColdWallet && !thisIsHotWallet)
            {
                this.logger.LogTrace("(-)[COLDSTAKE_ADDRESSES_NOT_IN_ACCOUNTS]");
                throw new WalletException("The hot and cold wallet addresses could not be found in the corresponding accounts.");
            }

            KeyId hotPubKeyHash = new BitcoinPubKeyAddress(hotWalletAddress, wallet.Network).Hash;
            KeyId coldPubKeyHash = new BitcoinPubKeyAddress(coldWalletAddress, wallet.Network).Hash;
            Script destination = ColdStakingScriptTemplate.Instance.GenerateScriptPubKey(hotPubKeyHash, coldPubKeyHash);

            // Only normal accounts should be allowed.
            if (!this.GetAccounts(walletName).Any(a => a.Name == walletAccount))
            {
                this.logger.LogTrace("(-)[COLDSTAKE_ACCOUNT_NOT_FOUND]");
                throw new WalletException($"Can't find wallet account '{walletAccount}'.");
            }

            var context = new TransactionBuildContext(wallet.Network)
            {
                AccountReference = new WalletAccountReference(walletName, walletAccount),
                TransactionFee = feeAmount,
                MinConfirmations = 0,
                Shuffle = false,
                WalletPassword = walletPassword,
                Recipients = new List<Recipient>() { new Recipient { Amount = amount, ScriptPubKey = destination } }
            };

            // Avoid errors being raised due to the special script that we are using.
            context.TransactionBuilder.StandardTransactionPolicy.CheckScriptPubKey = false;

            Transaction transaction = walletTransactionHandler.BuildTransaction(context);

            this.logger.LogTrace("(-)");
            return transaction;
        }

        /// <summary>
        /// Creates cold staking cancellation <see cref="Transaction"/>.
        /// </summary>
        /// <remarks>
        /// Cold staking cancellation can only be performed on the wallet that is in the role of the cold staking cold wallet.
        /// The <paramref name="coldWalletAddress"/> would be expected to be from the wallet identified in <paramref name="walletName"/>
        /// and the <paramref name="hotWalletAddress"/> would be expected NOT to be from the wallet identifed in <paramref name="walletName"/>.
        /// The following rules are enforced by this method and would lead to a <see cref="WalletException"/> otherwise:
        /// <list type="bullet">
        /// <item><description>the <paramref name="hotWalletAddress"/> would be expected NOT to be from the wallet identifed in <paramref name="walletName"/>.</description></item>
        /// <item><description>The <paramref name="coldWalletAddress"/> would be expected to be from the wallet identified in
        /// <paramref name="walletName"/></description></item>
        /// <item><description>The account specified in <paramref name="walletAccount"/> can't be a cold staking account.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="walletTransactionHandler">The wallet transaction handler. Contains the <see cref="WalletTransactionHandler.BuildTransaction"/> method.</param>
        /// <param name="coldWalletAddress">The cold wallet address generated by <see cref="GetColdStakingAddress"/>.</param>
        /// <param name="hotWalletAddress">The hot wallet address generated by <see cref="GetColdStakingAddress"/>.</param>
        /// <param name="walletName">The name of the wallet in the role of cold wallet.</param>
        /// <param name="walletPassword">The wallet password.</param>
        /// <param name="amount">The amount to remove from cold staking.</param>
        /// <param name="feeAmount">The fee to pay for cold staking transaction cancellation.</param>
        /// <returns>The <see cref="Transaction"/> for cold staking cancellation.</returns>
        /// <exception cref="WalletException">Thrown if any of the rules listed in the remarks section of this method are broken.</exception>
        internal Transaction GetColdStakingCancellationTransaction(IWalletTransactionHandler walletTransactionHandler,
            string coldWalletAddress, string hotWalletAddress, string walletName,
            string walletPassword, Money amount, Money feeAmount)
        {
            Guard.NotEmpty(coldWalletAddress, nameof(coldWalletAddress));
            Guard.NotEmpty(hotWalletAddress, nameof(hotWalletAddress));
            Guard.NotEmpty(walletName, nameof(walletName));
            Guard.NotNull(amount, nameof(amount));
            Guard.NotNull(feeAmount, nameof(feeAmount));

            this.logger.LogTrace("({0}:'{1}',{2}:'{3}',{4}:'{5}',{6}:'{7}',{8}:{9})",
                nameof(coldWalletAddress), coldWalletAddress,
                nameof(hotWalletAddress), hotWalletAddress,
                nameof(walletName), walletName,
                nameof(amount), amount,
                nameof(feeAmount), feeAmount
                );

            Wallet.Wallet wallet = this.GetWalletByName(walletName);

            // Get/create the cold staking account.
            HdAccount coldAccount = this.GetColdStakingAccount(wallet, true);
            if (coldAccount == null)
            {
                this.logger.LogTrace("(-)[COLDSTAKE_ACCOUNT_DOES_NOT_EXIST]");
                throw new WalletException("The cold wallet account does not exist.");
            }

            if (coldAccount.ExternalAddresses.Select(a => a.Address).Contains(hotWalletAddress))
            {
                this.logger.LogTrace("(-)[COLDSTAKE_BOTH_HOT_AND_COLD]");
                throw new WalletException("You can't use this wallet as both hot wallet and cold wallet.");
            }

            HdAddress coldAddress = coldAccount.ExternalAddresses.Where(a => a.Address == coldWalletAddress).FirstOrDefault();
            if (coldAddress == null)
            {
                this.logger.LogTrace("(-)[COLDSTAKE_ADDRESS_DOES_NOT_EXIST]");
                throw new WalletException("The cold wallet address does not exist.");
            }

            Script destination = BitcoinAddress.Create(coldWalletAddress, wallet.Network).ScriptPubKey.PaymentScript;

            var context = new TransactionBuildContext(wallet.Network)
            {
                AccountReference = new WalletAccountReference(walletName, coldAccount.Name),
                TransactionFee = feeAmount,
                MinConfirmations = 0,
                Shuffle = false,
                WalletPassword = walletPassword,
                Recipients = new[] { new Recipient { Amount = amount, ScriptPubKey = destination } }.ToList()
            };

            // Avoid errors being raised due to the special script that we are using.
            context.TransactionBuilder.StandardTransactionPolicy.CheckScriptPubKey = false;

            Transaction transaction = walletTransactionHandler.BuildTransaction(context);

            this.logger.LogTrace("(-):'{0}'", transaction.GetHash());
            return transaction;
        }

        /// <summary>
        /// Gets the spendable transactions associated with cold wallet addresses.
        /// </summary>
        /// <param name="walletName">The name of the wallet.</param>
        /// <param name="isColdWalletAccount">The cold staking account to get the transactions for.</param>
        /// <param name="confirmations">The number of confirmations.</param>
        /// <returns>An enumeration of <see cref="UnspentOutputReference"/> items.</returns>
        public IEnumerable<UnspentOutputReference> GetSpendableTransactionsInColdWallet(string walletName, bool isColdWalletAccount, int confirmations = 0)
        {
            Guard.NotEmpty(walletName, nameof(walletName));
            this.logger.LogTrace("({0}:'{1}',{2}:{3})", nameof(walletName), walletName, nameof(confirmations), confirmations);

            Wallet.Wallet wallet = this.GetWalletByName(walletName);
            UnspentOutputReference[] res = null;
            lock (this.lockObject)
            {
                res = wallet.GetAllSpendableTransactions(this.coinType, this.chain.Tip.Height, confirmations,
                    a => a.Index == (isColdWalletAccount?ColdWalletAccountIndex:HotWalletAccountIndex)).ToArray();
            }

            this.logger.LogTrace("(-):*.Count={0}", res.Count());
            return res;
        }
    }
}
