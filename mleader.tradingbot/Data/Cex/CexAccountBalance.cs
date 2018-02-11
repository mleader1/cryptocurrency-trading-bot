using Newtonsoft.Json;
using OElite;

namespace mleader.tradingbot.Data.Cex
{
    public class CexAccountBalance
    {
        [JsonProperty("timestamp")]
        public double Timestamp { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("BTC")]
        public CexAccountBalanceItem BalanceBTC { get; set; }

        [JsonProperty("BCH")]
        public CexAccountBalanceItem BalanceBCH { get; set; }

        [JsonProperty("ETH")]
        public CexAccountBalanceItem BalanceETH { get; set; }

        [JsonProperty("LTC")]
        public CexAccountBalanceItem BalanceLTC { get; set; }

        [JsonProperty("DASH")]
        public CexAccountBalanceItem BalanceDASH { get; set; }

        [JsonProperty("ZEC")]
        public CexAccountBalanceItem BalanceZEC { get; set; }

        [JsonProperty("USD")]
        public CexAccountBalanceItem BalanceUSD { get; set; }

        [JsonProperty("EUR")]
        public CexAccountBalanceItem BalanceEUR { get; set; }

        [JsonProperty("GBP")]
        public CexAccountBalanceItem BalanceGBP { get; set; }

        [JsonProperty("RUB")]
        public CexAccountBalanceItem BalanceRUB { get; set; }

        public AccountBalance ToAccountBalance()
        {
            if (BalanceBCH != null) BalanceBCH.Currency = "BCH";
            if (BalanceBTC != null) BalanceBTC.Currency = "BTC";
            if (BalanceDASH != null) BalanceDASH.Currency = "DASH";
            if (BalanceETH != null) BalanceETH.Currency = "ETH";
            if (BalanceEUR != null) BalanceEUR.Currency = "ERU";
            if (BalanceGBP != null) BalanceGBP.Currency = "GBP";
            if (BalanceLTC != null) BalanceLTC.Currency = "LTC";
            if (BalanceUSD != null) BalanceUSD.Currency = "USD";
            if (BalanceZEC != null) BalanceZEC.Currency = "ZEC";

            return new AccountBalance
            {
                BalanceBCH = BalanceBCH,
                BalanceBTC = BalanceBTC,
                BalanceDASH = BalanceDASH,
                BalanceETH = BalanceETH,
                BalanceEUR = BalanceEUR,
                BalanceGBP = BalanceGBP,
                BalanceLTC = BalanceLTC,
                BalanceRUB = BalanceRUB,
                BalanceUSD = BalanceUSD,
                BalanceZEC = BalanceZEC
            };
        }
    }

    public class CexAccountBalanceItem : AccountBalanceItem
    {
        [JsonProperty("available")]
        public override decimal Available { get; set; }

        [JsonProperty("orders")]
        public override decimal InOrders { get; set; }

        public CexAccountBalanceItem(string currency) : base(currency)
        {
        }
    }
}