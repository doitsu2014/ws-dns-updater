using System.Net.Http.Json;

namespace Ws.DnsUpdater.Runner;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TimeSpan _delay;
    private readonly string _cloudflareHostname;
    private readonly string _cloudflareApiKey;
    private readonly string _cloudflareZoneId;
    private const string HomelabName = "homelab";


    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _delay = configuration.GetValue<TimeSpan>("Configuration:Delay");
        _cloudflareHostname = configuration.GetValue<string>("Configuration:Cloudflare:Hostname") ??
                              throw new ArgumentException("Cloudflare hostname is not configured");
        _cloudflareApiKey = configuration.GetValue<string>("Configuration:Cloudflare:ApiKey") ?? string.Empty;
        _cloudflareZoneId = configuration.GetValue<string>("Configuration:Cloudflare:ZoneId") ?? string.Empty;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                var ipAddress = await GetCurrentIpAddressAsync(stoppingToken);
                var dnsRecords = await GetListRecordsAsync(stoppingToken);
                if (dnsRecords.Result.Any())
                {
                    var homelabRecord = dnsRecords.Result.FirstOrDefault(e => e.Name.StartsWith(HomelabName));
                    if (homelabRecord is not null)
                    {
                        _logger.LogInformation(
                            "Start updating homelab record {Id} {Name}, current ip {Content}, and target ip {IpAddress}",
                            homelabRecord.Id,
                            homelabRecord.Name,
                            homelabRecord.Content,
                            ipAddress);
                        await UpdateDnsRecordAsync(homelabRecord, ipAddress, stoppingToken);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Start creating homelab record {Name} {IpAddress}",
                            HomelabName,
                            ipAddress);
                        await CreateHomelabDnsRecordAsync(ipAddress, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while executing the worker");
            }
            finally
            {
                await Task.Delay(_delay, stoppingToken);
            }
        }
    }

    private async Task<string> GetCurrentIpAddressAsync(CancellationToken ct)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetStringAsync("https://api.ipify.org", ct);
        return response;
    }

    private async Task UpdateDnsRecordAsync(ListItemCloudflareRecordModel updateItem, string ipAddress,
        CancellationToken ct)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_cloudflareApiKey}");

        var request = new BaseCloudflareRecordModel()
        {
            Content = ipAddress,
            Name = updateItem.Name,
            Proxied = updateItem.Proxied,
            Ttl = updateItem.Ttl,
            Type = updateItem.Type,
            Comment = updateItem.Comment
        };

        var res = await httpClient.PutAsJsonAsync<BaseCloudflareRecordModel>(
            $"{this._cloudflareHostname}/zones/{this._cloudflareZoneId}/dns_records/{updateItem.Id}",
            request,
            ct);

        if (res.IsSuccessStatusCode)
        {
            _logger.LogInformation("DNS record updated successfully ({IpAddress})", ipAddress);
        }
        else
        {
            _logger.LogError("Failed to update DNS record ({StatusCode})", res.StatusCode);
        }
    }

    private async Task CreateHomelabDnsRecordAsync(string ipAddress,
        CancellationToken ct)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_cloudflareApiKey}");

        var request = new BaseCloudflareRecordModel()
        {
            Content = ipAddress,
            Name = HomelabName,
            Proxied = false,
            Ttl = null,
            Type = "A",
            Comment = "Homelab DNS Record"
        };

        var res = await httpClient.PostAsJsonAsync(
            $"{this._cloudflareHostname}/zones/{this._cloudflareZoneId}/dns_records",
            request,
            ct);

        if (res.IsSuccessStatusCode)
        {
            _logger.LogInformation("DNS record created successfully ({IpAddress})", ipAddress);
        }
        else
        {
            _logger.LogError("Failed to create DNS record ({StatusCode})", res.StatusCode);
        }
    }

    private async Task<ListCloudflareRecordResponse> GetListRecordsAsync(CancellationToken ct)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_cloudflareApiKey}");
        var res = await httpClient.GetFromJsonAsync<ListCloudflareRecordResponse>(
            $"{this._cloudflareHostname}/zones/{this._cloudflareZoneId}/dns_records", ct);
        return res ?? new ListCloudflareRecordResponse()
        {
            Result = []
        };
    }
}

public class BaseCloudflareRecordModel
{
    public string Comment { get; set; }

    // There is target ip
    public string Content { get; set; }

    // There is subdomain
    public string Name { get; set; }

    // Proxied is flag to support https by Cloudflare
    public bool Proxied { get; set; }

    // null is default
    public int? Ttl { get; set; }

    // Type: A, AAA, CNAME ...
    public string Type { get; set; }
}

public class ListItemCloudflareRecordModel : BaseCloudflareRecordModel
{
    public string Id { get; set; }
}

public class ListCloudflareRecordResponse
{
    public ListItemCloudflareRecordModel[] Result { get; set; }
}