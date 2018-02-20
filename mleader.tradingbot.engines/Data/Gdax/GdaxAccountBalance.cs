using System.Collections.Generic;
using System.Linq;
using mleader.tradingbot.Data;
using Newtonsoft.Json;

namespace mleader.tradingbot.engines.Data.Gdax
{
    public class GdaxAccountBalance : List<GdaxAccountBalanceItem>
    {
        public AccountBalance ToAccountBalance()
        {
            return new AccountBalance
            {
                BalanceBCH = this.FirstOrDefault(item => item.Currency == "BCH"),
                BalanceBTC = this.FirstOrDefault(item => item.Currency == "BTC"),
                BalanceDASH = this.FirstOrDefault(item => item.Currency == "DASH"),
                BalanceETH = this.FirstOrDefault(item => item.Currency == "ETH"),
                BalanceEUR = this.FirstOrDefault(item => item.Currency == "EUR"),
                BalanceGBP = this.FirstOrDefault(item => item.Currency == "GBP"),
                BalanceLTC = this.FirstOrDefault(item => item.Currency == "LTC"),
                BalanceRUB = this.FirstOrDefault(item => item.Currency == "RUB"),
                BalanceUSD = this.FirstOrDefault(item => item.Currency == "USD"),
                BalanceZEC = this.FirstOrDefault(item => item.Currency == "ZEC"),
            };
        }
    }

    public class GdaxAccountBalanceItem : AccountBalanceItem
    {
        public GdaxAccountBalanceItem(string currency) : base(currency)
        {
        }

        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("currency")] public string Currency { get; set; }
        [JsonProperty("balance")] public decimal Balance { get; set; }
        [JsonProperty("available")] public override decimal Available { get; set; }
        [JsonProperty("hold")] public override decimal InOrders { get; set; }
        [JsonProperty("profile_id")] public string ProfileId { get; set; }
    }
}