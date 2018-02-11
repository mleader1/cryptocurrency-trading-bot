using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using mleader.tradingbot.Data.Cex;

namespace mleader.tradingbot.Data
{
    public class AccountBalance
    {
        public AccountBalanceItem BalanceBTC { get; set; }
        public AccountBalanceItem BalanceBCH { get; set; }
        public AccountBalanceItem BalanceETH { get; set; }
        public AccountBalanceItem BalanceLTC { get; set; }
        public AccountBalanceItem BalanceDASH { get; set; }
        public AccountBalanceItem BalanceZEC { get; set; }
        public AccountBalanceItem BalanceUSD { get; set; }
        public AccountBalanceItem BalanceEUR { get; set; }
        public AccountBalanceItem BalanceGBP { get; set; }
        public AccountBalanceItem BalanceRUB { get; set; }


        public Dictionary<string, AccountBalanceItem> CurrencyBalances
        {
            get
            {
                var result = new Dictionary<string, AccountBalanceItem>
                {
                    {"BTC", BalanceBTC},
                    {"BCH", BalanceBCH},
                    {"ETH", BalanceETH},
                    {"LTC", BalanceLTC},
                    {"DASH", BalanceDASH},
                    {"ZEC", BalanceZEC},
                    {"USD", BalanceUSD},
                    {"EUR", BalanceEUR},
                    {"GBP", BalanceGBP},
                    {"RUB", BalanceRUB}
                };
                return result;
            }
        }
    }

    public abstract class AccountBalanceItem
    {
        protected AccountBalanceItem(string currency)
        {
            Currency = currency;
        }

        public string Currency { get; set; }
        public abstract decimal Available { get; set; }
        public abstract decimal InOrders { get; set; }
        public decimal Total => Available + InOrders;
    }
}