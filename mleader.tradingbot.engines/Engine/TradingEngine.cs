using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using mleader.tradingbot.Data;
using mleader.tradingbot.Data.Cex;
using mleader.tradingbot.engines.Data;
using OElite;

namespace mleader.tradingbot.Engine
{
    public class TradingEngine : ITradingEngine
    {
        public IApi Api { get; set; }
        public string ReserveCurrency { get; set; }
        public Dictionary<string, decimal> MinimumCurrencyOrderAmount { get; set; }

        public List<ITradeHistory> LatestPublicSaleHistory { get; set; }
        public List<ITradeHistory> LatestPublicPurchaseHistory { get; set; }
        public List<IOrder> LatestAccountSaleHistory { get; set; }
        public List<IOrder> LatestAccountPurchaseHistory { get; set; }
        public List<IOrder> AccountOpenOrders { get; set; }

        public IOrder AccountNextBuyOpenOrder => AccountOpenOrders?.Where(item => item.Type == OrderType.Buy)
            .OrderByDescending(item => item.Price)
            .FirstOrDefault();

        public IOrder AccountNextSellOpenOrder => AccountOpenOrders?.Where(item => item.Type == OrderType.Sell)
            .OrderBy(item => item.Price).FirstOrDefault();

        public IOrder AccountLastBuyOpenOrder => AccountOpenOrders?.Where(item => item.Type == OrderType.Buy)
            .OrderBy(item => item.Price).FirstOrDefault();

        public IOrder AccountLastSellOpenOrder => AccountOpenOrders?.Where(item => item.Type == OrderType.Sell)
            .OrderByDescending(item => item.Price).FirstOrDefault();


        public ITradingStrategy TradingStrategy => Api?.TradingStrategy;

        private Orderbook CurrentOrderbook { get; set; }
        private string OperatingExchangeCurrency { get; }
        private string OperatingTargetCurrency { get; }
        private List<CurrencyLimit> CurrencyLimits { get; set; }
        private CurrencyLimit ExchangeCurrencyLimit { get; set; }
        private CurrencyLimit TargetCurrencyLimit { get; set; }
        private decimal InitialBuyingCapInTargetCurrency { get; set; }
        private decimal InitialSellingCapInExchangeCurrency { get; set; }
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
        public DateTime LastTimeBuyOrderCancellation { get; set; }
        public DateTime LastTimeSellOrderCancellation { get; set; }
        public DateTime LastTimeBuyOrderExecution { get; set; }
        public DateTime LastTimeSellOrderExecution { get; set; }
        public DateTime LastCaculationTime { get; set; }
        public DateTime TradingSessionInHours { get; set; }
        public decimal TradingValueBleedRatio { get; set; }


        public decimal TradingStartBalanceInExchangeCurrency { get; set; }
        public decimal TradingStartBalanceInTargetCurrency { get; set; }
        public decimal TradingStartValueInExchangeCurrency { get; set; }
        public decimal TradingStartValueInTargetCurrency { get; set; }
        public DateTime TradingStartTime { get; set; }


        public TradingEngine(IApi api, string exchangeCurrency, string targetCurrency)
        {
            OperatingExchangeCurrency = exchangeCurrency;
            OperatingTargetCurrency = targetCurrency;
            Api = api;

            AutoExecution = TradingStrategy.AutoDecisionExecution;

            Api.Rest.LogInfo("Init Trading Engine");
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
            await RefreshCurrencyLimitsAsync();
            if (!await InitBaseDataAsync())
            {
                Console.WriteLine("Init Data Failed. Program Terminated.");
                return;
            }

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

            InitialBuyingCapInTargetCurrency = PublicLastPurchasePrice > 0
                ? (totalTargetCurrencyBalance + totalExchangeCurrencyBalance * PublicLastPurchasePrice) *
                  TradingStrategy.OrderCapPercentageOnInit
                : 0;
            InitialSellingCapInExchangeCurrency = PublicLastSellPrice > 0
                ? (totalExchangeCurrencyBalance +
                   (PublicLastSellPrice > 0 ? totalTargetCurrencyBalance / PublicLastSellPrice : 0)) *
                  TradingStrategy.OrderCapPercentageOnInit
                : 0;

            if (ExchangeCurrencyLimit?.MaximumExchangeAmount * PublicLastPurchasePrice <
                InitialBuyingCapInTargetCurrency)
                InitialBuyingCapInTargetCurrency = (ExchangeCurrencyLimit.MaximumExchangeAmount == null
                                                       ? decimal.MaxValue
                                                       : ExchangeCurrencyLimit.MaximumExchangeAmount.GetValueOrDefault()
                                                   ) * PublicLastPurchasePrice;

            if (ExchangeCurrencyLimit?.MinimumExchangeAmount * PublicLastPurchasePrice >=
                InitialBuyingCapInTargetCurrency)
                InitialBuyingCapInTargetCurrency = (ExchangeCurrencyLimit.MinimumExchangeAmount == null
                                                       ? 0
                                                       : ExchangeCurrencyLimit.MinimumExchangeAmount.GetValueOrDefault()
                                                   ) * PublicLastPurchasePrice;

            if (TargetCurrencyLimit?.MaximumExchangeAmount < InitialSellingCapInExchangeCurrency)
                InitialSellingCapInExchangeCurrency = PublicLastSellPrice > 0
                    ? (TargetCurrencyLimit.MaximumExchangeAmount == null
                        ? decimal.MaxValue
                        : (PublicLastSellPrice > 0
                            ? TargetCurrencyLimit.MaximumExchangeAmount
                                  .GetValueOrDefault() / PublicLastSellPrice
                            : 0))
                    : 0;
            if (TargetCurrencyLimit?.MinimumExchangeAmount >= InitialSellingCapInExchangeCurrency * PublicLastSellPrice)
                InitialSellingCapInExchangeCurrency = PublicLastSellPrice > 0
                    ? (TargetCurrencyLimit.MinimumExchangeAmount == null
                        ? 0
                        : (PublicLastSellPrice > 0
                            ? TargetCurrencyLimit.MinimumExchangeAmount
                                  .GetValueOrDefault() / PublicLastSellPrice
                            : 0))
                    : 0;

            if (InitialBuyingCapInTargetCurrency <= 0)
                InitialBuyingCapInTargetCurrency = PublicLastPurchasePrice > 0
                    ? totalTargetCurrencyBalance / PublicLastPurchasePrice
                    : 0;
            if (InitialSellingCapInExchangeCurrency <= 0)
                InitialSellingCapInExchangeCurrency = totalExchangeCurrencyBalance;


            InitialBatchCycles = (int) (TradingStrategy.OrderCapPercentageOnInit > 0
                ? 1 / TradingStrategy.OrderCapPercentageOnInit
                : 0);


            TradingStartBalanceInExchangeCurrency = ExchangeCurrencyBalance.Total;
            TradingStartBalanceInTargetCurrency = TargetCurrencyBalance.Total;

            TradingStartValueInExchangeCurrency = PublicLastSellPrice > 0
                ? ExchangeCurrencyBalance.Total +
                  TargetCurrencyBalance.Total / PublicLastSellPrice
                : 0;
            TradingStartValueInTargetCurrency =
                TargetCurrencyBalance.Total + ExchangeCurrencyBalance.Total *
                PublicLastSellPrice;
            TradingStartTime = DateTime.Now;
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

                LastCaculationTime = DateTime.Now;

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
            var error = !(await InitBaseDataAsync());
            try
            {
                await DrawDecisionUIsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Thread.Sleep(1000);
                Console.Clear();
            }
            finally
            {
                if (InitialBatchCycles > 0)
                    InitialBatchCycles--;
            }

            return !error;
        }

        private async Task<bool> InitBaseDataAsync()
        {
            #region Get Historical Trade Histories

            var tradeHistory = await Api.GetHistoricalTradeHistoryAsync(
                OperatingExchangeCurrency,
                OperatingTargetCurrency, DateTime.UtcNow.AddMinutes(-1 * (
                                                                        TradingStrategy
                                                                            .MinutesOfPublicHistoryOrderForPurchaseDecision >
                                                                        TradingStrategy
                                                                            .MinutesOfPublicHistoryOrderForSellDecision
                                                                            ? TradingStrategy
                                                                                .MinutesOfPublicHistoryOrderForPurchaseDecision
                                                                            : TradingStrategy
                                                                                .MinutesOfPublicHistoryOrderForSellDecision
                                                                    )));
            var error = false;
            if (tradeHistory?.Count > 0)
            {
                LatestPublicPurchaseHistory = tradeHistory
                    .Where(item => item.OrderType == OrderType.Buy && item.Timestamp >=
                                   DateTime.UtcNow.AddMinutes(
                                       -1 * TradingStrategy.MinutesOfPublicHistoryOrderForPurchaseDecision))
                    .Select(item => item).ToList();
                LatestPublicSaleHistory = tradeHistory.Where(item =>
                        item.OrderType == OrderType.Sell && item.Timestamp >=
                        DateTime.UtcNow.AddMinutes(-1 * TradingStrategy.MinutesOfPublicHistoryOrderForSellDecision))
                    .Select(item => item).ToList();
            }
            else
            {
                LatestPublicPurchaseHistory = new List<ITradeHistory>();
                LatestPublicSaleHistory = new List<ITradeHistory>();
                error = true;
            }

            if (error) return !error;

            #endregion


            #region Get Orderbook

            await RefreshPublicOrderbookAsync();

            #endregion


            #region Get Account Trade Histories

            if (!await RefreshAccountTradeHistory()) return false;

            #endregion

            #region Get Account Trading Fees

            await RefreshAccountFeesAsync();

            #endregion

            #region Get Account Open Orders

            AccountOpenOrders = await GetOpenOrdersAsync();
            if (AccountOpenOrders == null)
            {
                Console.WriteLine("\n [Unable to receive account open orders] - Carrying 3 seconds sleep...");

                Thread.Sleep(3000);
                ApiRequestcrruedAllowance = 0;
                ApiRequestCounts = 0;
                return false;
            }

            #endregion

            #region Get Account Balancees

            AccountBalance = await GetAccountBalanceAsync();
            if (AccountBalance == null)
            {
                Console.WriteLine("\n [Unable to receive account balance] - Carrying 3 seconds sleep...");

                Thread.Sleep(3000);
                ApiRequestcrruedAllowance = 0;
                ApiRequestCounts = 0;
                return false;
            }

            #endregion


            return !error;
        }

