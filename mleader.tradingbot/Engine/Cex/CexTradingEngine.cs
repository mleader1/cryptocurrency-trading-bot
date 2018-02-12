using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using mleader.tradingbot.Data;
using mleader.tradingbot.Data.Cex;
using Newtonsoft.Json.Linq;
using OElite;

namespace mleader.tradingbot.Engine.Cex
{
    public class CexTradingEngine : ITradingEngine
    {
        public ExchangeApiConfig ApiConfig { get; set; }
        public string ReserveCurrency { get; set; }
        public Dictionary<string, decimal> MinimumCurrencyOrderAmount { get; set; }

        public List<ITradeHistory> LatestPublicSaleHistory { get; set; }
        public List<ITradeHistory> LatestPublicPurchaseHistory { get; set; }
        public List<IOrder> LatestAccountSaleHistory { get; set; }
        public List<IOrder> LatestAccountPurchaseHistory { get; set; }
        private List<IOrder> AccountOpenOrders { get; set; }

        public ITradingStrategy TradingStrategy { get; }

        private string OperatingExchangeCurrency { get; }
        private string OperatingTargetCurrency { get; }
        private List<CurrencyLimit> CurrencyLimits { get; set; }
        private CurrencyLimit ExchangeCurrencyLimit { get; set; }
        private CurrencyLimit TargetCurrencyLimit { get; set; }
        private decimal InitialBuyingCap { get; set; }
        private decimal InitialSellingCap { get; set; }
        private int InitialBatchCycles { get; set; }

        private System.Timers.Timer RequestTimer { get; set; }

        private System.Timers.Timer FeecheckTimer { get; set; }
        private int ApiRequestcrruedAllowance { get; set; }
        private int ApiRequestCounts { get; set; }
        private AccountBalance AccountBalance { get; set; }
        private bool _isActive = true;
        private bool SleepNeeded = false;
        private bool AutoExecution = false;
        private int InputTimeout = 5000;

        public CexTradingEngine(ExchangeApiConfig apiConfig, string exchangeCurrency, string targetCurrency,
            ITradingStrategy strategy)
        {
            ApiConfig = apiConfig;
            OperatingExchangeCurrency = exchangeCurrency;
            OperatingTargetCurrency = targetCurrency;
            TradingStrategy = strategy ?? new TradingStrategy
            {
                MinutesOfAccountHistoryOrderForPurchaseDecision = 60,
                MinutesOfAccountHistoryOrderForSellDecision = 60,
                MinutesOfPublicHistoryOrderForPurchaseDecision = 120,
                MinutesOfPublicHistoryOrderForSellDecision = 120,
                MinimumReservePercentageAfterInit = 0.1m,
                OrderCapPercentageAfterInit = 0.6m,
                OrderCapPercentageOnInit = 0.25m,
                AutoDecisionExecution = true,
                MarketChangeSensitivityRatio = 0.03m
            };

            AutoExecution = TradingStrategy.AutoDecisionExecution;

            Rest = new Rest("https://cex.io/api/",
                new RestConfig
                {
                    OperationMode = RestMode.HTTPRestClient,
                    UseRestConvertForCollectionSerialization = false
                },
                apiConfig?.Logger);


//            Console.WriteLine("Init Cex Trading Engine");
            RequestTimer = new System.Timers.Timer(1000) {Enabled = true, AutoReset = true};
            RequestTimer.Elapsed += (sender, args) =>
            {
                ApiRequestCounts++;

                if (ApiRequestCounts > ApiRequestcrruedAllowance)
                {
                    if (ApiRequestCounts - ApiRequestcrruedAllowance > 0)
                        SleepNeeded = true;

                    ApiRequestCounts = ApiRequestCounts - ApiRequestcrruedAllowance;
                    ApiRequestcrruedAllowance = 0;
                }
                else
                {
                    ApiRequestCounts = 0;
                    ApiRequestcrruedAllowance = ApiRequestcrruedAllowance - ApiRequestCounts;
                }
            };
            FeecheckTimer = new System.Timers.Timer(1000 * 60 * 3) {Enabled = true, AutoReset = true};
            FeecheckTimer.Elapsed += (sender, args) => RefreshAccountFeesAsync().Wait();

            FirstBatchPreparationAsync().Wait();
        }

        public async Task FirstBatchPreparationAsync()
        {
            var balance = await GetAccountBalanceAsync();
            if (balance == null) return;

            await RefreshCexCurrencyLimitsAsync();

            var totalExchangeCurrencyBalance =
            (AccountBalance?.CurrencyBalances?.Where(item => item.Key == OperatingExchangeCurrency)
                .Select(c => c.Value?.Total)
                .FirstOrDefault()).GetValueOrDefault();
            var totalTargetCurrencyBalance = (AccountBalance?.CurrencyBalances
                ?.Where(item => item.Key == OperatingTargetCurrency)
                .Select(c => c.Value?.Total)
                .FirstOrDefault()).GetValueOrDefault();

            ExchangeCurrencyLimit =
                CurrencyLimits?.FirstOrDefault(item => item.ExchangeCurrency == OperatingExchangeCurrency);
            TargetCurrencyLimit =
                CurrencyLimits?.FirstOrDefault(item => item.ExchangeCurrency == OperatingTargetCurrency);

            InitialBuyingCap =
                totalTargetCurrencyBalance * TradingStrategy.OrderCapPercentageOnInit;
            InitialSellingCap =
                totalExchangeCurrencyBalance * TradingStrategy.OrderCapPercentageOnInit;

            if (ExchangeCurrencyLimit?.MaximumExchangeAmount < InitialBuyingCap)
                InitialBuyingCap = ExchangeCurrencyLimit.MaximumExchangeAmount == null
                    ? decimal.MaxValue
                    : ExchangeCurrencyLimit.MaximumExchangeAmount.GetValueOrDefault();

            if (ExchangeCurrencyLimit?.MinimumExchangeAmount >= InitialBuyingCap)
                InitialBuyingCap = ExchangeCurrencyLimit.MinimumExchangeAmount == null
                    ? 0
                    : ExchangeCurrencyLimit.MinimumExchangeAmount.GetValueOrDefault();

            if (TargetCurrencyLimit?.MaximumExchangeAmount < InitialSellingCap)
                InitialSellingCap = TargetCurrencyLimit.MaximumExchangeAmount == null
                    ? decimal.MaxValue
                    : TargetCurrencyLimit.MaximumExchangeAmount.GetValueOrDefault();
            if (TargetCurrencyLimit?.MinimumExchangeAmount >= InitialSellingCap)
                InitialSellingCap = TargetCurrencyLimit.MinimumExchangeAmount == null
                    ? 0
                    : TargetCurrencyLimit.MinimumExchangeAmount.GetValueOrDefault();
            if (InitialBuyingCap <= 0) InitialBuyingCap = totalExchangeCurrencyBalance;
            if (InitialSellingCap <= 0) InitialSellingCap = totalTargetCurrencyBalance;


            InitialBatchCycles = (int) Math.Min(
                InitialBuyingCap > 0 ? totalTargetCurrencyBalance / InitialBuyingCap : 0,
                InitialSellingCap > 0 ? totalExchangeCurrencyBalance / InitialSellingCap : 0);
        }

