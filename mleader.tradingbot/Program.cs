using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using mleader.tradingbot.Engine;
using mleader.tradingbot.Engine.Cex;
using Microsoft.Extensions.Logging;

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

            var tradingStrategy = new TradingStrategy
            {
                HoursOfAccountHistoryOrderForPurchaseDecision = 24,
                HoursOfAccountHistoryOrderForSellDecision = 24,
                HoursOfPublicHistoryOrderForPurchaseDecision = 24,
                HoursOfPublicHistoryOrderForSellDecision = 24,
                MinimumReservePercentageAfterInit = 0.1m,
                OrderCapPercentageAfterInit = 0.9m,
                OrderCapPercentageOnInit = 0.25m,
                AutoDecisionExecution = true,
                StopLine = 8000
            };

            tradingEngines.Add(new CexTradingEngine(new ExchangeApiConfig()
            {
                ApiSecret = secret,
                ApiUsername = username,
                ApiKey = apiKey,
                Logger = loggerFactory.CreateLogger($"{typeof(CexTradingEngine).Name} - BTC - USD")
            }, "BTC", "USD", tradingStrategy));

            var tasks = new List<Task>();
            foreach (var engine in tradingEngines)
            {
                tasks.Add(Task.Run(async () => await engine.StartAsync()));
            }

            Task.WaitAll(tasks.ToArray());
        }
    }
}