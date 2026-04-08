using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TelnetCommanderPro
{
    public class WalletManager
    {
        private const string BackendUrl = "https://telnetcommanderpro.onrender.com";
        private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        // Ping the server to wake it up before making real requests
        public static async Task WakeUpServerAsync()
        {
            try { await _client.GetStringAsync($"{BackendUrl}/"); } catch { }
        }

        public static async Task<decimal> GetBalanceAsync(string hardwareId)
        {
            try
            {
                var res = await _client.GetStringAsync($"{BackendUrl}/wallet/{hardwareId}");
                var data = JsonConvert.DeserializeObject<WalletResponse>(res);
                return data?.BalanceKes ?? 0;
            }
            catch { return 0; }
        }

        public static async Task<TopUpResult> InitiateTopUpAsync(string hardwareId, string phone, decimal amount)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { hardwareId, phone, amount });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var res = await _client.PostAsync($"{BackendUrl}/topup", content);
                var body = await res.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<TopUpResult>(body);
                return data ?? new TopUpResult { Success = false, Message = "No response" };
            }
            catch (Exception ex)
            {
                return new TopUpResult { Success = false, Message = ex.Message };
            }
        }

        public static async Task<GenerateTokenResult> GeneratePaymentTokenAsync(string hardwareId)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { hardwareId });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var res = await _client.PostAsync($"{BackendUrl}/generate-token", content);
                var body = await res.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<GenerateTokenResult>(body);
                return data ?? new GenerateTokenResult { Error = "No response" };
            }
            catch (Exception ex)
            {
                return new GenerateTokenResult { Error = ex.Message };
            }
        }

        public static async Task<VerifyPaymentResult> VerifyPaymentAsync(string token, string hardwareId)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { token, hardwareId });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var res = await _client.PostAsync($"{BackendUrl}/verify-payment", content);
                var body = await res.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<VerifyPaymentResult>(body);
                return data ?? new VerifyPaymentResult { Success = false };
            }
            catch (Exception ex)
            {
                return new VerifyPaymentResult { Success = false, Error = ex.Message };
            }
        }

        public static async Task<DeductResult> DeductAsync(string hardwareId, string routerType)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { hardwareId, routerType });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var res = await _client.PostAsync($"{BackendUrl}/deduct", content);
                var body = await res.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<DeductResult>(body);
                return data ?? new DeductResult { Success = false };
            }
            catch (Exception ex)
            {
                return new DeductResult { Success = false, Error = ex.Message };
            }
        }

        // Cost per router type in KES
        public static int GetOperationCost(string routerType) => routerType == "X6" ? 150 : 100;
    }

    public class WalletResponse
    {
        [JsonProperty("balanceKes")] public decimal BalanceKes { get; set; }
    }

    public class TopUpResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("checkoutRequestId")] public string? CheckoutRequestId { get; set; }
        [JsonProperty("message")] public string? Message { get; set; }
        [JsonProperty("error")] public string? Error { get; set; }
    }

    public class GenerateTokenResult
    {
        [JsonProperty("token")] public string? Token { get; set; }
        [JsonProperty("paybill")] public string? Paybill { get; set; }
        [JsonProperty("instructions")] public string? Instructions { get; set; }
        [JsonProperty("error")] public string? Error { get; set; }
        public bool Success => Token != null && Error == null;
    }

    public class VerifyPaymentResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("newBalance")] public decimal NewBalance { get; set; }
        [JsonProperty("amount")] public decimal Amount { get; set; }
        [JsonProperty("error")] public string? Error { get; set; }
    }

    public class DeductResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("newBalance")] public decimal NewBalance { get; set; }
        [JsonProperty("error")] public string? Error { get; set; }
        [JsonProperty("balance")] public decimal Balance { get; set; }
        [JsonProperty("required")] public int Required { get; set; }
    }
}