        public Task StartAsync()
        {
            SendWebhookMessage("*Trading Engine Started* :smile:");
            while (_isActive)
            {
                if (SleepNeeded)
                {
                    SleepNeeded = false;
                    var count = ApiRequestCounts - ApiRequestcrruedAllowance;
                    count = count - ApiRequestcrruedAllowance;
                    ApiRequestCounts = 0;

                    ApiRequestcrruedAllowance = 0;
                    if (count > 0)
                    {
                        count = count > 5 ? 5 : count;
                        Thread.Sleep(count * 1000);
                    }
                }

                try
                {
                    MarkeDecisionsAsync().Wait();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }

                Thread.Sleep(1000);
                ApiRequestcrruedAllowance++;
            }

            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            _isActive = false;
            SendWebhookMessage("*Trading Engine Stopped* :end:");
            Thread.CurrentThread.Abort();
            return Task.CompletedTask;
        }

        #region Price Calculations        

        public async Task<bool> MarkeDecisionsAsync()
        {
            #region Get Historical Trade Histories

            var latestThousandTradeHistories =
                await Rest.GetAsync<List<TradeHistory>>(
                    $"trade_history/{OperatingExchangeCurrency}/{OperatingTargetCurrency}");
            ApiRequestCounts++;
            var error = false;
            if (latestThousandTradeHistories?.Count > 0)
            {
                var lastXMinutes =
                    latestThousandTradeHistories.Where(item => item.Timestamp >= DateTime.UtcNow.AddMinutes(-1 * (
                                                                                                                TradingStrategy
                                                                                                                    .MinutesOfPublicHistoryOrderForPurchaseDecision >
                                                                                                                TradingStrategy
                                                                                                                    .MinutesOfPublicHistoryOrderForSellDecision
                                                                                                                    ? TradingStrategy
                                                                                                                        .MinutesOfPublicHistoryOrderForPurchaseDecision
                                                                                                                    : TradingStrategy
                                                                                                                        .MinutesOfPublicHistoryOrderForSellDecision
                                                                                                            )));


                LatestPublicPurchaseHistory = lastXMinutes
                    .Where(item => item.OrderType == OrderType.Buy && item.Timestamp >=
                                   DateTime.UtcNow.AddMinutes(
                                       -1 * TradingStrategy.MinutesOfPublicHistoryOrderForPurchaseDecision))
                    .Select(item => item as ITradeHistory).ToList();
                LatestPublicSaleHistory = lastXMinutes.Where(item =>
                        item.OrderType == OrderType.Sell && item.Timestamp >=
                        DateTime.UtcNow.AddMinutes(-1 * TradingStrategy.MinutesOfPublicHistoryOrderForSellDecision))
                    .Select(item => item as ITradeHistory).ToList();
            }
            else
            {
                LatestPublicPurchaseHistory = new List<ITradeHistory>();
                LatestPublicSaleHistory = new List<ITradeHistory>();
                error = true;
            }

            if (error) return !error;

//            Console.WriteLine(
//                $"Cex Exchange order executions in last " +
//                $"{(TradingStrategy.HoursOfPublicHistoryOrderForPurchaseDecision > TradingStrategy.HoursOfPublicHistoryOrderForSellDecision ? TradingStrategy.HoursOfPublicHistoryOrderForPurchaseDecision : TradingStrategy.HoursOfPublicHistoryOrderForSellDecision)} hours: " +
//                $"\t BUY: {LatestPublicPurchaseHistory?.Count}\t SELL: {LatestPublicSaleHistory?.Count}");

            #endregion

            #region Get Account Trade Histories

            var nonce = GetNonce();
            var latestAccountTradeHistories = await Rest.PostAsync<List<FullOrder>>(
                $"archived_orders/{OperatingExchangeCurrency}/{OperatingTargetCurrency}", new
                {
                    Key = ApiConfig.ApiKey,
                    Signature = GetApiSignature(nonce),
                    Nonce = nonce,
                    DateFrom = (DateTime.UtcNow.AddMinutes(
                                    -1 * (TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision >
                                          TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision
                                        ? TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision
                                        : TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision)) -
                                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds,
                    DateTo = (DateTime.UtcNow.AddMinutes(
                                  (TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision >
                                   TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision
                                      ? TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision
                                      : TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision)) -
                              new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds,
                });
            ApiRequestCounts++;

            if (latestAccountTradeHistories == null)
            {
                Console.WriteLine(await Rest.PostAsync<string>(
                    $"archived_orders/{OperatingExchangeCurrency}/{OperatingTargetCurrency}", new
                    {
                        Key = ApiConfig.ApiKey,
                        Signature = GetApiSignature(nonce),
                        Nonce = nonce,
                        DateFrom = (DateTime.UtcNow.AddMinutes(
                                        -1 * (TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision >
                                              TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision
                                            ? TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision
                                            : TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision)) -
                                    new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds,
                        DateTo = (DateTime.UtcNow.AddMinutes(
                                      (TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision >
                                       TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision
                                          ? TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision
                                          : TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision)) -
                                  new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds,
                    }));

                Console.WriteLine("\n [Unable to receive account records] - Carrying 3 seconds sleep...");

                Thread.Sleep(3000);
                ApiRequestcrruedAllowance = 0;
                ApiRequestCounts = 0;
                return false;
            }

            if (latestAccountTradeHistories?.Count > 0)
            {
                LatestAccountPurchaseHistory = latestAccountTradeHistories
                    .Where(item => item.Type == OrderType.Buy && item.Timestamp >=
                                   DateTime.UtcNow.AddMinutes(
                                       -1 * TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision))
                    .Select(item => item as IOrder).ToList();
                LatestAccountSaleHistory = latestAccountTradeHistories
                    .Where(item => item.Type == OrderType.Sell && item.Timestamp >=
                                   DateTime.UtcNow.AddMinutes(
                                       -1 * TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision))
                    .Select(item => item as IOrder).ToList();
            }
            else
            {
                LatestAccountSaleHistory = new List<IOrder>();
                LatestAccountPurchaseHistory = new List<IOrder>();
                error = true;
            }

//            Console.WriteLine(
//                $"Account orders executions in last " +
//                $"{(TradingStrategy.HoursOfAccountHistoryOrderForPurchaseDecision > TradingStrategy.HoursOfAccountHistoryOrderForSellDecision ? TradingStrategy.HoursOfAccountHistoryOrderForPurchaseDecision : TradingStrategy.HoursOfAccountHistoryOrderForSellDecision)} hours: " +
//                $"\t BUY: {LatestAccountPurchaseHistory?.Count}\t SELL: {LatestAccountSaleHistory?.Count}");

            #endregion

            await RefreshAccountFeesAsync();
            AccountBalance = await GetAccountBalanceAsync();
            if (AccountBalance == null)
            {
                Console.WriteLine("\n [Unable to receive account balance] - Carrying 3 seconds sleep...");

                Thread.Sleep(3000);
                ApiRequestcrruedAllowance = 0;
                ApiRequestCounts = 0;
                return false;
            }

            AccountOpenOrders = await GetOpenOrdersAsync();
            if (AccountOpenOrders == null)
            {
                Console.WriteLine("\n [Unable to receive account open orders] - Carrying 3 seconds sleep...");

                Thread.Sleep(3000);
                ApiRequestcrruedAllowance = 0;
                ApiRequestCounts = 0;
                return false;
            }

            try
            {
                await DrawDecisionUIsAsync();
            }
            catch (Exception ex)
            {
                Console.Clear();
            }
            finally
            {
                if (InitialBatchCycles > 0)
                    InitialBatchCycles--;
            }

            return !error;
        }

        private async Task RefreshCexCurrencyLimitsAsync()
        {
            var result = await Rest.GetAsync<JObject>("currency_limits");
            ApiRequestCounts++;
            var limits = result?["data"]?["pairs"]?.Children().Select(item => item.ToObject<CurrencyLimit>());

            CurrencyLimits = limits?.ToList() ?? new List<CurrencyLimit>();
        }

        private async Task RefreshAccountFeesAsync()
        {
            var nonce = GetNonce();
            var myFees = await Rest.PostAsync<JObject>("get_myfee", new
            {
                key = ApiConfig.ApiKey,
                signature = GetApiSignature(nonce),
                nonce
            });
            ApiRequestCounts++;
            SellingFeeInPercentage = (myFees?.GetValue("data")
                                         ?.Value<JToken>($"{OperatingExchangeCurrency}:{OperatingTargetCurrency}")
                                         ?.Value<decimal>("sell"))
                                     .GetValueOrDefault() / 100;
            SellingFeeInAmount = 0;
            BuyingFeeInPercentage = (myFees?.GetValue("data")
                                        ?.Value<JToken>($"{OperatingExchangeCurrency}:{OperatingTargetCurrency}")
                                        ?.Value<decimal>("buy"))
                                    .GetValueOrDefault() / 100;
            BuyingFeeInAmount = 0;
        }

        public Task<decimal> GetSellingPriceInPrincipleAsync() => Task.FromResult(Math.Floor(ProposedSellingPrice *
                                                                                             (1 +
                                                                                              BuyingFeeInPercentage +
                                                                                              AverageTradingChangeRatio /
                                                                                              2 *
                                                                                              (IsPublicUpTrending
                                                                                                  ? 1
                                                                                                  : -1)) +
                                                                                             BuyingFeeInAmount));

        public Task<decimal> GetPurchasePriceInPrincipleAsync() => Task.FromResult(Math.Floor(ProposedPurchasePrice *
                                                                                              (1 -
                                                                                               BuyingFeeInPercentage +
                                                                                               AverageTradingChangeRatio /
                                                                                               2 *
                                                                                               (IsPublicUpTrending
                                                                                                   ? 1
                                                                                                   : -1)) +
                                                                                              BuyingFeeInAmount));


        public async Task<AccountBalance> GetAccountBalanceAsync()
        {
            var nonce = GetNonce();
            AccountBalance = (await Rest.PostAsync<CexAccountBalance>("balance/", new
            {
                key = ApiConfig.ApiKey,
                signature = GetApiSignature(nonce),
                nonce
            }))?.ToAccountBalance();
            ApiRequestCounts++;
            return AccountBalance;
        }

        private async Task DrawDecisionUIsAsync()
        {
            var sellingPriceInPrinciple = await GetSellingPriceInPrincipleAsync();
            var buyingPriceInPrinciple = await GetPurchasePriceInPrincipleAsync();


            var exchangeCurrencyBalance =
                AccountBalance?.CurrencyBalances?.Where(item => item.Key == OperatingExchangeCurrency)
                    .Select(item => item.Value).FirstOrDefault();
            var targetCurrencyBalance = AccountBalance?.CurrencyBalances
                ?.Where(item => item.Key == OperatingTargetCurrency)
                .Select(item => item.Value).FirstOrDefault();

            bool buyingAmountAvailable = true,
                sellingAmountAvailable = true,
                buyingReserveRequirementMatched = true,
                sellingReserveRequirementMatched = true,
                finalPortfolioValueDecreasedWhenBuying,
                finalPortfolioValueDecreasedWhenSelling;
            decimal buyingAmountInPrinciple, sellingAmountInPrinciple;
            if (InitialBatchCycles > 0)
            {
                buyingAmountInPrinciple =
                    (TradingStrategy.OrderCapPercentageOnInit * targetCurrencyBalance?.Total)
                    .GetValueOrDefault() / buyingPriceInPrinciple;
                sellingAmountInPrinciple = (TradingStrategy.OrderCapPercentageOnInit *
                                            exchangeCurrencyBalance?.Total).GetValueOrDefault();
                buyingAmountInPrinciple = buyingAmountInPrinciple >
                                          InitialBuyingCap / buyingPriceInPrinciple
                    ? InitialBuyingCap
                    : buyingAmountInPrinciple;
                sellingAmountInPrinciple = sellingAmountInPrinciple > InitialSellingCap
                    ? InitialSellingCap
                    : sellingAmountInPrinciple;
            }
            else
            {
                buyingAmountInPrinciple =
                    (TradingStrategy.OrderCapPercentageAfterInit * targetCurrencyBalance?.Total)
                    .GetValueOrDefault() / buyingPriceInPrinciple;
                sellingAmountInPrinciple = (TradingStrategy.OrderCapPercentageAfterInit *
                                            exchangeCurrencyBalance?.Total).GetValueOrDefault();
            }

            var exchangeCurrencyLimit = ExchangeCurrencyLimit?.MinimumExchangeAmount > 0
                ? ExchangeCurrencyLimit.MinimumExchangeAmount
                : 0;
            var targetCurrencyLimit = TargetCurrencyLimit?.MinimumExchangeAmount > 0
                ? TargetCurrencyLimit.MinimumExchangeAmount
                : 0;

            if (exchangeCurrencyLimit > buyingAmountInPrinciple)
                buyingAmountInPrinciple = exchangeCurrencyLimit.GetValueOrDefault();
            if (exchangeCurrencyLimit > sellingAmountInPrinciple)
                sellingAmountInPrinciple = exchangeCurrencyLimit.GetValueOrDefault();

            buyingAmountAvailable = buyingAmountInPrinciple > 0 &&
                                    buyingAmountInPrinciple * buyingPriceInPrinciple <=
                                    targetCurrencyBalance?.Available;
            sellingAmountAvailable = sellingAmountInPrinciple > 0 &&
                                     sellingAmountInPrinciple <= exchangeCurrencyBalance?.Available;

            buyingAmountInPrinciple =
                buyingAmountAvailable || (targetCurrencyBalance?.Available).GetValueOrDefault() <= 0
                    ? buyingAmountInPrinciple
                    : (targetCurrencyBalance?.Available / buyingPriceInPrinciple).GetValueOrDefault();
            sellingAmountInPrinciple =
                sellingAmountAvailable || (exchangeCurrencyBalance?.Available).GetValueOrDefault() <= 0
                    ? sellingAmountInPrinciple
                    : (exchangeCurrencyBalance?.Available).GetValueOrDefault();


            buyingReserveRequirementMatched = targetCurrencyBalance?.Total <= 0 ||
                                              (targetCurrencyBalance?.Total > buyingAmountInPrinciple &&
                                               (targetCurrencyBalance.Available -
                                                buyingAmountInPrinciple * buyingPriceInPrinciple) /
                                               (targetCurrencyBalance.Total -
                                                buyingAmountInPrinciple * buyingPriceInPrinciple) >=
                                               TradingStrategy.MinimumReservePercentageAfterInit);

            sellingReserveRequirementMatched = exchangeCurrencyBalance?.Total <= 0 ||
                                               (exchangeCurrencyBalance?.Total > sellingAmountInPrinciple &&
                                                (exchangeCurrencyBalance.Available - sellingAmountInPrinciple) /
                                                (exchangeCurrencyBalance.Total - sellingAmountInPrinciple) >=
                                                TradingStrategy.MinimumReservePercentageAfterInit);

            while (!buyingReserveRequirementMatched && buyingAmountInPrinciple > exchangeCurrencyLimit)
            {
                buyingAmountInPrinciple = buyingAmountInPrinciple * 0.9m;

                buyingAmountAvailable = buyingAmountInPrinciple > 0 &&
                                        buyingAmountInPrinciple * buyingPriceInPrinciple <=
                                        targetCurrencyBalance?.Available &&
                                        buyingAmountInPrinciple >= exchangeCurrencyLimit
                                        && buyingAmountInPrinciple * buyingPriceInPrinciple >= targetCurrencyLimit;
                var calculatedTotalTargetCurrencyAmount = targetCurrencyBalance?.Available +
                                                          (exchangeCurrencyBalance?.Total -
                                                           exchangeCurrencyBalance?.Available) * buyingPriceInPrinciple;
                buyingReserveRequirementMatched = calculatedTotalTargetCurrencyAmount <= 0 ||
                                                  (1 - buyingAmountInPrinciple * buyingPriceInPrinciple /
                                                   calculatedTotalTargetCurrencyAmount) >=
                                                  TradingStrategy.MinimumReservePercentageAfterInit;

                if (Math.Round(buyingAmountInPrinciple, 8) ==
                    Math.Round((targetCurrencyBalance?.Available).GetValueOrDefault(), 8))
                    break;
            }

            while (!sellingReserveRequirementMatched && sellingAmountInPrinciple > targetCurrencyLimit)
            {
                sellingAmountInPrinciple = sellingAmountInPrinciple * 0.9m;
                sellingAmountAvailable = sellingAmountInPrinciple > 0 &&
                                         sellingAmountInPrinciple <= exchangeCurrencyBalance?.Available &&
                                         sellingAmountInPrinciple >= exchangeCurrencyLimit &&
                                         sellingAmountInPrinciple * sellingPriceInPrinciple >= targetCurrencyLimit;
                var calculatedTotalExchangeCurrencyAmount =
                (exchangeCurrencyBalance?.Available +
                 (targetCurrencyBalance?.Total - targetCurrencyBalance?.Available) / sellingPriceInPrinciple);

                sellingReserveRequirementMatched = calculatedTotalExchangeCurrencyAmount <= 0 ||
                                                   (1 - sellingAmountInPrinciple /
                                                    calculatedTotalExchangeCurrencyAmount) >=
                                                   TradingStrategy.MinimumReservePercentageAfterInit;
                if (Math.Round(sellingAmountInPrinciple, 8) ==
                    Math.Round((exchangeCurrencyBalance?.Available).GetValueOrDefault(), 8))
                {
                    break;
                }
            }

            var finalPortfolioValueWhenBuying =
                Math.Round(
                    ((exchangeCurrencyBalance?.Total + buyingAmountInPrinciple +
                      targetCurrencyBalance?.Total / buyingPriceInPrinciple) * PublicLastSellPrice)
                    .GetValueOrDefault(), 2);
            var originalPortfolioValueWhenBuying =
                Math.Round(
                    (exchangeCurrencyBalance?.Total * PublicLastSellPrice + targetCurrencyBalance?.Total)
                    .GetValueOrDefault(), 2);
            var finalPortfolioValueWhenSelling =
                Math.Round(
                    (decimal) (exchangeCurrencyBalance?.Total * sellingPriceInPrinciple +
                               targetCurrencyBalance?.Total),
                    2);
            var originalPortfolioValueWhenSelling =
                Math.Round(
                    (exchangeCurrencyBalance?.Total * PublicLastPurchasePrice + targetCurrencyBalance?.Total)
                    .GetValueOrDefault(), 2);

            finalPortfolioValueDecreasedWhenBuying = finalPortfolioValueWhenBuying < originalPortfolioValueWhenBuying;
            finalPortfolioValueDecreasedWhenSelling =
                finalPortfolioValueWhenSelling < originalPortfolioValueWhenSelling;

            Console.WriteLine("");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Blue;
            //Console.BackgroundColor = ConsoleColor.White;
            Console.WriteLine("\n\t_____________________________________________________________________");
            Console.WriteLine("\n\t                         Account Balance                            ");
            Console.WriteLine("\t                       +++++++++++++++++++                          ");
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine(
                $"\n\t {exchangeCurrencyBalance?.Currency}: {exchangeCurrencyBalance?.Available}{(exchangeCurrencyBalance?.InOrders > 0 ? " \t\t\t" + exchangeCurrencyBalance?.InOrders + "\tIn Orders" : "")}" +
                $"\n\t {targetCurrencyBalance?.Currency}: {Math.Round((targetCurrencyBalance?.Available).GetValueOrDefault(), 2)}{(targetCurrencyBalance?.InOrders > 0 ? " \t\t\t" + Math.Round((targetCurrencyBalance?.InOrders).GetValueOrDefault(), 2) + "\tIn Orders" : "")}\t\t\t\t");
            Console.WriteLine($"\n\t Execution Time: {DateTime.Now}");
            Console.ForegroundColor = ConsoleColor.Blue;

            Console.WriteLine("\n\t===================Buy / Sell Price Recommendation===================\n");
            Console.WriteLine($"\t Buying\t\t\t\t\t  Selling  \t\t\t\t");
            Console.WriteLine($"\t ========\t\t\t\t  ========\t\t\t\t");
            Console.WriteLine($"\t CEX Latest:\t{PublicLastPurchasePrice}\t\t\t  {PublicLastSellPrice}\t\t\t\t");
            Console.WriteLine($"\t Your Latest:\t{AccountLastPurchasePrice}\t\t\t  {AccountLastSellPrice}\t\t\t\t");
            var nextBuyOrder = AccountOpenOrders?.Where(item => item.Type == OrderType.Buy)
                .OrderByDescending(item => item.Price)
                .FirstOrDefault();
            var nextSellOrder = AccountOpenOrders?.Where(item => item.Type == OrderType.Sell)
                .OrderBy(item => item.Price)
                .FirstOrDefault();
            Console.WriteLine(
                $"\t Next Order:\t{(nextBuyOrder == null ? "N/A" : nextBuyOrder.Amount.ToString(CultureInfo.InvariantCulture) + nextBuyOrder.ExchangeCurrency)}{(nextBuyOrder != null ? "@" + nextBuyOrder.Price : "")}\t\t  " +
                $"{(nextSellOrder == null ? "N/A  " : nextSellOrder.Amount + nextSellOrder.ExchangeCurrency)}{(nextSellOrder != null ? "@" + nextSellOrder.Price : "")}");
            Console.WriteLine("\n\t_____________________________________________________________________\n");
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine("\n\t Buying Decision: \t\t\t  Selling Decision:");

            Console.WriteLine(
                $"\t Price:\t{buyingPriceInPrinciple} {targetCurrencyBalance?.Currency}\t\t\t  {sellingPriceInPrinciple} {targetCurrencyBalance?.Currency}\t\t\t\t");
            Console.Write($"\t ");

            #region Buying Decision

            Console.ForegroundColor = ConsoleColor.White;
            if (buyingAmountAvailable && buyingReserveRequirementMatched)
            {
                if (!finalPortfolioValueDecreasedWhenBuying)
                {
                    Console.BackgroundColor = ConsoleColor.DarkGreen;
                    Console.Write(
                        $"BUY {buyingAmountInPrinciple} {exchangeCurrencyBalance?.Currency} ({buyingAmountInPrinciple * buyingPriceInPrinciple:N2} {targetCurrencyBalance?.Currency})");
                    Console.ResetColor();
                    Console.Write("\t\t  ");
                }
                else
                {
                    Console.BackgroundColor = ConsoleColor.DarkRed;
                    Console.Write("Depreciation  ");
                    Console.ResetColor();
                    Console.Write("\t\t  ");
                }
            }
            else
            {
                Console.BackgroundColor = ConsoleColor.DarkRed;
                Console.Write(
                    $"{(!buyingReserveRequirementMatched ? $"Limited Reserve - {TradingStrategy.MinimumReservePercentageAfterInit:P2}" : buyingAmountInPrinciple > 0 ? $"Low Fund - Need {(buyingAmountInPrinciple > targetCurrencyLimit ? buyingAmountInPrinciple : targetCurrencyLimit) * buyingPriceInPrinciple:N2} {targetCurrencyBalance.Currency}" : "Low Fund")}");
                Console.ResetColor();
                Console.Write("\t\t  ");
            }

            #endregion

            #region Selling Decision

            Console.ForegroundColor = ConsoleColor.White;
            if (sellingAmountAvailable && sellingReserveRequirementMatched)
            {
                if (!finalPortfolioValueDecreasedWhenSelling)
                {
                    Console.BackgroundColor = ConsoleColor.DarkGreen;
                    Console.Write(
                        $"SELL {sellingAmountInPrinciple} {exchangeCurrencyBalance?.Currency} ({Math.Round(sellingAmountInPrinciple * sellingPriceInPrinciple, 2)} {targetCurrencyBalance?.Currency})");
                    Console.ResetColor();
                    Console.Write("\n");
                }
                else
                {
                    Console.BackgroundColor = ConsoleColor.DarkRed;
                    Console.Write("Depreciation");
                    Console.ResetColor();
                    Console.Write("\t\t\n");
                }
            }
            else
            {
                Console.BackgroundColor = ConsoleColor.DarkRed;
                Console.Write(
                    $"{(!sellingReserveRequirementMatched ? $"Limited Reserve - {TradingStrategy.MinimumReservePercentageAfterInit:P2}" : sellingAmountInPrinciple > 0 ? $"Low Fund - Need {(sellingAmountInPrinciple > exchangeCurrencyLimit ? sellingAmountInPrinciple : exchangeCurrencyLimit):N4} {exchangeCurrencyBalance.Currency}" : "Low Fund")}");
                Console.ResetColor();
                Console.Write("\t\t\n");
            }

            #endregion

            Console.ResetColor();
            Console.WriteLine("\n\n\t Portfolio Estimates (A.I.):");
            Console.WriteLine(
                $"\t Current:\t{originalPortfolioValueWhenBuying} {targetCurrencyBalance?.Currency}\t\t  {originalPortfolioValueWhenSelling} {targetCurrencyBalance?.Currency}\t\t\t\t");
            Console.WriteLine(
                $"\t After  :\t{finalPortfolioValueWhenBuying} {targetCurrencyBalance?.Currency}\t\t  {finalPortfolioValueWhenSelling} {targetCurrencyBalance?.Currency}\t\t\t\t");
            Console.WriteLine(
                $"\t Difference:\t{finalPortfolioValueWhenBuying - originalPortfolioValueWhenBuying} {targetCurrencyBalance?.Currency}\t\t  {finalPortfolioValueWhenSelling - originalPortfolioValueWhenSelling} {targetCurrencyBalance?.Currency} ");
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine(
                $"\n\t Stop Line:\t{TradingStrategy.StopLine} {targetCurrencyBalance.Currency}\t\t  ");

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\n\t===============================****==================================\n");
            Console.ResetColor();
            Console.WriteLine("");
            if (buyingAmountAvailable && buyingReserveRequirementMatched && !finalPortfolioValueDecreasedWhenBuying &&
                finalPortfolioValueWhenBuying >= TradingStrategy.StopLine)
            {
                var immediateExecute = false;
                var skip = true;
                if (!AutoExecution)
                {
                    Console.WriteLine(
                        $"Do you want to execute this buy order? (BUY {buyingAmountInPrinciple} {exchangeCurrencyBalance?.Currency} at {buyingPriceInPrinciple} {targetCurrencyBalance?.Currency})");
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.ResetColor();
                    Console.WriteLine(
                        $"Press [Y] to continue execution, otherwise Press [S] or [N] to skip.");

                    try
                    {
                        var lineText = Console.ReadLine();
                        if (lineText?.ToLower() == "y")
                            immediateExecute = true;
                        else if (lineText?.ToLower() == "s" || lineText?.ToLower() == "n")
                        {
                            skip = true;
                        }

                        while (!immediateExecute &&
                               (lineText.IsNullOrEmpty() || lineText.IsNotNullOrEmpty() && !skip))
                        {
                            Console.WriteLine(
                                $"Press [Y] to continue execution, otherwise Press [S] or [N] to skip.");

                            var read = Console.ReadLine();
                            if (read?.ToLower() == "y")
                            {
                                immediateExecute = true;
                                break;
                            }

                            if (read?.ToLower() != "s" && read?.ToLower() != "n") continue;

                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }


                if (AutoExecution)
                {
                    immediateExecute = true;
                    Console.WriteLine("Auto execution triggered.");
                }
                else
                {
                    Console.WriteLine("Skipped. Refreshing...");
                }


                if (immediateExecute)
                {
                    //execute buy order
                    var nonce = GetNonce();
                    var order = await Rest.PostAsync<ShortOrder>(
                        $"place_order/{OperatingExchangeCurrency}/{OperatingTargetCurrency}", new
                        {
                            signature = GetApiSignature(nonce),
                            key = ApiConfig.ApiKey,
                            nonce,
                            type = "buy",
                            amount = buyingAmountInPrinciple,
                            price = buyingPriceInPrinciple
                        });
                    ApiRequestCounts++;
                    if (order?.OrderId?.IsNotNullOrEmpty() == true)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkGreen;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine(
                            $" [BUY] Order {order.OrderId} Executed: {order.Amount} {OperatingExchangeCurrency} at {order.Price} per {OperatingExchangeCurrency}");
                        Console.ResetColor();
                        SendWebhookMessage(
                            $" :smile: *[BUY]* Order {order.OrderId} \n" +
                            $" *Executed:* {order.Amount} {OperatingExchangeCurrency} \n" +
                            $" *Price:* {order.Price} {OperatingTargetCurrency}\n" +
                            $" *Cost:* {order.Amount * order.Price} {OperatingTargetCurrency}\n" +
                            $" *Estimated Current Value:* {originalPortfolioValueWhenBuying} {OperatingTargetCurrency} \n" +
                            $" *Estimated Target Value:* {finalPortfolioValueWhenBuying} {OperatingTargetCurrency} \n");
                        Thread.Sleep(1000);
                        ApiRequestcrruedAllowance++;
                    }
                }
            }

            if (sellingAmountAvailable && sellingReserveRequirementMatched &&
                !finalPortfolioValueDecreasedWhenSelling &&
                finalPortfolioValueWhenSelling >= TradingStrategy.StopLine)
            {
                var immediateExecute = false;
                var skip = true;
                if (!AutoExecution)
                {
                    Console.WriteLine(
                        $"Do you want to execute this sell order? (SELL {buyingAmountInPrinciple} {exchangeCurrencyBalance?.Currency} at {buyingPriceInPrinciple} {targetCurrencyBalance?.Currency})");
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.ResetColor();
                    Console.WriteLine(
                        $"Press [Y] to continue execution, otherwise Press [S] or [N] to skip.");

                    try
                    {
                        var lineText = Console.ReadLine();
                        if (lineText?.ToLower() == "y")
                            immediateExecute = true;
                        else if (lineText?.ToLower() == "s" || lineText?.ToLower() == "n")
                        {
                            skip = true;
                        }

                        while (!immediateExecute &&
                               (lineText.IsNullOrEmpty() || lineText.IsNotNullOrEmpty() && !skip))
                        {
                            Console.WriteLine(
                                $"Press [Y] to continue execution, otherwise Press [S] or [N] to skip.");

                            var read = Console.ReadLine();
                            if (read?.ToLower() == "y")
                            {
                                immediateExecute = true;
                                break;
                            }

                            if (read?.ToLower() != "s" && read?.ToLower() != "n") continue;

                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }

                if (AutoExecution)
                {
                    immediateExecute = true;
                    Console.WriteLine("Auto execution triggered.");
                }
                else
                {
                    Console.WriteLine("Skipped. Refreshing...");
                }


                if (immediateExecute)
                {
                    //execute buy order
                    var nonce = GetNonce();
                    var order = await Rest.PostAsync<ShortOrder>(
                        $"place_order/{OperatingExchangeCurrency}/{OperatingTargetCurrency}", new
                        {
                            signature = GetApiSignature(nonce),
                            key = ApiConfig.ApiKey,
                            nonce,
                            type = "sell",
                            amount = sellingAmountInPrinciple,
                            price = sellingPriceInPrinciple
                        });
                    ApiRequestCounts++;
                    if (order?.OrderId?.IsNotNullOrEmpty() == true)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkGreen;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine(
                            $" [SELL] Order {order.OrderId} Executed: {order.Amount} {OperatingExchangeCurrency} at {order.Price} per {OperatingExchangeCurrency}");
                        Console.ResetColor();
                        SendWebhookMessage(
                            $" :moneybag: *[SELL]* Order {order.OrderId} Executed: {order.Amount} {OperatingExchangeCurrency} at {order.Price} per {OperatingExchangeCurrency}");
                        SendWebhookMessage(
                            $" :moneybag: *[SELL]* Order {order.OrderId} \n" +
                            $" *Executed:* {order.Amount} {OperatingExchangeCurrency} \n" +
                            $" *Price:* {order.Price} {OperatingTargetCurrency}\n" +
                            $" *Cost:* {order.Amount * order.Price} {OperatingTargetCurrency}\n" +
                            $" *Estimated Current Value:* {originalPortfolioValueWhenSelling} {OperatingTargetCurrency} \n" +
                            $" *Estimated Target Value:* {finalPortfolioValueWhenSelling} {OperatingTargetCurrency} \n");
                        Thread.Sleep(1000);
                        ApiRequestcrruedAllowance++;
                    }
                }
            }
        }


        public async Task<List<IOrder>> GetOpenOrdersAsync()
        {
            var nonce = GetNonce();
            var orders = await Rest.PostAsync<List<ShortOrder>>(
                $"open_orders/{OperatingExchangeCurrency}/{OperatingTargetCurrency}", new
                {
                    signature = GetApiSignature(nonce),
                    key = ApiConfig.ApiKey,
                    nonce
                });
            ApiRequestCounts++;
            AccountOpenOrders = orders?.Select(item => item as IOrder).ToList() ?? new List<IOrder>();
            return AccountOpenOrders;
        }

        public void SendWebhookMessage(string message)
        {
            try
            {
                if (ApiConfig.SlackWebhook.IsNotNullOrEmpty() && message.IsNotNullOrEmpty())
                {
                    new Rest(ApiConfig.SlackWebhook).PostAsync<string>("", new
                    {
                        text = message,
                        username =
                        $"MLEADER's CEX.IO Trading Bot - {OperatingExchangeCurrency}/{OperatingTargetCurrency} "
                    }).Wait();
                }
            }
            catch (Exception ex)
            {
                Rest.LogDebug(ex.Message, ex);
            }
        }

        private bool HasAvailableAmountToPurchase(decimal buyingAmount, AccountBalanceItem balanceItem)
        {
            return false;
        }

        #region Private Members

        private Rest Rest { get; }

        private long GetNonce()
        {
            return Convert.ToInt64(Math.Truncate((DateTime.UtcNow - DateTime.MinValue).TotalMilliseconds));
        }

        private string GetApiSignature(long nonce)
        {
            // Validation first
            if (string.IsNullOrEmpty(ApiConfig?.ApiKey))
            {
                throw new ArgumentException("Parameter apiUsername is not set.");
            }

            if (string.IsNullOrEmpty(ApiConfig.ApiKey))
            {
                throw new ArgumentException("Parameter apiKey is not set");
            }

            if (string.IsNullOrEmpty(ApiConfig.ApiSecret))
            {
                throw new ArgumentException("Parameter apiSecret is not set");
            }

            // HMAC input is nonce + username + key
            var hashInput = string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}", nonce, ApiConfig.ApiUsername,
                ApiConfig.ApiKey);
            var hashInputBytes = Encoding.UTF8.GetBytes(hashInput);

            var secretBytes = Encoding.UTF8.GetBytes(ApiConfig.ApiSecret);
            var hmac = new HMACSHA256(secretBytes);
            var signatureBytes = hmac.ComputeHash(hashInputBytes);
            var signature = BitConverter.ToString(signatureBytes).ToUpper().Replace("-", string.Empty);
            return signature;
        }

        #endregion


        #region Calculation of Key Factors

        #region Staging Calculations

        private decimal PublicUpLevelSell1 =>
            Math.Abs(PublicWeightedAverageBestSellPrice - PublicWeightedAverageSellPrice) /
            PublicWeightedAverageSellPrice;

        private decimal PublicUpLevelSell2 =>
            Math.Abs(PublicWeightedAverageLowSellPrice - PublicWeightedAverageSellPrice) /
            PublicWeightedAverageSellPrice;

        private decimal PublicUpLevelPurchase1 =>
            Math.Abs(PublicWeightedAverageBestPurchasePrice - PublicWeightedAveragePurchasePrice) /
            PublicWeightedAveragePurchasePrice;

        private decimal PublicUpLevelPurchase2 =>
            Math.Abs(PublicWeightedAverageLowPurchasePrice - PublicWeightedAveragePurchasePrice) /
            PublicWeightedAveragePurchasePrice;

        #endregion

        /// <summary>
        /// Is market price going up: buying amount > selling amount
        /// </summary>
        private bool IsPublicUpTrending => LatestPublicPurchaseHistory?.Sum(item => item.Amount) >
                                           LatestPublicSaleHistory?.Sum(item => item.Amount);

        /// <summary>
        /// Find the last X records of public sale prices and do a weighted average
        /// </summary>
        private decimal PublicWeightedAverageSellPrice
        {
            get
            {
                if (!(LatestPublicSaleHistory?.Count > 0)) return 0;
                var totalAmount = LatestPublicSaleHistory.Sum(item => item.Amount);
                return totalAmount > 0
                    ? LatestPublicSaleHistory.Sum(item => item.Amount * item.Price) / totalAmount
                    : 0;
            }
        }

        /// <summary>
        /// Find the best weighted average selling price of the 1/3 best sellig prices
        /// </summary>
        /// <returns></returns>
        private decimal PublicWeightedAverageBestSellPrice
        {
            get
            {
                var bestFirstThirdPrices = LatestPublicSaleHistory?.OrderByDescending(item => item.Price)
                    .Take(LatestPublicSaleHistory.Count / 3);
                var totalAmount = (bestFirstThirdPrices?.Sum(item => item.Amount)).GetValueOrDefault();
                return totalAmount > 0
                    ? bestFirstThirdPrices.Sum(item => item.Amount * item.Price) / totalAmount
                    : 0;
            }
        }

        /// <summary>
        /// Find the poorest weighted average selling price of the 1/3 low selling prices
        /// </summary>
        /// <returns></returns>
        private decimal PublicWeightedAverageLowSellPrice
        {
            get
            {
                var bestLastThirdPrices = LatestPublicSaleHistory?.OrderBy(item => item.Price)
                    .Take(LatestPublicSaleHistory.Count / 3);
                var totalAmount = (bestLastThirdPrices?.Sum(item => item.Amount)).GetValueOrDefault();
                return totalAmount > 0
                    ? bestLastThirdPrices.Sum(item => item.Amount * item.Price) / totalAmount
                    : 0;
            }
        }

        /// <summary>
        /// Find the last public sale price
        /// </summary>
        /// <returns></returns>
        private decimal PublicLastSellPrice => (LatestPublicSaleHistory?.OrderByDescending(item => item.Timestamp)
            ?.Select(item => item.Price)?.FirstOrDefault()).GetValueOrDefault();

        private decimal AccountLastSellPrice => (LatestAccountSaleHistory?.OrderByDescending(item => item.Timestamp)
            ?.Select(item => item.Price)?.FirstOrDefault()).GetValueOrDefault();

        /// <summary>
        /// Find the last X records of public purchase prices and do a weighted average
        /// </summary>
        /// <returns></returns>
        private decimal PublicWeightedAveragePurchasePrice
        {
            get
            {
                var totalAmount = (LatestPublicPurchaseHistory?.Sum(item => item.Amount)).GetValueOrDefault();
                return totalAmount > 0
                    ? LatestPublicPurchaseHistory.Sum(item =>
                          item.Amount * item.Price) / totalAmount
                    : 0;
            }
        }

        /// <summary>
        /// Find the best weighted average purchase price of the 1/3 lowest purchase prices
        /// </summary>
        /// <returns></returns>
        private decimal PublicWeightedAverageBestPurchasePrice
        {
            get
            {
                var bestFirstThirdPrices = LatestPublicPurchaseHistory?.OrderByDescending(item => item.Price)
                    .Take(LatestPublicPurchaseHistory.Count / 3);
                var totalAmount = (bestFirstThirdPrices?.Sum(item => item.Amount)).GetValueOrDefault();
                return totalAmount > 0
                    ? bestFirstThirdPrices.Sum(item => item.Amount * item.Price) / totalAmount
                    : 0;
            }
        }

        /// <summary>
        /// Find the poorest weighted average purchase price of the 1/3 highest purchase prices
        /// </summary>
        /// <returns></returns>
        private decimal PublicWeightedAverageLowPurchasePrice
        {
            get
            {
                var bestLastThirdPrices = LatestPublicPurchaseHistory?.OrderBy(item => item.Price)
                    .Take(LatestPublicPurchaseHistory.Count / 3);
                var totalAmount = (bestLastThirdPrices?.Sum(item => item.Amount)).GetValueOrDefault();
                return totalAmount > 0
                    ? bestLastThirdPrices.Sum(item => item.Amount * item.Price) / totalAmount
                    : 0;
            }
        }

        /// <summary>
        /// Find the last public purchase price
        /// </summary>
        /// <returns></returns>
        private decimal PublicLastPurchasePrice => (LatestPublicPurchaseHistory
            ?.OrderByDescending(item => item.Timestamp)
            ?.Select(item => item.Price)?.FirstOrDefault()).GetValueOrDefault();

        public decimal AccountLastPurchasePrice => (LatestAccountPurchaseHistory
            ?.OrderByDescending(item => item.Timestamp)
            ?.Select(item => item.Price)?.FirstOrDefault()).GetValueOrDefault();

        /// <summary>
        /// Find the last X record of account purchase prices and do a weighted average (i.e. should sell higher than this)
        /// </summary>
        /// <returns></returns>
        private decimal AccountWeightedAveragePurchasePrice
        {
            get
            {
                var totalAmount = (LatestAccountPurchaseHistory?.Sum(item => item.Amount)).GetValueOrDefault();
                return totalAmount > 0
                    ? LatestAccountPurchaseHistory.Sum(item =>
                          item.Amount * item.Price) / totalAmount
                    : 0;
            }
        }

        /// <summary>
        /// Find the last Y records of account sale prices and do a weighted average
        /// </summary>
        /// <returns></returns>
        private decimal AccountWeightedAverageSellPrice
        {
            get
            {
                var totalAmount = (LatestAccountSaleHistory?.Sum(item => item.Amount)).GetValueOrDefault();
                return totalAmount > 0
                    ? LatestAccountSaleHistory.Sum(item =>
                          item.Amount * item.Price) / totalAmount
                    : 0;
            }
        }

        /// <summary>
        /// My trading fee calculated in percentage
        /// </summary>
        /// <returns></returns>
        private decimal BuyingFeeInPercentage { get; set; }

        private decimal SellingFeeInPercentage { get; set; }

        /// <summary>
        /// My trading fee calculated in fixed amount
        /// </summary>
        /// <returns></returns>
        private decimal BuyingFeeInAmount { get; set; }

        private decimal SellingFeeInAmount { get; set; }

        /// <summary>
        /// The avarage market trading change ratio based on both buying/selling's high/low
        /// [AverageTradingChangeRatio] = AVG([PublicUpLevelSell1] , [PublicUpLevelSell2], [PublicUpLevelPurchase1], [PublicUpLevelPurchase2])
        /// </summary>
        /// <returns></returns>
        private decimal AverageTradingChangeRatio => new[]
            {PublicUpLevelSell1, PublicUpLevelSell2, PublicUpLevelPurchase1, PublicUpLevelPurchase2}.Average();

        /// <summary>
        /// [ProposedSellingPrice] = MAX(AVG([PublicWeightedAverageSellPrice],[PublicLastSellPrice], [AccountWeightedAveragePurchasePrice], [AccountWeightedAverageSellPrice]),[PublicLastSellPrice])
        /// </summary>
        /// <returns></returns>
        private decimal ProposedSellingPrice => new[]
        {
            new[]
            {
                PublicWeightedAverageSellPrice,
                PublicLastSellPrice,
                ReasonableAccountWeightedAverageSellPrice,
                ReasonableAccountLastPurchasePrice,
                PublicWeightedAverageBestSellPrice,
                PublicWeightedAverageLowSellPrice,
                ReasonableAccountLastSellPrice,
                (PublicLastSellPrice + ReasonableAccountLastSellPrice) / 2,
                (ReasonableAccountLastSellPrice + ReasonableAccountLastPurchasePrice) / 2,
                ReasonableAccountLastSellPrice * (1 + AverageTradingChangeRatio * (IsPublicUpTrending ? 1 : -1))
            }.Average(),
            PublicLastSellPrice,
            (PublicLastSellPrice + ReasonableAccountLastSellPrice) / 2,
            (ReasonableAccountLastPurchasePrice + ReasonableAccountLastSellPrice) / 2
        }.Max();

        /// <summary>
        /// [ProposedPurchasePrice] = MIN(AVG([PublicWeightedAveragePurchasePrice],[PublicLastPurchasePrice], [PublicWeightedAverageBestPurchasePrice], [AccountWeightedAverageSellPrice]), [PublicLastPurchasePrice])
        /// </summary>
        /// <returns></returns>
        private decimal ProposedPurchasePrice => new[]
        {
            new[]
            {
                PublicWeightedAveragePurchasePrice,
                PublicLastPurchasePrice,
                ReasonableAccountWeightedAveragePurchasePrice,
                ReasonableAccountLastPurchasePrice,
                PublicWeightedAverageBestPurchasePrice,
                (PublicLastPurchasePrice + ReasonableAccountLastPurchasePrice) / 2,
                (ReasonableAccountLastSellPrice + ReasonableAccountLastPurchasePrice) / 2,
                ReasonableAccountLastPurchasePrice * (1 + AverageTradingChangeRatio * (IsPublicUpTrending ? 1 : -1))
            }.Average(),
            PublicLastPurchasePrice,
            (PublicLastPurchasePrice + ReasonableAccountLastPurchasePrice) / 2,
            (ReasonableAccountLastSellPrice + ReasonableAccountLastPurchasePrice) / 2
        }.Min();

        private decimal ReasonableAccountLastPurchasePrice =>
            Math.Abs(AccountLastPurchasePrice - PublicLastPurchasePrice) /
            Math.Min(PublicLastPurchasePrice,
                AccountLastPurchasePrice > 0 ? AccountLastPurchasePrice : PublicLastPurchasePrice) >
            TradingStrategy.MarketChangeSensitivityRatio
                ? PublicLastPurchasePrice
                : AccountLastPurchasePrice;

        private decimal ReasonableAccountLastSellPrice =>
            Math.Abs(AccountLastSellPrice - PublicLastSellPrice) / Math.Min(PublicLastSellPrice,
                AccountLastSellPrice > 0 ? AccountLastSellPrice : PublicLastSellPrice) >
            TradingStrategy.MarketChangeSensitivityRatio
                ? PublicLastSellPrice
                : AccountLastSellPrice;

        private decimal ReasonableAccountWeightedAverageSellPrice =>
            Math.Abs(AccountWeightedAverageSellPrice - PublicLastSellPrice) /
            Math.Min(AccountWeightedAverageSellPrice > 0 ? AccountWeightedAverageSellPrice : PublicLastSellPrice,
                PublicLastSellPrice) >
            TradingStrategy.MarketChangeSensitivityRatio
                ? PublicLastSellPrice
                : AccountWeightedAverageSellPrice;

        private decimal ReasonableAccountWeightedAveragePurchasePrice =>
            Math.Abs(AccountWeightedAveragePurchasePrice - PublicLastPurchasePrice) /
            Math.Min(PublicLastPurchasePrice,
                AccountWeightedAveragePurchasePrice > 0
                    ? AccountWeightedAveragePurchasePrice
                    : PublicLastPurchasePrice) >
            TradingStrategy.MarketChangeSensitivityRatio
                ? PublicLastPurchasePrice
                : AccountWeightedAveragePurchasePrice;

        #endregion

        /// Automated AI logics:
        /// 1. Identify how much amount can be spent for the next order
        /// 2. Identify how much commission/fee (percentage) will be charged for the next order
        /// 3. Identify the correct amount to be spent for the next order (using historical order)
        /// 4. If Reserve amount after order is lower than the minimum reserve amount calculated based on percentage then drop the order, otherwise execute the order
        /// Price decision making logic:
        /// 1. fetch X number of historical orders to check their prices
        /// 2. setting the decision factors:
        ///         2.1  [PublicWeightedAverageSellPrice] Find the last X records of public sale prices and do a weighted average
        ///         2.2  [PublicWeightedAverageBestSellPrice] Find the best weighted average selling price of the 1/3 best sellig prices
        ///         2.3  [PublicWeightedAverageLowSellPrice]  Find the poorest weighted average selling price of the 1/3 low selling prices
        ///         2.4  [PublicLastSellPrice] Find the last public sale price
        ///         2.5  [PublicWeightedAveragePurchasePrice] Find the last X records of public purchase prices and do a weighted average
        ///         2.6  [PublicWeightedAverageBestPurchasePrice] Find the best weighted average purchase price of the 1/3 lowest purchase prices
        ///         2.7  [PublicWeightedAverageLowPurchasePrice] Find the poorest weighted average purchase price of the 1/3 highest purchase prices
        ///         2.8  [PublicLastPurchasePrice] Find the last public purchase price
        ///         2.9  [AccountWeightedAveragePurchasePrice] Find the last X record of account purchase prices and do a weighted average (i.e. should sell higher than this)
        ///         2.10 [AccountWeightedAverageSellPrice] Find the last Y records of account sale prices and do a weighted average
        ///         2.11 [BuyingFeeInPercentage] My trading buying fee calculated in percentage
        ///         2.12 [SellingFeeInPercentage] My selling fee calculated in percentage
        ///         2.13 [BuyingFeeInAmount] My buying fee calculated in fixed amount
        ///         2.14 [SellingFeeInAmount] My selling fee calculated in fixed amount
        /// 
        ///         LOGIC, Decide if the market is trending price up
        ///         [PublicUpLevelSell1] = ABS([PublicWeightedAverageBestSellPrice] - [PublicWeightedAverageSellPrice]) / [PublicWeightedAverageSellPrice]
        ///         [PublicUpLevelSell2] = ABS([PublicWeightedAverageLowSellPrice] - [PublicWeightedAverageSellPrice]) / [PublicWeightedAverageSellPrice]
        ///         [PublicUpLevelPurchase1] = ABS([PublicWeightedAverageBestPurchasePrice] - [PublicWeightedAveragePurchasePrice]) / [PublicWeightedAveragePurchasePrice]
        ///         [PublicUpLevelPurchase2] = ABS([PublicWeightedAverageLowPurchasePrice] - [PublicWeightedAveragePurchasePrice]) / [PublicWeightedAveragePurchasePrice] 
        ///        
        ///         [IsPublicUp] = [PublicUpLevelPurchase1] >= [PublicUpLevelSell1] && [PublicUpLevelPurchase2] <= [PublicUpLevelPurchase2]
        ///         [AverageTradingChangeRatio] = AVG([PublicUpLevelSell1] , [PublicUpLevelSell2], [PublicUpLevelPurchase1], [PublicUpLevelPurchase2])
        /// 
        /// 
        /// 3. when selling:
        ///         3.1 [ProposedSellingPrice] = MAX(AVG([PublicWeightedAverageSellPrice],[PublicLastSellPrice], [AccountWeightedAveragePurchasePrice], [AccountWeightedAverageSellPrice]), [PublicLastSellPrice])
        ///         3.2 [SellingPriceInPrinciple] = [ProposedSellingPrice] * (1+ [SellingFeeInPercentage] + [AverageTradingChangeRatio] * ([IsPublicUp]? 1: -1)) + [SellingFeeInAmount]
        /// 
        /// 4. when buying:
        ///         4.1 [ProposedPurchasePrice] = MIN(AVG([PublicWeightedAveragePurchasePrice],[PublicLastPurchasePrice], [PublicWeightedAverageBestPurchasePrice], [AccountWeightedAverageSellPrice]), [PublicLastPurchasePrice])
        ///         4.2 [PurchasePriceInPrinciple] = [ProposedPurchasePrice] * (1 - [BuyingFeeInPercentage] + [AverageTradingChangeRatio] * ([IsPublicUp] ? 1: -1)) + [BuyingFeeInAmount]
        /// Final Decision:
        /// 5. If final portfolio value is descreasing, do not buy/sell

        #endregion
    }
}