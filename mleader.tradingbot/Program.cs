using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using mleader.tradingbot.Data;
using mleader.tradingbot.engines;
using mleader.tradingbot.Engine;
using mleader.tradingbot.Engine.Api;
using Microsoft.Extensions.Logging;
using OElite;

namespace mleader.tradingbot
{
    class Program
    {
        static void Main(string[] args)
        {
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddConsole();
            Console.WriteLine("======================");
            var tradingEngines = new List<ITradingEngine>();

            var useExchange = SupportedExchanges.Unknown;
            while (useExchange == SupportedExchanges.Unknown)
            {
                Console.WriteLine("Type 1 for CEX.IO, Type 2 for GDAX, Type 3 for Bitstamp:");
                useExchange =
                    NumericUtils.GetIntegerValueFromObject(Console.ReadLine()).ParseEnum<SupportedExchanges>();
            }


            Console.Write($"{useExchange.ToString()} API Secret:");
            var secret = Console.ReadLine();
            switch (useExchange)
            {
                case SupportedExchanges.Cex:
                    Console.Write("\n CEX Username:");
                    break;
                case SupportedExchanges.Gdax:
                    Console.Write("\n GDAX Pass phrase:");
                    break;
                case SupportedExchanges.Bitstamp:
                    Console.Write("\n Bitstamp Client Id:");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var username = Console.ReadLine();


            Console.Write($"\n{useExchange.ToString()} API Key:");
            var apiKey = Console.ReadLine();
            Console.Write("\nSlack Notification Webhook Url:");
            var slackWebhook = Console.ReadLine();


            var exchangeCurrency = string.Empty;
            while (exchangeCurrency.IsNullOrEmpty())
            {
                Console.Write($"\n{useExchange.ToString()} Exhcange Base currency name: (default BTC)");
                exchangeCurrency = Console.ReadLine();
                if (Currencies.SupportedCurrencies.Count(i => i == exchangeCurrency) > 0) continue;

                if (exchangeCurrency.IsNullOrEmpty())
                {
                    Console.WriteLine("Default cryptocurrency BTC selected.");
                    exchangeCurrency = Currencies.BTC;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.BackgroundColor = ConsoleColor.White;
                    Console.WriteLine("Invalid currency name. Please try again.");
                    Console.ResetColor();
                    exchangeCurrency = null;
                }
            }

            var targetCurrency = string.Empty;
            while (targetCurrency.IsNullOrEmpty())
            {
                Console.Write($"\n{useExchange.ToString()} Exhcange Target currency name: (default USD)");
                targetCurrency = Console.ReadLine();
                if (Currencies.SupportedCurrencies.Count(i => i == targetCurrency) > 0) continue;

                if (targetCurrency.IsNullOrEmpty())
                {
                    Console.WriteLine("Default target currency USD selected.");
                    targetCurrency = Currencies.USD;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.BackgroundColor = ConsoleColor.White;
                    Console.WriteLine("Invalid currency name. Please try again.");
                    Console.ResetColor();
                    targetCurrency = null;
                }
            }


            var stopLine = 0m;
            while (stopLine <= 0)
            {
                Console.Write("\nSpecify the bottom line value where execution should STOP:");
                stopLine = NumericUtils.GetDecimalValueFromObject(Console.ReadLine());
                if (stopLine > 0) continue;

                Console.ForegroundColor = ConsoleColor.Red;
                Console.BackgroundColor = ConsoleColor.White;
                Console.WriteLine(
                    "Bottom line value must be positive number representing your target currency value. (e.g. 5000 )");
                Console.ResetColor();
            }

            Console.WriteLine(
                $"Minutes of historical orders on {useExchange.ToString()} for buying considerations: (default 3)");
            var publicOrderHistoryForBuyingDecision = NumericUtils.GetIntegerValueFromObject(Console.ReadLine());
            if (publicOrderHistoryForBuyingDecision <= 0) publicOrderHistoryForBuyingDecision = 3;

            Console.WriteLine(
                $"Minutes of historical orders on {useExchange.ToString()} for selling considerations: (default 3)");
            var publicOrderHistoryForSellingDecision = NumericUtils.GetIntegerValueFromObject(Console.ReadLine());
            if (publicOrderHistoryForSellingDecision <= 0) publicOrderHistoryForSellingDecision = 3;

            Console.WriteLine("Minutes of historical account orders for buying considerations: (default 5)");
            var accountOrderHistoryForBuyingDecision = NumericUtils.GetIntegerValueFromObject(Console.ReadLine());
            if (accountOrderHistoryForBuyingDecision <= 0) accountOrderHistoryForBuyingDecision = 5;

            Console.WriteLine("Minutes of historical account orders for selling considerations: (default 5)");
            var accountOrderHistoryForSellingDecision = NumericUtils.GetIntegerValueFromObject(Console.ReadLine());
            if (accountOrderHistoryForSellingDecision <= 0) accountOrderHistoryForSellingDecision = 5;

            Console.WriteLine("Minutes change sensitivity ratio in decimal: (default 0.005)");
            var sensitivityRatio = NumericUtils.GetDecimalValueFromObject(Console.ReadLine());
            if (sensitivityRatio <= 0) sensitivityRatio = 0.005m;

            Console.WriteLine("Minimum Reserve In Target Currency: (default 0.2)");
            var minimumReservePercentageAfterInitInTargetCurrency =
                NumericUtils.GetDecimalValueFromObject(Console.ReadLine());
            if (minimumReservePercentageAfterInitInTargetCurrency <= 0)
                minimumReservePercentageAfterInitInTargetCurrency = 0.2m;

            Console.WriteLine("Minimum Reserve In Exchange Currency: (default 0.2)");
            var minimumReservePercentageAfterInitInExchangeCurrency =
                NumericUtils.GetDecimalValueFromObject(Console.ReadLine());
            if (minimumReservePercentageAfterInitInExchangeCurrency <= 0)
                minimumReservePercentageAfterInitInExchangeCurrency = 0.2m;

            Console.WriteLine("Order Cap Percentage On Init: (default 0.25)");
            var orderCapPercentageOnInit =
                NumericUtils.GetDecimalValueFromObject(Console.ReadLine());
            if (orderCapPercentageOnInit <= 0)
                orderCapPercentageOnInit = 0.25m;

            Console.WriteLine("Order Cap Percentage After Init: (default 0.3)");
            var orderCapPercentageAfterInit =
                NumericUtils.GetDecimalValueFromObject(Console.ReadLine());
            if (orderCapPercentageAfterInit <= 0)
                orderCapPercentageAfterInit = 0.3m;


            bool autoExecution;
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine("Automated order execution - Enter 'CONFIRM' to execute order automatically: ");
            Console.ResetColor();
            autoExecution = Console.ReadLine() == "CONFIRM";


            var tradingStrategy = new TradingStrategy
            {
                MinutesOfAccountHistoryOrderForPurchaseDecision = accountOrderHistoryForBuyingDecision,
                MinutesOfAccountHistoryOrderForSellDecision = accountOrderHistoryForSellingDecision,
                MinutesOfPublicHistoryOrderForPurchaseDecision = publicOrderHistoryForBuyingDecision,
                MinutesOfPublicHistoryOrderForSellDecision = publicOrderHistoryForSellingDecision,
                MinimumReservePercentageAfterInitInTargetCurrency = minimumReservePercentageAfterInitInTargetCurrency,
                MinimumReservePercentageAfterInitInExchangeCurrency =
                    minimumReservePercentageAfterInitInExchangeCurrency,
                OrderCapPercentageAfterInit = orderCapPercentageAfterInit,
                OrderCapPercentageOnInit = orderCapPercentageOnInit,
                AutoDecisionExecution = autoExecution,
                StopLine = stopLine,
                MarketChangeSensitivityRatio = sensitivityRatio,
                PriceCorrectionFrequencyInHours = 6,
                TradingValueBleedRatio = 0.1m
            };

            IApi api;
            switch (useExchange)
            {
                case SupportedExchanges.Gdax:
                    api = new GdaxApi(apiKey, secret, username, slackWebhook,
                        loggerFactory.CreateLogger($"GDAX Trading Engine - {exchangeCurrency} - {targetCurrency}"),
                        tradingStrategy);
                    break;
                case SupportedExchanges.Cex:
                    api = new CexApi(apiKey, secret, username, slackWebhook,
                        loggerFactory.CreateLogger($"CEX.IO Trading Engine - {exchangeCurrency} - {targetCurrency}"),
                        tradingStrategy);
                    break;
                case SupportedExchanges.Bitstamp:
                    api = new BitstampApi(apiKey, secret, username, slackWebhook,
                        loggerFactory.CreateLogger($"Bitstamp Trading Engine - {exchangeCurrency} - {targetCurrency}"),
                        tradingStrategy);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            tradingEngines.Add(new TradingEngine(api, exchangeCurrency, targetCurrency));

            var tasks = new List<Task>();
            foreach (var engine in tradingEngines)
            {
                tasks.Add(Task.Run(async () => await engine.StartAsync()));
            }

            Task.WaitAll(tasks.ToArray());
        }
    }
}