        private async Task<bool> RefreshAccountTradeHistory()
        {
            bool error;
            var latestAccountTradeHistories = await Api.GetAccountTradeHistoryAsync(OperatingExchangeCurrency,
                OperatingTargetCurrency);

            if (latestAccountTradeHistories == null)
            {
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
            return true;
        }

        private async Task RefreshPublicOrderbookAsync()
        {
            CurrentOrderbook = await Api.GetPublicOrderbookAsync(OperatingExchangeCurrency, OperatingTargetCurrency);
            ApiRequestCounts++;

            if (CurrentOrderbook == null)
            {
                Console.WriteLine("\n [Unable to receive public orderbook] - Carrying 3 seconds sleep...");
                Thread.Sleep(3000);
                ApiRequestcrruedAllowance = 0;
                ApiRequestCounts = 0;
            }
        }

        private async Task RefreshCurrencyLimitsAsync()
        {
            CurrencyLimits = await Api.GetCurrencyLimitsAsync();
        }

        private async Task RefreshAccountFeesAsync()
        {
            var fees = await Api.GetAccountFeesAsync(OperatingExchangeCurrency, OperatingTargetCurrency);

            SellingFeeInPercentage = fees.SellingFeeInPercentage;
            SellingFeeInAmount = fees.SellingFeeInAmount;
            BuyingFeeInPercentage = fees.BuyingFeeInPercentage;
            BuyingFeeInAmount = 0;
        }

        public Task<decimal> GetSellingPriceInPrincipleAsync() => Task.FromResult(Math.Ceiling(ProposedSellingPrice /
                                                                                               (1 -
                                                                                                BuyingFeeInPercentage -
                                                                                                (IsBullMarket &&
                                                                                                 IsBullMarketContinuable
                                                                                                    ? TradingStrategy
                                                                                                        .MarketChangeSensitivityRatio
                                                                                                    : 0)
                                                                                               ) +
                                                                                               BuyingFeeInAmount));

        public Task<decimal> GetPurchasePriceInPrincipleAsync() => Task.FromResult(Math.Floor(ProposedPurchasePrice *
                                                                                              (1 -
                                                                                               BuyingFeeInPercentage -
                                                                                               (!IsBullMarket &&
                                                                                                IsBearMarketContinuable
                                                                                                   ? TradingStrategy
                                                                                                       .MarketChangeSensitivityRatio
                                                                                                   : 0)
                                                                                              ) -
                                                                                              BuyingFeeInAmount));


        public async Task<AccountBalance> GetAccountBalanceAsync()
        {
            AccountBalance = await Api.GetAccountBalanceAsync();
            return AccountBalance;
        }

        public AccountBalanceItem ExchangeCurrencyBalance => AccountBalance?.CurrencyBalances?.Where(
                                                                     item => item.Key == OperatingExchangeCurrency)
                                                                 .Select(item =>
                                                                 {
//                                                                     if (item.Value != null)
//                                                                     {
//                                                                         item.Value.InOrders = Math.Min(
//                                                                             (AccountOpenOrders
//                                                                                 ?.Where(i => i.Type == OrderType.Sell)
//                                                                                 .Sum(i => i.Amount))
//                                                                             .GetValueOrDefault(), item.Value.InOrders);
//                                                                     }
//
                                                                     return item.Value;
                                                                 }).FirstOrDefault() ??
                                                             new CexAccountBalanceItem(OperatingExchangeCurrency)
                                                             {
                                                                 Available = 0,
                                                                 InOrders = 0
                                                             };

        public AccountBalanceItem TargetCurrencyBalance => AccountBalance?.CurrencyBalances
                                                               ?.Where(item => item.Key == OperatingTargetCurrency)
                                                               .Select(item =>
                                                               {
//                                                                   if (item.Value == null) return item.Value;
//                                                                   var newInOrder = (AccountOpenOrders
//                                                                       ?.Where(i =>
//                                                                           i.Type == OrderType.Buy)
//                                                                       .Sum(i =>
//                                                                           i.Amount * i.Price)).GetValueOrDefault();
//                                                                   item.Value.InOrders = Math.Min(newInOrder,
//                                                                       item.Value.InOrders);
//
                                                                   return item.Value;
                                                               }).FirstOrDefault() ??
                                                           new CexAccountBalanceItem(OperatingTargetCurrency)
                                                           {
                                                               Available = 0,
                                                               InOrders = 0
                                                           };


        private async Task DrawDecisionUIsAsync()
        {
            #region Validate and Calculate Selling/Buying Prices and Trade Amount

            var sellingPriceInPrinciple = await GetSellingPriceInPrincipleAsync();
            var buyingPriceInPrinciple = await GetPurchasePriceInPrincipleAsync();


            var isBullMarket = IsBullMarket;
            var isBullMarketContinuable = IsBullMarketContinuable;
            var isBearMarketContinuable = IsBearMarketContinuable;
            var betterHoldBuying = false;
            var betterHoldSelling = false;
            var buyingHigherThanSelling = false;
            var sellingLowerThanBuying = false;


            bool buyingAmountAvailable = true,
                sellingAmountAvailable = true,
                finalPortfolioValueDecreasedWhenBuying,
                finalPortfolioValueDecreasedWhenSelling;
            decimal buyingAmountInPrinciple, sellingAmountInPrinciple;
            if (InitialBatchCycles > 0)
            {
                buyingAmountInPrinciple = TradingStrategy.OrderCapPercentageOnInit *
                                          GetPortfolioValueInExchangeCurrency(
                                              ExchangeCurrencyBalance.Available + (PublicLastPurchasePrice > 0
                                                  ? TargetCurrencyBalance.InOrders /
                                                    PublicLastPurchasePrice
                                                  : 0),
                                              TargetCurrencyBalance.Available + ExchangeCurrencyBalance.InOrders *
                                              PublicLastSellPrice, buyingPriceInPrinciple) *
                                          (1 - BuyingFeeInPercentage) - BuyingFeeInAmount;
                sellingAmountInPrinciple = TradingStrategy.OrderCapPercentageOnInit *
                                           GetPortfolioValueInExchangeCurrency(
                                               ExchangeCurrencyBalance.Available + (PublicLastPurchasePrice > 0
                                                   ? TargetCurrencyBalance.InOrders /
                                                     PublicLastPurchasePrice
                                                   : 0),
                                               TargetCurrencyBalance.Available + ExchangeCurrencyBalance.InOrders *
                                               PublicLastSellPrice, sellingPriceInPrinciple) *
                                           (1 - SellingFeeInPercentage) - SellingFeeInAmount;

                buyingAmountInPrinciple = buyingAmountInPrinciple >
                                          (PublicLastPurchasePrice > 0
                                              ? InitialBuyingCapInTargetCurrency / PublicLastPurchasePrice
                                              : 0)
                    ? (PublicLastPurchasePrice > 0 ? InitialBuyingCapInTargetCurrency / PublicLastPurchasePrice : 0)
                    : buyingAmountInPrinciple;
                sellingAmountInPrinciple = sellingAmountInPrinciple > InitialSellingCapInExchangeCurrency
                    ? InitialSellingCapInExchangeCurrency
                    : sellingAmountInPrinciple;
            }
            else
            {
                buyingAmountInPrinciple =
                    TradingStrategy.OrderCapPercentageAfterInit *
                    GetCurrentPortfolioEstimatedTargetValue(buyingPriceInPrinciple) / buyingPriceInPrinciple *
                    (1 - BuyingFeeInPercentage) - BuyingFeeInAmount;

                sellingAmountInPrinciple = TradingStrategy.OrderCapPercentageAfterInit *
                                           GetCurrentPortfolioEstimatedExchangeValue(sellingPriceInPrinciple) *
                                           (1 - SellingFeeInPercentage) - SellingFeeInAmount;
            }

            if (isBullMarket && isBullMarketContinuable)
            {
                buyingAmountInPrinciple =
                    buyingAmountInPrinciple * (1 - TradingStrategy.MarketChangeSensitivityRatio);
                sellingAmountInPrinciple =
                    sellingAmountInPrinciple * (1 + TradingStrategy.MarketChangeSensitivityRatio);
            }

            if (!isBullMarket && isBearMarketContinuable)
            {
                buyingAmountInPrinciple =
                    buyingAmountInPrinciple * (1 + TradingStrategy.MarketChangeSensitivityRatio);
                sellingAmountInPrinciple =
                    sellingAmountInPrinciple * (1 - TradingStrategy.MarketChangeSensitivityRatio);
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
                                    TargetCurrencyBalance?.Available;
            sellingAmountAvailable = sellingAmountInPrinciple > 0 &&
                                     sellingAmountInPrinciple <= ExchangeCurrencyBalance?.Available;

            buyingAmountInPrinciple =
                buyingAmountAvailable || (TargetCurrencyBalance?.Available).GetValueOrDefault() <= 0
                    ? buyingAmountInPrinciple
                    : ((PublicLastPurchasePrice > 0 ? TargetCurrencyBalance?.Available / PublicLastPurchasePrice : 0))
                      .GetValueOrDefault() *
                      (1 - BuyingFeeInPercentage) - BuyingFeeInAmount;
            sellingAmountInPrinciple =
                sellingAmountAvailable || (ExchangeCurrencyBalance?.Available).GetValueOrDefault() <= 0
                    ? sellingAmountInPrinciple
                    : (ExchangeCurrencyBalance?.Available).GetValueOrDefault() *
                      (1 - SellingFeeInPercentage) - SellingFeeInAmount;


            var finalPortfolioValueWhenBuying =
                Math.Round(
                    (ExchangeCurrencyBalance.Available + buyingAmountInPrinciple) * buyingPriceInPrinciple +
                    (AccountOpenOrders?.Where(item => item.Type == OrderType.Buy).Sum(item => item.Amount))
                    .GetValueOrDefault() * PublicLastSellPrice
                    + (AccountOpenOrders?.Where(item => item.Type == OrderType.Sell)
                        .Sum(item => item.Amount * item.Price)).GetValueOrDefault() +
                    TargetCurrencyBalance.Available, 2);
            var originalPortfolioValueWhenBuying =
                Math.Round(GetCurrentPortfolioEstimatedTargetValue(PublicLastSellPrice), 2);

            var finalPortfolioValueWhenSelling =
                Math.Round(
                    (ExchangeCurrencyBalance.Available * sellingPriceInPrinciple + TargetCurrencyBalance.Available +
                     AccountOpenOrders?.Sum(item =>
                         item.Type == OrderType.Buy ? item.Amount * sellingPriceInPrinciple : item.Amount * item.Price))
                    .GetValueOrDefault(),
                    2);
            var originalPortfolioValueWhenSelling =
                Math.Round(GetCurrentPortfolioEstimatedTargetValue(PublicLastPurchasePrice), 2);

            finalPortfolioValueDecreasedWhenBuying = finalPortfolioValueWhenBuying < originalPortfolioValueWhenBuying;
            finalPortfolioValueDecreasedWhenSelling =
                finalPortfolioValueWhenSelling < originalPortfolioValueWhenSelling;

            if (!IsBuyingReserveRequirementMatched(buyingAmountInPrinciple, buyingPriceInPrinciple))
            {
                //find how much we can buy]
                var maxAmount =
                    GetMaximumBuyableAmountBasedOnReserveRatio(buyingPriceInPrinciple);
                if (maxAmount < buyingAmountInPrinciple)
                    buyingAmountInPrinciple = maxAmount;
            }

            if (!IsSellingReserveRequirementMatched(sellingAmountInPrinciple, sellingPriceInPrinciple))
            {
                var maxAmount =
                    GetMaximumSellableAmountBasedOnReserveRatio(sellingPriceInPrinciple);
                if (maxAmount < sellingAmountInPrinciple)
                    sellingAmountInPrinciple = maxAmount;
            }

            buyingAmountInPrinciple = Math.Truncate(buyingAmountInPrinciple * 100000000) / 100000000;
            sellingAmountInPrinciple = Math.Truncate(sellingAmountInPrinciple * 100000000) / 100000000;

            buyingAmountAvailable = buyingAmountInPrinciple > 0 &&
                                    buyingAmountInPrinciple * buyingPriceInPrinciple <=
                                    TargetCurrencyBalance?.Available &&
                                    buyingAmountInPrinciple >= exchangeCurrencyLimit
                                    && buyingAmountInPrinciple * buyingPriceInPrinciple >= targetCurrencyLimit;

            sellingAmountAvailable = sellingAmountInPrinciple > 0 &&
                                     sellingAmountInPrinciple <= ExchangeCurrencyBalance?.Available &&
                                     sellingAmountInPrinciple >= exchangeCurrencyLimit &&
                                     sellingAmountInPrinciple * sellingPriceInPrinciple >= targetCurrencyLimit;


//            var IsBullMarketContinuable =
//                PublicWeightedAverageBestSellPrice * (1 + AverageTradingChangeRatio) > sellingPriceInPrinciple
//                ||
//                PublicWeightedAverageBestSellPrice > PublicWeightedAverageBestPurchasePrice
//                ||
//                Math.Abs(PublicWeightedAverageBestSellPrice - PublicWeightedAverageBestSellPrice) /
//                PublicWeightedAverageBestSellPrice > TradingStrategy.MarketChangeSensitivityRatio ||
//                Math.Abs(PublicWeightedAverageLowPurchasePrice * (1 + AverageTradingChangeRatio) -
//                         PublicLastPurchasePrice) /
//                PublicLastPurchasePrice > TradingStrategy.MarketChangeSensitivityRatio;
//            var IsBearMarketContinuable =
//                Math.Abs(PublicWeightedAverageBestPurchasePrice * (1 - AverageTradingChangeRatio) -
//                         PublicLastPurchasePrice) /
//                PublicLastPurchasePrice > TradingStrategy.MarketChangeSensitivityRatio ||
//                Math.Abs(PublicWeightedAverageLowSellPrice * (1 - AverageTradingChangeRatio) - PublicLastSellPrice) /
//                PublicLastSellPrice > TradingStrategy.MarketChangeSensitivityRatio;

            #endregion

            if (finalPortfolioValueWhenBuying <= 0 && finalPortfolioValueWhenSelling <= 0)
            {
                Console.WriteLine("Insufficient data for analysis. Skip...");
                Thread.Sleep(1000);
                return;
            }

            #region Draw the Graph

            Console.WriteLine("");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Blue;
            //Console.BackgroundColor = ConsoleColor.White;
            Console.WriteLine("\n\t_____________________________________________________________________");
            Console.WriteLine("\n\t                         Account Balance                            ");
            Console.WriteLine("\t                       +++++++++++++++++++                          ");
            Console.WriteLine($"\t                              {Api.ExchangeName}");
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine(
                $"\n\t {OperatingExchangeCurrency}: {ExchangeCurrencyBalance?.Available}{(ExchangeCurrencyBalance?.InOrders > 0 ? " \t\t\t" + ExchangeCurrencyBalance?.InOrders + " In Orders" : "")}" +
                $"\n\t {OperatingTargetCurrency}: {Math.Round((TargetCurrencyBalance?.Available).GetValueOrDefault(), 2)}{(TargetCurrencyBalance?.InOrders > 0 ? " \t\t\t" + Math.Round((TargetCurrencyBalance?.InOrders).GetValueOrDefault(), 2) + " In Orders" : "")}\t\t\t\t");
            Console.WriteLine($"\n\t Start Time: {TradingStartTime} \t\n\t Current Time: {DateTime.Now}\n\n");
            Console.ForegroundColor = ConsoleColor.Blue;

            Console.WriteLine("\n\t===================Buy / Sell Price Recommendation===================\n");
            Console.WriteLine($"\t Buying\t\t\t\t\t  Selling  \t\t\t\t");
            Console.WriteLine($"\t ========\t\t\t\t  ========\t\t\t\t");
            Console.WriteLine(
                $"\t {Api.ExchangeName} Latest:\t{PublicLastPurchasePrice}\t\t\t  {PublicLastSellPrice}\t\t\t\t");
            Console.WriteLine($"\t Last Executed:\t{AccountLastPurchasePrice}\t\t\t  {AccountLastSellPrice}\t\t\t\t");
            Console.WriteLine(
                $"\t Next Order:\t{(AccountNextBuyOpenOrder == null ? "N/A" : AccountNextBuyOpenOrder.Amount.ToString(CultureInfo.InvariantCulture) + AccountNextBuyOpenOrder.ExchangeCurrency)}{(AccountNextBuyOpenOrder != null ? "@" + AccountNextBuyOpenOrder.Price : "")}\t\t  " +
                $"{(AccountNextSellOpenOrder == null ? "N/A  " : AccountNextSellOpenOrder.Amount + AccountNextSellOpenOrder.ExchangeCurrency)}{(AccountNextSellOpenOrder != null ? "@" + AccountNextSellOpenOrder.Price : "")}");
            Console.WriteLine(
                $"\t Last Order:\t{(AccountLastBuyOpenOrder == null ? "N/A" : AccountLastBuyOpenOrder.Amount.ToString(CultureInfo.InvariantCulture) + AccountLastBuyOpenOrder.ExchangeCurrency)}{(AccountLastBuyOpenOrder != null ? "@" + AccountLastBuyOpenOrder.Price : "")}\t\t  " +
                $"{(AccountLastSellOpenOrder == null ? "N/A  " : AccountLastSellOpenOrder.Amount + AccountNextSellOpenOrder.ExchangeCurrency)}{(AccountLastSellOpenOrder != null ? "@" + AccountLastSellOpenOrder.Price : "")}");

            Console.Write("\t Market Status:\t ");
            if (isBullMarket)
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.Write("Bull Market \t\t  ");
                if (!isBullMarketContinuable)
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                }

                Console.Write($"{(isBullMarketContinuable ? "Up" : "Down")}\t\t\t\t\n");
                Console.ForegroundColor = ConsoleColor.Blue;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.Write("Bear Market \t\t  ");
                if (!isBearMarketContinuable)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                }

                Console.Write($"{(isBearMarketContinuable ? "Down" : "Up")}\t\t\t\t\n");
                Console.ForegroundColor = ConsoleColor.Blue;
            }

