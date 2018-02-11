using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using mleader.tradingbot.Data;
using mleader.tradingbot.Engine;
using mleader.tradingbot.Engine.Cex;
using Microsoft.Extensions.Logging;
using System.Linq;
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


            Console.Write("CEX API Secret:");
            var secret = Console.ReadLine();
            Console.Write("\nCEX Username:");
            var username = Console.ReadLine();
            Console.Write("\nCex API Key:");
            var apiKey = Console.ReadLine();
            Console.Write("\nSlack Notification Webhook Url:");
            var slackWebhook = Console.ReadLine();


            var exchangeCurrency = string.Empty;
            while (exchangeCurrency.IsNullOrEmpty())
            {
                Console.Write("\nCex Exhcange Base currency name: (default BTC)");
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
                Console.Write("\nCex Exhcange Target currency name: (default USD)");
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

            bool autoExecution;
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine("Automated order execution - Enter 'CONFIRM' to execute order automatically: ");
            Console.ResetColor();
            autoExecution = Console.ReadLine() == "CONFIRM";


            var tradingStrategy = new TradingStrategy
            {
                HoursOfAccountHistoryOrderForPurchaseDecision = 24,
                HoursOfAccountHistoryOrderForSellDecision = 24,
                HoursOfPublicHistoryOrderForPurchaseDecision = 24,
                HoursOfPublicHistoryOrderForSellDecision = 24,
                MinimumReservePercentageAfterInit = 0.1m,
                OrderCapPercentageAfterInit = 0.9m,
                OrderCapPercentageOnInit = 0.25m,
                AutoDecisionExecution = autoExecution,
                StopLine = stopLine
            };

            tradingEngines.Add(new CexTradingEngine(new ExchangeApiConfig()
            {
                ApiSecret = secret,
                ApiUsername = username,
                ApiKey = apiKey,
                SlackWebhook = slackWebhook,
                Logger = loggerFactory.CreateLogger($"{typeof(CexTradingEngine).Name} - BTC - USD")
            }, exchangeCurrency, targetCurrency, tradingStrategy));

            var tasks = new List<Task>();
            foreach (var engine in tradingEngines)
            {
                tasks.Add(Task.Run(async () => await engine.StartAsync()));
            }

            Task.WaitAll(tasks.ToArray());
        }
    }
}