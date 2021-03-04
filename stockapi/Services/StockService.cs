using stockapi.Entities;
using stockapi.Models.Stocks;
using stockapi.Models.Subscription;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace stockapi.Services
{
    public interface IStockPortfolioService
    {
        Task<IList<PortfolioStockResponse>> GetInvestmentPortfolio(int accountId);
        void AddStockToPortfolio(int accountId, Stock stock);
        void EditStocksInPortfolio(int accountId, Stock stock);
        void DeleteStockFromPortfolio(int accountId, string ticker);
        void ClearInvestmentPortfolio(int accountId);
    }

    public interface IStockScreenerService
    {
        Task<IList<SearchCompanyResponse>> SearchCompany(string company);
        IList<Company> GetStocks(string[] categories);
        Task<JsonElement> GetPrices(string ticker);
        Task<JsonElement> GetAnalystsRecommendations(string ticker);
        Task<JsonElement> GetChart(string stockId);
        Task<Company> GetBasePropOfCompany(string ticker);
        Task<JsonElement> GetSmryStockInfo(string ticker);
        Task<JsonElement> GetCompleteStockInfo(string ticker);
        Task UpdateStockCategory(string category);
        JsonElement GetIndicatorsVsIndustry(string ticker);
        JsonElement GetFinancials(string ticker);
    }
}
