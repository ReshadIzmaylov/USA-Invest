using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using stockapi.Entities;
using stockapi.Helpers;
using stockapi.Models;
using stockapi.Models.Stocks;

namespace stockapi.Services
{
    public partial class StockService : IStockScreenerService
    {
        public async Task<IList<SearchCompanyResponse>> SearchCompany(string company)
        {
            string result = await SendWebRequest($"https://www.gurufocus.com/reader/_api/_search?text={company}");

            var listCompany = new List<SearchCompanyResponse>();

            if (!string.IsNullOrEmpty(result))
            {
                var companyGurufocus = JsonSerializer.Deserialize<List<CompanyGurufocusModel>>(result);

                int idComp = 0;
                foreach (var comp in companyGurufocus)
                {
                    string compType = comp.type;
                    JsonElement compData = comp.data;

                    // нас интересуют только акции, размещенные на биржах NYSE и NASDAQ (на гуруфокусе обозначена как NAS)
                    if (compType != "stock")
                        continue;
                    string exchange = compData.GetProperty("exchange").GetString();

                    if (exchange != "NYSE" && exchange != "NAS")
                        continue;

                    string ticker = compData.GetProperty("symbol").GetString();
                    string compName = compData.GetProperty("company").GetString();

                    listCompany.Add(new SearchCompanyResponse
                    {
                        Id = idComp++,
                        Name = compName,
                        Ticker = ticker
                    });
                }
            }

            return listCompany;
        }

        public IList<Company> GetStocks(string[] categories)
        {
            var listCompany = new List<Company>();
            listCompany = _context.Companies.Where(x => categories.Contains(x.Category))
                .Include(comp => comp.FutureForecast)
                .AsEnumerable()
                .GroupBy(stock => stock.Ticker)
                .Select(y => y.First())
                .ToList();
            return listCompany;
        }

        public async Task<JsonElement> GetPrices(string ticker)
        {
            ticker = ticker.ToUpper();
            string result = await SendWebRequest($"https://www.gurufocus.com/modules/chart/chart_json_morn.php?symbol={ticker}");

            JsonElement jsonElement = GetJsElemFromString(result);

            return jsonElement;
        }

        public async Task<JsonElement> GetAnalystsRecommendations(string ticker)
        {
            ticker = ticker.ToUpper();
            string result = await SendWebRequest($"https://finnhub.io/api/v1/stock/recommendation?symbol={ticker}&token=bup5se748v6sjkjikmf0");

            JsonElement jsonElement = GetJsElemFromString(result);

            return jsonElement;
        }

        public async Task<JsonElement> GetChart(string stockId)
        {
            string result = await SendWebRequest($"https://www.gurufocus.com/reader/_api/chart/{stockId}/valuation");

            JsonElement jsonElement = GetJsElemFromString(result);

            return jsonElement;
        }

        public async Task<Company> GetBasePropOfCompany(string ticker)
        {
            // тикер должен быть написан большими буквами, иначе возможны ошибки в запросах к другим сайтам
            ticker = ticker.ToUpper();
            var company = new Company();
            string result = await SendWebRequest($"https://www.gurufocus.com/reader/_api/stocks/{ticker}/summary");
            JsonElement root = GetJsElemFromString(result);
            var webScraper = new StocksWebScraping();
            try
            {
                company.Ticker = ticker.ToUpper();
                company.Sector = root.GetProperty("sector").GetString();
                company.Name = root.GetProperty("company").GetString();
                company.StockId = root.GetProperty("stockid").GetString();
                company.Price = root.GetProperty("price").GetDouble();
                company.FinancialStrength = root.GetProperty("rank_balancesheet").GetInt32();
                company.ProfitabilityRank = root.GetProperty("rank_profitability").GetInt32();
                company.ValuationRank = webScraper.GetValuationRank(ticker);

                // получим описание и лого компании через StocksWebScraping
                webScraper.GetLogoAndDescription(
                    ticker,
                    out string description,
                    out string logoLink);

                company.Description = description;

                // проверим, нет ли лого компании в нашем облаке Cloudinary
                SearchLogoInCloud(ticker, ref logoLink);

                company.LogoUrl = logoLink;
                return company;
            }
            catch
            {
                return new Company();
            }
        }

        public async Task<JsonElement> GetSmryStockInfo(string ticker)
        {
            string result = await SendWebRequest($"https://www.gurufocus.com/reader/_api/stocks/{ticker}/summary");

            JsonElement jsonElement = GetJsElemFromString(result);

            return jsonElement;
        }

