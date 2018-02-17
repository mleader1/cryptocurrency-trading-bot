using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;

namespace mleader.tradingbot.Data
{
    public class Currencies
    {
        public const string BTC = "BTC";
        public const string BCH = "BCH";
        public const string ETH = "ETH";
        public const string LTC = "LTC";
        public const string DASH = "DASH";
        public const string ZEC = "ZEC";
        public const string USD = "USD";
        public const string EUR = "EUR";
        public const string GBP = "GBP";
        public const string RUB = "RUB";
        public const string XRP = "XRP";


        public static List<string> SupportedCurrencies => new List<string>
        {
            BTC,
            BCH,
            ETH,
            LTC,
            DASH,
            XRP,
            ZEC,
            USD,
            EUR,
            GBP,
            RUB
        };

        public static List<string> SupportedFiatCurrencies => new List<string>
        {
            USD,
            EUR,
            GBP,
            RUB
        };
    }
}