            Console.WriteLine("\n\t_____________________________________________________________________\n");
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine("\n\t Buying Decision: \t\t\t  Selling Decision:");

            Console.WriteLine(
                $"\t Price:\t{buyingPriceInPrinciple} {TargetCurrencyBalance?.Currency}\t\t\t  {sellingPriceInPrinciple} {OperatingTargetCurrency}\t\t\t\t");
            Console.Write($"\t ");

            #region Buying Decision

            Console.ForegroundColor = ConsoleColor.White;
            if (buyingAmountAvailable &&
                IsBuyingReserveRequirementMatched(buyingAmountInPrinciple, buyingPriceInPrinciple))
            {
                if (!finalPortfolioValueDecreasedWhenBuying)
                {
                    if (isBullMarket && isBullMarketContinuable || !isBullMarket && isBearMarketContinuable
                                                                || AccountNextBuyOpenOrder?.Price > 0 &&
                                                                PublicLastPurchasePrice >
                                                                AccountNextBuyOpenOrder.Price *
                                                                (1 + TradingStrategy.MarketChangeSensitivityRatio))
                    {
                        Console.BackgroundColor = ConsoleColor.DarkGray;
                        Console.Write("Better Hold");
                        betterHoldBuying = true;
                    }
                    else
                    {
                        Console.BackgroundColor = ConsoleColor.DarkGreen;
                        Console.Write(
                            $"BUY {buyingAmountInPrinciple} {ExchangeCurrencyBalance?.Currency} ({buyingAmountInPrinciple * buyingPriceInPrinciple:N2} {OperatingTargetCurrency})");
                    }
                }
                else
                {
                    Console.BackgroundColor = ConsoleColor.DarkRed;
                    Console.Write("Depreciation  ");
                }
            }
            else
            {
                Console.BackgroundColor = ConsoleColor.DarkRed;
                Console.Write(
                    $"{(!IsBuyingReserveRequirementMatched(buyingAmountInPrinciple, buyingPriceInPrinciple) || buyingAmountInPrinciple == GetMaximumBuyableAmountBasedOnReserveRatio(buyingPriceInPrinciple) ? $"Limited Reserve - {TargetCurrencyBalance.Available * TradingStrategy.MinimumReservePercentageAfterInitInTargetCurrency:N2} {OperatingTargetCurrency}" : buyingAmountInPrinciple > 0 ? $"Low Fund - Need {(buyingAmountInPrinciple > exchangeCurrencyLimit ? buyingAmountInPrinciple : exchangeCurrencyLimit) * buyingPriceInPrinciple:N2} {OperatingTargetCurrency}" : "Low Fund")}");
            }

            Console.ResetColor();
            Console.Write("\t\t  ");

            #endregion

            #region Selling Decision

            Console.ForegroundColor = ConsoleColor.White;
            if (sellingAmountAvailable &&
                IsSellingReserveRequirementMatched(sellingAmountInPrinciple, sellingPriceInPrinciple))
            {
                if (!finalPortfolioValueDecreasedWhenSelling)
                {
                    if (isBullMarket && isBullMarketContinuable || !isBullMarket && isBearMarketContinuable ||
                        AccountNextSellOpenOrder?.Price > 0 && PublicLastSellPrice < AccountNextSellOpenOrder.Price *
                        (1 - TradingStrategy.MarketChangeSensitivityRatio))
                    {
                        Console.BackgroundColor = ConsoleColor.DarkGray;
                        Console.Write("Better Hold");
                        betterHoldSelling = true;
                    }
                    else
                    {
                        Console.BackgroundColor = ConsoleColor.DarkGreen;
                        Console.Write(
                            $"SELL {sellingAmountInPrinciple} {OperatingExchangeCurrency} ({Math.Round(sellingAmountInPrinciple * sellingPriceInPrinciple, 2)} {OperatingTargetCurrency})");
                    }
                }
                else
                {
                    Console.BackgroundColor = ConsoleColor.DarkRed;
                    Console.Write("Depreciation");
                }
            }
            else
            {
                Console.BackgroundColor = ConsoleColor.DarkRed;
                Console.Write(
                    $"{(!IsSellingReserveRequirementMatched(sellingAmountInPrinciple, sellingPriceInPrinciple) || sellingAmountInPrinciple == GetMaximumSellableAmountBasedOnReserveRatio(sellingPriceInPrinciple) ? $"Limited Reserve - {ExchangeCurrencyBalance.Available * TradingStrategy.MinimumReservePercentageAfterInitInExchangeCurrency:N4} {OperatingExchangeCurrency}" : sellingAmountInPrinciple > 0 ? $"Low Fund - Need {(sellingAmountInPrinciple > exchangeCurrencyLimit ? sellingAmountInPrinciple : exchangeCurrencyLimit):N4} {OperatingExchangeCurrency}" : "Low Fund")}");
            }