        public async Task<JsonElement> GetCompleteStockInfo (string ticker)
        {
            Company company = await GetBasePropOfCompany(ticker);
            JsonElement jsonElement = await GetSmryStockInfo(ticker);

            string baseCompanyInfo = JsonSerializer.Serialize<Company>(company);
            string smryCompanyInfo = jsonElement.ToString();
            string resultJson = "{\"aboutComp\":" + baseCompanyInfo + "," + "\"summary\":" + smryCompanyInfo + "}";

            return GetJsElemFromString(resultJson);
        }

        public async Task UpdateStockCategory (string category)
        {
            category = category.ToLower();
            var categoryLinks = new List<string>();
            var tickers = new List<string>();
            var webScraper = new StocksWebScraping();

            // в companieslist.json лежат ссылки на finviz с отобранными по фильтрам акциями для основных категорий
            categoryLinks = await GetCategoryInfoFromFile("companieslist.json", category);
            if (categoryLinks.Any())
            {
                tickers = webScraper.GetTickersFromFinviz(categoryLinks);
            }
            else 
            {
                // если ссылок на finviz для запрошенной category нет, пробуем искать сами тикеры в OtherCompanies.json
                tickers = await GetCategoryInfoFromFile("OtherCompanies.json", category);
            }

            if (!tickers.Any()) throw new AppException($"Нет данных для категории {category}");

            var updatedCategory = new List<Company>();
            bool checkConditions = (
                category == "growrecommendations" ||
                category == "biotech" ||
                category == "dividends");
            tickers = tickers.Distinct().ToList();
            foreach(string ticker in tickers)
            {
                Company company = await GetBasePropOfCompany(ticker);
                company.Category = category;
                List<MedpsGurufocusModel> stockPriceList = await GetStockPriceList(company.StockId);
                if (stockPriceList.Any())
                {
                    company.Status = GetStockValuation(company.Price, stockPriceList);
                    if (checkConditions)
                    {
                        bool meetConditions = CheckConditions(company, stockPriceList);
                        if (!meetConditions)
                            continue;
                    }
                }
                updatedCategory.Add(company);
            }

            if (updatedCategory.Any())
            {
                // удаляем все компании этой категории из БД, т.к. постоянно может быть разный набор подходящих компаний
                // а затем добавляем обновленный список
                var oldCategory = _context.Companies.Where(x => x.Category == category);
                _context.Companies.RemoveRange(oldCategory);
                _context.Companies.AddRange(updatedCategory);
                await _context.SaveChangesAsync();
            }
        }

        private async Task<List<string>> GetCategoryInfoFromFile(string fileName, string category)
        {
            var categoryInfoList = new List<string>();

            string dataFromFile = await System.IO.File.ReadAllTextAsync(fileName);
            JsonElement allCategoriesInfo = GetJsElemFromString(dataFromFile);
            try
            {
                // получим из allCategoriesInfo массив информации для запрошенной category;
                // здесь может быть исключение KeyNotFoundException, означающее, что информации для запрошенной category в файле нет
                JsonElement categoryInfo = allCategoriesInfo.GetProperty(category); 

                for (int i = 0; i < categoryInfo.GetArrayLength(); i++)
                {
                    categoryInfoList.Add(categoryInfo[i].GetString());
                }
            }
            catch (KeyNotFoundException)
            {
                // оставляем categoryInfoList пустым
            }

            return categoryInfoList;
        }

        private async Task<List<MedpsGurufocusModel>> GetStockPriceList(string stockId)
        {
            var stockPriceList = new List<MedpsGurufocusModel>();
            string result = await SendWebRequest($"https://www.gurufocus.com/reader/_api/chart/{stockId}/valuation");
            JsonElement root = GetJsElemFromString(result);
            try
            {
                // medps - это массив вида [[String date1, Double price1],...,[String dateN, Double priceN]], 
                // хранящий цены акции в прошлом и прогнозируемые цены в будущем
                // он есть не для всех акций, поэтому тут может возникнуть KeyNotFoundException
                JsonElement medps = root.GetProperty("medps");

                // преобразуем medps в удобную форму
                foreach (JsonElement stockPriceArray in medps.EnumerateArray())
                {
                    var medpsModel = new MedpsGurufocusModel();
                    foreach (JsonElement stockPrice in stockPriceArray.EnumerateArray())
                    {
                        if (stockPrice.ValueKind == JsonValueKind.String)
                        {
                            medpsModel.PriceDate = DateTime.Parse(stockPrice.GetString());
                        }
                        else
                        {
                            medpsModel.PriceValue = stockPrice.GetDouble();
                        }
                    }
                    stockPriceList.Add(medpsModel);
                }
            }
            catch (KeyNotFoundException)
            {
                // вернем пустой список в случае отсутствия medps
                return new List<MedpsGurufocusModel>();
            }

            return stockPriceList;
        }

