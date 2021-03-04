namespace stockapi.Models.Stocks
{
    public class SearchCompanyResponse
    {
        public int Id { get; internal set; }
        public string Ticker { get; set; }
        public string Name { get; set; }
    }
}