            Console.ResetColor();
            Console.Write("\t\t\n");

            #endregion

            #region Drawing Estimates

            Console.ResetColor();
            Console.WriteLine("\n\n\t Portfolio Estimates (A.I.):");
            Console.WriteLine(
                $"\tOriginal Value On Start: {Math.Round(TradingStartValueInExchangeCurrency, 8)} {OperatingExchangeCurrency} ({TradingStartValueInTargetCurrency:N2} {OperatingTargetCurrency})\n");
            Console.WriteLine(
                $"\t Current:   \t\t{originalPortfolioValueWhenBuying:N2} {OperatingTargetCurrency}\t\t  {originalPortfolioValueWhenSelling:N2} {OperatingTargetCurrency}\t\t\t\t");
            Console.WriteLine(
                $"\t            \t\t{GetCurrentPortfolioEstimatedExchangeValue(PublicLastPurchasePrice):N8} {OperatingExchangeCurrency}\t\t  {GetCurrentPortfolioEstimatedExchangeValue(PublicLastSellPrice):N8} {OperatingExchangeCurrency}\t\t\t\t");
            Console.WriteLine(
                $"\t After  :\t\t{finalPortfolioValueWhenBuying:N2} {OperatingTargetCurrency}\t\t  {finalPortfolioValueWhenSelling:N2} {TargetCurrencyBalance?.Currency}\t\t\t\t");
            Console.WriteLine(
                $"\t            \t\t{GetCurrentPortfolioEstimatedExchangeValue(buyingPriceInPrinciple):N8} {OperatingExchangeCurrency}\t\t  {GetCurrentPortfolioEstimatedExchangeValue(sellingPriceInPrinciple):N8} {OperatingExchangeCurrency}\t\t\t\t");
            Console.WriteLine(
                $"\t Order Difference:\t{finalPortfolioValueWhenBuying - originalPortfolioValueWhenBuying:N2} {OperatingTargetCurrency}\t\t  {finalPortfolioValueWhenSelling - originalPortfolioValueWhenSelling:N2} {OperatingTargetCurrency} ");

            Console.Write(
                $"\n\t Profit Current:\t");
            Console.ForegroundColor = originalPortfolioValueWhenBuying - TradingStartValueInTargetCurrency > 0
                ? ConsoleColor.DarkGreen
                : ConsoleColor.DarkRed;
            Console.Write(
                $"{originalPortfolioValueWhenBuying - TradingStartValueInTargetCurrency:N2} {OperatingTargetCurrency}\t\t  ");
            Console.ForegroundColor = originalPortfolioValueWhenSelling - TradingStartValueInTargetCurrency > 0
                ? ConsoleColor.DarkGreen
                : ConsoleColor.DarkRed;

            Console.Write(
                $"{originalPortfolioValueWhenSelling - TradingStartValueInTargetCurrency:N2} {OperatingTargetCurrency}\n");
            Console.ResetColor();
            Console.Write(
                $"\t Profit After:\t\t");
            Console.ForegroundColor = finalPortfolioValueWhenBuying - TradingStartValueInTargetCurrency > 0
                ? ConsoleColor.DarkGreen
                : ConsoleColor.DarkRed;
            Console.Write(
                $"{finalPortfolioValueWhenBuying - TradingStartValueInTargetCurrency:N2} {OperatingTargetCurrency}\t\t  ");
            Console.ForegroundColor = finalPortfolioValueWhenSelling - TradingStartValueInTargetCurrency > 0
                ? ConsoleColor.DarkGreen
                : ConsoleColor.DarkRed;
            Console.Write(
                $"{finalPortfolioValueWhenSelling - TradingStartValueInTargetCurrency:N2} {OperatingTargetCurrency} ");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine(
                $"\n\t Stop Line:\t{TradingStrategy.StopLine} {OperatingTargetCurrency}\t\t  ");

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\n\t===============================****==================================\n");
            Console.ResetColor();
            Console.WriteLine("");

            #endregion

            #endregion

            if (PublicLastPurchasePrice <= 0 || PublicLastSellPrice <= 0)
            {
                Console.Write("Not enough historical buy/sell data. Skip order...");
                Thread.Sleep(1000);
                return;
            }

            #region Cancel orders that have better potentials

            if (isBullMarket && isBullMarketContinuable || !isBullMarket && isBearMarketContinuable)
            {
                var betterHoldBids = AccountOpenOrders?.Where(item =>
                    CurrentOrderbook?.Bids?.Take(10)?.Count(bid => bid[0] >= item.Price) > 0);
                if (betterHoldBids?.Count() > 0)
                {
                    Console.BackgroundColor = ConsoleColor.DarkRed;
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine("Cancelling open BUY orders that may better off if hold");
                    foreach (var invalidatedOrder in betterHoldBids)
                    {
                        Task.Run(async () =>
                        {
                            try
                            {
                                Console.WriteLine(
                                    $"Attempt to cancel BUY order {invalidatedOrder.OrderId} - {invalidatedOrder.Amount}@{invalidatedOrder.Price}");
                                await CancelOrderAsync(invalidatedOrder);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.Message);
                            }
                        }).RunInBackgroundAndForget();
                    }
                }

                var betterHoldAsks = AccountOpenOrders?.Where(item =>
                    CurrentOrderbook?.Asks?.Take(10)?.Count(bid => bid[0] <= item.Price) > 0);
                if (betterHoldAsks?.Count() > 0)
                {
                    Console.BackgroundColor = ConsoleColor.DarkRed;
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine("Cancelling open SELL orders that may better off if hold");
                    foreach (var invalidatedOrder in betterHoldAsks)
                    {
                        Task.Run(async () =>
                        {
                            try
                            {
                                Console.WriteLine(
                                    $"Attempt to cancel SELL order {invalidatedOrder.OrderId} - {invalidatedOrder.Amount}@{invalidatedOrder.Price}");
                                await CancelOrderAsync(invalidatedOrder);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.Message);
                            }
                        }).RunInBackgroundAndForget();
                    }
                }