        private string GetStockValuation(double stockPrice, List<MedpsGurufocusModel> stockPriceList)
        {
            MedpsGurufocusModel nearestPredictedPrice = stockPriceList.First(x => x.PriceDate > DateTime.Today);
            MedpsGurufocusModel lastPrice = stockPriceList.Last(x => x.PriceDate <= DateTime.Today);

            double avgPriceChange = (nearestPredictedPrice.PriceValue - lastPrice.PriceValue) / (nearestPredictedPrice.PriceDate - lastPrice.PriceDate).Days;
            double expectedTodayPrice = lastPrice.PriceValue + avgPriceChange * (DateTime.Today - lastPrice.PriceDate).Days;
            double difference = Math.Abs((expectedTodayPrice - stockPrice) / stockPrice) * 100;

            string stockValuation = string.Empty;
            if (difference < 5)
            {
                stockValuation = "Справедливо оценена";
            }
            else if (difference < 20)
            {
                stockValuation = (stockPrice < expectedTodayPrice) ? "Недооценена" : "Переоценена";
            }
            else
            {
                stockValuation = (stockPrice < expectedTodayPrice) ? "Сильно недооценена" : "Сильно переоценена";
            }
            return stockValuation;
        }

        private bool CheckConditions(Company company, List<MedpsGurufocusModel> stockPriceList)
        {
            var culture = new CultureInfo("ru-RU");

            bool firstCondition = (
                company.Status == "Справедливо оценена" ||
                company.Status == "Недооценена" ||
                company.Status == "Сильно недооценена");
            bool secondCondition = false;

            MedpsGurufocusModel lastPredictedPrice = stockPriceList.Last();
            double yearsDifference = (lastPredictedPrice.PriceDate - DateTime.Today).TotalDays / 365;
            double priceDifference = (lastPredictedPrice.PriceValue - company.Price) / company.Price * 100;

            switch (company.Category)
            {
                case "growrecommendations":
                    if (lastPredictedPrice.PriceValue > company.Price && (priceDifference / yearsDifference) >= 15)
                        secondCondition = true;
                    break;
                case "dividends":
                case "biotech":
                    if (lastPredictedPrice.PriceValue > company.Price)
                        secondCondition = true;
                    break;
                default:
                    break;
            }
            company.FutureForecast = new StockFutureForecast
            {
                PriceDifference = (int)priceDifference,
                Date = lastPredictedPrice.PriceDate.ToString("MMMM yyyy", culture)
            };
            return firstCondition && secondCondition;
        }

        public JsonElement GetIndicatorsVsIndustry(string ticker)
        {
            var webScraper = new StocksWebScraping();
            Dictionary<string, double> dictIndicators = webScraper.GetIndicatorsVsIndustry(ticker.ToUpper());
            string strResult = string.Empty;
            JsonElement result = new JsonElement();
            if (dictIndicators.Any())
            {
                strResult = JsonSerializer.Serialize<Dictionary<string, double>>(dictIndicators);
                result = GetJsElemFromString(strResult);
            }

            return result;
        }

        public JsonElement GetFinancials (string ticker)
        {
            var webScraper = new StocksWebScraping();
            string strResult = webScraper.GetFinancials(ticker.ToUpper());
            JsonElement result = GetJsElemFromString(strResult);

            return result;
        }

        private void SearchLogoInCloud (string ticker, ref string logoLink)
        {
            var cloudinaryAccount = new CloudinaryDotNet.Account(
                    _appSettings.CloudinaryCloudName,
                    _appSettings.CloudinaryApiKey,
                    _appSettings.CloudinaryApiSecret);
            var cloudinary = new CloudinaryDotNet.Cloudinary(cloudinaryAccount);
            var checkLogoInCloud = cloudinary.GetResource(ticker);

            if (checkLogoInCloud.StatusCode == HttpStatusCode.OK)
            {
                logoLink = checkLogoInCloud.SecureUrl;
            }
            else
            {
                // если лого в облаке нет, попытаемся загрузить его туда
                try
                {
                    var uploadParams = new CloudinaryDotNet.Actions.ImageUploadParams()
                    {
                        File = new CloudinaryDotNet.FileDescription(ticker + ".png", logoLink),
                        PublicId = ticker
                    };
                    var uploadResult = cloudinary.Upload(uploadParams);
                    if (uploadResult.StatusCode == HttpStatusCode.OK)
                        logoLink = uploadResult.SecureUrl?.ToString();
                }
                catch
                {
                    // если не получилось, оставляем logoLink как есть, т.е. ссылкой, полученной из StocksWebScraping.GetLogo
                }
            }
        }
    }
}
