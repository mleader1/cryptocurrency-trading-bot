using mleader.tradingbot.Data;
using Newtonsoft.Json;

namespace mleader.tradingbot.engines.Data.Bitstamp
{
    public class BitstampAccountBalance
    {
        [JsonProperty("timestamp")] public double Timestamp { get; set; }

        [JsonProperty("username")] public string Username { get; set; }

        [JsonProperty("btc_balance")] public decimal BalanceBTC { get; set; }
        [JsonProperty("btc_reserved")] public decimal InOrdersBTC { get; set; }

        [JsonProperty("bch_balance")] public decimal BalanceBCH { get; set; }
        [JsonProperty("bch_reserved")] public decimal InOrdersBCH { get; set; }

        [JsonProperty("eth_balance")] public decimal BalanceETH { get; set; }
        [JsonProperty("eth_reserved")] public decimal InOrdersETH { get; set; }

        [JsonProperty("ltc_balance")] public decimal BalanceLTC { get; set; }
        [JsonProperty("ltc_reserved")] public decimal InOrdersLTC { get; set; }

        [JsonProperty("dash_balance")] public decimal BalanceDASH { get; set; }
        [JsonProperty("dash_reserved")] public decimal InOrdersDASH { get; set; }

        [JsonProperty("zec_balance")] public decimal BalanceZEC { get; set; }
        [JsonProperty("zec_reserved")] public decimal InOrdersZEC { get; set; }

        [JsonProperty("xrp_balance")] public decimal BalanceXRP { get; set; }
        [JsonProperty("xrp_reserved")] public decimal InOrdersXRP { get; set; }

        [JsonProperty("usd_balance")] public decimal BalanceUSD { get; set; }
        [JsonProperty("usd_reserved")] public decimal InOrdersUSD { get; set; }

        [JsonProperty("eur_balance")] public decimal BalanceEUR { get; set; }
        [JsonProperty("eur_reserved")] public decimal InOrdersEUR { get; set; }

        [JsonProperty("gbp_balance")] public decimal BalanceGBP { get; set; }
        [JsonProperty("gbp_reserved")] public decimal InOrdersGBP { get; set; }

        public AccountBalance ToAccountBalance()
        {
            return new AccountBalance
            {
                BalanceBCH =
                    new BitstampAccountBalanceItem("BCH")
                    {
                        Available = BalanceBCH - InOrdersBCH,
                        InOrders = InOrdersBCH
                    },
                BalanceBTC =
                    new BitstampAccountBalanceItem("BTC")
                    {
                        Available = BalanceBTC - InOrdersBTC,
                        InOrders = InOrdersBTC
                    },
                BalanceDASH =
                    new BitstampAccountBalanceItem("DASH")
                    {
                        Available = BalanceDASH - InOrdersDASH,
                        InOrders = InOrdersDASH
                    },
                BalanceETH =
                    new BitstampAccountBalanceItem("ETH")
                    {
                        Available = BalanceETH - InOrdersETH,
                        InOrders = InOrdersETH
                    },
                BalanceEUR =
                    new BitstampAccountBalanceItem("EUR")
                    {
                        Available = BalanceEUR - InOrdersEUR,
                        InOrders = InOrdersEUR
                    },
                BalanceGBP =
                    new BitstampAccountBalanceItem("GBP")
                    {
                        Available = BalanceGBP - InOrdersGBP,
                        InOrders = InOrdersGBP
                    },
                BalanceLTC =
                    new BitstampAccountBalanceItem("LTC")
                    {
                        Available = BalanceLTC - InOrdersLTC,
                        InOrders = InOrdersLTC
                    },
                BalanceRUB = null,
                BalanceUSD =
                    new BitstampAccountBalanceItem("USD")
                    {
                        Available = BalanceUSD - InOrdersUSD,
                        InOrders = InOrdersUSD
                    },
                BalanceZEC =
                    new BitstampAccountBalanceItem("ZEC") {Available = BalanceZEC - InOrdersZEC, InOrders = InOrdersZEC}
            };
        }
    }

    public class BitstampAccountBalanceItem : AccountBalanceItem
    {
        public override decimal Available { get; set; }

        public override decimal InOrders { get; set; }

        public BitstampAccountBalanceItem(string currency) : base(currency)
        {
            Currency = currency;
        }
    }
}