                Console.ResetColor();
            }

            #endregion

            #endregion

            #region Execute Buy Order

            if (buyingAmountAvailable &&
                IsBuyingReserveRequirementMatched(buyingAmountInPrinciple, buyingPriceInPrinciple) &&
                !finalPortfolioValueDecreasedWhenBuying &&
                finalPortfolioValueWhenBuying >= TradingStrategy.StopLine && !IsBearMarketContinuable)
            {
                if ((buyingPriceInPrinciple > sellingPriceInPrinciple)
                    && !(isBullMarket && isBullMarketContinuable))
                {
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(
                        $"WARNING - Buying price ({buyingPriceInPrinciple}) higher than selling price ({sellingPriceInPrinciple}) - Skip current [BUY] order execution for lower risk.");
//                    SendWebhookMessage(
//                        $":warning:  Buying Higher than selling - BUY: {buyingPriceInPrinciple} / SELL: {sellingPriceInPrinciple} / AVG SELL: {AccountWeightedAverageSellPrice}\n" +
//                        $"Skipped Order Amount In {OperatingExchangeCurrency}: {buyingAmountInPrinciple} {OperatingExchangeCurrency}\n" +
//                        $"Skipped Order Amount In {OperatingTargetCurrency}: {buyingAmountInPrinciple * buyingPriceInPrinciple:N2} {OperatingTargetCurrency}\n" +
//                        $"Skkipped on: {DateTime.Now}");
                    Console.ResetColor();
                    buyingHigherThanSelling = true;
                }
                else if (betterHoldBuying)

                {
                    Console.WriteLine("...HOLD...");

                    //If holding, better cancel previous buying orders that are lower than current price
                    var invalidatedOrders = AccountOpenOrders?.Where(item =>
                        item.Type == OrderType.Buy && item.Price >= buyingPriceInPrinciple &&
                        item.Timestamp.AddHours((double) TradingStrategy.PriceCorrectionFrequencyInHours) <=
                        DateTime.Now);
                    if (invalidatedOrders?.Count() > 0)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkRed;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine("Cancelling open BUY orders that may better off if hold [1]");
                        foreach (var invalidatedOrder in invalidatedOrders)
                        {
                            Task.Run(async () =>
                            {
                                try
                                {
                                    Console.WriteLine(
                                        $"Attempt to cancel BUY order {invalidatedOrder.OrderId} - {invalidatedOrder.Amount}@{invalidatedOrder.Price}");
                                    await CancelOrderAsync(invalidatedOrder);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(ex.Message);
                                }
                            }).RunInBackgroundAndForget();
                        }
                    }


                    Console.ResetColor();
                }
                else if (finalPortfolioValueWhenBuying < TradingStrategy.StopLine)
                {
                    Console.WriteLine("...Unmatched Stop Line...");
                }
                else
                {
                    var immediateExecute = false;
                    var skip = buyingHigherThanSelling || betterHoldBuying ||
                               finalPortfolioValueWhenBuying < TradingStrategy.StopLine;
                    if (!AutoExecution && !skip)
                    {
                        Console.WriteLine(
                            $"Do you want to execute this buy order? (BUY {buyingAmountInPrinciple} {ExchangeCurrencyBalance?.Currency} at {buyingPriceInPrinciple} {TargetCurrencyBalance?.Currency})");
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.ResetColor();
                        Console.WriteLine(
                            $"Press [Y] to continue execution, otherwise Press [S] or [N] to skip.");

                        try
                        {
                            var lineText = Console.ReadLine().Trim('\t');
                            if (lineText?.ToLower() == "y")
                            {
                                immediateExecute = true;
                                skip = false;
                            }
                            else if (lineText?.ToLower() == "s" || lineText?.ToLower() == "n")
                            {
                                immediateExecute = false;
                                skip = true;
                            }

                            while (!immediateExecute &&
                                   (lineText.IsNullOrEmpty() || lineText.IsNotNullOrEmpty() && !skip))
                            {
                                Console.WriteLine(
                                    $"Press [Y] to continue execution, otherwise Press [S] or [N] to skip.");

                                var read = Console.ReadLine().Trim('\t');
                                if (read?.ToLower() == "y")
                                {
                                    immediateExecute = true;
                                    skip = false;
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
                    }

                    if (skip)
                    {
                        Console.WriteLine("Skipped. Refreshing...");
                    }


                    if (immediateExecute & !skip)
                    {
                        var invalidatedOrders = AccountOpenOrders?.Where(item =>
                            item.Type == OrderType.Sell &&
                            item.Price <= buyingPriceInPrinciple &&
                            item.Timestamp.AddHours((double) TradingStrategy.PriceCorrectionFrequencyInHours) <=
                            DateTime.Now);
                        if (invalidatedOrders?.Count() > 0)
                        {
                            Console.BackgroundColor = ConsoleColor.DarkRed;
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine(
                                "Cancelling SELL open orders that are conflicting with current buying order.");
                            foreach (var invalidatedOrder in invalidatedOrders)
                            {
                                Task.Run(async () =>
                                {
                                    try
                                    {
                                        await CancelOrderAsync(invalidatedOrder);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine(ex.Message);
                                    }
                                }).RunInBackgroundAndForget();
                            }

                            Console.ResetColor();
                        }

                        //execute buy order
                        var order = await Api.ExecuteOrderAsync(OrderType.Buy, OperatingExchangeCurrency,
                            OperatingTargetCurrency, buyingAmountInPrinciple, buyingPriceInPrinciple);

                        if (order?.OrderId?.IsNotNullOrEmpty() == true)
                        {
                            Console.BackgroundColor = ConsoleColor.DarkGreen;
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine(
                                $" [BUY] Order {order.OrderId} Executed: {order.Amount} {OperatingExchangeCurrency} at {order.Price} per {OperatingExchangeCurrency}");
                            Console.ResetColor();
                            SendWebhookMessage(
                                $" :smile: *[BUY]* Order {order.OrderId} - {order.Timestamp}\n" +
                                $" *Executed:* {order.Amount} {OperatingExchangeCurrency} \n" +
                                $" *Price:* {order.Price} {OperatingTargetCurrency}\n" +
                                $" *Cost:* {order.Amount * order.Price} {OperatingTargetCurrency}\n" +
                                $" *Current Value in {OperatingTargetCurrency}:* {originalPortfolioValueWhenBuying} {OperatingTargetCurrency} \n" +
                                $" *Target Value in {OperatingTargetCurrency}:* {finalPortfolioValueWhenBuying} {OperatingTargetCurrency} \n" +
                                $" *Current Value in {OperatingExchangeCurrency}:* {(PublicLastSellPrice > 0 ? originalPortfolioValueWhenBuying / PublicLastSellPrice : 0)} {OperatingExchangeCurrency} \n" +
                                $" *Target Value in {OperatingExchangeCurrency}:* {finalPortfolioValueWhenBuying / order.Price} {OperatingExchangeCurrency}"
                            );
                            Thread.Sleep(1000);
                            ApiRequestcrruedAllowance++;
                            LastTimeBuyOrderExecution = DateTime.Now;


                            invalidatedOrders = AccountOpenOrders?.Where(item =>
                                item.Type == OrderType.Buy &&
                                item.Price < buyingPriceInPrinciple *
                                (1 - TradingStrategy.MarketChangeSensitivityRatio) &&
                                item.Timestamp.AddHours((double) TradingStrategy.PriceCorrectionFrequencyInHours) <=
                                DateTime.Now);
                            if (invalidatedOrders?.Count() > 0)
                            {
                                Console.BackgroundColor = ConsoleColor.DarkRed;
                                Console.ForegroundColor = ConsoleColor.White;
                                Console.WriteLine("Cancelling BUY open orders that are unlikely to achieve.");
                                foreach (var invalidatedOrder in invalidatedOrders)
                                {
                                    Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await CancelOrderAsync(invalidatedOrder);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine(ex.Message);
                                        }
                                    }).RunInBackgroundAndForget();
                                }

                                Console.ResetColor();
                            }
                        }
                    }
                }
            }

            #endregion

            #region Execute Sell Order

            if (sellingAmountAvailable &&
                IsSellingReserveRequirementMatched(sellingAmountInPrinciple, sellingPriceInPrinciple) &&
                !finalPortfolioValueDecreasedWhenSelling &&
                finalPortfolioValueWhenSelling >= TradingStrategy.StopLine && !IsBullMarketContinuable)
            {
                if ((buyingPriceInPrinciple > sellingPriceInPrinciple) &&
                    !(!isBullMarket && isBearMarketContinuable))
                {
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(
                        "WARNING - Selling price lower than buying price - Skip current [SELL] order execution for lower risk.");
//                    SendWebhookMessage(
//                        $":warning:  Selling lower than buying - BUY: {buyingPriceInPrinciple} / AVG BUY: {AccountWeightedAveragePurchasePrice} / SELL: {sellingPriceInPrinciple} \n" +
//                        $"Skipped Order Amount In {OperatingExchangeCurrency}: {buyingAmountInPrinciple} {OperatingExchangeCurrency}\n" +
//                        $"Skipped Order Amount In {OperatingTargetCurrency}: {buyingAmountInPrinciple * buyingPriceInPrinciple:N2} {OperatingTargetCurrency}\n" +
//                        $"Skkipped on: {DateTime.Now}");
                    Console.ResetColor();
                }
                else if (betterHoldSelling)
                {
                    Console.WriteLine("...HOLD...");

                    //If holding, better cancel previous selling orders that are lower than current price
                    var invalidatedOrders = AccountOpenOrders?.Where(item =>
                        item.Type == OrderType.Sell && item.Price <= sellingPriceInPrinciple);
                    if (invalidatedOrders?.Count() > 0)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkRed;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine("Cancelling open SELL orders that may better off if hold");
                        foreach (var invalidatedOrder in invalidatedOrders)
                        {
                            Task.Run(async () =>
                            {
                                try
                                {
                                    await CancelOrderAsync(invalidatedOrder);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(ex.Message);
                                }
                            }).RunInBackgroundAndForget();
                        }
                    }

                    Console.ResetColor();
                }
                else if (finalPortfolioValueWhenSelling < TradingStrategy.StopLine)
                {
                    Console.WriteLine("...Unmatched Stop Line...");
                }
                else
                {
                    var immediateExecute = false;
                    var skip = sellingLowerThanBuying || betterHoldSelling ||
                               finalPortfolioValueWhenSelling < TradingStrategy.StopLine;
                    if (!AutoExecution && !skip)
                    {
                        Console.WriteLine(
                            $"Do you want to execute this sell order? (SELL {buyingAmountInPrinciple} {ExchangeCurrencyBalance?.Currency} at {buyingPriceInPrinciple} {TargetCurrencyBalance?.Currency})");
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.ResetColor();
                        Console.WriteLine(
                            $"Press [Y] to continue execution, otherwise Press [S] or [N] to skip.");

                        try
                        {
                            var lineText = Console.ReadLine().Trim('\t');
                            if (lineText?.ToLower() == "y")
                            {
                                immediateExecute = true;
                                skip = false;
                            }
                            else if (lineText?.ToLower() == "s" || lineText?.ToLower() == "n")
                            {
                                immediateExecute = false;
                                skip = true;
                            }

                            while (!immediateExecute &&
                                   (lineText.IsNullOrEmpty() || lineText.IsNotNullOrEmpty() && !skip))
                            {
                                Console.WriteLine(
                                    $"Press [Y] to continue execution, otherwise Press [S] or [N] to skip.");

                                var read = Console.ReadLine().Trim('\t');
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
                    }

                    if (skip)
                    {
                        Console.WriteLine("Skipped. Refreshing...");
                    }


                    if (immediateExecute & !skip)
                    {
                        var invalidatedOrders = AccountOpenOrders?.Where(item =>
                            item.Type == OrderType.Buy &&
                            item.Price >= sellingPriceInPrinciple &&
                            item.Timestamp.AddHours((double) TradingStrategy.PriceCorrectionFrequencyInHours) <=
                            DateTime.Now);
                        if (invalidatedOrders?.Count() > 0)
                        {
                            Console.BackgroundColor = ConsoleColor.DarkRed;
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine(
                                "Cancelling BUY open orders that are conflicting with current selling order.");
                            foreach (var invalidatedOrder in invalidatedOrders)
                            {
                                Task.Run(async () =>
                                {
                                    try
                                    {
                                        await CancelOrderAsync(invalidatedOrder);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine(ex.Message);
                                    }
                                }).RunInBackgroundAndForget();
                            }

                            Console.ResetColor();
                        }


                        //execute buy order
                        var order = await Api.ExecuteOrderAsync(OrderType.Sell, OperatingExchangeCurrency,
                            OperatingTargetCurrency, sellingAmountInPrinciple, sellingPriceInPrinciple);
                        if (order?.OrderId?.IsNotNullOrEmpty() == true)
                        {
                            Console.BackgroundColor = ConsoleColor.DarkGreen;
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine(
                                $" [SELL] Order {order.OrderId} Executed: {order.Amount} {OperatingExchangeCurrency} at {order.Price} per {OperatingExchangeCurrency}");
                            Console.ResetColor();

                            SendWebhookMessage(
                                $" :moneybag: *[SELL]* Order {order.OrderId}  - {order.Timestamp}\n" +
                                $" *Executed:* {order.Amount} {OperatingExchangeCurrency} \n" +
                                $" *Price:* {order.Price} {OperatingTargetCurrency}\n" +
                                $" *Cost:* {order.Amount * order.Price} {OperatingTargetCurrency}\n" +
                                $" *Estimated Current Value:* {originalPortfolioValueWhenSelling} {OperatingTargetCurrency} \n" +
                                $" *Estimated Target Value:* {finalPortfolioValueWhenSelling} {OperatingTargetCurrency} \n" +
                                $" *Current Value in {OperatingTargetCurrency}:* {originalPortfolioValueWhenSelling} {OperatingTargetCurrency} \n" +
                                $" *Target Value in {OperatingTargetCurrency}:* {finalPortfolioValueWhenSelling} {OperatingTargetCurrency} \n" +
                                $" *Current Value in {OperatingExchangeCurrency}:* {(PublicLastSellPrice > 0 ? originalPortfolioValueWhenSelling / PublicLastSellPrice : 0)} {OperatingExchangeCurrency} \n" +
                                $" *Target Value in {OperatingExchangeCurrency}:* {finalPortfolioValueWhenSelling / order.Price} {OperatingExchangeCurrency}"
                            );
                            Thread.Sleep(1000);
                            ApiRequestcrruedAllowance++;
                            LastTimeSellOrderExecution = DateTime.Now;
                            invalidatedOrders = AccountOpenOrders?.Where(item =>
                                item.Type == OrderType.Sell &&
                                item.Price >= sellingPriceInPrinciple *
                                (1 + TradingStrategy.MarketChangeSensitivityRatio) &&
                                item.Timestamp.AddHours((double) TradingStrategy.PriceCorrectionFrequencyInHours) <=
                                DateTime.Now);
                            if (invalidatedOrders?.Count() > 0)
                            {
                                Console.BackgroundColor = ConsoleColor.DarkRed;
                                Console.ForegroundColor = ConsoleColor.White;
                                Console.WriteLine("Cancelling open SELL orders that are unlikely to achieve");
                                foreach (var invalidatedOrder in invalidatedOrders)
                                {
                                    Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await CancelOrderAsync(invalidatedOrder);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine(ex.Message);
                                        }
                                    }).RunInBackgroundAndForget();
                                }

                                Console.ResetColor();
                            }
                        }
                    }
                }
            }

            #endregion

            #region Execute Drop Order

            // execute only when orderbook is available and no trade transaction in the current period
            if (CurrentOrderbook?.BuyTotal > 0 && CurrentOrderbook?.SellTotal > 0)
            {
                //Test whether to drop last buy order when no historical buy transaction in the current period
                if (AccountLastBuyOpenOrder != null && AccountLastPurchasePrice <= 0 &&
                    CurrentOrderbook.BuyTotal <= CurrentOrderbook.SellTotal * PublicLastSellPrice &&
                    LastTimeBuyOrderExecution.AddMinutes(
                        (double) (Math.Max(TradingStrategy.MinutesOfAccountHistoryOrderForPurchaseDecision,
                                      TradingStrategy.MinutesOfPublicHistoryOrderForPurchaseDecision) *
                                  Math.Max(1m + AverageTradingChangeRatio,
                                      1m + TradingStrategy.MarketChangeSensitivityRatio))) < DateTime.Now &&
                    AccountLastBuyOpenOrder.Timestamp.AddHours((double) TradingStrategy
                        .PriceCorrectionFrequencyInHours) <=
                    DateTime.Now)
                {
                    // only do it when changes are significant (i.e. can't easily purchase)
                    if (
                        //PublicWeightedAveragePurchasePrice
                        Math.Abs(AccountLastBuyOpenOrder.Price - PublicWeightedAveragePurchasePrice) /
                        Math.Min(AccountLastBuyOpenOrder.Price, PublicWeightedAveragePurchasePrice) >
                        TradingStrategy.MarketChangeSensitivityRatio
                        &&
                        //PublicWeightedAverageBestPurchasePrice
                        Math.Abs(AccountLastBuyOpenOrder.Price - PublicWeightedAverageBestPurchasePrice) /
                        Math.Min(AccountLastBuyOpenOrder.Price, PublicWeightedAverageBestPurchasePrice) >
                        TradingStrategy.MarketChangeSensitivityRatio
                        &&
                        //PublicWeightedAverageLowPurchasePrice
                        Math.Abs(AccountLastBuyOpenOrder.Price - PublicWeightedAverageLowPurchasePrice) /
                        Math.Min(AccountLastBuyOpenOrder.Price, PublicWeightedAverageLowPurchasePrice) >
                        TradingStrategy.MarketChangeSensitivityRatio
                        &&
                        //PublicLastPurchasePrice
                        Math.Abs(AccountLastBuyOpenOrder.Price - PublicLastPurchasePrice) /
                        Math.Min(AccountLastBuyOpenOrder.Price, PublicLastPurchasePrice) >
                        TradingStrategy.MarketChangeSensitivityRatio
                    )
                    {
                        var priorityBids =
                            CurrentOrderbook.Bids.Where(item => item[0] >= AccountLastBuyOpenOrder.Price);
                        var buyingWeightedAveragePrice =
                            (priorityBids?.Sum(item => item[0] * item[1]) /
                             priorityBids.Sum(item => item[1]))
                            .GetValueOrDefault();
                        // only do it when changes are significant based on future purchase demand
                        if (buyingWeightedAveragePrice > 0 &&
                            Math.Abs(AccountLastBuyOpenOrder.Price - buyingWeightedAveragePrice) /
                            Math.Min(AccountLastBuyOpenOrder.Price, buyingWeightedAveragePrice) >
                            TradingStrategy.MarketChangeSensitivityRatio
                        )
                        {
                            var portfolioValueAfterCancellation =
                                originalPortfolioValueWhenBuying -
                                AccountLastBuyOpenOrder.Price * AccountLastBuyOpenOrder.Amount +
                                PublicLastSellPrice * AccountLastBuyOpenOrder.Amount;
                            if (portfolioValueAfterCancellation > TradingStrategy.StopLine)
                                await CancelOrderAsync(AccountLastBuyOpenOrder);
                        }
                    }
                }

                //Test whether to drop last sell order when no historical sell transaction in the current period
                if (AccountLastSellOpenOrder != null && AccountLastSellPrice <= 0 && CurrentOrderbook.BuyTotal >=
                    CurrentOrderbook.SellTotal * PublicLastPurchasePrice * (1 - AverageTradingChangeRatio) &&
                    LastTimeSellOrderExecution.AddMinutes(
                        (double) (Math.Max(TradingStrategy.MinutesOfAccountHistoryOrderForSellDecision,
                                      TradingStrategy.MinutesOfPublicHistoryOrderForSellDecision) *
                                  Math.Max(1m + AverageTradingChangeRatio,
                                      1m + TradingStrategy.MarketChangeSensitivityRatio))) < DateTime.Now &&
                    AccountLastSellOpenOrder.Timestamp.AddHours(
                        (double) TradingStrategy.PriceCorrectionFrequencyInHours) <=
                    DateTime.Now)
                {
                    // only do it when changes are significant (i.e. can't easily sell)
                    if (
                        //PublicWeightedAveragePurchasePrice
                        Math.Abs(AccountLastSellOpenOrder.Price - PublicWeightedAveragePurchasePrice) /
                        Math.Min(AccountLastSellOpenOrder.Price, PublicWeightedAveragePurchasePrice) >
                        TradingStrategy.MarketChangeSensitivityRatio
                        &&
                        //PublicWeightedAverageBestPurchasePrice
                        Math.Abs(AccountLastSellOpenOrder.Price - PublicWeightedAverageBestPurchasePrice) /
                        Math.Min(AccountLastSellOpenOrder.Price, PublicWeightedAverageBestPurchasePrice) >
                        TradingStrategy.MarketChangeSensitivityRatio
                        &&
                        //PublicWeightedAverageLowPurchasePrice
                        Math.Abs(AccountLastSellOpenOrder.Price - PublicWeightedAverageLowPurchasePrice) /
                        Math.Min(AccountLastSellOpenOrder.Price, PublicWeightedAverageLowPurchasePrice) >
                        TradingStrategy.MarketChangeSensitivityRatio
                        &&
                        //PublicLastPurchasePrice
                        Math.Abs(AccountLastSellOpenOrder.Price - PublicLastPurchasePrice) /
                        Math.Min(AccountLastSellOpenOrder.Price, PublicLastPurchasePrice) >
                        TradingStrategy.MarketChangeSensitivityRatio
                    )
                    {
                        var priorityBids =
                            CurrentOrderbook.Asks.Where(item => item[0] >= AccountLastSellOpenOrder.Price);

                        var sellingWeightedAveragePrice =
                            (priorityBids?.Sum(item => item[0] * item[1]) /
                             priorityBids?.Sum(item => item[1]))
                            .GetValueOrDefault();
                        // only do it when changes are significant based on future purchase demand
                        if (sellingWeightedAveragePrice > 0 &&
                            Math.Abs(AccountLastSellOpenOrder.Price - sellingWeightedAveragePrice) /
                            Math.Min(AccountLastSellOpenOrder.Price, sellingWeightedAveragePrice) >
                            TradingStrategy.MarketChangeSensitivityRatio
                        )
                        {
                            var portfolioValueAfterCancellation =
                                originalPortfolioValueWhenSelling -
                                AccountLastSellOpenOrder.Price * AccountLastSellOpenOrder.Amount +
                                PublicLastPurchasePrice * AccountLastSellOpenOrder.Amount;
                            if (portfolioValueAfterCancellation > TradingStrategy.StopLine)
                                await CancelOrderAsync(AccountLastSellOpenOrder);
                        }
                    }
                }
            }

            #endregion
        }


        private bool IsBuyingReserveRequirementMatched(decimal buyingAmountInPrinciple,
            decimal buyingPriceInPrinciple)
        {
            return buyingAmountInPrinciple <=
                   GetMaximumBuyableAmountBasedOnReserveRatio(buyingPriceInPrinciple);
        }

        private decimal GetMaximumBuyableAmountBasedOnReserveRatio(decimal buyingPriceInPrinciple)
        {
            var maxAmount = buyingPriceInPrinciple > 0
                ? (GetCurrentPortfolioEstimatedTargetValue(buyingPriceInPrinciple) *
                   (1 - TradingStrategy.MinimumReservePercentageAfterInitInTargetCurrency) /
                   buyingPriceInPrinciple *
                   (1 - BuyingFeeInPercentage) - BuyingFeeInAmount)
                : 0;
            if (maxAmount > TargetCurrencyBalance.Available)
                maxAmount = TargetCurrencyBalance.Available;
            return Math.Truncate(maxAmount * 100000000) / 100000000;
        }

        private decimal GetMaximumSellableAmountBasedOnReserveRatio(decimal sellingPriceInPrinciple)

        {
            var maxAmount = GetCurrentPortfolioEstimatedExchangeValue(sellingPriceInPrinciple) *
                            (1 - TradingStrategy.MinimumReservePercentageAfterInitInExchangeCurrency) *
                            (1 - SellingFeeInPercentage) - SellingFeeInAmount;
            if (maxAmount > ExchangeCurrencyBalance.Available)
                maxAmount = ExchangeCurrencyBalance.Available;
            return maxAmount;
        }

        private decimal GetCurrentPortfolioEstimatedExchangeValue(decimal exchangePrice)
        {
//            if (exchangePrice <= 0) throw new InvalidOperationException();

//            return ExchangeCurrencyBalance.Available + (AccountOpenOrders
//                       ?.Where(item => item.Type == OrderType.Sell)
//                       .Sum(item => item.Amount)).GetValueOrDefault() +
//                   TargetCurrencyBalance.Available / exchangePrice + (AccountOpenOrders
//                       ?.Where(item => item.Type == OrderType.Buy)
//                       .Sum(item => item.Amount * item.Price / exchangePrice))
//                   .GetValueOrDefault();
            return exchangePrice > 0 ? ExchangeCurrencyBalance.Total + TargetCurrencyBalance.Total / exchangePrice : 0;
        }

        private decimal GetCurrentPortfolioEstimatedTargetValue(decimal exchangePrice)
        {
//            if (exchangePrice <= 0) throw new InvalidCastException();
//
            return ExchangeCurrencyBalance.Total * exchangePrice + TargetCurrencyBalance.Total;
        }

        private bool IsSellingReserveRequirementMatched(decimal sellingAmountInPrinciple,
            decimal sellingPriceInPrinciple)
        {
            return sellingAmountInPrinciple <=
                   GetMaximumSellableAmountBasedOnReserveRatio(sellingPriceInPrinciple);
        }


        private decimal GetPortfolioValueInTargetCurrency(decimal exchangeCurrencyValue, decimal targetCurrencyValue,
            decimal exchangePrice, int decimalPlaces = 2)
        {
            var realValue = exchangeCurrencyValue * exchangePrice + targetCurrencyValue;
            var baseTens = 1;
            for (var i = 1; i <= decimalPlaces; i++)
            {
                baseTens = baseTens * 10;
            }

            return Math.Floor(Math.Truncate(realValue * baseTens) / baseTens);
        }

        private decimal GetPortfolioValueInExchangeCurrency(decimal exchangeCurrencyValue, decimal targetCurrencyValue,
            decimal exchangePrice, int decimalPlaces = 8)
        {
            var realValue = exchangeCurrencyValue + targetCurrencyValue / exchangePrice;
            var baseTens = 1;
            for (var i = 1; i <= decimalPlaces; i++)
            {
                baseTens = baseTens * 10;
            }

            return Math.Truncate((exchangeCurrencyValue + targetCurrencyValue / exchangePrice) * baseTens) / baseTens;
        }

        public async Task<bool> CancelOrderAsync(IOrder order)
        {
            if (order?.OrderId?.IsNullOrEmpty() == true) return false;

//            var executable = order.Type == OrderType.Buy &&
//                             (!LastTimeBuyOrderCancellation.IsValidSqlDateTime() ||
//                              LastTimeBuyOrderCancellation.IsValidSqlDateTime() &&
//                              LastTimeBuyOrderCancellation.AddMinutes(TradingStrategy
//                                                                          .MinutesOfAccountHistoryOrderForPurchaseDecision +
//                                                                      (double) (1 + AverageTradingChangeRatio * 10)) <=
//                              DateTime.Now) ||
//                             order.Type == OrderType.Sell &&
//                             (!LastTimeSellOrderCancellation.IsValidSqlDateTime() ||
//                              LastTimeSellOrderCancellation.IsValidSqlDateTime() &&
//                              LastTimeSellOrderCancellation.AddMinutes(TradingStrategy
//                                                                           .MinutesOfAccountHistoryOrderForSellDecision +
//                                                                       (double) (1 + AverageTradingChangeRatio * 10)) <=
//                              DateTime.Now);
//            if (!executable)
//            {
//                Console.WriteLine(
//                    $"Cancellation requirements not met yet. Skip cancellation execuation for {order.OrderId}.");
//                return false;
//            }

            var result = await Api.CancelOrderAsync(order);

            if (!result) return BooleanUtils.GetBooleanValueFromObject(result);

            if (order.Type == OrderType.Buy)
                LastTimeBuyOrderCancellation = DateTime.Now;
            if (order.Type == OrderType.Sell)
                LastTimeSellOrderCancellation = DateTime.Now
                    ;
            ;
            Console.BackgroundColor = ConsoleColor.DarkRed;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(
                $" [CANCEL] [{(order.Type == OrderType.Buy ? "BUY" : "SELL")}] Order {order.OrderId} Cancelled: {(order.Type == OrderType.Buy ? order.Amount * order.Price : order.Amount)} {(order.Type == OrderType.Buy ? OperatingTargetCurrency : OperatingExchangeCurrency)} at {order.Price} per {OperatingExchangeCurrency}");
            Console.ResetColor();

            var currentValue = ExchangeCurrencyBalance?.Total * PublicLastSellPrice + TargetCurrencyBalance?.Total;

            var targetValue = order.Type == OrderType.Buy
                ? (ExchangeCurrencyBalance?.Total + order.Amount) * order.Price + TargetCurrencyBalance?.Total -
                  (order.Amount * order.Price)
                : (ExchangeCurrencyBalance?.Total - order.Amount) * order.Price + TargetCurrencyBalance?.Total +
                  (order.Amount * order.Price);


            SendWebhookMessage(
                $" :cry: *[CANCELCATION]* [{(order.Type == OrderType.Buy ? "BUY" : "SELL")}] Order {order.OrderId} - {order.Timestamp}\n" +
                $" *Executed:* {(order.Type == OrderType.Buy ? order.Amount * order.Price : order.Amount)} {(order.Type == OrderType.Buy ? OperatingTargetCurrency : OperatingExchangeCurrency)} \n" +
                $" *Price:* {order.Price} {OperatingTargetCurrency}_\n" +
                $" *Current Price:* {(order.Type == OrderType.Buy ? PublicLastPurchasePrice : PublicLastSellPrice)} {OperatingTargetCurrency}_\n" +
                $" *Current Value in {OperatingTargetCurrency}:* {currentValue} {OperatingTargetCurrency} \n" +
                $" *Target Value in {OperatingTargetCurrency}:* {targetValue} {OperatingTargetCurrency} \n" +
                $" *Current Value in {OperatingExchangeCurrency}:* {(PublicLastSellPrice > 0 ? currentValue / PublicLastSellPrice : 0)} {OperatingExchangeCurrency} \n" +
                $" *Target Value in {OperatingExchangeCurrency}:* {targetValue / order.Price} {OperatingExchangeCurrency}"
            );
            Thread.Sleep(1000);
            ApiRequestcrruedAllowance++;

            return BooleanUtils.GetBooleanValueFromObject(result);
        }


        public async Task<List<IOrder>> GetOpenOrdersAsync()
        {
            AccountOpenOrders = await Api.GetAccountOpenOrdersAsync(OperatingExchangeCurrency, OperatingTargetCurrency);
            return AccountOpenOrders;
        }

        private void SendWebhookMessage(string message)
        {
            Api.SendWebhookMessage(message,
                $"MLEADER's {Api.ExchangeName} Trading Bot - {OperatingExchangeCurrency}/{OperatingTargetCurrency} ");
        }


        #region Calculation of Key Factors

        #region Staging Calculations

        private decimal PublicUpLevelSell1 =>
            PublicWeightedAverageSellPrice > 0
                ? Math.Abs(PublicWeightedAverageBestSellPrice - PublicWeightedAverageSellPrice) /
                  PublicWeightedAverageSellPrice
                : 0;

        private decimal PublicUpLevelSell2 =>
            PublicWeightedAverageSellPrice > 0
                ? Math.Abs(PublicWeightedAverageLowSellPrice - PublicWeightedAverageSellPrice) /
                  PublicWeightedAverageSellPrice
                : 0;

        private decimal PublicUpLevelPurchase1 =>
            PublicWeightedAveragePurchasePrice > 0
                ? Math.Abs(PublicWeightedAverageBestPurchasePrice - PublicWeightedAveragePurchasePrice) /
                  PublicWeightedAveragePurchasePrice
                : 0;

        private decimal PublicUpLevelPurchase2 =>
            PublicWeightedAveragePurchasePrice > 0
                ? Math.Abs(PublicWeightedAverageLowPurchasePrice - PublicWeightedAveragePurchasePrice) /
                  PublicWeightedAveragePurchasePrice
                : 0;

        #endregion

        /// <summary>
        /// Is market price going up: buying amount > selling amount
        /// </summary>
        private bool IsBullMarket => LatestPublicPurchaseHistory?.Sum(item => item.Amount) >
                                     LatestPublicSaleHistory?.Sum(item => item.Amount) &&
                                     PublicWeightedAveragePurchasePrice > PublicWeightedAverageSellPrice &&
                                     PublicWeightedAverageLowPurchasePrice > PublicWeightedAverageBestSellPrice &&
                                     PublicWeightedAverageLowPurchasePrice > PublicWeightedAverageLowSellPrice &&
                                     (PublicLastPurchasePrice > PublicWeightedAveragePurchasePrice ||
                                      PublicLastSellPrice > PublicWeightedAverageSellPrice);

        private bool IsBullMarketContinuable => IsBullMarket &&
                                                CurrentOrderbook?.Bids?.Where(item =>
                                                        item[0] >= PublicWeightedAverageLowSellPrice *
                                                        (1 - AverageTradingChangeRatio))
                                                    .Sum(item => item[0] * item[1]) > CurrentOrderbook?.Asks
                                                    ?.Where(item =>
                                                        item[0] <= PublicWeightedAverageLowPurchasePrice *
                                                        (1 + AverageTradingChangeRatio))
                                                    .Sum(item => item[0] * item[1]) &&
                                                (PublicLastPurchasePrice == 0 || PublicLastPurchasePrice >=
                                                 PublicWeightedAverageLowPurchasePrice) &&
                                                (PublicLastSellPrice == 0 || PublicLastSellPrice >=
                                                 PublicWeightedAverageBestSellPrice);

        private bool IsBearMarketContinuable => !IsBullMarket &&
                                                CurrentOrderbook?.Bids?.Where(item =>
                                                        item[0] >= PublicWeightedAverageBestSellPrice *
                                                        (1 - AverageTradingChangeRatio))
                                                    .Sum(item => item[0] * item[1]) < CurrentOrderbook?.Asks
                                                    ?.Where(item =>
                                                        item[0] <= PublicWeightedAverageBestPurchasePrice *
                                                        (1 + AverageTradingChangeRatio))
                                                    .Sum(item => item[0] * item[1]) &&
                                                (PublicLastPurchasePrice == 0 || PublicLastPurchasePrice <=
                                                 PublicWeightedAverageBestPurchasePrice) &&
                                                (PublicLastSellPrice == 0 || PublicLastSellPrice <=
                                                 PublicWeightedAverageLowSellPrice);


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
                    : PublicLastSellPrice;
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
                var bestFirstThirdPrices = LatestPublicSaleHistory?.OrderBy(item => item.Price)
                    .Take(LatestPublicSaleHistory.Count / 3);
                var totalAmount = (bestFirstThirdPrices?.Sum(item => item.Amount)).GetValueOrDefault();
                return totalAmount > 0
                    ? bestFirstThirdPrices.Sum(item => item.Amount * item.Price) / totalAmount
                    : PublicLastSellPrice;
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
                var bestLastThirdPrices = LatestPublicSaleHistory?.OrderByDescending(item => item.Price)
                    .Take(LatestPublicSaleHistory.Count / 3);
                var totalAmount = (bestLastThirdPrices?.Sum(item => item.Amount)).GetValueOrDefault();
                return totalAmount > 0
                    ? bestLastThirdPrices.Sum(item => item.Amount * item.Price) / totalAmount
                    : PublicLastSellPrice;
            }
        }

        /// <summary>
        /// Find the last public sale price
        /// </summary>
        /// <returns></returns>
        public decimal PublicLastSellPrice => (LatestPublicSaleHistory?.OrderByDescending(item => item.Timestamp)
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
                    : PublicLastPurchasePrice;
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
                var bestFirstThirdPrices = LatestPublicPurchaseHistory?.OrderBy(item => item.Price)
                    .Take((int) Math.Ceiling((decimal) LatestPublicPurchaseHistory.Count / 3));
                var totalAmount = (bestFirstThirdPrices?.Sum(item => item.Amount)).GetValueOrDefault();
                return totalAmount > 0
                    ? bestFirstThirdPrices.Sum(item => item.Amount * item.Price) / totalAmount
                    : PublicLastPurchasePrice;
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
                var bestLastThirdPrices = LatestPublicPurchaseHistory?.OrderByDescending(item => item.Price)
                    .Take((int) Math.Ceiling((decimal) LatestPublicPurchaseHistory.Count / 3));
                var totalAmount = (bestLastThirdPrices?.Sum(item => item.Amount)).GetValueOrDefault();
                return totalAmount > 0
                    ? bestLastThirdPrices.Sum(item => item.Amount * item.Price) / totalAmount
                    : PublicLastPurchasePrice;
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
                    : PublicLastPurchasePrice;
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
                    : PublicLastSellPrice;
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
        private decimal ProposedSellingPrice
        {
            get
            {
                var orderbookPriorityAsks = CurrentOrderbook?.Asks?.Where(i => i[0] <= ReasonableAccountLastSellPrice);
                var orderbookValuedPrice = (CurrentOrderbook?.Asks?.Min(i => i[0])).GetValueOrDefault();
                if (orderbookPriorityAsks?.Count() > 0)
                {
                    orderbookValuedPrice = orderbookPriorityAsks.Sum(i => i[1] * i[0]) /
                                           orderbookPriorityAsks.Sum(i => i[1]);
                }

                if (orderbookValuedPrice <= 0) orderbookValuedPrice = ReasonableAccountLastSellPrice;

                var proposedSellingPrice = new[]
                {
                    new[]
                    {
                        PublicWeightedAverageSellPrice,
                        PublicLastSellPrice,
                        PublicWeightedAverageBestSellPrice,
                        orderbookValuedPrice
                    }.Average(),
                    new[]
                    {
                        ReasonableAccountLastPurchasePrice,
                        ReasonableAccountLastSellPrice,
                        orderbookValuedPrice
                    }.Max(),
                    IsBullMarket
                        ? Math.Max(ReasonableAccountWeightedAverageSellPrice, PublicWeightedAverageSellPrice)
                        : new[] {ReasonableAccountWeightedAverageSellPrice, PublicWeightedAverageSellPrice}.Average(),
                    IsBullMarket ? PublicWeightedAverageBestSellPrice : PublicWeightedAverageLowSellPrice,
                    orderbookValuedPrice
                }.Average();

                orderbookPriorityAsks = CurrentOrderbook?.Asks?.Where(i => i[0] <= proposedSellingPrice);

                if (!(orderbookPriorityAsks?.Count() > 0) || ExchangeCurrencyBalance == null ||
                    TargetCurrencyBalance == null) return proposedSellingPrice;
                var currentPortfolioValue = GetCurrentPortfolioEstimatedTargetValue(PublicLastSellPrice);

                foreach (var order in orderbookPriorityAsks)
                {
                    var portfolioValueBasedOnOrder = GetCurrentPortfolioEstimatedTargetValue((decimal) Math.Ceiling(
                        order[0] * (1 - SellingFeeInPercentage) -
                        SellingFeeInAmount));

                    //i.e. still make a profit
                    if (portfolioValueBasedOnOrder > currentPortfolioValue) return order[0];
                }

                proposedSellingPrice = proposedSellingPrice * (1 + (IsBullMarket
                                                                   ? (IsBullMarketContinuable
                                                                       ? Math.Abs(AverageTradingChangeRatio +
                                                                                  TradingStrategy
                                                                                      .MarketChangeSensitivityRatio)
                                                                       : Math.Abs(AverageTradingChangeRatio -
                                                                                  TradingStrategy
                                                                                      .MarketChangeSensitivityRatio)
                                                                   )
                                                                   : Math.Abs(AverageTradingChangeRatio -
                                                                              TradingStrategy
                                                                                  .MarketChangeSensitivityRatio) *
                                                                     (IsBearMarketContinuable ? 0 : 1)
                                                               ));

                return proposedSellingPrice;
            }
        }

        /// <summary>
        /// [ProposedPurchasePrice] = MIN(AVG([PublicWeightedAveragePurchasePrice],[PublicLastPurchasePrice], [PublicWeightedAverageBestPurchasePrice], [AccountWeightedAverageSellPrice]), [PublicLastPurchasePrice])
        /// </summary>
        /// <returns></returns>
        private decimal ProposedPurchasePrice
        {
            get
            {
                var orderbookPriorityBids =
                    CurrentOrderbook?.Bids?.Where(i => i[0] >= ReasonableAccountLastPurchasePrice);
                var orderbookValuatedPrice = (CurrentOrderbook?.Bids?.Max(i => i[0])).GetValueOrDefault();
                if (orderbookPriorityBids?.Count() > 0)
                {
                    orderbookValuatedPrice = orderbookPriorityBids.Sum(i => i[1] * i[0]) /
                                             orderbookPriorityBids.Sum(i => i[1]);
                }

                if (orderbookValuatedPrice <= 0) orderbookValuatedPrice = ReasonableAccountLastPurchasePrice;

                var proposedPurchasePrice = new[]
                {
                    new[]
                    {
                        PublicWeightedAveragePurchasePrice,
                        PublicLastPurchasePrice,
                        PublicWeightedAverageBestPurchasePrice,
                        orderbookValuatedPrice
                    }.Average(),
                    IsBullMarket
                        ? Math.Max(ReasonableAccountWeightedAveragePurchasePrice, PublicWeightedAveragePurchasePrice)
                        : new[] {ReasonableAccountWeightedAveragePurchasePrice, PublicWeightedAveragePurchasePrice}
                            .Average(),
                    IsBullMarket ? PublicWeightedAverageBestPurchasePrice : PublicWeightedAverageLowPurchasePrice,
                    orderbookValuatedPrice,
//                    AccountWeightedAveragePurchasePrice > 0
//                        ? AccountWeightedAveragePurchasePrice
//                        : ReasonableAccountWeightedAveragePurchasePrice
//                    (PublicLastPurchasePrice + ReasonableAccountLastPurchasePrice + ReasonableAccountLastSellPrice +
//                     PublicLastSellPrice) / 4,
//                    (ReasonableAccountLastPurchasePrice + orderbookValuatedPrice) / 2
                }.Average();

                orderbookPriorityBids = CurrentOrderbook?.Bids?.Where(i => i[0] >= proposedPurchasePrice);
                var exchangeCurrencyBalance =
                    AccountBalance?.CurrencyBalances?.Where(item => item.Key == OperatingExchangeCurrency)
                        ?.Select(item => item.Value)?.FirstOrDefault();
                var targetCurrencyBalance =
                    AccountBalance?.CurrencyBalances?.Where(item => item.Key == OperatingTargetCurrency)
                        ?.Select(item => item.Value)?.FirstOrDefault();

                if (!(orderbookPriorityBids?.Count() > 0) || exchangeCurrencyBalance == null ||
                    targetCurrencyBalance == null) return proposedPurchasePrice;

                var currentPortfolioValue =
                    exchangeCurrencyBalance.Total * PublicLastPurchasePrice + targetCurrencyBalance.Total;

                foreach (var order in orderbookPriorityBids)
                {
                    var portfolioValueBasedOnOrder =
                        exchangeCurrencyBalance.Total *
                        Math.Floor(order[0] * (1 - BuyingFeeInPercentage) - BuyingFeeInAmount)
                        + targetCurrencyBalance.Total;
                    //i.e. still make a profit
                    if (portfolioValueBasedOnOrder > currentPortfolioValue)
                    {
                        proposedPurchasePrice = order[0];
                        break;
                    }

                    ;
                }

                proposedPurchasePrice = proposedPurchasePrice * (1 + (IsBullMarket
                                                                     ? (IsBullMarketContinuable
                                                                         ? Math.Abs(AverageTradingChangeRatio -
                                                                                    TradingStrategy
                                                                                        .MarketChangeSensitivityRatio)
                                                                         : 0)
                                                                     : IsBearMarketContinuable
                                                                         ? -1 * (AverageTradingChangeRatio +
                                                                                 TradingStrategy
                                                                                     .MarketChangeSensitivityRatio)
                                                                         : -1 * Math.Abs(
                                                                               AverageTradingChangeRatio -
                                                                               TradingStrategy
                                                                                   .MarketChangeSensitivityRatio)
                                                                 ));
                return proposedPurchasePrice;
            }
        }

        private decimal ReasonableAccountLastPurchasePrice =>
            Math.Abs(AccountLastPurchasePrice - PublicLastPurchasePrice) /
            (PublicLastPurchasePrice > 0
                ? Math.Min(PublicLastPurchasePrice,
                    AccountLastPurchasePrice > 0 ? AccountLastPurchasePrice : PublicLastPurchasePrice)
                : 1) >
            TradingStrategy.MarketChangeSensitivityRatio
                ? PublicLastPurchasePrice
                : AccountLastPurchasePrice;

        private decimal ReasonableAccountLastSellPrice => Math.Min(PublicLastSellPrice,
                                                              AccountLastSellPrice > 0
                                                                  ? AccountLastSellPrice
                                                                  : PublicLastSellPrice) > 0
            ? (
                Math.Abs(AccountLastSellPrice - PublicLastSellPrice) / (PublicLastSellPrice > 0
                    ? Math.Min(PublicLastSellPrice,
                        AccountLastSellPrice > 0 ? AccountLastSellPrice : PublicLastSellPrice)
                    : 1) >
                TradingStrategy.MarketChangeSensitivityRatio
                    ? PublicLastSellPrice
                    : AccountLastSellPrice)
            : PublicLastSellPrice;

        private decimal ReasonableAccountWeightedAverageSellPrice =>
            Math.Min(AccountWeightedAverageSellPrice > 0 ? AccountWeightedAverageSellPrice : PublicLastSellPrice,
                PublicLastSellPrice) > 0
                ? (
                    Math.Abs(AccountWeightedAverageSellPrice - PublicLastSellPrice) /
                    (PublicLastSellPrice > 0
                        ? Math.Min(
                            AccountWeightedAverageSellPrice > 0 ? AccountWeightedAverageSellPrice : PublicLastSellPrice,
                            PublicLastSellPrice)
                        : 1) >
                    TradingStrategy.MarketChangeSensitivityRatio
                        ? PublicLastSellPrice
                        : AccountWeightedAverageSellPrice)
                : PublicLastSellPrice;

        private decimal ReasonableAccountWeightedAveragePurchasePrice => Math.Min(PublicLastPurchasePrice,
                                                                             AccountWeightedAveragePurchasePrice > 0
                                                                                 ? AccountWeightedAveragePurchasePrice
                                                                                 : PublicLastPurchasePrice) > 0
            ? (
                Math.Abs(AccountWeightedAveragePurchasePrice - PublicLastPurchasePrice) /
                (PublicLastPurchasePrice > 0
                    ? Math.Min(PublicLastPurchasePrice,
                        AccountWeightedAveragePurchasePrice > 0
                            ? AccountWeightedAveragePurchasePrice
                            : PublicLastPurchasePrice)
                    : 1) >
                TradingStrategy.MarketChangeSensitivityRatio
                    ? PublicLastPurchasePrice
                    : AccountWeightedAveragePurchasePrice)
            : PublicLastPurchasePrice;

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