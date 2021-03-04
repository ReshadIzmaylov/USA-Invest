using AutoMapper;
using Microsoft.Extensions.Options;
using stockapi.Entities;
using stockapi.Helpers;
using stockapi.Models.Subscription;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace stockapi.Services
{
    public partial class StockService : IStockPortfolioService
    {
        private readonly DataContext _context;
        private readonly IMapper _mapper;
        private readonly AppSettings _appSettings;

        public StockService(DataContext context, IMapper mapper, IOptions<AppSettings> appSettings)
        {
            _context = context;
            _mapper = mapper;
            _appSettings = appSettings.Value;
        }

        public async Task<IList<PortfolioStockResponse>> GetInvestmentPortfolio(int accountId)
        {
            var account = GetAccount(accountId);

            var portfolioResponse = new List<PortfolioStockResponse>();
            foreach (var stock in account.InvestmentPortfolio)
            {
                portfolioResponse.Add(_mapper.Map<PortfolioStockResponse>(stock));

                string result = await SendWebRequest($"https://finnhub.io/api/v1/quote?symbol={stock.Ticker}&token=bvdjkqv48v6p35e0g2sg");
                JsonElement root = GetJsElemFromString(result);

                portfolioResponse.Last().Price = root.GetProperty("c").GetDouble();
            }

            return portfolioResponse;
        }

        public void AddStockToPortfolio(int accountId, Stock stock)
        {
            var account = GetAccount(accountId);

            if (account.InvestmentPortfolio.Any(x => x.Ticker == stock.Ticker))
                throw new AppException($"Эта акция уже есть в портфеле");

            stock.Amount = 1;
            stock.Ticker = stock.Ticker.ToUpper();
            account.InvestmentPortfolio.Add(stock);

            _context.Accounts.Update(account);
            _context.SaveChanges();
        }

        public void EditStocksInPortfolio(int accountId, Stock stock)
        {
            var account = GetAccount(accountId);

            if (account.InvestmentPortfolio.Any(x => x.Ticker == stock.Ticker))
            {
                account.InvestmentPortfolio.Find(x => x.Ticker == stock.Ticker).Amount = stock.Amount;
            }
            else
            {
                account.InvestmentPortfolio.Add(stock);
            }

            _context.Accounts.Update(account);
            _context.SaveChanges();
        }

        public void DeleteStockFromPortfolio(int accountId, string ticker)
        {
            var account = GetAccount(accountId);

            var deletedStock = account.InvestmentPortfolio.Find(x => x.Ticker == ticker);

            if (deletedStock == null)
                throw new AppException($"Акции {ticker} не найдены");

            account.InvestmentPortfolio.Remove(deletedStock);

            _context.Accounts.Update(account);
            _context.SaveChanges();
        }

        public void ClearInvestmentPortfolio(int accountId)
        {
            var account = GetAccount(accountId);

            account.InvestmentPortfolio.Clear();

            _context.Accounts.Update(account);
            _context.SaveChanges();
        }

        // вспомогательные методы

        private Account GetAccount(int id)
        {
            var account = _context.Accounts.Find(id);
            if (account == null) throw new KeyNotFoundException("Аккаунт не найден");
            return account;
        }

        private async Task<string> SendWebRequest(string uri)
        {
            WebRequest request = WebRequest.Create(uri);
            WebResponse response;
            string result = string.Empty;
            try
            {
                response = await request.GetResponseAsync();
                using (Stream stream = response.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        result = reader.ReadToEnd();
                    }
                }
                response.Close();

                return result;
            }
            catch (Exception ex)
            {
                throw new AppException(ex.Message);
            }
        }

        private JsonElement GetJsElemFromString(string text)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(text))
                {
                    JsonElement jsonElement = doc.RootElement.Clone();
                    return jsonElement;
                }
            }
            catch (Exception ex)
            {
                throw new AppException(ex.Message);
            }
        }
    }
}
