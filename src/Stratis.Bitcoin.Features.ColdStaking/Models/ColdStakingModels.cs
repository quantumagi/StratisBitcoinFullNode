﻿using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Stratis.Bitcoin.Utilities.ValidationAttributes;

namespace Stratis.Bitcoin.Features.ColdStaking.Models
{
    /// <summary>
    /// The data structure used by a client requesting a cold staking address.
    /// Refer to <see cref="Controllers.ColdStakingController.GetColdStakingAddress(GetColdStakingAddressRequest)"/>.
    /// </summary>
    public class GetColdStakingAddressRequest
    {
        /// <summary>The wallet name.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletName")]
        public string WalletName { get; set; }

        /// <summary>The (optional) wallet password. Required for generating cold staking accounts on demand.</summary>
        [JsonProperty(PropertyName = "walletPassword")]
        public string WalletPassword { get; set; }

        /// <summary>Determines from which of the cold staking accounts the address will be taken.</summary>
        [Required]
        [JsonProperty(PropertyName = "isColdWalletAddress")]
        public bool IsColdWalletAddress { get; set; }
    }

    /// <summary>
    /// The response data structure received by a client after requesting a cold staking address.
    /// Refer to <see cref="GetColdStakingAddressRequest"/>.
    /// </summary>
    public class GetColdStakingAddressResponse
    {
        /// <summary>A Base58 cold staking address from the hot or cold wallet accounts.</summary>
        [JsonProperty(PropertyName = "address")]
        public string Address { get; set; }
    }

    /// <summary>
    /// The data structure used by a client requesting that a cold staking setup be performed.
    /// Refer to <see cref="Controllers.ColdStakingController.SetupColdStaking(SetupColdStakingRequest)"/>.
    /// </summary>
    public class SetupColdStakingRequest
    {
        /// <summary>The Base58 cold wallet address.</summary>
        [Required]
        [JsonProperty(PropertyName = "coldWalletAddress")]
        public string ColdWalletAddress { get; set; }

        /// <summary>The Base58 hot wallet address.</summary>
        [Required]
        [JsonProperty(PropertyName = "hotWalletAddress")]
        public string HotWalletAddress { get; set; }

        /// <summary>The name of the wallet from which we select coins for cold staking.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletName")]
        public string WalletName { get; set; }

        /// <summary>The password of the wallet from which we select coins for cold staking.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletPassword")]
        public string WalletPassword { get; set; }

        /// <summary>The wallet account from which we select coins for cold staking.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletAccount")]
        public string WalletAccount { get; set; }

        /// <summary>The amount of coins selected for cold staking.</summary>
        [Required]
        [MoneyFormat(ErrorMessage = "The amount is not in the correct format.")]
        [JsonProperty(PropertyName = "amount")]
        public string Amount { get; set; }

        /// <summary>The fees for the cold staking setup transaction.</summary>
        [Required]
        [MoneyFormat(ErrorMessage = "The fees are not in the correct format.")]
        [JsonProperty(PropertyName = "fees")]
        public string Fees { get; set; }
    }

    /// <summary>
    /// The response data structure received by a client after requesting that a cold staking setup be performed.
    /// Refer to <see cref="SetupColdStakingRequest"/>.
    /// </summary>
    public class SetupColdStakingResponse
    {
        /// <summary>The transaction bytes as a hexadecimal string.</summary>
        [JsonProperty(PropertyName = "transactionHex")]
        public string TransactionHex { get; set; }
    }
}
